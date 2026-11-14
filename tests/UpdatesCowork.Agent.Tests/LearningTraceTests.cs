using System.ClientModel.Primitives;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text;
using Azure.AI.AgentServer.Responses;
using Azure.AI.AgentServer.Responses.Models;
using Microsoft.Agents.AI.Foundry.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using UpdatesCowork.Agent.Learning;
using UpdatesCowork.Shared;
using UpdatesCowork.Shared.Contracts;
using UpdatesCowork.Shared.Telemetry;
using Xunit;

namespace UpdatesCowork.Agent.Tests;

public sealed class LearningTraceTests
{
    [Fact]
    public async Task MeasuredScopeSurvivesOpaqueSdkParentsAndParallelToolExecution()
    {
        using var collector = new LearningTraceCollector();
        using var telemetry = new AgentTelemetry();
        using var sdk = new ActivitySource("auc-test-sdk");
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "auc-test-sdk",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        var session = collector.CreateSession();
        using (LearningTraceCollector.Enter(session))
        using (var root = telemetry.Source.StartActivity("agent.turn"))
        {
            async Task InvokeAsync()
            {
                using var sdkAgent = sdk.StartActivity("opaque-agent", ActivityKind.Internal, root!.Context);
                using var tool = telemetry.Source.StartActivity("tool.execute", ActivityKind.Internal, sdkAgent!.Context);
                using var sdkTool = sdk.StartActivity("opaque-tool", ActivityKind.Internal, tool!.Context);
                using var client = new LearningChatClient(new StubChatClient(), telemetry, "test-model");
                await client.GetStreamingResponseAsync([], cancellationToken: TestContext.Current.CancellationToken)
                    .ToChatResponseAsync(TestContext.Current.CancellationToken);
            }
            await Task.WhenAll(InvokeAsync(), InvokeAsync());
        }
        var spans = Drain(session);
        var completed = spans.Where(span => span.Status == "completed").ToArray();
        var rootSpan = Assert.Single(completed, span => span.Name == "agent.turn");
        var tools = completed.Where(span => span.Name == "tool.execute").ToArray();
        Assert.Equal(2, tools.Length);
        Assert.All(tools, span => Assert.Equal(rootSpan.SpanId, span.ParentSpanId));
        var models = completed.Where(span => span.Name == "model.call").ToArray();
        Assert.Equal(2, models.Length);
        Assert.Equal(tools.Select(span => span.SpanId).Order(StringComparer.Ordinal),
            models.Select(span => span.ParentSpanId!).Order(StringComparer.Ordinal));
        foreach (var started in spans.Where(span => span.Status == "running"))
            Assert.Equal(started.ParentSpanId, Assert.Single(completed, span => span.SpanId == started.SpanId).ParentSpanId);
    }

