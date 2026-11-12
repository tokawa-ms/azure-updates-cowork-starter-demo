using Microsoft.Extensions.Logging.Abstractions;
using UpdatesCowork.Shared.Contracts;
using UpdatesCowork.Web.Chat;
using Xunit;

namespace UpdatesCowork.Web.Tests;

public sealed class ChatEventMapperTests
{
    private const string ThreadId = "th_01JCZ6V3S1Y4M5K8Q2W7E9R0TA";

    [Fact]
    public async Task FakeWord_MapsEventsInDesignOrder_AndQueuesArtifact()
    {
        var (mapper, events) = await MapAsync("fake-word.sse");

        string[] expected =
        [
            ChatEventNames.Meta,
            ChatEventNames.ToolStarted, ChatEventNames.ToolArguments, ChatEventNames.PlanUpdated, ChatEventNames.ToolCompleted,
            ChatEventNames.ToolStarted, ChatEventNames.ToolArguments, ChatEventNames.ToolCompleted,
            ChatEventNames.ToolArguments, ChatEventNames.PlanUpdated, ChatEventNames.ToolCompleted,
            ChatEventNames.ToolStarted, ChatEventNames.ToolArguments, ChatEventNames.ToolCompleted,
            ChatEventNames.ToolArguments, ChatEventNames.PlanUpdated, ChatEventNames.ToolCompleted,
            ChatEventNames.TextDelta, ChatEventNames.TextDelta, ChatEventNames.TextDelta,
            ChatEventNames.Usage, ChatEventNames.Done,
        ];
        Assert.Equal(expected, events.Select(e => e.EventName));

        var meta = Assert.IsType<MetaEvent>(events[0]);
        Assert.Equal(ThreadId, meta.ThreadId);
        Assert.Equal("resp_fake_word_0001", meta.ResponseId);
        Assert.Null(meta.ConversationId);
        Assert.Equal("trace-1", meta.TraceId);

        var firstPlan = events.OfType<PlanUpdatedEvent>().First();
        Assert.Equal(["in_progress", "pending", "pending"], firstPlan.Steps.Select(s => s.Status));
        Assert.Equal("更新情報を取得する", firstPlan.Steps[0].Title);

        var wordCompleted = events.OfType<ToolCompletedEvent>().Single(e => e.CallId == "call_word_1");
        Assert.Equal("create_word_report", wordCompleted.Name);
        Assert.True(wordCompleted.Ok);

        Assert.True(mapper.TryDequeueArtifact(out var artifact));
        Assert.Equal("art_01JCZ7A8B9C0D1E2F3G4H5J6K7", artifact.ArtifactId);
        Assert.Equal("local", artifact.Storage);
        Assert.Equal("art_01JCZ7A8B9C0D1E2F3G4H5J6K7/azure-update-559242-summary.docx", artifact.Location);
        Assert.False(mapper.TryDequeueArtifact(out _));

        var usage = Assert.IsType<UsageEvent>(events[^2]);
        Assert.Equal((5321, 812, 6133), (usage.InputTokens, usage.OutputTokens, usage.TotalTokens));
        var done = Assert.IsType<DoneEvent>(events[^1]);
        Assert.Equal("completed", done.Status);
        Assert.Equal("resp_fake_word_0001", done.ResponseId);
        Assert.Equal("completed", mapper.DoneStatus);
        Assert.Equal(1, mapper.ReasoningCount);
        Assert.Null(mapper.ErrorCode);
    }

    [Fact]
    public async Task Summarize_ReadsConversationId_AndOutputGivenAsContentParts()
    {
        var (mapper, events) = await MapAsync("summarize.sse");

        Assert.Equal("conv_sum_0001", Assert.IsType<MetaEvent>(events[0]).ConversationId);
        var completed = Assert.Single(events.OfType<ToolCompletedEvent>());
        Assert.Equal("get_azure_update", completed.Name);
        Assert.True(completed.Ok);
        Assert.StartsWith("{\"found\":true", completed.Summary, StringComparison.Ordinal);
        Assert.Equal("## 要約\n\nAzure Container Apps express が一般提供になりました(ID: 559242)。",
            string.Concat(events.OfType<TextDeltaEvent>().Select(e => e.Text)));
        Assert.Equal("conv_sum_0001", mapper.ConversationId);
        Assert.False(mapper.TryDequeueArtifact(out _));
    }

