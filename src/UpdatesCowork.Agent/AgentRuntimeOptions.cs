namespace UpdatesCowork.Agent;

public sealed record AgentRuntimeOptions(
    Uri ProjectEndpoint,
    string ModelDeployment,
    string ArtifactsMode,
    string LocalDirectory,
    string SessionDirectory,
    PromptShieldsMode ShieldsMode,
    Uri? ContentSafetyEndpoint,
    bool RequireApproval)
{
    public static AgentRuntimeOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var endpoint = RequiredHttpsUri(configuration["FOUNDRY_PROJECT_ENDPOINT"], "FOUNDRY_PROJECT_ENDPOINT");
        var model = configuration["AZURE_AI_MODEL_DEPLOYMENT_NAME"];
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException("AZURE_AI_MODEL_DEPLOYMENT_NAME を設定してください。");
        }

        var mode = configuration["ARTIFACTS_MODE"] ?? "Local";
        if (mode is not ("Local" or "SessionFiles"))
        {
            throw new InvalidOperationException("ARTIFACTS_MODE は Local または SessionFiles にしてください。");
        }

        var sessionDirectory = mode == "SessionFiles"
            ? RequiredDirectory(configuration["HOME"] ?? Environment.GetEnvironmentVariable("HOME"), "HOME")
            : string.Empty;
        var shieldsText = configuration["PROMPT_SHIELDS_MODE"] ?? "Off";
        if (!Enum.TryParse<PromptShieldsMode>(shieldsText, ignoreCase: true, out var shields) || !Enum.IsDefined(shields))
        {
            throw new InvalidOperationException("PROMPT_SHIELDS_MODE は Off、Annotate、Block のいずれかにしてください。");
        }

        var approvalText = configuration["REQUIRE_APPROVAL_FOR_ARTIFACTS"] ?? "false";
        if (!bool.TryParse(approvalText, out var approval))
        {
            throw new InvalidOperationException("REQUIRE_APPROVAL_FOR_ARTIFACTS は true または false にしてください。");
        }

        return new AgentRuntimeOptions(endpoint, model, mode,
            Path.GetFullPath(configuration["ARTIFACTS_LOCAL_DIR"] ?? "../../.artifacts"),
            sessionDirectory, shields,
            shields == PromptShieldsMode.Off ? null : RequiredHttpsUri(configuration["CONTENT_SAFETY_ENDPOINT"], "CONTENT_SAFETY_ENDPOINT"),
            approval);
    }

    private static string RequiredDirectory(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
        {
            throw new InvalidOperationException($"{name} に絶対パスを設定してください。");
        }

        return Path.GetFullPath(value);
    }

    private static Uri RequiredHttpsUri(string? value, string name)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0
            || uri.Query.Length != 0 || uri.Fragment.Length != 0)
        {
            throw new InvalidOperationException($"{name} に HTTPS のエンドポイントを設定してください。");
        }

        return uri;
    }
}

public enum PromptShieldsMode { Off, Annotate, Block }
