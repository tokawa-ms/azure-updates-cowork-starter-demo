using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using UpdatesCowork.Shared.Telemetry;

namespace UpdatesCowork.AzureUpdates;

/// <summary>
/// データ API を HTTP で呼ぶクライアント。再試行と打ち切りは DI で付ける標準の回復性ハンドラーが受け持つ。
/// 結果は <see cref="IMemoryCache"/> に置く(個別 10 分、一覧 5 分)。
/// </summary>
public sealed class AzureUpdatesClient : IAzureUpdatesClient
{
    private const string ItemCachePrefix = "azure-updates:item:";
    private const string ListCachePrefix = "azure-updates:list:";

    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly AzureUpdatesOptions _options;
    private readonly ILogger<AzureUpdatesClient> _logger;

    public AzureUpdatesClient(HttpClient http, IMemoryCache cache, AzureUpdatesOptions options, ILogger<AzureUpdatesClient> logger)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _http = http;
        _cache = cache;
        _options = options;
        _logger = logger;
    }

    public async Task<AzureUpdate?> GetAsync(string id, CancellationToken ct)
    {
        if (!AzureUpdateQueryRules.IsValidId(id))
        {
            throw new ArgumentException("ID は半角英数字とハイフンで 200 文字までです。", nameof(id));
        }

        using var activity = AzureUpdatesTelemetry.StartGet(id);
        var cacheKey = ItemCachePrefix + id;
        if (_cache.TryGetValue(cacheKey, out AzureUpdate? cached) && cached is not null)
        {
            activity?.SetTag(TelemetryNames.Attributes.CacheHit, true);
            activity?.SetTag(TelemetryNames.Attributes.Items, 1);
            return cached;
        }

        activity?.SetTag(TelemetryNames.Attributes.CacheHit, false);
        var uri = BuildItemUri(id);
        try
        {
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
            {
                activity?.SetTag(TelemetryNames.Attributes.Items, 0);
                return null;
            }

            response.EnsureSuccessStatusCode();
            var item = await response.Content
                .ReadFromJsonAsync(AzureUpdatesJsonContext.Default.AzureUpdate, ct)
                .ConfigureAwait(false);

            // 応答の ID が要求と違うものは採用しない
            if (item is null || !string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                activity?.SetTag(TelemetryNames.Attributes.Items, 0);
                return null;
            }

            _cache.Set(cacheKey, item, _options.ItemCacheDuration);
            activity?.SetTag(TelemetryNames.Attributes.Items, 1);
            return item;
        }
        catch (Exception ex) when (IsFetchFailure(ex, ct))
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.GetType().Name);
            AzureUpdatesLog.FetchFailed(_logger, "item", ex);
            throw new AzureUpdatesUnavailableException("Azure の更新情報を取得できませんでした。", ex);
        }
    }

    public async Task<AzureUpdateSearchResult> SearchAsync(AzureUpdateQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var normalized = AzureUpdateFilter.Normalize(query);
        using var activity = AzureUpdatesTelemetry.StartSearch();

        var uri = BuildListUri(normalized);
        var cacheKey = ListCachePrefix + uri.AbsoluteUri;
        if (!_cache.TryGetValue(cacheKey, out AzureUpdateListResponse? list) || list is null)
        {
            activity?.SetTag(TelemetryNames.Attributes.CacheHit, false);
            list = await FetchListAsync(uri, ct).ConfigureAwait(false);
            _cache.Set(cacheKey, list, _options.ListCacheDuration);
        }
        else
        {
            activity?.SetTag(TelemetryNames.Attributes.CacheHit, true);
        }

        var fetched = (list.Value ?? []).Where(u => u is not null && AzureUpdateQueryRules.IsValidId(u.Id)).ToList();
        var result = AzureUpdateFilter.Apply(fetched, normalized, list.Count);
        activity?.SetTag(TelemetryNames.Attributes.Items, result.Items.Count);
        return result;
    }

    /// <summary>個別の URL。<c>{基底}/{id}</c>。</summary>
    internal Uri BuildItemUri(string id) =>
        new(_options.ApiBase.AbsoluteUri.TrimEnd('/') + "/" + Uri.EscapeDataString(id));

    /// <summary>一覧の URL。正規化済みの条件を受け取る。</summary>
    internal Uri BuildListUri(AzureUpdateQuery normalized) =>
        new(_options.ApiBase.AbsoluteUri.TrimEnd('/') + ODataQueryBuilder.BuildListQuery(normalized, AzureUpdateFilter.FetchSize(normalized)));

    private async Task<AzureUpdateListResponse> FetchListAsync(Uri uri, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var list = await response.Content
                .ReadFromJsonAsync(AzureUpdatesJsonContext.Default.AzureUpdateListResponse, ct)
                .ConfigureAwait(false);
            return list ?? new AzureUpdateListResponse();
        }
        catch (Exception ex) when (IsFetchFailure(ex, ct))
        {
            System.Diagnostics.Activity.Current?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.GetType().Name);
            AzureUpdatesLog.FetchFailed(_logger, "list", ex);
            throw new AzureUpdatesUnavailableException("Azure の更新情報の一覧を取得できませんでした。", ex);
        }
    }

    // 呼び出し元による取り消しはそのまま伝え、それ以外(HTTP の失敗、打ち切り、回復性ハンドラーによる拒否、JSON の形の違い)は取得失敗として扱う
    private static bool IsFetchFailure(Exception ex, CancellationToken ct) =>
        !ct.IsCancellationRequested
        && ex is HttpRequestException or JsonException or OperationCanceledException or NotSupportedException or Polly.ExecutionRejectedException;
}
