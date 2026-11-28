# Privileges, installation and ownership

Implementation update: [current status](implementation-status.md) and [packaging scripts](../packaging/windows/README.md) describe the first foundation. The broader requirements below are not all verified yet.

Design only. This preparation performs no registry, startup, SCM, ACL, firewall, security-setting or installed-application changes.

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

## Identities

Run the service with a dedicated least-privilege virtual service account, proposed `NT SERVICE\Tlachihuani`, and an own-process SCM registration. Grant only required access to its own data/log directories. No LocalSystem or administrator token is justified by the initial heartbeat/status workload. Reassess if Angela defines a workload that needs additional resources.

Implemented services each run under their own virtual account, registered Manual: `NT SERVICE\Tlachihuani`, `NT SERVICE\Tlachihuani.Jobs` and `NT SERVICE\Tlachihuani.RemoteController`. Remote-Controller listens only on loopback (`127.0.0.1:12481` by default), uses P-256 device proofs and short-lived in-memory sessions, and keeps its installation manifest protected from service and operator writes. See [Remote-Controller storage and native control](remote-controller.md#storage-and-native-control).

The interactive tray runs unelevated as the signed-in user. So does HandGI (`Tlachihuani.HandGI.exe`), one per Windows session, because the camera, the sound volume and its keyboard shortcut belong to that user; it is not a service and never runs in Session 0. Proposed local group **Tlachihuani Operators** receives query/start/stop rights on this service, with status-only access for explicitly selected viewers. The installer must distinguish the initiating user from an elevation account and not silently grant all authenticated users lifecycle privileges. Do not alter Tlamatini's existing rights.

Named-pipe DACLs explicitly allow the service identity, administrators and configured operators/viewers with operation-specific enforcement. Reject network clients; the default pipe DACL is insufficient. Use precise rights that do not accidentally grant pipe-instance creation to clients. See [Microsoft's named-pipe security reference](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights).

## Force termination

**Implemented in the current source: XT's Stop Services.** Each Windows service grants
the configured operator the right to end its own process when it starts
(`OperatorProcessAccess.AllowOperatorStop`: query, terminate and wait only; no right to
read or change the process). **Stop Services** asks every service to stop; one still
running three seconds later is ended through the process the Service Control Manager
names for it, after its executable is checked against the installation, never by image
name or a remembered PID. A service started from a binary older than this change has
not granted that right, so XT waits for it and names it; installing Tlachihuani again
allows it. Per-user processes (HandGI and Flow Pills) are ended through the record each
one published, whose PID, creation time and executable must all still match
([ForcedStop.cs](../src/Tlachihuani.Windows/ForcedStop.cs),
[UserProcess.cs](../src/Tlachihuani.Windows/UserProcess.cs)). HandGI's control pipe is
created for that user and session only, and XT accepts a reply only when the pipe's
server is the process HandGI's record names. The design below remains the plan for
force-ending anything else.

Normal controls remain unelevated when the operator has SCM rights. Force termination uses a short-lived, narrowly scoped administrator helper only after explicit host-side consent. The Tlamatini panel initiates the request and reports its outcome; a compatible host tray handles local consent/UAC. With no eligible interactive tray/session, return `host_consent_required` with guidance instead of attempting invisible elevation. Remote browsers cannot approve a Windows elevation dialog on somebody else's desktop.

The helper accepts only a request ID/nonce through an ACL-protected, short-lived result channel. It independently resolves the fixed SCM service, verifies own-process type, installation-owned binary path, PID creation time, and expected instance captured for the confirmation. Open the process handle, recheck identity before termination, and wait for that exact handle to signal exit. PID reuse or identity mismatch cancels the operation. Windows SCM state must subsequently be STOPPED. No process-name sweep; no termination of unrelated agents, browsers, consoles, or Explorer.

The first release disables SCM failure restart actions, so Force kill stays stopped for the current boot. Do not temporarily rewrite recovery policy in an ad hoc helper. If graceful shutdown already completed, return a truthful already-stopped result. If it restarted into a new instance, require fresh observation/confirmation.

## Installation and upgrade design

**Current package:** HandGI ships in the `HandGI` folder beside the other parts, with its three hand models. It is not a service, so setup creates no service or account for it. Installing or repairing stops with a message while an installed HandGI is running: choose XT → **Stop Services**, then **Exit tray** ([setup](../packaging/setup/README.md)).

The future installer requires explicit administration for the machine service and Program Files. It validates destination ownership and reparse points, writes its own binary/installation manifest, creates data-directory ACLs and Event Log source, configures the service and failure policy, and optionally enables the tray at logon for selected users. No installation runs from this scaffold.

Installation metadata lives in the new project's own registry key and signed/hashed manifest; it must agree with SCM. The trusted controller path must not be replaceable by a normal user. Registry values alone do not authorize execution.

Upgrade: authenticate the delivered package, acquire the installation operation lock, record owned configuration/state, stop gracefully, replace binaries transactionally, verify version/protocol compatibility, and restart only if the recorded desired state requires it. A failure restores the prior usable binaries and reports the real outcome. Test power-loss/locked-file boundaries before promising rollback. Signing certificate/package distribution choices are future release decisions, not established credentials.

Uninstall: request confirmation in the future uninstaller, stop only this service and its owned tray and HandGI instances, remove only its own SCM/startup/discovery registrations and binaries, and separately ask whether to retain its logs/configuration. Never touch Tlamatini, its database, `agents/`, `Templates/`, file associations, or Windows UserChoice keys.

## Storage and diagnostics

Proposed log retention: 5 files × 10 MiB, plus bounded Windows Event Log startup/fatal/lifecycle entries. Limit event payload lengths and escape text before browser rendering. Keep installation paths and detailed errors behind staff/operator views. Never store Tlamatini passwords, browser cookies, API keys, chat messages, or full inherited environment blocks.

XT writes `Tlachihuani.log` (256 MB per file, up to 40 files kept) in `%LOCALAPPDATA%\XAIHT\Tlachihuani` for each Windows user; Services → See log shows it. Since 0.38.0 each service also writes a trace log under `%ProgramData%\XAIHT\<service>\logs`, which setup makes writable by the service and readable by the operator; XT copies those lines into Tlachihuani.log, and keys, tokens and passwords are masked. Since 0.37.0 XT can be connected to xaiht.org: it makes outgoing HTTPS requests only, with no listening port or firewall rule, and keeps one computer key in `xaiht-link.json`, protected with Windows DPAPI for that user. xaiht.org stores only the key's SHA-256. Diagnostic messages are logged and never executed. The separate typed prompt channel additionally requires explicit local account approval; its durable grants and receipts are in the RC service's ProgramData state. Prompts, answers and RC native frame contents are not written to XT's log. The website is a trusted HTTPS intermediary, not an end-to-end or PQ transport. See the [xaiht.org link](xaiht-link.md).

HandGI keeps its files in `%LOCALAPPDATA%\XAIHT\Tlachihuani\HandGI` for each Windows user: `service.json` (its running process, for XT), `settings.json` (camera and start choices) and `profiles\` (the signs the user taught). Its log goes to that user's `Tlachihuani.log` as component `handgi`. It works only on this computer and never saves or sends pictures of the user ([HandGI](handgi.md)).

No network control endpoint, firewall opening, remote shell, automatic updater, credential store, or scheduled worker is part of the initial design. Add a capability only after its actual workload and permission needs are specified.
