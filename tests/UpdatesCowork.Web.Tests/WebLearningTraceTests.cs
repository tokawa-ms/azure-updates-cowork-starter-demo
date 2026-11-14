using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UpdatesCowork.Shared;
using UpdatesCowork.Shared.Contracts;
using UpdatesCowork.Web.AgentGateway;
using UpdatesCowork.Web.Chat;
using UpdatesCowork.Web.Configuration;
using Xunit;

namespace UpdatesCowork.Web.Tests;

public sealed class WebLearningTraceTests
{
    [Fact]
    public async Task EndpointMeasuresWebPipelineBeforeTerminalEventWithoutAgentMeasurements()
    {
        await using var factory = new Factory(new RecordingAgentGateway("fake-word.sse"));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-AUC-Request", "1");
        using var response = await client.PostAsJsonAsync("/api/chat",
            new ChatRequest(null, "private prompt", null, null), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var events = await ClientSse.ParseAsync(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("done", events[^1].Name);
        var spans = events.Where(evt => evt.Name == "trace.span")
            .Select(evt => evt.Data.Deserialize(AucJsonContext.Default.TraceSpanEvent)!).ToArray();
        Assert.NotEmpty(spans);
        Assert.All(spans, span => Assert.Equal("web", span.Source));
        var completed = spans.Where(span => span.Status == "completed").ToArray();
        Assert.Equal(spans.Length / 2, completed.Length);
        foreach (var name in new[] { "web.request.prepare", "chat.turn", "agent.conversation.create",
            "web.agent.invoke", "web.sse.relay", "web.artifact.register" })
            Assert.Contains(completed, span => span.Name == name);
        var root = Assert.Single(completed, span => span.Name == "chat.turn");
        var invoke = Assert.Single(completed, span => span.Name == "web.agent.invoke");
        Assert.Equal(root.SpanId, invoke.ParentSpanId);
        Assert.Equal(invoke.SpanId, Assert.Single(completed, span => span.Name == "web.sse.relay").ParentSpanId);
        Assert.All(completed, span => Assert.True(span.DurationMs >= 0));
        Assert.All(completed, span => Assert.Equal(span.StartedAt,
            Assert.Single(spans, started => started.SpanId == span.SpanId && started.Status == "running").StartedAt));
        var statuses = events.Where(evt => evt.Name == "trace.status").ToArray();
        Assert.Equal(["started", "complete"], statuses.Select(evt => evt.Data.GetProperty("status").GetString()));
        Assert.All(statuses, evt => Assert.Equal("web", evt.Data.GetProperty("source").GetString()));
        Assert.Equal("complete", events[^2].Data.GetProperty("status").GetString());
        var traceJson = JsonSerializer.Serialize(spans, AucJsonContext.Default.TraceSpanEventArray);
        Assert.DoesNotContain("private", traceJson, StringComparison.Ordinal);
        Assert.DoesNotContain("userKey", traceJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fileName", traceJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GatewayFailureEndsMeasuredWebOperationsBeforeError()
    {
        await using var factory = new Factory(new FailingGateway());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-AUC-Request", "1");
        using var response = await client.PostAsJsonAsync("/api/chat",
            new ChatRequest(null, "private prompt", null, null), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var events = await ClientSse.ParseAsync(body);
        Assert.Equal("error", events[^1].Name);
        var spans = events.Where(evt => evt.Name == "trace.span")
            .Select(evt => evt.Data.Deserialize(AucJsonContext.Default.TraceSpanEvent)!).ToArray();
        foreach (var name in new[] { "chat.turn", "web.agent.invoke" })
            Assert.Equal("failed", Assert.Single(spans, span => span.Name == name && span.Status != "running").Status);
        Assert.DoesNotContain(spans, span => span.Name == "web.sse.relay");
        Assert.Equal("complete", events[^2].Data.GetProperty("status").GetString());
        Assert.DoesNotContain("private", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TimeoutCancelsAgentCallButFailsWebTurnBeforeTerminalError()
    {
        await using var factory = new Factory(new RecordingAgentGateway("fake-word.sse") { Hang = TimeSpan.FromMinutes(1) }, 1);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-AUC-Request", "1");
        using var response = await client.PostAsJsonAsync("/api/chat",
            new ChatRequest(null, "timeout", null, null), TestContext.Current.CancellationToken);
        var events = await ClientSse.ParseAsync(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("timeout", events[^1].Data.GetProperty("code").GetString());
        var spans = events.Where(evt => evt.Name == "trace.span")
            .Select(evt => evt.Data.Deserialize(AucJsonContext.Default.TraceSpanEvent)!).ToArray();
        Assert.Equal("failed", Assert.Single(spans, span => span.Name == "chat.turn" && span.Status != "running").Status);
        Assert.Equal("cancelled", Assert.Single(spans, span => span.Name == "web.agent.invoke" && span.Status != "running").Status);
        Assert.Equal("complete", events[^2].Data.GetProperty("status").GetString());
    }

    [Fact]
    public async Task InvalidInputRemainsAnHttpErrorAndDoesNotInvokeAgent()
    {
        var gateway = new RecordingAgentGateway("fake-word.sse");
        await using var factory = new Factory(gateway);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-AUC-Request", "1");
        using var response = await client.PostAsJsonAsync("/api/chat",
            new ChatRequest(null, "", null, null), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(gateway.Requests);
        Assert.DoesNotContain("trace.span", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ParallelScopesRemainSeparateAndCancellationIsNotSuccess()
    {
        async Task<TraceSpanEvent[]> RunAsync(bool cancel)
        {
            var events = new List<ChatEvent>();
            var trace = new WebLearningTrace();
            trace.Attach(events.Add);
            using var cts = new CancellationTokenSource();
            using (var root = trace.Start("chat.turn", cancellationToken: cts.Token))
            {
                using var call = trace.Start("web.agent.invoke", ActivityKind.Client, cts.Token);
                await Task.Yield();
                if (cancel) cts.Cancel();
                else { call.Complete(); root.Complete(); }
            }
            trace.Finish();
            return events.OfType<TraceSpanEvent>().Where(span => span.Status != "running").ToArray();
        }
        var results = await Task.WhenAll(RunAsync(false), RunAsync(true));
        foreach (var (index, status) in new[] { (0, "completed"), (1, "cancelled") })
        {
            var root = Assert.Single(results[index], span => span.Name == "chat.turn");
            var call = Assert.Single(results[index], span => span.Name == "web.agent.invoke");
            Assert.Equal(root.SpanId, call.ParentSpanId);
            Assert.All(results[index], span => Assert.Equal(status, span.Status));
        }
        Assert.NotEqual(results[0][0].SpanId, results[1][0].SpanId);
    }

    [Fact]
    public void LimitDoesNotSuppressBusinessExecutionAndIsExplicit()
    {
        var events = new List<ChatEvent>();
        var trace = new WebLearningTrace();
        trace.Attach(events.Add);
        var executed = 0;
        for (var index = 0; index < LearningTraceRules.MaximumSpans + 1; index++)
        {
            using var span = trace.Start("web.artifact.register", cancellationToken: TestContext.Current.CancellationToken);
            executed++;
            span.Complete();
        }
        trace.Finish();
        Assert.Equal(LearningTraceRules.MaximumSpans + 1, executed);
        Assert.Equal(2 * LearningTraceRules.MaximumSpans, events.OfType<TraceSpanEvent>().Count());
        Assert.Equal(new TraceStatusEvent("limited", 2, "web"), events[^1]);
    }

    private sealed class Factory(IAgentGateway gateway, int timeoutSeconds = 200) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAgentGateway>();
                services.AddSingleton(gateway);
                services.PostConfigure<AgentOptions>(options =>
                {
                    options.ConversationMode = ConversationMode.Conversations;
                    options.TurnTimeoutSeconds = timeoutSeconds;
                });
            });
    }

    private sealed class FailingGateway : IAgentGateway
    {
        public Task<string> CreateConversationAsync(AgentCallContext context, CancellationToken cancellationToken) =>
            Task.FromResult("conv_test");

        public async IAsyncEnumerable<AgentStreamEvent> StreamAsync(AgentTurnRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            await Task.FromException(new AgentGatewayException("agent_error", "接続できません。", innerException: new HttpRequestException("private")));
            yield break;
        }
    }
}
