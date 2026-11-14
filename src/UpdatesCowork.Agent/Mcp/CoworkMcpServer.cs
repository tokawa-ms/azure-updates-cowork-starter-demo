using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using UpdatesCowork.Agent.Tools;
using UpdatesCowork.Agent.Learning;
using UpdatesCowork.Shared;
using UpdatesCowork.Shared.Telemetry;

namespace UpdatesCowork.Agent.Mcp;

internal sealed class CoworkMcpServer(AgentTelemetry telemetry, ILogger<CoworkMcpServer> logger, LearningTraceCollector traces)
{
    internal const string AccessHeader = "X-AUC-MCP-Access";
    internal string AccessToken { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    internal WebApplication CreateHost(IServiceProvider services, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.WebHost.ConfigureKestrel(server => server.Limits.MaxRequestBodySize = 512 * 1024);
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(services.GetRequiredService<ILoggerFactory>());
        var tools = services.GetRequiredService<CoworkTools>().CreateTools(requireApproval: false).Cast<AIFunction>().ToArray();
        builder.Services.AddMcpServer(options =>
            options.ServerInfo = new Implementation { Name = "updates-cowork-tools", Version = "1.0.0" })
            .WithHttpTransport(options => options.Stateless = true)
            .WithTools(tools.Select(tool => McpServerTool.Create(tool, new McpServerToolCreateOptions
            {
                UseStructuredContent = true,
                ReadOnly = !ToolNames.IsArtifactTool(tool.Name),
                Destructive = false,
                Idempotent = !ToolNames.IsArtifactTool(tool.Name),
                OpenWorld = tool.Name is ToolNames.SearchAzureUpdates or ToolNames.GetAzureUpdate,
            })))
            .WithRequestFilters(filters => filters.AddCallToolFilter(next => async (request, cancellationToken) =>
            {
                LearningTraceSession? trace = null;
                var traceNode = request.Params?.Meta?[LearningTraceCollector.SessionMetadataKey];
                var parentNode = request.Params?.Meta?[LearningTraceCollector.ParentMetadataKey];
                string? traceId = null;
                string? parentId = null;
                if ((traceNode is not null && (traceNode is not JsonValue traceValue || !traceValue.TryGetValue(out traceId)))
                    || (parentNode is not null && (parentNode is not JsonValue parentValue || !parentValue.TryGetValue(out parentId)))
                    || (traceId is not null || parentId is not null)
                    && (traceId is null || parentId is null || !traces.TryGetSession(traceId, parentId, out trace)))
                {
                    AgentLog.InvalidInput(logger, "mcp.trace");
                    return Error("学習トレースの関連付けが正しくありません。");
                }
                using var scope = trace is null ? null : LearningTraceCollector.Enter(trace);
                using var activity = trace is null
                    ? telemetry.Source.StartActivity(TelemetryNames.Spans.McpToolExecute)
                    : telemetry.Source.StartActivity(TelemetryNames.Spans.McpToolExecute, ActivityKind.Internal, trace.GetContext(parentId!));
                activity?.SetTag(TelemetryNames.Attributes.ToolName, request.Params?.Name);
                var previous = McpInvocationContext.SessionId.Value;
                try
                {
                    var session = request.Params?.Meta?[McpInvocationContext.SessionMetadataKey]?.GetValue<string>();
                    if (session is not null && !McpInvocationContext.IsValidSessionId(session))
                    {
                        AgentLog.InvalidInput(logger, request.Params?.Name ?? "mcp");
                        activity?.SetStatus(ActivityStatusCode.Error);
                        return Error("セッション情報が正しくありません。");
                    }

                    var tool = tools.FirstOrDefault(tool => tool.Name == request.Params?.Name);
                    if (tool is not null && tool.JsonSchema.TryGetProperty("required", out var required))
                    {
                        var missing = required.EnumerateArray().Select(property => property.GetString()!)
                            .Where(name => request.Params?.Arguments is not { } arguments
                                || !arguments.TryGetValue(name, out var value) || value.ValueKind == JsonValueKind.Null).ToArray();
                        if (missing.Length > 0)
                        {
                            AgentLog.InvalidInput(logger, tool.Name);
                            activity?.SetStatus(ActivityStatusCode.Error);
                            activity?.SetTag(TelemetryNames.Attributes.Outcome, TelemetryNames.Outcomes.Error);
                            return Error($"必須の引数がありません: {string.Join(", ", missing)}。ツール定義に従って引数を指定してください。");
                        }
                    }

                    McpInvocationContext.SessionId.Value = session;
                    var result = await next(request, cancellationToken).ConfigureAwait(false);
                    var json = CoworkMcpClient.ReadResult(result);
                    if (json.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False)
                    {
                        result.IsError = true;
                        activity?.SetStatus(ActivityStatusCode.Error);
                    }

                    activity?.SetTag(TelemetryNames.Attributes.Outcome,
                        result.IsError == true ? TelemetryNames.Outcomes.Error : TelemetryNames.Outcomes.Ok);
                    return result;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    activity?.SetTag(TelemetryNames.Attributes.Outcome, "cancelled");
                    throw;
                }
                catch (Exception ex)
                {
                    // ツール境界で失敗を MCP のエラー結果に変換し、例外本文は外に出さない。
                    AgentLog.ToolFailed(logger, request.Params?.Name ?? "mcp", ex.GetType().Name);
                    activity?.SetTag(TelemetryNames.Attributes.ErrorType, ex.GetType().FullName);
                    activity?.SetStatus(ActivityStatusCode.Error);
                    return Error("internal_error");
                }
                finally
                {
                    McpInvocationContext.SessionId.Value = previous;
                }
            }));
        configure?.Invoke(builder);
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            var token = context.Request.Headers[AccessHeader].ToString();
            if (context.Request.Headers.ContainsKey("Origin")
                || (context.Connection.RemoteIpAddress is { } remote && !IPAddress.IsLoopback(remote))
                || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(AccessToken)))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await next(context).ConfigureAwait(false);
        });
        app.MapMcp("/mcp");
        return app;
    }

    private static CallToolResult Error(string message)
    {
        var json = ToolCallMiddleware.Error(message);
        return new CallToolResult
        {
            IsError = true,
            StructuredContent = json,
            Content = [new TextContentBlock { Text = json.GetRawText() }],
        };
    }
}