    [Fact]
    public async Task FunctionLoopMeasuresEachModelCallRatherThanTheWholeTurn()
    {
        using var collector = new LearningTraceCollector();
        using var telemetry = new AgentTelemetry();
        var session = collector.CreateSession();
        using var scope = LearningTraceCollector.Enter(session);
        using var measured = new LearningChatClient(new StubChatClient(), telemetry, "test-model");
        using var client = new ChatClientBuilder(measured).UseFunctionInvocation().Build();

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "private prompt")],
            new ChatOptions { Tools = [AIFunctionFactory.Create(() => "private tool result", "demo")] },
            TestContext.Current.CancellationToken);

        Assert.Equal("private answer", response.Text);
        var spans = Drain(session);
        var completed = spans.Where(span => span.Status == "completed").ToArray();
        Assert.Equal(2, completed.Length);
        Assert.All(completed, span => Assert.Equal("model.call", span.Name));
        Assert.All(completed, span => Assert.Equal("test-model", span.Model));
        Assert.Equal([7L, 9L], completed.Select(span => span.InputTokens));
        Assert.All(spans, span => Assert.True(LearningTraceRules.IsValid(span)));
        var json = JsonSerializer.Serialize(spans, AucJsonContext.Default.TraceSpanEventArray);
        Assert.DoesNotContain("private", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, "completed")]
    [InlineData(true, "failed")]
    public async Task StreamingRecordsUsageAndFailureWithoutExposingMessages(bool fail, string status)
    {
        using var collector = new LearningTraceCollector();
        using var telemetry = new AgentTelemetry();
        var session = collector.CreateSession();
        using var scope = LearningTraceCollector.Enter(session);
        using var client = new LearningChatClient(new StubChatClient { Fail = fail }, telemetry, "test-model");
        async Task ReadAsync()
        {
            await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "private prompt")],
                cancellationToken: TestContext.Current.CancellationToken)) { }
        }
        if (fail)
        {
            await Assert.ThrowsAsync<HttpRequestException>(ReadAsync);
        }
        else
        {
            await ReadAsync();
        }
        var spans = Drain(session);
        Assert.Equal(2, spans.Length);
        Assert.Equal("running", spans[0].Status);
        Assert.Equal(status, spans[1].Status);
        Assert.True(spans[1].DurationMs >= 0);
        if (!fail)
        {
            Assert.Equal(11, spans[1].InputTokens);
            Assert.Equal(3, spans[1].OutputTokens);
        }
        Assert.DoesNotContain("private", JsonSerializer.Serialize(spans, AucJsonContext.Default.TraceSpanEventArray), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SplitStreamingUsageMatchesTheSdkResponseAggregate()
    {
        using var collector = new LearningTraceCollector();
        using var telemetry = new AgentTelemetry();
        var session = collector.CreateSession();
        using var scope = LearningTraceCollector.Enter(session);
        using var client = new LearningChatClient(new StubChatClient { SplitUsage = true }, telemetry, "test-model");
        var response = await client.GetStreamingResponseAsync([], cancellationToken: TestContext.Current.CancellationToken)
            .ToChatResponseAsync(TestContext.Current.CancellationToken);
        var measured = Drain(session)[^1];
        Assert.NotNull(response.Usage);
        Assert.Equal(response.Usage.InputTokenCount, measured.InputTokens);
        Assert.Equal(response.Usage.OutputTokenCount, measured.OutputTokens);
    }

    [Fact]
    public async Task DisposingStreamingEnumerationRecordsCancellation()
    {
        using var collector = new LearningTraceCollector();
        using var telemetry = new AgentTelemetry();
        var session = collector.CreateSession();
        using var scope = LearningTraceCollector.Enter(session);
        using var client = new LearningChatClient(new StubChatClient(), telemetry, "test-model");
        await using (var updates = client.GetStreamingResponseAsync([],
            cancellationToken: TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken))
        {
            Assert.True(await updates.MoveNextAsync());
        }
        Assert.Equal("cancelled", Drain(session)[^1].Status);
    }

    [Fact]
    public async Task ActualSdkHttpSerializerPreservesTheTraceCarrierWithoutUnknownEventRecursion()
    {
        using var collector = new LearningTraceCollector();
        using var telemetry = new AgentTelemetry();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.AddFoundryResponses();
        builder.Services.RemoveAll<ResponseHandler>();
        builder.Services.AddSingleton<ResponseHandler>(new LearningResponseHandler(new StubResponseHandler(telemetry),
            collector, telemetry, NullLogger<LearningResponseHandler>.Instance));
        await using var app = builder.Build();
        app.MapFoundryResponses();
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var http = app.GetTestClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/responses")
        {
            Content = new StringContent("""{"model":"test","input":"test","stream":true}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("text/event-stream");
        using var response = await http.SendAsync(request, timeout.Token);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(timeout.Token);
        Assert.Contains("response.completed", body, StringComparison.Ordinal);
        Assert.Contains("\"auc_trace\":", body, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"model.call\"", body, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"complete\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("private", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResponseStreamPreservesStandardEventsAndSafeTraceJsonInSequence()
    {
        using var collector = new LearningTraceCollector();
        using var telemetry = new AgentTelemetry();
        var inner = new StubResponseHandler(telemetry);
        var handler = new LearningResponseHandler(inner, collector, telemetry, NullLogger<LearningResponseHandler>.Instance);
        var request = ModelReaderWriter.Read<CreateResponse>(BinaryData.FromString("""{"model":"test","input":"private prompt","stream":true}"""))!;
        var events = new List<ResponseStreamEvent>();
        await foreach (var evt in handler.CreateAsync(request, new ResponseContext("resp_measured"), TestContext.Current.CancellationToken))
        {
            events.Add(evt);
        }
        Assert.Equal("response.created", events[0].EventType.ToString());
        Assert.Equal("response.completed", events[^1].EventType.ToString());
        Assert.Equal(Enumerable.Range(0, events.Count).Select(n => (long)n), events.Select(evt => evt.SequenceNumber));
        var envelopes = events.Where(evt => evt.EventType.ToString() == AgentTraceEnvelope.CarrierEventType).Select(ReadEnvelope).ToArray();
        Assert.Equal("started", envelopes[0].Status?.Status);
        Assert.Equal("complete", envelopes[^1].Status?.Status);
        Assert.All(envelopes, envelope => Assert.Equal("resp_measured", envelope.ResponseId));
        var spans = envelopes.Where(envelope => envelope.Span is not null).Select(envelope => envelope.Span!).ToArray();
        Assert.All(spans, span => Assert.True(LearningTraceRules.IsValid(span)));
        var root = Assert.Single(spans, span => span.Name == "agent.turn" && span.Status == "completed");
        var model = Assert.Single(spans, span => span.Name == "model.call" && span.Status == "completed");
        Assert.Equal(root.SpanId, model.ParentSpanId);
        Assert.Equal(42, model.InputTokens);
        Assert.DoesNotContain("private", string.Join("", envelopes.Select(envelope =>
            JsonSerializer.Serialize(envelope, AucJsonContext.Default.AgentTraceEnvelope))), StringComparison.Ordinal);
        Assert.False(collector.TryGetSession(inner.Session!.Id, root.SpanId, out _));
        Assert.Null(LearningTraceCollector.Current);
    }

    [Fact]
    public async Task StoppingResponseEnumerationCancelsProducerAndRemovesRegistration()
    {
        using var collector = new LearningTraceCollector();
        using var telemetry = new AgentTelemetry();
        var inner = new StubResponseHandler(telemetry) { WaitForCancellation = true };
        var handler = new LearningResponseHandler(inner, collector, telemetry, NullLogger<LearningResponseHandler>.Instance);
        var request = ModelReaderWriter.Read<CreateResponse>(BinaryData.FromString("""{"model":"test","input":"test","stream":true}"""))!;
        await foreach (var _ in handler.CreateAsync(request, new ResponseContext("resp_measured"), TestContext.Current.CancellationToken))
        {
            break;
        }
        Assert.True(inner.Cancelled);
        var root = Assert.Single(Drain(inner.Session!), span => span.Name == "agent.turn");
        Assert.Equal("cancelled", root.Status);
        Assert.False(collector.TryGetSession(inner.Session!.Id, root.SpanId, out _));
    }

    [Fact]
    public async Task ResponseReportsOmittedTraceEventsBeforeItsTerminalEvent()
    {
        using var collector = new LearningTraceCollector();
        using var telemetry = new AgentTelemetry();
        var handler = new LearningResponseHandler(new StubResponseHandler(telemetry) { FillSpans = true },
            collector, telemetry, NullLogger<LearningResponseHandler>.Instance);
        var request = ModelReaderWriter.Read<CreateResponse>(BinaryData.FromString("""{"model":"test","input":"test","stream":true}"""))!;
        var events = new List<ResponseStreamEvent>();
        await foreach (var evt in handler.CreateAsync(request, new ResponseContext("resp_measured"), TestContext.Current.CancellationToken))
            events.Add(evt);
        var envelope = ReadEnvelope(events[^2]);
        Assert.Equal("limited", envelope.Status?.Status);
        Assert.True(envelope.Status?.DroppedEvents > 0);
        Assert.Equal("response.completed", events[^1].EventType.ToString());
    }

    [Fact]
    public void CollectorBoundsSpansAndDoesNotCollectAnotherOwnersSession()
    {
        using var collector = new LearningTraceCollector();
        using var other = new LearningTraceCollector();
        using var telemetry = new AgentTelemetry();
        var session = collector.CreateSession();
        var foreign = other.CreateSession();
        using (LearningTraceCollector.Enter(session))
        {
            for (var index = 0; index < LearningTraceRules.MaximumSpans + 1; index++)
            {
                using var activity = telemetry.Source.StartActivity("model.call");
                activity?.SetTag("private", "private prompt");
            }
        }
        Assert.Equal(LearningTraceRules.MaximumSpans * 2, Drain(session).Length);
        Assert.Equal(2, session.DroppedEvents);
        Assert.Empty(Drain(foreign));
    }

    private static TraceSpanEvent[] Drain(LearningTraceSession session)
    {
        var spans = new List<TraceSpanEvent>();
        while (session.Events.Reader.TryRead(out var item))
        {
            if (item.Span is { } span) spans.Add(span);
        }
        return [.. spans];
    }

    private static AgentTraceEnvelope ReadEnvelope(ResponseStreamEvent evt)
    {
        using var document = JsonDocument.Parse(ModelReaderWriter.Write(evt));
        return document.RootElement.GetProperty(AgentTraceEnvelope.MetadataProperty).Deserialize(AucJsonContext.Default.AgentTraceEnvelope)!;
    }

    private sealed class StubResponseHandler(AgentTelemetry telemetry) : ResponseHandler
    {
        internal LearningTraceSession? Session { get; private set; }
        internal bool WaitForCancellation { get; init; }
        internal bool FillSpans { get; init; }
        internal bool Cancelled { get; private set; }
        public override async IAsyncEnumerable<ResponseStreamEvent> CreateAsync(CreateResponse request, ResponseContext context,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Session = LearningTraceCollector.Current;
            yield return Read("response.created", "in_progress", context.ResponseId);
            if (WaitForCancellation)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                finally
                {
                    Cancelled = cancellationToken.IsCancellationRequested;
                }
            }
            if (FillSpans)
            {
                for (var index = 0; index < LearningTraceRules.MaximumSpans; index++)
                {
                    using var span = telemetry.Source.StartActivity("model.call");
                }
            }
            using (var activity = telemetry.Source.StartActivity("model.call"))
            {
                activity?.SetTag(TelemetryNames.Attributes.InputTokens, 42L);
                activity?.SetTag("private", "private prompt");
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
            }
            yield return Read("response.completed", "completed", context.ResponseId);
        }
        private static ResponseStreamEvent Read(string type, string status, string responseId) =>
            ModelReaderWriter.Read<ResponseStreamEvent>(BinaryData.FromString(
                $$$"""{"type":"{{{type}}}","sequence_number":99,"response":{"id":"{{{responseId}}}","object":"response","status":"{{{status}}}","output":[]}}"""))!;
    }

    private sealed class StubChatClient : IChatClient
    {
        private int _calls;
        internal bool Fail { get; init; }
        internal bool SplitUsage { get; init; }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _calls++;
            var message = _calls == 1
                ? new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call_demo", "demo")])
                : new ChatMessage(ChatRole.Assistant, "private answer");
            return Task.FromResult(new ChatResponse(message)
            {
                FinishReason = _calls == 1 ? ChatFinishReason.ToolCalls : ChatFinishReason.Stop,
                Usage = new UsageDetails { InputTokenCount = _calls == 1 ? 7 : 9, OutputTokenCount = 2 },
            });
        }
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            if (Fail) throw new HttpRequestException("private failure");
            yield return new ChatResponseUpdate(ChatRole.Assistant, "private answer");
            if (SplitUsage)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant,
                    [new UsageContent(new UsageDetails { InputTokenCount = 4, OutputTokenCount = 1 })]);
            }
            yield return new ChatResponseUpdate(ChatRole.Assistant,
                [new UsageContent(new UsageDetails { InputTokenCount = 11, OutputTokenCount = 3 })]);
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
