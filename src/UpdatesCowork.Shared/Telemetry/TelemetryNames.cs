namespace UpdatesCowork.Shared.Telemetry;

/// <summary>テレメトリの名前(8.3節)。スパン名、属性名、メトリック名はここの定数だけを使う。</summary>
public static class TelemetryNames
{
    public static class Sources
    {
        public const string Web = "UpdatesCowork.Web";
        public const string SiteProxy = "UpdatesCowork.SiteProxy";
        public const string Agent = "UpdatesCowork.Agent";
        public const string McpSdk = "Experimental.ModelContextProtocol";
    }

    public static class Spans
    {
        public const string ChatTurn = "chat.turn";
        public const string AgentConversationCreate = "agent.conversation.create";
        public const string ArtifactDownload = "artifact.download";
        public const string ArtifactSessionFileRead = "artifact.session_file.read";
        public const string ArtifactSessionDelete = "artifact.session.delete";
        public const string ProxyHtmlRewrite = "proxy.html_rewrite";
        public const string AzureUpdatesGet = "azure_updates.get";
        public const string AzureUpdatesSearch = "azure_updates.search";
        public const string OfficeWordBuild = "office.word.build";
        public const string OfficePptxBuild = "office.pptx.build";
        public const string ArtifactSave = "artifact.save";
        public const string ContentSafetyShieldPrompt = "content_safety.shield_prompt";
        public const string McpToolExecute = "mcp.tool.execute";
    }

    public static class Attributes
    {
        public const string ThreadId = "auc.thread_id";
        public const string ConversationId = "gen_ai.conversation.id";
        public const string ResponseId = "gen_ai.response.id";
        public const string PageUpdateId = "auc.page.update_id";
        public const string Action = "auc.action";
        public const string UserKey = "auc.user_key";
        public const string TtftMs = "auc.ttft_ms";
        public const string Events = "auc.events";
        public const string ArtifactId = "auc.artifact.id";
        public const string ArtifactKind = "auc.artifact.kind";
        public const string ArtifactStorage = "auc.artifact.storage";
        public const string UrlPath = "url.path";
        public const string BytesIn = "auc.bytes_in";
        public const string BytesOut = "auc.bytes_out";
        public const string BridgeInjected = "auc.bridge_injected";
        public const string UpdateId = "auc.update.id";
        public const string CacheHit = "auc.cache_hit";
        public const string Items = "auc.items";
        public const string ValidationErrors = "auc.validation_errors";
        public const string Bytes = "auc.bytes";
        public const string AttackDetected = "auc.attack_detected";
        public const string ClientTraceId = "auc.client_trace_id";
        public const string Outcome = "auc.outcome";
        public const string ErrorType = "error.type";
        public const string ToolName = "gen_ai.tool.name";
        public const string FeedbackRating = "auc.feedback.rating";
        public const string FeedbackComment = "auc.feedback.comment";
    }

    public static class Metrics
    {
        public const string ChatTurns = "auc.chat.turns";
        public const string ChatTtft = "auc.chat.ttft";
        public const string ChatDuration = "auc.chat.duration";
        public const string Feedback = "auc.feedback";
        public const string ArtifactsCreated = "auc.artifacts.created";
        public const string OfficeBuildDuration = "auc.office.build.duration";
        public const string ToolCalls = "auc.tool.calls";
        public const string ToolDuration = "auc.tool.duration";
        public const string ContentSafetyDetections = "auc.content_safety.detections";
    }

    /// <summary>メトリックの次元名。</summary>
    public static class Dimensions
    {
        public const string Action = "action";
        public const string Outcome = "outcome";
        public const string Rating = "rating";
        public const string Kind = "kind";
        public const string Tool = "tool";
    }

    /// <summary><c>auc.chat.turns</c> の <c>outcome</c> の値。</summary>
    public static class Outcomes
    {
        public const string Completed = "completed";
        public const string Incomplete = "incomplete";
        public const string Error = "error";
        public const string Timeout = "timeout";
        public const string Ok = "ok";
    }

    /// <summary>フィードバックのログのメッセージ。</summary>
    public const string FeedbackLogMessage = "auc.feedback";
}
