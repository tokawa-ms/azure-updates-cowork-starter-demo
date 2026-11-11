using System.Text.Json;
using System.Text.Json.Serialization;
using UpdatesCowork.Shared.Contracts;

namespace UpdatesCowork.Shared;

/// <summary>画面、BFF、エージェントで共有する JSON のソース生成(camelCase)。</summary>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(PageContext))]
[JsonSerializable(typeof(ChatRequest))]
[JsonSerializable(typeof(ApprovalRequest))]
[JsonSerializable(typeof(FeedbackRequest))]
[JsonSerializable(typeof(NewThreadResponse))]
[JsonSerializable(typeof(ClientConfig))]
[JsonSerializable(typeof(ChatEvent))]
[JsonSerializable(typeof(MetaEvent))]
[JsonSerializable(typeof(TextDeltaEvent))]
[JsonSerializable(typeof(ToolStartedEvent))]
[JsonSerializable(typeof(ToolArgumentsEvent))]
[JsonSerializable(typeof(PlanUpdatedEvent))]
[JsonSerializable(typeof(ToolCompletedEvent))]
[JsonSerializable(typeof(ArtifactCreatedEvent))]
[JsonSerializable(typeof(ApprovalRequestedEvent))]
[JsonSerializable(typeof(UsageEvent))]
[JsonSerializable(typeof(DoneEvent))]
[JsonSerializable(typeof(ErrorEvent))]
[JsonSerializable(typeof(PingEvent))]
[JsonSerializable(typeof(ArtifactInfo))]
[JsonSerializable(typeof(ArtifactResult))]
[JsonSerializable(typeof(WordReportSpec))]
[JsonSerializable(typeof(DeckSpec))]
[JsonSerializable(typeof(IReadOnlyList<PlanStep>))]
public sealed partial class AucJsonContext : JsonSerializerContext;
