using System.Globalization;
using Azure.AI.AgentServer.Core;
using UpdatesCowork.Office;
using UpdatesCowork.Shared;
using UpdatesCowork.Shared.Contracts;

namespace UpdatesCowork.Agent.Artifacts;

internal static class ArtifactPath
{
    internal static ArtifactInfo Create(string fileName, OfficeDocument document, string storage, TimeProvider time, string? sessionId = null)
    {
        if (document.ValidationErrors.Count != 0 || document.Content.Length == 0
            || document.Extension is not (ArtifactKinds.Docx or ArtifactKinds.Pptx))
        {
            throw new ArgumentException("検証済みの Office ファイルだけを保存できます。", nameof(document));
        }

        var id = IdGenerator.NewArtifactId(time);
        var name = OfficeFileNames.WithExtension(fileName, document.Extension);
        var date = time.GetUtcNow().ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
        var location = storage switch
        {
            ArtifactStorages.Session when !string.IsNullOrWhiteSpace(sessionId) => $"artifacts/{id}/{name}",
            ArtifactStorages.Session => throw new InvalidOperationException("Hosted Agent のセッション ID を取得できません。"),
            _ => $"{date}/{id}/{name}",
        };
        return new(id, name, document.ContentType, document.Extension, document.Content.LongLength,
            document.ItemCount, storage, location, sessionId);
    }
}
