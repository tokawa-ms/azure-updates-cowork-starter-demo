using Azure.Core;
using Microsoft.Agents.AI;
using UpdatesCowork.Agent.Artifacts;
using UpdatesCowork.Agent.Tools;
using UpdatesCowork.Agent.Mcp;
using UpdatesCowork.AzureUpdates;
using UpdatesCowork.Office;

namespace UpdatesCowork.Agent;

internal static class ToolServices
{
    internal static void AddTo(IServiceCollection services, AgentRuntimeOptions options, TokenCredential credential, IConfiguration configuration)
    {
        services.AddSingleton(options);
        services.AddSingleton(credential);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<AgentTelemetry>();
        services.AddAzureUpdatesClient(configuration);
        services.AddOfficeDocuments();
        services.AddHttpClient<PromptShieldClient>(client => client.Timeout = TimeSpan.FromSeconds(20));
        services.AddSingleton<AgentSkillsProvider>(sp => new AgentSkillsProvider(
            Path.Combine(AppContext.BaseDirectory, "Skills"),
            options: new AgentSkillsProviderOptions
            {
                DisableLoadSkillApproval = true,
                DisableReadSkillResourceApproval = true,
            }, loggerFactory: sp.GetRequiredService<ILoggerFactory>()));
        services.AddSingleton<IArtifactStore>(sp => options.ArtifactsMode switch
        {
            "Local" => new LocalArtifactStore(options, sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<AgentTelemetry>()),
            "SessionFiles" => new SessionArtifactStore(options.SessionDirectory,
                () => McpInvocationContext.SessionId.Value,
                sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<AgentTelemetry>()),
            _ => throw new InvalidOperationException("ARTIFACTS_MODE の値が正しくありません。"),
        });
        services.AddSingleton<CoworkTools>();
        services.AddSingleton<ToolCallMiddleware>();
        services.AddSingleton<CoworkMcpServer>();
        services.AddSingleton<CoworkMcpClient>();
    }
}
