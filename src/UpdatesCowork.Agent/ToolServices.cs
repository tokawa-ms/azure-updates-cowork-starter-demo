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
                SkillsInstructionPrompt = """
                    # スキルの利用条件
                    スキルの読み込みより先に、依頼が Azure の更新情報の範囲内か、対象が分かるかを確認してください。
                    「この更新」「いま見ている更新」の ID がページ情報にも会話にもない場合は、更新の選択または ID を依頼して終了します。
                    対象外・対象未確定の場合は、以下のスキルと一致する単語があっても load_skill を含めツールを一切呼びません。
                    製品名・キーワードのある検索依頼は、ページが未選択でも対象を検索できます。

                    <available_skills>
                    {skills}
                    </available_skills>

                    上の条件を満たした依頼だけ、対応する load_skill を呼び、返された手順に従います。
                    参照資料が必要なときは read_skill_resource に記載どおりのリソース名を指定します。
                    """,
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
