using System.Diagnostics;
using Microsoft.Extensions.Logging;
using UpdatesCowork.Shared.Telemetry;

namespace UpdatesCowork.AzureUpdates;

/// <summary>データ API のスパン(8.3節)。エージェントのプロセスで動くので、ソース名は <c>UpdatesCowork.Agent</c>。</summary>
internal static class AzureUpdatesTelemetry
{
    public static ActivitySource Source { get; } = new(TelemetryNames.Sources.Agent);

    public static Activity? StartGet(string id)
    {
        var activity = Source.StartActivity(TelemetryNames.Spans.AzureUpdatesGet);
        activity?.SetTag(TelemetryNames.Attributes.UpdateId, id);
        return activity;
    }

    public static Activity? StartSearch() => Source.StartActivity(TelemetryNames.Spans.AzureUpdatesSearch);
}

/// <summary>ログの定義。</summary>
internal static partial class AzureUpdatesLog
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning, Message = "Azure の更新情報の取得に失敗しました。対象: {Target}")]
    public static partial void FetchFailed(ILogger logger, string target, Exception exception);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning, Message = "固定データを読めませんでした。ファイル: {FileName}")]
    public static partial void FixtureReadFailed(ILogger logger, string fileName, Exception exception);
}
