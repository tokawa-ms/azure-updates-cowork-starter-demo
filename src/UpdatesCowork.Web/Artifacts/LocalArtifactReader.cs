using UpdatesCowork.Shared.Contracts;
using UpdatesCowork.Web.Registry;

namespace UpdatesCowork.Web.Artifacts;

/// <summary>
/// ローカルのフォルダー(<c>Artifacts:LocalDir</c>)から成果物を読む。エージェントの LocalArtifactStore と同じフォルダーを指す。
/// <c>location</c> は <c>art_.../ファイル名</c> の相対パスで、フォルダーの外を指すものは読まない。
/// </summary>
internal sealed class LocalArtifactReader(string rootDirectory) : IArtifactReader
{
    private readonly string _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));

    public Task<Stream?> OpenReadAsync(ArtifactRecord artifact, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        if (artifact.Storage != ArtifactStorages.Local || !ArtifactLocationValidator.IsValid(artifact.Storage, artifact.Location))
        {
            return Task.FromResult<Stream?>(null);
        }

        var fullPath = Path.GetFullPath(Path.Combine(_root, artifact.Location));
        if (!fullPath.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
        {
            return Task.FromResult<Stream?>(null);
        }

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Task.FromResult<Stream?>(stream);
    }
}
