<!-- Tlachihuani — Created for Angela López Mendoza · XAIHT · @angelahack1 -->
# Tlachihuani implementation contract

This is the onboarding document for Claude, Codex, and every coding agent working on Tlachihuani.

Latest version: **[0.38.0](docs/releases/v0.38.0.md)**, as requested by Angela on
21 November 2026. Read the [Windows setup guide](packaging/setup/README.md)
for packaging and migration, and the [10 November recovery record](docs/verification/2026-11-10-torrenter-recovery.md)
for source verification. This checkout declares `0.38.0` in `Directory.Build.props` and internal
project lock references. Preserve those matching values. Future stamps must update
both together; documentation changes do not rebuild existing binaries or create Git tags.

`Build-Release.cmd` is the root release entry point. It chooses the highest stable local Git tag numerically, stamps product/internal project versions, rebuilds into fresh staging and verifies before copying the install ZIP to `dist`. It never commits, tags or pushes. See [release building](docs/release-building.md). Setup asks whether XT should appear at sign-in, always installs Start/Desktop shortcuts, and leaves all services stopped/Manual. Startup shortcuts must never start services. Preserve the distinction between the original unelevated wizard and its privileged MSI helper.

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
[current integration guide](docs/xaiht-link.md) and [dated evidence](docs/verification/2026-11-23-hosted-prompts.md) before making
installation or release-acceptance claims. This pass changes documentation only;
existing version identifiers and installed services remain unchanged.

## Project identity and controller

**Project root:** `C:\Development\XAIHT\repo\Tlachihuani`.
Its GitHub repository is **https://github.com/angelahack1/Tlachihuani**; the verified Git origin is **https://github.com/angelahack1/Tlachihuani.git**.

**Tlamatini reference checkout:** `C:\Development\XAIHT\Tlamatini`.
Its Django source root is `C:\Development\XAIHT\Tlamatini\Tlamatini`.
Its GitHub repository is **https://github.com/XAIHT/Tlamatini**; the verified Git origin is **https://github.com/XAIHT/Tlamatini.git**.

Tlachihuani is a separate Windows application/service. Tlamatini's implemented **Panels → Tlachihuani** integration provides typed lifecycle controls; the authorized Remote-Controller adapter also exposes account-bound operations. The original design deferred cross-repository edits, but the current integration is implemented. Host consent and capability checks still govern privileged actions. See the integration and verification guides for current capabilities; do not restore the former planned-only description.

The 2026-10-11 review used Tlamatini HEAD `c18d50815cfe8b1681f805fd4e76c66fbaeabaf6`, not just the older v1.78.0 tag. Recheck source before integration because this is a dated design reference.

## Read order

1. [AGENTS.md](AGENTS.md), [NEXT_SESSION.md](NEXT_SESSION.md), [architecture](docs/architecture.md).
2. [Lifecycle](docs/lifecycle.md), [control protocol](docs/control-protocol.md), [security and installation](docs/security-and-installation.md).
3. [Panel/tray specification](docs/panel-and-tray.md) and [Tlamatini integration](docs/tlamatini-integration.md).
4. [Implementation plan](docs/implementation-plan.md), [verification plan](docs/verification-plan.md), [source review](docs/source-review.md).
5. Before editing Tlamatini in a later authorized session, read its `AGENTS.md`, `CLAUDE.md`, `TestsVisiblesAndVisibleExecutionFromClaude2Codex.md`, and `docs/claude/INDEX.md` at the full checkout path above. Relevant skill paths and applicability are indexed in [skill-inventory.md](docs/skill-inventory.md).

## Torrenter and the Jobs host

The separate [Remote-Controller plane](docs/remote-controller.md) was authorized on
13 November 2026. Its source, XT controls and live Tlamatini-RC client are implemented;
read the linked implementation boundary and dated verification before claiming
remote or installed acceptance. It owns no Torrenter processes or downloads.

Read [the operating and REST contract](docs/torrenter.md). The independent `Tlachihuani.Jobs` SCM service owns the provider registry and child-process lifetime. A protected worker journal owns transfer state, permissions and durable receipts. XT manages providers on the Jobs v2 pipe. The web facade at `127.0.0.1:12064` exposes the account-scoped transfer API. Browsers sign in through Tlamatini and use its session and CSRF protection; ordinary workspace access asks for no pairing code. Native Windows enrollment supplies scoped automation credentials.

