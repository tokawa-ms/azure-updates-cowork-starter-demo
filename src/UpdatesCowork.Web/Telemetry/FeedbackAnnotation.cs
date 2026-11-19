using System.Diagnostics;
using UpdatesCowork.Shared.Contracts;
using UpdatesCowork.Shared.Telemetry;
using UpdatesCowork.Web.Registry;

namespace UpdatesCowork.Web.Telemetry;

/// <summary>
/// 利用者の評価を Foundry のトレース注釈(プレビュー)の形で記録する(8.6節)。
/// Azure Monitor のエクスポーターは <c>microsoft.custom_event.name</c> を持つログを customEvents に送る。
/// </summary>
internal static class FeedbackAnnotation
{
    public const int EventId = 1002;

    /// <summary>既定の採点テンプレートの評価名。高評価は 1.0 / pass、低評価は 0.0 / fail。</summary>
    public const string EvaluationName = "task_completion";

    public const string EndUserSource = "end_user";

    public const string HumanActor = "human";

    /// <summary>採点の範囲と向き。Foundry の人による評価のサンプルと同じ形の JSON 文字列で送る。</summary>
    public const string InternalProperties =
        """{"gen_ai.evaluation.threshold":"1.0","gen_ai.evaluation.min_value":"0.0","gen_ai.evaluation.max_value":"1.0","gen_ai.evaluation.desirable_direction":"increase","gen_ai.evaluation.type":"boolean"}""";

    /// <summary>
    /// 元の chat.turn の子として <c>feedback.record</c> を開き、その中で <paramref name="record"/> を実行する。
    /// ログはこのスパンのトレース ID(operation_Id)を持つので、評価が会話と同じトレースに並ぶ。
    /// </summary>
    public static void RecordInTrace(ResponseTrace trace, Action record)
    {
        ArgumentNullException.ThrowIfNull(trace);
        ArgumentNullException.ThrowIfNull(record);

        var previous = Activity.Current;
        var parent = new ActivityContext(
            ActivityTraceId.CreateFromString(trace.TraceId),
            ActivitySpanId.CreateFromString(trace.SpanId),
            trace.Recorded ? ActivityTraceFlags.Recorded : ActivityTraceFlags.None,
            isRemote: true);

        // 計測が無効でもログの相関だけは取れるように、発行元が無視したときは手で Activity を作る。
        var activity = WebTelemetry.Source.StartActivity(TelemetryNames.Spans.FeedbackRecord, ActivityKind.Internal, parent)
            ?? new Activity(TelemetryNames.Spans.FeedbackRecord).SetParentId(parent.TraceId, parent.SpanId, parent.TraceFlags).Start();
        try
        {
            record();
        }
        finally
        {
            activity.Dispose();
            Activity.Current = previous;
        }
    }

    /// <summary><c>gen_ai.evaluation.result</c> を1件記録する。コメントは整えたものだけを渡す。</summary>
    public static void Log(ILogger logger, string rating, string? comment, string responseId, string? conversationId)
    {
        ArgumentNullException.ThrowIfNull(logger);

        var good = rating == FeedbackRatings.Good;
        List<KeyValuePair<string, object?>> state =
        [
            new(TelemetryNames.Attributes.CustomEventName, TelemetryNames.EvaluationResultEvent),
            new(TelemetryNames.Attributes.EvaluationName, EvaluationName),
            new(TelemetryNames.Attributes.EvaluationScoreValue, good ? 1.0 : 0.0),
            new(TelemetryNames.Attributes.EvaluationScoreLabel, good ? "pass" : "fail"),
            new(TelemetryNames.Attributes.ResponseId, responseId),
            new(TelemetryNames.Attributes.HumanEvaluationSource, EndUserSource),
            new(TelemetryNames.Attributes.EvaluationActorType, HumanActor),
            new(TelemetryNames.Attributes.InternalProperties, InternalProperties),
        ];
        if (comment is not null) state.Add(new(TelemetryNames.Attributes.EvaluationExplanation, comment));
        if (conversationId is not null) state.Add(new(TelemetryNames.Attributes.ConversationId, conversationId));
        state.Add(new("{OriginalFormat}", TelemetryNames.EvaluationResultEvent));

        logger.Log(LogLevel.Information, new EventId(EventId, "FeedbackAnnotation"), state, null,
            static (_, _) => TelemetryNames.EvaluationResultEvent);
    }
}
