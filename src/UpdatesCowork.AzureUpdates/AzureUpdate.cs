using System.Text.Json.Serialization;

namespace UpdatesCowork.AzureUpdates;

/// <summary>
/// Azure の更新情報 1 件。データ API の JSON と同じ形(説明文は HTML のまま)。
/// </summary>
public sealed record AzureUpdate
{
    public required string Id { get; init; }

    public string? Title { get; init; }

    /// <summary>説明文の HTML。モデルに渡すときは <see cref="HtmlToText"/> でテキストにする。</summary>
    public string? Description { get; init; }

    /// <summary>状態。例: <c>Launched</c>、<c>In preview</c>、<c>In development</c>。</summary>
    public string? Status { get; init; }

    public IReadOnlyList<string>? Products { get; init; }

    public IReadOnlyList<string>? ProductCategories { get; init; }

    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>一般提供の時期。例: <c>2026-11</c>。</summary>
    public string? GeneralAvailabilityDate { get; init; }

    public string? PreviewAvailabilityDate { get; init; }

    public string? PrivatePreviewAvailabilityDate { get; init; }

    public DateTimeOffset? Created { get; init; }

    public DateTimeOffset? Modified { get; init; }

    public string? Locale { get; init; }

    public IReadOnlyList<AzureUpdateAvailability>? Availabilities { get; init; }
}

/// <summary>提供時期の 1 段階。例: <c>{ "ring": "General Availability", "year": 2026, "month": "September" }</c>。</summary>
public sealed record AzureUpdateAvailability
{
    public string? Ring { get; init; }

    public int? Year { get; init; }

    /// <summary>月。データ API は月名の文字列で返すが、数値でも読めるようにしておく。</summary>
    [JsonConverter(typeof(LenientStringConverter))]
    public string? Month { get; init; }
}

/// <summary>一覧の問い合わせの結果。</summary>
/// <param name="Items">条件に合った更新(新しい順)。</param>
/// <param name="Scanned">データ API から受け取って調べた件数。</param>
/// <param name="TotalCount">データ API が返した全体の件数(<c>@odata.count</c>)。</param>
public sealed record AzureUpdateSearchResult(IReadOnlyList<AzureUpdate> Items, int Scanned, long? TotalCount);

/// <summary>データ API の一覧の応答。</summary>
internal sealed record AzureUpdateListResponse
{
    [JsonPropertyName("@odata.count")]
    public long? Count { get; init; }

    [JsonPropertyName("value")]
    public IReadOnlyList<AzureUpdate>? Value { get; init; }
}
