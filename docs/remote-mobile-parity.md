# Mobile Tlamatini scope and acceptance

Angela's explicit scope, 13 November 2026: control Tlamatini remotely through a
mobile application, with its capabilities adapted for touch. This is not Windows
desktop control and is not a miniature copy of the desktop interface.

For now, exclude exactly three panels: **Agentic Control Panel, Prompt Flow
Panel, and Admin Panel**. Carry every other menu, page and action, including
**Torrenter Workspace** and **Torrenter Sources**. English and Spanish must keep
the same behavior and separate account/edition identities.

Latest version: **[1.0.0](releases/v1.0.0.md)**. Firewall/network transport work
is deferred by Angela; acceptance first runs through the real local connection.

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

## Required mobile surface

The inventory comes from the current English `agent_page.html` navigation and
`agent/urls.py`, with Spanish parity required. A navigation entry alone is not
completion: its normal account permissions, input, action, outcome, cancellation
and error handling must work through RC.

| Area | Included functions |
| --- | --- |
| Chat | Conversation/history, cancellation, exact-request approvals, progress, execution reports and returned artifacts |
| Execution | Multiturn, Exec-Report, ACPX, Self-Modify, Ask Execs, Step by step, existing model-capacity rules |
| Files and views | Open, Save, Canvas, Terminals, Reset layout, context document and supported Open in actions |
| Context | Directory, file, view directory, clear context, file-type omissions |
| Panels retained | Tlachihuani/RC, Torrenter Sources, Torrenter Workspace |
| ACPX-Skills | Browse, Configure, Diagnostics, Reload Registry |
| External | MCP catalog, configuration and its existing actions |
| Config | MCPs, Agents, Models, URLs, Contacts, Access Keys Wizard, Live Editor & Terminater, Voice, Mic |
| Database | Backup database and Set DB, preserving the existing database mechanics |
| About | About, Usage, Check for updates |
| Session | Account/edition selection, reconnect, logout, start/stop Tlamatini |
| RC | Enrollment, device permissions/revocation, service observations, schedules, appearance |

Use the existing application behavior behind mobile-specific navigation and
controls. Keep secrets out of URLs and logs. Preserve account isolation, device
revocation, exact execution consent and owned-process shutdown. Do not expose an
arbitrary host URL, command, executable, service name or PID through a gateway.

## Work and evidence as of this record

The current installed work is tracked in the
[installed acceptance record](verification/2026-11-13-installed-rc-acceptance.md).
It supersedes the historical checkpoints below. Package
`20261114-033543-1abcb41e` is installed after uninstall with 1,475 matching files.
The completed final set has 100 real requests, 99 exact-answer passes, 170 mobile
passes and independent remote restart/final stop in both editions. The remaining
Spanish ES050 answer-format failure is retained. LAN/firewall remains deferred.

**Historical checkpoint — 01:35 UTC, 14 November 2026:** the verified package is
`20261114-012418-5334617f`. It passed **267/267 core** and **40/40 gateway** checks;
both installed main editions passed **85/85 mobile interaction checks** against
that exact packaged gateway. The gateway runs as an isolated visible development
host for these checks, so this remains distinct from installed SCM acceptance.
Generated-file links in chat now switch the phone to Canvas. A real touch through
the existing safe formatter and loader verifies the missing-file response travels
through the gateway, returns 404 and preserves the existing canvas document.
The legacy route accepts a generated-program database name, not a host file path;
the route traversal restriction remains intact.

- Gateway: `artifacts/rc-mobile-gateway-checks/bbd97ee8d8a142988e631ac1c6eae200/results.json`
- English: `artifacts/rc-live-mobile-preview/6c81398e3d1c4f97952cebb4d478ae0d/english-installed/mobile-results.json`
- Spanish: `artifacts/rc-live-mobile-preview/3289ac7dae51439292023739fe19ebcb/spanish-installed/mobile-results.json`
- Package log: `artifacts/release-builds/20261114-012418-5334617f/build.log`

The earlier generated-link assertion relied on Resource Timing, which did not
record the rejected response. The application displayed its normal missing-file
message and retained the document. The final assertion observes Chrome's actual
response event instead. The failed run and diagnosis are retained under
`rc-live-mobile-preview/7ada5cb0ad084be2b79ada642ed47735`. Spanish's first run
stopped on a real foreground-check failure before mobile acceptance; inspection
observed Chrome's low-storage notification and recovered document focus. The
fresh run passed without changing the visibility guard.

