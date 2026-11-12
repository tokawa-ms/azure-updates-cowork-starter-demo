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
}

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
