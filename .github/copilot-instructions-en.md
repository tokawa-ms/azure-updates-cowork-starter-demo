# GitHub Copilot instructions (Azure Updates Cowork)

[日本語 (primary)](copilot-instructions.md) | English

## About this repository

This learning sample reverse-proxies the Azure updates site and places a chat beside it. The chat calls a Microsoft Foundry Hosted Agent (C#, Microsoft Agent Framework) to summarize the displayed update and generate Word/PowerPoint. The web app runs on Azure Container Apps Express.

- The design baseline is `docs/design.md`; work order/acceptance criteria are in `docs/implementation-plan.md`. English companions are [design](../docs/design-en.md) and [implementation plan](../docs/implementation-plan-en.md).
- Implement only the requested phase. If implementation must differ from the design, explain why and propose an alternative in the PR rather than changing it silently.

## Technical rules

- .NET 10 (`net10.0`), latest C# language version, `Nullable` enabled, warnings treated as errors.
- Declare package versions only in `Directory.Packages.props`. When adding a package, update the table in design section 4.3.
- Web APIs use Minimal API. DTOs are `record` types. JSON uses `System.Text.Json` source generation (`AucJsonContext`).
- The UI is static HTML/CSS/ES modules with no build step. Do not add frameworks or CDNs.
- UI strings/code comments are Japanese; identifiers are English.

## Preview APIs

- Hosted Agent hosting, `Azure.AI.AgentServer.*`, and `Azure.AI.Projects` 3.x are preview APIs and change frequently. Do not guess method names/arguments.
- First verify pinned-package XML documentation, official samples, and Microsoft Learn (MCP server `microsoft-learn`).
- For design items marked [要確認], record verified results as “Q number: result” in the PR (design section 14.2).

## Security rules

- Never put API keys, connection-string keys, or passwords in source/configuration. Azure authentication uses only `TokenCredential`: `ManagedIdentityCredential` in Azure and `DefaultAzureCredential` locally.
- Treat Agent tool arguments as untrusted and always validate them. Do not build tools that accept a URL to fetch.
- Enclose externally retrieved strings in `<untrusted_content>` before passing them to the model.
- Always sanitize displayed HTML with `DOMPurify`.
- Do not log cookie values, tokens, or email addresses in logs/spans. Represent users through hashed user keys.

## Observability rules

- ActivitySource/meter names are `UpdatesCowork.Web`, `UpdatesCowork.SiteProxy`, and `UpdatesCowork.Agent`. Follow design section 8.3 for spans, attributes, and metrics; use `TelemetryNames` constants from `UpdatesCowork.Shared`.
- For every new external call, verify emitted spans in Aspire Dashboard.

## Testing rules

- Tests use xUnit v3. Unit tests do not access the network; substitute `HttpMessageHandler`.
- Model-calling evaluation tests carry `[Trait("Category", "Eval")]` and are excluded from normal CI.
- Copy each phase's acceptance criteria into the PR description as a checklist.

## Common commands

```powershell
dotnet build
dotnet test --filter Category!=Eval
docker compose -f tools\otel\compose.yaml up -d          # Aspire Dashboard (http://localhost:18888)
dotnet run --project src\UpdatesCowork.Agent             # http://localhost:8088
dotnet run --project src\UpdatesCowork.SiteProxy         # https://localhost:7102
dotnet run --project src\UpdatesCowork.Web               # https://localhost:7101
azd up
azd deploy agent --preview --no-prompt                   # Preflight for azure.yaml
```
