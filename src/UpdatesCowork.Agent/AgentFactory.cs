using Azure.AI.Projects;
using Azure.Core;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UpdatesCowork.Agent.Mcp;
using UpdatesCowork.Agent.Learning;
using UpdatesCowork.Shared;
using UpdatesCowork.Shared.Telemetry;

namespace UpdatesCowork.Agent;

internal static class AgentFactory
{
    internal static AIAgent Create(AgentRuntimeOptions options, TokenCredential credential, IServiceProvider services)
    {
        var skills = services.GetRequiredService<AgentSkillsProvider>();
        var tools = services.GetRequiredService<CoworkMcpClient>();
        var project = new AIProjectClient(options.ProjectEndpoint, credential);
        var agent = project.AsAIAgent(new ChatClientAgentOptions
        {
            Name = services.GetRequiredService<IConfiguration>()["FOUNDRY_AGENT_NAME"] ?? AgentNames.AgentName,
            Description = "Azure の更新情報を要約し、Word と PowerPoint を作る学習用エージェント",
            ChatOptions = new ChatOptions
            {
                ModelId = options.ModelDeployment,
                Instructions = LoadInstructions(),
                Tools = tools.CreateTools(options.RequireApproval,
                    () => FoundryAgentRequestContext.Current?.SessionId ?? FoundryEnvironment.SessionId),
                MaxOutputTokens = 8192,
            },
            AIContextProviders = [new CurrentTimeContextProvider(services.GetRequiredService<TimeProvider>()), skills],
        }, clientFactory: client => new LearningChatClient(client, services.GetRequiredService<AgentTelemetry>(), options.ModelDeployment),
            loggerFactory: services.GetRequiredService<ILoggerFactory>(), services: services);
        var middleware = services.GetRequiredService<ToolCallMiddleware>();
        return agent.AsBuilder().Use(middleware.InvokeAsync).Use(middleware.RunAsync)
            .UseOpenTelemetry(TelemetryNames.Sources.Agent, telemetry =>
                telemetry.EnableSensitiveData = string.Equals(
                    services.GetRequiredService<IConfiguration>()["OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT"],
                    "true", StringComparison.OrdinalIgnoreCase))
            .Build();
    }

    internal static string LoadInstructions()
    {
        using var stream = typeof(AgentFactory).Assembly.GetManifestResourceStream(
            "UpdatesCowork.Agent.Instructions.agent-instructions.md")
            ?? throw new InvalidOperationException("エージェントの指示文が見つかりません。");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