    [Fact]
    public async Task PptxSessionFiles_AcceptsOutputAsJsonObject()
    {
        var (mapper, _) = await MapAsync("pptx-session.sse");

        Assert.True(mapper.TryDequeueArtifact(out var artifact));
        Assert.Equal("pptx", artifact.Kind);
        Assert.Equal("session", artifact.Storage);
        Assert.Equal("artifacts/art_01JCZ7B0C1D2E3F4G5H6J7K8M9/azure-update-559242-deck.pptx", artifact.Location);
        Assert.Equal("session_test", artifact.SessionId);
    }

    [Fact]
    public async Task InvalidArtifacts_AreNotQueued_AndFailedToolIsReported()
    {
        var (mapper, events) = await MapAsync("artifact-invalid.sse");

        Assert.False(mapper.TryDequeueArtifact(out _));
        var completed = events.OfType<ToolCompletedEvent>().ToList();
        Assert.Equal(2, completed.Count);
        Assert.True(completed[0].Ok);
        Assert.False(completed[1].Ok);
        Assert.Contains("10 列", completed[1].Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failed_MapsToResponseFailed_WithoutUpstreamMessage()
    {
        var (mapper, events) = await MapAsync("failed.sse");

        var error = Assert.IsType<ErrorEvent>(events[^1]);
        Assert.Equal(ChatErrorCodes.ResponseFailed, error.Code);
        Assert.DoesNotContain("Internal", error.Message, StringComparison.Ordinal);
        Assert.Equal("trace-1", error.TraceId);
        Assert.Null(mapper.DoneStatus);
        Assert.Equal(ChatErrorCodes.ResponseFailed, mapper.ErrorCode);
    }

    [Fact]
    public async Task Incomplete_MapsToDoneWithReason()
    {
        var (mapper, events) = await MapAsync("incomplete.sse");

        var usage = Assert.IsType<UsageEvent>(events[^2]);
        Assert.Equal(4096, usage.OutputTokens);
        var done = Assert.IsType<DoneEvent>(events[^1]);
        Assert.Equal("incomplete", done.Status);
        Assert.Equal("max_output_tokens", done.Reason);
        Assert.Equal("incomplete", mapper.DoneStatus);
    }

    [Fact]
    public async Task StreamError_WithRateLimitCode_MapsToRateLimited()
    {
        var (mapper, events) = await MapAsync("error.sse");

        var error = Assert.IsType<ErrorEvent>(events[^1]);
        Assert.Equal(ChatErrorCodes.RateLimited, error.Code);
        Assert.DoesNotContain("model", error.Message, StringComparison.Ordinal);
        Assert.Equal(ChatErrorCodes.RateLimited, mapper.ErrorCode);
    }

    [Fact]
    public async Task ApprovalRequest_IsSentOnce()
    {
        var (_, events) = await MapAsync("approval.sse");

        var approval = Assert.Single(events.OfType<ApprovalRequestedEvent>());
        Assert.Equal("mcpr_0001", approval.ApprovalRequestId);
        Assert.Equal("create_word_report", approval.ToolName);
        Assert.Contains("azure-update-559242-summary", approval.ArgumentsPreview, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolOutput_IsTruncatedTo2KB()
    {
        var mapper = new ChatEventMapper(ThreadId, null, null, NullLogger.Instance);
        var longText = new string('a', 5000);
        var item = $$$"""{"type":"response.output_item.done","item":{"type":"function_call_output","call_id":"c1","output":"{{{longText}}}"}}""";

        var events = mapper.Map(new AgentGateway.AgentStreamEvent("response.output_item.done", Parse(item)));

        var completed = Assert.IsType<ToolCompletedEvent>(Assert.Single(events));
        Assert.Equal(ChatEventMapper.MaxPreviewLength + 1, completed.Summary!.Length);
    }

    [Fact]
    public void UnknownEvent_IsDropped()
    {
        var mapper = new ChatEventMapper(ThreadId, null, null, NullLogger.Instance);

        var events = mapper.Map(new AgentGateway.AgentStreamEvent("response.debug.custom", Parse("""{"type":"response.debug.custom"}""")));

        Assert.Empty(events);
    }

    private static async Task<(ChatEventMapper Mapper, List<ChatEvent> Events)> MapAsync(string recording)
    {
        var mapper = new ChatEventMapper(ThreadId, null, "trace-1", NullLogger.Instance);
        var events = new List<ChatEvent>();
        foreach (var evt in await Recordings.LoadAsync(recording))
        {
            events.AddRange(mapper.Map(evt));
        }

        return (mapper, events);
    }

    private static System.Text.Json.JsonElement Parse(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
