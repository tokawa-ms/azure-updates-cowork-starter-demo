using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace UpdatesCowork.SiteProxy.Transforms;

/// <summary>上流の応答ヘッダーを、iframe での表示と siteproxy の中にとどめるために書き換える(設計書5.1節)。</summary>
internal sealed class ResponseHeaderRewriter
{
    internal const string DataApiProxyPrefix = "/__rc/";

    // プロキシのオリジンから上流の報告先へ送らせないために外すヘッダー。
    private static readonly string[] RemovedHeaders =
    [
        HeaderNames.XFrameOptions,
        HeaderNames.ContentSecurityPolicyReportOnly,
        "Report-To",
        "Reporting-Endpoints",
        "NEL",
    ];

    private readonly string _contentSecurityPolicy;
    private readonly string _upstreamOrigin;
    private readonly string _upstreamProtocolRelative;
    private readonly string? _dataApiPrefix;

    public ResponseHeaderRewriter(SiteProxyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _contentSecurityPolicy = string.Join(' ', ["frame-ancestors", "'self'", .. options.NormalizedParentOrigins]);
        _upstreamOrigin = options.NormalizedUpstreamOrigin;
        _upstreamProtocolRelative = _upstreamOrigin[_upstreamOrigin.IndexOf("//", StringComparison.Ordinal)..];
        _dataApiPrefix = options.ProxyDataApi ? options.NormalizedDataApiOrigin + options.DataApiPathPrefix : null;
    }

    /// <summary>付け直す <c>Content-Security-Policy</c> の値。</summary>
    public string ContentSecurityPolicy => _contentSecurityPolicy;

    public void Apply(IHeaderDictionary headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        foreach (var name in RemovedHeaders)
        {
            headers.Remove(name);
        }

        headers.ContentSecurityPolicy = _contentSecurityPolicy;

        var location = headers.Location;
        if (location.Count == 1 && location[0] is { } value)
        {
            headers.Location = RewriteLocation(value);
        }

        var cookies = headers.SetCookie;
        if (cookies.Count > 0)
        {
            var rewritten = new string?[cookies.Count];
            for (var i = 0; i < cookies.Count; i++)
            {
                rewritten[i] = cookies[i] is { } cookie ? SetCookieRewriter.RemoveDomain(cookie) : null;
            }

            headers.SetCookie = rewritten;
        }
    }

    /// <summary>上流(とデータ API)のオリジンで始まる <c>Location</c> を siteproxy のパスにする。それ以外はそのまま返す。</summary>
    public string RewriteLocation(string location)
    {
        ArgumentNullException.ThrowIfNull(location);

        if (_dataApiPrefix is not null && location.StartsWith(_dataApiPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return DataApiProxyPrefix + location[_dataApiPrefix.Length..];
        }

        if (TryStripOrigin(location, _upstreamOrigin, out var path) || TryStripOrigin(location, _upstreamProtocolRelative, out path))
        {
            // "//別のホスト" になるとプロトコル相対の URL として外へ出てしまうので、書き換えない。
            return path.StartsWith("//", StringComparison.Ordinal) || path.StartsWith("/\\", StringComparison.Ordinal)
                ? location
                : path;
        }

        return location;
    }

    private static bool TryStripOrigin(string location, string origin, out string path)
    {
        path = string.Empty;
        if (!location.StartsWith(origin, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = location[origin.Length..];
        if (rest.Length == 0)
        {
            path = "/";
            return true;
        }

        switch (rest[0])
        {
            case '/':
                path = rest;
                return true;
            case '?':
            case '#':
                path = "/" + rest;
                return true;
            default:
                // "https://azure.microsoft.com.example.net" のような別ホストは対象外。
                return false;
        }
    }
}
