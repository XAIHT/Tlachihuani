# Remote-control predeployment acceptance

Created for Angela López Mendoza · XAIHT · 15 November 2026.

This checklist covers the current English and Spanish Tlamatini sources, Tlachihuani RC/XT, the canonical Tlamatini-RC client and the hosted cloud/connector. The previous 127.0.0.1:12481 walkthrough covered the installed local path only. The development videos cover a separate hosted lab with deterministic main adapters. Neither substitutes for the complete campaign below.

## Hosted prompt integration · 23 November review

The website path now carries real prompts and answers to an explicitly approved
running Tlamatini account through XT's outgoing HTTPS connection and the local
Remote-Controller adapter. It does not open an inbound port or add website launch,
shutdown or phone-audio controls. Diagnostics remain inert and separate from typed
commands. Retries retain identity, receipts survive reload, and full answers use
validated account-bound chunks. Ask Execs follows the existing selective policy;
Netstat is exempt, whereas the tested Executer action displayed approval.

Physical-phone English prompt/answer, reload and approval display were observed.
Real denial/no-execution and cancellation were not completed before tests stopped.
The wording-only website candidate was built but not promoted. Read the
[current integration guide](xaiht-link.md) and [dated evidence](verification/2026-11-23-hosted-prompts.md) before making
installation or release-acceptance claims. This pass changes documentation only;
existing version identifiers and installed services remain unchanged.

## Current boundary, verified in source

CloudConfiguration.Validate in src/Tlachihuani.RC.Cloud/CloudConfiguration.cs accepts only development-loopback and an HTTPS loopback IP. Program.cs accepts only --development. The public xaiht.org origin is currently rejected by implementation. Hosted full workspace and voice are also unavailable in this profile. Record these as unavailable/blocked, never passed. Do not remove the origin guards or expose the lab through an unrestricted proxy to make a deployment test pass.

The remaining public profile, browser-to-PC security protocol/client trust, consumer account/PC provisioning, production identity/secrets/operations and real external-device acceptance are described in [the hosted contract](../src/Tlachihuani.RC.Cloud/README.md) and [development guide](hosted-development.md). Testing the existing lab is useful predeployment evidence; these remaining implementation gates still need to be closed before a public launch.

## Establish the exact build under test

1. Finish development regression checks. Preserve all failing attempts and their subsequent corrections.
2. Angela rebuilds/reinstalls Tlamatini English, Tlamatini Spanish and then Tlachihuani. The RC client is packaged from the controller's wwwroot mirror of the separate canonical Tlamatini-RC/public source. The cloud service is a separate artifact; rebuilding a Windows installer alone does not build or deploy the cloud.
3. Record source commit plus dirty-source hashes, build timestamp/manifest, installer hashes, installed executable/module hashes and all client-asset hashes. Compare installed payloads to those exact builds. Version labels alone do not prove freshness. Retain the cloud image digest and source manifest separately.
4. Open fresh application pages after restarting the rebuilt applications. Confirm the updated static assets and shell cache v13. Preserve account data/grants. Replace or disable the initial user/changeme credential before any nonlocal acceptance.
5. Track each test as PASS, FAIL, NOT RUN or BLOCKED with edition, artifact identity, operation/request ID, screenshots and relevant redacted logs. Do not overwrite a failure with a later pass without retaining its evidence.

## Guided installed campaign — repeat in English and Spanish

Run one stage at a time. Keep the main chat and the RC client open side by side for approval tests. Use a separate application-owned test folder for deliberate file operations, outside Windows profiles; final user-chosen downloads are allowed.

