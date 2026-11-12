using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace UpdatesCowork.AzureUpdates.Tests;

public sealed class AzureUpdatesClientTests
{
    private static readonly string FixturesDir = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static string ListJson => File.ReadAllText(Path.Combine(FixturesDir, "list.json"));

    private static string ItemJson => File.ReadAllText(Path.Combine(FixturesDir, "100001.json"));

    private static AzureUpdatesClient CreateClient(FakeHttpMessageHandler handler, IMemoryCache? cache = null) =>
        new(
            new HttpClient(handler),
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            new AzureUpdatesOptions(),
            NullLogger<AzureUpdatesClient>.Instance);

    [Fact]
    public async Task GetAsync_RequestsItemUrlAndParsesFields()
    {
        var handler = FakeHttpMessageHandler.Json(ItemJson);
        var client = CreateClient(handler);

        var item = await client.GetAsync("100001", TestContext.Current.CancellationToken);

        Assert.NotNull(item);
        Assert.Equal("Generally available: Azure Container Apps express", item.Title);
        Assert.Equal("Launched", item.Status);
        Assert.Equal(["Azure Container Apps"], item.Products);
        Assert.Equal("2026-11", item.GeneralAvailabilityDate);
        Assert.Equal(new DateTimeOffset(2026, 11, 10, 9, 30, 0, TimeSpan.Zero), item.Modified);
        var availability = Assert.Single(item.Availabilities!);
        Assert.Equal(2026, availability.Year);
        Assert.Equal("November", availability.Month);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://www.microsoft.com/releasecommunications/api/v2/azure/100001", request.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task GetAsync_UsesCacheOnSecondCall()
    {
        var handler = FakeHttpMessageHandler.Json(ItemJson);
        var client = CreateClient(handler);

        await client.GetAsync("100001", TestContext.Current.CancellationToken);
        var second = await client.GetAsync("100001", TestContext.Current.CancellationToken);

        Assert.NotNull(second);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetAsync_NotFound_ReturnsNull()
    {
        var handler = FakeHttpMessageHandler.Json("{}", HttpStatusCode.NotFound);
        var client = CreateClient(handler);

        Assert.Null(await client.GetAsync("999999", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetAsync_MismatchedId_ReturnsNull()
    {
        var handler = FakeHttpMessageHandler.Json(ItemJson);
        var client = CreateClient(handler);

        Assert.Null(await client.GetAsync("100002", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("../list")]
    [InlineData("1 2")]
    [InlineData("")]
    public async Task GetAsync_InvalidId_ThrowsWithoutRequest(string id)
    {
        var handler = FakeHttpMessageHandler.Json(ItemJson);
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<ArgumentException>(() => client.GetAsync(id, TestContext.Current.CancellationToken));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetAsync_ServerError_ThrowsUnavailable()
    {
        var handler = FakeHttpMessageHandler.Json("oops", HttpStatusCode.InternalServerError);
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<AzureUpdatesUnavailableException>(() => client.GetAsync("100001", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetAsync_InvalidJson_ThrowsUnavailable()
    {
        var handler = FakeHttpMessageHandler.Json("{ not json");
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<AzureUpdatesUnavailableException>(() => client.GetAsync("100001", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SearchAsync_WithoutKeyword_RequestsTopAndFilter()
    {
        var handler = FakeHttpMessageHandler.Json(ListJson);
        var client = CreateClient(handler);

        var result = await client.SearchAsync(
            new AzureUpdateQuery { Status = "In preview", ModifiedSince = new DateOnly(2026, 11, 1), Top = 3 },
            TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(
            "https://www.microsoft.com/releasecommunications/api/v2/azure?%24count=true&%24top=3&%24orderby=modified%20desc&%24filter=status%20eq%20%27In%20preview%27%20and%20modified%20ge%202026-11-01",
            request.RequestUri!.OriginalString);
        Assert.Equal(3, result.Items.Count);
        Assert.Equal(4, result.Scanned);
        Assert.Equal(4, result.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_WithKeyword_ScansNewest200AndFiltersLocally()
    {
        var handler = FakeHttpMessageHandler.Json(ListJson);
        var client = CreateClient(handler);

        var result = await client.SearchAsync(new AzureUpdateQuery { Keyword = "CONTAINER apps", Top = 5 }, TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.Contains("%24top=200", request.RequestUri!.OriginalString, StringComparison.Ordinal);
        var item = Assert.Single(result.Items);
        Assert.Equal("100001", item.Id);
        Assert.Equal(4, result.Scanned);
    }

    [Fact]
    public async Task SearchAsync_ProductFilterMatchesProductNames()
    {
        var handler = FakeHttpMessageHandler.Json(ListJson);
        var client = CreateClient(handler);

        var result = await client.SearchAsync(new AzureUpdateQuery { Product = "functions" }, TestContext.Current.CancellationToken);

        Assert.Equal("100002", Assert.Single(result.Items).Id);
    }

    [Fact]
    public async Task SearchAsync_LenientlyReadsAvailabilityShapes()
    {
        var handler = FakeHttpMessageHandler.Json(ListJson);
        var client = CreateClient(handler);

        var result = await client.SearchAsync(new AzureUpdateQuery { Product = "functions" }, TestContext.Current.CancellationToken);

        var availability = Assert.Single(Assert.Single(result.Items).Availabilities!);
        Assert.Equal(2026, availability.Year);
        Assert.Equal("11", availability.Month);
    }

    [Fact]
    public async Task SearchAsync_UsesCacheForSameQuery()
    {
        var handler = FakeHttpMessageHandler.Json(ListJson);
        var client = CreateClient(handler);

        await client.SearchAsync(new AzureUpdateQuery { Keyword = "azure" }, TestContext.Current.CancellationToken);
        await client.SearchAsync(new AzureUpdateQuery { Keyword = "functions" }, TestContext.Current.CancellationToken);
        await client.SearchAsync(new AzureUpdateQuery { Top = 2 }, TestContext.Current.CancellationToken);

        // キーワードが違っても、データ API への問い合わせ(最新 200 件)は同じなのでキャッシュを使う
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task SearchAsync_InvalidQuery_ThrowsWithoutRequest()
    {
        var handler = FakeHttpMessageHandler.Json(ListJson);
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.SearchAsync(new AzureUpdateQuery { Top = 50 }, TestContext.Current.CancellationToken));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SearchAsync_ServerError_ThrowsUnavailable()
    {
        var handler = FakeHttpMessageHandler.Json("busy", HttpStatusCode.ServiceUnavailable);
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<AzureUpdatesUnavailableException>(() =>
            client.SearchAsync(new AzureUpdateQuery(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Options_FromConfiguration_ReadsBaseAndFixtures()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [AzureUpdatesOptions.ApiBaseKey] = "https://example.test/api/azure/",
                [AzureUpdatesOptions.FixturesDirectoryKey] = "fixtures",
            })
            .Build();

        var options = AzureUpdatesOptions.FromConfiguration(configuration);

        Assert.Equal("https://example.test/api/azure", options.ApiBase.AbsoluteUri);
        Assert.Equal(Path.GetFullPath("fixtures"), options.FixturesDirectory);
    }

    [Theory]
    [InlineData("http://example.test/api")]
    [InlineData("ftp://example.test/api")]
    [InlineData("not a url")]
    public void Options_FromConfiguration_RejectsUnsafeBase(string value)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [AzureUpdatesOptions.ApiBaseKey] = value })
            .Build();

        Assert.Throws<InvalidOperationException>(() => AzureUpdatesOptions.FromConfiguration(configuration));
    }
}
