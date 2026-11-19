using System.Text;
using Microsoft.Extensions.Options;
using UpdatesCowork.Shared;
using UpdatesCowork.Shared.Contracts;
using UpdatesCowork.Shared.Telemetry;
using UpdatesCowork.Web.Configuration;
using UpdatesCowork.Web.Identity;
using UpdatesCowork.Web.Registry;
using UpdatesCowork.Web.Telemetry;

namespace UpdatesCowork.Web.Endpoints;

/// <summary>利用者の評価を記録する(8.6節)。</summary>
internal static class FeedbackEndpoints
{
    public const int MaxCommentLength = 500;

    public const string LoggerCategory = "UpdatesCowork.Web.Feedback";

    public static IEndpointRouteBuilder MapFeedbackEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/feedback", SubmitAsync);
        return app;
    }

    private static async Task<IResult> SubmitAsync(
        FeedbackRequest request,
        HttpContext context,
        IChatRegistry registry,
        WebTelemetry telemetry,
        ILoggerFactory loggerFactory,
        IOptions<WebOptions> webOptions,
        CancellationToken ct)
    {
        if (UserKeys.Get(context) is not { } userKey)
        {
            return ApiResults.Unauthorized();
        }

        if (!IdGenerator.IsValid(request.ThreadId, IdGenerator.ThreadPrefix))
        {
            return ApiResults.BadRequest("threadId の形式が正しくありません。");
        }

        if (request.ResponseId is null || !ChatEndpoints.OpaqueIdPattern().IsMatch(request.ResponseId))
        {
            return ApiResults.BadRequest("responseId の形式が正しくありません。");
        }

        if (!FeedbackRatings.IsKnown(request.Rating))
        {
            return ApiResults.BadRequest("rating は good か bad にしてください。");
        }

        var thread = await registry.GetThreadAsync(userKey, request.ThreadId!, ct).ConfigureAwait(false);
        if (thread is null)
        {
            return ApiResults.ThreadNotFound();
        }

        var comment = SanitizeComment(request.Comment);
        var logger = loggerFactory.CreateLogger(LoggerCategory);
        var trace = webOptions.Value.Features.FeedbackAnnotations ? thread.FindResponseTrace(request.ResponseId) : null;
        List<KeyValuePair<string, object?>> state =
        [
            new(TelemetryNames.Attributes.ResponseId, request.ResponseId),
            new(TelemetryNames.Attributes.ConversationId, thread.ConversationId),
            new(TelemetryNames.Attributes.ThreadId, thread.ThreadId),
            new(TelemetryNames.Attributes.FeedbackRating, request.Rating),
            new(TelemetryNames.Attributes.FeedbackComment, comment),
            new(TelemetryNames.Attributes.FeedbackTraceLinked, trace is not null),
            new("{OriginalFormat}", TelemetryNames.FeedbackLogMessage),
        ];

        // メッセージは固定の auc.feedback にし、値は属性として送る(KQL で customDimensions から引ける)。
        void Record()
        {
            logger.Log(LogLevel.Information, new EventId(1001, "Feedback"), state, null, static (_, _) => TelemetryNames.FeedbackLogMessage);
            if (trace is not null)
            {
                FeedbackAnnotation.Log(logger, request.Rating!, comment, request.ResponseId, thread.ConversationId);
            }
        }

        // 応答のトレースが分かるときは、元の会話のトレースの中で記録する。分からなければ注釈は送らない
        // (評価要求のトレースに注釈を付けると、Foundry では別の会話の評価に見えてしまうため)。
        if (trace is not null) FeedbackAnnotation.RecordInTrace(trace, Record);
        else Record();

        telemetry.RecordFeedback(request.Rating!);
        return TypedResults.NoContent();
    }

    /// <summary>制御文字(改行を除く)を取り除き、500文字までにする。</summary>
    internal static string? SanitizeComment(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            return null;
        }

        var builder = new StringBuilder(Math.Min(comment.Length, MaxCommentLength));
        foreach (var c in comment)
        {
            if (builder.Length >= MaxCommentLength)
            {
                break;
            }

            if (c == '\n' || !char.IsControl(c))
            {
                builder.Append(c);
            }
        }

        var result = builder.ToString().Trim();
        return result.Length == 0 ? null : result;
    }
}
