namespace UpdatesCowork.AzureUpdates;

/// <summary>一覧の絞り込み。HTTP 版と固定データ版で同じ規則を使う。</summary>
internal static class AzureUpdateFilter
{
    /// <summary>検証済みの条件を正規化する(前後の空白、状態の表記)。</summary>
    public static AzureUpdateQuery Normalize(AzureUpdateQuery query)
    {
        var errors = AzureUpdateQueryRules.Validate(query);
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(query));
        }

        return query with
        {
            Keyword = AzureUpdateQueryRules.NormalizeText(query.Keyword),
            Product = AzureUpdateQueryRules.NormalizeText(query.Product),
            Status = AzureUpdateStatuses.TryNormalize(query.Status, out var status) ? status : null,
        };
    }

    /// <summary>キーワードか製品名で絞るときは、データ API から多めに取って手元で絞る。</summary>
    public static bool NeedsLocalFilter(AzureUpdateQuery query) => query.Keyword is not null || query.Product is not null;

    /// <summary>データ API に求める件数。</summary>
    public static int FetchSize(AzureUpdateQuery query) =>
        NeedsLocalFilter(query) ? AzureUpdateQueryRules.ScanSize : query.Top;

    /// <summary>キーワード(タイトルと製品名)と製品名で絞る。大文字小文字は区別しない。</summary>
    public static bool MatchesLocal(AzureUpdate update, AzureUpdateQuery query)
    {
        if (query.Keyword is { } keyword
            && !Contains(update.Title, keyword)
            && !(update.Products?.Any(p => Contains(p, keyword)) ?? false))
        {
            return false;
        }

        if (query.Product is { } product && !(update.Products?.Any(p => Contains(p, product)) ?? false))
        {
            return false;
        }

        return true;
    }

    /// <summary>データ API の <c>$filter</c> と同じ絞り込み(固定データ版で使う)。</summary>
    public static bool MatchesServer(AzureUpdate update, AzureUpdateQuery query)
    {
        if (query.Status == AzureUpdateStatuses.Retirement)
        {
            if (!(update.Tags?.Any(t => string.Equals(t, AzureUpdateStatuses.RetirementTag, StringComparison.OrdinalIgnoreCase)) ?? false))
            {
                return false;
            }
        }
        else if (query.Status is { } status && !string.Equals(update.Status, status, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (query.ModifiedSince is { } since)
        {
            var threshold = new DateTimeOffset(since.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            if (update.Modified is not { } modified || modified < threshold)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>手元で絞り込み、件数をそろえた結果を作る。</summary>
    public static AzureUpdateSearchResult Apply(IReadOnlyList<AzureUpdate> fetched, AzureUpdateQuery query, long? totalCount)
    {
        var items = fetched.Where(u => MatchesLocal(u, query)).Take(query.Top).ToList();
        return new AzureUpdateSearchResult(items, fetched.Count, totalCount);
    }

    private static bool Contains(string? value, string term) =>
        value is not null && value.Contains(term, StringComparison.OrdinalIgnoreCase);
}
