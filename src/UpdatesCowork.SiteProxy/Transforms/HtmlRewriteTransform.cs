using System.Buffers;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using UpdatesCowork.Shared.Telemetry;
using Yarp.ReverseProxy.Transforms;

namespace UpdatesCowork.SiteProxy.Transforms;

/// <summary>
/// 上流の HTML を書き換える(設計書5.1節「HTMLの書き換え」)。
/// 絶対 URL を siteproxy のパスにし、<c>bridge.js</c> のタグを差し込む。
/// </summary>
internal sealed class HtmlRewriteTransform
{
    /// <summary>書き換える本文の上限。超えたら書き換えずに中継する。</summary>
    public const int MaxBodyBytes = 5 * 1024 * 1024;

    public const string BridgeScriptTag = "<script src=\"/__auc/bridge.js\" defer></script>";

    private const int CopyBufferSize = 81920;

    private readonly bool _injectBridge;
    private readonly Regex? _absoluteUrlPattern;

    public HtmlRewriteTransform(SiteProxyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _injectBridge = options.InjectBridge;
        if (options.RewriteAbsoluteUrls)
        {
            var authority = new Uri(options.NormalizedUpstreamOrigin).Authority;

            // "https://azure.microsoft.com/" と "//azure.microsoft.com/" を "/" にする。
            // "http://azure.microsoft.com/" も同じく置き換え、"http:/" のような壊れた URL を残さない。
            _absoluteUrlPattern = new Regex(
                $"(?:https?:)?//{Regex.Escape(authority)}/",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(2));
        }
    }

    /// <summary>書き換えることが1つでもあるか。</summary>
    public bool IsEnabled => _injectBridge || _absoluteUrlPattern is not null;

    public async ValueTask ApplyAsync(ResponseTransformContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var proxyResponse = context.ProxyResponse;
        if (!IsEnabled
            || proxyResponse is null
            || HttpMethods.IsHead(context.HttpContext.Request.Method)
            || proxyResponse.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotModified)
        {
            return;
        }

        var contentHeaders = proxyResponse.Content.Headers;
        var contentType = contentHeaders.ContentType;
        if (!string.Equals(contentType?.MediaType, "text/html", StringComparison.OrdinalIgnoreCase)
            || contentHeaders.ContentEncoding.Any(e => !e.Equals("identity", StringComparison.OrdinalIgnoreCase))
            || contentHeaders.ContentLength > MaxBodyBytes)
        {
            return;
        }

        var encoding = ResolveEncoding(contentType!.CharSet);
        if (encoding is null)
        {
            return;
        }

        using var activity = SiteProxyTelemetry.Source.StartActivity(TelemetryNames.Spans.ProxyHtmlRewrite);
        activity?.SetTag(TelemetryNames.Attributes.UrlPath, context.HttpContext.Request.Path.Value);

        var cancellationToken = context.CancellationToken;
        var response = context.HttpContext.Response;

        // 本文はこの変換で書き出すので、YARP には書かせない。長さが変わるので Content-Length も外す。
        context.SuppressResponseBody = true;
        response.Headers.ContentLength = null;

        var upstream = await proxyResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (upstream.ConfigureAwait(false))
        {
            var (body, complete) = await ReadUpToAsync(upstream, MaxBodyBytes, cancellationToken).ConfigureAwait(false);
            if (!complete)
            {
                // Content-Length のない 5 MB 超えの本文は、読んだ分と残りをそのまま流す。
                await response.Body.WriteAsync(body, cancellationToken).ConfigureAwait(false);
                var rest = await CopyAsync(upstream, response.Body, cancellationToken).ConfigureAwait(false);
                activity?.SetTag(TelemetryNames.Attributes.BytesIn, body.Length + rest);
                activity?.SetTag(TelemetryNames.Attributes.BytesOut, body.Length + rest);
                activity?.SetTag(TelemetryNames.Attributes.BridgeInjected, false);
                return;
            }

            var (html, bridgeInjected) = Rewrite(encoding.GetString(body));
            var output = encoding.GetBytes(html);
            await response.Body.WriteAsync(output, cancellationToken).ConfigureAwait(false);

            activity?.SetTag(TelemetryNames.Attributes.BytesIn, (long)body.Length);
            activity?.SetTag(TelemetryNames.Attributes.BytesOut, (long)output.Length);
            activity?.SetTag(TelemetryNames.Attributes.BridgeInjected, bridgeInjected);
        }
    }

    /// <summary>HTML の文字列を書き換える。<c>bridge.js</c> を差し込めたかも返す。</summary>
    public (string Html, bool BridgeInjected) Rewrite(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        if (_absoluteUrlPattern is not null)
        {
            html = _absoluteUrlPattern.Replace(html, "/");
        }

        if (!_injectBridge)
        {
            return (html, false);
        }

        var index = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            index = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        }

        return index < 0 ? (html, false) : (html.Insert(index, BridgeScriptTag), true);
    }

    /// <summary><c>Content-Type</c> の charset から文字コードを決める。なければ UTF-8。知らない charset なら null。</summary>
    internal static Encoding? ResolveEncoding(string? charset)
    {
        var name = charset?.Trim().Trim('"', '\'');
        if (string.IsNullOrEmpty(name))
        {
            return Encoding.UTF8;
        }

        try
        {
            return Encoding.GetEncoding(name);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static async Task<(byte[] Body, bool Complete)> ReadUpToAsync(Stream stream, int limit, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        try
        {
            int read;
            while ((read = await stream.ReadAsync(chunk.AsMemory(0, CopyBufferSize), cancellationToken).ConfigureAwait(false)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > limit)
                {
                    return (buffer.ToArray(), false);
                }
            }

            return (buffer.ToArray(), true);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }
    }

    private static async Task<long> CopyAsync(Stream source, Stream destination, CancellationToken cancellationToken)
    {
        var chunk = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        try
        {
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(chunk.AsMemory(0, CopyBufferSize), cancellationToken).ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                total += read;
            }

            return total;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }
    }
}
