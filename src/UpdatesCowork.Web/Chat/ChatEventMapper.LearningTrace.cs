using System.Text.Json;
using UpdatesCowork.Shared;
using UpdatesCowork.Shared.Contracts;

namespace UpdatesCowork.Web.Chat;

internal sealed partial class ChatEventMapper
{
    private readonly Dictionary<string, TraceSpanEvent> _traceSpans = new(StringComparer.Ordinal);
    private bool _traceRejected;
    private bool _traceStarted;
    private bool _traceFinished;
    private string? _agentTraceId;

    private IReadOnlyList<ChatEvent> MapLearningTrace(JsonElement data)
    {
        if (_traceRejected)
        {
            return None;
        }
        AgentTraceEnvelope? envelope;
        try
        {
            envelope = data.Deserialize(AucJsonContext.Default.AgentTraceEnvelope);
        }
        catch (JsonException)
        {
            return RejectTrace();
        }
        if (envelope is null || ResponseId is null || envelope.ResponseId != ResponseId
            || envelope.Type != AgentTraceEnvelope.EventType || (envelope.Span is null) == (envelope.Status is null))
        {
            return RejectTrace();
        }
        if (envelope.Status is { } status)
        {
            if (status.Source != "agent") return RejectTrace();
            if (status.Status == "started" && status.DroppedEvents == 0 && !_traceStarted)
            {
                _traceStarted = true;
                return [status];
            }
            if (!_traceStarted || _traceFinished
                || !(status.Status == "limited" && status.DroppedEvents > 0
                    || status.Status == "complete" && status.DroppedEvents == 0 && _traceSpans.Count > 0
                    && _traceSpans.Values.All(span => span.Status != "running")))
            {
                return RejectTrace();
            }
            _traceFinished = true;
            return [status];
        }
        var span = envelope.Span!;
        if (!_traceStarted || _traceFinished || !LearningTraceRules.IsValid(span)
            || (_agentTraceId is not null && _agentTraceId != span.TraceId))
        {
            return RejectTrace();
        }
        if (_traceSpans.TryGetValue(span.SpanId, out var previous))
        {
            if (previous.Name != span.Name || previous.ParentSpanId != span.ParentSpanId || previous.StartedAt != span.StartedAt
                || previous.Status != "running")
            {
                return RejectTrace();
            }
        }
        else if (_traceSpans.Count >= LearningTraceRules.MaximumSpans)
        {
            return RejectTrace();
        }
        _agentTraceId = span.TraceId;
        _traceSpans[span.SpanId] = span;
        return [span];
    }

    private TraceStatusEvent[] RejectTrace()
    {
        _traceRejected = true;
        LogInvalidTrace(logger);
        return [new TraceStatusEvent("invalid", 0)];
    }

    [LoggerMessage(1210, LogLevel.Warning, "学習トレースの形式または応答への関連付けが不正です。実測表示を中断します。")]
    private static partial void LogInvalidTrace(ILogger logger);
}
