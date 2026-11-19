# Inspect MCP and traces

[日本語 (primary)](observability.md) | English

For developers running this sample. Follow update retrieval through document creation in local Aspire Dashboard and the Application Insights instance connected to Foundry. Verified: 2026-11-12.

## The actual MCP calls

The Agent's five business functions are `search_azure_updates`, `get_azure_update`, `update_plan`, `create_word_report`, and `create_powerpoint_deck`. Streamable HTTP MCP runs on loopback at a dynamic port inside the same container. The MCP client discovers tools and passes them to the model. These are not direct calls merely labeled with MCP-like span names.

MCP is not externally exposed. Startup secrets, caller addresses, Origin, request sizes, and session metadata are validated. Artifact approval happens on the MCP client side. Session IDs travel as request metadata, not model arguments. `load_skill` and `read_skill_resource` are MAF-internal tools, distinct from the five MCP functions.

## Inspect locally

1. Start Aspire Dashboard, Agent, and Web using the [README](../../README-en.md).
2. Select an update on the left and request a Word document in the chat.
3. Open the relevant Agent execution in Traces at `http://localhost:18888`. Expand `execute_tool create_word_report`.

The following parent-child hierarchy was verified during real Word generation:

```text
invoke_agent
  execute_tool create_word_report
    HTTP POST
      POST /mcp/
        tools/call create_word_report
          mcp.tool.execute
            office.word.build
            artifact.save
```

The local demonstration contained 45 spans, depth 6, lasted about 30 seconds, and generated a 36,383-byte Word file. Real environment trace IDs are not included in public documents. This record used a direct Agent call. Dashboard storage is not persisted, so the trace may not survive a restart.

The SDK 2.4.0 ActivitySource is `Experimental.ModelContextProtocol`. Subscribing only to `ModelContextProtocol` does not emit SDK spans. SDK `tools/call`, custom `mcp.tool.execute`, and HTTP spans occupy different levels.

## Inspect in Foundry and Application Insights

In the Foundry project's Agents, select `updates-cowork` or `updates-cowork-eval` and open Traces. Set the time range to the execution time, then expand from `invoke_agent` through business tools, HTTP/MCP, and Office. You need read access to the connected App Insights.

In the cloud, Web through Word generation was verified to share one `operation_Id`. Real environment IDs are not included in public documents. MCP SDK `tools/call` and custom spans also reached Azure.

Production v10 PowerPoint generation produced 78 spans. The first `tools/call create_powerpoint_deck` and `mcp.tool.execute` failed because slide counts were inconsistent; the call succeeded after one correction. The successful call linked to `office.pptx.build` and `artifact.save`, and the downloaded file contained exactly the requested three slides. Input errors and recovery remain visible in the same trace.

The verification browser required additional Foundry sign-in, so visual inspection of the portal itself is incomplete. Azure verification is based on retrieved App Insights data, not a claim that every portal view was checked.

Example: inspect only the structure of one trace in Logs.

```kusto
union requests, dependencies
| where timestamp > ago(1d)
| where operation_Id == "<trace ID>"
| project timestamp, cloud_RoleName, name, id, operation_ParentId, duration, success
| order by timestamp asc
```

Example: check MCP arrival.

```kusto
union requests, dependencies
| where timestamp > ago(2h)
| where cloud_RoleName == "UpdatesCowork.Agent"
| where name startswith "tools/call" or name == "mcp.tool.execute"
| summarize calls=count(), failures=countif(success == false), p95_ms=percentile(duration, 95) by name
```

The custom Agent's `cloud_RoleName` is `UpdatesCowork.Agent`; filtering by `updates-cowork` will not match it. Distinguish production and evaluation through `invoke_agent updates-cowork` / `invoke_agent updates-cowork-eval` in the same `operation_Id`.

Q4 W3C trace correlation is verified with real data. Propagation solely through the Q5 fallback `x-client-trace-id` header remains unverified; Q4 success is not evidence for it. Response/conversation ID correlation is also retained.

## Attach end-user ratings to the original trace

The chat's "Helpful" / "Not helpful" ratings are recorded inside `feedback.record`, a child of the rated response's `chat.turn` span. Two logs are emitted there: `auc.feedback`, and `gen_ai.evaluation.result`, which has the same shape as a Foundry trace annotation (preview) (design §8.6). The rating arrives as a separate HTTP request, but the logs appear in the original conversation trace.

This was checked on 2026-11-19 with the fake Agent (`--launch-profile fake-agent`) and the Aspire Dashboard. One chat message was sent, and its response was rated "Not helpful" with the comment 「出典のURLがない」 ("no source URL").

```powershell
docker compose -f tools/otel/compose.yaml up -d
dotnet run --project src/UpdatesCowork.Web --launch-profile fake-agent
```

The trace ID received in the chat SSE `meta` event:

```text
event: meta
data: {"threadId":"th_01M7SMWGBD8T29K5FCZH9YY9J5","responseId":"resp_fake_word_0001","traceId":"1dfad41fb91aab6af968a56ad29280fa",...}
```

After the rating was sent, opening this trace ID in the Dashboard showed `feedback.record` at the end of the `POST /api/chat` trace with two logs. The attributes of the second log:

| Name | Value |
|---|---|
| `microsoft.custom_event.name` | `gen_ai.evaluation.result` |
| `gen_ai.evaluation.name` | `task_completion` |
| `gen_ai.evaluation.score.value` | `0` |
| `gen_ai.evaluation.score.label` | `fail` |
| `gen_ai.response.id` | `resp_fake_word_0001` |
| `microsoft.gen_ai.human_evaluation.source` | `end_user` |
| `microsoft.gen_ai.evaluation.actor.type` | `human` |
| `gen_ai.evaluation.explanation` | `出典のURLがない` |
| TraceId | `1dfad41fb91aab6af968a56ad29280fa` |

In the console logs, both entries had `ParentId` equal to the `chat.turn` span ID `6a1f8decd0db8b38`. In previous-response-ID mode (the local default) there is no conversation ID, so `gen_ai.conversation.id` is omitted from the annotation.

In Azure, `gen_ai.evaluation.result` is expected to land in Application Insights `customEvents` and `auc.feedback` in `traces`. Arrival in `customEvents` and display as an annotation on the trace in the Foundry portal are post-deployment checks (Q16) and have not been verified yet. Use the KQL in design Appendix D when checking.

## Content capture and precautions

| Source | Configuration / observation |
|---|---|
| Production custom Agent | `OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT=false`; no input/output content in 144 inspected spans |
| Production Web | No input/output content in 19 inspected spans |
| Production Foundry `responsesapi` | Content in 20 of 26 inspected spans; maximum input length 33,278, output length 2,761 |
| Evaluation custom Agent | `true` for fixed-data evaluation only; content in 168 of 1,426 inspected spans |
| Evaluation Foundry `responsesapi` | Content in 230 of 262 inspected spans |

These are counts and lengths from a two-hour observation window, collected without retrieving content values. They are not lifetime totals. **Setting the application flag to `false` does not guarantee that the Foundry platform does not capture content. Do not enter confidential information.** Shared Foundry settings were not changed. Confirm platform storage policy, retention, and access permissions separately with administrators.

Custom attributes exclude cookies, tokens, and email addresses; user keys are hashed. Do not add trace-content JSON, recordings, or screenshots to a public repository. Being able to run fixed-data evaluation does not itself justify enabling continuous evaluation of production traffic.
