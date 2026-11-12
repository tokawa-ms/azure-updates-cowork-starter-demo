using UpdatesCowork.SiteProxy.Transforms;
using Xunit;

namespace UpdatesCowork.SiteProxy.Tests;

public sealed class HtmlRewriteTransformTests
{
    private const string Tag = HtmlRewriteTransform.BridgeScriptTag;

    [Fact]
    public void Rewrite_ReplacesAbsoluteUrlsOfUpstreamOnly()
    {
        var transform = new HtmlRewriteTransform(TestOptions.Create(o => o.InjectBridge = false));
        const string html = """
            <a href="https://azure.microsoft.com/ja-jp/updates/?id=1">a</a>
            <img src="//azure.microsoft.com/images/x.png">
            <a href="http://azure.microsoft.com/en-us/">c</a>
            <a href="HTTPS://Azure.Microsoft.com/en-us/">d</a>
            <a href="https://learn.microsoft.com/ja-jp/azure/">e</a>
            <a href="https://azure.microsoft.com.example.net/">f</a>
            <a href="https://azure.microsoft.com">g</a>
            """;

        var (result, injected) = transform.Rewrite(html);

        Assert.False(injected);
        Assert.Equal("""
            <a href="/ja-jp/updates/?id=1">a</a>
            <img src="/images/x.png">
            <a href="/en-us/">c</a>
            <a href="/en-us/">d</a>
            <a href="https://learn.microsoft.com/ja-jp/azure/">e</a>
            <a href="https://azure.microsoft.com.example.net/">f</a>
            <a href="https://azure.microsoft.com">g</a>
            """, result);
    }

    [Fact]
    public void Rewrite_InjectsBridgeBeforeFirstHeadClose()
    {
        var transform = new HtmlRewriteTransform(TestOptions.Create());

        var (result, injected) = transform.Rewrite("<html><HEAD><title>t</title></HEAD><body><template></head></template></body></html>");

        Assert.True(injected);
        Assert.Equal($"<html><HEAD><title>t</title>{Tag}</HEAD><body><template></head></template></body></html>", result);
    }

    [Fact]
    public void Rewrite_InjectsBridgeBeforeBodyClose_WhenNoHead()
    {
        var transform = new HtmlRewriteTransform(TestOptions.Create());

        var (result, injected) = transform.Rewrite("<body><p>x</p></BODY>");

        Assert.True(injected);
        Assert.Equal($"<body><p>x</p>{Tag}</BODY>", result);
    }

    [Fact]
    public void Rewrite_DoesNotInject_WhenNoHeadOrBody()
    {
        var transform = new HtmlRewriteTransform(TestOptions.Create());

        var (result, injected) = transform.Rewrite("<p>fragment</p>");

        Assert.False(injected);
        Assert.Equal("<p>fragment</p>", result);
    }

    [Fact]
    public void Rewrite_KeepsAbsoluteUrls_WhenRewriteIsDisabled()
    {
        var transform = new HtmlRewriteTransform(TestOptions.Create(o => o.RewriteAbsoluteUrls = false));

        var (result, injected) = transform.Rewrite("<head></head><a href=\"https://azure.microsoft.com/x\">");

        Assert.True(injected);
        Assert.Equal($"<head>{Tag}</head><a href=\"https://azure.microsoft.com/x\">", result);
    }

    [Theory]
    [InlineData(null, "utf-8")]
    [InlineData("", "utf-8")]
    [InlineData("UTF-8", "utf-8")]
    [InlineData("\"utf-8\"", "utf-8")]
    [InlineData("iso-8859-1", "iso-8859-1")]
    [InlineData("utf-16", "utf-16")]
    public void ResolveEncoding_UsesCharsetOrUtf8(string? charset, string expected)
    {
        Assert.Equal(expected, HtmlRewriteTransform.ResolveEncoding(charset)?.WebName);
    }

    [Fact]
    public void ResolveEncoding_ReturnsNull_ForUnknownCharset()
    {
        Assert.Null(HtmlRewriteTransform.ResolveEncoding("x-unknown-charset"));
    }
}
