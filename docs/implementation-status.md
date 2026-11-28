# Implementation status · 1.0.1

Latest version: **[1.0.1](releases/v1.0.1.md)**, a fix release for 1.0.0, the first official release after several pre-releases. Dated sections keep the versions they tested.

## In the source after 1.0.1 · not committed · 27 November 2026

**HandGI** (`src/Tlachihuani.HandGI`, `src/Tlachihuani.HandGI.Core`) controls the sound
volume with hand signs in front of a camera; XT controls it from **Services… → Runtime →
HandGI**. XT's **Stop Services** now ends every service at once (`ForcedStop`), every
**Services… → Runtime** section has the same light, status line and buttons, and Flow
Pills publishes its process so that its Runtime **Stop** can stop every flow and close it.
A new layout check of HandGI's windows found three problems, and all three were fixed.
Visible run `artifacts/verification/services-handgi-2026-11-27-h` then passed: 19 unit
checks, the HandGI lifecycle (7) and the Flow Pills lifecycle (4) in test folders, and the
Services panel and HandGI windows at 144 and 96 DPI. A 1.0.1 package built from this
working tree (`artifacts/release-builds/20261127-222520-d6ba8cb5`, local edits included)
passed its package checks and was installed on Angela's computer; her own check of the
installed HandGI stopped at its first step. None of this is committed. See
[HandGI](handgi.md) and the [27 November verification](verification/2026-11-27-handgi.md).

## 1.0.1 · fix release · 26 November 2026

The `v1.0.1` tag points to commit `4ef6b61`. Jobs no longer restarts a busy Torrenter
engine: a 15-second timeout now means busy, and only an engine that has owed a reply for
more than 3 minutes is restarted. The release builder built the tag with no local edits
(run `20261127-044723-c3f7fa5b`): 13 component checks, publish, setup and the MSI payload
and ZIP checks passed. `Directory.Build.props` and the internal lock references declare
**1.0.1**. A clean-Windows installation and reboot of the 1.0.1 package have not been
performed. See the [1.0.1 notes](releases/v1.0.1.md).

## 1.0.0 · first official release · 25 November 2026

Tlachihuani 1.0.0 is the first official release, after the pre-releases 0.1.0 to
0.40.1; see the [1.0.0 notes](releases/v1.0.0.md). The `v1.0.0` tag points to commit
`a7bd0df`. The release builder (run `20261125-135752-f5990157`) passed the
release-version regression checks, 13 component checks including Flow Pills, publish,
setup, and the MSI payload and ZIP verifications, and wrote
`dist/Tlachihuani-1.0.0-windows-x64.zip` with its `.sha256`. `Directory.Build.props`
and the internal lock references declare **1.0.0**. A clean-Windows installation and
reboot of the 1.0.0 package have not been performed.

## Flow Pills source implementation · 25 November 2026

