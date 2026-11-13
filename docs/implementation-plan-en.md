[日本語](implementation-plan.md) | **English**

# Azure Updates Cowork Implementation Plan

- Version: 1.1 (2026-11-12, updated for the dedicated viewer, MCP, and fixed-data evaluation)
- Prerequisites: the design is `docs/design.md` (English: [design-en.md](design-en.md)), and Copilot's rules are `.github/copilot-instructions.md`
- How to proceed: implement one phase per branch and PR. In the PR description, include the checklist of acceptance criteria and the results of the [To verify] items you checked (numbers from section 14.2 of the design document).
- BFF: Backend for Frontend, a web API that receives requests from the screens and calls server-side services such as the agent.

| Phase | Content | Main sections used | Azure required? |
|---|---|---|---|
| P0 | Repository foundation | Chapter 4 | No |
| P1 | Former siteproxy (preserved for comparison) | 5.1 | No |
| P2 | Dedicated viewer and BFF (fake agent) | 5.2, 6.1 to 6.3 | No |
| P3 | Running the agent locally (fetching, skills, progress) | 5.3, 5.5, Appendices A, B | Only a model (a project already created with `azd provision`) |
| P4 | Artifacts (Word, PowerPoint, saving, Prompt Shields) | 5.3, 5.4, 6.5 | Partly |
| P5 | Connecting web and the agent (local) | 5.2, 6.3, 6.4, 6.6 | Only a model |
| P6 | Deploying to Azure | Chapters 7, 10, 11 | Yes |
| P7 | Observability | Chapter 8, Appendix D | Yes |
| P8 | Evaluation | Chapter 9, Appendix C | Yes |
| P9 | Finishing touches (approval, sign-in, learning guides) | 5.2, 5.3, 7.3, chapter 12 | Yes |

If you want to use a model before P3, reference an existing Foundry and model. If new creation is needed, check and deploy only the P6 targets in advance. Storage is not needed in the current configuration.

The user-approved rebuild changes P2, P3/P4, P7, and P8. The left pane is rendered from the API instead of an iframe of the site, and the five business functions move to a private MCP server inside the same container. Evaluation is done with an Agent separate from production. Continuous evaluation, the seven labs, a public repository, and automated deployment are not included in the completion criteria this time. The unchecked items below are remaining work in the overall plan; for current verification results, see `docs/notes/`.

---

## P0 Repository foundation

What to build: `global.json`, `Directory.Build.props`, `Directory.Packages.props` (versions from section 4.3 of the design document), `UpdatesCowork.slnx`, projects in `src/`, `tests/`, and `eval/` (they can be empty), `.gitignore` (including `.azure/`, `.artifacts/`, `eval/.results/`, `eval/out/`), `.github/workflows/ci.yml`, `.vscode/mcp.json` (Appendix E), `README.md` (only an overview and a link to the design document).

### Example request to Copilot

> Following chapter 4 of `docs/design.md`, create the skeleton of the solution and projects. Pin package versions in `Directory.Packages.props` as in the table in section 4.3, and for those marked "latest stable at implementation time", use the version you confirmed with `dotnet package search`. `ci.yml` only builds and runs unit tests.

### Acceptance criteria

- [ ] `dotnet build` and `dotnet test --filter Category!=Eval` succeed.
- [ ] The folder structure matches section 4.1 of the design document.
- [ ] All projects are `net10.0`, and package versions are written only in `Directory.Packages.props`.
- [ ] `ci.yml` succeeds on PRs.

## P1 siteproxy

For comparison with and recovery of the former configuration. The following are the acceptance criteria of the former phase and are not prerequisites for current startup or deployment. Existing Azure resources are also preserved, not deleted.

What to build: the pre-survey record `docs/notes/site-survey.md`, `UpdatesCowork.SiteProxy` (routes, transforms, rate limiting, `/healthz`, `/robots.txt`, `/__auc/bridge.js`), `wwwroot/__auc/bridge.js`, and unit tests.

### Example request to Copilot

> Implement siteproxy following section 5.1 of `docs/design.md`. First, perform the four items of "P1 pre-survey", write the results in `docs/notes/site-survey.md`, and decide from those results whether to enable the optional rules (such as `ProxyDataApi`). Add unit tests for HTML rewriting, header rewriting, and `Set-Cookie` rewriting.

