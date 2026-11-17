# Tlachihuani handoff · 17 November 2026 · 0.36.0

Latest version: **[0.36.0](docs/releases/v0.36.0.md)**. `Directory.Build.props` and the
internal project lock references declare 0.36.0. Dated sections below keep the
versions they describe.

## Approval synchronization and latest predeployment scope · 15 November 2026

Angela found a real installed failure: approving RC-TEST-02 in RC completed the
command but left the main chat approval modal open until Ctrl+R. Development fixes
now synchronize the exact request across main English, main Spanish and RC, make
answers atomic, and prevent stale prompts/receipts from affecting newer approvals.
The canonical client and packaged mirrors use shell cache v13.

Final checks: 307 English, 309 Spanish, 294 RC core, 41 headed approval UI,
15 client, 89 hosted relay/browser, 25 real OIDC and 8 Linux-container checks.
All 18 packaged assets match canonical/source/built Windows/built cloud copies.
See [correction and complete evidence](docs/verification/2026-11-15-approval-sync.md)
and [the full predeployment checklist](docs/remote-control-predeployment-checklist.md).
Initial failures and corrected final runs are retained. No installed application,
release version or production deployment was changed; no commit/tag/push.

Angela will rebuild/reinstall both main editions and then Tlachihuani. Verify the
exact installed payloads against those builds before resuming with RC-TEST-02 in
both windows, then denial/cancellation and the remaining local/hosted checks.
The prior walkthrough only covered the local installed connection. The latest
cloud code still accepts only development-loopback and rejects public origins;
xaiht.org deployment and real external-phone acceptance remain explicit gates.

All owned test processes/containers are closed, test ports are free, compiler
servers are stopped. Other containers were running, so Docker Desktop was left
on. The approval console stays open and its queue ends with finish.ready; do not
restart it. Artifacts: rc-approval-sync-20261115 and hosted validation
20261115-195008-4d96aa8e.

## Hardening review and revised videos · 15 November 2026

Reviewed the new RC queue, connector, revocation and execution-mode changes.
Fixed two reproducible defects in development: exponential retry overflow after
1,024 failures in both main editions, and receipt wording that could conceal an
unknown execution outcome. Canonical/client mirror assets match; shell cache v12.
Final regression stages passed 1,013 checks; the initial OIDC foreground failure
and its successful 25/25 retry are both retained. No installed update or deployment.

Revised EN/ES videos are in `artifacts/rc-hardening-review/watch/Watch.html`,
with 12 chapters each, narration/captions, and 36 recorded assertions. They show
expanding execution modes, Multiturn dependencies, uncertain results and late
completion without resubmission. Prior videos remain historical. See
[findings, evidence and qualification limits](docs/verification/2026-11-15-hardening-review.md).
Passing this development campaign does not establish mission-critical readiness.
All owned test workers/containers are closed and test ports are free. The
hardening-review console is left open; its queue ended with `finish.ready`.
Do not restart old test queues. The installed applications remain untouched.

## Recorded hosted walkthrough · 15 November 2026

Actual English (3:32) and Spanish (3:50) development-lab videos are available in
`artifacts/rc-hosted-video/watch/Watch.html`, with narration, captions and twelve
chapters each. The share ZIP excludes test credentials and browser profiles.
Thirty combined OIDC/connector/browser/sample assertions passed. The main adapter
is deterministic test data; this does not certify the unfinished public product.
See [recording evidence](docs/verification/2026-11-15-hosted-video.md).

## Hosted RC development foundation · 15 November 2026

Implemented the hosted client in English/Spanish, cross-platform cloud
OIDC/directory/relay, and service-owned outbound connector for the explicit
HTTPS loopback development profile. **443 distinct checks passed**: 51 API/relay,
34 headed hosted-browser, 25 real Keycloak/OIDC, 8 Linux-container, 285 RC core
and 40 final local rich-response cases (20 per edition). See
[the complete record](docs/verification/2026-11-15-rc-hosted-development.md) for
source hashes, fixtures, the retained foreground-guard interruption and resumed
stages. Use [the runner guide](docs/hosted-development.md), also available
[in Spanish](docs/hosted-development.es.md).