XT gained a Flow Pills mode: a separate per-user window that opens, draws and runs
Tlamatini's `.flw` agentic flows and `.fpmt` flow pills (`src/Tlachihuani.FlowPills`,
`workers/flowpills/flowpills_compile.py` and the window in `src/Tlachihuani.Tray`). Each run
lives on its own thread and in its own kill-on-close Windows job; up to 256 run at once by
default, admitted by count and free memory. `scripts\Build.ps1 -Mode Check` passed 13 checks,
including the new Flow Pills check, and RC 318/318. Visible runs of the real window passed
with the agents of `C:\Tlamatini`, and a publish with the release settings carries the new
files. That pass built no installer and made no version change, tag or commit; the work
was then committed as `a7bd0df` and released in 1.0.0. A pill answered by a real model
and Terminater steps were not exercised. See
[Flow Pills](flow-pills.md#verification--25-november-2026).

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

## Verbose trace log, quiet idle loops and 0.38.0 · 21 November 2026

Product projects are compiled from traced copies of their sources: a
`TraceLog.Enter` at every method and a `TraceLog.Step` before every statement
(`tools/Tlachihuani.TraceWeaver`, wired in by `Directory.Build.targets`). The
Torrenter engine, setup and the scripts write the same line format, and XT copies
the services' logs into Tlachihuani.log. Polling loops run inside `TraceLog.Pass`
or `tracing.pass_scope`, which leave out a pass identical to the previous one. A
Debug build of XT left `StudioForm.cs` untraced as intended; no 0.38.0 package was
built, installed or tested. See the [0.38.0 notes](releases/v0.38.0.md).

`Directory.Build.props` and the internal lock references now declare **0.38.0**,
matching the `v0.38.0` tag, and every current documentation pointer names 0.38.0.

## xaiht.org link, Tlachihuani.log and 0.37.0 · 19 November 2026

XT writes **Tlachihuani.log** and shows it with **Services → See log**. The new
**xaiht.org** tab connects the computer to the **Tlamatini-RC** page on xaiht.org;
XT asks for messages over outgoing HTTPS, the page shows the computer online, and
each JSON message sent with **SEND** is written to the log and acknowledged. A
visible end-to-end check against the real page served locally with a memory store
passed **21/21**, twice; the web-app's 171 tests passed. xaiht.org's own Cloud SQL
tables and an installed 0.37.0 package are not yet tested. See the
[guide](xaiht-link.md) and the [19 November record](verification/2026-11-19-xaiht-link.md).

`Directory.Build.props` and the internal lock references now declare **0.37.0**,
matching the `v0.37.0` tag, and every current documentation pointer names 0.37.0.

## XT workspace sign-in and 0.36.0 · 17 November 2026

XT **Services → Runtime → Open workspace** now opens `127.0.0.1:12064/?signin=1`,
and the Jobs launcher sends the browser to the edition's login page before the
workspace. The queue shown therefore belongs only to the account that just
signed in; **Open downloads** keeps the current login. Headed source checks
passed **20/20 English and 20/20 Spanish** on isolated ports. See the
[17 November record](verification/2026-11-17-workspace-signin.md).

`Directory.Build.props` and the internal lock references now declare **0.36.0**,
matching the `v0.36.0` tag, and every current documentation pointer names 0.36.0.
The installed XT and Jobs still run their previous build until a package with this
change is installed.

## Hosted development foundation · 15 November 2026

The English/Spanish hosted client, OIDC sign-in, bounded relay and outbound RC
connector are implemented in an explicit HTTPS loopback lab. **443 distinct
checks passed**, including real headed-browser, Keycloak, Linux-container and
existing RC regressions. Public Internet mode remains disabled and unfinished:
reviewed endpoint encryption/PQ, trusted-client distribution and production
account provisioning are still required. Torrenter remains independent; no
installed update or xaiht.org deployment occurred. Read the
[verification record](verification/2026-11-15-rc-hosted-development.md),
[English guide](hosted-development.md) or [guía en español](hosted-development.es.md).

## Installed connection walkthrough · 14 November 2026

The installed local-PC connection procedure now passes through real XT controls,
account enablement, fingerprint approval, real model replies, retained browser
approval and independent RC stop/start. Direct tray/setup links, restored shortcut
windows, account readiness labels and client recovery fixes are installed as a
local RC/XT validation overlay with updated external main templates. This is not
a new MSI release. See [the complete report](verification/2026-11-14-rc-connection-walkthrough.md)
for 11 lifecycle, 267 core, 30 headed recovery and 15 final installed checks, their
fixture boundaries, failures and rollback records. English and Spanish narrated
tutorials and tested chapter players are linked there. Same-PC access remains the
supported boundary; physical-phone/LAN and remote transport are not certified.
Earlier sections retain their dated verification scope and remaining gates.

## Remote-Controller source implementation · 13 November 2026

The independent `Tlachihuani.RemoteController` service, RC core, live client assets,
native control pipe and XT controls are implemented in source. The packaging source
now includes its own virtual account, Manual startup and protected data directories.
Both main Tlamatini editions now implement account activation, their existing chat
engine, execution approvals and owned cancellation; XT implements the interactive
installed-application launch broker. The service permits only local-loopback
connections. Post-quantum remote transport, independently trusted delivery, speech
and final installed end-to-end acceptance remain open gates. Read
[the full boundary and operating contract](remote-controller.md) and
[the separate verification record](verification/2026-11-13-remote-controller.md).
Earlier package and Torrenter results below retain their original dates and scopes.

Recorded evidence: **212/212 RC core checks**, **210/210 main-application checks
in each edition**, and **24 broker/path checks passed with two symbolic-link
fixtures skipped** because Windows denied creation. Each main suite includes
16 real SQLite migration, deletion and durable-revocation checks. Source carriage,
syntax and edition parity passed. Real HTTP/native-pipe verification passed **54/54**;
the headed RC client passed **119/119**, including uncertain outcomes, late completion
and no command replay. The final r5 package verified **1,466 runtime files
byte-for-byte**, and native XT layout passed **11/11** at 144 DPI. Main-account
browser acceptance passed **129/129 checks** in both editions, with actual
foreground HWND/document verification, account revocation and ORM deletion.
The earlier real RC executable passed **14/14 isolated SCM checks** and its temporary
service was removed; that run does not certify the latest installed package.
Exact evidence paths and scope are in the linked verification record.

## Previous implementation evidence

Latest version: **[1.0.1](releases/v1.0.1.md)**. The 0.3.3 documentation was
updated 11 November 2026; its source evidence is the
[10 November Torrenter recovery record](verification/2026-11-10-torrenter-recovery.md):
64 automated checks, 48 headed-browser checks across both controller editions,
four real local-peer transfers and 432 independently matched existing files.

The implementation includes stalled-slot scheduling, disk-complete verification,
accurate completed/stopped/error reporting, state-aware recovery controls and the
sortable per-account workspace. Windows ZIP setup, optional XT sign-in startup,
Manual services and Control Panel removal are carried forward. Their earlier
package tests below retain the versions actually tested. No new package or
installed-service acceptance is implied by this documentation bump.

The active checkout is `C:\Development\XAIHT\repo\Tlachihuani`. Its product
metadata declares 1.0.1. The [11 November documentation check](verification/2026-11-11-documentation.md)
records the path correction and documentation-only validation separately from
the historical runtime tests below.

Remaining release qualification: fresh package validation, clean installation,
both startup choices, real reboot, ordinary-user XT, lifecycle and file delivery,
Control Panel removal, signing, rollback and long-running transfer checks. The
separate **Panels → Tlachihuani** lifecycle panel remains a design target.

## Release command and optional tray startup — 2026-11-04

`Build-Release.cmd` now generates `dist/Tlachihuani-<version>-windows-x64.zip`
and its SHA-256 from the latest numeric stable local Git tag and the current
checkout. Each run freshly stages all four self-contained components, setup,
the offline guide and the six-file public bundle. The builder neither commits
nor uploads. See [release building](release-building.md).

Setup asks whether XT should appear at Windows sign-in (a common shortcut for
all Windows users), always installs Start/Desktop shortcuts, and leaves both
services stopped/Manual. The normal unelevated wizard launches XT once after
success. Login never starts services; the operator uses Start Services in XT.

Two visible release-command runs completed at 0.2.1, each passing four release
version tests, eleven lifecycle checks, a byte-for-byte comparison of 1,100
runtime files plus maintenance files in three MSI cabinets, and ZIP CRC,
allowlist, extraction, size and hash checks. That tested ZIP was 103,415,658 bytes;
SHA-256: `013f1ce6d3d60c92a331d63ff39684fe263cdbb0bdc340f23295fec9747240af`.
Run evidence: `artifacts/release-builds/20261104-195653-49a20ec3/build.log`.

The actual packaged installer was visually checked with the startup choice
checked and unchecked; Review displayed Yes/No correctly, version 0.2.1,
Start/Desktop shortcuts and stopped/Manual services without observed clipping.
It correctly refused to overwrite the existing workstation installation.

`tests/integration/Test-SetupStartup.ps1` then performed real MSI install/remove
cycles for startup=0 and startup=1 using separately named inert QA services.
Both cycles confirmed stopped/Manual virtual-account services, correct
Start/Desktop shortcuts, login shortcut absence/presence, persisted preference,
Windows uninstall registration, complete shortcut/service removal and retained
identity across reinstall. All four MSI transactions returned 0. Evidence:
`artifacts/verification/startup-e41091ef50b44a18b05d3cbee2302c69/`.
The final whitespace check detected CRLFs in the branding notes, subsequently
normalized; that documentation finding did not invalidate the MSI results.

These tests do not replace production installation, automatic XT launch,
cross-account login and reboot acceptance on a clean Windows machine. The
shipping package remains unsigned. Existing production services/downloads were
not modified by the QA test.

## Offline ZIP and Windows registration — 2026-11-04

The 0.2.1 packaging update creates a self-contained Windows x64 ZIP with both wizards, an offline styled guide, quick-start instructions, a file manifest and SHA-256 checksums. Separate .NET 10 installation is no longer required. See [the packaging notes](releases/v0.2.1.md) and [setup guide](../packaging/setup/README.md).

Visible workstation verification passed all 11 lifecycle checks, ZIP extraction/hash checks and a byte-for-byte comparison of 1,104 published runtime files plus all maintenance files across three MSI cabinets. Both shipping wizards showed 0.2.1 and passed Review/Cancel. A separately named QA package completed real MSI installation, native service start, registered Windows uninstall with services running, reinstall retaining its identity and final wizard removal. Registration checks cover version, publisher, size, install directory, the Windows Installer ProductIcon, app discovery values and Start/login/uninstall shortcuts. Programs and Features was also visually inspected: the QA entry appeared beside Tlamatini with XT and version 0.2.1. Explorer's virtualized item flags were not used to claim visual success.

These SCM acceptance tests use inert QA service binaries with separate identities and paths. They do not certify the production Jobs workload or upgrade the existing 0.1.0 workstation installation. The package is unsigned. Reboot, cross-account and long-running transfer acceptance remain separate gates.

## Windows setup — 2026-11-03

The versioned setup output under `artifacts/setup/<version>/windows-x64/` contains the graphical installer and matching uninstaller. Workstation UI automation has exercised both version labels, Review and Cancel. Visible isolated acceptance installed the QA package, started both SCM services, removed it, reinstalled it with the same protected Jobs identity and removed it again. The test uses inert service binaries and separate product and service IDs. Full production-payload acceptance requires a clean machine or Windows Sandbox. Build, maintenance and test commands are in the [Windows setup guide](../packaging/setup/README.md).

On 3 November the operator also completed installation of the production 0.1.0 package on the workstation after preserving the legacy folders. Launching the tray executable with `--show` opened the panel. **Start Services** changed XT from gray to green, the Jobs health endpoint returned `engine: ready`, and the workspace displayed the signed-in Tlamatini account. This was a manual workstation check. See [v0.2.0](releases/v0.2.0.md) for the release scope.

## Jobs and Torrenter — 2026-10-31

On ANGYSLAP, Jobs was installed and upgraded under its virtual service account. The delegated operator verified the named pipe and restarted Jobs from standard Windows PowerShell, while the original Tlachihuani service stayed stopped. The installed XT panel approved an actual headed Chrome session on port 12064. A private swarm exercised transfer, publication, recovery, isolation and deletion. See [the operating guide and verification scope](torrenter.md).

The installed XT menu was exercised on ANGYSLAP with both services set to Manual. **Start Services** brought up both hosts and the REST endpoint. Tlamatini's enrolled Torrenter agent listed the completed download. The original icon appeared in color when the API was ready and turned gray when the provider was disabled. **Start Services** also recovered that partially running configuration.

After **Stop Services**, both SCM PIDs were zero, the captured service and child processes had exited, port 12064 had no listener, and the same XT process displayed the gray icon. An isolated Jobs host also shut down with its torrent worker deliberately suspended. A local-pipe regression check disconnects a client mid-request and verifies that the primary host keeps serving other clients.

The following entries are the historical October 11 foundation record.

## Lifecycle foundation — 2026-10-11

The following foundation inventory and limitations describe the 11 October
baseline. Later Jobs and installer evidence appears above; do not read this
historical list as the current 1.0.1 feature inventory.

This page distinguishes working code from the broader design. No SCM service, registry startup entry, local Git repository or GitHub repository was created. Tlamatini remains a separate, read-only checkout at `C:\Development\XAIHT\Tlamatini`, https://github.com/XAIHT/Tlamatini.

### Implemented at that baseline

| Component | Current behavior |
|---|---|
| Contracts | Snake-case JSON; four-byte little-endian length; 64 KiB maximum; depth 16; version, operation, request ID, cursor and limit validation |
| Core | Independent health/SCM states; heartbeat every two seconds; boot instance ID; last 256 events; idempotent Start/Stop and ordered Restart with deadlines |
| Windows | Fixed-name own-process SCM adapter; quoted image-path validation; live pipe PID/image/held-process identity; restricted global mutation mutex |
| Service | Microsoft Windows-service lifetime; 16 reusable local-only pipe instances; three-second exchanges; ready event; bounded cancellation; console mode must be explicit |
| Client/Control | Synchronous status/events/start/stop/restart; one JSON envelope; documented exit codes; missing service reported honestly; no implicit install/elevation |
| Tray | Original XT icon; one companion per Windows session; slate/teal status window; nonblocking polling and lifecycle actions; independent Close/Exit; configurable loopback Tlamatini URL |
| Packaging | Framework-dependent publish; explicit first-install, registration removal and per-user logon scripts; scripts require verified foreground development console |

The pipe's DACL grants the selected operator data read/write rights without permission to create another pipe instance. SYSTEM, administrators and the service identity can administer the endpoint. The kernel rejects remote clients. Client SQOS is identification-only. Pipe observations verify the live server process before accepting data; installed mode additionally checks SCM. The separate development endpoint is never reported as an installed/running SCM service.

Protocol clarification from live testing: after reading a complete framed reply, a client sends byte `0x06`. The server waits within the same three-second deadline before disconnecting. Without this acknowledgement, Windows can discard unread reply bytes when an instance is reused. No second request is dispatched on that connection. Semantically invalid requests receive a correlated error; malformed/oversized frames are disconnected. The CLI forwards `next_cursor` and `cursor_expired` for events.

Start/Stop/Restart operate entirely through SCM and do not wait for pipe health. Their 30-second deadline returns failure/timeout rather than guessing success or killing a process. Mutations serialize across processes through a Windows mutex restricted to the configured operator, administrators and SYSTEM. Synchronous results use `request_id`; persistent `operation_id` receipts and automatic retries are not part of this foundation. Do not retry Restart blindly after a controller disconnect.

### Foundation verification

See [the dated verification record](verification/2026-10-11-foundation.md) for exact checks, results, initial defects and limitations. Tests execute visibly, including the temporary real service-host process. That process shuts itself down after 12 seconds and is never registered with SCM. The tray's native status window was inspected on the real desktop; shell notification-area positioning remains Windows-controlled.

NuGet is the sole package source. `Microsoft.Extensions.Hosting.WindowsServices` is pinned to `10.0.12`, SDK to `10.0.401` (latest patch roll-forward), and transitive dependencies are locked. NuGet audit is enabled. No Snyk scan, hook, skill or package is used. Microsoft references: [Worker services on Windows](https://learn.microsoft.com/en-us/dotnet/core/extensions/windows-service), [named-pipe ACLs](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights), [package metadata](https://www.nuget.org/packages/Microsoft.Extensions.Hosting.WindowsServices/10.0.12).

### Limitations recorded on 11 October

- Actual installed-service acceptance, cross-account/remote-pipe tests, reboot/sign-out tests and Explorer restart verification. Current token is unelevated; native installation was not performed.
- Force termination, UAC/host consent, elevated helper, durable receipts and restart request deduplication. `force_stop` is explicitly false.
- Signed release installer, update/repair/rollback and automatic file cleanup on uninstall. Scripts are a reviewable first-install foundation, not a certified release installer.
- Persistent service-specific log files and the proposed local-log menu. Installed hosting diagnostics use the Windows Application log (`.NET Runtime` source); the activity ring is ephemeral.
- The proposed tray consent IPC and full Tlamatini panel. Double-click currently opens the native status window; the optional menu link opens an existing Tlamatini URL, without assuming its port or adding a panel route.
- Functional business workload. The service only demonstrates lifecycle, readiness and heartbeat.

These limitations must remain visible in future handoffs; source/console tests do not certify a Windows installation.
