using UpdatesCowork.Office;
using UpdatesCowork.Shared.Contracts;
using UpdatesCowork.Shared.Telemetry;

namespace UpdatesCowork.Agent.Artifacts;

internal sealed class LocalArtifactStore(AgentRuntimeOptions options, TimeProvider time, AgentTelemetry telemetry) : IArtifactStore
{
    public async Task<ArtifactInfo> SaveAsync(string fileName, OfficeDocument document, CancellationToken cancellationToken)
    {
        var artifact = ArtifactPath.Create(fileName, document, ArtifactStorages.Local, time);
        using var activity = telemetry.Source.StartActivity(TelemetryNames.Spans.ArtifactSave);
        activity?.SetTag(TelemetryNames.Attributes.ArtifactId, artifact.ArtifactId);
        activity?.SetTag(TelemetryNames.Attributes.ArtifactStorage, artifact.Storage);
        var path = Path.Combine(options.LocalDirectory, artifact.Location.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await stream.WriteAsync(document.Content, cancellationToken).ConfigureAwait(false);
        return artifact;
    }
}
