using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Azure.Core;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using UpdatesCowork.Agent.Mcp;
using UpdatesCowork.Agent.Learning;
using UpdatesCowork.AzureUpdates;
using UpdatesCowork.Shared;
using UpdatesCowork.Shared.Contracts;
using UpdatesCowork.Shared.Telemetry;
using Xunit;

namespace UpdatesCowork.Agent.Tests;

public sealed class McpToolsTests
{
    [Fact]
    public async Task DiscoveryAndCallsUseMcpAndPreserveJsonAndErrorContracts()
    {
        await using var environment = await McpEnvironment.CreateAsync();
        var tools = environment.Client.CreateTools(requireApproval: true, () => null);
        Assert.Equal(5, tools.Count);
        Assert.Equal(2, tools.OfType<ApprovalRequiredAIFunction>().Count());
        var get = Assert.IsAssignableFrom<AIFunction>(tools.Single(tool => tool.Name == ToolNames.GetAzureUpdate));
        var result = Assert.IsType<JsonElement>(await get.InvokeAsync(new AIFunctionArguments { ["id"] = "559242" }, TestContext.Current.CancellationToken));
        Assert.True(result.GetProperty("found").GetBoolean());
        Assert.Contains("<untrusted_content", result.GetProperty("content").GetString(), StringComparison.Ordinal);
        Assert.Equal(1, environment.Updates.Calls);

        var failed = await environment.RawClient.CallToolAsync(ToolNames.GetAzureUpdate,
            new Dictionary<string, object?> { ["id"] = "../bad" }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(failed.IsError);
        Assert.False(CoworkMcpClient.ReadResult(failed).GetProperty("ok").GetBoolean());
        Assert.Equal(1, environment.Updates.Calls);
        environment.Updates.Fail = true;
        var unavailable = await environment.RawClient.CallToolAsync(ToolNames.GetAzureUpdate,
            new Dictionary<string, object?> { ["id"] = "559242" }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(unavailable.IsError);
        Assert.Equal("internal_error", CoworkMcpClient.ReadResult(unavailable).GetProperty("error").GetString());
    }

    [Fact]
    public async Task MisplacedSourcesExplainTheNestedArgumentAndAllowCorrection()
    {
        await using var environment = await McpEnvironment.CreateAsync();
        var function = Assert.IsAssignableFrom<AIFunction>(environment.Client.CreateTools(false, () => "repair-session")
            .Single(tool => tool.Name == ToolNames.CreateWordReport));
        using var malformed = JsonDocument.Parse("""
            {"fileName":"report","title":"更新情報","language":"ja-JP","sections":[{"heading":"概要","paragraphs":["固定データ"]}]}
            """);
        SourceRef[] sources = [new("出典", "https://azure.microsoft.com/updates?id=559242")];
        var rejected = Assert.IsType<JsonElement>(await function.InvokeAsync(
            new AIFunctionArguments { ["report"] = malformed.RootElement, ["sources"] = sources },
            TestContext.Current.CancellationToken));
        Assert.False(rejected.GetProperty("ok").GetBoolean());
        Assert.Contains("report.sources", rejected.GetProperty("error").GetString(), StringComparison.Ordinal);
        Assert.False(rejected.TryGetProperty("artifact", out _));

        var report = new WordReportSpec("report", "更新情報", null, "ja-JP",
            [new ReportSection("概要", Paragraphs: ["固定データ"])], sources);
        var corrected = Assert.IsType<JsonElement>(await function.InvokeAsync(
            new AIFunctionArguments { ["report"] = report }, TestContext.Current.CancellationToken));
        Assert.True(corrected.GetProperty("ok").GetBoolean());
        Assert.Equal("repair-session", corrected.GetProperty("artifact").GetProperty("sessionId").GetString());
    }

    [Fact]
    public async Task PowerPointTotalIncludesGeneratedCoverAndSourcesAndRejectsMismatches()
    {
        await using var environment = await McpEnvironment.CreateAsync();
        var function = Assert.IsAssignableFrom<AIFunction>(environment.Client.CreateTools(false, () => "slides-session")
            .Single(tool => tool.Name == ToolNames.CreatePowerPointDeck));
        var body = new SlideSpec("概要", ["固定データの内容"], "発表者ノート");
        var deck = new DeckSpec("slides", "更新情報", null, [body, body, body],
            [new SourceRef("出典", "https://azure.microsoft.com/updates?id=559242")]);
        var missing = Assert.IsType<JsonElement>(await function.InvokeAsync(
            new AIFunctionArguments { ["deck"] = deck }, TestContext.Current.CancellationToken));
        Assert.False(missing.GetProperty("ok").GetBoolean());
        Assert.Contains("totalSlides", missing.GetProperty("error").GetString(), StringComparison.Ordinal);
        Assert.False(missing.TryGetProperty("artifact", out _));

        foreach (var total in new[] { 2, 3, 15 })
        {
            var rejected = Assert.IsType<JsonElement>(await function.InvokeAsync(
                new AIFunctionArguments { ["deck"] = deck, ["totalSlides"] = total },
                TestContext.Current.CancellationToken));
            Assert.False(rejected.GetProperty("ok").GetBoolean());
            Assert.Contains("totalSlides", rejected.GetProperty("error").GetString(), StringComparison.Ordinal);
            Assert.False(rejected.TryGetProperty("artifact", out _));
        }

        var corrected = Assert.IsType<JsonElement>(await function.InvokeAsync(
            new AIFunctionArguments { ["deck"] = deck with { Slides = [body] }, ["totalSlides"] = 3 },
            TestContext.Current.CancellationToken));
        Assert.True(corrected.GetProperty("ok").GetBoolean());
        var artifact = corrected.GetProperty("artifact");
        Assert.Equal(3, artifact.GetProperty("itemCount").GetInt32());
        using var document = PresentationDocument.Open(
            Path.Combine(environment.Directory, artifact.GetProperty("location").GetString()!), false);
        Assert.Equal(3, document.PresentationPart?.Presentation?.SlideIdList?.ChildElements.Count);
    }

    [Fact]
    public async Task McpIsPrivateAndRejectsBrowserOrigins()
    {
        await using var environment = await McpEnvironment.CreateAsync();
        using var anonymous = environment.Host.GetTestClient();
        using var denied = await anonymous.GetAsync("/mcp", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        anonymous.DefaultRequestHeaders.Add(CoworkMcpServer.AccessHeader, environment.Server.AccessToken);
        anonymous.DefaultRequestHeaders.Add("Origin", "https://untrusted.test");
        using var browser = await anonymous.GetAsync("/mcp", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, browser.StatusCode);
    }

    [Fact]
    public async Task ReachingTheTraceLimitDoesNotRejectBusinessCalls()
    {
        await using var environment = await McpEnvironment.CreateAsync();
        var session = environment.Traces.CreateSession();
        using (LearningTraceCollector.Enter(session))
        {
            for (var index = 0; index < LearningTraceRules.MaximumSpans; index++)
            {
                using var activity = environment.Telemetry.Source.StartActivity("model.call");
            }
            var function = Assert.IsAssignableFrom<AIFunction>(environment.Client.CreateTools(false, () => null)
                .Single(tool => tool.Name == ToolNames.GetAzureUpdate));
            var result = Assert.IsType<JsonElement>(await function.InvokeAsync(
                new AIFunctionArguments { ["id"] = "559242" }, TestContext.Current.CancellationToken));
            Assert.True(result.GetProperty("found").GetBoolean());
            Assert.Equal(1, environment.Updates.Calls);
        }
        Assert.True(session.DroppedEvents > 0);
        environment.Traces.Remove(session);
    }

    [Fact]
    public async Task ConcurrentSessionsAndRealOfficeSpansRemainIsolatedAcrossMcp()
    {
        var spans = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is TelemetryNames.Sources.Agent or TelemetryNames.Sources.McpSdk,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Add,
        };
        ActivitySource.AddActivityListener(listener);
        await using var environment = await McpEnvironment.CreateAsync();
        var traces = new ConcurrentBag<TraceSpanEvent[]>();
        async Task<JsonElement> Create(string session)
        {
            var measured = environment.Traces.CreateSession();
            JsonElement result;
            using (LearningTraceCollector.Enter(measured))
            using (var root = environment.Telemetry.Source.StartActivity("agent.turn"))
            {
                var tools = environment.Client.CreateTools(false, () => session);
                var function = Assert.IsAssignableFrom<AIFunction>(tools.Single(tool => tool.Name == ToolNames.CreateWordReport));
                var spec = new WordReportSpec("report", "更新情報", null, "ja-JP",
                    [new ReportSection("概要", Paragraphs: ["固定データの内容"])],
                    [new SourceRef("出典", "https://azure.microsoft.com/updates?id=559242")]);
                result = Assert.IsType<JsonElement>(await function.InvokeAsync(
                    new AIFunctionArguments { ["report"] = spec }, TestContext.Current.CancellationToken));
            }
            var received = new List<TraceSpanEvent>();
            while (measured.Events.Reader.TryRead(out var item))
                if (item.Span is { } span) received.Add(span);
            traces.Add([.. received]);
            environment.Traces.Remove(measured);
            return result;
        }

        using var sharedTrace = new Activity("shared-request").SetIdFormat(ActivityIdFormat.W3C).Start();
        var results = await Task.WhenAll(Create("session-one"), Create("session-two"));
        Assert.Equal("session-one", results[0].GetProperty("artifact").GetProperty("sessionId").GetString());
        Assert.Equal("session-two", results[1].GetProperty("artifact").GetProperty("sessionId").GetString());
        Assert.Null(McpInvocationContext.SessionId.Value);
        foreach (var result in results)
        {
            var artifact = result.GetProperty("artifact");
            var bytes = await File.ReadAllBytesAsync(Path.Combine(environment.Directory, artifact.GetProperty("location").GetString()!),
                TestContext.Current.CancellationToken);
            Assert.Equal(artifact.GetProperty("sizeBytes").GetInt64(), bytes.LongLength);
        }
        var office = spans.Where(span => span.OperationName == TelemetryNames.Spans.OfficeWordBuild).ToArray();
        Assert.Equal(2, office.Length);
        Assert.All(office, span => Assert.Contains(spans,
            parent => parent.OperationName == TelemetryNames.Spans.McpToolExecute && parent.SpanId == span.ParentSpanId));
        Assert.Contains(spans, span => span.OperationName == TelemetryNames.Spans.ArtifactSave);
        Assert.Contains(spans, span => span.Source.Name == TelemetryNames.Sources.McpSdk && span.Kind == ActivityKind.Client);
        Assert.Contains(spans, span => span.Source.Name == TelemetryNames.Sources.McpSdk && span.Kind == ActivityKind.Server);
        Assert.Equal(2, traces.Count);
        Assert.Equal(2, traces.Select(trace => trace[0].SpanId).Distinct(StringComparer.Ordinal).Count());
        Assert.Single(traces.Select(trace => trace[0].TraceId).Distinct(StringComparer.Ordinal));
        foreach (var trace in traces)
        {
            Assert.All(trace, span => Assert.True(LearningTraceRules.IsValid(span)));
            var completed = trace.Where(span => span.Status == "completed").ToArray();
            var root = Assert.Single(completed, span => span.Name == "agent.turn");
            var client = Assert.Single(completed, span => span.Name == "mcp.call");
            var server = Assert.Single(completed, span => span.Name == "mcp.tool.execute");
            var build = Assert.Single(completed, span => span.Name == "office.word.build");
            Assert.Equal(root.SpanId, client.ParentSpanId);
            Assert.Equal(client.SpanId, server.ParentSpanId);
            Assert.Equal(server.SpanId, build.ParentSpanId);
            Assert.All(completed, span => Assert.Equal(root.TraceId, span.TraceId));
            Assert.Contains(completed, span => span.Name == "artifact.save" && span.ParentSpanId == server.SpanId);
        }
    }

    private sealed class McpEnvironment : IAsyncDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "auc-mcp-" + Guid.NewGuid().ToString("N"));
        public StubUpdates Updates { get; } = new();
        public CoworkMcpClient Client { get; private set; } = null!;
        public CoworkMcpServer Server { get; private set; } = null!;
        public Microsoft.AspNetCore.Builder.WebApplication Host { get; private set; } = null!;
        public McpClient RawClient { get; private set; } = null!;
        public LearningTraceCollector Traces => _services.GetRequiredService<LearningTraceCollector>();
        public AgentTelemetry Telemetry => _services.GetRequiredService<AgentTelemetry>();
        private ServiceProvider _services = null!;
        private HttpClient _http = null!;

        public static async Task<McpEnvironment> CreateAsync()
        {
            var environment = new McpEnvironment();
            var services = new ServiceCollection().AddLogging();
            var configuration = new ConfigurationBuilder().Build();
            var options = new AgentRuntimeOptions(new Uri("https://example.test/api/projects/test"), "test",
                "SessionFiles", environment.Directory, environment.Directory, PromptShieldsMode.Off, null, false);
            ToolServices.AddTo(services, options, new RejectCredential(), configuration);
            services.RemoveAll<IAzureUpdatesClient>();
            services.AddSingleton<IAzureUpdatesClient>(environment.Updates);
            environment._services = services.BuildServiceProvider();
            environment.Server = environment._services.GetRequiredService<CoworkMcpServer>();
            environment.Host = environment.Server.CreateHost(environment._services, builder => builder.WebHost.UseTestServer());
            await environment.Host.StartAsync(TestContext.Current.CancellationToken);
            environment._http = environment.Host.GetTestClient();
            environment._http.DefaultRequestHeaders.Add(CoworkMcpServer.AccessHeader, environment.Server.AccessToken);
            environment.Client = environment._services.GetRequiredService<CoworkMcpClient>();
            var endpoint = new Uri("http://localhost/mcp");
            await environment.Client.ConnectAsync(endpoint, environment._http,
                environment._services.GetRequiredService<ILoggerFactory>(), TestContext.Current.CancellationToken);
            environment.RawClient = await McpClient.CreateAsync(new HttpClientTransport(
                new HttpClientTransportOptions { Endpoint = endpoint, TransportMode = HttpTransportMode.StreamableHttp },
                environment._http, ownsHttpClient: false), cancellationToken: TestContext.Current.CancellationToken);
            return environment;
        }

        public async ValueTask DisposeAsync()
        {
            await RawClient.DisposeAsync();
            await _services.DisposeAsync();
            _http.Dispose();
            await Host.DisposeAsync();
            if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, true);
        }
    }

    private sealed class StubUpdates : IAzureUpdatesClient
    {
        public int Calls { get; private set; }
        public bool Fail { get; set; }
        public Task<AzureUpdate?> GetAsync(string id, CancellationToken ct)
        {
            Calls++;
            if (Fail) throw new HttpRequestException("private upstream error");
            return Task.FromResult<AzureUpdate?>(new AzureUpdate { Id = id, Title = "更新情報", Description = "<p>固定データ</p>" });
        }
        public Task<AzureUpdateSearchResult> SearchAsync(AzureUpdateQuery query, CancellationToken ct) =>
            Task.FromResult(new AzureUpdateSearchResult([], 0, 0));
    }

    private sealed class RejectCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("単体テストでは認証を呼びません。");
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("単体テストでは認証を呼びません。");
    }
}
