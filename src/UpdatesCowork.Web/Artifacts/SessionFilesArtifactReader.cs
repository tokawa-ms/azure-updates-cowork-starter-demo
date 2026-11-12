using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using UpdatesCowork.Shared.Telemetry;
using UpdatesCowork.Web.Configuration;
using UpdatesCowork.Web.Registry;
using UpdatesCowork.Web.Telemetry;

namespace UpdatesCowork.Web.Artifacts;

internal interface ISessionArtifactManager
{
    Task DeleteSessionAsync(string sessionId, CancellationToken ct);
}

internal sealed partial class SessionFilesArtifactReader(
    HttpClient httpClient,
    IOptions<AgentOptions> agentOptions) : IArtifactReader, ISessionArtifactManager
{
    private const string OpenAiProtocolSuffix = "/protocols/openai";

    public async Task<Stream?> OpenReadAsync(ArtifactRecord artifact, CancellationToken ct)
    {
        if (!ArtifactLocationValidator.IsValid(artifact.Storage, artifact.Location)
            || artifact.Storage != UpdatesCowork.Shared.Contracts.ArtifactStorages.Session
            || !SessionIdPattern().IsMatch(artifact.SessionId ?? string.Empty))
        {
            return null;
        }

        using var activity = WebTelemetry.Source.StartActivity(
            TelemetryNames.Spans.ArtifactSessionFileRead, ActivityKind.Client);
        activity?.SetTag(TelemetryNames.Attributes.ArtifactStorage, artifact.Storage);
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(
            $"/sessions/{Uri.EscapeDataString(artifact.SessionId!)}/files/content",
            artifact.Location));
        var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            try
            {
                response.EnsureSuccessStatusCode();
            }
            finally
            {
                response.Dispose();
            }
        }

        var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return new ResponseOwnedStream(stream, response);
    }

    public async Task DeleteSessionAsync(string sessionId, CancellationToken ct)
    {
        if (!SessionIdPattern().IsMatch(sessionId))
        {
            throw new ArgumentException("Session ID の形式が正しくありません。", nameof(sessionId));
        }

        using var activity = WebTelemetry.Source.StartActivity(
            TelemetryNames.Spans.ArtifactSessionDelete, ActivityKind.Client);
        using var request = new HttpRequestMessage(HttpMethod.Delete,
            BuildUri($"/sessions/{Uri.EscapeDataString(sessionId)}"));
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }
    }

    internal Uri BuildUri(string relativePath, string? filePath = null)
    {
        var options = agentOptions.Value;
        var baseUrl = options.BaseUrl.TrimEnd('/');
        if (!baseUrl.EndsWith(OpenAiProtocolSuffix, StringComparison.OrdinalIgnoreCase)
            || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException("Agent:BaseUrl は Hosted Agent の OpenAI protocol endpoint にしてください。");
        }

        var query = new StringBuilder("api-version=").Append(Uri.EscapeDataString(options.ApiVersion));
        if (filePath is not null)
        {
            query.Append("&path=").Append(Uri.EscapeDataString(filePath));
        }

        return new UriBuilder(baseUri)
        {
            Path = baseUri.AbsolutePath[..^OpenAiProtocolSuffix.Length] + relativePath,
            Query = query.ToString(),
        }.Uri;
    }

    [System.Text.RegularExpressions.GeneratedRegex("^[A-Za-z0-9_-]{1,200}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex SessionIdPattern();

    private sealed class ResponseOwnedStream(Stream inner, HttpResponseMessage response) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            inner.ReadAsync(buffer, offset, count, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    inner.Dispose();
                }
                finally
                {
                    response.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            try
            {
                await inner.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                response.Dispose();
                GC.SuppressFinalize(this);
                await base.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
