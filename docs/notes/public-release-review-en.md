# Pre-publication information review

[日本語 (primary)](public-release-review.md) | English

Reviewed and remediated: 2026-11-13. At the user's request, local Git history was sanitized and a public author identity configured. No remote is configured; no visibility change or push was performed.

## Results

No credentials, access tokens, or private keys were detected in reviewed files and reachable history. Findings concerning real environment identifiers, internal information, and commit authors' personal names/corporate email addresses were remediated in both current files and local history. The table records the original disclosure findings and their resolution, not evidence that the values enable unauthorized access.

Line numbers and commit IDs refer to the pre-rewrite history. Original commits are excluded from publishable history and were also removed from local object storage. Sensitive values are not reproduced here.

| # | Severity | File | Lines | Vulnerability | Confidence |
|---|----------|------|-------|---------------|------------|
| 1 | ⚪ LOW | `docs/notes/deploy.md`, `docs/notes/evaluation.md`, `docs/notes/observability.md`, `preflight-report.md` (original `da46186977`) | 5; 110,133,160; 30,38,40; 43 | Resolved: real Web URL, Foundry management links, trace IDs, and resource identifiers sanitized in history | 10/10 |
| 2 | ⚪ LOW | `docs/notes/deploy.md` (original `da46186977`) | 103 | Resolved: internal network details replaced with neutral design wording in history | 10/10 |
| 3 | ⚪ LOW | All three pre-rewrite commits | author / committer headers | Resolved: personal names/corporate email addresses replaced with public identity and noreply email | 10/10 |

Foundry links point to authenticated management pages; they are not treated as access tokens. Public Microsoft references, built-in Azure role GUIDs, localhost, explicit placeholders, synthetic test values, and third-party license attribution were not classified as disclosures.

## Changes to current files

Real Web URLs, person-linked shared-resource names, deployment/correlation IDs, image digests, and deleted-resource identifiers were removed or replaced with placeholders in deployment records. Real Foundry report URLs, actual ACR/deployment identifiers in preflight, and trace IDs in observability records were also removed from public documents. The internal-network explanation was replaced with a neutral description of the design that avoids Blob downloads. Learning procedures, results, and limitations remain.

Japanese remains primary. `-en.md` companions cover README, design, implementation plan, investigation/operations records, preflight, contributor instructions, legacy design documents, and this report. Runtime prompts/skills under `src/UpdatesCowork.Agent/Instructions/` and `Skills/` were not translated, to avoid changing behavior.

The root `LICENSE` is MIT License, copyright holder `tokawa-ms`, year 2026. DOMPurify/marked license files remain intact. Third-party content such as Azure update articles is not relicensed under this repository's MIT License; terms and redistribution permission require separate review.

## History sanitization and remediation results

All three commits reachable at remediation time were rewritten while preserving ancestry and timestamps. Nine replacements sanitized historical versions of four documents. All 251 tracked working files, including current source/documents, and the HEAD tree were verified identical before and after the rewrite. Application code and behavior were not changed.

Every author and committer header now uses `tokawa-ms <18474082+tokawa-ms@users.noreply.github.com>`. The same values were configured locally for this repository; global configuration was not changed. Copilot co-author attribution using a public GitHub noreply address was retained.

Reflogs retaining original history were expired, and unreachable original objects were removed. All three original commit objects were confirmed absent, `refs/heads/main` was the only ref, and `git fsck --strict --full --no-reflogs` passed. All rewritten commits and 300 unique reachable blobs were scanned; no extracted private indicators or old author identities remained.

Avoid distributing a ZIP of the entire working directory. Ignored `.azure`, `.artifacts`, `.scratch`, `eval/out`, and build/test outputs may contain real settings, responses, or generated files. Distribute only reviewed, tracked content. After pushing to GitHub, also check Secret scanning / Push protection, published history, and attached artifacts.

## Scope and limitations

The initial audit covered 236 tracked files, 258 unique historical blobs, and two commits reachable through the sole local ref `refs/heads/main`. Seven newly appearing nonignored files were also scanned. Both tracked Word/PowerPoint templates were inspected for XML, metadata, and external relationships. The 6,346 ignored files were inventoried by filename only, not content-audited.

Documentation changed concurrently, so the initial audit is not certification of an immutable final tree. Unreachable objects/reflogs were outside the initial audit but were removed during remediation to avoid retaining original history. Copies stored elsewhere, OCR of image text, and legal redistribution rights for third-party content remain outside scope. No remote was configured. No files were uploaded to external scanning services and no live environment was contacted. Automated/manual checks can miss information; no detection is not proof of absence.

After translation, 33 changed/new documentation files including LICENSE were rechecked for known private indicators and generic secret patterns; no new actionable disclosure was found. All 13 Japanese/English pairs passed heading, checklist, code-fence, table-structure, relative-link, and anchor checks. All 26 documents were rendered locally to HTML and automatically checked for unintended horizontal overflow; representative images were visually inspected. `git diff --check` also passed. This task changed documentation/licenses only; application builds/tests were not rerun.
