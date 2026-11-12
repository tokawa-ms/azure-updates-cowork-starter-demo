using System.Diagnostics;
using UpdatesCowork.Shared;
using UpdatesCowork.Shared.Contracts;
using UpdatesCowork.Shared.Telemetry;
using UpdatesCowork.Web.Artifacts;
using UpdatesCowork.Web.Identity;
using UpdatesCowork.Web.Registry;
using UpdatesCowork.Web.Telemetry;

namespace UpdatesCowork.Web.Endpoints;

/// <summary>成果物のダウンロード。台帳でこの利用者のものと確かめてから読み出す。</summary>
internal static class ArtifactEndpoints
{
    public static IEndpointRouteBuilder MapArtifactEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/artifacts/{artifactId}", DownloadAsync);
        return app;
    }

    private static async Task<IResult> DownloadAsync(
        string artifactId,
        HttpContext context,
        IChatRegistry registry,
        IArtifactReader reader,
        CancellationToken ct)
    {
        if (!IdGenerator.IsValid(artifactId, IdGenerator.ArtifactPrefix))
        {
            return ApiResults.NotFound();
        }

        if (UserKeys.Get(context) is not { } userKey)
        {
            return ApiResults.Unauthorized();
        }

        var activity = WebTelemetry.Source.StartActivity(TelemetryNames.Spans.ArtifactDownload, ActivityKind.Internal);
        if (activity is not null)
        {
            // 応答を書き終えるまでをスパンに含める。
            context.Response.RegisterForDispose(activity);
            activity.SetTag(TelemetryNames.Attributes.ArtifactId, artifactId);
        }

        var artifact = await registry.GetArtifactAsync(userKey, artifactId, ct).ConfigureAwait(false);
        if (artifact is null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "not_found");
            return ApiResults.NotFound();
        }

        activity?.SetTag(TelemetryNames.Attributes.ArtifactKind, artifact.Kind);
        var stream = await reader.OpenReadAsync(artifact, ct).ConfigureAwait(false);
        if (stream is null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "not_found");
            return ApiResults.NotFound();
        }

        context.Response.Headers.CacheControl = "private, no-store";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        return TypedResults.Stream(stream, GetContentType(artifact.Kind), GetDownloadFileName(artifact));
    }

    /// <summary>種類から Content-Type を決める。エージェントが返した値はそのまま使わない。</summary>
    internal static string GetContentType(string kind) => kind switch
    {
        ArtifactKinds.Docx => ArtifactKinds.DocxContentType,
        ArtifactKinds.Pptx => ArtifactKinds.PptxContentType,
        _ => "application/octet-stream",
    };

    /// <summary>パスの区切りや制御文字を除いたファイル名を返す。空になったら成果物 ID を使う。</summary>
    internal static string GetDownloadFileName(ArtifactRecord artifact)
    {
        var name = new string(artifact.FileName
            .Where(c => !char.IsControl(c) && c is not ('/' or '\\' or '"' or ':' or '*' or '?' or '<' or '>' or '|'))
            .ToArray()).Trim().TrimStart('.');
        if (name.Length == 0)
        {
            name = artifact.Kind is ArtifactKinds.Docx or ArtifactKinds.Pptx
                ? $"{artifact.ArtifactId}.{artifact.Kind}"
                : artifact.ArtifactId;
        }

        return name.Length > 150 ? name[..150] : name;
    }
}
