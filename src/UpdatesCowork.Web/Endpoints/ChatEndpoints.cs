using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using UpdatesCowork.Shared;
using UpdatesCowork.Shared.Contracts;
using UpdatesCowork.Web.AgentGateway;
using UpdatesCowork.Web.Artifacts;
using UpdatesCowork.Web.Chat;
using UpdatesCowork.Web.Configuration;
using UpdatesCowork.Web.Identity;
using UpdatesCowork.Web.Registry;

namespace UpdatesCowork.Web.Endpoints;

/// <summary>スレッドの作成、チャット(SSE)、承認の返事。</summary>
internal static partial class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/threads/new", CreateThreadAsync);
        app.MapPost("/api/threads/end", EndThreadAsync);
        app.MapPost("/api/chat", ChatAsync);
        app.MapPost("/api/chat/approval", ApprovalAsync);
        return app;
    }

    private static async Task<IResult> CreateThreadAsync(HttpContext context, IChatRegistry registry, CancellationToken ct)
    {
        if (UserKeys.Get(context) is not { } userKey)
        {
            return ApiResults.Unauthorized();
        }

        var thread = await registry.CreateThreadAsync(userKey, ct).ConfigureAwait(false);
        return TypedResults.Ok(new NewThreadResponse(thread.ThreadId));
    }

    private static async Task<IResult> EndThreadAsync(
        EndThreadRequest request,
        HttpContext context,
        IChatRegistry registry,
        ISessionArtifactManager sessionArtifacts,
        CancellationToken ct)
    {
        if (UserKeys.Get(context) is not { } userKey)
        {
            return ApiResults.Unauthorized();
        }

        if (request.ThreadId is not { } threadId || !IdGenerator.IsValid(threadId, IdGenerator.ThreadPrefix))
        {
            return ApiResults.BadRequest("threadId の形式が正しくありません。");
        }

        var thread = await registry.GetThreadAsync(userKey, threadId, ct).ConfigureAwait(false);
        if (thread is null)
        {
            return TypedResults.NoContent();
        }

        if (thread.SessionId is { Length: > 0 } sessionId)
        {
            await sessionArtifacts.DeleteSessionAsync(sessionId, ct).ConfigureAwait(false);
        }

        await registry.DeleteThreadAsync(userKey, threadId, ct).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> ChatAsync(
        ChatRequest request,
        HttpContext context,
        IChatRegistry registry,
        ChatOrchestrator orchestrator,
        CancellationToken ct)
    {
        if (UserKeys.Get(context) is not { } userKey)
        {
            return ApiResults.Unauthorized();
        }

        if (!PageContextValidator.TryValidate(request, out var input, out var error))
        {
            return ApiResults.BadRequest(error!);
        }

        ChatThread thread;
        if (string.IsNullOrEmpty(request.ThreadId))
        {
            thread = await registry.CreateThreadAsync(userKey, ct).ConfigureAwait(false);
        }
        else
        {
            if (!IdGenerator.IsValid(request.ThreadId, IdGenerator.ThreadPrefix))
            {
                return ApiResults.BadRequest("threadId の形式が正しくありません。");
            }

            var found = await registry.GetThreadAsync(userKey, request.ThreadId, ct).ConfigureAwait(false);
            if (found is null)
            {
                return ApiResults.ThreadNotFound();
            }

            thread = found;
        }

        var turn = new ChatTurnInput
        {
            Thread = thread,
            Action = input!.Action,
            Page = input.Page,
            Message = input.Message,
        };
        return ServerSentEvents(context, orchestrator.RunAsync(turn, context.RequestAborted));
    }

    private static async Task<IResult> ApprovalAsync(
        ApprovalRequest request,
        HttpContext context,
        IChatRegistry registry,
        ChatOrchestrator orchestrator,
        IOptions<WebOptions> webOptions,
        IOptions<AgentOptions> agentOptions,
        CancellationToken ct)
    {
        // 承認は任意機能。無効なときは API がないものとして扱う。
        if (!webOptions.Value.Features.Approvals)
        {
            return TypedResults.NotFound();
        }

        if (UserKeys.Get(context) is not { } userKey)
        {
            return ApiResults.Unauthorized();
        }

        if (!IdGenerator.IsValid(request.ThreadId, IdGenerator.ThreadPrefix))
        {
            return ApiResults.BadRequest("threadId の形式が正しくありません。");
        }

        if (request.ApprovalRequestId is null || !OpaqueIdPattern().IsMatch(request.ApprovalRequestId))
        {
            return ApiResults.BadRequest("approvalRequestId の形式が正しくありません。");
        }

        var thread = await registry.GetThreadAsync(userKey, request.ThreadId!, ct).ConfigureAwait(false);
        if (thread is null)
        {
            return ApiResults.ThreadNotFound();
        }

        var hasContinuation = agentOptions.Value.ConversationMode == ConversationMode.Conversations
            ? !string.IsNullOrEmpty(thread.ConversationId)
            : !string.IsNullOrEmpty(thread.LastResponseId);
        if (!hasContinuation)
        {
            return ApiResults.BadRequest("承認を待っている依頼がありません。");
        }

        var turn = new ChatTurnInput
        {
            Thread = thread,
            Approval = new AgentApprovalResponse(request.ApprovalRequestId, request.Approve),
        };
        return ServerSentEvents(context, orchestrator.RunAsync(turn, context.RequestAborted));
    }

    private static ServerSentEventsResult<string> ServerSentEvents(HttpContext context, IAsyncEnumerable<System.Net.ServerSentEvents.SseItem<string>> items)
    {
        // リバースプロキシにバッファーさせない。
        context.Response.Headers["X-Accel-Buffering"] = "no";
        context.Response.Headers.CacheControl = "no-cache";
        return TypedResults.ServerSentEvents(items);
    }

    /// <summary>エージェントが発行した ID (承認要求 ID、応答 ID)の形。</summary>
    [GeneratedRegex("^[A-Za-z0-9_-]{1,200}$", RegexOptions.CultureInvariant)]
    internal static partial Regex OpaqueIdPattern();
}

internal sealed record EndThreadRequest(string? ThreadId);

/// <summary>API の共通のエラー応答。</summary>
internal static class ApiResults
{
    public static IResult BadRequest(string message) =>
        TypedResults.BadRequest(new ApiError(ChatErrorCodes.InvalidRequest, message));

    public static IResult ThreadNotFound() =>
        TypedResults.NotFound(new ApiError("thread_not_found", "スレッドが見つかりません。新しい会話を始めてください。"));

    public static IResult NotFound() =>
        TypedResults.NotFound(new ApiError("not_found", "見つかりません。"));

    public static IResult Unauthorized() =>
        TypedResults.Json(new ApiError("unauthorized", "利用者を識別できません。"), WebJsonContext.Default.ApiError, statusCode: StatusCodes.Status401Unauthorized);
}
