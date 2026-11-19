using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using UpdatesCowork.Shared.Contracts;

namespace UpdatesCowork.EvalRunner;

internal sealed record EvaluationCase(
    string Id, string Category, string Query, string? PageUpdateId,
    string[] ExpectedTools, string? ExpectedArtifact, string[] MustInclude, string Language, string GroundTruth);

internal sealed record EvaluationArtifact(string CaseId, ArtifactInfo Artifact);
internal sealed record CaseScore(string Id, Dictionary<string, bool?> Checks, string[] Errors);
internal sealed record MetricScore(string Name, int Passed, int Total, double? Rate, double Threshold, bool MeetsThreshold, int NotApplicable);
internal sealed record EvaluationSummary(bool Passed, int ExpectedCases, int ReceivedCases, string? ReportUrl,
    string? RunStatus, string? RunError, MetricScore[] Metrics, CaseScore[] Cases);

/// <summary>合格率と、その 95% Wilson 区間。</summary>
internal sealed record RateEstimate(int Passed, int Total, double? Rate, double? Low, double? High);

/// <summary>1つの指標の比較。<see cref="Improved"/> と <see cref="Worsened"/> は、両方の版で判定できたケースだけを数える。</summary>
internal sealed record MetricComparison(string Name, RateEstimate Baseline, RateEstimate Candidate, double? Delta,
    int PairedCases, string[] Improved, string[] Worsened, double PValue, string Verdict, bool CandidateMeetsThreshold);

internal sealed record EvaluationComparison(string Baseline, string Candidate, bool Comparable, string[] Warnings,
    bool Regressed, double SignificanceLevel, MetricComparison[] Metrics);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, WriteIndented = true)]
[JsonSerializable(typeof(EvaluationCase))]
[JsonSerializable(typeof(EvaluationCase[]))]
[JsonSerializable(typeof(JsonObject))]
[JsonSerializable(typeof(JsonArray))]
[JsonSerializable(typeof(EvaluationArtifact[]))]
[JsonSerializable(typeof(EvaluationSummary))]
[JsonSerializable(typeof(EvaluationComparison))]
[JsonSerializable(typeof(Dictionary<string, double>))]
internal sealed partial class EvalJsonContext : JsonSerializerContext;