| Stage | Exercise | Required observation |
|---|---|---|
| 1. Identity and onboarding | Named account login; enable account; invitation expiry and one-use redemption; compare full fingerprint; approve browser; reconnect | Correct account/edition in RC. Approval is required once; reconnect preserves the approved identity. Wrong/expired invitations fail. |
| 2. Permissions and isolation | Grant each scope independently; remove a scope; second account/browser; disable account and revoke device | Unauthorized controls/actions are refused server-side. No cross-account history, files or operations. Revocation ends relevant access and pending work. |
| 3. Monitoring and lifecycle | Start when stopped; ready disables Start; separate Stop authority; stop/start both main editions; RC stop/restart | RC and Torrenter lifetimes remain independent. Observations distinguish offline, loading, ready and unknown. No duplicate launch or replay after reconnect. |
| 4. Basic conversation/history | Repeat RC-TEST-01, load recent history, inspect main chat | One request/reply under the correct user, terminal receipt, matching saved history and no duplicate execution. |
| 5. Approval regression — first after rebuilding | Repeat RC-TEST-02 with Ask Execs enabled: run Get-Date -Format o once, approve in RC while main modal is visible | One execution; matching output; both prompts retire automatically without Ctrl+R. Main-to-RC approval also clears the RC prompt. |
| 6. Denial, cancellation and competing windows | Deny in each interface; cancel while awaiting approval; stop/disconnect while waiting; two tabs answer; request B after A | No execution after denial/cancellation; no orphan modal; first valid answer wins; stale A cannot dismiss B or cause another execution. A lost connection is not reported as proof of completion. |
| 7. Execution modes | Multiturn, Exec-Report, ACPX, Ask Execs, Step by step and shared Self-Modify; change dependencies; inspect effective modes | Submitted/effective options match the chosen modes; Step by step enables Multiturn; shared settings are not silently overwritten. Test actual applicable tools rather than checking labels only. |
| 8. Rich responses/files | Text, code, headings/tables, safe styled HTML, history; TXT/HTML/CSV/SVG/PNG/binary downloads; Unicode filenames | Local/RC rendering preserves intended content. Downloaded original bytes/hash and ownership match. Invalid, expired or other-account file access is refused. |
| 9. Full workspace | Open local full mobile workspace; chat, files, canvas, settings and available voice features; narrow viewport | Correct account and permissions; usable controls and no unintended host picker. Hosted workspace/voice remain explicitly unavailable until implemented. |
| 10. Scheduling | Create/edit/disable/delete schedules; correct timezone/day; restart; missed-run handling; revoke schedule authority | Exactly the intended bounded action is dispatched once. Missed/unknown results are explicit. Stop RC suspends its scheduler without stopping Torrenter. |
| 11. Recovery and persistence | Reload RC, reconnect, session expiry, adapter crash, RC restart and late completion during active work | No automatic resubmission; unknown outcomes remain visible until reconciled; saved grants/history persist appropriately; no stale approval after recovery. |
| 12. Completion review | Correlate RC activity, main execution report/history and controller receipts | No unexplained executions, missing results, credential leakage, cross-account records or unresolved failures. |

The earlier RC-TEST-01 and history observations remain useful historical evidence. RC-TEST-02 exposed a failed approval synchronization check; its installed retest remains mandatory after rebuilding.

## Latest hosted infrastructure campaign

Use the current source, not an older recording. The existing visible runner is:

    .\scripts\Test-HostedDevelopment.ps1 -IncludeBrowser -IncludeOidc -IncludeDocker

It verifies real HTTPS cloud/connector/relay, real Keycloak OIDC exchange, owner filtering, origin/CSRF/session/ticket enforcement, account/device authorization, permission changes/revocation, retries and uncertain results, long-lived routes, rich responses and original file bytes, English/Spanish headed UI, Linux image behavior and owned-process cleanup. Its main replies are fixtures; label them accordingly.

After that campaign passes, repeat the applicable installed stages through a hosted connection using the freshly rebuilt real English/Spanish adapters and live model, recording the actual cloud/connector/PC identities. This must be a separate live integration result; fixture success cannot stand in for it. Public profile limitations currently block the external phase.

## Public staging gate before xaiht.org

Once the public implementation and intended security/trust profile exist, verify a staging artifact with the same configuration shape as production: real provider registration and account provisioning; exact public TLS/callback/origin settings; secret rotation; session/device revocation; browser-to-PC protection; cloud restart and persistence/recovery; isolation and limits; logs/alerts; rollback and artifact provenance. Exercise actual phones and another computer over both Wi-Fi and a separate mobile network, including network switching/background/resume, cancellation, file download and reconnect without command replay. Desktop mobile-width emulation is not a physical-phone result.

Publish only after reviewing every failed, blocked and unrun row against the final artifact. This document authorizes no deployment and claims no current public acceptance.
