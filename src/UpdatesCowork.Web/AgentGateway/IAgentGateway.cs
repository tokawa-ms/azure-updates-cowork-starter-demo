namespace UpdatesCowork.Web.AgentGateway;

/// <summary>Hosted Agent (Responses プロトコル)の呼び出し口(5.2節)。</summary>
internal interface IAgentGateway
{
    /// <summary>会話を作り、会話 ID を返す。</summary>
    Task<string> CreateConversationAsync(AgentCallContext context, CancellationToken cancellationToken);

    /// <summary>応答をストリームで受け取る。HTTP のエラーは <see cref="AgentGatewayException"/> になる。</summary>
    IAsyncEnumerable<AgentStreamEvent> StreamAsync(AgentTurnRequest request, CancellationToken cancellationToken);
}
