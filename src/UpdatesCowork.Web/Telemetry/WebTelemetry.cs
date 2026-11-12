using System.Diagnostics;
using System.Diagnostics.Metrics;
using UpdatesCowork.Shared.Telemetry;

namespace UpdatesCowork.Web.Telemetry;

/// <summary>web のスパンとメトリック(8.3節)。名前は <see cref="TelemetryNames"/> の定数だけを使う。</summary>
internal sealed class WebTelemetry
{
    /// <summary><c>UpdatesCowork.Web</c> のスパンの発行元。</summary>
    public static readonly ActivitySource Source = new(TelemetryNames.Sources.Web);

    private readonly Counter<long> _chatTurns;
    private readonly Histogram<double> _chatTtft;
    private readonly Histogram<double> _chatDuration;
    private readonly Counter<long> _feedback;

    public WebTelemetry(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        var meter = meterFactory.Create(TelemetryNames.Sources.Web);
        _chatTurns = meter.CreateCounter<long>(TelemetryNames.Metrics.ChatTurns, description: "チャットの依頼の回数");
        _chatTtft = meter.CreateHistogram<double>(TelemetryNames.Metrics.ChatTtft, unit: "ms", description: "最初の text.delta までの時間");
        _chatDuration = meter.CreateHistogram<double>(TelemetryNames.Metrics.ChatDuration, unit: "ms", description: "1回分の依頼にかかった時間");
        _feedback = meter.CreateCounter<long>(TelemetryNames.Metrics.Feedback, description: "利用者の評価の件数");
    }

    public void RecordTurn(string? action, string outcome, double durationMs)
    {
        var actionTag = new KeyValuePair<string, object?>(TelemetryNames.Dimensions.Action, action ?? "none");
        _chatTurns.Add(1, actionTag, new KeyValuePair<string, object?>(TelemetryNames.Dimensions.Outcome, outcome));
        _chatDuration.Record(durationMs, actionTag);
    }

    public void RecordTtft(string? action, double ttftMs) =>
        _chatTtft.Record(ttftMs, new KeyValuePair<string, object?>(TelemetryNames.Dimensions.Action, action ?? "none"));

    public void RecordFeedback(string rating) =>
        _feedback.Add(1, new KeyValuePair<string, object?>(TelemetryNames.Dimensions.Rating, rating));
}
