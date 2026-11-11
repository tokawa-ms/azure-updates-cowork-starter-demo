using System.Text.Json.Serialization;

namespace UpdatesCowork.Shared.Contracts;

/// <summary>
/// BFF から画面に送る SSE イベント(6.3節)。<see cref="EventName"/> が <c>event:</c> 行、
/// レコードの残りのプロパティが <c>data:</c> 行の JSON になる。
/// </summary>
[JsonPolymorphic]
[JsonDerivedType(typeof(MetaEvent))]
[JsonDerivedType(typeof(TextDeltaEvent))]
[JsonDerivedType(typeof(ToolStartedEvent))]
[JsonDerivedType(typeof(ToolArgumentsEvent))]
[JsonDerivedType(typeof(PlanUpdatedEvent))]
[JsonDerivedType(typeof(ToolCompletedEvent))]
[JsonDerivedType(typeof(ArtifactCreatedEvent))]
[JsonDerivedType(typeof(ApprovalRequestedEvent))]
[JsonDerivedType(typeof(UsageEvent))]
[JsonDerivedType(typeof(DoneEvent))]
[JsonDerivedType(typeof(ErrorEvent))]
[JsonDerivedType(typeof(PingEvent))]
public abstract record ChatEvent
{
    [JsonIgnore]
    public abstract string EventName { get; }
}

public sealed record MetaEvent(string ThreadId, string? ConversationId, string? ResponseId, string? TraceId) : ChatEvent
{
    public override string EventName => ChatEventNames.Meta;
}

public sealed record TextDeltaEvent(string Text) : ChatEvent
{
    public override string EventName => ChatEventNames.TextDelta;
}

public sealed record ToolStartedEvent(string CallId, string Name) : ChatEvent
{
    public override string EventName => ChatEventNames.ToolStarted;
}

public sealed record ToolArgumentsEvent(string CallId, string Name, string Arguments) : ChatEvent
{
    public override string EventName => ChatEventNames.ToolArguments;
}

public sealed record PlanStep(string Title, string Status);

public sealed record PlanUpdatedEvent(IReadOnlyList<PlanStep> Steps) : ChatEvent
{
    public override string EventName => ChatEventNames.PlanUpdated;
}

public sealed record ToolCompletedEvent(string CallId, string Name, bool Ok, string? Summary) : ChatEvent
{
    public override string EventName => ChatEventNames.ToolCompleted;
}

public sealed record ArtifactCreatedEvent(string ArtifactId, string FileName, string Kind, long SizeBytes, string DownloadUrl) : ChatEvent
{
    public override string EventName => ChatEventNames.ArtifactCreated;
}

public sealed record ApprovalRequestedEvent(string ApprovalRequestId, string ToolName, string? ArgumentsPreview) : ChatEvent
{
    public override string EventName => ChatEventNames.ApprovalRequested;
}

public sealed record UsageEvent(long InputTokens, long OutputTokens, long TotalTokens) : ChatEvent
{
    public override string EventName => ChatEventNames.Usage;
}

public sealed record DoneEvent(string Status, string? Reason, string? ResponseId) : ChatEvent
{
    public override string EventName => ChatEventNames.Done;
}

public sealed record ErrorEvent(string Code, string Message, string? TraceId) : ChatEvent
{
    public override string EventName => ChatEventNames.Error;
}

public sealed record PingEvent : ChatEvent
{
    public override string EventName => ChatEventNames.Ping;
}

/// <summary>SSE の <c>event:</c> 名。</summary>
public static class ChatEventNames
{
    public const string Meta = "meta";
    public const string TextDelta = "text.delta";
    public const string ToolStarted = "tool.started";
    public const string ToolArguments = "tool.arguments";
    public const string PlanUpdated = "plan.updated";
    public const string ToolCompleted = "tool.completed";
    public const string ArtifactCreated = "artifact.created";
    public const string ApprovalRequested = "approval.requested";
    public const string Usage = "usage";
    public const string Done = "done";
    public const string Error = "error";
    public const string Ping = "ping";
}

/// <summary><c>error</c> イベントの <c>code</c>。</summary>
public static class ChatErrorCodes
{
    public const string Timeout = "timeout";
    public const string AgentForbidden = "agent_forbidden";
    public const string AgentNotFound = "agent_not_found";
    public const string RateLimited = "rate_limited";
    public const string AgentError = "agent_error";
    public const string ResponseFailed = "response_failed";
    public const string InvalidRequest = "invalid_request";
}
