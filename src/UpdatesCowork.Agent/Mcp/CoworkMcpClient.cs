using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using UpdatesCowork.Shared;

namespace UpdatesCowork.Agent.Mcp;

internal sealed class CoworkMcpClient : IAsyncDisposable
{
    private McpClient? _client;
    private IList<McpClientTool>? _tools;

    internal async Task ConnectAsync(Uri endpoint, HttpClient http, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = endpoint, TransportMode = HttpTransportMode.StreamableHttp },
            http, loggerFactory, ownsHttpClient: false);
        _client = await McpClient.CreateAsync(transport, new McpClientOptions
        {
            ClientInfo = new Implementation { Name = "updates-cowork-agent", Version = "1.0.0" },
        }, loggerFactory, cancellationToken).ConfigureAwait(false);
        _tools = await _client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        string[] expected = [ToolNames.SearchAzureUpdates, ToolNames.GetAzureUpdate, ToolNames.UpdatePlan,
            ToolNames.CreateWordReport, ToolNames.CreatePowerPointDeck];
        if (!_tools.Select(tool => tool.Name).Order(StringComparer.Ordinal).SequenceEqual(expected.Order(StringComparer.Ordinal)))
        {
            throw new InvalidOperationException("MCP サーバーのツール一覧が許可リストと一致しません。");
        }
    }

    internal IList<AITool> CreateTools(bool requireApproval, Func<string?> sessionIdProvider)
    {
        if (_client is null || _tools is null)
        {
            throw new InvalidOperationException("MCP クライアントが接続されていません。");
        }

        return _tools.Select(tool =>
        {
            AIFunction function = new JsonMcpFunction(tool, _client, sessionIdProvider);
            return (AITool)(requireApproval && ToolNames.IsArtifactTool(tool.Name)
                ? new ApprovalRequiredAIFunction(function) : function);
        }).ToList();
    }

    internal static JsonElement ReadResult(CallToolResult result)
    {
        if (result.StructuredContent is { ValueKind: JsonValueKind.Object } json)
        {
            return json.Clone();
        }

        if (result.Content is [TextContentBlock text])
        {
            using var document = JsonDocument.Parse(text.Text);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                return document.RootElement.Clone();
            }
        }

        throw new InvalidDataException("MCP ツールから JSON オブジェクトが返されませんでした。");
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class JsonMcpFunction(McpClientTool tool, McpClient client, Func<string?> sessionIdProvider)
        : DelegatingAIFunction(tool)
    {
        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            // モデルの引数ではなく、ホストが管理するセッションだけをプロトコルのメタデータに載せる。
            var metadata = new JsonObject();
            if (sessionIdProvider() is { } sessionId)
            {
                if (!McpInvocationContext.IsValidSessionId(sessionId))
                {
                    throw new InvalidOperationException("Hosted Agent のセッション ID が正しくありません。");
                }

                metadata[McpInvocationContext.SessionMetadataKey] = sessionId;
            }

            var result = await client.CallToolAsync(Name, arguments,
                options: new RequestOptions { Meta = metadata }, cancellationToken: cancellationToken).ConfigureAwait(false);
            var json = ReadResult(result);
            if (result.IsError == true && (!json.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.False))
            {
                throw new InvalidDataException("MCP のエラー結果の形式が正しくありません。");
            }

            return json;
        }
    }
}