This is a development foundation, not the completed public remote product.
The relay can read lab traffic. Public/PQ modes fail closed; reviewed endpoint
crypto, independent trusted-client delivery, consumer account provisioning,
production operations and external-phone/installed acceptance remain open.
No deployment, installation, main-account DB change, commit, tag or version
change occurred. Preserve pre-existing version/lock/MobileRoutes changes.
The canonical Tlamatini-RC source is still separate and must not be deleted.

All owned test processes/containers and compiler servers are cleaned up at the
end of this run; see the retained final-cleanup.json and visible transcript in
artifacts/rc-hosted-implementation. The existing console remains open and idle;
its queue ends with finish.ready. Do not restart an old queue automatically.

## Development rich responses and file downloads · 14 November, 17:38 Mexico City

Completed in Tlachihuani, canonical Tlamatini-RC assets and both main editions.
RC renders the same prepared HTML as main history; response canvas actions save
files locally with English/Spanish labels, original-byte verification and trusted
message-owned artifact records. Main migration 0225_remote_response_files and
module/source inventories are included. Large responses use scoped content tickets.
Read [the complete evidence and delivery record](docs/verification/2026-11-14-rc-rich-responses.md).

Passed: 20 English + 20 Spanish headed RC cases; 12 additional mobile checks;
275 English and 277 Spanish main regressions; 285 RC core checks; build and
15-file canonical/server/built asset parity. Actual parser saves and file bytes
were exercised in isolated databases. No installed update or live model campaign.
Legacy files without trusted ownership records must be regenerated.

Angela will rebuild/reinstall both main applications, then Tlachihuani. Do not
patch the running installation or delete the separate canonical client source.
Owned test browsers/hosts are stopped, .NET build servers shut down, and the
rc-rich-responses queue ended with finish.ready. Do not resume old queues.

## Development lifecycle-button fix · 14 November, 16:56 Mexico City

Start now respects observed ready/loading state as well as permission. Both controls
remain locked while a start/stop submission or receipt is pending; late receipt
responses cannot reopen completed work. Canonical and packaged development client
assets match the built output. Build, 49 headed regression checks, source whitespace
and asset hashes passed. Read [the focused report](docs/verification/2026-11-14-rc-lifecycle-buttons.md).

This update remains development-only; Angela will rebuild/reinstall Tlachihuani.
The installed applications and user grants were not changed. Test runtimes and
compiler servers were stopped afterward; no helper queue remains active.

## Development Start-permission fix · 14 November, 16:28 Mexico City

The source correction is complete: XT can edit an approved browser’s permissions,
with clear RC client guidance, stable device identity, separate Start/Stop authority
and stale-review/session safeguards. Canonical and packaged development client assets
were updated. Build, 285 core checks, 36 isolated headed-browser checks and 11 visible
dialog assertions passed. Read [the focused report](docs/verification/2026-11-14-rc-start-permission.md).

Angela explicitly requested development-only changes and will rebuild/reinstall
herself. Do not patch the active installation. No new package was produced. Existing
browser grants remain unchanged; after reinstall, enable Start in Connections →
Change permissions and reconnect without a new invitation.

Angela then requested all task background processes stopped. The owned test browsers
and isolated hosts are stopped, helper loops are absent, and the walkthrough queue
has finish.ready with no queued command. Do not restart the old queue automatically.

## Completed connection walkthrough and tutorials · 14 November 2026

The installed onboarding procedure and English/Spanish narrated tutorials are complete.
Read [the current report](docs/verification/2026-11-14-rc-connection-walkthrough.md).

- Actual XT start, real main-account enablement, invitation, fresh browser enrollment,
  full fingerprint comparison and native approval succeeded. Two real English model
  replies succeeded, including a cold-account request without opening main chat first.
- RC stop/start and reconnection preserved the main process and independent Torrenter
  state. Both repeated installed XT shortcuts restore the existing window.
