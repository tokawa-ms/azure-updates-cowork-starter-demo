using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.AI.Projects;
using Azure.Core;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UpdatesCowork.Agent;
using UpdatesCowork.Agent.Tools;
using UpdatesCowork.AzureUpdates;
using UpdatesCowork.Shared;
using UpdatesCowork.Shared.Contracts;

namespace UpdatesCowork.EvalRunner;

internal static class EvaluationDefinition
{
    private static readonly HashSet<string> Categories = ["summarize", "word", "pptx", "related", "no_context", "out_of_scope", "injection"];
    private static readonly HashSet<string> Tools = [ToolNames.GetAzureUpdate, ToolNames.SearchAzureUpdates,
        ToolNames.CreateWordReport, ToolNames.CreatePowerPointDeck, ToolNames.UpdatePlan];

    internal static async Task<EvaluationCase[]> LoadAsync(string path, CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false);
        var cases = lines.Where(line => !string.IsNullOrWhiteSpace(line)).Select(line =>
            JsonSerializer.Deserialize(line, EvalJsonContext.Default.EvaluationCase)
            ?? throw new InvalidDataException("空の評価ケースです。")).ToArray();
        if (cases.Length is 0 or > 100 || cases.Select(test => test.Id).Distinct(StringComparer.Ordinal).Count() != cases.Length)
        {
            throw new InvalidDataException("評価ケースは 1〜100 件で、ID が重複しないようにしてください。");
        }

        foreach (var test in cases)
        {
            if (!AzureUpdateQueryRules.IsValidId(test.Id) || !Categories.Contains(test.Category)
                || string.IsNullOrWhiteSpace(test.Query) || test.Query.Length > 2000
                || test.Language is not ("ja" or "en") || string.IsNullOrWhiteSpace(test.GroundTruth)
                || test.ExpectedArtifact is not (null or ArtifactKinds.Docx or ArtifactKinds.Pptx)
                || test.ExpectedTools is null || test.ExpectedTools.Any(tool => !Tools.Contains(tool))
                || test.MustInclude is null || test.MustInclude.Any(string.IsNullOrWhiteSpace)
                || (test.PageUpdateId is not null && !AzureUpdateQueryRules.IsValidId(test.PageUpdateId)))
            {
                throw new InvalidDataException($"評価ケース {test.Id} の形式が正しくありません。");
            }
        }

