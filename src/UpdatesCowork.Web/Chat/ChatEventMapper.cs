using System.Text.Json;
using UpdatesCowork.Shared;
using UpdatesCowork.Shared.Contracts;
using UpdatesCowork.Web.AgentGateway;
using UpdatesCowork.Web.Artifacts;

namespace UpdatesCowork.Web.Chat;

/// <summary>
/// Responses のイベントを画面向けのイベントに変える(6.3節)。1回分の依頼ごとに作り、
/// <c>callId</c> とツール名の対応や、台帳に登録待ちの成果物を覚えておく。
/// </summary>
internal sealed partial class ChatEventMapper(string threadId, string? conversationId, string? traceId, ILogger logger)
{
    /// <summary>画面に送るツールの引数と結果の上限(UTF-16 の文字数で約 2 KB)。</summary>
    public const int MaxPreviewLength = 2048;

    public const int MaxPlanSteps = 20;

    private static readonly ChatEvent[] None = [];

    private static readonly HashSet<string> IgnoredTypes = new(StringComparer.Ordinal)
    {
        "response.queued",
        "response.output_text.done",
        "response.output_text.annotation.added",
        "response.content_part.added",
        "response.content_part.done",
        "response.function_call_arguments.delta",
        "response.function_call_arguments.done",
        "response.reasoning_summary_part.added",
        "response.reasoning_summary_part.done",
        "response.reasoning_summary_text.delta",
        "response.reasoning_summary_text.done",
        "response.reasoning_text.delta",
        "response.reasoning_text.done",
        "response.refusal.delta",
        "response.refusal.done",
    };

    private readonly Dictionary<string, string> _toolNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _approvalIds = new(StringComparer.Ordinal);
    private readonly Queue<ArtifactInfo> _pendingArtifacts = new();
    private bool _metaSent;

    public string? ResponseId { get; private set; }

    public string? ConversationId { get; private set; } = conversationId;

    /// <summary>推論の要約(<c>reasoning</c>)の件数。画面には出さず、スパンにだけ記録する。</summary>
    public int ReasoningCount { get; private set; }

    /// <summary>最後に送った <c>done</c> の状態(<c>completed</c> / <c>incomplete</c>)。終わっていなければ <c>null</c>。</summary>
    public string? DoneStatus { get; private set; }

    /// <summary>最後に送った <c>error</c> のコード。</summary>
    public string? ErrorCode { get; private set; }

    /// <summary>台帳に登録を待つ成果物を1件取り出す。</summary>
    public bool TryDequeueArtifact(out ArtifactInfo artifact) => _pendingArtifacts.TryDequeue(out artifact!);

    public IReadOnlyList<ChatEvent> Map(AgentStreamEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);

        var data = evt.Data;
        if (data.ValueKind != JsonValueKind.Object)
        {
            return evt.Type == AgentTraceEnvelope.EventType && !_traceRejected ? RejectTrace() : None;
        }

