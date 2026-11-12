using Microsoft.Extensions.Logging;
using UpdatesCowork.Shared.Contracts;

namespace UpdatesCowork.Office.Tests;

/// <summary>テストで使う入力と道具。</summary>
internal static class TestSpecs
{
    public const string UpdateUrl = "https://azure.microsoft.com/updates?id=559242";
    public const string LearnUrl = "https://learn.microsoft.com/azure/container-apps/overview";

    // 2026-11-10T16:00Z は日本時間で 2026-11-11 01:00
    public static readonly DateTimeOffset UtcNow = new(2026, 11, 10, 16, 0, 0, TimeSpan.Zero);

    public static readonly TimeZoneInfo Jst =
        TimeZoneInfo.CreateCustomTimeZone("JST", TimeSpan.FromHours(9), "JST", "JST");

    public static IReadOnlyList<SourceRef> Sources { get; } =
    [
        new("Azure の更新情報 (ID 559242)", UpdateUrl),
        new("Azure Container Apps の概要", LearnUrl),
    ];

    public static WordReportSpec Word() => new(
        FileName: "azure-update-559242-summary",
        Title: "Azure Container Apps express の一般提供",
        Subtitle: "Azure の更新情報の要約",
        Language: "ja",
        Sections:
        [
            new("概要", 1,
                Paragraphs: ["Azure Container Apps express が一般提供になりました。", "2 つ目の段落です。"],
                Bullets: ["すぐに使える", "料金は従量課金"],
                Table: new ReportTable(["項目", "内容"], [["状態", "一般提供"], ["対象", "全リージョン"]])),
            new("詳細", 2, Paragraphs: ["詳細の段落です。"]),
            new("次にすること", 1, Bullets: ["ドキュメントを読む"]),
        ],
        Sources: Sources);

    public static DeckSpec Deck() => new(
        FileName: "azure-update-559242-deck",
        Title: "Azure Container Apps express の一般提供",
        Subtitle: "Azure の更新情報の要約 (2026-11-11)",
        Slides:
        [
            new("何が変わったか", ["一般提供になった", "SLA が付いた"], "話すときの補足"),
            new("使い方", ["az containerapp up で作れる"]),
            new("次にすること", ["試す", "評価する"], "1 行目\n2 行目"),
        ],
        Sources: Sources);

    public static OfficeDocumentFactory CreateFactory(ListLogger<OfficeDocumentFactory>? logger = null) =>
        new(logger ?? new ListLogger<OfficeDocumentFactory>(), new FixedTimeProvider(UtcNow, Jst));
}

/// <summary>決まった時刻を返す <see cref="TimeProvider"/>。</summary>
internal sealed class FixedTimeProvider(DateTimeOffset utcNow, TimeZoneInfo localTimeZone) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;

    public override TimeZoneInfo LocalTimeZone => localTimeZone;
}

/// <summary>ログをリストにためる。</summary>
internal sealed class ListLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, EventId EventId, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, eventId, formatter(state, exception)));
}
