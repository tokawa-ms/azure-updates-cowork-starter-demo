using System.Text.Json.Serialization;

namespace UpdatesCowork.Shared.Contracts;

public sealed record TraceSpanEvent(
    string TraceId,
    string SpanId,
    string? ParentSpanId,
    string Name,
    DateTimeOffset StartedAt,
    string Status,
    DateTimeOffset? EndedAt = null,
    double? DurationMs = null,
    string? ToolName = null,
    string? CallId = null,
    string? Model = null,
    long? InputTokens = null,
    long? OutputTokens = null,
    bool? CacheHit = null,
    string Source = "agent") : ChatEvent
{
    public override string EventName => ChatEventNames.TraceSpan;
}

public sealed record TraceStatusEvent(string Status, int DroppedEvents, string Source = "agent") : ChatEvent
{
    public override string EventName => ChatEventNames.TraceStatus;
}

public sealed record AgentTraceEnvelope(string Type, [property: JsonPropertyName("sequence_number")] long SequenceNumber,
    string ResponseId, TraceSpanEvent? Span = null, TraceStatusEvent? Status = null)
{
    public const string EventType = "response.auc.trace";
    public const string CarrierEventType = "response.in_progress";
    public const string MetadataProperty = "auc_trace";
}
