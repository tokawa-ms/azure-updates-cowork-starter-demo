using UpdatesCowork.Office;
using UpdatesCowork.Shared.Contracts;
using UpdatesCowork.Shared.Telemetry;

namespace UpdatesCowork.Agent.Artifacts;

internal sealed class SessionArtifactStore(
    string sessionDirectory,
    Func<string?> sessionIdProvider,
    TimeProvider time,
    AgentTelemetry telemetry) : IArtifactStore
{
    private readonly string _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sessionDirectory));

    public async Task<ArtifactInfo> SaveAsync(string fileName, OfficeDocument document, CancellationToken cancellationToken)
    {
        var artifact = ArtifactPath.Create(fileName, document, ArtifactStorages.Session, time, sessionIdProvider());
        var path = Path.GetFullPath(Path.Combine(_root, artifact.Location.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("成果物の保存先がセッション領域の外です。");
        }

        using var activity = telemetry.Source.StartActivity(TelemetryNames.Spans.ArtifactSave);
        activity?.SetTag(TelemetryNames.Attributes.ArtifactId, artifact.ArtifactId);
        activity?.SetTag(TelemetryNames.Attributes.ArtifactStorage, artifact.Storage);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await stream.WriteAsync(document.Content, cancellationToken).ConfigureAwait(false);
        return artifact;
    }
}
