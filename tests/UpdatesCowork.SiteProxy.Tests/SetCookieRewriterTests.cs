using Xunit;

namespace UpdatesCowork.SiteProxy.Tests;

public sealed class SetCookieRewriterTests
{
    [Theory]
    [InlineData("a=b; Domain=.microsoft.com; Path=/; Secure; HttpOnly", "a=b; Path=/; Secure; HttpOnly")]
    [InlineData("a=b; domain=azure.microsoft.com", "a=b")]
    [InlineData("a=b;Path=/;DOMAIN = .microsoft.com ;SameSite=Lax", "a=b;Path=/;SameSite=Lax")]
    [InlineData("a=b; Expires=Wed, 21 Oct 2026 07:28:00 GMT; Domain=.microsoft.com", "a=b; Expires=Wed, 21 Oct 2026 07:28:00 GMT")]
    [InlineData("a=b; Path=/", "a=b; Path=/")]
    [InlineData("a=b", "a=b")]
    [InlineData("Domain=keep; Path=/", "Domain=keep; Path=/")]
    [InlineData("a=b; DomainX=1", "a=b; DomainX=1")]
    public void RemoveDomain_RemovesOnlyDomainAttribute(string input, string expected)
    {
        Assert.Equal(expected, Transforms.SetCookieRewriter.RemoveDomain(input));
    }
}
