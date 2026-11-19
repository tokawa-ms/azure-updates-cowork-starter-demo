namespace UpdatesCowork.Web.Registry;

/// <summary>台帳(6.6節)。行は利用者キーで分けるので、他人の行は検索の対象にもならない。</summary>
internal interface IChatRegistry
{
    Task<ChatThread> CreateThreadAsync(string userKey, CancellationToken ct);

    Task<ChatThread?> GetThreadAsync(string userKey, string threadId, CancellationToken ct);

    Task UpdateThreadAsync(ChatThread thread, CancellationToken ct);

    Task<ChatThread?> DeleteThreadAsync(string userKey, string threadId, CancellationToken ct);

    Task AddArtifactAsync(string userKey, ArtifactRecord artifact, CancellationToken ct);

    Task<ArtifactRecord?> GetArtifactAsync(string userKey, string artifactId, CancellationToken ct);
}

/// <summary>会話のスレッド。</summary>
internal sealed record ChatThread
{
    public required string ThreadId { get; init; }

    public required string UserKey { get; init; }

    public string? ConversationId { get; init; }

    public string? LastResponseId { get; init; }

    public string? SessionId { get; init; }

    public DateTimeOffset CreatedUtc { get; init; }

    public DateTimeOffset UpdatedUtc { get; init; }

    /// <summary>応答 ID ごとの chat.turn のトレース。評価を元のトレースに結び付けるために使う(8.6節)。</summary>
    public IReadOnlyList<ResponseTrace> ResponseTraces { get; init; } = [];

    /// <summary>保持する応答トレースの上限。古いものから捨てる。</summary>
    public const int MaxResponseTraces = 20;

    public ChatThread WithResponseTrace(ResponseTrace trace) => this with
    {
        ResponseTraces = [.. ResponseTraces.Where(item => item.ResponseId != trace.ResponseId).TakeLast(MaxResponseTraces - 1), trace],
    };

    public ResponseTrace? FindResponseTrace(string responseId) =>
        ResponseTraces.LastOrDefault(item => item.ResponseId == responseId);
}

/// <summary>応答を返した chat.turn スパンの識別子(W3C の16進表記)。</summary>
internal sealed record ResponseTrace(string ResponseId, string TraceId, string SpanId, bool Recorded);

/// <summary>台帳に登録した成果物。</summary>
internal sealed record ArtifactRecord
{
    public required string ArtifactId { get; init; }

    public required string ThreadId { get; init; }

    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    public required string Kind { get; init; }

    public long SizeBytes { get; init; }

    public required string Storage { get; init; }

    public required string Location { get; init; }

    public string? SessionId { get; init; }

    public DateTimeOffset CreatedUtc { get; init; }
}
