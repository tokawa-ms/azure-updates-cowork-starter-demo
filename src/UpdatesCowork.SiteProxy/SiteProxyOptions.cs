using Microsoft.Extensions.Options;

namespace UpdatesCowork.SiteProxy;

/// <summary><c>appsettings.json</c> の <c>SiteProxy</c> 節(設計書5.1節)。</summary>
internal sealed class SiteProxyOptions
{
    public const string SectionName = "SiteProxy";

    /// <summary>中継先のオリジン。https の絶対オリジンに限る。</summary>
    public string UpstreamOrigin { get; set; } = "https://azure.microsoft.com";

    /// <summary>web が iframe で最初に開くパス。siteproxy のルートには使わない。</summary>
    public string StartPath { get; set; } = "/ja-jp/updates/";

    /// <summary>iframe の親として許可するオリジン(web の公開 URL)。</summary>
    public IList<string> AllowedParentOrigins { get; } = [];

    public bool InjectBridge { get; set; } = true;

    public bool RewriteAbsoluteUrls { get; set; } = true;

    /// <summary>true のときだけ <c>/__rc/{**path}</c> をデータ API へ中継する。</summary>
    public bool ProxyDataApi { get; set; }

    public string DataApiOrigin { get; set; } = "https://www.microsoft.com";

    public string DataApiPathPrefix { get; set; } = "/releasecommunications/";

    /// <summary>クライアント IP ごとの1分あたりの要求数の上限。</summary>
    public int RateLimitPerMinute { get; set; } = 240;

    /// <summary>検証済みの値から、末尾の <c>/</c> を除いた正規形のオリジンを返す。</summary>
    public string NormalizedUpstreamOrigin => NormalizeOrigin(UpstreamOrigin);

    public string NormalizedDataApiOrigin => NormalizeOrigin(DataApiOrigin);

    public IReadOnlyList<string> NormalizedParentOrigins => [.. AllowedParentOrigins.Select(NormalizeOrigin)];

    /// <summary>設定を読み込んで検証する。誤りがあれば <see cref="OptionsValidationException"/> を投げる。</summary>
    public static SiteProxyOptions Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new SiteProxyOptions();
        configuration.GetSection(SectionName).Bind(options);

        var errors = options.Validate();
        if (errors.Count > 0)
        {
            throw new OptionsValidationException(SectionName, typeof(SiteProxyOptions), errors);
        }

        return options;
    }

    /// <summary>設定の誤りを列挙する。空なら正しい。</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (!IsOrigin(UpstreamOrigin, httpsOnly: true))
        {
            errors.Add($"{SectionName}:{nameof(UpstreamOrigin)} は https の絶対オリジン(パスなし)にしてください。");
        }

        if (string.IsNullOrEmpty(StartPath) || !StartPath.StartsWith('/') || StartPath.StartsWith("//", StringComparison.Ordinal))
        {
            errors.Add($"{SectionName}:{nameof(StartPath)} は / で始まるパスにしてください。");
        }

        for (var i = 0; i < AllowedParentOrigins.Count; i++)
        {
            if (!IsOrigin(AllowedParentOrigins[i], httpsOnly: false))
            {
                errors.Add($"{SectionName}:{nameof(AllowedParentOrigins)}:{i} は http または https の絶対オリジン(パスなし)にしてください。");
            }
        }

        if (ProxyDataApi)
        {
            if (!IsOrigin(DataApiOrigin, httpsOnly: true))
            {
                errors.Add($"{SectionName}:{nameof(DataApiOrigin)} は https の絶対オリジン(パスなし)にしてください。");
            }

            if (string.IsNullOrEmpty(DataApiPathPrefix)
                || !DataApiPathPrefix.StartsWith('/')
                || !DataApiPathPrefix.EndsWith('/')
                || DataApiPathPrefix.StartsWith("//", StringComparison.Ordinal)
                || DataApiPathPrefix.IndexOfAny(['?', '#', '{', '}', '*', '\\']) >= 0)
            {
                errors.Add($"{SectionName}:{nameof(DataApiPathPrefix)} は / で始まり / で終わるパスにしてください。");
            }
        }

        if (RateLimitPerMinute <= 0)
        {
            errors.Add($"{SectionName}:{nameof(RateLimitPerMinute)} は1以上にしてください。");
        }

        return errors;
    }

    private static bool IsOrigin(string? value, bool httpsOnly)
    {
        // CSP のヘッダーと JavaScript に埋め込むため、区切りになりうる文字は最初から拒む。
        if (string.IsNullOrWhiteSpace(value)
            || value.IndexOfAny(['?', '#', '@', ';', ',', '\'', '"', '\\', '<', '>', ' ', '\t', '\r', '\n']) >= 0
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var schemeOk = uri.Scheme == Uri.UriSchemeHttps || (!httpsOnly && uri.Scheme == Uri.UriSchemeHttp);
        return schemeOk
            && !string.IsNullOrEmpty(uri.Host)
            && uri.AbsolutePath == "/";
    }

    // ブラウザーの event.origin と同じ形(小文字のホスト、既定のポートは省略、末尾の / なし)にそろえる。
    private static string NormalizeOrigin(string value) =>
        new Uri(value, UriKind.Absolute).GetLeftPart(UriPartial.Authority);
}
