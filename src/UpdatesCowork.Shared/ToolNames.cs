namespace UpdatesCowork.Shared;

/// <summary>エージェントのツール名(5.3節)。</summary>
public static class ToolNames
{
    public const string SearchAzureUpdates = "search_azure_updates";
    public const string GetAzureUpdate = "get_azure_update";
    public const string UpdatePlan = "update_plan";
    public const string CreateWordReport = "create_word_report";
    public const string CreatePowerPointDeck = "create_powerpoint_deck";

    /// <summary>成果物を作るツール。</summary>
    public static IReadOnlyList<string> ArtifactTools { get; } = [CreateWordReport, CreatePowerPointDeck];

    public static bool IsArtifactTool(string? name) =>
        name is CreateWordReport or CreatePowerPointDeck;
}

/// <summary>エージェントの名前などの固定値。</summary>
public static class AgentNames
{
    public const string AgentName = "updates-cowork";
}
