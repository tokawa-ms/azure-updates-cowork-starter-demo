namespace UpdatesCowork.SiteProxy.Tests;

internal static class TestOptions
{
    public static SiteProxyOptions Create(Action<SiteProxyOptions>? configure = null)
    {
        var options = new SiteProxyOptions();
        options.AllowedParentOrigins.Add("https://localhost:7101");
        configure?.Invoke(options);
        return options;
    }
}
