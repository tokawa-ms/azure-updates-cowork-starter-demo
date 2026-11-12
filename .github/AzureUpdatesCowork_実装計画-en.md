[日本語](AzureUpdatesCowork_実装計画.md) | **English**

# Azure Updates Cowork Implementation Plan

- Version: 1.0 (2026-11-11)
- Prerequisites: the design is `docs/design.md` (English: [design-en.md](../docs/design-en.md)), and Copilot's rules are `.github/copilot-instructions.md`
- How to proceed: implement one phase per branch and PR. In the PR description, include the checklist of acceptance criteria and the results of the [To verify] items you checked (numbers from section 14.2 of the design document).

| Phase | Content | Main sections used | Azure required? |
|---|---|---|---|
| P0 | Repository foundation | Chapter 4 | No |
| P1 | siteproxy | 5.1, 6.1 | No |
| P2 | web screens and BFF skeleton (fake agent) | 5.2, 6.2, 6.3 | No |
| P3 | Running the agent locally (fetching, skills, progress) | 5.3, 5.5, Appendices A, B | Only a model (a project already created with `azd provision`) |
| P4 | Artifacts (Word, PowerPoint, saving, Prompt Shields) | 5.3, 5.4, 6.5 | Partly |
| P5 | Connecting web and the agent (local) | 5.2, 6.3, 6.4, 6.6 | Only a model |
| P6 | Deploying to Azure | Chapters 7, 10, 11 | Yes |
| P7 | Observability | Chapter 8, Appendix D | Yes |
| P8 | Evaluation | Chapter 9, Appendix C | Yes |
| P9 | Finishing touches (approval, sign-in, learning guides) | 5.2, 5.3, 7.3, chapter 12 | Yes |

If you want to use a model before P3, you may run `azd provision` first for only the Foundry account, project, model deployment, and storage from P6.

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

What to build: the static screens of `UpdatesCowork.Web` (`wwwroot`), `/api/config`, `/api/chat` (SSE), `/api/threads/new`, `/api/feedback`, `InMemoryChatRegistry`, anonymous user identification, `FakeAgentGateway` (returns a fixed sequence of events), `ChatEventMapper`, security headers, and rate limiting.

### Example request to Copilot

> Implement web's screens and BFF following section 5.2 and sections 6.1 to 6.3 of `docs/design.md`. Instead of the agent, use `FakeAgentGateway`, which replays recorded SSE. Write the recording by hand in `tests/recordings/fake-word.sse` so that all events in section 6.3 appear.

### Acceptance criteria

- [ ] The siteproxy iframe is shown on the left, and the chat on the right. Navigating pages inside the iframe changes the "displayed" indicator.
- [ ] After sending, progress, tool activity, artifact cards, and response text are displayed in order.
- [ ] `postMessage` from other origins is ignored (check with a test HTML page).
- [ ] POSTs without the `X-AUC-Request` header return 400.
- [ ] `<script>` contained in response Markdown is not executed.
- [ ] Unit tests: every row of the `ChatEventMapper` mapping table (section 6.3), and `PageContextValidator`.

## P3 Running the agent locally

What to build: `UpdatesCowork.AzureUpdates` (HTTP version and fixed-data version), `eval/fixtures/updates/` (save 5 or more items and a list from the real API), `tools/fixtures/refresh-fixtures.ps1`, the `UpdatesCowork.Agent` Program.cs, AgentFactory, instructions (Appendix A), skills (`summarize-update`, `compare-updates`), tools (`search_azure_updates`, `get_azure_update`, `update_plan`), `CurrentTimeContextProvider`, `ToolCallMiddleware`, and `tools/http/agent-local.http`.

### Example request to Copilot

> Following sections 5.3 and 5.5 of `docs/design.md`, implement the agent up to the point where it runs locally. Do not create the artifact tools yet. Verify Q1 and Q2 of section 14.2 against the XML documentation of the pinned packages before writing Program.cs and AgentFactory, and write in the PR which form you chose.

### Acceptance criteria

- [ ] `dotnet run --project src/UpdatesCowork.Agent` starts on port 8088, and `/readiness` returns 200.
- [ ] Sending "Summarize 559242" with `agent-local.http` calls `get_azure_update` and returns a summary that includes the ID and URL.
- [ ] "Find related updates" calls `search_azure_updates`.
- [ ] With `AZURE_UPDATES_FIXTURES_DIR` set, the same flow works without a network.
- [ ] Confirmed that function tools are actually called with the selected model (section 14.1).
- [ ] Unit tests: data API query strings, HTML-to-text conversion, argument validation, the tool call limit.

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

