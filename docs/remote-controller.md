# Tlachihuani-RC Server · 1.0.0

Latest version: **[1.0.0](releases/v1.0.0.md)**, the first official release.

Created for Angela López Mendoza · XAIHT.

> **xaiht.org prompt channel.** XT receives typed commands through its existing
> outgoing HTTPS link and passes only explicitly approved account operations to
> this service. It returns the real adapter's progress, approvals and answers.
> Diagnostic messages remain inert and log-only. See the [setup and trust
> boundary](xaiht-link.md#allow-an-account-and-send-a-real-prompt).

Remote-Controller is a separate Windows service plane. Its fixed SCM name is
`Tlachihuani.RemoteController`, display name is **Remote-Controller**, and executable
is `RCServer/Tlachihuani.RC.Server.exe`. The service owns the live Tlamatini-RC web
client, HTTP/WebSocket endpoints, enrollment, device grants, sessions, durable
operation receipts, events and schedules in one host process. Node.js is not a
product prerequisite. The existing Node preview remains a design demonstration.

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

## Current implementation boundary

The implemented connection profile is **local-only**, on `127.0.0.1:12481` by default.
This profile uses P-256 device proofs and short-lived in-memory bearer sessions.
It does **not** provide the approved future ML-KEM-1024 / ML-DSA-87 remote transport.
Selecting `post-quantum-required` fails closed; it never silently chooses classical
remote TLS. LAN, VPN and Internet exposure remain disabled until the reviewed
transport and independently trusted client distribution are available.

Both main Tlamatini editions now contain source integration for account activation,
the existing chat engine, execution approvals and cancellation. XT contains the
interactive-session application-launch broker. Account identity uses immutable
UUIDs and the edition, rather than a display name or a copied browser session.
The service/client package is built and verified. Clean installation and installed
end-to-end acceptance remain separate gates.
The HTTP/browser harnesses use explicitly labelled QA accounts and adapter results;
their success alone does not certify a production model conversation or app launch.

The direct remote/PQ transport, independently trusted client distribution and speech
capture/transcription remain unavailable. The additional website relay trusts
xaiht.org with plaintext and is ordinary HTTPS, not the approved future PQ/E2EE
design. See the [dated verification](verification/2026-11-13-remote-controller.md)
for tested behavior and the remaining gates.

## Start and stop

The MSI source installs Remote-Controller as an independent own-process service,
under `NT SERVICE\Tlachihuani.RemoteController`, **Manual and stopped**. Installing
the package or opening XT does not start it.

Open **XT → Show panel → Services → Remote-Controller…**. The Runtime tab shows
Windows service state separately from application readiness, and supplies Start,
Stop, Restart and Open RC client. `Tlachihuani.Tray.exe --remote-controller` opens
the same panel directly. Closing the panel or exiting XT leaves services alone.

**Start Services** includes RC only when its Network setting explicitly opts in.
**Stop Services** stops RC first, then continues stopping the original and Jobs
services even if an earlier stop reports an error. Stopping RC alone disconnects
its clients and suspends its scheduler. It neither stops Tlamatini nor controls
Torrenter. The remote client cannot start its own stopped server.

The local lifecycle controller verifies the fixed service executable and account,
serializes lifecycle changes with a protected mutex, and observes SCM state and
the original process handle on stop. It never kills a browser-supplied PID or
accepts a browser-supplied command, service name or executable.

## Local enrollment and client use

On the PC running Tlamatini, sign in to the desired English or Spanish account.
Open **Panels → Tlachihuani · Remote-Controller**, confirm the current account
password, and select **Enable this account**. This page makes the account available
to XT; it neither approves a device nor starts the service. Its requests require
local access, the authenticated account, same-origin checks and CSRF protection.

Select that account in XT's Remote-Controller **Access** tab, choose its permissions
and create a five-minute invitation.
Open the service's client address, enter the invitation once, and compare the
complete device fingerprint in both places before approving in XT. Each invitation
is single-use. Reload and reconnect use the approved device signing key, with no
new invitation. Revocation ends the device's sessions and pending requests.

**Disable remote access** in Tlamatini first saves local denial, then revokes the
service binding, device grants and outstanding invitations. If the service is
unreachable, the page reports pending service revocation and the adapter retries.
Re-enabling preserves the immutable account identity but requires fresh device
approval. English and Spanish accounts stay separate even when names match.
Deleting the local account or its RC profile records durable revocation intent
outside that deleted profile. Revocation therefore survives an unavailable service
and an application restart; the adapter retries until RC acknowledges it.

The live client is `Tlamatini-RC/public/remote.html`; its matching assets are included
under the server's `wwwroot`. It has Conversation, Monitoring, Automations,
Connection & trust, Activity and Appearance views. Session tokens stay in memory.
IndexedDB retains the non-exportable browser signing key. Only the explicit public
shell allowlist can enter the service worker cache; API responses and conversation
content never do. Loss of the RC session disables commands and does not imply SCM stopped.

A fresh client opens **Connection & trust**. A disconnected Conversation page
offers a link to connect or set up the browser. A revoked or unknown saved device
can use **Set up this browser again** after confirmation; this forgets only this
origin's local signing key and enrollment record. It neither changes server grants
nor approves a replacement device. A new invitation and local fingerprint approval
are still required. Normal disconnect, reload and session expiry retain the key.

The browser's Internet-offline notification does not end a working local loopback
session. An interrupted RC event channel or network failure during an authenticated
request ends the local session and offers restart/reconnect guidance while retaining
the approved key.

Protocol v1 advertises the additive `account-capabilities` feature. Its authenticated
`GET /api/rc/v1/capabilities` endpoint accepts `chat` or `workspace` permission and
returns only the current account's scoped execution/workspace availability booleans.
It supplies no service state, model name, local port or other account observation.
`/snapshot` still requires `monitor`. Older clients ignore the new feature; an
updated client can still use monitoring with an older server, but Conversation
without monitoring requires the updated server and displays upgrade guidance.

## Main application chat and launch

Remote conversation requests enter Tlamatini's existing `AgentConsumer` and use
the selected account's history, configuration and normal answer persistence.
One account cannot run overlapping desktop and remote model requests. Remote chat
forwards the six explicitly selected execution modes. The adapter forwards only the frames emitted by its owned consumer.

**Load recent conversation** uses the typed `conversation.history` operation to
read only the selected account's saved messages. It returns at most 40 rows within
a 24,000-byte escaped-JSON receipt budget. Large saved assistant responses use
account-bound content tickets to load their complete HTML separately; omitted
older rows remain explicitly identified. It does not merge
another account's history or trigger a model request.

Execution requests show **Proceed** or **Deny** for the exact pending prompt.
Later status updates do not hide that prompt. Consent is tied to its conversation
and request identifier, consumed once, and checked again before dispatch. Oversized
approval details are denied rather than approved from incomplete information.
Cancellation uses the existing owned run epoch and permission broker; a cancellation
request is distinct from confirmation that the running work has ended. Loss of
the approval channel or authorization requests cancellation and closes consent.
Native transport and account polling allow at most four concurrent calls. An
active operation's local authorization watchdog requests cancellation after
20 seconds without renewed authority, even if a native call has stalled.

An authorized **Start Tlamatini** request is handled by the normal XT tray in the
delegated Windows account's existing unelevated session. A locked desktop or
disconnected session does not disable the product's remote-control function.
It discovers only the selected edition's installed executable from that account's Installed Apps
registration: `Tlamatini.exe` or `Tlamatini-Spanish.exe`. There is no source-checkout,
PATH, shell-command or browser-supplied executable fallback. Existing processes
are checked by real process handle, executable identity, account and session.

Launch and readiness remain separate: the receipt reports **starting** if the
process exists but its account adapter is not yet ready. A heartbeat is required
to report readiness. XT never unlocks Windows, launches an interactive application
from service session 0, or kills Tlamatini when RC or XT exits. The application
retains its normal main console; RC helpers add no console. Launch scratch stays
in application storage outside Windows user profiles.

## Durable operations and schedules

The closed operation set is `application.status`, `application.start`,
`conversation.history`, `conversation.submit`, `conversation.cancel`,
`conversation.permission` and `audio.transcribe`; capability availability
additionally depends on the connected application adapter.
Authorization binds installation, edition, immutable account, device and scope.
An accepted receipt is not completion. Repeating the same idempotency key and
payload returns the same receipt; changing its meaning returns a conflict.

Dispatch is durably marked before the adapter receives work. A host restart marks
previously running work `outcome_unknown`; it is never automatically replayed.
Lost application heartbeat for 20 seconds, or broker heartbeat for 45 seconds,
also moves running work to `outcome_unknown`. This records uncertainty rather than
asserting that an external action stopped. The owning adapter may later reconcile
the receipt; the client keeps observing uncertain receipts so it can display that
late result. It is never automatically redispatched. Account disable also marks
already dispatched work uncertain and clears pending consent. Device revocation
clears its prompts; admission and dispatch revalidate the originating chat device,
even when a second approved device answers the prompt. Expired pending work is refused
at dispatch. Application and broker dispatch have separate operation roles;
conversation control messages can be serviced while a model answer is running.

Schedules belong to an account and the creating device's current authorization.
They store timezone, wall-clock time, weekdays, revision and next occurrence.
Nonexistent DST times are skipped; repeated times run once at the later UTC
instant. Missed runs either skip or catch up once within a bounded grace period.
Stopped RC dispatches nothing. Revocation preserves the schedule record while
preventing further execution under the revoked authority. Edits require a matching
revision, avoiding silent overwrites between clients.

## Storage and native control

Installation identity is under
`%ProgramData%\XAIHT\Tlachihuani.RemoteController\installation.json`.
The installer protects this manifest from service/operator writes. The service may
modify its `state` and `settings` subdirectories; the operator can read settings
and control changes through the authenticated pipe. No scratch directory is
created under a Windows user profile. Reparse paths are refused.

State uses an exclusive writer lease and flushed copy-on-write snapshots published
with a same-directory rename. Failed commits leave the live snapshot unchanged.
Messages, sessions, queues, history and persisted state have explicit limits.

Native control uses an installation-specific local pipe with Windows ACLs,
identification-only client authentication and verified server process identity.
`Tlachihuani.RC.Server.exe --control` reads one bounded JSON request from standard
input and writes its response. This interface belongs to trusted native adapters
and XT, not to a remotely supplied shell command. Browser routes cannot enroll an
account, approve a device, change listener settings, or control SCM.

The native operator's `accounts` response retains all enabled bindings, including
offline ones, and adds `application_state` (`ready`, `loading` or `offline`) from
the current adapter heartbeat. Display names do not replace immutable identifiers;
offline bindings are neither silently deleted nor reassigned to a matching name.

## Development and evidence

Follow [visible execution](development-execution.md). Use the application-owned
`artifacts` tree for scratch, browser profiles and evidence. The explicit console
host is `--console <isolated data directory> <guid-N> <port>`; it does not certify
an installed service. `tests/integration/rc_scm.ps1` exercises the real executable
under a fixed temporary SCM acceptance registration and preserves the independent
production service registrations.

Current installed connection evidence and English/Spanish tutorials are in
[the 14 November walkthrough](verification/2026-11-14-rc-connection-walkthrough.md).
The earlier [implementation verification record](verification/2026-11-13-remote-controller.md)
retains its original scope.
No package publication, signing, commit or push is implied by source checks.

## Change an approved browser’s permissions

In XT → Remote-Controller → Connections, select the browser and choose **Change
permissions…**. The account and edition are shown alongside its name. Enable
**Start Tlamatini** if that browser should start a stopped main application; **Stop
Tlamatini** remains a separate choice. Save, then reconnect the browser using its
existing approval. No new invitation is required. Reinstallation does not silently
expand existing grants.

Only the delegated local Windows operator can submit the typed set-device-scopes
operation. It requires the current device revision, preserves the approved key and
account, and ends that device’s previous sessions and tickets. Revoked devices and
disabled accounts cannot regain access through a permission edit. See the
[development regression record](verification/2026-11-14-rc-start-permission.md).

## Application lifecycle buttons

Start is disabled while the current application observation is ready or loading.
Both Start and Stop remain disabled while a local lifecycle submission or its
accepted/running receipt is pending. A failed operation releases that pending lock;
permission and current application-state checks still apply. Offline means the
account adapter is not currently observed, not proof that its process stopped.
The trusted broker checks the installed process before acting. See the
[development button regression](verification/2026-11-14-rc-lifecycle-buttons.md).

## Rich replies and saving response files

Conversation and loaded history render Tlamatini’s prepared HTML, preserving
inline presentation, code, tables, diagrams and the separate execution report.
The RC renderer sanitizes active content and isolates response styles. User
messages remain literal text. It uses the same existing DOMPurify 3.4.16 dependency
carried by Tlamatini, with its license included.

**Save file** / **Guardar archivo** replaces response **Load in canvas** actions
in RC, including the full mobile workspace. The browser downloads the original
bytes to its own device. Size and SHA-256 are verified before the download starts;
HTML, SVG and scripts are downloaded as files. A failed download offers a retry
and does not report success. Files are limited to 64 MiB.

Both main editions persist trusted, message-owned attachment records in
AgentMessage.response_files through additive migration
0225_remote_response_files. Only artifacts actually created by the response
parser or returned by supported successful tools are eligible. Model-authored
links, guessed names and arbitrary paths cannot grant file access. Old messages
without these records ask the user to regenerate the attachment. Downloads recheck
account consent, file identity and original bytes; changed files require a fresh
response. Tickets expire after 30 minutes and after application restart; loading
recent conversation refreshes eligible links.

The fixed response-file and response-content endpoints require the approved RC
chat scope. Full-workspace saving additionally retains the existing workspace
session/account checks. Tokens, response bodies and file bytes are not cached in
the service worker. Formatted responses exceeding the native receipt budget load
through a bounded 8 MiB content endpoint, with at most three client loads at once.

Build both main editions and then Tlachihuani to deliver these matching changes.
The canonical Tlamatini-RC/public assets and the packaged wwwroot assets must stay
identical. This is development work; installed acceptance is a separate gate.
See [the rendering and file verification record](verification/2026-11-14-rc-rich-responses.md).
