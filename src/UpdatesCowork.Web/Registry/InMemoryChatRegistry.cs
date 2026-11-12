using System.Collections.Concurrent;
using UpdatesCowork.Shared;

namespace UpdatesCowork.Web.Registry;

/// <summary>メモリー上の台帳(ローカルとテスト用)。アプリを止めると消える。</summary>
internal sealed class InMemoryChatRegistry(TimeProvider timeProvider) : IChatRegistry
{
    private readonly ConcurrentDictionary<(string UserKey, string ThreadId), ChatThread> _threads = new();
    private readonly ConcurrentDictionary<(string UserKey, string ArtifactId), ArtifactRecord> _artifacts = new();

    public Task<ChatThread> CreateThreadAsync(string userKey, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(userKey);

        var now = timeProvider.GetUtcNow();
        var thread = new ChatThread
        {
            ThreadId = IdGenerator.NewThreadId(timeProvider),
            UserKey = userKey,
            CreatedUtc = now,
            UpdatedUtc = now,
        };
        _threads[(userKey, thread.ThreadId)] = thread;
        return Task.FromResult(thread);
    }

    public Task<ChatThread?> GetThreadAsync(string userKey, string threadId, CancellationToken ct) =>
        Task.FromResult(_threads.TryGetValue((userKey, threadId), out var thread) ? thread : null);

    public Task UpdateThreadAsync(ChatThread thread, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(thread);

        _threads[(thread.UserKey, thread.ThreadId)] = thread with { UpdatedUtc = timeProvider.GetUtcNow() };
        return Task.CompletedTask;
    }

    public Task<ChatThread?> DeleteThreadAsync(string userKey, string threadId, CancellationToken ct)
    {
        if (!_threads.TryRemove((userKey, threadId), out var thread))
        {
            return Task.FromResult<ChatThread?>(null);
        }

        foreach (var key in _artifacts.Keys)
        {
            if (key.UserKey == userKey
                && _artifacts.TryGetValue(key, out var artifact)
                && artifact.ThreadId == threadId)
            {
                _artifacts.TryRemove(key, out _);
            }
        }

        return Task.FromResult<ChatThread?>(thread);
    }

    public Task AddArtifactAsync(string userKey, ArtifactRecord artifact, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(userKey);
        ArgumentNullException.ThrowIfNull(artifact);

        _artifacts[(userKey, artifact.ArtifactId)] = artifact;
        return Task.CompletedTask;
    }

    public Task<ArtifactRecord?> GetArtifactAsync(string userKey, string artifactId, CancellationToken ct) =>
        Task.FromResult(_artifacts.TryGetValue((userKey, artifactId), out var artifact) ? artifact : null);
}
