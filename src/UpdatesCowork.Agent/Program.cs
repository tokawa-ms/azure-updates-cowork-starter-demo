using Azure.Core;
using Azure.Identity;
using Microsoft.Agents.AI.Foundry.Hosting;
using Microsoft.Agents.AI;
using Microsoft.OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using UpdatesCowork.Agent;
using UpdatesCowork.Agent.Mcp;
using UpdatesCowork.Agent.Learning;
using Azure.AI.AgentServer.Responses;
using UpdatesCowork.Shared;
using UpdatesCowork.Shared.Telemetry;

var builder = WebApplication.CreateBuilder(args);
if (string.IsNullOrEmpty(builder.Configuration["ASPNETCORE_URLS"])
    && string.IsNullOrEmpty(builder.Configuration["ASPNETCORE_HTTP_PORTS"]))
{
    builder.WebHost.UseUrls("http://localhost:8088");
}

var options = AgentRuntimeOptions.FromConfiguration(builder.Configuration);
TokenCredential credential = string.IsNullOrEmpty(builder.Configuration["FOUNDRY_AGENT_NAME"])
    ? new DefaultAzureCredential()
    : new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned);
ToolServices.AddTo(builder.Services, options, credential, builder.Configuration);
builder.Services.AddSingleton(sp => AgentFactory.Create(options, credential, sp));
builder.Services.AddKeyedSingleton<AIAgent>(builder.Configuration["FOUNDRY_AGENT_NAME"] ?? AgentNames.AgentName,
    (sp, _) => sp.GetRequiredService<AIAgent>());
builder.AddFoundryResponses();
var handlerRegistration = builder.Services.Single(service => service.ServiceType == typeof(ResponseHandler));
var createHandler = handlerRegistration.ImplementationFactory
    ?? throw new InvalidOperationException("固定 SDK の Responses ハンドラーの登録を確認してください。");
builder.Services.Remove(handlerRegistration);
builder.Services.AddSingleton<ResponseHandler>(sp => new LearningResponseHandler(
    (ResponseHandler)createHandler(sp), sp.GetRequiredService<LearningTraceCollector>(),
    sp.GetRequiredService<AgentTelemetry>(), sp.GetRequiredService<ILogger<LearningResponseHandler>>()));
builder.UseMicrosoftOpenTelemetry(o => o.Exporters = TelemetrySetup.ResolveExporters(builder.Configuration));
builder.Services.AddOpenTelemetry().ConfigureResource(resource =>
    resource.AddService(builder.Configuration["OTEL_SERVICE_NAME"] ?? TelemetryNames.Sources.Agent));
builder.Services.ConfigureOpenTelemetryTracerProvider(tracing =>
    tracing.AddSource("Azure.AI.AgentServer.Responses", TelemetryNames.Sources.Agent, TelemetryNames.Sources.McpSdk));
builder.Services.ConfigureOpenTelemetryMeterProvider(metrics =>
    metrics.AddMeter("Azure.AI.AgentServer.Responses", TelemetryNames.Sources.Agent, TelemetryNames.Sources.McpSdk));
await using var app = builder.Build();
var mcpServer = app.Services.GetRequiredService<CoworkMcpServer>();
await using var mcpHost = mcpServer.CreateHost(app.Services);
await mcpHost.StartAsync().ConfigureAwait(false);
using var mcpHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
mcpHttp.DefaultRequestHeaders.Add(CoworkMcpServer.AccessHeader, mcpServer.AccessToken);
using var startupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
await app.Services.GetRequiredService<CoworkMcpClient>().ConnectAsync(
    new Uri(mcpHost.Urls.Single() + "/mcp"), mcpHttp, app.Services.GetRequiredService<ILoggerFactory>(),
    startupTimeout.Token).ConfigureAwait(false);
app.MapFoundryResponses();
try
{
    await app.RunAsync().ConfigureAwait(false);
}
finally
{
    await mcpHost.StopAsync().ConfigureAwait(false);
}

public partial class Program;