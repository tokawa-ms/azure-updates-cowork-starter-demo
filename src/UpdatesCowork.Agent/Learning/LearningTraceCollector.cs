using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Azure.AI.AgentServer.Responses.Models;
using UpdatesCowork.Shared.Contracts;
using UpdatesCowork.Shared.Telemetry;

namespace UpdatesCowork.Agent.Learning;

internal sealed class LearningTraceCollector : IDisposable
{
    internal const string SessionMetadataKey = "auc.learning.session";
    internal const string ParentMetadataKey = "auc.learning.parent";
    private static readonly AsyncLocal<LearningTraceSession?> CurrentSession = new();
    private readonly ConcurrentDictionary<string, LearningTraceSession> _sessions = new(StringComparer.Ordinal);
    private readonly ActivityListener _listener;

    public LearningTraceCollector()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == TelemetryNames.Sources.Agent,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                Current is null ? ActivitySamplingResult.None : ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => OwnedSession()?.Record(activity, false),
            ActivityStopped = activity => OwnedSession()?.Record(activity, true),
        };
        ActivitySource.AddActivityListener(_listener);
    }

    internal static LearningTraceSession? Current => CurrentSession.Value;

    internal LearningTraceSession CreateSession()
    {
        var session = new LearningTraceSession(Guid.NewGuid().ToString("N"), this);
        if (!_sessions.TryAdd(session.Id, session))
        {
            throw new InvalidOperationException("学習トレースを開始できませんでした。");
        }
        return session;
    }

    internal static IDisposable Enter(LearningTraceSession session)
    {
        var previous = CurrentSession.Value;
        CurrentSession.Value = session;
        return new Scope(() => CurrentSession.Value = previous);
    }

    internal bool TryGetSession(string id, string parentSpanId, out LearningTraceSession? session)
    {
        session = _sessions.TryGetValue(id, out var found) && found.Contains(parentSpanId) ? found : null;
        return session is not null;
    }

    internal void Remove(LearningTraceSession session) => _sessions.TryRemove(session.Id, out _);

    private LearningTraceSession? OwnedSession() => ReferenceEquals(Current?.Owner, this) ? Current : null;

    public void Dispose() => _listener.Dispose();

    private sealed class Scope(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}

internal sealed record LearningTraceItem(ResponseStreamEvent? Response = null, TraceSpanEvent? Span = null);

internal sealed class LearningTraceSession(string id, LearningTraceCollector owner)
{
    internal const int MaximumSpans = LearningTraceRules.MaximumSpans;
    internal string Id { get; } = id;
    internal LearningTraceCollector Owner { get; } = owner;
    internal Channel<LearningTraceItem> Events { get; } = Channel.CreateBounded<LearningTraceItem>(
        new BoundedChannelOptions(512) { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait });
    private readonly ConcurrentDictionary<string, RecordedSpan> _spans = new(StringComparer.Ordinal);
    private readonly AsyncLocal<SpanFrame?> _currentSpan = new();
    private int _droppedEvents;
    private int _spanCount;
    internal int DroppedEvents => Volatile.Read(ref _droppedEvents);

    internal bool Contains(string spanId) => _spans.ContainsKey(spanId);
    internal ActivityContext GetContext(string spanId) => _spans[spanId].Context;

    internal void Record(Activity activity, bool completed)
    {
        if (!LearningTraceRules.IsMeasuredOperation(activity.OperationName))
        {
            return;
        }
        var spanId = activity.SpanId.ToHexString();
        RecordedSpan recorded;
        if (!completed)
        {
            var frame = new SpanFrame(spanId, _currentSpan.Value);
            recorded = new RecordedSpan(activity.Context, FindParent(activity), frame);
            if (Interlocked.Increment(ref _spanCount) > MaximumSpans || !_spans.TryAdd(spanId, recorded))
            {
                Interlocked.Increment(ref _droppedEvents);
                return;
            }
            _currentSpan.Value = frame;
        }
        else if (!_spans.TryGetValue(spanId, out recorded!))
        {
            Interlocked.Increment(ref _droppedEvents);
            return;
        }
        else if (ReferenceEquals(_currentSpan.Value, recorded.Frame))
        {
            _currentSpan.Value = recorded.Frame.Previous;
        }

        var started = new DateTimeOffset(activity.StartTimeUtc);
        var evt = new TraceSpanEvent(
            activity.TraceId.ToHexString(), spanId, recorded.ParentSpanId, activity.OperationName, started,
            !completed ? "running"
                : activity.GetTagItem(TelemetryNames.Attributes.Outcome) as string == "cancelled" ? "cancelled"
                : activity.Status == ActivityStatusCode.Error ? "failed" : "completed",
            completed ? started + activity.Duration : null, completed ? activity.Duration.TotalMilliseconds : null,
            SafeName(activity.GetTagItem(TelemetryNames.Attributes.ToolName)),
            SafeName(activity.GetTagItem(TelemetryNames.Attributes.ToolCallId)),
            SafeName(activity.GetTagItem(TelemetryNames.Attributes.Model)),
            Count(activity.GetTagItem(TelemetryNames.Attributes.InputTokens)),
            Count(activity.GetTagItem(TelemetryNames.Attributes.OutputTokens)),
            activity.GetTagItem(TelemetryNames.Attributes.CacheHit) as bool?);
        if (!Events.Writer.TryWrite(new LearningTraceItem(Span: evt)))
        {
            Interlocked.Increment(ref _droppedEvents);
        }
    }

    private string? FindParent(Activity activity)
    {
        var parent = activity.Parent;
        while (parent is not null && !Contains(parent.SpanId.ToHexString()))
        {
            parent = parent.Parent;
        }
        if (parent is not null) return parent.SpanId.ToHexString();
        if (Contains(activity.ParentSpanId.ToHexString())) return activity.ParentSpanId.ToHexString();

        // SDK が中間 Activity を別コンテキストで作っても、実測処理の非同期スコープは引き継ぐ。
        for (var frame = _currentSpan.Value; frame is not null; frame = frame.Previous)
        {
            if (_spans.TryGetValue(frame.SpanId, out var span) && span.Context.TraceId == activity.TraceId)
                return frame.SpanId;
        }
        return null;
    }

    private sealed record SpanFrame(string SpanId, SpanFrame? Previous);
    private sealed record RecordedSpan(ActivityContext Context, string? ParentSpanId, SpanFrame Frame);

    private static string? SafeName(object? value) => value is string text && LearningTraceRules.IsOptionalName(text) ? text : null;
    private static long? Count(object? value) => value is long and >= 0 ? (long)value : null;
}