What to build: `infra/` (the resources in section 10.1 of the design document, and the parameters and outputs in 10.2), `azure.yaml` (10.3), `infra/hooks/postdeploy.*`, three Dockerfiles, `.github/workflows/deploy.yml`, `InMemoryChatRegistry`, `SessionFilesArtifactReader`, `tools/env/load-env.*`, and `tools/http/agent-remote.http`.

### Example request to Copilot

> Following chapters 7, 10, and 11 of `docs/design.md`, make it possible to create everything with `azd up`. Verify Q6 to Q9 of section 14.2 with `azd deploy agent --preview --no-prompt`, `az deployment group what-if`, and `az rest`, and write in `docs/notes/deploy.md` whether azure.yaml uses option A or option B.

### Acceptance criteria

- [ ] `azd up` succeeds in an empty subscription, and the screen opens at `WEB_URL`.
- [ ] With your own IP in `allowedIpRanges`, other IPs get 403.
- [ ] "To Word" succeeds on Azure and can be downloaded to the screen from the Hosted Agent's session files.
- [ ] `id-web` can retrieve and delete files of Agent sessions it created itself. Broad roles such as Foundry User are not added.
- [ ] Old sessions are deleted on a new conversation, and deletion is also attempted when the browser closes. Confirm that if the request does not arrive, Foundry's maximum 30-day retention applies.
- [ ] Confirm that web has one replica and that the in-memory registry and Data Protection keys are lost after a process restart.
- [ ] Existing Storage Accounts and data are not deleted, and the new deployment and app do not reference them.
- [ ] Removing Foundry Agent Consumer from `id-web` results in 403, and `agent_forbidden` appears on the screen (restore it after checking).
- [ ] Confirmed with `azd ai agent show` that the RAI policy is attached.
- [ ] There are no keys or connection-string keys anywhere (check with `git grep` and the portal).

## P7 Observability

What to build: OpenTelemetry configuration for the three apps, the spans and metrics in section 8.3 of the design document, `tools/otel/compose.yaml` (Aspire Dashboard), recording of user ratings, `docs/kql/*.kql` (Appendix D), Bicep for search alerts (optional), and `docs/notes/observability.md` (results of Q4 and Q5).

### Example request to Copilot

> Implement observability following chapter 8 of `docs/design.md`. Locally, send to the Aspire Dashboard in `tools/otel/compose.yaml`; on Azure, send to Application Insights. Verify Q4 and Q5 of section 14.2, and if they do not connect, implement the second and third methods in section 8.4.

### Acceptance criteria

- [ ] Locally, for a single request, the sequence `chat.turn` → HTTP → `invoke_agent` → `execute_tool` → custom spans is visible in the Aspire Dashboard.
- [ ] On Azure, conversations appear in the Traces tab of the Foundry portal, and the order of tool calls is visible.
- [ ] All KQL in Appendix D returns results (place versions with column names fixed to match real data in `docs/kql/`).
- [ ] A response for which "Not helpful" was pressed can be traced with KQL to the agent-side trace.
- [ ] The same span does not appear twice.

## P8 Evaluation

What to build: `eval/datasets/core.jsonl` (the 30 cases in section 9.2 of the design document), fixed data for `injection`, `eval/UpdatesCowork.Evaluation.Tests` (the evaluators in 9.3), `eval/UpdatesCowork.EvalRunner` (`cloud` and `continuous enable/disable`), `eval/thresholds.json`, and `.github/workflows/eval.yml`.

### Example request to Copilot

> Implement evaluation following chapter 9 of `docs/design.md`. First run the 30 cases with local evaluation and create a report, then evaluate the deployed agent with `EvalRunner cloud`. For Q10 to Q12 of section 14.2, verify them by running an evaluation of just one case before expanding to all cases.

### Acceptance criteria

- [ ] After `dotnet test eval/UpdatesCowork.Evaluation.Tests --filter Category=Eval`, the `aieval report` HTML is created.
- [ ] `EvalRunner cloud` results appear in Evaluations in the Foundry portal, and `eval/out/summary.md` shows the pass rate for each evaluator.
- [ ] Temporarily raising the threshold to 1.0 makes `eval.yml` fail (restore it after checking).
- [ ] Continuous evaluation was enabled, and the time until results appear in the Monitor tab was recorded in `docs/notes/evaluation.md`.
- [ ] In the four `injection` cases, the embedded instructions are not followed.

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