- Direct XT menu, both Welcome setup links, account readiness labels, stable account
  selection, scoped capabilities and client recovery guidance are installed locally.
- Checks: 11 lifecycle, 267 RC core, 30 headed recovery (explicit adapter fixture),
  15 installed consistency/state; both tutorial chapter players passed.
- The latest RC/XT overlay verified 643 files; this is a local validation overlay,
  not a new MSI release. Both main editions have the updated external templates.
- Final observed state: RC running/ready; English main running; Spanish main stopped;
  primary service and Torrenter Jobs stopped, preserving the initial baseline.
- Deliver Spanish from artifacts/rc-video-tutorial/es and corrected English from
  artifacts/rc-video-tutorial/en-verified. Both include MP4, subtitles and a chapter
  player. They are narrated screenshots with labeled examples, not continuous video.
- Same-PC connections only. No phone/LAN acceptance, commit, push, tag or publication.
- Retained development consoles are idle. No job/update is queued. Do not replay
  earlier commands. The approved QA browser remains available for inspection.

Earlier dated records below are historical, including the strict Spanish 49/50
answer result. Today's connection checks do not replace that result.

## Completed local acceptance · 13 November, 23:28 Mexico City

The requested uninstall-first controller rebuild/install and bilingual local
acceptance are complete. Read [the final report](docs/verification/2026-11-13-installed-rc-acceptance.md).
Final evidence: `artifacts/rc-final-acceptance/20261114-051121/summary.json`.

- Controller package: `20261114-033543-1abcb41e`; all 1,475 installed runtime
  files match, setup exits 0, protected identities preserved.
- Main runtimes: English `english-20261114-0255` (7,870 files), Spanish
  `spanish-20261114-0457` (7,873 files); protected database/configuration bytes preserved.
- 50 English + 50 Spanish requests completed with all six modes exercised on/off.
  English answer checks: 50/50; Spanish: 49/50. ES050 returned
  `Cae lluvia de las nubes.` instead of only `lluvia`; preserve this failure.
- 85/85 mobile checks per edition; independent remote restart and final stop
  verified. Both main processes and HTTP listeners are stopped. All three
  controller services are running, RC is ready, and installed mobile assets match
  canonical source and repository mirror. Final verification exited 0.
- English evidence was copied from `043459` with SHA-256 provenance because its
  runtime was unchanged. Spanish was rebuilt and retested after fixing the
  `Devuelve` one-shot input-validation defect. Its source checks passed 247/247.
- Earlier failed runs remain intact. No commit, push, tag, firewall change or
  publication occurred. LAN/physical-phone checks remain deferred.

The normal test console remains open and idle. The installer finish marker is
set; no additional installation or prompt batch is queued. Do not replay the
old commands below. They are historical recovery notes.

## Historical checkpoints — superseded by the completed result
## Corrected Spanish run active · 13 November, 23:12 Mexico City

**Current acceptance:** `artifacts/rc-final-acceptance/20261114-051121`.
The corrected Spanish `spanish-20261114-0457` runtime installed successfully at
23:10 local: 7,873 hashes verified, 13 files updated, database/configuration
bytes preserved. Rollback: `artifacts/main-rc-installed-backup/214634dd2e334f7ba5d9f9447910a035/Tlamatini-Spanish`.
Spanish onboarding passed 10/10; the new remote restart/50-prompt/mobile/stop
sequence is running. English evidence is copied unchanged from run `043459` with
SHA-256 provenance; its executable remains the same tested `0255` build.

A first evidence-copy attempt (`051051`) stopped before onboarding because this
PowerShell environment lacked `Get-FileHash`. The helper now uses .NET SHA-256,
matching the installer. No prompt was dispatched in that failed handoff. The
normal queue is active; do not run the rebuild or batch again. Monitor the path
file afresh: old JavaScript monitor closures can retain the old acceptance path.
The final verification script is ready, including provenance checks and strict
five-second socket-refusal checks. Execute it only after the new run completes.

## Spanish input repair building · 13 November, 23:04 Mexico City

