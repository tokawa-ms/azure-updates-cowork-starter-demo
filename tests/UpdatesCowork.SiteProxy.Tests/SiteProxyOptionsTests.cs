using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace UpdatesCowork.SiteProxy.Tests;

public sealed class SiteProxyOptionsTests
{
    [Fact]
    public void Load_BindsAndNormalizesOrigins()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SiteProxy:UpstreamOrigin"] = "https://AZURE.microsoft.com/",
            ["SiteProxy:AllowedParentOrigins:0"] = "https://web.example.net:443/",
            ["SiteProxy:AllowedParentOrigins:1"] = "http://localhost:5101",
            ["SiteProxy:ProxyDataApi"] = "true",
        }).Build();

        var options = SiteProxyOptions.Load(configuration);

        Assert.Equal("https://azure.microsoft.com", options.NormalizedUpstreamOrigin);
        Assert.Equal(["https://web.example.net", "http://localhost:5101"], options.NormalizedParentOrigins);
        Assert.True(options.ProxyDataApi);
    }

    [Theory]
    [InlineData("SiteProxy:UpstreamOrigin", "http://azure.microsoft.com")]
    [InlineData("SiteProxy:UpstreamOrigin", "https://azure.microsoft.com/ja-jp/")]
    [InlineData("SiteProxy:UpstreamOrigin", "azure.microsoft.com")]
    [InlineData("SiteProxy:AllowedParentOrigins:0", "*")]
    [InlineData("SiteProxy:AllowedParentOrigins:0", "https://web.example.net/path")]
    [InlineData("SiteProxy:AllowedParentOrigins:0", "https://web.example.net?x=1")]
    [InlineData("SiteProxy:AllowedParentOrigins:0", "https://web.example.net; script-src *")]
    [InlineData("SiteProxy:AllowedParentOrigins:0", "javascript:alert(1)")]
    [InlineData("SiteProxy:AllowedParentOrigins:0", "https://user@web.example.net")]
    [InlineData("SiteProxy:DataApiPathPrefix", "releasecommunications")]
    [InlineData("SiteProxy:RateLimitPerMinute", "0")]
    public void Load_RejectsInvalidValues(string key, string value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SiteProxy:ProxyDataApi"] = "true",
            [key] = value,
        }).Build();

        Assert.Throws<OptionsValidationException>(() => SiteProxyOptions.Load(configuration));
    }
}
