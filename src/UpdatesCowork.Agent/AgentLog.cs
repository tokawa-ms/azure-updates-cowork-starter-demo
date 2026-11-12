namespace UpdatesCowork.Agent;

internal static partial class AgentLog
{
    [LoggerMessage(1, LogLevel.Warning, "ツール {Tool} の入力が不正です。")]
    internal static partial void InvalidInput(ILogger logger, string tool);

    [LoggerMessage(2, LogLevel.Error, "ツール {Tool} が失敗しました。例外の種類: {ErrorType}")]
    internal static partial void ToolFailed(ILogger logger, string tool, string errorType);

    [LoggerMessage(3, LogLevel.Warning, "ツール呼び出しの上限に達しました。")]
    internal static partial void LimitReached(ILogger logger);
}
