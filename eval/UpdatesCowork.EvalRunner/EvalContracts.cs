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

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, WriteIndented = true)]
[JsonSerializable(typeof(EvaluationCase))]
[JsonSerializable(typeof(EvaluationCase[]))]
[JsonSerializable(typeof(JsonObject))]
[JsonSerializable(typeof(JsonArray))]
[JsonSerializable(typeof(EvaluationArtifact[]))]
[JsonSerializable(typeof(EvaluationSummary))]
[JsonSerializable(typeof(Dictionary<string, double>))]
internal sealed partial class EvalJsonContext : JsonSerializerContext;
