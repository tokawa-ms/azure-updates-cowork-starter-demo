using System.Diagnostics;
using UpdatesCowork.Shared.Telemetry;

namespace UpdatesCowork.SiteProxy;

/// <summary>siteproxy が自分で作るスパンのソース(設計書8.3節)。</summary>
internal static class SiteProxyTelemetry
{
    public static readonly ActivitySource Source = new(TelemetryNames.Sources.SiteProxy);
}
