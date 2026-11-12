using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Azure.Core;
using UpdatesCowork.Shared.Telemetry;

namespace UpdatesCowork.Agent;

internal sealed class PromptShieldClient(
    HttpClient httpClient, TokenCredential credential, AgentRuntimeOptions options, AgentTelemetry telemetry)
{
    private static readonly TokenRequestContext TokenContext = new(["https://cognitiveservices.azure.com/.default"]);

    internal async Task<SafetyResult> InspectAsync(string content, CancellationToken cancellationToken)
    {
        if (options.ShieldsMode == PromptShieldsMode.Off)
        {
            return new(false, false, false);
        }

        using var activity = telemetry.Source.StartActivity(TelemetryNames.Spans.ContentSafetyShieldPrompt);
        var attackDetected = false;
        var token = await credential.GetTokenAsync(TokenContext, cancellationToken).ConfigureAwait(false);
        // 文書ごとの上限を守り、分割境界の前後も同じ文書で検査する。
        for (var start = 0; start < Math.Max(1, content.Length); start += 9000)
        {
            var chunk = content.Substring(start, Math.Min(10000, content.Length - start));
            using var request = new HttpRequestMessage(HttpMethod.Post,
                new Uri(options.ContentSafetyEndpoint!, "/contentsafety/text:shieldPrompt?api-version=2024-09-01"))
            {
                Content = JsonContent.Create(new ShieldRequest(string.Empty, [chunk]), AgentJsonContext.Default.ShieldRequest),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync(
                AgentJsonContext.Default.ShieldResponse, cancellationToken).ConfigureAwait(false);
            if (result?.DocumentsAnalysis is not { Count: 1 } analyses || analyses[0] is null)
            {
                activity?.SetStatus(ActivityStatusCode.Error);
                throw new InvalidDataException("Prompt Shields の応答が不正です。");
            }

            attackDetected |= analyses[0].AttackDetected;
            if (start + chunk.Length >= content.Length)
            {
                break;
            }
        }

        activity?.SetTag(TelemetryNames.Attributes.AttackDetected, attackDetected);
        if (attackDetected)
        {
            telemetry.SafetyDetections.Add(1);
        }

        return new(true, attackDetected, attackDetected && options.ShieldsMode == PromptShieldsMode.Block);
    }
}