Build `20261114-011935-63c94a4d` failed with WiX `ERROR_DISK_FULL` at roughly
185 MiB free. Checksum-verified cleanup of obsolete generated staging recovered
8.06 GiB; source, main backups, packaged ZIPs, logs and evidence were retained.
See `artifacts/rc-local-diagnosis/package-failure-0120.json` and
`obsolete-staging-cleanup.json`. The subsequent full rebuild passed.

**The final uninstall/reinstall and 100 real-model batch are still pending
Windows administrator consent. 0/100 batch prompts have completed.** No fresh
elevation request or install request is staged. Firewall/LAN work stays deferred.

**Previous checkpoint — 01:10 UTC, 14 November 2026:** the corrected package was
`20261114-005815-bb532b43`, with **267/267 core** and **40/40 gateway** checks passed.
English and Spanish each passed **84/84 real installed-main mobile interactions**
through an isolated development gateway, including logout. The Spanish run found
its distinct `tlm_csrf_es_mx` cookie was not forwarded; the gateway now uses the
edition's CSRF cookie and scopes it to the workspace, isolating prior direct
browser logins. Both runs retained a real direct-login session to exercise this
boundary. English used the final package; Spanish used the corrected development
publish immediately before that package was built.

Final evidence:

- Gateway: `artifacts/rc-mobile-gateway-checks/ff221b8af94b48768fefc7b9497de3e8/results.json`
- English mobile: `artifacts/rc-live-mobile-preview/09a74df429f94d0ba6f482fc5c2fe33b/english-installed/mobile-results.json`
- Spanish mobile: `artifacts/rc-live-mobile-preview/f4170a63376c4f66b61d54f83190b7f7/spanish-installed/mobile-results.json`
- Stopped processes/services: `artifacts/rc-local-diagnosis/final-idle.json`
- Final script syntax and whitespace checks: `artifacts/rc-local-diagnosis/visible.log`

The final Tlachihuani reinstall and 100 real-model batch remain **unperformed**.
Windows canceled both elevation attempts; the second ended at 00:48 UTC. No
elevated installer queue started. The stale install request was removed before
the corrected package was built. Reopen Windows consent when Angela is available,
then install the exact corrected build. The earlier evidence below is retained
as chronological development history, not a claim that those packages are current.

- The old Tlachihuani installation was removed before the new package was
  installed. Both compiled setup helpers returned 0; 1,466 shipped runtime files
  matched the fresh build. The later source changes below need a new package.
- Normal installed English login, password-confirmed account enablement, native
  invitation/fingerprint approval, browser connection and one real model answer
  passed. The model returned `42` after its endpoint recovered.
- The 50-prompt English batch failed before a completed case: its application
  and browser did not survive between harness stages. The harness now requests
  detached application/browser processes; this correction still needs acceptance.
- A typed, separately granted stop operation and a dedicated-console cleanup
  helper were added. The current core run passed **267/267** checks. Actual
  installed English and Spanish boot, normal login, immutable account identity
  and owned-console cleanup each passed **6/6** checks. This direct helper test
  does **not** certify browser RC start/stop through the installed broker.
- Selectable execution modes and fresh capability observations were added to
  both main adapters and the RC client. **235/235** source checks passed in each
  edition. Both editions were frozen through the reviewed non-destructive
  packaging path and installed as runtime updates: **7,870 English** and
  **7,873 Spanish** files matched, with database/configuration bytes preserved.
  These are runtime overlays, not new full main-application installers.
- The mobile workspace now carries the existing application behind a fixed
  loopback, account-bound gateway. Chat, Canvas, Downloads, Settings and More
  provide touch navigation; all 32 explicitly inventoried menu actions and both
  Torrenter pages are present, with exactly the three requested panels excluded.
  Photos, mobile host-folder selection and canvas Save As have mobile controls.
  Terminals, dialogs and other existing application actions retain their normal
  handlers and permissions. Presence alone does not certify their outcomes.
- **33/33** isolated gateway HTTP/WebSocket checks passed against release build
  `20261113-235828-d4b69466`. Later fixes for form referrers, attachment streaming
  and mobile layout require a fresh package and rerun. The package verified
  **1,475** runtime files; it is not the currently installed final payload.
