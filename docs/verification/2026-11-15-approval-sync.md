# Approval synchronization correction · 15 November 2026

Created for Angela López Mendoza · XAIHT.

## Reported failure

During Angela's fresh-install walkthrough, RC-TEST-02 asked the English Tlamatini account angela to execute Get-Date -Format o. Angela approved in RC and received a successful result. The main chat showed the same result but kept its old execution-permission modal open until Ctrl+R. This is a failed cross-window synchronization check, not acceptable completed behavior.

## Correction

Both main editions now emit a request-scoped exec-permission-resolved event when the broker retires an approval. The event is delivered before the waiting execution continues. The real main consumer forwards it to every observing tab in that account; the RC runtime translates it into its existing permission progress contract. Approval, denial, cancellation, teardown and runtime auto-proceed use this path.

The broker accepts the first decision atomically, so a competing window cannot overwrite it before the worker wakes. Frontend dismissal is silent and matches the exact request ID; it never sends a second denial, closes a newer prompt or reopens a recently resolved duplicate. A lost chat socket also removes the unusable modal while retaining the disconnected-state guidance.

The controller clears only the matching pending request and rejects invalid decision values. A bounded transient replay guard suppresses delayed prompts already answered by this operation. Recovery removes running authority, so replay protection needs no durable schema or wire-format change. Existing account/grant data and backup/restore mechanisms are unaffected.

The shared local/hosted RC client ignores older receipt responses once a newer response has been applied, retires permission cards consistently, and ignores late callbacks for removed cards. Canonical Tlamatini-RC/public and RC.Server/wwwroot are updated together; service-worker cache is v13. Main edition static cache suffix includes rc-approval-sync-1.

## Verification

Final evidence is retained under artifacts/rc-approval-sync-20261115. The owned foreground PowerShell transcript and headed-browser photographs are retained there.

| Checks | Final result / evidence |
|---|---|
| English main regressions, including real broker and consumer/RC event delivery | 307 passed; main-en.json |
| Spanish main regressions | 309 passed; main-es.json |
| RC core including approval conflict, stale identity, duplicate progress and restart compatibility | 294 passed; core-final/results.json |
| Headed approval scripts, both languages, including loss of connection and delayed HTTP receipt ordering | 41 passed; browser-styled/results.json, using the actual application dialog CSS |
| Canonical client checks | 15 passed; client/results.json |
| Current hosted relay/API and bilingual browser | 89 passed; rc-hosted-validation/20261115-195008-4d96aa8e/relay-and-browser/results.json |
| Current real Keycloak/OIDC | 25 passed; rc-hosted-validation/20261115-195008-4d96aa8e/oidc/results.json |
| Linux-container checks | 8 passed; rc-hosted-validation/20261115-195008-4d96aa8e/container/results.json |

The final hosted [summary](../../artifacts/rc-hosted-validation/20261115-195008-4d96aa8e/summary.json) reports all requested stages passed. The eight functional suites above total **1,088 passing checks**, counted once per final suite; retries are not added. All 18 packaged client assets match the canonical copy, source mirror, built Windows server and built cloud output. Source syntax and whitespace checks pass with Windows CRLF recognized. Provenance/source hashes and asset hashes are retained in the approval evidence directory.

All owned test hosts, browsers, sockets and containers are closed; test ports are free and compiler servers were shut down. Docker Desktop was started for this campaign and left running because other containers were active at cleanup; no unrelated container was stopped. The development console remains open and its queue is ended.

Final provenance scripts corrected assumptions about mixed Windows line endings, the standalone canonical client (which is not a Git checkout), its unshipped legacy files, and the unavailable Get-FileHash cmdlet. Earlier failed script records remain visible; final SHA-256 checks use the actual packaged manifest and .NET hashing.

The focused browser harness executes the actual production approval and receipt scripts with deterministic event/receipt inputs and actual jQuery dialog behavior. Its fixtures do not execute a model or run the installed applications. The Python transport regression executes the actual broker and consumer approval methods with an isolated account lane. The hosted harness uses real cloud/connector/relay and identity-provider components but deterministic main replies. Installed live-model approval acceptance must be repeated on Angela's rebuilt packages.

Earlier attempts remain retained: native unittest stderr was initially treated as a PowerShell error; the transport test fixture initially omitted the required account-lane claim; the first hosted run passed 89 relay/browser checks but could not start OIDC because Docker's Linux engine was stopped; a whole-working-tree whitespace check reported unrelated existing changes. These attempts are not relabeled as passes. Final checks preserve exit codes and scope whitespace validation to this change.

No installer was built or installed, no production site was changed, no version was bumped, and no commit/tag/push was performed. Development builds and isolated test artifacts are distinct from Angela's release recompilation.

## Resume acceptance

Use the [complete current predeployment checklist](../remote-control-predeployment-checklist.md). Rebuild/reinstall both main editions and Tlachihuani, verify installed hashes against those builds, then repeat RC-TEST-02 with both windows visible. Approving in RC must close the matching main prompt without a refresh; approving in main must retire the RC prompt. Follow with denial, cancellation, competing tabs and the remaining local and hosted stages.

The latest cloud entry point still implements only --development and rejects public origins. Public xaiht.org deployment, real external-device acceptance and the remaining security/provisioning implementation gates are explicitly separate from passing this source regression campaign.
