using Microsoft.AspNetCore.Http;
using UpdatesCowork.SiteProxy.Transforms;
using Xunit;

namespace UpdatesCowork.SiteProxy.Tests;

public sealed class ResponseHeaderRewriterTests
{
    [Fact]
    public void Apply_RemovesFramingAndReportingHeaders_AndReplacesCsp()
    {
        var rewriter = new ResponseHeaderRewriter(TestOptions.Create(o => o.AllowedParentOrigins.Add("https://web.example.net/")));
        IHeaderDictionary headers = new HeaderDictionary
        {
            ["X-Frame-Options"] = "SAMEORIGIN",
            ["Content-Security-Policy"] = "default-src 'self'; frame-ancestors 'self'",
            ["Content-Security-Policy-Report-Only"] = "default-src 'none'",
            ["Report-To"] = "{\"group\":\"default\"}",
            ["Reporting-Endpoints"] = "default=\"https://example.com/r\"",
            ["NEL"] = "{\"report_to\":\"default\"}",
            ["Cache-Control"] = "max-age=60",
        };

        rewriter.Apply(headers);

        Assert.False(headers.ContainsKey("X-Frame-Options"));
        Assert.False(headers.ContainsKey("Content-Security-Policy-Report-Only"));
        Assert.False(headers.ContainsKey("Report-To"));
        Assert.False(headers.ContainsKey("Reporting-Endpoints"));
        Assert.False(headers.ContainsKey("NEL"));
        Assert.Equal("frame-ancestors 'self' https://localhost:7101 https://web.example.net", headers.ContentSecurityPolicy.ToString());
        Assert.Equal("max-age=60", headers.CacheControl.ToString());
    }

    [Fact]
    public void Apply_AddsCsp_WhenUpstreamHasNone()
    {
        IHeaderDictionary headers = new HeaderDictionary();

        new ResponseHeaderRewriter(TestOptions.Create()).Apply(headers);

        Assert.Equal("frame-ancestors 'self' https://localhost:7101", headers.ContentSecurityPolicy.ToString());
    }

    [Fact]
    public void Apply_RewritesLocationAndEverySetCookie()
    {
        IHeaderDictionary headers = new HeaderDictionary
        {
            ["Location"] = "https://azure.microsoft.com/ja-jp/updates/?id=559242",
            ["Set-Cookie"] = new(["a=1; Domain=.microsoft.com; Path=/", "b=2; Path=/; domain=azure.microsoft.com; Secure"]),
        };

        new ResponseHeaderRewriter(TestOptions.Create()).Apply(headers);

        Assert.Equal("/ja-jp/updates/?id=559242", headers.Location.ToString());
        Assert.Equal(["a=1; Path=/", "b=2; Path=/; Secure"], headers.SetCookie.ToArray());
    }

    [Theory]
    [InlineData("https://azure.microsoft.com/ja-jp/updates/", "/ja-jp/updates/")]
    [InlineData("https://AZURE.microsoft.com/en-us/updates?id=1", "/en-us/updates?id=1")]
    [InlineData("https://azure.microsoft.com", "/")]
    [InlineData("https://azure.microsoft.com?x=1", "/?x=1")]
    [InlineData("//azure.microsoft.com/ja-jp/", "/ja-jp/")]
    [InlineData("/ja-jp/updates/", "/ja-jp/updates/")]
    [InlineData("https://learn.microsoft.com/ja-jp/", "https://learn.microsoft.com/ja-jp/")]
    [InlineData("https://azure.microsoft.com.example.net/a", "https://azure.microsoft.com.example.net/a")]
    [InlineData("https://azure.microsoft.com//evil.example/a", "https://azure.microsoft.com//evil.example/a")]
    [InlineData("https://www.microsoft.com/releasecommunications/api/v2/azure", "https://www.microsoft.com/releasecommunications/api/v2/azure")]
    public void RewriteLocation_KeepsRedirectsInsideProxy(string location, string expected)
    {
        var rewriter = new ResponseHeaderRewriter(TestOptions.Create());

        Assert.Equal(expected, rewriter.RewriteLocation(location));
    }

    [Fact]
    public void RewriteLocation_MapsDataApi_WhenProxyDataApiIsEnabled()
    {
        var rewriter = new ResponseHeaderRewriter(TestOptions.Create(o => o.ProxyDataApi = true));

        Assert.Equal("/__rc/api/v2/azure?id=1", rewriter.RewriteLocation("https://www.microsoft.com/releasecommunications/api/v2/azure?id=1"));
        Assert.Equal("https://www.microsoft.com/en-us/", rewriter.RewriteLocation("https://www.microsoft.com/en-us/"));
    }
}
