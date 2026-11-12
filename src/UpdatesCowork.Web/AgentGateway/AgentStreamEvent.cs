using System.Text.Json;

namespace UpdatesCowork.Web.AgentGateway;

/// <summary>エージェントから受け取った Responses のストリームのイベント1件。</summary>
/// <param name="Type">イベントの種類(<c>response.created</c> など)。</param>
/// <param name="Data">イベントの JSON (<c>JsonDocument</c> から切り離した複製)。</param>
internal sealed record AgentStreamEvent(string Type, JsonElement Data);

/// <summary>エージェントを呼ぶときの共通の情報(ヘッダーに使う)。</summary>
internal sealed record AgentCallContext(string ThreadId, string UserKey);

/// <summary>承認の返事(任意機能の承認)。</summary>
internal sealed record AgentApprovalResponse(string ApprovalRequestId, bool Approve);

/// <summary>1回分の依頼。<see cref="Approval"/> があるときは承認の返事だけを送る。</summary>
internal sealed record AgentTurnRequest
{
    public required AgentCallContext Context { get; init; }

    public string? ConversationId { get; init; }

    public string? PreviousResponseId { get; init; }

    /// <summary>developer メッセージ(<c>&lt;page_context&gt;</c>)。ないときは付けない。</summary>
    public string? DeveloperText { get; init; }

    public string? UserText { get; init; }

    public AgentApprovalResponse? Approval { get; init; }
}

/// <summary>エージェントの呼び出しの失敗。<see cref="Code"/> は <c>ChatErrorCodes</c> の値。</summary>
internal sealed class AgentGatewayException : Exception
{
    public AgentGatewayException()
        : this("agent_error", "エージェントの呼び出しに失敗しました。")
    {
    }

    public AgentGatewayException(string message)
        : this("agent_error", message)
    {
    }

    public AgentGatewayException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = "agent_error";
    }

    public AgentGatewayException(string code, string message, int? statusCode = null, TimeSpan? retryAfter = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        StatusCode = statusCode;
        RetryAfter = retryAfter;
    }

    public string Code { get; }

    public int? StatusCode { get; }

    public TimeSpan? RetryAfter { get; }
}