### Acceptance criteria

- [ ] The updates site is displayed at `https://localhost:7102/ja-jp/updates/`, and you can also navigate to detail pages.
- [ ] Responses have no `X-Frame-Options` and include `Content-Security-Policy: frame-ancestors 'self' https://localhost:7101`.
- [ ] The `bridge.js` tag is inserted immediately before `</head>` in the HTML.
- [ ] Methods other than GET and HEAD return 405. Nothing is relayed to hosts other than the upstream.
- [ ] Unit tests: replacing absolute URLs, injecting the tag, rewriting `Location` and `Set-Cookie`, and not rewriting HTML larger than 5 MB.

## P2 web screens and BFF skeleton

What to build: the static screens of `UpdatesCowork.Web` (`wwwroot`), `viewer.js`, `/api/updates` and `/api/updates/{id}`, `/api/config`, `/api/chat` (SSE), `/api/threads/new` and `end`, `/api/feedback`, `InMemoryChatRegistry`, anonymous user identification, `FakeAgentGateway`, `ChatEventMapper`, security headers, and rate limiting.

### Example request to Copilot

> Implement web's screens and BFF following section 5.2 and sections 6.1 to 6.3 of `docs/design.md`. Instead of the agent, use `FakeAgentGateway`, which replays recorded SSE. Write the recording by hand in `tests/recordings/fake-word.sse` so that all events in section 6.3 appear.

### Acceptance criteria

- [x] Update search, list, and body are shown on the left, and the chat on the right. No iframe and no loading of assets from external pages.
- [x] Only updates whose details were fetched successfully are used as the "displayed" page context. Article bodies are not passed from the browser to the model.
- [ ] After sending, progress, tool activity, artifact cards, and response text are displayed in order.
- [x] Article bodies are processed with DOMPurify, and scripts, iframes, forms, and external images are not displayed.
- [ ] POSTs without the `X-AUC-Request` header return 400.
- [ ] `<script>` contained in response Markdown is not executed.
- [ ] Unit tests: every row of the `ChatEventMapper` mapping table (section 6.3), and `PageContextValidator`.

## P3 Running the agent locally

What to build: `UpdatesCowork.AzureUpdates` (HTTP version and fixed-data version), `eval/fixtures/updates/` (save 5 or more items and a list from the real API), the `UpdatesCowork.Agent` Program.cs, AgentFactory, instructions (Appendix A), skills (`summarize-update`, `compare-updates`), the business MCP server/client, `CurrentTimeContextProvider`, `ToolCallMiddleware`, and `tools/http/agent-local.http`. Fetching updates, searching, progress, and P4's artifact generation are provided as five MCP tools.

### Example request to Copilot

> Following sections 5.3 and 5.5 of `docs/design.md`, implement the agent up to the point where it runs locally. Do not create the artifact tools yet. Verify Q1 and Q2 of section 14.2 against the XML documentation of the pinned packages before writing Program.cs and AgentFactory, and write in the PR which form you chose.

### Acceptance criteria

- [ ] `dotnet run --project src/UpdatesCowork.Agent` starts on port 8088, and `/readiness` returns 200.
- [ ] Sending "Summarize 559242" with `agent-local.http` calls `get_azure_update` and returns a summary that includes the ID and URL.
- [ ] "Find related updates" calls `search_azure_updates`.
- [ ] With `AZURE_UPDATES_FIXTURES_DIR` set, the same flow works without a network.
- [ ] Confirmed that function tools are actually called with the selected model (section 14.1).
- [ ] Unit tests: data API query strings, HTML-to-text conversion, argument validation, the tool call limit.
- [x] Business tools are called via real Streamable HTTP MCP. The names and input schemas of the five discovered tools are passed to the model.
- [x] MCP listens only on a loopback dynamic port, and validates the startup secret, Origin, request size, and session metadata. No public `/mcp` is created.
- [x] Tool failures, approval, and session isolation of file save locations are preserved after the MCP migration.

## P4 Artifacts

What to build: `UpdatesCowork.Office` (embedded templates, `WordReportBuilder`, `SlideDeckBuilder`, validation), the tools `create_word_report` and `create_powerpoint_deck`, `LocalArtifactStore`, `SessionArtifactStore`, `PromptShieldClient`, and the skills `word-report` (including `references/outline.md`) and `slide-deck`.

