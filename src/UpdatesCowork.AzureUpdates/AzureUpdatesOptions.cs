using Microsoft.Extensions.Configuration;

namespace UpdatesCowork.AzureUpdates;

/// <summary>データ API のクライアントの設定。</summary>
public sealed class AzureUpdatesOptions
{
    /// <summary>データ API の基底 URL の設定キー。</summary>
    public const string ApiBaseKey = "AZURE_UPDATES_API_BASE";

    /// <summary>固定データのフォルダーの設定キー。指定するとネットワークを使わない。</summary>
    public const string FixturesDirectoryKey = "AZURE_UPDATES_FIXTURES_DIR";

    /// <summary>User-Agent に付ける値。</summary>
    public const string UserAgent = "AzureUpdatesCowork/1.0";

    /// <summary>既定のデータ API の基底 URL。</summary>
    public static Uri DefaultApiBase { get; } = new("https://www.microsoft.com/releasecommunications/api/v2/azure");

    /// <summary>個別の更新をキャッシュに置く時間。</summary>
    public TimeSpan ItemCacheDuration { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>一覧をキャッシュに置く時間。</summary>
    public TimeSpan ListCacheDuration { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>データ API の基底 URL(末尾の <c>/</c> なし)。</summary>
    public Uri ApiBase { get; init; } = DefaultApiBase;

    /// <summary>固定データのフォルダー(絶対パス)。null なら HTTP を使う。</summary>
    public string? FixturesDirectory { get; init; }

    /// <summary>設定(環境変数)から読む。値がおかしいときは起動時に分かるよう例外にする。</summary>
    public static AzureUpdatesOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var apiBase = DefaultApiBase;
        var rawBase = configuration[ApiBaseKey];
        if (!string.IsNullOrWhiteSpace(rawBase))
        {
            if (!Uri.TryCreate(rawBase.Trim(), UriKind.Absolute, out var parsed) || !IsAllowedScheme(parsed))
            {
                throw new InvalidOperationException(
                    $"{ApiBaseKey} には https の絶対 URL を指定してください(http はローカルのテスト用に localhost だけ使えます)。");
            }

            apiBase = parsed;
        }

        var fixtures = configuration[FixturesDirectoryKey];
        return new AzureUpdatesOptions
        {
            ApiBase = new Uri(apiBase.GetLeftPart(UriPartial.Path).TrimEnd('/')),
            FixturesDirectory = string.IsNullOrWhiteSpace(fixtures) ? null : Path.GetFullPath(fixtures.Trim()),
        };
    }

    private static bool IsAllowedScheme(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
}
