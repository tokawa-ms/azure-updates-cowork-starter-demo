# Azure deployment preflight

[日本語 (primary)](preflight-report.md) | English

First run: 2026-11-11. Updated: 2026-11-13. Shared-infrastructure and Web updates succeeded. Production Agent v11 and evaluation v12 are active and match current source. See the evaluation record for fixed-data evaluation results.

## Summary

| Item | Result |
|---|---|
| Type | azd project, resourceGroup scope |
| Template | `infra/main.bicep` and referenced modules |
| Parameters | `infra/main.parameters.json`, local azd environment |
| Bicep syntax | Build after final change succeeded without warnings |
| Initial preview | Succeeded; no new existing Foundry/model resources |
| Initial provision | Succeeded |
| Initial app deployment | Hosted Agent, Web, siteproxy deployed after resolving ACR/RAI blockers |
| Preview after Session Files migration | Succeeded; applied after rechecking explicit current values. No Storage deletion |
| Dedicated viewer preview / provision | Succeeded; legacy proxy/Storage skipped, not deleted. Web retained one replica |
| November 13 full update and recheck | Infrastructure deployment Succeeded; ACR-built Web Healthy / Running. Current Bicep, deployed images, and live endpoints compared |
| November 13 preview after legacy deletion | Succeeded; no recreation of old proxy, dedicated identity, or Storage. Current Web/Agents remained running |

## Tools executed

- `az bicep build --file infra\main.bicep --outfile <session-arm-file>`: exit 0.
- `azd provision --preview`: exit 0; initial preview took 42 seconds.
- `azd provision --no-prompt`: initial success. Reprovision with real images failed at ACR pull.
- `azd deploy agent --preview --no-prompt`: succeeded, but did not guarantee real deployment success.
- `azd provision --preview --environment auc-dev`: post-Session Files preview succeeded, exit 0. Changes were not applied at that point.
- Azure CLI retrieved current Container Apps environment, ACR, Web, and siteproxy values to compare with preview differences.

Versions: azd 1.37.3, azure.ai.agents 1.0.0, Bicep 0.49.2. Because the azd preview path was used, az CLI Provider / ProviderNoRbac switching was not performed.

## Initial what-if

The azd summary reported Create for six major resources: two Container Apps, an Express environment, ACR, Log Analytics, and Storage. It did not show Create for existing Foundry/models. Six is azd's major-resource count, not the total ARM count including children and role assignments. No deletion was requested.

## What-if after Session Files migration

The `auc-dev` preview succeeded. Existing Storage was `Skip`; no resource Create/Delete was shown. Observed differences:

| Resource | Preview difference | Assessment |
|---|---|---|
| `ca-web-auc-dev` | ACA registry/container settings; replicas `0..3` → `1..1`; omitted `workloadProfileName` | One replica matches the in-memory registry/Data Protection design; includes app/image updates |
| `ca-proxy-auc-dev` | ACA registry/container settings; omitted `workloadProfileName` | Includes app/image updates for the new configuration |
| `cae-auc-dev` | Expression for Log Analytics `customerId`; omitted peer settings, `publicNetworkAccess`, workload profile | Current public network is `Enabled`, mTLS/peer traffic encryption disabled, Consumption profile. Intended values, but template omissions were present |
| `<container-registry>` | Omitted `dataEndpointEnabled` and `encryption` | Azure CLI current values were both null |
| `log-auc-dev`, Storage Account | Skip | No change or Storage deletion |

The ACA environment `customerId` changed to an expression referencing the same existing Log Analytics workspace. Although no deletion was shown, property omissions on several resources led to holding application at that stage.

## Recheck with explicit current values and apply

ACA `publicNetworkAccess`, peer settings, Consumption workload profile, Container App `workloadProfileName`, and ACR `dataEndpointEnabled` / `encryption` were explicitly set in Bicep to match current Azure values. `enableFips` was not specified because it is absent from Bicep type definitions (BCP037). Bicep built without warnings.

The remaining preview differences were Container App registry/container settings, Web replicas `0..3` → `1..1`, and the ACA `customerId` expression. ACR, Log Analytics, and Storage were Skip; no deletion.

`azd provision --environment auc-dev` was applied on 2026-11-11 (1 minute 47 seconds), followed by Agent, Web, and siteproxy deployment. Cloud smoke tests verified artifact generation/download, user isolation, and session deletion. See [deployment record](docs/notes/deploy-en.md).

## Issues and recommendations

Initial deployment stopped on Express ACR pull and Hosted Agent RAI policy configuration, then succeeded after those issues were resolved. See the [deployment record](docs/notes/deploy-en.md) for history/workarounds.