### Example request to Copilot

> Implement Word and PowerPoint generation following sections 5.4 and 6.5 of `docs/design.md`. Use the templates in `src/UpdatesCowork.Office/Templates/` and do not recreate them. Validate the generated Word and PowerPoint files with `OpenXmlValidator`, and do not save them if there are errors.

### Acceptance criteria

- [ ] Generated files open without errors in Word and PowerPoint (desktop or web). PowerPoint is 16:9 and notes are displayed.
- [ ] Japanese is displayed in Yu Gothic (or a substitute font).
- [ ] Unit tests: all items in "Tests" of section 5.4 of the design document.
- [ ] Confirmed the behavior of the three Prompt Shields modes (`Off`, `Annotate`, `Block`) with tests that replace HTTP.

## P5 Connecting web and the agent (local)

What to build: `ResponsesAgentGateway`, `BearerTokenHandler`, `LocalArtifactReader`, registering artifacts in the registry and downloading them, a mechanism to save actual SSE to `tests/recordings/` (the `Agent:RecordTo` setting), and `tools/http/web.http`.

### Example request to Copilot

> Following IAgentGateway in section 5.2 and section 6.4 of `docs/design.md`, make it possible for the BFF to call the local agent (`http://localhost:8088`). Verify Q3 of section 14.2 and decide the default for how conversations continue (`Agent:ConversationMode`). Record three kinds of actual SSE (summary, Word, PowerPoint) and use them in the `ChatEventMapper` tests.

### Acceptance criteria

- [ ] "Summarize", "To Word", and "To PowerPoint" can be run from the screen, and artifacts can be downloaded.
- [ ] The second request returns an answer that takes the previous conversation into account.
- [ ] Another person's artifact ID (one not in the registry) returns 404.
- [ ] Closing the browser also cancels the request to the agent (check in the logs).
- [ ] Exceeding 200 seconds returns `error` (`timeout`) (check by shortening the setting).

## P6 Deploying to Azure

What to build: `infra/`, `azure.yaml`, `infra/hooks/postdeploy.*`, Dockerfiles for Web and Agent, `InMemoryChatRegistry`, `SessionFilesArtifactReader`, `tools/env/load-env.*`, and `tools/http/agent-remote.http`. Deploy the same Agent source for production and for evaluation. Preserve the former proxy's Dockerfile. Automated deployment with `deploy.yml` is a follow-up task.

### Example request to Copilot

> Following chapters 7, 10, and 11 of `docs/design.md`, make it possible to create everything with `azd up`. Verify Q6 to Q9 of section 14.2 with `azd deploy agent --preview --no-prompt`, `az deployment group what-if`, and `az rest`, and write in `docs/notes/deploy.md` whether azure.yaml uses option A or option B.

### Acceptance criteria

- [x] In an environment that references an existing Foundry, the dedicated viewer can be used from `WEB_URL`.
- [ ] `azd up` succeeds in an empty subscription. Because the current extension's RAI variable expansion requires a workaround, first-time reproducibility is unconfirmed.
- [ ] With your own IP in `allowedIpRanges`, other IPs get 403.
- [ ] "To Word" succeeds on Azure and can be downloaded to the screen from the Hosted Agent's session files.
- [ ] `id-web` can retrieve and delete files of Agent sessions it created itself. Broad roles such as Foundry User are not added.
- [ ] Old sessions are deleted on a new conversation, and deletion is also attempted when the browser closes. Confirm that if the request does not arrive, Foundry's maximum 30-day retention applies.
- [ ] Confirm that web has one replica and that the in-memory registry and Data Protection keys are lost after a process restart.
- [ ] During migration, existing Storage Accounts and data are not deleted automatically, and the new deployment and app do not reference them. When cleaning up after migration, confirm that there are no references and obtain the operator's explicit approval, then delete separately.
- [ ] Removing Foundry Agent Consumer from `id-web` results in 403, and `agent_forbidden` appears on the screen (restore it after checking).
- [ ] Confirmed with `azd ai agent show` that the RAI policy is attached.
- [ ] There are no keys or connection-string keys anywhere (check with `git grep` and the portal).

## P7 Observability

