using UpdatesCowork.Shared.Telemetry;

namespace UpdatesCowork.Shared.Contracts;

public static class LearningTraceRules
{
    public const int MaximumSpans = 128;

    public static bool IsValid(TraceSpanEvent span) =>
        span.Source == "agent" && IsHex(span.TraceId, 32) && IsHex(span.SpanId, 16)
        && (span.ParentSpanId is null || IsHex(span.ParentSpanId, 16) && span.ParentSpanId != span.SpanId)
        && IsMeasuredOperation(span.Name)
        && span.StartedAt.Year is >= 2000 and <= 2100
        && span.Status is "running" or "completed" or "failed" or "cancelled"
        && (span.Status == "running"
            ? span.EndedAt is null && span.DurationMs is null
            : span.EndedAt >= span.StartedAt && span.DurationMs is >= 0 and <= 86_400_000
                && Math.Abs((span.EndedAt.Value - span.StartedAt).TotalMilliseconds - span.DurationMs.Value) <= 1)
        && IsOptionalName(span.ToolName) && IsOptionalName(span.CallId) && IsOptionalName(span.Model)
        && (span.InputTokens is null or >= 0) && (span.OutputTokens is null or >= 0);

    public static bool IsMeasuredOperation(string name) => name is
        TelemetryNames.Spans.AgentTurn or TelemetryNames.Spans.ModelCall or TelemetryNames.Spans.ToolExecute
        or TelemetryNames.Spans.McpCall or TelemetryNames.Spans.McpToolExecute
        or TelemetryNames.Spans.AzureUpdatesGet or TelemetryNames.Spans.AzureUpdatesSearch
        or TelemetryNames.Spans.ContentSafetyShieldPrompt or TelemetryNames.Spans.OfficeWordBuild
        or TelemetryNames.Spans.OfficePptxBuild or TelemetryNames.Spans.ArtifactSave;

    public static bool IsOptionalName(string? name) => name is null || name.Length is > 0 and <= 200
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or ':');

    private static bool IsHex(string? value, int length) =>
        value?.Length == length && value.Any(c => c != '0') && value.All(char.IsAsciiHexDigitLower);
}
