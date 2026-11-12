<h1 align="center">Azure Updates Cowork</h1>

<p align="center">
  A learning sample for summarizing Azure updates and creating Word and PowerPoint documents
</p>

<p align="center">
  <a href="global.json"><img src="https://img.shields.io/badge/.NET-10-512BD4?style=flat-square" alt=".NET 10"></a>
  <a href="docs/design-en.md"><img src="https://img.shields.io/badge/Microsoft_Foundry-Hosted_Agent-0078D4?style=flat-square" alt="Microsoft Foundry Hosted Agent"></a>
  <a href="docs/notes/observability-en.md"><img src="https://img.shields.io/badge/MCP-Streamable_HTTP-0F766E?style=flat-square" alt="MCP Streamable HTTP"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-64748B?style=flat-square" alt="License: MIT"></a>
</p>

<p align="center">
  <a href="README.md">日本語 (primary)</a> · English
</p>

<p align="center">
  <a href="#try-it-locally">Quick start</a> ·
  <a href="docs/design-en.md">Design</a> ·
  <a href="docs/notes/evaluation-en.md">Evaluation results</a> ·
  <a href="#documentation">Documentation</a>
</p>

---

Explore Azure updates in a dedicated viewer alongside a chat that asks a Microsoft Agent Framework agent to summarize updates and create documents. The sample uses .NET 10, static HTML/CSS/JavaScript, and Microsoft Foundry Hosted Agent. It does not reverse-proxy the entire website or use an iframe.

## Features

| Feature | What you can do |
|---|---|
| Search and summarize | Select updates, request summaries with official source URLs, and compare related updates |
| Create Word and PowerPoint | Generate documents in the same conversation and download them from artifact cards |
| Trace and evaluate | Follow Web, MCP, and Office-generation calls; check quality with a separate Agent and 30 fixed cases |

## Architecture and usage notes

Five business tools are provided by a private MCP server inside the Agent container. The viewer and document-generation workflow have been verified in an existing development environment. This is a low-volume learning sample, not a complete production system. See the [design](docs/design-en.md) and [deployment record](docs/notes/deploy-en.md) for architecture and limitations.

> [!IMPORTANT]
> Do not enter confidential information. Production content capture is disabled in the custom Agent, but production input/output content was observed in the shared Foundry `responsesapi` layer. This setting is separate from application switches; the shared environment's configuration was not changed.

> [!NOTE]
> Model calls connect to Azure and incur charges even when running locally. Fixtures replace only update data; they do not make model calls offline.

<details>
<summary>Artifact storage and session retention</summary>

Artifacts are temporarily stored in the Hosted Agent's `$HOME/artifacts`. Web verifies ownership and retrieves them through the Session Files API; public Blob downloads are unnecessary. Starting a new conversation deletes the previous session, and closing the tab also attempts deletion. If the termination request does not arrive, files may remain for up to 30 days. Web has one replica; a restart loses the registry and session-identification keys.

Traces cover HTTP/MCP calls, Office generation, and file storage. Fixed-data evaluation uses a separate `updates-cowork-eval` agent.

</details>

## Try it locally

### Prerequisites

| Requirement | Purpose |
|---|---|
| .NET 10 SDK | Build and run locally |
| Azure CLI | Run `az login` with an account authorized to call the project |
| Existing Foundry project and model | A model deployment supporting function calls |
| Docker (optional) | Run Aspire Dashboard |

### Prepare to run

Run from the repository root:

```powershell
dotnet dev-certs https --trust
dotnet build
docker compose -p auc-otel -f tools\otel\compose.yaml up -d
```

### Terminal 1: Agent

Replace `<account>`, `<project>`, and `<deployment>` with existing environment values. These commands do not create resources.

```powershell
$env:FOUNDRY_PROJECT_ENDPOINT = 'https://<account>.services.ai.azure.com/api/projects/<project>'
$env:AZURE_AI_MODEL_DEPLOYMENT_NAME = '<deployment>'
$env:AZURE_TOKEN_CREDENTIALS = 'AzureCliCredential'
$env:ARTIFACTS_MODE = 'Local'
$env:ARTIFACTS_LOCAL_DIR = Join-Path $PWD '.artifacts'
$env:AZURE_UPDATES_FIXTURES_DIR = Join-Path $PWD 'eval\fixtures\updates'
$env:PROMPT_SHIELDS_MODE = 'Off'
$env:OTEL_EXPORTER_OTLP_ENDPOINT = 'http://localhost:4317'
dotnet run --project src\UpdatesCowork.Agent
```

Verify that `http://localhost:8088/readiness` returns 200. `AZURE_UPDATES_FIXTURES_DIR` switches only update data to fixtures. **Model calls connect to Azure and incur charges.** Do not set `FOUNDRY_AGENT_NAME` locally.

If you already have an `azd` environment for this repository, load local settings with `. .\tools\env\load-env.ps1` instead of the example configuration. Prompt Shields `Off` is for local learning.

### Terminal 2: UI and BFF

```powershell
$env:Artifacts__LocalDir = Join-Path $PWD '.artifacts'
$env:AZURE_UPDATES_FIXTURES_DIR = Join-Path $PWD 'eval\fixtures\updates'
$env:OTEL_EXPORTER_OTLP_ENDPOINT = 'http://localhost:4317'
dotnet run --project src\UpdatesCowork.Web
```

Open `https://localhost:7101` and select an update from the left-hand list. After choosing “要約” (Summarize), ask in the same conversation: “今要約した更新のWordを作って” (Create a Word document for the update just summarized) and “同じ更新のPowerPointを作って” (Create a PowerPoint for the same update). Download artifacts from their chat cards. Other browser sessions cannot retrieve them.