Keep the canonical workspace assets synchronized with both Tlamatini editions
when their changes are authorized. Preserve recovery semantics: completion is
separate from stopped peer activity; current errors are not historical errors;
reported seeds are not connected peers. The scheduler yields a stalled slot only
to blocked eligible work (180-second stall, minimum 60-second retry). Publication
waits for the disk-complete pause alert. Source and installed acceptance are
separate evidence; see the linked verification record.

## Flow Pills · 25 November 2026

**XT → Flow Pills…** (`Tlachihuani.Tray.exe --flow-pills [files]`) is a separate per-user
window, not a Session 0 service. It opens, draws and runs Tlamatini's `.flw` flows and
`.fpmt` flow pills, up to 256 at once by default, each on its own thread and in its own
kill-on-close Windows job, with every log tailed independently. Opening never runs a flow.
Runs use only the agent templates, Python and `config.json` of the Tlamatini that publishes
itself in `HKCU\Software\XAIHT\Tlamatini`. End processes only through the run's job; wait
for `FlowRegistry.Idle` before closing. Keep the compiler and the pill validator in step with
Tlamatini. Read [Flow Pills](docs/flow-pills.md) for behavior, limits and dated evidence;
it is source-verified only, not installed.

## Original service design decisions

- SCM service name and display name: `Tlachihuani`; own-process Windows service.
- Resident binaries implemented: `Tlachihuani.Service.exe` (SCM/Session 0 or explicit development console) and `Tlachihuani.Tray.exe` (signed-in user's session).
- `Tlachihuani.Control.exe` implements synchronous JSON/SCM bridging. `Tlachihuani.Elevated.exe`, force-stop, durable operation receipts and host consent are still future work.
- C#/.NET 10 Worker Service + WinForms tray; solution `Tlachihuani.slnx`, SDK `10.0.401`, WindowsServices package `10.0.12`, lock files saved in each project. See [implementation status](docs/implementation-status.md).
- Reuse the existing XT icon copied from `C:\Development\XAIHT\Tlamatini\Tlamatini.ico`, with provenance in `assets/branding/`. Do not generate a replacement logo.
- Local-only named-pipe IPC, explicit Windows ACLs, bounded/versioned messages. Tlamatini exposes authenticated same-origin web endpoints; the browser never talks directly to the service.
- No dependency on Tlamatini's Django database, Python imports, RAG/model loop, agent pool, or process reaper.
- Install, update, uninstall, and application data are owned by Tlachihuani. Do not reuse Tlamatini's installer or database handling.
- Stop/kill are different actions. A killed process must remain stopped for the current boot, and success must be observed through SCM and process exit. First-release SCM crash-restart actions are off.
- The tray remains available to show a stopped or unavailable service. Exiting the tray does not stop the service. Closing the panel does neither.

## Development conduct

Follow [development-execution.md](docs/development-execution.md): verified visible foreground development console; explicitly headed browser automation; no invisible test or diagnostic; monitor live and retain exit codes. This is not a requirement for extra end-user service consoles.

Work within this project unless the user broadens scope. The existing GitHub repository is listed above. Do not create additional repositories or remotes, commit, publish, or push without instructions. Preserve authorship and never rewrite history.

Angela explicitly rejected Snyk on 2026-10-11. Do not use its skills, hooks or tools. Its global Codex server/hooks/skills were disabled at her request. NuGet audit remains enabled for this solution.

Tlamatini's protected SQLite backup/restore mechanics are out of scope. Its `build.py` erases its development database and dist tree; do not run it for this project. No source changes there, database migrations, installations, registry writes, or service registrations were performed for this scaffold.

## Completion standards

Use separate evidence for design review, unit checks, native SCM/service checks, tray UI, future Tlamatini browser integration, and installed release acceptance. A JSON fixture, mockup, or exit code alone cannot prove a running service or a visible tray icon.

Torrenter is the first Jobs provider. Add later providers through the Jobs registry, and keep torrent-specific operations in its adapter. Consult the dated verification records before claiming installed acceptance.
