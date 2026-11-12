using Microsoft.Net.Http.Headers;
using UpdatesCowork.SiteProxy.Transforms;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace UpdatesCowork.SiteProxy;

/// <summary>YARP のルート、クラスター、変換(設計書5.1節「ルート」「変換の規則」)。</summary>
internal static class SiteProxyRoutes
{
    public const string UpstreamClusterId = "upstream";
    public const string DataApiClusterId = "data-api";
    public const string DataApiRoutePrefix = "/__rc";

    /// <summary>要求1件あたりのタイムアウト。</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    // 中継するのは GET と HEAD だけ。ほかのメソッドはルーティングが 405 を返す。
    private static readonly string[] ReadOnlyMethods = [HttpMethods.Get, HttpMethods.Head];

    public static IReadOnlyList<RouteConfig> CreateRoutes(SiteProxyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var routes = new List<RouteConfig>();
        if (options.ProxyDataApi)
        {
            routes.Add(new RouteConfig
            {
                RouteId = "data-api",
                ClusterId = DataApiClusterId,
                Match = new RouteMatch { Path = DataApiRoutePrefix + "/{**path}", Methods = ReadOnlyMethods },
            }.WithTransformPathRouteValues(options.DataApiPathPrefix + "{**path}"));
        }

        routes.Add(new RouteConfig
        {
            RouteId = "site",
            ClusterId = UpstreamClusterId,
            Match = new RouteMatch { Path = "/{**catch-all}", Methods = ReadOnlyMethods },
        });

        return routes;
    }

    public static IReadOnlyList<ClusterConfig> CreateClusters(SiteProxyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var clusters = new List<ClusterConfig> { CreateCluster(UpstreamClusterId, options.NormalizedUpstreamOrigin) };
        if (options.ProxyDataApi)
        {
            clusters.Add(CreateCluster(DataApiClusterId, options.NormalizedDataApiOrigin));
        }

        return clusters;
    }

    public static void ConfigureTransforms(TransformBuilderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // HTML を書き換えるため、上流から圧縮なしで受け取る。
        context.AddRequestHeaderRemove(HeaderNames.AcceptEncoding);

        // X-Forwarded-* を付けない。イングレスが付けた値(利用者の IP)も上流へは渡さない。
        context.AddXForwarded(ForwardedTransformActions.Remove);
        context.AddRequestHeaderRemove("Forwarded");

        var headerRewriter = context.Services.GetRequiredService<ResponseHeaderRewriter>();
        var htmlRewriter = context.Route.ClusterId == UpstreamClusterId
            ? context.Services.GetRequiredService<HtmlRewriteTransform>()
            : null;

        context.AddResponseTransform(async transformContext =>
        {
            if (transformContext.ProxyResponse is null)
            {
                return;
            }

            // 本文を書き出す前にヘッダーを書き換える。
            headerRewriter.Apply(transformContext.HttpContext.Response.Headers);
            if (htmlRewriter is not null)
            {
                await htmlRewriter.ApplyAsync(transformContext).ConfigureAwait(false);
            }
        });
    }

    private static ClusterConfig CreateCluster(string clusterId, string origin) => new()
    {
        ClusterId = clusterId,
        Destinations = new Dictionary<string, DestinationConfig>
        {
            [clusterId] = new() { Address = origin + "/" },
        },
        HttpRequest = new ForwarderRequestConfig { ActivityTimeout = RequestTimeout },
    };
}
