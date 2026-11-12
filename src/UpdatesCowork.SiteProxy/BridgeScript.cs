using System.Text;
using System.Text.Json;

namespace UpdatesCowork.SiteProxy;

/// <summary>
/// <c>/__auc/bridge.js</c> の本文。<c>wwwroot/__auc/bridge.js</c> の先頭に、
/// 許可する親オリジンなどの定数を付けたもの(設計書5.1節のルート3)。
/// </summary>
internal sealed class BridgeScript
{
    public const string RelativePath = "__auc/bridge.js";

    public BridgeScript(IWebHostEnvironment environment, SiteProxyOptions options)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(options);

        var file = environment.WebRootFileProvider.GetFileInfo(RelativePath);
        if (!file.Exists)
        {
            throw new InvalidOperationException($"wwwroot/{RelativePath} が見つかりません。");
        }

        using var reader = new StreamReader(file.CreateReadStream(), Encoding.UTF8);
        Content = BuildPrefix(options) + reader.ReadToEnd();
    }

    public string Content { get; }

    /// <summary>
    /// スクリプトの先頭に付ける定数の宣言を作る。
    /// 文字列は JSON の文字列として書き、<c>&lt;</c> や引用符をエスケープしてスクリプトを壊させない。
    /// </summary>
    internal static string BuildPrefix(SiteProxyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var parentOrigins = string.Join(", ", options.NormalizedParentOrigins.Select(Quote));
        var dataApiPrefix = options.ProxyDataApi ? options.NormalizedDataApiOrigin + options.DataApiPathPrefix : string.Empty;
        var proxyDataApi = options.ProxyDataApi ? "true" : "false";

        return $"""
            const AUC_PARENT_ORIGINS = [{parentOrigins}];
            const AUC_UPSTREAM_ORIGIN = {Quote(options.NormalizedUpstreamOrigin)};
            const AUC_PROXY_DATA_API = {proxyDataApi};
            const AUC_DATA_API_PREFIX = {Quote(dataApiPrefix)};

            """;
    }

    private static string Quote(string value) => "\"" + JsonEncodedText.Encode(value).ToString() + "\"";
}
