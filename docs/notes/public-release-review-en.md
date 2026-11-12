# Pre-publication information review

[日本語 (primary)](public-release-review.md) | English

Reviewed: 2026-11-13. No visibility change, push, commit, or Git history rewrite was performed.

## Results

No credentials, access tokens, or private keys were detected in reviewed files and reachable history. However, historical commits retain real environment identifiers and commit authors' personal names/corporate email addresses. Sanitizing current files does not remove history; decide how to handle it before publication. These are disclosure findings, not evidence that the values enable unauthorized access.

Line numbers refer to the indicated commit. Sensitive values are not reproduced here.

| # | Severity | File | Lines | Vulnerability | Confidence |
|---|----------|------|-------|---------------|------------|
| 1 | ⚪ LOW | `docs/notes/deploy.md`, `docs/notes/evaluation.md`, `preflight-report.md` (`da46186977`) | 5; 110,133,160; 43 | Real Web URL, Foundry management links, and resource identifiers remain in history | 10/10 |
| 2 | ⚪ LOW | `docs/notes/deploy.md` (`da46186977`) | 103 | Internal network-policy statement remains in history | 10/10 |
| 3 | ⚪ LOW | Commits `0d0cd4e14b`, `da46186977` | author / committer headers | Personal names/corporate email addresses remain in commit metadata | 10/10 |

Foundry links point to authenticated management pages; they are not treated as access tokens. Public Microsoft references, built-in Azure role GUIDs, localhost, explicit placeholders, synthetic test values, and third-party license attribution were not classified as disclosures.

## Changes to current files

Real Web URLs, person-linked shared-resource names, deployment/correlation IDs, image digests, and deleted-resource identifiers were removed or replaced with placeholders in deployment records. Real Foundry report URLs, actual ACR/deployment identifiers in preflight, and trace IDs in observability records were also removed from public documents. The internal-network explanation was replaced with a neutral description of the design that avoids Blob downloads. Learning procedures, results, and limitations remain.

Japanese remains primary. `-en.md` companions cover README, design, implementation plan, investigation/operations records, preflight, contributor instructions, legacy design documents, and this report. Runtime prompts/skills under `src/UpdatesCowork.Agent/Instructions/` and `Skills/` were not translated, to avoid changing behavior.

The root `LICENSE` is MIT License, copyright holder `tokawa-ms`, year 2026. DOMPurify/marked license files remain intact. Third-party content such as Azure update articles is not relicensed under this repository's MIT License; terms and redistribution permission require separate review.

## Decisions required before publication

If publishing both historical commits, confirm permission to disclose environment and author information. Otherwise, use an approved history-sanitization process or create the initial commit of a new public repository using only reviewed files. Adding a sanitizing commit while retaining the original history does not resolve these findings.

Explicitly configure a public commit identity, such as a GitHub noreply address. This review did not rewrite history or create a separate public repository.

Avoid distributing a ZIP of the entire working directory. Ignored `.azure`, `.artifacts`, `.scratch`, `eval/out`, and build/test outputs may contain real settings, responses, or generated files. Distribute only reviewed, tracked content. After pushing to GitHub, also check Secret scanning / Push protection, published history, and attached artifacts.

## Scope and limitations

The initial audit covered 236 tracked files, 258 unique historical blobs, and two commits reachable through the sole local ref `refs/heads/main`. Seven newly appearing nonignored files were also scanned. Both tracked Word/PowerPoint templates were inspected for XML, metadata, and external relationships. The 6,346 ignored files were inventoried by filename only, not content-audited.

Documentation changed concurrently, so the initial audit is not certification of an immutable final tree. Remote-only history, unreachable objects/reflogs, OCR of image text, and legal redistribution rights for third-party content were outside scope. No files were uploaded to external scanning services and no live environment was contacted. Automated/manual checks can miss information; no detection is not proof of absence.

After translation, 33 changed/new documentation files including LICENSE were rechecked for known private indicators and generic secret patterns; no new actionable disclosure was found. All 13 Japanese/English pairs passed heading, checklist, code-fence, table-structure, relative-link, and anchor checks. All 26 documents were rendered locally to HTML and automatically checked for unintended horizontal overflow; representative images were visually inspected. `git diff --check` also passed. This task changed documentation/licenses only; application builds/tests were not rerun.