        return cases;
    }

    internal static async Task PrepareAsync(string dataset, string fixtures, string agent, string version, string judge,
        string output, int limit, CancellationToken cancellationToken)
    {
        if (!AzureUpdateQueryRules.IsValidId(agent) || !agent.EndsWith("-eval", StringComparison.Ordinal)
            || !version.All(char.IsAsciiDigit) || version.Length == 0 || limit is < 1 or > 100)
        {
            throw new ArgumentException("評価専用の -eval エージェント名、数値の版、1〜100 件の上限を指定してください。");
        }

        var cases = (await LoadAsync(dataset, cancellationToken).ConfigureAwait(false)).Take(limit).ToArray();
        var client = new FixtureAzureUpdatesClient(fixtures);
        var (definitions, skillInstructions) = await CreateToolDefinitionsAsync(fixtures, cancellationToken).ConfigureAwait(false);
        var instructions = AgentFactory.LoadInstructions() + "\n\n" + skillInstructions;
        var rows = new JsonArray();
        Directory.CreateDirectory(Path.Combine(output, "requests"));
        foreach (var test in cases)
        {
            var update = test.PageUpdateId is { } id
                ? await client.GetAsync(id, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidDataException($"固定データ {id} が見つかりません。")
                : null;
            var row = JsonSerializer.SerializeToNode(test, EvalJsonContext.Default.EvaluationCase)?.AsObject()
                ?? throw new InvalidDataException("評価データを変換できませんでした。");
            var pageContext = PageContextText.Format(update is null ? null : new PageContext(
                "https://azure.microsoft.com/updates?id=" + update.Id, update.Id, update.Title, "ja-JP")) ?? string.Empty;
            row["page_context"] = pageContext;
            var query = new JsonArray(QueryMessage("system", instructions));
            if (pageContext.Length > 0) query.Add(QueryMessage("developer", pageContext));
            query.Add(QueryMessage("user", test.Query));
            row["query"] = query;
            // Task Adherence は developer を取り出さないため、採点用だけ system 内に元の文脈を保持する。
            row["task_query"] = new JsonArray(
                QueryMessage("system", pageContext.Length == 0 ? instructions
                    : instructions + "\n\n# この依頼に付随する developer メッセージ（元のページ情報）\n" + pageContext),
                QueryMessage("user", test.Query));
            row["tool_definitions"] = definitions.DeepClone();
            row["context"] = update is null ? test.GroundTruth : HtmlToText.Convert(update.Description);
            rows.Add(new JsonObject { ["item"] = row });
            var messages = new JsonArray();
            if (pageContext.Length > 0) messages.Add(Message("developer", pageContext));
            messages.Add(Message("user", test.Query));
            await WriteAsync(Path.Combine(output, "requests", test.Id + ".json"),
                new JsonObject { ["input"] = messages, ["stream"] = false }, cancellationToken).ConfigureAwait(false);
        }

        var datasetHash = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(dataset, cancellationToken).ConfigureAwait(false)));
        using var fixtureHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in Directory.GetFiles(fixtures, "*.json").Order(StringComparer.Ordinal))
        {
            fixtureHash.AppendData(Encoding.UTF8.GetBytes(Path.GetFileName(file)));
            fixtureHash.AppendData(await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false));
        }

        var metadata = new JsonObject
        {
            ["dataset_sha256"] = datasetHash,
            ["fixtures_sha256"] = Convert.ToHexStringLower(fixtureHash.GetHashAndReset()),
            ["instructions_sha256"] = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(instructions))),
            ["agent_name"] = agent,
            ["agent_version"] = version,
            ["judge"] = judge,
            ["response_source"] = "recorded_hosted_response",
            ["task_query_format"] = "system_with_developer_context_v1",
        };
        var properties = new JsonObject();
        foreach (var field in new[] { "id", "page_context", "ground_truth", "context" })
        {
            properties[field] = new JsonObject { ["type"] = "string" };
        }
        properties["query"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "object" } };
        properties["task_query"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "object" } };
        properties["tool_definitions"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "object" } };
        properties["response"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "object" } };
        properties["output_items"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "object" } };
        var criteria = new JsonArray();
        foreach (var name in new[] { "task_adherence", "intent_resolution", "tool_call_accuracy" })
        {
            var mapping = new JsonObject
            {
                ["query"] = name == "task_adherence" ? "{{item.task_query}}" : "{{item.query}}",
                ["response"] = "{{item.response}}",
                ["tool_definitions"] = "{{item.tool_definitions}}",
            };
            criteria.Add(new JsonObject
            {
                ["type"] = "azure_ai_evaluator", ["name"] = name, ["evaluator_name"] = "builtin." + name,
                ["initialization_parameters"] = new JsonObject { ["model"] = judge },
                ["data_mapping"] = mapping,
            });
        }

        var definition = new JsonObject
        {
            ["name"] = "auc-core-fixed-data",
            ["metadata"] = metadata.DeepClone(),
            ["data_source_config"] = new JsonObject
            {
                ["type"] = "custom",
                ["item_schema"] = new JsonObject
                {
                    ["type"] = "object", ["properties"] = properties,
                    ["required"] = new JsonArray("id", "query", "task_query", "page_context", "ground_truth", "tool_definitions", "response", "output_items"),
                },
                ["include_sample_schema"] = false,
            },
            ["testing_criteria"] = criteria,
        };
        var run = new JsonObject
        {
            ["name"] = $"auc-core-{agent}-v{version}",
            ["metadata"] = metadata.DeepClone(),
            ["data_source"] = new JsonObject
            {
                ["type"] = "jsonl",
                ["source"] = new JsonObject { ["type"] = "file_content", ["content"] = rows },
            },
        };
        Directory.CreateDirectory(output);
        await WriteAsync(Path.Combine(output, "definition.json"), definition, cancellationToken).ConfigureAwait(false);
        await WriteAsync(Path.Combine(output, "run-request.json"), run, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(output, "cases.json"),
            JsonSerializer.Serialize(cases, EvalJsonContext.Default.EvaluationCaseArray), cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"{cases.Length} 件の固定データ評価を準備しました。Agent: {agent} / version {version}");
    }

    internal static async Task AttachResponsesAsync(string output, CancellationToken cancellationToken)
    {
        var path = Path.Combine(output, "run-request.json");
        var request = JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false))?.AsObject()
            ?? throw new InvalidDataException("評価要求がありません。");
        var rows = request["data_source"]?["source"]?["content"]?.AsArray()
            ?? throw new InvalidDataException("評価データがありません。");
        foreach (var row in rows)
        {
            var item = row?["item"]?.AsObject() ?? throw new InvalidDataException("評価データが不正です。");
            var id = item["id"]?.GetValue<string>();
            if (!AzureUpdateQueryRules.IsValidId(id)) throw new InvalidDataException("評価ケース ID が不正です。");
            using var response = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(output, "responses", id + ".json"), cancellationToken).ConfigureAwait(false));
            var root = response.RootElement;
            var agent = root.GetProperty("agent_reference");
            if (agent.GetProperty("name").GetString() != request["metadata"]?["agent_name"]?.GetValue<string>()
                || agent.GetProperty("version").GetString() != request["metadata"]?["agent_version"]?.GetValue<string>())
            {
                throw new InvalidDataException($"応答のエージェントまたは版が一致しません: {id}");
            }
            var messages = root.GetProperty("output");
            item["output_items"] = JsonNode.Parse(messages.GetRawText());
            item["response"] = ToAgentMessages(messages);
            item["response_id"] = root.GetProperty("id").GetString();
            item["response_status"] = root.GetProperty("status").GetString();
            item["agent_session_id"] = root.GetProperty("agent_session_id").GetString();
        }
        await WriteAsync(path, request, cancellationToken).ConfigureAwait(false);
    }

    internal static JsonArray ToAgentMessages(JsonElement output)
    {
        var messages = new JsonArray();
        foreach (var item in output.EnumerateArray())
        {
            switch (item.GetProperty("type").GetString())
            {
                case "function_call":
                    messages.Add(new JsonObject
                    {
                        ["role"] = "assistant",
                        ["content"] = new JsonArray(new JsonObject
                        {
                            ["type"] = "tool_call",
                            ["tool_call_id"] = item.GetProperty("call_id").GetString(),
                            ["name"] = item.GetProperty("name").GetString(),
                            ["arguments"] = JsonNode.Parse(item.GetProperty("arguments").GetString()!),
                        }),
                    });
                    break;
                case "function_call_output":
                    messages.Add(new JsonObject
                    {
                        ["role"] = "tool",
                        ["tool_call_id"] = item.GetProperty("call_id").GetString(),
                        ["content"] = new JsonArray(new JsonObject
                        {
                            ["type"] = "tool_result", ["tool_result"] = item.GetProperty("output").GetString(),
                        }),
                    });
                    break;
                case "message":
                    var content = new JsonArray();
                    foreach (var part in item.GetProperty("content").EnumerateArray())
                    {
                        content.Add(new JsonObject
                        {
                            ["type"] = "text",
                            ["text"] = part.GetProperty(part.GetProperty("type").GetString() == "refusal" ? "refusal" : "text").GetString(),
                        });
                    }
                    messages.Add(new JsonObject { ["role"] = item.GetProperty("role").GetString(), ["content"] = content });
                    break;
                case "reasoning":
                    break;
                default:
                    throw new InvalidDataException("評価に未対応の応答項目があります。");
            }
        }
        return messages;
    }

    private static JsonObject Message(string role, string text) => new()
    {
        ["type"] = "message", ["role"] = role,
        ["content"] = new JsonArray(new JsonObject { ["type"] = "input_text", ["text"] = text }),
    };

    private static JsonObject QueryMessage(string role, string text) => new() { ["role"] = role, ["content"] = text };

    internal static Task WriteAsync(string path, JsonNode json, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(path, json.ToJsonString(EvalJsonContext.Default.Options), cancellationToken);

    private static async Task<(JsonArray Tools, string SkillInstructions)> CreateToolDefinitionsAsync(string fixtures, CancellationToken cancellationToken)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AZURE_UPDATES_FIXTURES_DIR"] = Path.GetFullPath(fixtures),
        }).Build();
        var services = new ServiceCollection().AddLogging();
        var options = new AgentRuntimeOptions(new Uri("https://offline.test/api/projects/eval"), "unused", "Local",
            Path.GetTempPath(), string.Empty, PromptShieldsMode.Off, null, false);
        ToolServices.AddTo(services, options, new OfflineCredential(), configuration);
        using var provider = services.BuildServiceProvider();
        var schemaAgent = new AIProjectClient(options.ProjectEndpoint, new OfflineCredential())
            .AsAIAgent(new ChatClientAgentOptions { ChatOptions = new ChatOptions { ModelId = "unused" } });
        // 固定版の評価用 API で、モデルを呼ばずに実際のスキルツールのスキーマを取得する。
#pragma warning disable MAAI001
        var invoking = new AIContextProvider.InvokingContext(schemaAgent, null, new AIContext
            {
                Tools = provider.GetRequiredService<CoworkTools>().CreateTools(false),
            });
#pragma warning restore MAAI001
        var context = await provider.GetRequiredService<AgentSkillsProvider>().InvokingAsync(invoking, cancellationToken).ConfigureAwait(false);
        var definitions = new JsonArray();
        foreach (var tool in (context.Tools ?? []).OfType<AIFunction>())
        {
            definitions.Add(new JsonObject
            {
                ["type"] = "function",
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["parameters"] = JsonNode.Parse(tool.JsonSchema.GetRawText()),
            });
        }
        return (definitions, context.Instructions
            ?? throw new InvalidDataException("実際のスキル案内を評価入力へ取得できませんでした。"));
    }

    private sealed class OfflineCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("評価の準備ではネットワークを使いません。");
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("評価の準備ではネットワークを使いません。");
    }
}