Original full run `artifacts/rc-final-acceptance/20261114-043459` is complete:
English 50/50 answers, Spanish 47/50, both 85/85 mobile, both remote restart/final
stop. `summary.json` verifies 100 actual requests, 97 answer checks and 170 mobile
checks. The first final socket check timed out at two seconds (Windows 10035);
its strict refusal assertion passed with a five-second deadline. No timeout was
accepted as shutdown. Both main processes are stopped.

Spanish ES016 exposed a real lexical validation defect: the one-shot prompt-shape
gate did not recognize `Devuelve` or several other text-transformation verbs.
`Tlamatini-Spanish/Tlamatini/agent/rag/interface.py` and its existing prompt-shape
regressions now cover those imperative/infinitive forms, retaining the separate
access validator. **247/247 Spanish source checks passed.** The other original
failures, ES028 `3 plumas` and ES050 a full sentence instead of `lluvia`, remain
strict answer-format failures. Every original result remains intact.

The retained normal console is executing `repair-spanish-input.ps1`: it verified
the original summary, is freezing `spanish-20261114-0457`, then installs that
Spanish-only runtime with full hashes, protected data and rollback copies.
English remains on its unchanged `english-20261114-0255` runtime. The controller
is still the uninstall-first verified `20261114-033543-1abcb41e` package.
After installation, `repeat-spanish-after-input-repair.ps1` automatically creates
a new acceptance directory, copies English JSON/PNG/log evidence with SHA-256
and origin metadata, and runs Spanish onboarding/restart/50/mobile/stop afresh.
Do not dispatch another copy while this command is active. Monitor build log and
`final-acceptance-path.txt` every 5–15 seconds. Final evidence verification checks
the copied English provenance and requires that its installed runtime is unchanged.

## Fresh acceptance running · 13 November, 22:35 Mexico City

The installed package is `20261114-033543-1abcb41e` (267 core and 42 gateway
checks passed; uninstall-first install verified all 1,475 files and identities).
Both main editions use the verified `20261114-0255` frozen runtimes.
The normal console queue was resumed at its verified idle foreground prompt
using `resume-visible-console.py`; its PID, creation time, owner, path, console
membership and empty prompt were checked before entering the staged command.
No duplicate console was opened and no visibility guard was bypassed.

**Active full acceptance:** `artifacts/rc-final-acceptance/20261114-043459`.
It runs both editions independently: onboarding, immediate remote restart,
50 real prompts, 85 mobile checks and final remote stop. The runner preserves
strict model-answer failures and only continues a batch with all 50 requests
completed with matching modes. Monitor every 5–15 seconds; never replay a batch.
After `complete.json`, run `verify-final-installed-acceptance.ps1` visibly and
update the final evidence. The old run and every failure remain preserved.

## Renewal repair checkpoint · 14 November, 03:36 UTC

Run `20261114-030824` completed all 50 English requests with matching modes;
46 strict answer checks passed and four failed. All four failures remain intact.
Its mobile run passed 78 checks before the host-directory picker hit an expired
RC session. The workspace opened near the end of the original 15-minute session,
but its first page-relative renewal would have happened after expiry.
The canonical and packaged mobile transport now renew immediately, use the
actual expiry, and share renewal before requests and on foreground recovery.
Mutations are never replayed. The headed gateway regression uses actual
one-minute sessions and verifies that revocation remains effective.

English stopped normally through RC at 03:35 UTC; its process and HTTP listener
closed. Spanish has not run in this acceptance. Both main editions are stopped.
Package `20261114-033543-1abcb41e` passed 267 core and 42 gateway checks,
including the real one-minute renewal/revocation regression. Uninstall-first
installation completed at 03:41 UTC: all 1,475 files match, both setup exits 0,
all protected identities preserved. The fresh full bilingual run is queued in
the normal console; Windows refused its focus switch and Angela was asked to
bring that existing console to the foreground. The 90-second guard then timed
out and ended the queue before a new acceptance directory was created. Angela
was asked to run `& 'C:\Development\XAIHT\repo\Tlachihuani\artifacts\rc-local-diagnosis\visible.ps1'`
in that same console. `command.ready` remains staged; no new prompt was sent.
The queue script now catches later foreground timeouts and keeps the request
pending; it still verifies actual visibility before executing anything. Inspect
queue/report state before issuing another command. The runner preserves strict
answer failures while continuing only when all 50 requests and modes succeeded;
transport, permission, mobile or lifecycle failures still stop the sequence.

