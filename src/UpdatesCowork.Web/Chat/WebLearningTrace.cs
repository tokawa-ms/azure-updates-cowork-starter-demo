using System.Diagnostics;
using UpdatesCowork.Shared.Contracts;
using UpdatesCowork.Shared.Telemetry;
using UpdatesCowork.Web.Telemetry;

namespace UpdatesCowork.Web.Chat;

internal sealed class WebLearningTrace
{
    private static readonly AsyncLocal<bool> Measuring = new();
    private static readonly ActivityListener Listener = new()
    {
        ShouldListenTo = source => source.Name == TelemetryNames.Sources.Web,
        Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
            Measuring.Value ? ActivitySamplingResult.AllDataAndRecorded : ActivitySamplingResult.None,
    };
    private readonly HashSet<string> _spans = new(StringComparer.Ordinal);
    private readonly List<ChatEvent> _pending = [];
    private Action<ChatEvent>? _write;
    private int _droppedEvents;

    static WebLearningTrace() => ActivitySource.AddActivityListener(Listener);

    internal void Attach(Action<ChatEvent> write)
    {
        _write = write;
        write(new TraceStatusEvent("started", 0, "web"));
        foreach (var item in _pending) write(item);
        _pending.Clear();
    }

    internal Span Start(string name, ActivityKind kind = ActivityKind.Internal, CancellationToken cancellationToken = default)
    {
        if (name is not (TelemetryNames.Spans.ChatTurn or TelemetryNames.Spans.WebRequestPrepare
            or TelemetryNames.Spans.WebAgentInvoke or TelemetryNames.Spans.WebSseRelay
            or TelemetryNames.Spans.WebArtifactRegister or TelemetryNames.Spans.AgentConversationCreate))
            throw new ArgumentException("学習トレースの処理名が未登録です。", nameof(name));
        var previous = Measuring.Value;
        Measuring.Value = true;
        Activity activity;
        try
        {
            activity = WebTelemetry.Source.StartActivity(name, kind)
                ?? throw new InvalidOperationException("Web の計測を開始できませんでした。");
        }
        finally
        {
            Measuring.Value = previous;
        }
        var parent = activity.Parent;
        while (parent is not null && !_spans.Contains(parent.SpanId.ToHexString())) parent = parent.Parent;
        var parentId = parent?.SpanId.ToHexString()
            ?? (_spans.Contains(activity.ParentSpanId.ToHexString()) ? activity.ParentSpanId.ToHexString() : null);
        var measured = _spans.Count < LearningTraceRules.MaximumSpans;
        var started = new TraceSpanEvent(activity.TraceId.ToHexString(), activity.SpanId.ToHexString(), parentId,
            name, new DateTimeOffset(activity.StartTimeUtc), "running", Source: "web");
        if (measured)
        {
            _spans.Add(started.SpanId);
            Emit(started);
        }
        else _droppedEvents += 2;
        return new Span(activity, started, measured ? Emit : null, cancellationToken);
    }

    internal void Finish() => Emit(new TraceStatusEvent(_droppedEvents == 0 ? "complete" : "limited", _droppedEvents, "web"));

    private void Emit(ChatEvent item)
    {
        if (_write is { } write) write(item);
        else _pending.Add(item);
    }

    internal sealed class Span(Activity activity, TraceSpanEvent started, Action<ChatEvent>? write,
        CancellationToken cancellationToken) : IDisposable
    {
        private bool _finished;
        private bool _completed;
        internal Activity Activity => activity;
        internal void Complete() => _completed = true;

        internal void Finish(string status)
        {
            if (_finished) return;
            _finished = true;
            if (status == "failed") activity.SetStatus(ActivityStatusCode.Error);
            if (activity.GetTagItem(TelemetryNames.Attributes.Outcome) is null)
                activity.SetTag(TelemetryNames.Attributes.Outcome, status);
            activity.Dispose();
            write?.Invoke(started with
            {
                Status = status, EndedAt = started.StartedAt + activity.Duration, DurationMs = activity.Duration.TotalMilliseconds,
            });
        }

        public void Dispose() => Finish(_completed ? "completed" : cancellationToken.IsCancellationRequested ? "cancelled" : "failed");
    }
}
