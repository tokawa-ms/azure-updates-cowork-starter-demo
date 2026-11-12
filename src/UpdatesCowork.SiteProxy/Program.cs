using System.Text;
using System.Threading.RateLimiting;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using UpdatesCowork.Shared.Telemetry;
using UpdatesCowork.SiteProxy;
using UpdatesCowork.SiteProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

builder.UseMicrosoftOpenTelemetry(o => o.Exporters = TelemetrySetup.ResolveExporters(builder.Configuration));
builder.Services.AddOpenTelemetry().ConfigureResource(resource =>
    resource.AddService(builder.Configuration["OTEL_SERVICE_NAME"] ?? TelemetryNames.Sources.SiteProxy));
builder.Services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddSource(TelemetryNames.Sources.SiteProxy));
builder.Services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddMeter(TelemetryNames.Sources.SiteProxy));

// 設定に誤りがあれば、ここで例外にして起動を止める。
var siteProxy = SiteProxyOptions.Load(builder.Configuration);
builder.Services.AddSingleton(siteProxy);
builder.Services.AddSingleton<ResponseHeaderRewriter>();
builder.Services.AddSingleton<HtmlRewriteTransform>();
builder.Services.AddSingleton<BridgeScript>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        context.Request.Path.StartsWithSegments("/healthz", StringComparison.OrdinalIgnoreCase)
            ? RateLimitPartition.GetNoLimiter("healthz")
            : RateLimitPartition.GetFixedWindowLimiter(ClientAddress.Resolve(context), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = siteProxy.RateLimitPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
});

builder.Services.AddReverseProxy()
    .LoadFromMemory(SiteProxyRoutes.CreateRoutes(siteProxy), SiteProxyRoutes.CreateClusters(siteProxy))
    .AddTransforms(SiteProxyRoutes.ConfigureTransforms);

var app = builder.Build();

// bridge.js が読めなければ起動を止める。
var bridgeScript = app.Services.GetRequiredService<BridgeScript>();

// プロキシした内容を検索エンジンに載せないよう、すべての応答に付ける。
app.Use((context, next) =>
{
    context.Response.OnStarting(static state =>
    {
        ((HttpResponse)state).Headers["X-Robots-Tag"] = "noindex, nofollow";
        return Task.CompletedTask;
    }, context.Response);
    return next(context);
});

app.UseRateLimiter();

string[] readOnlyMethods = [HttpMethods.Get, HttpMethods.Head];

app.MapMethods("/healthz", readOnlyMethods, () => Results.Ok());

app.MapMethods("/robots.txt", readOnlyMethods, () =>
    Results.Text("User-agent: *\nDisallow: /\n", "text/plain", Encoding.UTF8));

app.MapMethods("/__auc/bridge.js", readOnlyMethods, (HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-cache";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    return Results.Text(bridgeScript.Content, "text/javascript", Encoding.UTF8);
});

// siteproxy 自身のパスは上流へ流さない。
app.Map("/__auc/{**path}", () => Results.NotFound());
if (!siteProxy.ProxyDataApi)
{
    app.Map(SiteProxyRoutes.DataApiRoutePrefix + "/{**path}", () => Results.NotFound());
}

app.MapReverseProxy();

await app.RunAsync().ConfigureAwait(false);
