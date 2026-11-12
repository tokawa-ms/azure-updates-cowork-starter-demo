# Local integration and observability record

[日本語 (primary)](local-integration.md) | English

Verified: 2026-11-11. The real model was the existing `gpt-5.4-mini` deployment. Update information came from repository fixtures. No new Azure model was created.

This record predates the MCP migration. See the [observability record](observability-en.md) for the current MCP configuration and Azure verification of Q4. The instrumentation and unverified items below describe the situation at the time.

## Verified APIs

- Q1: The pinned Foundry.Hosting package contains `builder.AddFoundryResponses(agent)` and `app.MapFoundryResponses()`. Verified through XML documentation, a build, and a 200 response from `/readiness`.
- Q2: `AIProjectClient.AsAIAgent(ChatClientAgentOptions, ... loggerFactory, services)` exists and worked with current-time context, local skills, and function tools.
- Q3: The local conversation API and `previous_response_id` were verified. The BFF development configuration uses `PreviousResponseId`; Word and PowerPoint could be requested in the same conversation after a summary.
- Q13: Prompt Shields uses `2024-09-01`. HTTP substitution tests verified Off / Annotate / Block, inspection failures, invalid responses, and long-text splitting. This does not constitute a measurement of attack-detection accuracy in the real service.

`Directory.Packages.props` is authoritative for packages. Local `DefaultAzureCredential` was restricted to CLI credentials with `AZURE_TOKEN_CREDENTIALS=AzureCliCredential`. Because `dev` selected another cached identity and caused a 403, credential selection was corrected rather than adding permissions.

## Conversations and documents

Summarizing update ID `559242` actually invoked `load_skill` and `get_azure_update`. Word and PowerPoint were generated using the same cookie and conversation and downloaded through the BFF. Requesting the same artifact ID with a different cookie returned 404.

The generated Word document was opened in desktop Word, converted to PDF, and its single page visually inspected. PowerPoint was opened in the desktop app; all four 16:9 slides were exported to PNG and inspected. No missing Japanese characters, overflow, or overlap was observed. PowerPoint text heights were also checked. Validation files and images were saved in session storage, not the source repository.

The BFF was corrected to accept local artifact paths in the `yyyy/MM/dd/art_ID/file` format. Path traversal and trailing newlines remain disallowed, and the ownership registry check is retained.

## Input and logging boundaries

External strings, including page titles, are HTML-encoded and enclosed in `<untrusted_content>`. Error response bodies and communication exception bodies are not logged; only status codes and exception types are recorded. The tool limit is 12 calls per turn, and cancellation propagates.

The SSE recordings themselves were valid UTF-8. Reading response `.Content` in PowerShell caused mojibake through charset interpretation, so validation explicitly decoded raw bytes as UTF-8. Large SSE and CLI debug outputs are saved to files rather than streamed to the terminal.

## Traces

In Aspire Dashboard, only the traceId for the relevant BFF response was opened. These parent-child relationships were verified within one trace:

```text
POST /api/chat
  chat.turn
    HTTP POST
      POST /responses
        invoke_agent updates-cowork
          chat gpt-5.4-mini
          execute_tool load_skill
          execute_tool get_azure_update
            azure_updates.get
          execute_tool read_skill_resource
          execute_tool create_word_report
            artifact.save
```

Because the host provides agent/chat/tool instrumentation, AgentFactory does not add another `UseOpenTelemetry` wrapper. Each app has an identifiable default service.name. Other apps may send data to a shared Dashboard, so filter by your own traceId rather than dumping all logs.

Azure Hosted Agent verification of Q4 / Q5, the Foundry portal, KQL, and feedback correlation were not complete at that time. Local success is not a substitute for verifying Azure propagation.