        switch (evt.Type)
        {
            case AgentTraceEnvelope.CarrierEventType:
                return data.TryGetProperty(AgentTraceEnvelope.MetadataProperty, out var trace) ? MapLearningTrace(trace) : None;
            case AgentTraceEnvelope.EventType:
                return MapLearningTrace(data);
            case "response.created":
                return MapCreated(data);
            case "response.output_text.delta":
                return GetString(data, "delta") is { Length: > 0 } delta ? [new TextDeltaEvent(delta)] : None;
            case "response.output_item.added":
                return data.TryGetProperty("item", out var added) && added.ValueKind == JsonValueKind.Object ? MapItemAdded(added) : None;
            case "response.output_item.done":
                return data.TryGetProperty("item", out var done) && done.ValueKind == JsonValueKind.Object ? MapItemDone(done) : None;
            case "response.completed":
                return MapFinished(data, "completed");
            case "response.incomplete":
                return MapFinished(data, "incomplete");
            case "response.failed":
                return MapFailed(data);
            case "error":
                return MapError(data);
            default:
                if (!IgnoredTypes.Contains(evt.Type))
                {
                    LogUnknownEvent(logger, evt.Type);
                }

                return None;
        }
    }

    private ChatEvent[] MapCreated(JsonElement data)
    {
        if (data.TryGetProperty("response", out var response) && response.ValueKind == JsonValueKind.Object)
        {
            ReadResponseIds(response);
        }

        if (_metaSent)
        {
            return None;
        }

        _metaSent = true;
        return [new MetaEvent(threadId, ConversationId, ResponseId, traceId)];
    }

    private ChatEvent[] MapItemAdded(JsonElement item)
    {
        switch (GetString(item, "type"))
        {
            case "function_call":
                {
                    var callId = GetString(item, "call_id") ?? GetString(item, "id") ?? string.Empty;
                    var name = GetString(item, "name") ?? string.Empty;
                    _toolNames[callId] = name;
                    return [new ToolStartedEvent(callId, name)];
                }

            case "mcp_approval_request":
                {
                    var id = GetString(item, "id");
                    if (string.IsNullOrEmpty(id) || !_approvalIds.Add(id))
                    {
                        return None;
                    }

                    var name = GetString(item, "name") ?? string.Empty;
                    return [new ApprovalRequestedEvent(id, name, Truncate(GetString(item, "arguments")))];
                }

            case "reasoning":
                ReasoningCount++;
                return None;
            default:
                return None;
        }
    }

    private ChatEvent[] MapItemDone(JsonElement item)
    {
        switch (GetString(item, "type"))
        {
            case "function_call":
                {
                    var callId = GetString(item, "call_id") ?? GetString(item, "id") ?? string.Empty;
                    var name = GetString(item, "name") ?? (_toolNames.TryGetValue(callId, out var known) ? known : string.Empty);
                    _toolNames[callId] = name;
                    var arguments = GetString(item, "arguments") ?? string.Empty;
                    var argumentsEvent = new ToolArgumentsEvent(callId, name, Truncate(arguments) ?? string.Empty);
                    if (name == ToolNames.UpdatePlan && TryParsePlan(arguments) is { } plan)
                    {
                        return [argumentsEvent, plan];
                    }

                    return [argumentsEvent];
                }

            case "function_call_output":
                return MapFunctionOutput(item);
            case "mcp_approval_request":
                // added で送っていなければ、ここで送る。
                return MapItemAdded(item);
            default:
                return None;
        }
    }

    private ChatEvent[] MapFunctionOutput(JsonElement item)
    {
        var callId = GetString(item, "call_id") ?? string.Empty;
        var name = _toolNames.TryGetValue(callId, out var known) ? known : string.Empty;
        var outputText = ReadOutputText(item);
        var parsed = TryParseObject(outputText);

        var ok = true;
        if (parsed is { } result)
        {
            if (result.TryGetProperty("ok", out var okElement) && okElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                ok = okElement.GetBoolean();
            }
            else if (result.TryGetProperty("error", out var errorElement) && errorElement.ValueKind is not JsonValueKind.Null)
            {
                ok = false;
            }
        }

        if (ok && ToolNames.IsArtifactTool(name) && parsed is { } artifactResult)
        {
            TryQueueArtifact(artifactResult);
        }

        return [new ToolCompletedEvent(callId, name, ok, Truncate(outputText))];
    }

    private void TryQueueArtifact(JsonElement result)
    {
        ArtifactResult? artifactResult;
        try
        {
            artifactResult = result.Deserialize(AucJsonContext.Default.ArtifactResult);
        }
        catch (JsonException)
        {
            LogInvalidArtifact(logger, "json");
            return;
        }

        if (artifactResult is not { Ok: true, Artifact: { } artifact })
        {
            return;
        }

        if (!IdGenerator.IsValid(artifact.ArtifactId, IdGenerator.ArtifactPrefix))
        {
            LogInvalidArtifact(logger, "artifact_id");
            return;
        }

        if (!ArtifactLocationValidator.IsValid(artifact.Storage, artifact.Location)
            || !artifact.Location.Contains(artifact.ArtifactId + "/", StringComparison.Ordinal))
        {
            LogInvalidArtifact(logger, "location");
            return;
        }

        if (string.IsNullOrWhiteSpace(artifact.FileName) || artifact.FileName.Length > 200)
        {
            LogInvalidArtifact(logger, "file_name");
            return;
        }

        _pendingArtifacts.Enqueue(artifact);
    }

    private ChatEvent[] MapFinished(JsonElement data, string status)
    {
        string? reason = null;
        UsageEvent? usage = null;
        if (data.TryGetProperty("response", out var response) && response.ValueKind == JsonValueKind.Object)
        {
            ReadResponseIds(response);
            usage = ReadUsage(response);
            if (response.TryGetProperty("incomplete_details", out var details) && details.ValueKind == JsonValueKind.Object)
            {
                reason = GetString(details, "reason");
            }
        }

        DoneStatus = status;
        var doneEvent = new DoneEvent(status, reason, ResponseId);
        return usage is null ? [doneEvent] : [usage, doneEvent];
    }

    private ChatEvent[] MapFailed(JsonElement data)
    {
        string? code = null;
        if (data.TryGetProperty("response", out var response) && response.ValueKind == JsonValueKind.Object)
        {
            ReadResponseIds(response);
            if (response.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                code = GetString(error, "code");
            }
        }

        LogResponseFailed(logger, Truncate(code, 100) ?? "unknown");
        return [CreateError(code)];
    }

    private ChatEvent[] MapError(JsonElement data)
    {
        var code = GetString(data, "code");
        if (code is null && data.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
        {
            code = GetString(error, "code");
        }

        LogStreamError(logger, Truncate(code, 100) ?? "unknown");
        if (IsRateLimit(code))
        {
            ErrorCode = ChatErrorCodes.RateLimited;
            return [new ErrorEvent(ChatErrorCodes.RateLimited, "混み合っています。しばらく待ってからもう一度お試しください。", traceId)];
        }

        ErrorCode = ChatErrorCodes.AgentError;
        return [new ErrorEvent(ChatErrorCodes.AgentError, "エージェントでエラーが発生しました。", traceId)];
    }

    private ErrorEvent CreateError(string? upstreamCode)
    {
        if (IsRateLimit(upstreamCode))
        {
            ErrorCode = ChatErrorCodes.RateLimited;
            return new ErrorEvent(ChatErrorCodes.RateLimited, "混み合っています。しばらく待ってからもう一度お試しください。", traceId);
        }

        ErrorCode = ChatErrorCodes.ResponseFailed;
        return new ErrorEvent(ChatErrorCodes.ResponseFailed, "応答を作れませんでした。もう一度お試しください。", traceId);
    }

    private static bool IsRateLimit(string? code) =>
        code is not null && code.Contains("rate_limit", StringComparison.OrdinalIgnoreCase);

    private void ReadResponseIds(JsonElement response)
    {
        if (GetString(response, "id") is { Length: > 0 } id)
        {
            ResponseId = id;
        }

        if (response.TryGetProperty("conversation", out var conversation))
        {
            var conversationIdValue = conversation.ValueKind switch
            {
                JsonValueKind.Object => GetString(conversation, "id"),
                JsonValueKind.String => conversation.GetString(),
                _ => null,
            };
            if (!string.IsNullOrEmpty(conversationIdValue))
            {
                ConversationId = conversationIdValue;
            }
        }
    }

    private static UsageEvent? ReadUsage(JsonElement response)
    {
        if (!response.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var input = GetInt64(usage, "input_tokens");
        var output = GetInt64(usage, "output_tokens");
        var total = GetInt64(usage, "total_tokens");
        return new UsageEvent(input, output, total == 0 ? input + output : total);
    }

    private static PlanUpdatedEvent? TryParsePlan(string arguments)
    {
        if (TryParseObject(arguments) is not { } root
            || !root.TryGetProperty("steps", out var steps)
            || steps.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var list = new List<PlanStep>();
        foreach (var step in steps.EnumerateArray())
        {
            if (list.Count >= MaxPlanSteps)
            {
                break;
            }

            if (step.ValueKind != JsonValueKind.Object || GetString(step, "title") is not { Length: > 0 } title)
            {
                continue;
            }

            var status = GetString(step, "status") switch
            {
                "in_progress" => "in_progress",
                "completed" => "completed",
                _ => "pending",
            };
            list.Add(new PlanStep(Truncate(title, 200)!, status));
        }

        return new PlanUpdatedEvent(list);
    }

    /// <summary>
    /// <c>function_call_output</c> の <c>output</c> を文字列にする。文字列、JSON のオブジェクト、
    /// <c>[{ "type": "input_text", "text": "..." }]</c> の形のどれでも受け付ける。
    /// </summary>
    private static string ReadOutputText(JsonElement item)
    {
        if (!item.TryGetProperty("output", out var output))
        {
            return string.Empty;
        }

        switch (output.ValueKind)
        {
            case JsonValueKind.String:
                return output.GetString() ?? string.Empty;
            case JsonValueKind.Array:
                {
                    var texts = new List<string>();
                    foreach (var part in output.EnumerateArray())
                    {
                        if (part.ValueKind == JsonValueKind.Object && GetString(part, "text") is { } text)
                        {
                            texts.Add(text);
                        }
                        else if (part.ValueKind == JsonValueKind.String && part.GetString() is { } value)
                        {
                            texts.Add(value);
                        }
                    }

                    return texts.Count > 0 ? string.Concat(texts) : output.GetRawText();
                }

            case JsonValueKind.Null or JsonValueKind.Undefined:
                return string.Empty;
            default:
                return output.GetRawText();
        }
    }

    private static JsonElement? TryParseObject(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long GetInt64(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : 0;

    private static string? Truncate(string? value, int maxLength = MaxPreviewLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength] + "…";

    [LoggerMessage(Level = LogLevel.Debug, Message = "知らない Responses のイベントを捨てました: {EventType}")]
    private static partial void LogUnknownEvent(ILogger logger, string eventType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "成果物の情報が正しくないため、台帳に登録しませんでした: {Reason}")]
    private static partial void LogInvalidArtifact(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "エージェントの応答が失敗しました: {Code}")]
    private static partial void LogResponseFailed(ILogger logger, string code);

    [LoggerMessage(Level = LogLevel.Warning, Message = "エージェントのストリームでエラーを受け取りました: {Code}")]
    private static partial void LogStreamError(ILogger logger, string code);
}