Older sections below are historical checkpoints, not current run instructions.

## Fresh installed run · 14 November, 03:08 UTC

**Active acceptance:** `artifacts/rc-final-acceptance/20261114-030824`.
Both `20261114-0255` frozen runtimes are now installed: 7,870 English and 7,873
Spanish files verified; 11 changed per edition; protected database/configuration
bytes preserved. Rollback directory: `artifacts/main-rc-installed-backup/41b68f175a734d1fa3092afb014cdb4e`.
Controller package remains the installed `20261114-023208-81091886`.

The existing normal console is running `start-fresh-after-main-repair.ps1`, which
invokes the complete `final-installed-acceptance.ps1`. It runs English onboarding,
remote restart, 50 real prompts, 85 mobile checks and remote stop, then Spanish
independently. Monitor this run's `stage.json`, per-edition reports and
`complete.json` every 5–15 seconds. Do not restart an in-progress batch.
English EN002 returned the correct value `45` plus an unrequested explanation;
its strict answer-format check remains failed. The installed model log confirms
the model authored the extra text; RC delivered it with matching modes. When the
English batch finishes and the current command exits, queue
`continue-completed-requests.ps1`. It verifies all 50 requests actually succeeded
with all modes available, preserves answer failures without regrading/replay,
and continues mobile/stop plus the independent Spanish sequence. Any transport,
permission, missing-mode or incomplete-batch failure still stops that continuation.
Final reporting must distinguish request completion from strict answer quality;
never call an answer-format failure a pass.
The corrected answer comparator passed **10 checks**, keeping literal markup
and explicitly requested casing strict. A PowerShell array-wrapper mistake in
the preflight was fixed before this run; no prompt was sent by that failed check.

After `complete.json` exists, run `verify-final-installed-acceptance.ps1` in the
visible console and update the final report/handoff with the actual outcome.
All earlier run directories and their failures remain preserved. The response
repair checkpoint below now describes completed building/installation work.

## Active response repair · 14 November, 02:59 UTC

The corrected Tlachihuani package **`20261114-023208-81091886` is installed** after
uninstall; all 1,475 runtime files matched and installation identities survived.
Fresh run `artifacts/rc-final-acceptance/20261114-023937` passed English onboarding
and immediate remote restart, then completed all 50 real prompts. Its original
report has 48 passes: one assertion wrongly required lowercase `no` instead of
the valid answer `No`; the other found actual loss of literal HTML text during RC
conversion. The model returned `<b>safe</b>`, but RC displayed `safe`. Preserve
that failure. English subsequently stopped normally through RC. Spanish has not
started in this run.

Both main editions now preserve original answer text through an owned channel
hook in `services/response_parser.py` and `remote_controller_runtime.py`, keeping
system-report/denial text and the normal rich output. **242/242 source checks per
edition passed**, including actual formatter-to-adapter regression coverage.
The normal visible console is running `repair-literal-main.ps1`: freeze English
then Spanish to `artifacts/main-rc-freeze/{english,spanish}-20261114-0255`, then
install the verified frozen runtime overlays using `Install-MainRcValidation.py
20261114-0255`. Protected database/configuration bytes and rollback copies remain
required. Do not run main `build.py`'s destructive main function.

After this command finishes successfully, queue **a fresh**
`final-installed-acceptance.ps1` in the existing verified foreground normal
console. It will copy both installation records into a new run and perform all
50 English + 50 Spanish prompts, 85 mobile checks per edition, immediate restart
and final stop. The answer comparator now accepts `No` only for the comprehension
case; explicit casing cases remain strict (8 comparator checks passed).
The unused answer-regrading continuation was removed after the actual literal
response defect was found. Do not regrade the literal mismatch into a pass.
The final evidence/state verifier is `verify-final-installed-acceptance.ps1`;
only run it after the fresh run's `complete.json` exists.

