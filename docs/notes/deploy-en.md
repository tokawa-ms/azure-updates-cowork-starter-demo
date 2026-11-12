# Deployment investigation record

[日本語 (primary)](deploy.md) | English

Verified: 2026-11-13. azd 1.37.3, azure.ai.agents extension 1.0.0, Bicep 0.49.2. The dedicated viewer and MCP were deployed to an existing Azure environment. Production Agent is version 11; fixed-data evaluation Agent is version 12. The legacy siteproxy and Storage, together with their dedicated related resources, were deleted after explicit operator approval.

The November 13 quality improvements were deployed to production Agent v11 after all gates passed on 30 fixed cases. A smoke test verified safe handling of a request with no selected page. Shared-infrastructure Bicep was then reapplied and Web redeployed from current source ([full Azure rollout update](#full-azure-rollout-update-2026-11-13)). Production and evaluation version numbers are independent. See the [evaluation record](evaluation-en.md) for improvements and evaluation history.

The Web URL is not included in public documents. Retrieve your environment's URL with `azd env get-value WEB_URL`. Resource names and IDs below use publication-safe placeholders; substitute your own values.

The initial failures below are resolved historical issues, not an indication that the app is currently stopped.

## Reuse existing Foundry

Development referenced an existing project, existing generation/evaluation model deployments, and existing Application Insights. No account, project, or model deployment was newly created. Environment-specific values belong in the untracked `.azure` directory.

Example for another development environment:

```powershell
azd env new <environment-name>
azd env set AZURE_LOCATION <region>
azd env set AZURE_SUBSCRIPTION_ID <subscription-id>
azd env set EXISTING_AI_PROJECT_RESOURCE_ID <existing-project-arm-resource-id>
azd env set EXISTING_APPINSIGHTS_RESOURCE_ID <connected-application-insights-arm-resource-id>
azd env set AGENT_DEPLOYMENT_NAME <existing-generation-model-deployment>
azd env set JUDGE_DEPLOYMENT_NAME <existing-judge-model-deployment>
# Only to avoid duplicate assignments for developers who already have the required roles
azd env set ASSIGN_DEVELOPER_FOUNDRY_ROLES false
azd provision --preview
```

An empty `EXISTING_AI_PROJECT_RESOURCE_ID` selects new Foundry creation. Review the preview before executing and ensure shared Foundry/models are not creation targets. Application deployment succeeded, but `azd up` reproducibility remains unverified because a temporary direct RAI policy ID is required.

## Results by Q item

| Item | Result |
|---|---|
| Q6 | `azure.yaml` uses option A: ai-project service and root Bicep. Production v11 and evaluation v12 deployed. Variables use `env`. The temporary direct RAI policy ID workaround is still required |
| Q7 | Express environment created; UAMI ACR pull and Web/siteproxy deployment succeeded. Both revisions' containers were ready with zero restarts |
| Q8 | Retrieved `instance_identity.principal_id` from `{project}/agents/{name}?api-version=v1`. Postdeploy assigned roles to production and evaluation identities, including successful reruns after redeployment |
| Q9 | Used an existing account's RAI policy and existing App Insights. Verified the full policy ARM resource ID in Agent version `rai_config` |
| Q4 | Verified in Azure that Web, Agent, MCP, and Office generation share an `operation_Id`. See [observability](observability-en.md) |
| Q5 | Propagation through fallback `x-client-*` headers alone is unverified; separate from Q4 evidence |
| Q10, Q12 | Registered three Foundry evaluators and scored 30 real responses using `jsonl/file_content`. Initialization uses `model`; tool calls use flat Agent Messages. See [evaluation](evaluation-en.md) |
| Q11 | Continuous evaluation is not enabled; separate advanced work |
| Q14 | Verified that Web can retrieve/delete its own Session Files with Foundry Agent Consumer alone |

## Findings and remaining limitations

### Container Apps Express ACR pull

Web and siteproxy images built in ACR. ACR could not interpret Dockerfile `FROM --platform=$BUILDPLATFORM`, so the platform qualifier was removed.

The first existing-app update failed:

```text
BuildFailed: Authentication failed when pulling container image ...
Provide 'registryCredentials' (username and token) or 'managedIdentityClientId' ...
```

The user-assigned identity and its ACR-scoped AcrPull assignment were verified. After the initial failure, ARM returned null for `configuration.registries`. After aligning apiVersion to 2026-07-01 and explicitly resubmitting image/registry settings through Bicep, GET returned server and identity, although provisioningState was still Failed at that point.

A temporary app using the same Express environment, ACR, UAMI, and image pulled successfully with UAMI. It was deleted after investigation. Redeployed Web/siteproxy containers were confirmed ready. No workaround enabled the ACR administrator password or changed Express to Standard.

### Hosted Agent RAI policy

An environment-variable reference in `raiPolicyName` caused HTTP 400 `invalid_payload` during version creation:

```text
RAI policy name must be a fully qualified Microsoft.CognitiveServices accounts/raiPolicies ARM resource ID.
```

The azd environment value was verified to be a full ARM resource ID. In an isolated Agent, a literal ID succeeded while `${AZURE_AI_RAI_POLICY_ID}` referencing the same value failed. This confirmed that the current Hosted Agent deployment path does not expand environment variables in `raiPolicyName`.

Without removing RAI, the environment value was temporarily written directly to local `azure.yaml` during deployment. The Agent became active and `rai_config.rai_policy_name` contained that ID. Afterwards, `azure.yaml` was restored to `${AZURE_AI_RAI_POLICY_ID}`; environment-specific IDs were not left in source. Redeployment requires an extension fix or the same temporary-expansion procedure.

Deploying Web before the Agent initially caused the shared postdeploy hook to fail because no Agent existed. The current hook targets both production and evaluation Agents; on first deployment, deploy both before completing role assignments. A failed hook after deploying only one service is not evidence that the whole application is ready.

With RAI temporarily expanded, redeploy these services. Do not repeat `provision` unless infrastructure changed.

```powershell
azd deploy agent --environment auc-dev --no-prompt
azd deploy agent-eval --environment auc-dev --no-prompt
# If Web changed
azd deploy web --environment auc-dev --no-prompt
```

Restore the RAI environment-variable reference in `azure.yaml` afterwards, even if a command fails. Do not commit environment-specific ARM IDs.

### Production quality rollout (2026-11-13)

The same source as evaluation Agent v12, which passed all eight gates, was deployed to production service `agent`. `azd deploy agent --environment auc-dev --no-prompt` succeeded; `azd ai agent show agent --environment auc-dev` confirmed `updates-cowork:11`, `active`. The Hosted Agent RAI policy was retained. The temporary ARM ID was restored to an environment-variable reference in `azure.yaml`.

A new-session smoke test targeted production v11 through the official CLI. For “この更新の内容を要約してください” (Summarize this update) without page context, it asked the user to select an update or provide an ID, without retrieving an incorrect update. At that stage, verification covered only this case; Web and shared infrastructure had not yet been redeployed.

### Full Azure rollout update (2026-11-13)

Infra and `azure.yaml` had no differences from HEAD and had not changed since the previous provision on 2026-11-12. For the Web build, only Shared (`TelemetryNames.cs`) was newer than the previous deployment.

1. `azd provision --preview --environment auc-dev --no-prompt` showed no deletion or creation of shared Foundry/models. Differences were limited to Web's existing image reference and the unresolved CAE Log Analytics reference display.
2. `azd provision --environment auc-dev --no-prompt` succeeded. Existing project, App Insights, and model deployments (`gpt-5.4-mini`, `gpt-5-mini`) remained referenced; no Foundry was created.
3. `azd deploy web --environment auc-dev --no-prompt` succeeded. The deployed revision was `Healthy`, `Running`, with one replica.
4. `azd hooks run postdeploy --environment auc-dev` succeeded and reverified role assignments for both Agents.
5. Production `updates-cowork:11` and evaluation `updates-cowork-eval:12` were active. Source checksums inside deployed images matched current source, as described below, so they were not redeployed. No temporary RAI substitution was required, and `azure.yaml` had no differences.
6. Opening WEB_URL in the browser showed 20 list entries and article content. A request to summarize “Azure Functions support for .NET 11” (ID 578773) invoked skill loading and update retrieval in the production Agent, then returned a Japanese summary citing the official URL.

At this rollout stage, legacy siteproxy and Storage were retained outside the service targets. They were subsequently deleted after explicit approval, as described below.

### Compare Azure execution results and deployed contents

On 2026-11-13, Azure CLI retrieved ARM deployment history, ACR build history, running Web revisions, and both Agent definitions. These are execution results; real identifiers have been replaced with publication-safe labels.

| Target | Azure identifier | Verified result |
|---|---|---|
| Shared infrastructure | `<app-resource-group>` / `<infrastructure-deployment>` | `Succeeded`, 2026-11-13 06:55:52 JST. Monitoring, registry, Container Apps environment, Web, identity, and Foundry access operations also succeeded |
| Shared Foundry access | `<foundry-resource-group>` / `<access-deployment>` | `Succeeded`; correlated with infrastructure through the same correlation ID |
| Web | `<web-revision>` / `<web-build-run>` | `Healthy` / `Running`, active, one replica, 100% traffic |
| Production Agent | `updates-cowork:11` / `<agent-build-run>` | `active` |
| Evaluation Agent | `updates-cowork-eval:12` / `<eval-build-run>` | `active` |

ACR build output digests were compared with current digests of the tags referenced by running definitions. All three builds—Web, production Agent, evaluation Agent—were `Succeeded`, and their respective digests matched. Real digest values are not included in public documents.

“Current” was not determined from file modification times alone. Images were retrieved by the verified digests without starting the app. Portable PDB C# source checksums, embedded instructions, skills, fixtures, and Web static files were compared with the current worktree. All 70 production, 70 evaluation, and 62 Web entries matched. The deployed ARM template exported from Azure also matched the current compiled Bicep, excluding metadata and type-name casing.

Live endpoint checks verified `/healthz` returning `ok`, SHA-256 equality between top-level HTML and local `index.html`, 20 `/api/updates` entries, and `/api/updates/578773` content retrieval. A new-session production v11 request without a selected page returned a Japanese request to select an update. Both Agents' RAI policies and Cognitive Services User assignments were verified. Production had no fixture setting; only evaluation did.

Retrieved data was saved under untracked `.artifacts/azure-rollout-20261113/`. `verification-summary.json` aggregates results; `infrastructure-deployment.json` and `infrastructure-operations.json` contain ARM execution results; `web-revisions.json` and `updates-cowork*.json` contain running state; `deployed-images.json` and `source-verification.json` record deployed-content comparisons. Actual ACR build logs, this preview, Web retrieval results, and production response logs are in the same directory. Credentials, cookies, and tokens were not saved.

### Delete legacy proxy and Storage (2026-11-13)

After explicit operator approval, legacy resources were deleted between 07:21 and 07:22 JST. Before deletion, Resource Graph reference searches and current Web/Agent settings confirmed there were no references from the current path, and artifacts used Session Files. No deletion locks, Blob immutability policies, or legal holds were present.

| Deleted target | Result |
|---|---|
| Container App `<legacy-proxy-app>` | `az containerapp delete` succeeded |
| Storage Account `<legacy-storage-account>` | `az storage account delete` succeeded. Legacy Blob containers `artifacts`, `dataprotection`, and the `auc` table were included. No new backup was created |
| Managed Identity `<legacy-proxy-identity>` | Confirmed dedicated to the old proxy, then deleted |
| Legacy proxy AcrPull assignment `<legacy-role-assignment-id>` | Only that assignment was deleted; shared ACR and current users' permissions retained |
| Storage-specific Event Grid system topic `<legacy-storage-system-topic>` | Disappeared with Storage deletion, including `StorageAntimalwareSubscription`; shared Defender settings unchanged |

After deletion, ARM listings contained none of the four resources, and no RBAC assignments remained for the legacy proxy principal or Storage scope. Five resources remained in the application resource group: current Web, its identity, Container Apps environment, ACR, and Log Analytics. Shared Foundry, models, and Application Insights in another group were unchanged. Historical ACR images were not deleted.

Current Web remained Healthy / Running on the same image, one replica, 100% traffic. `/healthz` returned `ok`; the update API returned 20 entries. Production v11 and evaluation v12 remained active. Post-deletion `azd provision --preview --environment auc-dev --no-prompt` succeeded without recreating the old proxy, identity, or Storage. `enableLegacyProxy=false` and the configuration that does not invoke Storage modules were retained. Legacy code was not removed, nor was the current app redeployed.

Before/after inventories, reference checks, deletion exit results, post-deletion health checks, and preview were saved in untracked `.artifacts/legacy-cleanup-20261113/`.

## Outstanding acceptance criteria

- [x] Open the app through Azure WEB_URL and download artifacts through Hosted Agent Session Files (verified 2026-11-11).
- [x] Verify RAI application and postdeploy reruns on redeployment.
- [ ] Verify IP restrictions and the 403 after removing Foundry Agent Consumer in the live environment. Shared permissions were not removed during this work.
- [ ] Verify Hosted Agent stop/restart and shutdown procedures preserving shared resources.
- [x] Implement P8: 30 real responses, Foundry scoring, Office inspection, reports, and quality gates. Retain failures.
- [ ] Add continuous evaluation separately; not enabled here.
- [ ] Complete P9 approval / Entra ID device verification and seven labs.
- [ ] Choose the public destination and verify a complete Deploy to Azure-to-application path.
- [x] Pass normal builds/unit tests including evaluation logic (375 tests, 2026-11-13).
- [ ] Configure GitHub OIDC and run `ci.yml` / `eval.yml`. `deploy.yml` is not implemented.

Git remote was not configured, and no repository creation, publication, or push was performed. Demo infrastructure and deployed applications remain; Web, Agent, ACR, logs, and related services may incur charges.

## Artifact storage change

To avoid requiring Blob downloads, artifacts use Agent Hosted Session Files. Web's `id-web`, with Foundry Agent Consumer, retrieves/deletes sessions belonging to the same identity. Web's registry and Data Protection keys are process-memory only; the ACA Web replica count is fixed at one.

Starting a new conversation deletes the Agent session; page termination also attempts best-effort deletion. Hosted Agent sessions/files may remain for up to 30 days if deletion requests do not arrive.

The change was applied to `auc-dev` on 2026-11-11. The `azd provision` preview showed no deletions. Current ACA environment/ACR values were explicitly recorded in Bicep to remove differences. Agent, Web, and siteproxy were then deployed. Table, Blob, and Data Protection settings disappeared from Web environment variables, and its replica count became one.

The first cloud attempt returned `internal_error` from Word generation for two reasons:

1. `FoundryAgentRequestContext.Current.SessionId` was sometimes empty during tool execution. Process-level `FoundryEnvironment.SessionId` now supplies the fallback.
2. The Agent Dockerfile used `USER $APP_UID`, which could not write to platform-provided `$HOME` (`/home/session`), causing `UnauthorizedAccessException`. Like the official C# file-tools sample, only the Agent image now uses the default user. Web and siteproxy remain non-root.

At the time, the tool logger used a separate service provider and logs did not reach Application Insights. The current MCP configuration uses shared tool DI and logging. Exception `error.type` and error status remain recorded; exception bodies and sensitive arguments are not logged.

After the fixes, cloud smoke tests confirmed:

- `/healthz` and the UI return 200.
- Summary, Word (36,038 bytes), and PowerPoint (37,809 bytes) generation succeeds, and downloaded sizes match.
- Another user receives 404 for the same artifact.
- Starting a new conversation returns 204, after which the original artifact returns 404.
- `id-web` can retrieve/delete its own session files with Foundry Agent Consumer alone, without Foundry User.

Existing Storage/data were preserved during migration, and the Storage module was no longer called by `main.bicep`. The old account was later deleted on 2026-11-13 with explicit operator approval.

Session Files API: [Manage hosted agent sessions](https://learn.microsoft.com/azure/foundry/agents/how-to/manage-hosted-sessions).

## Verification after the dedicated viewer / MCP migration

In the browser against production v10, summary, Word, and PowerPoint were run sequentially in the same Hosted Session conversation. Word was 36,570 bytes and PowerPoint 33,784 bytes; displayed and downloaded sizes matched. The requested three-slide presentation actually contained three slides total: cover, body, sources. Another browser session received 404 for both files. Starting a new conversation returned 204; old downloads then returned 404.

Versions v8/v9 misinterpreted three total slides as three body slides. Version v10 added required MCP argument `totalSlides` and consistency validation with body count. The latest real call rejected inconsistency before saving, and the Agent corrected it once to generate the correct three slides. Descriptions alone did not prevent the problem.

No iframe was present, and browser requests stayed on Web's origin. Displaying the list and selected article took 5.3 seconds in v8 and 1.3 seconds in the latest v10 check, each a single measurement with different cache conditions. These do not establish stable performance or a version-to-version improvement rate. Whole-site HTML/JavaScript/CSS are not forwarded; initial data API requests and Agent startup take additional time.

During verification, the Foundry Responses API intermittently returned HTTP 500 and the app displayed `agent_error`. App Insights confirmed the downstream Web call returned 500; authentication/input validation was not weakened. Later attempts succeeded, but do not guarantee that transient service issues are resolved. Only the evaluation script performs bounded retries for explicit `session_creation_failed`; Web does not automatically resubmit generation requests with unknown results.

MCP SDK/custom spans reached the cloud, and Aspire Dashboard showed the actual tree through Word generation. The automation browser required additional sign-in for Foundry. Visual verification of the portal itself is incomplete; cloud evidence comes from actual App Insights data and Foundry Evals API.

**The production custom Agent disables content capture, but content capture was observed in Foundry `responsesapi`.** Shared settings were not changed. Do not enter confidential information. The [observability record](observability-en.md) documents counts/lengths and precautions.