The initial existing-app rollout and Session Files migration are applied. Authentication was not replaced with keys, and guardrails were not removed. The new-resource branch without existing Foundry references remains unverified.

## Preflight for dedicated viewer and evaluation Agent

Executed on 2026-11-12 with azd 1.37.3:

- `az bicep build --file infra\main.bicep --outfile <session-directory>\viewer-infra.json`: no warnings, exit 0.
- `azd provision --preview --environment auc-dev --no-prompt`: succeeded, 26 seconds.
- `azd deploy agent-eval --preview --environment auc-dev --no-prompt`: succeeded; confirmed planned creation of `updates-cowork-eval`.
- `azd provision --environment auc-dev --no-prompt`: succeeded, 1 minute 38 seconds.

| Resource | what-if | Verification |
|---|---|---|
| `ca-web-auc-dev` | Modify | Registry/container settings. Removed old proxy variables; retained one replica |
| `cae-auc-dev` | Modify | Expression difference referencing the same Log Analytics workspace `customerId` |
| `ca-proxy-auc-dev` | Skip | Excluded by `enableLegacyProxy=false`; existing resource not deleted |
| ACR, Log Analytics, Storage | Skip | No deletion |
| Existing Foundry/models | No creation | Continued existing-project use |

Provision warned that it could not prevalidate roleAssignments/write at every target scope, but application succeeded. Postdeploy role assignment for the new Hosted Agent identity also succeeded. The evaluation Agent uses fixtures only, and custom production Agent content capture is disabled. However, production content was observed in Foundry `responsesapi`; app settings alone cannot disable that. Shared settings were not changed.

The final app fixes changed no Bicep and redeployed only `agent` / `agent-eval`. The deployed Web was verified for summaries, Word/PPTX generation/download, user isolation, and loss of access after conversation termination. See [deployment](docs/notes/deploy-en.md), [MCP/observability](docs/notes/observability-en.md), and [evaluation](docs/notes/evaluation-en.md).

## Recheck after the 2026-11-13 full update

Executed with Azure CLI 2.92.0, azd 1.37.3, Bicep 0.49.2. No additional provision was performed during this recheck.

- `az bicep build --file infra\main.bicep --outfile .artifacts\azure-rollout-20261113\main.compiled.json`: exit 0.
- `azd provision --preview --environment auc-dev --no-prompt`: exit 0, 23 seconds. Web/Container Apps environment were Modify; legacy proxy, ACR, Log Analytics, Storage were Skip. No Create/Delete.
- `az deployment group show -g <app-resource-group> -n <infrastructure-deployment>`: Succeeded, 2026-11-13 06:55:52 JST. References existing Foundry/App Insights/models, with `enableLegacyProxy=false`.
- `az deployment group export -g <app-resource-group> -n <infrastructure-deployment>`: exported deployed template compared with current compiled Bicep; zero differences excluding metadata/type-name casing. Resource names/IDs are publication-safe placeholders.

Remaining preview differences were registry/container settings referencing Web's existing image and the same workspace's `customerId` expression. The actual Web revision was Healthy / Running, one replica, 100% traffic. Production v11 / evaluation v12 were active. Existing `gpt-5.4-mini` / `gpt-5-mini` models were Succeeded; no model version/capacity update was performed.

Execution results, running-image digests, and matching source checksums are documented in the [deployment record](docs/notes/deploy-en.md#compare-azure-execution-results-and-deployed-contents). Retrieved JSON/logs were saved under `.artifacts/azure-rollout-20261113/`. Legacy proxy/Storage were still retained at that point. RAI variable-expansion and shared-Foundry content-capture limitations remain unchanged.

## Preview after legacy resource deletion

On 2026-11-13, after explicit operator approval, the legacy proxy, dedicated Managed Identity/AcrPull assignment, and old Storage were deleted. The Storage-specific Event Grid system topic disappeared with the account. Before deletion, current Web/Agents were verified not to reference those resources.

Post-deletion `azd provision --preview --environment auc-dev --no-prompt` took 23 seconds, exit 0. Web and Container Apps environment remained the same two Modify entries; ACR/Log Analytics were the two Skip entries. No Create/Delete, nor any planned recreation of old proxy/identity/Storage. Only preview was run; no additional provision.

Current Web remained Healthy / Running; `/healthz` returned `ok`; the update list returned 20 entries. Production v11 / evaluation v12 remained active. Before/after listings and execution records are in `.artifacts/legacy-cleanup-20261113/`; see [deletion procedure and targets](docs/notes/deploy-en.md#delete-legacy-proxy-and-storage-2026-11-13).