The elevated installation queue remains open in PID 43244; finish it using its
`installer-finish.ready` marker after completion. Leave the ordinary console and
headed browsers retained. Controller services remain running; both main apps
are currently stopped. Firewall/LAN stays deferred. Older checkpoints follow.

## Active installed acceptance · 14 November, 02:39 UTC

Read [the installed acceptance record](docs/verification/2026-11-13-installed-rc-acceptance.md)
first. Package **`20261114-023208-81091886`** fixes the stale heartbeat that caused
an immediate Spanish remote restart to report success without launching. Broker
regressions passed 31 checks (two symbolic-link fixtures unavailable); core 267
and isolated gateway 40 passed. Corrected uninstall-first installation is running
in the retained elevated console after Angela restarted its expired queue.
Then run `final-installed-acceptance.ps1` visibly for a fresh bilingual batch.

Earlier installed run `20261114-015347` passed English 50 real prompts, 85 mobile
checks and remote start/final stop. Spanish onboarding passed; its restart found
the now-repaired product defect before any Spanish batch prompt. Preserve all
failure/reconciliation evidence. The older checkpoints below are historical and
do not describe the current installation or count as corrected-build acceptance.

## Latest mobile work · 13 November local / 14 November UTC

Read [the mobile scope and live acceptance record](docs/remote-mobile-parity.md)
before using the older evidence below. Full touch-adapted Tlamatini is authorized,
excluding only Agentic Control Panel, Prompt Flow Panel and Admin Panel. Both
Torrenter pages and all other menu actions remain in scope. The canonical mobile
assets are in `C:\Development\XAIHT\Tlamatini-RC\public`; mirror them into this
repository's RC server `wwwroot` before packaging.

Both main editions now have rebuilt frozen runtimes installed with protected
database/configuration bytes preserved. Current source checks: 267 RC core,
235 per main edition; 40 isolated gateway checks passed on the corrected package.
Direct installed boot/login/owned-console cleanup passed 6 checks per edition.
Both installed main editions passed **85/85 real mobile interaction checks** in
visible touch-emulated Chrome, through an isolated development gateway. Spanish
and English both used the corrected packaged gateway. These checks do not certify
installed RC service integration or inference.
The latest verified package is **`20261114-012418-5334617f`**. It fixes bilingual
CSRF cookie names and workspace cookie isolation in addition to the earlier
multipart, renewal and layout fixes. Generated-file chat links now reveal the
mobile Canvas tab; the new touch regression also verifies a missing file retains
the existing document and returns a real gateway 404. `validated-mobile-build.txt`
points to this package. See the 01:35 UTC checkpoint in the mobile evidence record.

The immediately preceding build failed with WiX `ERROR_DISK_FULL`. A bounded
cleanup recovered 8.06 GiB from obsolete generated staging, retaining verified
ZIPs, logs, source, evidence and main-application backups. Do not reuse removed
staging paths; `obsolete-staging-cleanup.json` lists them. The latest package has
its complete verified installation payload. Older `005815` no longer has its
extracted `zip-check` or `payload-check` copies.

Two Windows elevation requests were canceled, most recently at 00:48 UTC on
14 November. No elevated queue started; no `installed-final.json` exists. The old
install request was removed when the Spanish defect was found. **No install
request is currently staged.** The existing installation is still the earlier
`20261113-223721-17936f9b` build. Only the normal diagnostic console remains;
`console-elevation-audit.json` found no reusable elevated development queue.
`final-idle.json` confirms both main editions exited and all three services stopped.