Aspire Dashboard is at `http://localhost:18888`. Inspect the parent-child chain `execute_tool` → `POST /mcp/` → `tools/call` → `mcp.tool.execute` → `office.word.build` / `artifact.save`. The browser does not send article content to the model. It sends the selected ID, title, and official URL; the agent retrieves content through MCP tools.

To use live update data, unset `AZURE_UPDATES_FIXTURES_DIR` in both Web and Agent. `UpdatesCowork.SiteProxy` remains for comparison with the old architecture but is not needed for current startup or deployment.

## Stop and restart

Press `Ctrl+C` in each local terminal. Restart with the same startup commands. Stop Dashboard as follows; in shared environments, first ensure nobody else is using it.

```powershell
docker compose -p auc-otel -f tools\otel\compose.yaml stop
# Restart
docker compose -p auc-otel -f tools\otel\compose.yaml start
```

Azure compute stop/restart procedures have not been verified. `azd down` deletes resources; it does not pause them. Do not run it against shared Foundry accounts, projects, or models. The legacy proxy and Storage were deleted with operator approval on 2026-11-13. Current Web, Agent, ACR, and logging resources may continue to incur charges.

## Build and unit tests

```powershell
dotnet build --configuration Release
dotnet test --no-build --configuration Release --filter 'Category!=Eval'
.\eval\tests\run-foundry.tests.ps1
```

Normal tests do not access the network. The evaluation project checks data, response conversion, and judging logic; the PowerShell tests check bounded CLI retries offline. These are separate from cloud evaluations that call models.

## Run Foundry evaluation with fixed data

After deploying the evaluation Agent and configuring permissions, sign in with Azure CLI and run in PowerShell 7. Only an explicitly specified version of an Agent whose name ends in `-eval` is targeted.

```powershell
.\eval\run-foundry.ps1 `
  -ProjectEndpoint 'https://<account>.services.ai.azure.com/api/projects/<project>' `
  -AgentVersion '<evaluation-agent-version>' -Limit 30 -Out eval\out\core
```

The 30 cases cover summaries, Word, PowerPoint, search/comparison, missing selection, out-of-scope requests, and indirect prompt injection. Real Hosted Agent responses are saved and passed to three Foundry evaluators. Local checks also inspect tool use, citations, Japanese, Office files, and attack resistance. Generation and judge models incur charges.

`eval\out\core` contains `summary.md`, `summary.json`, `report.html`, and the Foundry report URL. After interruption, use the same arguments with `-Resume` to reuse saved responses. The `check` exit codes are 0 for pass, 1 for failed gates, and 2 for invalid processing. PowerShell communication/build failures also exit nonzero. See [evaluation design and results](docs/notes/evaluation-en.md). Continuous evaluation and automatic scoring of all production traffic are not enabled.

<details>
<summary>Results on 30 fixed cases (2026-11-13)</summary>

Evaluation Agent v12 met all eight quality gates. Task adherence was 29/30 and citations 30/30. Fixes addressed missing page context in scoring inputs, conflicts between target validation and skill startup, incomplete search/citations, and missing attack notices. The dataset, models, and pass-rate thresholds were unchanged; citation and other checks were strengthened. Improvements were also deployed to production Agent v11, with a smoke test confirming that no update retrieval starts without a page selection. Shared infrastructure and Web were updated to current definitions/source on the same day. These results cover only 30 fixed cases and do not guarantee quality on unseen inputs.

</details>

## Azure and publication

`azure.yaml` and `infra` support referencing existing Foundry resources or creating new ones. For development, always set `EXISTING_AI_PROJECT_RESOURCE_ID`, `EXISTING_APPINSIGHTS_RESOURCE_ID`, and existing model deployment names. Omitting these settings targets new Foundry/model resources for creation. See the [deployment record](docs/notes/deploy-en.md).

There is no Deploy to Azure button yet. The public repository URL is not finalized, and creating infrastructure through ARM alone does not build the app or deploy the Hosted Agent. An unverified link is not presented as a completed deployment path.

Before publication, review the update site's and data API's terms and establish IP restrictions, authentication, and operating procedures. Never store keys or tokens in configuration files or commit `.azure`, `.artifacts`, or conversation recordings.

## Documentation

| Document | Contents |
|---|---|
| [Design](docs/design-en.md) | Architecture and contracts |
| [Implementation plan](docs/implementation-plan-en.md) | P0–P9 acceptance criteria |
| [Site survey](docs/notes/site-survey-en.md) | Why data API forwarding was needed |
| [Local integration and observability](docs/notes/local-integration-en.md) | API, generated document, conversation, and trace verification |
| [MCP and observability](docs/notes/observability-en.md) | Trace hierarchy, inspection, and content-capture scope |
| [Fixed-data evaluation](docs/notes/evaluation-en.md) | Real response collection, Foundry scoring, and quality gates |
| [Deployment investigation](docs/notes/deploy-en.md) | Azure deployment results, Q items, and remaining limitations |
| [Preflight validation](preflight-report-en.md) | Bicep and initial what-if results |
| [Pre-publication information review](docs/notes/public-release-review-en.md) | Sanitized information, historical disclosures, and publication decisions |

## License

The sample's code and documentation are licensed under the [MIT License](LICENSE). Copyright (c) 2026 tokawa-ms.
Bundled DOMPurify and marked retain their own licenses. Preserve the license notices under `src/UpdatesCowork.Web/wwwroot/lib/`. Third-party content, including Azure update articles and product names, belongs to its respective owners and is not relicensed under this repository's MIT License.
