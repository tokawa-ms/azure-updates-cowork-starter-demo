using System.Diagnostics;
using System.Diagnostics.Metrics;
using UpdatesCowork.Shared.Telemetry;

namespace UpdatesCowork.Agent;

internal sealed class AgentTelemetry : IDisposable
{
    internal ActivitySource Source { get; } = new(TelemetryNames.Sources.Agent);
    private readonly Meter _meter = new(TelemetryNames.Sources.Agent);
    internal Counter<long> ToolCalls { get; }
    internal Histogram<double> ToolDuration { get; }
    internal Counter<long> ArtifactsCreated { get; }
    internal Counter<long> SafetyDetections { get; }
    internal Histogram<double> OfficeBuildDuration { get; }

    public AgentTelemetry()
    {
        ToolCalls = _meter.CreateCounter<long>(TelemetryNames.Metrics.ToolCalls);
        ToolDuration = _meter.CreateHistogram<double>(TelemetryNames.Metrics.ToolDuration, "ms");
        ArtifactsCreated = _meter.CreateCounter<long>(TelemetryNames.Metrics.ArtifactsCreated);
        SafetyDetections = _meter.CreateCounter<long>(TelemetryNames.Metrics.ContentSafetyDetections);
        OfficeBuildDuration = _meter.CreateHistogram<double>(TelemetryNames.Metrics.OfficeBuildDuration, "ms");
    }

    public void Dispose()
    {
        Source.Dispose();
        _meter.Dispose();
    }
}