When Angela can handle Windows consent, queue `prepare-final-install.ps1` in the
existing visible diagnostic console. Observe `installer-ready.json`, stage
`{"build_id":"20261114-012418-5334617f"}` in `install-request.json`, restore the
installation console and verify `installed-final.json` reports the exact build.
Then run the prepared **`final-installed-acceptance.ps1`** from the unelevated,
verified foreground diagnostic console. It starts installed services/XT and runs
normal onboarding, browser RC stop/start, 50 real prompts, mobile checks and RC
stop for English, then Spanish independently. **0/100 batch prompts have completed.**
Do not count fixture/source checks as those prompts. Account activation for Spanish
already passed through its normal password-confirmed form. All changes remain
uncommitted; no push, tag or publication occurred.

Preserve the ongoing visible diagnostic console and all evidence. Do not run a
second hidden workload. Final install requires Windows administrator consent;
authorization to install is already present. Firewall/LAN work remains deferred.

## Active implementation · 13 November 2026

Angela authorized implementing the approved Remote-Controller design and at least
100 distinct tests. Read [Remote-Controller](docs/remote-controller.md) and
[the verification record](docs/verification/2026-11-13-remote-controller.md) first.
Angela also explicitly authorized changes to **both main Tlamatini applications**.
The source now has a separate RC service, core library, live client bundle, XT
controls and installed-application launch broker. English and Spanish implement
account activation and the existing chat engine with execution consent and owned
cancellation. Keep Torrenter independent and preserve database backup/restore
mechanics. Main editions remain at the latest released **1.93.5**; this work does
not bump a version or authorize a destructive main-app build.

Observed evidence: **212/212 core checks**, **210/210 main checks in each edition**
(including 16 real SQLite migration/deletion/revocation cases), 24 broker checks
passed with two unavailable symbolic-link fixtures, **54/54 HTTP checks**, and
**119/119 headed-client checks** including lost heartbeat followed by a late result
without replay. Source carriage, syntax and edition parity passed. The final r5
package verified **1,466 runtime files byte-for-byte**. Native XT layout passed
11/11 at 144 DPI. The earlier **14/14 real isolated SCM checks passed** and its
temporary service was removed; that SCM run predates final source changes.
No UAC retry is pending. Final main-account browser acceptance passed **129/129 checks** across both
editions, including actual chat/history/Proceed/Deny/cancellation/recovery,
revocation and ORM account deletion using an isolated model fixture. The driver
verified real owned Chrome foreground state before interactions and screenshots. See the verification record for paths.

Recent source behavior: **Load recent conversation** uses an account-scoped typed
history request (40 rows/24,000 JSON bytes). `RemoteControllerRevocation` retains
account-deletion intent through service outages and app restarts. Native/account
polling is bounded to four concurrent slots; an active authorization watchdog
cancels after 20 seconds without renewal. RC marks running application work
`outcome_unknown` after 20 seconds without its heartbeat, or 45 seconds for the
broker, without automatic replay; an owning adapter may reconcile its later result.

Production post-quantum transport, independent client trust/distribution, voice and
installed end-to-end acceptance remain open gates. Do not claim the whole approved
design is production-complete from source checks. Changes remain uncommitted;
there has been no push, tag or publication.

The earlier handoff below is historical context.

Work in `C:\Development\XAIHT\repo\Tlachihuani`. The separate Tlamatini reference checkout is `C:\Development\XAIHT\Tlamatini`. Read AGENTS.md, CLAUDE.md, [the Torrenter operating guide](docs/torrenter.md) and [implementation status](docs/implementation-status.md). The design baseline is Revision 05 of `TlachihuaniKickoffDesign.html` on Angela’s Desktop.

The checkout path above supersedes the original location. Product metadata and
internal project lock references now declare **0.36.0**; this documentation
pass preserves those existing edits. See the [11 November checkout verification](docs/verification/2026-11-11-documentation.md).

## Current version and installation evidence

**Latest version: [0.36.0](docs/releases/v0.36.0.md).** The previous [0.3.3 notes](docs/releases/v0.3.3.md) cover torrent queue recovery, accurate completion/error reporting, sortable account workspaces and the existing offline setup. Use the [Windows setup guide](packaging/setup/README.md) and [verification status](docs/implementation-status.md). The 0.1.0 workstation installation of 3 November and isolated 0.2.1 MSI checks of 4 November remain historical evidence; they do not certify a 0.36.0 installation.

