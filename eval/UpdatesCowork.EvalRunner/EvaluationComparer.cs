using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace UpdatesCowork.EvalRunner;

/// <summary>
/// 2つの評価結果(基準版と候補版)を同じケースどうしで比べる(9.6節)。
/// 合格率の差だけでなく、ケースごとの改善と悪化の件数から正確 McNemar 検定の p 値を出し、偶然の差かどうかを判断できるようにする。
/// </summary>
internal static class EvaluationComparer
{
    /// <summary>有意とみなす p 値の上限。</summary>
    public const double SignificanceLevel = 0.05;

    /// <summary>前提として一致していなければならない実行メタデータ(9.2節)。</summary>
    private static readonly string[] RequiredSameMetadata =
        ["dataset_sha256", "fixtures_sha256", "judge", "response_source", "task_query_format"];

    internal static async Task<int> CompareAsync(string baselineDirectory, string candidateDirectory, string output,
        CancellationToken cancellationToken)
    {
        var baseline = await ReadRunAsync(baselineDirectory, cancellationToken).ConfigureAwait(false);
        var candidate = await ReadRunAsync(candidateDirectory, cancellationToken).ConfigureAwait(false);
        var comparison = Compare(baseline.Summary, baseline.Metadata, candidate.Summary, candidate.Metadata);
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "comparison.json"),
            JsonSerializer.Serialize(comparison, EvalJsonContext.Default.EvaluationComparison), cancellationToken).ConfigureAwait(false);
        var markdown = ToMarkdown(comparison);
        await File.WriteAllTextAsync(Path.Combine(output, "comparison.md"), markdown, cancellationToken).ConfigureAwait(false);
        Console.WriteLine(markdown);
        return !comparison.Comparable ? 2 : comparison.Regressed ? 1 : 0;
    }

    internal static EvaluationComparison Compare(EvaluationSummary baseline, JsonObject? baselineMetadata,
        EvaluationSummary candidate, JsonObject? candidateMetadata)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);

        var warnings = new List<string>();
        if (baselineMetadata is null || candidateMetadata is null)
        {
            warnings.Add("definition.json の実行メタデータがないため、同じ条件で採点したかを確認できません。");
        }
        else
        {
            foreach (var key in RequiredSameMetadata)
            {
                var left = MetadataText(baselineMetadata, key);
                var right = MetadataText(candidateMetadata, key);
                if (!string.Equals(left, right, StringComparison.Ordinal))
                {
                    warnings.Add($"{key} が異なります (基準版: {left ?? "なし"}、候補版: {right ?? "なし"})。");
                }
            }
        }

        foreach (var (label, summary) in new[] { ("基準版", baseline), ("候補版", candidate) })
        {
            if (summary.RunStatus != "completed" || summary.ReceivedCases != summary.ExpectedCases)
            {
                warnings.Add($"{label}の実行が完了していません (状態: {summary.RunStatus ?? "なし"}、出力 {summary.ReceivedCases}/{summary.ExpectedCases} 件)。");
            }
        }

        var baselineCases = baseline.Cases.ToDictionary(score => score.Id, StringComparer.Ordinal);
        var candidateCases = candidate.Cases.ToDictionary(score => score.Id, StringComparer.Ordinal);
        if (!baselineCases.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(candidateCases.Keys))
        {
            warnings.Add("評価ケースの ID が一致しません。");
        }

        var candidateMetrics = candidate.Metrics.ToDictionary(metric => metric.Name, StringComparer.Ordinal);
        var metrics = new List<MetricComparison>();
        foreach (var before in baseline.Metrics)
        {
            if (!candidateMetrics.TryGetValue(before.Name, out var after))
            {
                warnings.Add($"候補版に指標 {before.Name} がありません。");
                continue;
            }

            var improved = new List<string>();
            var worsened = new List<string>();
            var paired = 0;
            foreach (var (id, left) in baselineCases.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (!candidateCases.TryGetValue(id, out var right)
                    || left.Checks.GetValueOrDefault(before.Name) is not { } leftPassed
                    || right.Checks.GetValueOrDefault(before.Name) is not { } rightPassed)
                {
                    continue;
                }

                paired++;
                if (!leftPassed && rightPassed) improved.Add(id);
                else if (leftPassed && !rightPassed) worsened.Add(id);
            }

            var pValue = McNemarExactPValue(improved.Count, worsened.Count);
            var verdict = improved.Count + worsened.Count == 0 ? ComparisonVerdicts.Unchanged
                : pValue >= SignificanceLevel ? ComparisonVerdicts.NotSignificant
                : improved.Count > worsened.Count ? ComparisonVerdicts.Improved : ComparisonVerdicts.Regressed;
            var (beforeLow, beforeHigh) = WilsonInterval(before.Passed, before.Total);
            var (afterLow, afterHigh) = WilsonInterval(after.Passed, after.Total);
            metrics.Add(new MetricComparison(before.Name,
                new RateEstimate(before.Passed, before.Total, before.Rate, beforeLow, beforeHigh),
                new RateEstimate(after.Passed, after.Total, after.Rate, afterLow, afterHigh),
                before.Rate is { } beforeRate && after.Rate is { } afterRate ? afterRate - beforeRate : null,
                paired, [.. improved], [.. worsened], pValue, verdict, after.MeetsThreshold));
        }

        return new EvaluationComparison(
            Label(baselineMetadata, baseline), Label(candidateMetadata, candidate),
            warnings.Count == 0, [.. warnings],
            metrics.Any(metric => metric.Verdict == ComparisonVerdicts.Regressed),
            SignificanceLevel, [.. metrics]);
    }

    /// <summary>
    /// 正確 McNemar 検定(両側)。食い違ったケース(改善 b 件と悪化 c 件)だけを使い、
    /// 「改善と悪化は半々の確率で起きる」という帰無仮説の二項分布 B(b + c, 0.5) で p 値を求める。
    /// </summary>
    internal static double McNemarExactPValue(int improved, int worsened)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(improved);
        ArgumentOutOfRangeException.ThrowIfNegative(worsened);

        var n = improved + worsened;
        if (n == 0) return 1;
        var k = Math.Min(improved, worsened);

        // n が大きくてもあふれないよう、二項係数は対数で足し合わせる。
        var logTerm = -n * Math.Log(2);
        var logSum = logTerm;
        for (var i = 0; i < k; i++)
        {
            logTerm += Math.Log(n - i) - Math.Log(i + 1);
            logSum = LogAdd(logSum, logTerm);
        }

        return Math.Min(1, 2 * Math.Exp(logSum));
    }

    /// <summary>合格率の Wilson スコア区間(95%)。件数が少ないときも 0〜1 の範囲に収まる。</summary>
    internal static (double? Low, double? High) WilsonInterval(int passed, int total)
    {
        if (total <= 0) return (null, null);
        const double z = 1.959963984540054;
        var p = (double)passed / total;
        var z2 = z * z;
        var denominator = 1 + z2 / total;
        var center = (p + z2 / (2 * total)) / denominator;
        var half = z * Math.Sqrt(p * (1 - p) / total + z2 / (4.0 * total * total)) / denominator;
        return (passed == 0 ? 0 : Math.Max(0, center - half), passed == total ? 1 : Math.Min(1, center + half));
    }

    internal static string ToMarkdown(EvaluationComparison comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);

        var markdown = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"# 評価の比較: {comparison.Baseline} → {comparison.Candidate}\n\n");
        markdown.AppendLine(comparison.Comparable
            ? "前提: データセット、固定データ、judge、採点入力の形式が一致しています。"
            : "前提: 一致していない条件があります。この比較で版の良し悪しを判断しないでください。");
        foreach (var warning in comparison.Warnings) markdown.AppendLine(CultureInfo.InvariantCulture, $"- {warning}");
        markdown.AppendLine(CultureInfo.InvariantCulture,
            $"\n判定は、同じケースで改善と悪化が食い違った件数を使う正確 McNemar 検定(両側、有意水準 {comparison.SignificanceLevel:0.00})によります。")
            .AppendLine("\n| 指標 | 基準版 | 候補版 | 差 | 改善 / 悪化 | p 値 | 判定 |")
            .AppendLine("|---|---:|---:|---:|---:|---:|---|");
        foreach (var metric in comparison.Metrics)
        {
            var delta = metric.Delta is { } value ? (value * 100).ToString("+0;-0;0", CultureInfo.InvariantCulture) + " pt" : "-";
            markdown.AppendLine(CultureInfo.InvariantCulture,
                $"| {metric.Name} | {Format(metric.Baseline)} | {Format(metric.Candidate)} | {delta} | {metric.Improved.Length} / {metric.Worsened.Length} | {metric.PValue:0.000} | {metric.Verdict} |");
        }

        var changed = comparison.Metrics.Where(metric => metric.Improved.Length + metric.Worsened.Length > 0).ToArray();
        if (changed.Length > 0) markdown.AppendLine();
        foreach (var metric in changed)
        {
            markdown.AppendLine(CultureInfo.InvariantCulture, $"- {metric.Name}: 改善 {Join(metric.Improved)}、悪化 {Join(metric.Worsened)}");
        }

        return markdown.ToString();
    }

    private static string Format(RateEstimate estimate) => estimate.Rate is { } rate
        ? string.Create(CultureInfo.InvariantCulture,
            $"{estimate.Passed}/{estimate.Total} ({Percent(rate)}、95%区間 {Percent(estimate.Low)}〜{Percent(estimate.High)})")
        : "対象なし";

    private static string Percent(double? value) => value is { } rate
        ? (rate * 100).ToString("0", CultureInfo.InvariantCulture) + "%"
        : "-";

    private static string Join(string[] ids) => ids.Length == 0 ? "なし" : string.Join(", ", ids);

    private static double LogAdd(double left, double right)
    {
        var max = Math.Max(left, right);
        return max + Math.Log(Math.Exp(left - max) + Math.Exp(right - max));
    }

    private static string? MetadataText(JsonObject metadata, string key) =>
        metadata.TryGetPropertyValue(key, out var value) && value is JsonValue text && text.TryGetValue<string>(out var result)
            ? result : null;

    private static string Label(JsonObject? metadata, EvaluationSummary summary) =>
        metadata is not null && MetadataText(metadata, "agent_version") is { } version
            ? $"{MetadataText(metadata, "agent_name") ?? "agent"}:{version}"
            : $"{summary.ExpectedCases} 件の評価";

    private static async Task<(EvaluationSummary Summary, JsonObject? Metadata)> ReadRunAsync(string directory,
        CancellationToken cancellationToken)
    {
        var summary = JsonSerializer.Deserialize(
            await File.ReadAllTextAsync(Path.Combine(directory, "summary.json"), cancellationToken).ConfigureAwait(false),
            EvalJsonContext.Default.EvaluationSummary) ?? throw new InvalidDataException($"{directory} の summary.json が空です。");
        var definitionPath = Path.Combine(directory, "definition.json");
        if (!File.Exists(definitionPath)) return (summary, null);
        var definition = JsonNode.Parse(await File.ReadAllTextAsync(definitionPath, cancellationToken).ConfigureAwait(false));
        return (summary, definition?["metadata"] as JsonObject);
    }
}

/// <summary>比較の判定。</summary>
internal static class ComparisonVerdicts
{
    public const string Improved = "改善";
    public const string Regressed = "悪化";
    public const string NotSignificant = "偶然の範囲";
    public const string Unchanged = "変化なし";
}
