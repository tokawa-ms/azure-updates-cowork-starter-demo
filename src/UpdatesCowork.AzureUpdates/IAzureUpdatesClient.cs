namespace UpdatesCowork.AzureUpdates;

/// <summary>Azure の更新情報のデータ API のクライアント(5.5節)。</summary>
public interface IAzureUpdatesClient
{
    /// <summary>
    /// 1 件を取得する。見つからないときは null。
    /// </summary>
    /// <exception cref="ArgumentException">ID の形が正しくないとき。</exception>
    /// <exception cref="AzureUpdatesUnavailableException">データ API から取得できなかったとき。</exception>
    Task<AzureUpdate?> GetAsync(string id, CancellationToken ct);

    /// <summary>
    /// 条件に合う更新を新しい順に返す。
    /// </summary>
    /// <exception cref="ArgumentException">条件が <see cref="AzureUpdateQueryRules.Validate"/> を通らないとき。</exception>
    /// <exception cref="AzureUpdatesUnavailableException">データ API から取得できなかったとき。</exception>
    Task<AzureUpdateSearchResult> SearchAsync(AzureUpdateQuery query, CancellationToken ct);
}

/// <summary>データ API から取得できなかったことを表す。</summary>
public sealed class AzureUpdatesUnavailableException : Exception
{
    public AzureUpdatesUnavailableException()
        : base("Azure の更新情報を取得できませんでした。")
    {
    }

    public AzureUpdatesUnavailableException(string message)
        : base(message)
    {
    }

    public AzureUpdatesUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