What to build: OpenTelemetry for Web and Agent, the MCP SDK's `Experimental.ModelContextProtocol`, the spans and metrics in section 8.3 of the design document, `tools/otel/compose.yaml` (Aspire Dashboard), recording of user ratings, KQL, Bicep for search alerts (optional), and `docs/notes/observability.md`.

### Example request to Copilot

> Implement observability following chapter 8 of `docs/design.md`. Locally, send to the Aspire Dashboard in `tools/otel/compose.yaml`; on Azure, send to Application Insights. Verify Q4 and Q5 of section 14.2, and if they do not connect, implement the second and third methods in section 8.4.

### Acceptance criteria

- [x] The actual tree `invoke_agent` → `execute_tool` → HTTP/MCP → `mcp.tool.execute` → Office generation and saving is visible in the Aspire Dashboard.
- [x] In Azure Application Insights, Web through Agent, MCP, and Office are correlated under the same `operation_Id`.
- [ ] On Azure, conversations appear in the Traces tab of the Foundry portal, and the order of tool calls is visible.
- [ ] All KQL in Appendix D returns results (place versions with column names fixed to match real data in `docs/kql/`).
- [ ] A response for which "Not helpful" was pressed can be traced with KQL to the agent-side trace.
- [ ] The same span does not appear twice.
- [x] Production content recording by the custom Agent is disabled and enabled only for evaluation. The measured result that the Foundry platform side records production content, and that constraint, are stated explicitly.

## P8 Evaluation

What to build: `eval/datasets/core.jsonl` (30 cases), fixed data for `injection`, offline `Evaluation.Tests`, `EvalRunner` (`prepare` / `attach` / `collect` / `check`), `eval/run-foundry.ps1`, `eval/thresholds.json`, and the manually run `eval.yml`. Continuous evaluation rules are not created.

### Example request to Copilot

> Following chapter 9 of `docs/design.md`, record the actual responses of the fixed-data-only Hosted Agent and score them in Foundry. Verify Q10 and Q12 with a small number of cases before expanding to 30. Inspect the real files, and do not fill unscored or unretrieved items in as passes. The normal CI does not call models.

### Acceptance criteria

- [x] Normal xUnit verifies preparation, response conversion, judgment, and Office checks without a network.
- [x] Specify the version of the fixed-data-only Agent, record 30 actual responses, and obtain the Foundry run ID and report URL.
- [x] JSON, Markdown, and HTML output the number of passes, number applicable, number not applicable, and failure reasons for each metric.
- [x] Failing the criteria returns exit code 1, and unscored items, run failures, and unretrieved files are not treated as success. Results are not hidden by changing the criteria.
- [x] For the four `injection` cases, check whether the embedded passphrase is followed.
- [x] After all results and artifacts are saved, only this run's evaluation sessions are deleted, and reuse and cleanup with `-Resume` are confirmed in the cloud (30 cases each for v2, v4, and v6, plus 3 pilot cases).
- [ ] Confirm the quality gate of `eval.yml` on GitHub with OIDC configured (after the publishing destination and authentication are set up).

## P9 Finishing touches

What to build: the approval feature (`REQUIRE_APPROVAL_FOR_ARTIFACTS`, `/api/chat/approval`, approval cards), optional Entra ID sign-in (`Auth:Mode=EntraId`), the learning guides `docs/labs/lab1.md` to `lab7.md` (chapter 12 of the design document), and the README quick start.

### Acceptance criteria

- [ ] With the approval feature enabled, an approval card appears before Word is created, and pressing "Cancel" means no file is created.
- [ ] In Entra ID mode, the chat cannot be used without signing in.
- [ ] There are seven labs, each with the four sections "Goal", "Code to touch", "What to check", and "Going further".
- [ ] Using only the README's steps, a newcomer can proceed from `azd up` to lab 1.

## Going further (P10 and later, optional)

- Per-user session isolation (section 7.3 of the design document, Q15).
- Compare two versions on the same dataset with `x-agent-version-override`.
- Handle long processing with background responses (`background: true`) and polling.
- Red teaming with Python (section 9.7 of the design document).
- Continuous evaluation after agreeing on the policy, cost, and permissions for storing production content (Q11). Treat this separately from the completion of fixed-data evaluation.
