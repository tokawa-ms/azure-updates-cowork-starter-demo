using System.Net;
using Microsoft.Extensions.DependencyInjection;

namespace UpdatesCowork.AzureUpdates.Tests;

public sealed class FixtureAzureUpdatesClientTests
{
    private static readonly string FixturesDir = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    [Fact]
    public async Task GetAsync_ReadsIdFile()
    {
        var client = new FixtureAzureUpdatesClient(FixturesDir);

        var item = await client.GetAsync("100001", TestContext.Current.CancellationToken);

        Assert.NotNull(item);
        Assert.Contains("<script>", item.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAsync_FallsBackToList()
    {
        var client = new FixtureAzureUpdatesClient(FixturesDir);

        var item = await client.GetAsync("100004", TestContext.Current.CancellationToken);

        Assert.NotNull(item);
        Assert.Equal("In development", item.Status);
    }

    [Fact]
    public async Task GetAsync_Unknown_ReturnsNull()
    {
        var client = new FixtureAzureUpdatesClient(FixturesDir);

        Assert.Null(await client.GetAsync("424242", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetAsync_InvalidId_Throws()
    {
        var client = new FixtureAzureUpdatesClient(FixturesDir);

        await Assert.ThrowsAsync<ArgumentException>(() => client.GetAsync("..\\list", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SearchAsync_ReturnsNewestFirst()
    {
        var client = new FixtureAzureUpdatesClient(FixturesDir);

        var result = await client.SearchAsync(new AzureUpdateQuery(), TestContext.Current.CancellationToken);

        Assert.Equal(["100001", "100002", "100003", "100004"], result.Items.Select(i => i.Id));
        Assert.Equal(4, result.Scanned);
    }

    [Theory]
    [InlineData("Launched", new[] { "100001", "100003" })]
    [InlineData("in preview", new[] { "100002" })]
    [InlineData("In development", new[] { "100004" })]
    [InlineData("Retirement", new[] { "100003" })]
    public async Task SearchAsync_FiltersByStatus(string status, string[] expected)
    {
        var client = new FixtureAzureUpdatesClient(FixturesDir);

        var result = await client.SearchAsync(new AzureUpdateQuery { Status = status }, TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task SearchAsync_FiltersByModifiedSinceKeywordAndTop()
    {
        var client = new FixtureAzureUpdatesClient(FixturesDir);

        var since = await client.SearchAsync(new AzureUpdateQuery { ModifiedSince = new DateOnly(2026, 11, 1) }, TestContext.Current.CancellationToken);
        var keyword = await client.SearchAsync(new AzureUpdateQuery { Keyword = "sql" }, TestContext.Current.CancellationToken);
        var top = await client.SearchAsync(new AzureUpdateQuery { Top = 1 }, TestContext.Current.CancellationToken);

        Assert.Equal(["100001", "100002"], since.Items.Select(i => i.Id));
        Assert.Equal(["100004"], keyword.Items.Select(i => i.Id));
        Assert.Equal(["100001"], top.Items.Select(i => i.Id));
    }

    [Fact]
    public void Constructor_MissingDirectory_Throws()
    {
        Assert.Throws<DirectoryNotFoundException>(() => new FixtureAzureUpdatesClient(Path.Combine(FixturesDir, "missing")));
    }

    [Fact]
    public void AddAzureUpdatesClient_WithFixtures_RegistersFixtureClient()
    {
        var services = new ServiceCollection();
        services.AddAzureUpdatesClient(new AzureUpdatesOptions { FixturesDirectory = FixturesDir });
        using var provider = services.BuildServiceProvider();

        Assert.IsType<FixtureAzureUpdatesClient>(provider.GetRequiredService<IAzureUpdatesClient>());
    }

    [Fact]
    public async Task AddAzureUpdatesClient_WithoutFixtures_UsesHttpWithUserAgent()
    {
        var handler = FakeHttpMessageHandler.Json(File.ReadAllText(Path.Combine(FixturesDir, "100001.json")));
        var services = new ServiceCollection();
        services.AddAzureUpdatesClient(new AzureUpdatesOptions());
        services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => handler));
        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IAzureUpdatesClient>();
        var item = await client.GetAsync("100001", TestContext.Current.CancellationToken);

        Assert.IsType<AzureUpdatesClient>(client);
        Assert.NotNull(item);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Contains(request.Headers.UserAgent, p => p.Product?.Name == "AzureUpdatesCowork" && p.Product.Version == "1.0");
    }

    [Fact]
    public async Task AddAzureUpdatesClient_NotFoundIsNotRetried()
    {
        var handler = FakeHttpMessageHandler.Json("{}", HttpStatusCode.NotFound);
        var services = new ServiceCollection();
        services.AddAzureUpdatesClient(new AzureUpdatesOptions());
        services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => handler));
        using var provider = services.BuildServiceProvider();

        var item = await provider.GetRequiredService<IAzureUpdatesClient>().GetAsync("100001", TestContext.Current.CancellationToken);

        Assert.Null(item);
        Assert.Single(handler.Requests);
    }
}
