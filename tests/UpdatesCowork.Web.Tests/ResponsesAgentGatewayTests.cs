using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UpdatesCowork.Shared.Contracts;
using UpdatesCowork.Web.AgentGateway;
using UpdatesCowork.Web.Chat;
using UpdatesCowork.Web.Configuration;
using Xunit;

namespace UpdatesCowork.Web.Tests;

public sealed class ResponsesAgentGatewayTests
{
    private const string BaseUrl = "https://example.services.ai.azure.com/api/projects/p1/agents/updates-cowork/endpoint/protocols/openai";
    private static readonly AgentCallContext Context = new("th_01JCZ6V3S1Y4M5K8Q2W7E9R0TA", "0123456789abcdef0123456789abcdef");

    [Fact]
    public async Task Stream_PostsResponsesWithPageContextAndHeaders()
    {
        var body = await File.ReadAllTextAsync(Recordings.PathOf("summarize.sse"), TestContext.Current.CancellationToken);
        var handler = new StubHttpHandler(_ => StubHttpHandler.EventStream(body));
        var gateway = CreateGateway(handler, o =>
        {
            o.SendUserIdentity = true;
            o.VersionOverride = "3";
        });
        var page = new ValidatedPageContext(
            "https://azure.microsoft.com/ja-jp/updates?id=559242",
            "559242",
            "Generally Available: Azure Container Apps express",
            "ja-JP");
        var request = new AgentTurnRequest
        {
            Context = Context,
            ConversationId = "conv_1",
            DeveloperText = AgentRequestBuilder.BuildPageContextText(page),
            UserText = "このアップデートを要約して",
        };

        using var activity = new Activity("test").Start();
        var events = await CollectAsync(gateway.StreamAsync(request, TestContext.Current.CancellationToken));

        Assert.Equal(7, events.Count);
        Assert.Equal("response.created", events[0].Type);
        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal($"{BaseUrl}/responses?api-version=v1", sent.Uri.ToString());
        Assert.Equal("application/json", sent.ContentType);
        Assert.Equal("text/event-stream", sent.Headers["Accept"]);
        Assert.Equal(activity.TraceId.ToHexString(), sent.Headers["x-client-trace-id"]);
        Assert.Equal(Context.ThreadId, sent.Headers["x-client-thread-id"]);
        Assert.Equal(Context.UserKey, sent.Headers["x-ms-user-identity"]);
        Assert.Equal("3", sent.Headers["x-agent-version-override"]);

        using var json = JsonDocument.Parse(sent.Body);
        var root = json.RootElement;
        Assert.Equal("conv_1", root.GetProperty("conversation").GetString());
        Assert.False(root.TryGetProperty("previous_response_id", out _));
        Assert.True(root.GetProperty("stream").GetBoolean());
        var input = root.GetProperty("input");
        Assert.Equal(2, input.GetArrayLength());
        Assert.Equal("developer", input[0].GetProperty("role").GetString());
        Assert.Equal(
            "<page_context>\n<untrusted_content>\nupstream_url: https://azure.microsoft.com/ja-jp/updates?id=559242\nupdate_id: 559242\n"
            + "title: Generally Available: Azure Container Apps express\nlocale: ja-JP\n</untrusted_content>\n</page_context>",
            input[0].GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal("input_text", input[0].GetProperty("content")[0].GetProperty("type").GetString());
        Assert.Equal("user", input[1].GetProperty("role").GetString());
        Assert.Equal("このアップデートを要約して", input[1].GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task Stream_OmitsOptionalHeaders_AndUsesPreviousResponseId()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.EventStream(string.Empty));
        var gateway = CreateGateway(handler, o => o.ApiVersion = string.Empty);
        var request = new AgentTurnRequest { Context = Context, PreviousResponseId = "resp_prev", UserText = "こんにちは" };

        await CollectAsync(gateway.StreamAsync(request, TestContext.Current.CancellationToken));

        var sent = Assert.Single(handler.Requests);
        Assert.Equal($"{BaseUrl}/responses", sent.Uri.ToString());
        Assert.False(sent.Headers.ContainsKey("x-ms-user-identity"));
        Assert.False(sent.Headers.ContainsKey("x-agent-version-override"));
        using var json = JsonDocument.Parse(sent.Body);
        Assert.Equal("resp_prev", json.RootElement.GetProperty("previous_response_id").GetString());
        Assert.False(json.RootElement.TryGetProperty("conversation", out _));
        var input = Assert.Single(json.RootElement.GetProperty("input").EnumerateArray());
        Assert.Equal("user", input.GetProperty("role").GetString());
    }

    [Fact]
    public void ApprovalBody_ContainsOnlyApprovalResponse()
    {
        var body = AgentRequestBuilder.BuildResponsesBody(new AgentTurnRequest
        {
            Context = Context,
            ConversationId = "conv_appr_0001",
            Approval = new AgentApprovalResponse("mcpr_0001", true),
        });

        using var json = JsonDocument.Parse(body);
        Assert.Equal("conv_appr_0001", json.RootElement.GetProperty("conversation").GetString());
        var item = Assert.Single(json.RootElement.GetProperty("input").EnumerateArray());
        Assert.Equal("mcp_approval_response", item.GetProperty("type").GetString());
        Assert.Equal("mcpr_0001", item.GetProperty("approval_request_id").GetString());
        Assert.True(item.GetProperty("approve").GetBoolean());
    }

    [Fact]
    public async Task CreateConversation_PostsConversationsAndReturnsId()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{"id":"conv_new_0001","object":"conversation"}"""));
        var gateway = CreateGateway(handler);

        var id = await gateway.CreateConversationAsync(Context, TestContext.Current.CancellationToken);

        Assert.Equal("conv_new_0001", id);
        var sent = Assert.Single(handler.Requests);
        Assert.Equal($"{BaseUrl}/conversations?api-version=v1", sent.Uri.ToString());
        Assert.Equal("{}", sent.Body);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ChatErrorCodes.AgentForbidden)]
    [InlineData(HttpStatusCode.Forbidden, ChatErrorCodes.AgentForbidden)]
    [InlineData(HttpStatusCode.NotFound, ChatErrorCodes.AgentNotFound)]
    [InlineData(HttpStatusCode.TooManyRequests, ChatErrorCodes.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, ChatErrorCodes.AgentError)]
    [InlineData(HttpStatusCode.BadGateway, ChatErrorCodes.AgentError)]
    public async Task Stream_MapsErrorStatus(HttpStatusCode status, string expectedCode)
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent("""{"error":{"message":"secret upstream detail"}}"""),
        });
        var gateway = CreateGateway(handler);

        var ex = await Assert.ThrowsAsync<AgentGatewayException>(() =>
            CollectAsync(gateway.StreamAsync(new AgentTurnRequest { Context = Context, UserText = "a" }, TestContext.Current.CancellationToken)));

        Assert.Equal(expectedCode, ex.Code);
        Assert.Equal((int)status, ex.StatusCode);
        Assert.DoesNotContain("secret", ex.Message, StringComparison.Ordinal);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Stream_RetriesOnceOn424()
    {
        var handler = new StubHttpHandler(attempt => attempt == 0
            ? new HttpResponseMessage((HttpStatusCode)424)
            : StubHttpHandler.EventStream(string.Empty));
        var gateway = CreateGateway(handler);

        await CollectAsync(gateway.StreamAsync(new AgentTurnRequest { Context = Context, UserText = "a" }, TestContext.Current.CancellationToken));

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Stream_FailsWhen424Repeats()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage((HttpStatusCode)424));
        var gateway = CreateGateway(handler);

        var ex = await Assert.ThrowsAsync<AgentGatewayException>(() =>
            CollectAsync(gateway.StreamAsync(new AgentTurnRequest { Context = Context, UserText = "a" }, TestContext.Current.CancellationToken)));

        Assert.Equal(ChatErrorCodes.AgentError, ex.Code);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Stream_ReadsRetryAfterOn429()
    {
        var handler = new StubHttpHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
            return response;
        });
        var gateway = CreateGateway(handler);

        var ex = await Assert.ThrowsAsync<AgentGatewayException>(() =>
            CollectAsync(gateway.StreamAsync(new AgentTurnRequest { Context = Context, UserText = "a" }, TestContext.Current.CancellationToken)));

        Assert.Equal(TimeSpan.FromSeconds(7), ex.RetryAfter);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stream_DoesNotLogUpstreamSecrets(bool connectionFailure)
    {
        const string privateDetail = "token=private-test-value; email=private@example.invalid";
        var handler = new StubHttpHandler(_ => connectionFailure
            ? throw new HttpRequestException(privateDetail)
            : new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent(privateDetail) });
        var logger = new RecordingLogger();
        var gateway = CreateGateway(handler, logger: logger);

        await Assert.ThrowsAsync<AgentGatewayException>(() =>
            CollectAsync(gateway.StreamAsync(new AgentTurnRequest { Context = Context, UserText = "a" }, TestContext.Current.CancellationToken)));

        Assert.NotEmpty(logger.Messages);
        Assert.All(logger.Messages, message => Assert.DoesNotContain("private", message, StringComparison.Ordinal));
    }

    private static ResponsesAgentGateway CreateGateway(
        StubHttpHandler handler,
        Action<AgentOptions>? configure = null,
        ILogger<ResponsesAgentGateway>? logger = null)
    {
        var options = new AgentOptions { Mode = AgentMode.Foundry, BaseUrl = BaseUrl + "/" };
        configure?.Invoke(options);
        return new ResponsesAgentGateway(
            new HttpClient(handler),
            Options.Create(options),
            TimeProvider.System,
            logger ?? NullLogger<ResponsesAgentGateway>.Instance)
        {
            RetryDelay = TimeSpan.Zero,
        };
    }

    private static async Task<List<AgentStreamEvent>> CollectAsync(IAsyncEnumerable<AgentStreamEvent> source)
    {
        var list = new List<AgentStreamEvent>();
        await foreach (var evt in source)
        {
            list.Add(evt);
        }

        return list;
    }

    private sealed class RecordingLogger : ILogger<ResponsesAgentGateway>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception) + exception);
    }
}
