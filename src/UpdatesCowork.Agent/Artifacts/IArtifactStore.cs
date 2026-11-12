using UpdatesCowork.Office;
using UpdatesCowork.Shared.Contracts;

namespace UpdatesCowork.Agent.Artifacts;

internal interface IArtifactStore
{
    Task<ArtifactInfo> SaveAsync(string fileName, OfficeDocument document, CancellationToken cancellationToken);
}
