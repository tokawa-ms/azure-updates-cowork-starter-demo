using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using UpdatesCowork.Shared.Telemetry;
using UpdatesCowork.SiteProxy.Transforms;
using Xunit;

namespace UpdatesCowork.SiteProxy.Tests;

public sealed class SiteProxyAppTests
{
    private const string Csp = "frame-ancestors 'self' https://localhost:7101";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static HttpResponseMessage Html(string html, string contentType = "text/html; charset=utf-8")
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(html)) };
        response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return response;
    }

    [Fact]
    public async Task Healthz_ReturnsOk_WithoutCallingUpstream()
    {
        await using var factory = new SiteProxyFactory();
        using var client = factory.CreateProxyClient();

        using var response = await client.GetAsync("/healthz", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("noindex, nofollow", response.Headers.GetValues("X-Robots-Tag").Single());
        Assert.Empty(factory.Upstream.Requests);
    }

    [Fact]
    public async Task RobotsTxt_DisallowsAll()
    {
        await using var factory = new SiteProxyFactory();
        using var client = factory.CreateProxyClient();

        using var response = await client.GetAsync("/robots.txt", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("User-agent: *\nDisallow: /\n", await response.Content.ReadAsStringAsync(Ct));
        Assert.Empty(factory.Upstream.Requests);
    }

    [Fact]
    public async Task BridgeJs_StartsWithAllowedOrigins_AndIsNotCached()
    {
        await using var factory = new SiteProxyFactory(new Dictionary<string, string?>
        {
            ["SiteProxy:AllowedParentOrigins:1"] = "https://web.example.net/",
        });
        using var client = factory.CreateProxyClient();

        using var response = await client.GetAsync("/__auc/bridge.js", Ct);
        var script = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoCache);
        Assert.StartsWith("const AUC_PARENT_ORIGINS = [\"https://localhost:7101\", \"https://web.example.net\"];", script, StringComparison.Ordinal);
        Assert.Contains("const AUC_UPSTREAM_ORIGIN = \"https://azure.microsoft.com\";", script, StringComparison.Ordinal);
        Assert.Contains("const AUC_PROXY_DATA_API = true;", script, StringComparison.Ordinal);
        Assert.Contains("(function () {", script, StringComparison.Ordinal);
        Assert.DoesNotContain("postMessage(page, \"*\")", script, StringComparison.Ordinal);
        Assert.Empty(factory.Upstream.Requests);
    }

    [Theory]
    [InlineData("POST", "/ja-jp/updates/")]
    [InlineData("PUT", "/ja-jp/updates/")]
    [InlineData("DELETE", "/")]
    [InlineData("POST", "/healthz")]
    public async Task NonReadOnlyMethods_Return405_WithoutCallingUpstream(string method, string path)
    {
        await using var factory = new SiteProxyFactory();
        using var client = factory.CreateProxyClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = new StringContent("x") };

        using var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Empty(factory.Upstream.Requests);
    }

    [Fact]
    public async Task Get_ForwardsToUpstream_WithoutAcceptEncodingOrForwardedHeaders()
    {
        await using var factory = new SiteProxyFactory();
        using var client = factory.CreateProxyClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/ja-jp/updates/?id=559242");
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, br");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.10");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Host", "siteproxy.example.net");
        request.Headers.TryAddWithoutValidation("Forwarded", "for=203.0.113.10");
        request.Headers.TryAddWithoutValidation("Accept-Language", "ja");

        using var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var upstream = Assert.Single(factory.Upstream.Requests);
        Assert.Equal(HttpMethod.Get, upstream.Method);
        Assert.Equal(new Uri("https://azure.microsoft.com/ja-jp/updates/?id=559242"), upstream.Uri);
        Assert.False(upstream.HasHeader("Accept-Encoding"));
        Assert.False(upstream.HasHeader("X-Forwarded-For"));
        Assert.False(upstream.HasHeader("X-Forwarded-Proto"));
        Assert.False(upstream.HasHeader("X-Forwarded-Host"));
        Assert.False(upstream.HasHeader("X-Forwarded-Prefix"));
        Assert.False(upstream.HasHeader("Forwarded"));
        Assert.True(!upstream.HasHeader("Host") || upstream.Headers["Host"].Single() == "azure.microsoft.com");
        Assert.Equal("ja", upstream.Headers["Accept-Language"].Single());
    }

    [Fact]
    public async Task Head_IsForwarded()
    {
        await using var factory = new SiteProxyFactory();
        factory.Upstream.Respond = _ => Html("<html><head></head></html>");
        using var client = factory.CreateProxyClient();
        using var request = new HttpRequestMessage(HttpMethod.Head, "/ja-jp/updates/");

        using var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpMethod.Head, Assert.Single(factory.Upstream.Requests).Method);
    }

    [Fact]
    public async Task HtmlResponse_HeadersAreRewritten_AndBridgeIsInjected()
    {
        await using var factory = new SiteProxyFactory();
        factory.Upstream.Respond = _ =>
        {
            var response = Html("""
                <!doctype html><html><head><title>Azure</title><link href="https://azure.microsoft.com/css/a.css"></head>
                <body><a href="//azure.microsoft.com/ja-jp/updates/?id=1">x</a></body></html>
                """);
            response.Headers.TryAddWithoutValidation("X-Frame-Options", "SAMEORIGIN");
            response.Headers.TryAddWithoutValidation("Content-Security-Policy", "frame-ancestors 'self'; upgrade-insecure-requests");
            response.Headers.TryAddWithoutValidation("Content-Security-Policy-Report-Only", "default-src 'none'");
            response.Headers.TryAddWithoutValidation("Report-To", "{\"group\":\"g\"}");
            response.Headers.TryAddWithoutValidation("NEL", "{\"report_to\":\"g\"}");
            response.Headers.TryAddWithoutValidation("X-Robots-Tag", "all");
            response.Headers.TryAddWithoutValidation("Set-Cookie", "s=1; Domain=.microsoft.com; Path=/; Secure");
            response.Headers.TryAddWithoutValidation("Set-Cookie", "t=2; path=/; domain=azure.microsoft.com");
            return response;
        };
        using var client = factory.CreateProxyClient();

        using var response = await client.GetAsync("/ja-jp/updates/", Ct);
        var bytes = await response.Content.ReadAsByteArrayAsync(Ct);
        var body = Encoding.UTF8.GetString(bytes);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("X-Frame-Options"));
        Assert.Equal(Csp, response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.False(response.Headers.Contains("Content-Security-Policy-Report-Only"));
        Assert.False(response.Headers.Contains("Report-To"));
        Assert.False(response.Headers.Contains("NEL"));
        Assert.Equal("noindex, nofollow", response.Headers.GetValues("X-Robots-Tag").Single());
        Assert.Equal(["s=1; Path=/; Secure", "t=2; path=/"], response.Headers.GetValues("Set-Cookie").ToArray());

        Assert.Contains($"<link href=\"/css/a.css\">{HtmlRewriteTransform.BridgeScriptTag}</head>", body, StringComparison.Ordinal);
        Assert.Contains("<a href=\"/ja-jp/updates/?id=1\">", body, StringComparison.Ordinal);
        Assert.DoesNotContain("azure.microsoft.com", body, StringComparison.Ordinal);
        Assert.True(response.Content.Headers.ContentLength is null || response.Content.Headers.ContentLength == bytes.Length);
    }

    [Fact]
    public async Task HtmlResponse_WithoutHead_GetsBridgeBeforeBodyClose()
    {
        await using var factory = new SiteProxyFactory();
        factory.Upstream.Respond = _ => Html("<body><p>x</p></body>");
        using var client = factory.CreateProxyClient();

        var body = await client.GetStringAsync("/ja-jp/updates/", Ct);

        Assert.Equal($"<body><p>x</p>{HtmlRewriteTransform.BridgeScriptTag}</body>", body);
    }

    [Fact]
    public async Task Redirect_LocationIsRewrittenToPath()
    {
        await using var factory = new SiteProxyFactory();
        factory.Upstream.Respond = _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.MovedPermanently);
            response.Headers.Location = new Uri("https://azure.microsoft.com/ja-jp/updates/?id=559242");
            return response;
        };
        using var client = factory.CreateProxyClient();

        using var response = await client.GetAsync("/ja-jp/updates?id=559242", Ct);

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/ja-jp/updates/?id=559242", response.Headers.Location?.OriginalString);
        Assert.Equal("noindex, nofollow", response.Headers.GetValues("X-Robots-Tag").Single());
    }

    [Fact]
    public async Task NonHtmlResponse_IsPassedThrough()
    {
        const string json = "{\"url\":\"https://azure.microsoft.com/ja-jp/\"}";
        await using var factory = new SiteProxyFactory();
        factory.Upstream.Respond = _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            response.Headers.TryAddWithoutValidation("X-Frame-Options", "DENY");
            return response;
        };
        using var client = factory.CreateProxyClient();

        using var response = await client.GetAsync("/ja-jp/data.json", Ct);

        Assert.Equal(json, await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(Encoding.UTF8.GetByteCount(json), response.Content.Headers.ContentLength);
        Assert.False(response.Headers.Contains("X-Frame-Options"));
        Assert.Equal(Csp, response.Headers.GetValues("Content-Security-Policy").Single());
    }

    public static TheoryData<bool> OversizedCases => new() { true, false };

    [Theory]
    [MemberData(nameof(OversizedCases))]
    public async Task HtmlOver5MB_IsNotRewritten(bool withContentLength)
    {
        var html = "<html><head></head><body><a href=\"https://azure.microsoft.com/x\"></a>"
            + new string('a', HtmlRewriteTransform.MaxBodyBytes) + "</body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        await using var factory = new SiteProxyFactory();
        factory.Upstream.Respond = _ =>
        {
            HttpContent content = withContentLength
                ? new ByteArrayContent(bytes)
                : new StreamContent(new UnknownLengthStream(bytes));
            content.Headers.ContentType = MediaTypeHeaderValue.Parse("text/html; charset=utf-8");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        };
        using var client = factory.CreateProxyClient();

        using var response = await client.GetAsync("/ja-jp/updates/", Ct);
        var received = await response.Content.ReadAsByteArrayAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(bytes.Length, received.Length);
        Assert.True(bytes.AsSpan().SequenceEqual(received));
    }

    [Fact]
    public async Task HtmlResponse_KeepsCharsetOfContentType()
    {
        var latin1 = Encoding.Latin1;
        await using var factory = new SiteProxyFactory();
        factory.Upstream.Respond = _ =>
        {
            var content = new ByteArrayContent(latin1.GetBytes("<head><title>caf\u00e9</title></head>"));
            content.Headers.ContentType = MediaTypeHeaderValue.Parse("text/html; charset=iso-8859-1");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        };
        using var client = factory.CreateProxyClient();

        using var response = await client.GetAsync("/ja-jp/updates/", Ct);
        var bytes = await response.Content.ReadAsByteArrayAsync(Ct);

        Assert.Equal("iso-8859-1", response.Content.Headers.ContentType?.CharSet);
        Assert.Equal(latin1.GetBytes($"<head><title>caf\u00e9</title>{HtmlRewriteTransform.BridgeScriptTag}</head>"), bytes);
    }

    [Fact]
    public async Task HtmlResponse_WithUnknownCharset_IsPassedThrough()
    {
        await using var factory = new SiteProxyFactory();
        factory.Upstream.Respond = _ => Html("<head></head>", "text/html; charset=x-unknown-charset");
        using var client = factory.CreateProxyClient();

        using var response = await client.GetAsync("/ja-jp/updates/", Ct);
        var bytes = await response.Content.ReadAsByteArrayAsync(Ct);

        Assert.Equal("x-unknown-charset", response.Content.Headers.ContentType?.CharSet);
        Assert.Equal(Encoding.UTF8.GetBytes("<head></head>"), bytes);
    }

    [Fact]
    public async Task HtmlRewrite_EmitsSpanWithAttributes()
    {
        var stopped = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == TelemetryNames.Sources.SiteProxy,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);

        await using var factory = new SiteProxyFactory();
        factory.Upstream.Respond = _ => Html("<head></head>");
        using var client = factory.CreateProxyClient();

        await client.GetStringAsync("/ja-jp/updates/span-test", Ct);

        var span = Assert.Single(stopped, a => a.OperationName == TelemetryNames.Spans.ProxyHtmlRewrite
            && (string?)a.GetTagItem(TelemetryNames.Attributes.UrlPath) == "/ja-jp/updates/span-test");
        Assert.Equal(13L, span.GetTagItem(TelemetryNames.Attributes.BytesIn));
        Assert.Equal(13L + HtmlRewriteTransform.BridgeScriptTag.Length, span.GetTagItem(TelemetryNames.Attributes.BytesOut));
        Assert.Equal(true, span.GetTagItem(TelemetryNames.Attributes.BridgeInjected));
    }

    [Theory]
    [InlineData("/__rc/api/v2/azure")]
    [InlineData("/__auc/other.js")]
    public async Task ProxyInternalPaths_Return404_WhenNotEnabled(string path)
    {
        await using var factory = new SiteProxyFactory(new Dictionary<string, string?> { ["SiteProxy:ProxyDataApi"] = "false" });
        using var client = factory.CreateProxyClient();

        using var response = await client.GetAsync(path, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(factory.Upstream.Requests);
    }

    [Fact]
    public async Task DataApi_IsForwardedToFixedOriginAndPrefix_ByDefault()
    {
        await using var factory = new SiteProxyFactory();
        factory.Upstream.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html><head></head></html>", Encoding.UTF8, "text/html"),
        };
        using var client = factory.CreateProxyClient();

        using var response = await client.GetAsync("/__rc/api/v2/azure/559242?includeFacets=true", Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        var script = await client.GetStringAsync("/__auc/bridge.js", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var upstream = Assert.Single(factory.Upstream.Requests);
        Assert.Equal(new Uri("https://www.microsoft.com/releasecommunications/api/v2/azure/559242?includeFacets=true"), upstream.Uri);
        Assert.False(upstream.HasHeader("Accept-Encoding"));
        Assert.Equal("<html><head></head></html>", body);
        Assert.Contains("const AUC_PROXY_DATA_API = true;", script, StringComparison.Ordinal);
        Assert.Contains("const AUC_DATA_API_PREFIX = \"https://www.microsoft.com/releasecommunications/\";", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DataApi_RejectsNonReadOnlyMethods()
    {
        await using var factory = new SiteProxyFactory(new Dictionary<string, string?> { ["SiteProxy:ProxyDataApi"] = "true" });
        using var client = factory.CreateProxyClient();

        using var response = await client.PostAsync("/__rc/api/v2/azure", new StringContent("{}"), Ct);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Empty(factory.Upstream.Requests);
    }

    [Fact]
    public async Task RateLimit_IsAppliedPerClientIp()
    {
        await using var factory = new SiteProxyFactory(new Dictionary<string, string?> { ["SiteProxy:RateLimitPerMinute"] = "2" });
        using var client = factory.CreateProxyClient();

        async Task<HttpStatusCode> GetAsync(string forwardedFor)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/robots.txt");
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwardedFor);
            using var response = await client.SendAsync(request, Ct);
            return response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.OK, await GetAsync("203.0.113.1"));
        Assert.Equal(HttpStatusCode.OK, await GetAsync("198.51.100.7, 203.0.113.1"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await GetAsync("203.0.113.1"));
        Assert.Equal(HttpStatusCode.OK, await GetAsync("203.0.113.2"));

        using var health = await client.GetAsync("/healthz", Ct);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task Startup_Fails_WhenUpstreamIsNotHttps()
    {
        await using var factory = new SiteProxyFactory(new Dictionary<string, string?> { ["SiteProxy:UpstreamOrigin"] = "http://azure.microsoft.com" });

        Assert.ThrowsAny<Exception>(() => factory.CreateProxyClient());
    }

    /// <summary>長さを知らせないストリーム(チャンク転送の上流を模す)。</summary>
    private sealed class UnknownLengthStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;

        public override long Length => throw new NotSupportedException();
    }
}
