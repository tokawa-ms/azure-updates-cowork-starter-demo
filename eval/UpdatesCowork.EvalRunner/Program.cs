using System.Globalization;
using UpdatesCowork.EvalRunner;

try
{
    if (args.Length == 0 || args[0] is not ("prepare" or "attach" or "collect" or "check" or "compare"))
    {
        Console.Error.WriteLine("prepare --dataset <jsonl> --fixtures <dir> --agent <name-eval> --agent-version <version> --judge <model> --out <dir> [--limit N]\nattach --out <dir>\ncollect --out <dir>\ncheck --out <dir> --thresholds <json>\ncompare --baseline <dir> --candidate <dir> [--out <dir>]");
        return 2;
    }

    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var index = 1; index < args.Length; index += 2)
    {
        if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal)
            || !options.TryAdd(args[index][2..], args[index + 1]))
        {
            throw new ArgumentException("オプションは --名前 値 の形式で、一度ずつ指定してください。");
        }
    }

    string Required(string name) => options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value : throw new ArgumentException($"--{name} が必要です。");
    if (args[0] == "prepare")
    {
        var limit = options.TryGetValue("limit", out var text) ? int.Parse(text, CultureInfo.InvariantCulture) : 100;
        await EvaluationDefinition.PrepareAsync(Required("dataset"), Required("fixtures"), Required("agent"),
            Required("agent-version"), Required("judge"), Required("out"), limit, CancellationToken.None).ConfigureAwait(false);
        return 0;
    }

    if (args[0] == "attach")
    {
        await EvaluationDefinition.AttachResponsesAsync(Required("out"), CancellationToken.None).ConfigureAwait(false);
        return 0;
    }

    if (args[0] == "collect")
    {
        await EvaluationReport.CollectAsync(Required("out"), CancellationToken.None).ConfigureAwait(false);
        return 0;
    }

    if (args[0] == "compare")
    {
        // 終了コード: 0 = 有意な悪化なし、1 = 有意に悪化した指標あり、2 = 比較の前提が揃っていない。
        var candidate = Required("candidate");
        return await EvaluationComparer.CompareAsync(Required("baseline"), candidate,
            options.TryGetValue("out", out var compareOut) && !string.IsNullOrWhiteSpace(compareOut) ? compareOut : candidate,
            CancellationToken.None).ConfigureAwait(false);
    }

    return await EvaluationReport.CheckAsync(Required("out"), Required("thresholds"), CancellationToken.None).ConfigureAwait(false);
}
catch (Exception ex) when (ex is ArgumentException or IOException or InvalidOperationException or System.Text.Json.JsonException or FormatException or OverflowException)
{
    Console.Error.WriteLine($"評価の処理に失敗しました: {ex.Message}");
    return 2;
}
