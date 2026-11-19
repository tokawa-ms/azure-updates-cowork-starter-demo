using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using UpdatesCowork.Shared;
using UpdatesCowork.Shared.Contracts;
using UpdatesCowork.Web.AgentGateway;
using UpdatesCowork.Web.Configuration;
using UpdatesCowork.Web.Endpoints;
using UpdatesCowork.Web.Registry;
using UpdatesCowork.Web.Telemetry;
using Xunit;

namespace UpdatesCowork.Web.Tests;

public sealed class FeedbackAnnotationTests
{
    [Fact]
    public async Task FeedbackIsRecordedAsEndUserAnnotationInTheOriginalChatTrace()
    {
        var logs = new CapturingLoggerProvider();
        await using var factory = new Factory(logs, annotations: true);
        using var client = CreateClient(factory);
        var turn = await ChatAsync(client);

        using var response = await client.PostAsJsonAsync("/api/feedback",
            new FeedbackRequest(turn.ThreadId, turn.ResponseId, FeedbackRatings.Bad, "出典が\u0007足りない"),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var feedback = Assert.Single(logs.Entries, entry => entry.EventId == 1001);
        var annotation = Assert.Single(logs.Entries, entry => entry.EventId == FeedbackAnnotation.EventId);
        Assert.Equal(true, feedback.State["auc.feedback.trace_linked"]);
        Assert.Equal("gen_ai.evaluation.result", annotation.Message);
        Assert.Equal("gen_ai.evaluation.result", annotation.State["microsoft.custom_event.name"]);
        Assert.Equal("task_completion", annotation.State["gen_ai.evaluation.name"]);
        Assert.Equal(0.0, annotation.State["gen_ai.evaluation.score.value"]);
        Assert.Equal("fail", annotation.State["gen_ai.evaluation.score.label"]);
        Assert.Equal("出典が足りない", annotation.State["gen_ai.evaluation.explanation"]);
        Assert.Equal(turn.ResponseId, annotation.State["gen_ai.response.id"]);
        Assert.Equal("end_user", annotation.State["microsoft.gen_ai.human_evaluation.source"]);
        Assert.Equal("human", annotation.State["microsoft.gen_ai.evaluation.actor.type"]);
        using (var properties = JsonDocument.Parse((string)annotation.State["internal_properties"]!))
            Assert.Equal("boolean", properties.RootElement.GetProperty("gen_ai.evaluation.type").GetString());
        Assert.DoesNotContain(annotation.State.Keys, key => key.Contains("user_key", StringComparison.Ordinal));

        // 2件とも、評価要求ではなく元の chat.turn のトレースの中(feedback.record の下)で記録される。
        foreach (var entry in new[] { feedback, annotation })
        {
            Assert.Equal(turn.TraceId, entry.TraceId);
            Assert.Equal(turn.ChatTurnSpanId, entry.ParentSpanId);
            Assert.Equal("feedback.record", entry.ActivityName);
        }
    }

    [Fact]
    public async Task UnknownResponsesAndDisabledFeatureDoNotSendAnnotations()
    {
        foreach (var (annotations, unknownResponse) in new[] { (true, true), (false, false) })
        {
            var logs = new CapturingLoggerProvider();
            await using var factory = new Factory(logs, annotations);
            using var client = CreateClient(factory);
            var turn = await ChatAsync(client);

            using var response = await client.PostAsJsonAsync("/api/feedback",
                new FeedbackRequest(turn.ThreadId, unknownResponse ? "resp_unknown" : turn.ResponseId, FeedbackRatings.Good, null),
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            var feedback = Assert.Single(logs.Entries, entry => entry.EventId == 1001);
            Assert.Equal(false, feedback.State["auc.feedback.trace_linked"]);
            Assert.NotEqual(turn.TraceId, feedback.TraceId);
            Assert.DoesNotContain(logs.Entries, entry => entry.EventId == FeedbackAnnotation.EventId);
        }
    }

    [Fact]
    public void ThreadKeepsOnlyRecentResponseTraces()
    {
        var thread = new ChatThread { ThreadId = "thr_x", UserKey = "user" };
        for (var index = 0; index < ChatThread.MaxResponseTraces + 5; index++)
            thread = thread.WithResponseTrace(new ResponseTrace($"resp_{index}", "trace", "span", true));
        thread = thread.WithResponseTrace(new ResponseTrace("resp_10", "trace2", "span2", true));
        Assert.Equal(ChatThread.MaxResponseTraces, thread.ResponseTraces.Count);
        Assert.Null(thread.FindResponseTrace("resp_4"));
        Assert.Equal("trace2", thread.FindResponseTrace("resp_10")!.TraceId);
        Assert.Single(thread.ResponseTraces, item => item.ResponseId == "resp_10");
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory)
    {
        // 利用者の Cookie は Secure なので、2回目の要求にも送られるように https で呼ぶ。
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-AUC-Request", "1");
        return client;
    }

    private static async Task<ChatTurn> ChatAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/chat",
            new ChatRequest(null, "要約して", null, null), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var events = await ClientSse.ParseAsync(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var meta = events.First(evt => evt.Name == "meta").Data;
        var done = events.Single(evt => evt.Name == "done").Data;
        var root = events.Where(evt => evt.Name == "trace.span")
            .Select(evt => evt.Data.Deserialize(AucJsonContext.Default.TraceSpanEvent)!)
            .Single(span => span.Name == "chat.turn" && span.Status == "completed");
        return new ChatTurn(meta.GetProperty("threadId").GetString()!, done.GetProperty("responseId").GetString()!,
            root.TraceId, root.SpanId);
    }

    private sealed record ChatTurn(string ThreadId, string ResponseId, string TraceId, string ChatTurnSpanId);

    private sealed class Factory(CapturingLoggerProvider logs, bool annotations) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAgentGateway>();
                services.AddSingleton<IAgentGateway>(new RecordingAgentGateway("fake-word.sse"));
                services.PostConfigure<AgentOptions>(options => options.ConversationMode = ConversationMode.Conversations);
                services.PostConfigure<WebOptions>(options => options.Features.FeedbackAnnotations = annotations);
            });
        }
    }

    private sealed record LogEntry(int EventId, string Message, IReadOnlyDictionary<string, object?> State,
        string? TraceId, string? ParentSpanId, string? ActivityName);

    /// <summary>評価のログだけを、記録した時点の Activity と一緒に取っておく。</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        public IReadOnlyList<LogEntry> Entries => [.. _entries];

        public ILogger CreateLogger(string categoryName) =>
            categoryName == FeedbackEndpoints.LoggerCategory ? new Logger(_entries) : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        public void Dispose()
        {
        }

        private sealed class Logger(ConcurrentQueue<LogEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                    ? pairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                    : new Dictionary<string, object?>(StringComparer.Ordinal);
                var activity = Activity.Current;
                entries.Enqueue(new LogEntry(eventId.Id, formatter(state, exception), values,
                    activity?.TraceId.ToHexString(), activity?.ParentSpanId.ToHexString(), activity?.OperationName));
            }
        }
    }
}
