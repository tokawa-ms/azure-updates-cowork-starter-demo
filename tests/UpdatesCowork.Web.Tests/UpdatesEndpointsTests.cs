using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UpdatesCowork.AzureUpdates;
using Xunit;

namespace UpdatesCowork.Web.Tests;

public sealed class UpdatesEndpointsTests
{
    [Fact]
    public async Task ViewerReturnsSelectionMetadataWithoutSendingHtmlInTheList()
    {
        await using var factory = new ViewerFactory();
        using var client = factory.CreateClient();
        var list = await client.GetFromJsonAsync<JsonElement>("/api/updates?keyword=SQL&status=Launched", TestContext.Current.CancellationToken);
        Assert.Equal("SQL", factory.Updates.LastQuery?.Keyword);
        Assert.Equal("Launched", factory.Updates.LastQuery?.Status);
        Assert.Equal(20, factory.Updates.LastQuery?.Top);
        var item = list.GetProperty("items")[0];
        Assert.Equal("559242", item.GetProperty("id").GetString());
        Assert.False(item.TryGetProperty("description", out _));
        var detail = await client.GetFromJsonAsync<JsonElement>("/api/updates/559242", TestContext.Current.CancellationToken);
        Assert.Equal("<p>公式の本文</p>", detail.GetProperty("html").GetString());
        Assert.Equal("https://azure.microsoft.com/updates?id=559242", detail.GetProperty("update").GetProperty("url").GetString());
        using var config = await client.GetAsync("/api/config", TestContext.Current.CancellationToken);
        Assert.Contains("frame-src 'none'", string.Join(";", config.Headers.GetValues("Content-Security-Policy")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/api/updates?status=unknown")]
    [InlineData("/api/updates?modifiedSince=tomorrow")]
    [InlineData("/api/updates?modifiedSince=1999-12-31")]
    [InlineData("/api/updates/bad%20id")]
    public async Task InvalidInputDoesNotCallTheDataClient(string path)
    {
        await using var factory = new ViewerFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, factory.Updates.Calls);
    }

    [Fact]
    public async Task MissingAndUnavailableArticlesAreNotSuccessfulEmptyResults()
    {
        await using var factory = new ViewerFactory();
        using var client = factory.CreateClient();
        using var missing = await client.GetAsync("/api/updates/missing", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        factory.Updates.Fail = true;
        using var response = await client.GetAsync("/api/updates", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("private upstream error", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    private sealed class ViewerFactory : WebApplicationFactory<Program>
    {
        public StubUpdates Updates { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAzureUpdatesClient>();
                services.AddSingleton<IAzureUpdatesClient>(Updates);
            });
    }

    private sealed class StubUpdates : IAzureUpdatesClient
    {
        private readonly AzureUpdate _item = new()
        {
            Id = "559242", Title = "SQL の更新情報", Description = "<p>公式の本文</p>",
            Status = "Launched", Products = ["SQL"], Locale = "en-us",
        };
        public AzureUpdateQuery? LastQuery { get; private set; }
        public int Calls { get; private set; }
        public bool Fail { get; set; }

        public Task<AzureUpdate?> GetAsync(string id, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(id == _item.Id ? _item : null);
        }

        public Task<AzureUpdateSearchResult> SearchAsync(AzureUpdateQuery query, CancellationToken ct)
        {
            Calls++;
            LastQuery = query;
            if (Fail) throw new AzureUpdatesUnavailableException("private upstream error");
            return Task.FromResult(new AzureUpdateSearchResult([_item], 1, 1));
        }
    }
}
