using System.Globalization;
using System.Threading.RateLimiting;
using Azure.Core;
using Azure.Identity;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Microsoft.OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using UpdatesCowork.Shared;
using UpdatesCowork.Shared.Contracts;
using UpdatesCowork.Shared.Telemetry;
using UpdatesCowork.AzureUpdates;
using UpdatesCowork.Web.AgentGateway;
using UpdatesCowork.Web.Artifacts;
using UpdatesCowork.Web.Chat;
using UpdatesCowork.Web.Configuration;
using UpdatesCowork.Web.Endpoints;
using UpdatesCowork.Web.Identity;
using UpdatesCowork.Web.Registry;
using UpdatesCowork.Web.Security;
using UpdatesCowork.Web.Telemetry;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// 観測(8.2節)。自分のソースとメーターだけを足す。
builder.UseMicrosoftOpenTelemetry(o => o.Exporters = TelemetrySetup.ResolveExporters(configuration));
builder.Services.AddOpenTelemetry().ConfigureResource(resource =>
    resource.AddService(configuration["OTEL_SERVICE_NAME"] ?? TelemetryNames.Sources.Web));
builder.Services.ConfigureOpenTelemetryTracerProvider(tracing =>
    tracing.AddSource(TelemetryNames.Sources.Web, TelemetryNames.Sources.Agent));
builder.Services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddMeter(TelemetryNames.Sources.Web));

// 設定。
builder.Services.Configure<AgentOptions>(configuration.GetSection(AgentOptions.SectionName));
builder.Services.Configure<ArtifactOptions>(configuration.GetSection(ArtifactOptions.SectionName));
builder.Services.Configure<WebOptions>(configuration.GetSection(WebOptions.SectionName));
builder.Services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));
var authMode = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>()?.Mode ?? AuthMode.Anonymous;

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AucJsonContext.Default);
    options.SerializerOptions.TypeInfoResolverChain.Insert(1, WebJsonContext.Default);
});
builder.Services.AddProblemDetails();
builder.Services.AddAzureUpdatesClient(configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<WebTelemetry>();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

// 資格情報(5.2節)。Azure ではユーザー割り当てマネージド ID、ローカルでは DefaultAzureCredential。
var azureClientId = configuration["AZURE_CLIENT_ID"];
TokenCredential credential = string.IsNullOrEmpty(azureClientId)
    ? new DefaultAzureCredential()
    : new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(azureClientId));
builder.Services.AddSingleton(credential);

// Data Protection のキーはプロセス内だけで保持する。再起動後の Cookie 再利用はできない。
builder.Services.AddDataProtection().SetApplicationName("UpdatesCowork.Web").UseEphemeralDataProtectionProvider();

// 台帳はメモリー上だけに保持し、プロセス再起動で破棄する。
builder.Services.AddSingleton<IChatRegistry, InMemoryChatRegistry>();

// Hosted Agent のセッションファイルを読む。ローカル開発では明示的に Local に切り替える。
builder.Services.AddTransient<IArtifactReader>(sp =>
{
    var options = sp.GetRequiredService<IOptions<ArtifactOptions>>().Value;
    if (options.Mode == ArtifactReaderMode.Local)
    {
        if (string.IsNullOrWhiteSpace(options.LocalDir))
        {
            throw new InvalidOperationException("Artifacts:LocalDir を設定してください。");
        }

        var environment = sp.GetRequiredService<IHostEnvironment>();
        return new LocalArtifactReader(Path.Combine(environment.ContentRootPath, options.LocalDir));
    }

    return sp.GetRequiredService<SessionFilesArtifactReader>();
});

// エージェントの呼び出し口。HttpClient のタイムアウトは無制限にし、取り消しは CancellationToken で行う。
builder.Services.AddTransient<BearerTokenHandler>();
builder.Services.AddHttpClient<SessionFilesArtifactReader>(client => client.Timeout = Timeout.InfiniteTimeSpan)
    .AddHttpMessageHandler<BearerTokenHandler>();
builder.Services.AddTransient<ISessionArtifactManager>(sp => sp.GetRequiredService<SessionFilesArtifactReader>());
builder.Services.AddHttpClient<ResponsesAgentGateway>(client => client.Timeout = Timeout.InfiniteTimeSpan)
    .AddHttpMessageHandler<BearerTokenHandler>();
builder.Services.AddTransient<FakeAgentGateway>();
builder.Services.AddTransient<IAgentGateway>(sp =>
    sp.GetRequiredService<IOptions<AgentOptions>>().Value.Mode == AgentMode.Fake
        ? sp.GetRequiredService<FakeAgentGateway>()
        : sp.GetRequiredService<ResponsesAgentGateway>());
builder.Services.AddTransient<ChatOrchestrator>();

// 任意の Entra ID モード。
if (authMode == AuthMode.EntraId)
{
    builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApp(configuration.GetSection("AzureAd"));
    builder.Services.AddAuthorization();
}

// Container Apps のイングレスが付ける X-Forwarded-* を信頼する(サインインの戻り先を https にするため)。
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// レート制限(5.2節「保護」)。POST /api/chat と /api/chat/approval だけが対象。
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        var response = context.HttpContext.Response;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        await response.WriteAsJsonAsync(
            new ApiError(ChatErrorCodes.RateLimited, "依頼が多すぎます。しばらく待ってからもう一度お試しください。"),
            WebJsonContext.Default.ApiError,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    };
    options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
        PartitionedRateLimiter.Create<HttpContext, string>(context => IsChatPost(context)
            ? RateLimitPartition.GetFixedWindowLimiter("rate:" + (UserKeys.Get(context) ?? "unknown"), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            })
            : RateLimitPartition.GetNoLimiter(string.Empty)),
        PartitionedRateLimiter.Create<HttpContext, string>(context => IsChatPost(context)
            ? RateLimitPartition.GetConcurrencyLimiter("user:" + (UserKeys.Get(context) ?? "unknown"), _ => new ConcurrencyLimiterOptions
            {
                PermitLimit = 2,
                QueueLimit = 0,
            })
            : RateLimitPartition.GetNoLimiter(string.Empty)),
        PartitionedRateLimiter.Create<HttpContext, string>(context => IsChatPost(context)
            ? RateLimitPartition.GetConcurrencyLimiter("global", _ => new ConcurrencyLimiterOptions
            {
                PermitLimit = 20,
                QueueLimit = 0,
            })
            : RateLimitPartition.GetNoLimiter(string.Empty)));
});

var app = builder.Build();

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
    app.UseHsts();
}

app.UseMiddleware<SecurityHeadersMiddleware>();
if (authMode == AuthMode.EntraId)
{
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseMiddleware<EntraUserMiddleware>();
}
else
{
    app.UseMiddleware<AnonymousUserMiddleware>();
}

app.UseMiddleware<RequestGuardMiddleware>();
app.UseRateLimiter();
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    // ビルド工程がなくファイル名にハッシュがないので、毎回更新を確かめさせる。
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache",
});

app.MapConfigEndpoints(authMode);
app.MapUpdatesEndpoints();
app.MapChatEndpoints();
app.MapArtifactEndpoints();
app.MapFeedbackEndpoints();

await app.RunAsync().ConfigureAwait(false);

static bool IsChatPost(HttpContext context) =>
    HttpMethods.IsPost(context.Request.Method)
    && context.Request.Path.StartsWithSegments("/api/chat", StringComparison.OrdinalIgnoreCase);

/// <summary>テスト(WebApplicationFactory)から参照するための宣言。</summary>
public partial class Program;
