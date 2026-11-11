using Microsoft.Extensions.Configuration;
using Microsoft.OpenTelemetry;

namespace UpdatesCowork.Shared.Telemetry;

/// <summary>OpenTelemetry の送り先を設定から決める(8.2節)。</summary>
public static class TelemetrySetup
{
    public const string AppInsightsConnectionStringKey = "APPLICATIONINSIGHTS_CONNECTION_STRING";
    public const string OtlpEndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>
    /// Application Insights の接続文字列があれば Azure Monitor、OTLP のエンドポイントがあれば OTLP に送る。
    /// どちらもなければ送らない。
    /// </summary>
    public static ExportTarget ResolveExporters(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var targets = ExportTarget.None;
        if (!string.IsNullOrWhiteSpace(configuration[AppInsightsConnectionStringKey]))
        {
            targets |= ExportTarget.AzureMonitor;
        }

        if (!string.IsNullOrWhiteSpace(configuration[OtlpEndpointKey]))
        {
            targets |= ExportTarget.Otlp;
        }

        return targets;
    }
}