- Real headed Chrome checks use the installed English application, normal login
  and an isolated development gateway. They uncovered and fixed login rejection
  caused by an opaque form origin and desktop minimum widths clipping the phone
  composer. Chat/navigation geometry passed at 360, 390 and 768 pixels. Detailed
  file, terminal, dialog and page checks are still in progress. Service
  registration/heartbeats in this preview are fixtures; inference is not tested
  by it. Do not count these as installed SCM or real-prompt acceptance.
- Subsequent real interaction checks passed photo upload/preview/removal, file
  opening, editor input, canceled Save As, an actual host-file save, rejection of
  an external revision change, and terminal output with usable phone dimensions.
  All 13 listed application dialogs and four custom settings/About dialogs opened
  and closed. The gateway now preserves multipart content length for Django and
  injects its transport once into the document `head` (not into `header` elements).
  Signed device renewal passed after that correction. The full touch run at
  `artifacts/rc-live-mobile-preview/261c8eec0cc04ebb9efbbc1889e7c5d8`
  passed 78 checks before its test-created unsaved document correctly prevented
  navigation. The harness now saves a separate conflict-recovery copy first.
  A focused page run at `5fcc18a6bc794c6fa7e17bea565573c1` passed 7 checks including
  both Torrenter pages and the RC account page. Final queue-data readiness and the
  complete revised workflow still need their final acceptance run.
- The final repository package `20261114-004000-69ee48de` subsequently passed
  **267/267 core checks**, **38/38 isolated gateway checks** and **83/83 real
  installed-English mobile checks through that packaged development gateway**.
  The mobile run used touch events and included actual queue-data readiness,
  signed renewal, save-conflict recovery, terminal output, all listed dialogs
  and the included pages. Evidence:
  `artifacts/rc-mobile-gateway-checks/e843b0f08a4544cb8e2314c28ab0a69f/results.json`
  and `artifacts/rc-live-mobile-preview/40c3e8003ff64e4299aa7c862a0f5a60/english-installed/mobile-results.json`.
  Normal logout was added to the next installed acceptance run; it was not one
  of those 83 checks. The final elevated installation was requested again after
  these checks; Windows canceled that second request at **00:48 UTC, 14 November**.
  No elevated queue started. The exact build request remains staged; relaunch
  elevation when Angela is available to handle Windows consent. Do not mark it
  installed until `installed-final.json` confirms this exact build.
- The final elevated Tlachihuani reinstall launch was canceled by Windows at
  23:56 UTC on 13 November. No final installer queue started and no final install
  record exists. The older, initially verified installation remains installed.
- **0/100 requested batch prompts have completed successfully.** The earlier
  single real answer is separate evidence. Mobile microphone controls currently
  operate the Tlamatini computer's microphone; phone microphone capture has not
  been implemented or accepted. Physical-phone and LAN tests remain outstanding.

Evidence locations under the authoritative checkout:

- `artifacts/rc-local-diagnosis/main-installed.json`
- `artifacts/rc-local-diagnosis/frozen-boot-english-1794614782/results.json`
- `artifacts/rc-local-diagnosis/frozen-boot-spanish-1794614791/results.json`
- `artifacts/rc-mobile-gateway-checks/079a471e9da5446baf711470f9d97181/results.json`
- `artifacts/rc-live-mobile-preview/` (separate dated runs, screenshots and
  failures retained; the latest run is not automatically a passing run)

## Final acceptance required

1. Build the final source, uninstall before reinstalling Tlachihuani, and verify
   the installed payload. Carry updated main-edition code in actual frozen builds
   through a non-destructive packaging path; do not run the main destructive builder.
2. Start English and Spanish independently through RC and observe readiness.
3. Process 100 real model prompts, initially planned as 50 in each edition, with
   per-case receipts and mode propagation evidence. Do not count isolated unit
   checks or simulated responses as these prompts.
4. Stop each edition through RC and observe the verified processes exit.
5. Exercise every included mobile menu/action and its failure states at narrow
   phone, larger phone and tablet sizes in a real headed browser. Record physical
   device/network limits honestly; desktop viewport tests are not phone testing.
6. Preserve durable logs, screenshots, artifact identities and dated results.
