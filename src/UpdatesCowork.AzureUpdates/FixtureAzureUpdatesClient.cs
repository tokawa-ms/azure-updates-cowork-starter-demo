using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UpdatesCowork.Shared.Telemetry;

namespace UpdatesCowork.AzureUpdates;

/// <summary>
/// ネットワークを使わず、固定データ(<c>list.json</c> と <c>&lt;id&gt;.json</c>)を返すクライアント。テストと評価で使う。
/// ファイルの形はデータ API の応答と同じ。
/// </summary>
public sealed class FixtureAzureUpdatesClient : IAzureUpdatesClient
{
    /// <summary>一覧の固定データのファイル名。</summary>
    public const string ListFileName = "list.json";

    private readonly string _directory;
    private readonly ILogger _logger;
    private readonly Lazy<IReadOnlyList<AzureUpdate>> _list;

    public FixtureAzureUpdatesClient(string directory, ILogger<FixtureAzureUpdatesClient>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"固定データのフォルダーが見つかりません: {directory}");
        }

        _directory = Path.GetFullPath(directory);
        _logger = logger ?? NullLogger<FixtureAzureUpdatesClient>.Instance;
        _list = new Lazy<IReadOnlyList<AzureUpdate>>(LoadList, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public async Task<AzureUpdate?> GetAsync(string id, CancellationToken ct)
    {
        if (!AzureUpdateQueryRules.IsValidId(id))
        {
            throw new ArgumentException("ID は半角英数字とハイフンで 200 文字までです。", nameof(id));
        }

        using var activity = AzureUpdatesTelemetry.StartGet(id);
        activity?.SetTag(TelemetryNames.Attributes.CacheHit, false);

        // ID は英数字とハイフンだけなので、フォルダーの外を指すことはない
        var path = Path.Combine(_directory, id + ".json");
        AzureUpdate? item = null;
        if (File.Exists(path))
        {
            try
            {
                var stream = File.OpenRead(path);
                await using (stream.ConfigureAwait(false))
                {
                    item = await JsonSerializer.DeserializeAsync(stream, AzureUpdatesJsonContext.Default.AzureUpdate, ct).ConfigureAwait(false);
                }
            }
            catch (JsonException ex)
            {
                AzureUpdatesLog.FixtureReadFailed(_logger, id + ".json", ex);
                throw new AzureUpdatesUnavailableException("固定データを読めませんでした。", ex);
            }
        }

        // 個別のファイルがなければ一覧の中から探す
        item ??= _list.Value.FirstOrDefault(u => string.Equals(u.Id, id, StringComparison.OrdinalIgnoreCase));
        if (item is not null && !string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase))
        {
            item = null;
        }

        activity?.SetTag(TelemetryNames.Attributes.Items, item is null ? 0 : 1);
        return item;
    }

    public Task<AzureUpdateSearchResult> SearchAsync(AzureUpdateQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var normalized = AzureUpdateFilter.Normalize(query);
        using var activity = AzureUpdatesTelemetry.StartSearch();
        activity?.SetTag(TelemetryNames.Attributes.CacheHit, false);

        var serverSide = _list.Value.Where(u => AzureUpdateFilter.MatchesServer(u, normalized)).ToList();
        var fetched = serverSide.Take(AzureUpdateFilter.FetchSize(normalized)).ToList();
        var result = AzureUpdateFilter.Apply(fetched, normalized, serverSide.Count);
        activity?.SetTag(TelemetryNames.Attributes.Items, result.Items.Count);
        return Task.FromResult(result);
    }

    private List<AzureUpdate> LoadList()
    {
        var path = Path.Combine(_directory, ListFileName);
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            using var stream = File.OpenRead(path);
            var list = JsonSerializer.Deserialize(stream, AzureUpdatesJsonContext.Default.AzureUpdateListResponse);
            return (list?.Value ?? [])
                .Where(u => u is not null && AzureUpdateQueryRules.IsValidId(u.Id))
                .OrderByDescending(u => u.Modified ?? DateTimeOffset.MinValue)
                .ToList();
        }
        catch (JsonException ex)
        {
            AzureUpdatesLog.FixtureReadFailed(_logger, ListFileName, ex);
            throw new AzureUpdatesUnavailableException("固定データの一覧を読めませんでした。", ex);
        }
    }
}