The [10 November recovery record](docs/verification/2026-11-10-torrenter-recovery.md)
records 64 automated checks and 48 headed-browser checks across both development
editions. Four real local-peer transfers completed despite two silent swarms.
A read-only installed audit matched all 432 delivered files from 21 completed
jobs. Three unfinished jobs were left unchanged. Preserve those exact scopes.

The workspace uses the signed-in Tlamatini account without pairing codes. Keep
its five canonical assets synchronized with both controller editions when those
changes are authorized. Start resumes an incomplete stopped job; Resume continues
a paused one; Recheck validates a failure. Completion is separate from stopped
peer activity, and reported seeds are separate from connected peers. Jobs yields
a blocked slot after 180 seconds without payload progress and retries after at
least 60 seconds. It waits for disk-complete pause acknowledgment before hashing.
The service and controller fixes require their respective installed updates.

* On ANGYSLAP both services are set to Manual. The delegated operator is `ANGYSLAP\angel`. XT starts independently at user sign-in. **Start Services** brings up the services and Torrenter on port 12064. **Stop Services** leaves only XT running. Jobs has a separate manifest from the original service.
* XT is gray until both services are running and Torrenter's API responds. The context menu also offers **Show panel** and **Exit tray**. The exit label reads **Exit tray (service keeps running)** until XT confirms both services are stopped or absent and any pending change finishes. The optional historical commands are preserved in `src/Tlachihuani.Tray/TrayMenu.cs`.
* `scripts/Build.ps1 -Mode Check` covers the original lifecycle. Publish, then run `scripts/Test-Jobs.ps1` for a real private swarm and crash/recovery checks. Supply a Playwright-capable Python and an installed Chrome for the explicitly headed workflow. Follow [the visible-execution policy](docs/development-execution.md).
* The installed XT/Chrome run on 31 October exercised the native code approval, upload, all detail tabs, stop, filter and record removal. The delegated account exercised actual SCM stop and start. The original Tlachihuani registration remained stopped. Installation and update retained the independent Jobs identity.

## Next work

The root release command is now `Build-Release.cmd`; read [release building](docs/release-building.md). It rebuilds the current checkout at the latest numeric local Git-tag version and emits a checked ZIP/checksum in `dist`. Setup now asks for the optional common login shortcut and always creates Start/Desktop shortcuts. Services remain Manual/stopped after installation and reboot. The normal unelevated installer opens XT once after success; an elevated launch directs the operator to the normal Desktop shortcut.

For 0.36.0, the builder selects the numeric `v0.36.0` tag; GitHub's release
flags are independent, and suffixed tags are ignored. This documentation update
does not stamp product metadata, build, install, modify services, create tags,
commit or publish. The separate **Panels → Tlachihuani** lifecycle panel remains
a design target; **Panels → Torrenter Workspace** is implemented.

1. Complete the release acceptance gates with a sign-out/Explorer restart, reboot, power interruption, multi-day transfer, a second real Windows login and packaged signing/rollback qualification. Keep the original service’s force-stop consent helper on its own backlog.
2. When authorized, qualify a fresh 0.36.0 package and the scheduler/disk changes on a clean Windows QA machine. Extend tracker error telemetry and rate-history visualization, and qualify large queues, network failures and disk exhaustion. The current REST and UI surfaces are listed in the [Torrenter guide](docs/torrenter.md).
3. Keep the existing Tlamatini wrapped Torrenter client compatible with the scoped REST contract. The future Tlamatini Panels integration and agent catalog belong to that checkout. Preserve its protected database mechanics.

Tlachihuani upstream is [angelahack1/Tlachihuani](https://github.com/angelahack1/Tlachihuani). Tlamatini upstream is [XAIHT/Tlamatini](https://github.com/XAIHT/Tlamatini). Keep the implementation uncommitted until Angela requests otherwise. Snyk remains disabled.
