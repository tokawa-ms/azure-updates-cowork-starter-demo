namespace UpdatesCowork.Agent.Mcp;

internal static class McpInvocationContext
{
    internal const string SessionMetadataKey = "auc/session_id";
    internal static AsyncLocal<string?> SessionId { get; } = new();

    internal static bool IsValidSessionId(string? value) =>
        value is { Length: > 0 and <= 200 }
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
