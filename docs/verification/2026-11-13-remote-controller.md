# Remote-Controller verification · 13 November 2026

Completed installed local acceptance: **100 real requests, 99 exact-answer passes,
170 mobile passes, and independent remote restart/final stop for both editions**.
Read [the final installed report](2026-11-13-installed-rc-acceptance.md) for the
current package and the retained Spanish answer-format failure. The dated
source/development checkpoints below remain historical.

Scope: source implementation in `C:\Development\XAIHT\repo\Tlachihuani`, live
client integration in `C:\Development\XAIHT\Tlamatini-RC`, and the explicitly
authorized English and Spanish main applications. Tlachihuani remains **0.3.3
pre-release**; both main editions remain at the latest released **1.93.5**.
Changes are uncommitted. No push, tag or publication was performed.

The [installed acceptance record](2026-11-13-installed-rc-acceptance.md) is the
current status. It records the real immediate-restart defect, corrected package
`20261114-023208-81091886`, and the fresh installed bilingual run. The checkpoints
below retain their historical scopes and do not override that record.

## Latest mobile checkpoint · 14 November, 01:35 UTC

The latest verified package is **`20261114-012418-5334617f`**: **267/267 core**,
**40/40 isolated gateway**, and **85/85 mobile interaction checks per edition**.
Both installed main editions were tested against this exact packaged development
gateway. Generated-file chat links now reveal the mobile Canvas tab; the added
touch regression verifies the normal missing-file path preserves existing text.
The failed Resource Timing assertion was replaced by direct browser response
observation. Spanish passed after a separate foreground-check failure was
inspected and document focus recovered; visibility checks remain enforced.
See [the scoped evidence and retained failures](../remote-mobile-parity.md).

One packaging attempt failed from insufficient disk space. Verified obsolete
staging cleanup recovered 8.06 GiB, retaining source, main backups, ZIPs, logs and
test evidence. The next full rebuild passed package verification.

**Final uninstall/reinstall still requires Windows administrator consent.** No
new UAC attempt has been launched after the two canceled requests, no install
request is staged, and **0/100 real-model batch prompts have completed**.
The prepared installed sequence remains English start/50 prompts/mobile/stop,
then Spanish independently, after the latest package is uninstalled/reinstalled.
These mobile results do not certify installed RC lifecycle or real inference.

## Previous mobile checkpoint · 14 November, 01:10 UTC

The latest verified repository package is `20261114-005815-bb532b43`.
It passed **267/267 core** and **40/40 isolated gateway** checks. Both actual
installed main editions passed **84/84 mobile interaction checks** through an
isolated development gateway in visible, touch-emulated Chrome. These tests
cover normal login/logout, menu inventory and exclusions, phone/tablet sizing,
photo upload/preview, actual canvas files/saves/conflicts/recovery, terminal
output, settings dialogs, folder selection, signed renewal and both Torrenter
pages with actual account queue readiness. No model response is mocked and
counted as a prompt; these mobile runs do not run the requested model batch.

English used the corrected final package; Spanish used the corrected development
publish immediately before packaging. The Spanish run exposed its distinct
CSRF cookie name and prior direct-session isolation problem; both are fixed and
covered by the 40-check gateway suite. See [the complete scoped evidence](../remote-mobile-parity.md).

Both main frozen runtimes were rebuilt and installed through a non-destructive
overlay, preserving database/configuration bytes during installation. Main
source checks passed 235 per edition; direct installed boot/login/owned-console
cleanup passed 6 per edition. This is separate from an RC browser lifecycle test.

**Final installation remains blocked on Windows administrator consent.** Both
elevation requests were canceled, most recently at 00:48 UTC. There is no elevated
installer queue or final-install result. The earlier Tlachihuani installation is
still present; both main editions and the three services are stopped, confirmed
in `artifacts/rc-local-diagnosis/final-idle.json`. The corrected package's install
request is not staged. The prepared `final-installed-acceptance.ps1` will run
English and Spanish independently after uninstall/reinstall, including RC
stop/start, 50 real prompts per edition, mobile checks and final remote stop.
**0/100 requested batch prompts have completed.**

The sections below retain the earlier source/fixture/SCM evidence and its original
scope; they do not override this latest checkpoint.

Development execution uses the retained visible foreground console. Scratch,
test databases and browser profiles stay outside Windows user profiles. Its
[transcript](../../artifacts/rc-implementation/visible-session.log) preserves the
observed failures, fixes and subsequent runs.

| Verification | Observed result | Evidence |
| --- | --- | --- |
| Locked NuGet restore and Release solution build | Passed, zero warnings/errors at the recorded build | [Console transcript](../../artifacts/rc-implementation/visible-session.log) |
| RC core, dispatch roles and consent lifecycle | **212/212 passed** | [Core r8](../../artifacts/rc-implementation/core-tests-r8/results.json) |
| English main application unit checks | **210/210 passed**, including 16 real SQLite schema/deletion checks | [English result](../../artifacts/rc-implementation/main-checks/Tlamatini.json) |
| Spanish main application unit checks | **210/210 passed**, including the same real database checks | [Spanish result](../../artifacts/rc-implementation/main-checks/Tlamatini-Spanish.json) |
| Broker path and plane-isolation checks | **24 passed, 0 failed, 2 skipped:** Windows denied symbolic-link fixture creation | [Broker checks](../../artifacts/rc-implementation/broker-checks/results.json) |
| Real HTTP and native-pipe checks with broker dispatch roles | **54/54 passed** | [HTTP result](../../artifacts/rc-implementation/http-1794557989925/results.json) |
| Headed Chrome live-client functional checks | **119/119 passed**, including heartbeat loss, late completion and no replay | [Browser result](../../artifacts/rc-implementation/browser-1794558058894/results.json) |
| Existing lifecycle checks | **11/11 passed**, including real console host and IPC | Console transcript |
| Native XT panel | **11/11 passed** at 144 DPI, including the RC invitation dialog; latest Access layout reviewed | [Native UI r4](../../artifacts/rc-implementation/native-ui-r4/results.json) |
| Actual RC executable under Windows SCM | **14/14 passed** with the earlier published executable; temporary acceptance service removed | [SCM results](../../artifacts/rc-implementation/scm-results.json), [SCM transcript](../../artifacts/rc-implementation/scm-session.log) |
| Full offline package construction and cabinet verification | r5 package passed; **1,466 runtime files** and maintenance files verified byte-for-byte; three services Manual with no automatic start | [Cabinet verification directory](../../artifacts/rc-implementation/cabinet-check-r5), console transcript |
| Final artifact consistency | Passed: client/source/published assets match; ZIP SHA-256 verified; Git diffs clean in all three Git checkouts | [Final consistency](../../artifacts/rc-implementation/final-consistency.json) |
| NuGet dependency audit | No vulnerable packages reported by the configured source at the recorded audit | Console transcript |
| Headed main-account fixture | **129/129 passed** across both editions with actual foreground HWND/document verification | [Main browser result](../../artifacts/rc-implementation/main-browser-1794558595159/results.json), [visible run log](../../artifacts/rc-implementation/main-browser-final.log) |
| Main source carriage and edition parity | Passed, including Python syntax, frozen module entries, shipped template/CSS/migration and mirrored runtime | [Source result](../../artifacts/rc-implementation/source-checks.json) |

## What the passing checks establish

The 212 recorded core cases cover configuration refusal, edition/account isolation, one-use
enrollment, exact fingerprint approval, signatures, replay, expiry, revocation,
socket tickets, typed payloads, durable admission, idempotency, adapter ownership,
completion and crash reconciliation. They also establish late reconciliation after
heartbeat loss, uncertain running work after account disable, and refusal of
consent from a second device when the originating chat device was revoked. They also cover broker/application dispatch
separation, control-message priority, sticky execution consent, stale/duplicate
decisions, account disable/re-enable and fresh device approval. Locked-destination
tests establish that a failed commit preserves both disk and live memory. Schedule
cases cover revision control, timezone/DST behavior and missed-run policy.

Each main edition's unit checks exercise account activation, local/authenticated
request checks, CSRF/origin boundaries, password confirmation, disable/retry,
existing chat integration, bounded output, permission ownership and cancellation.
These use isolated fixtures. They do not certify a newly installed executable,
a paid-provider model response, or a real application launch from an enrolled
device. Broker path checks neither mutate the registry nor launch applications;
the two unavailable reparse fixtures are reported as skips, never passes.

The HTTP suite runs the actual RC host and verifies packaged assets, Host/Origin
checks, cache/CSP headers, route confinement, native control, authentication,
receipts, schedules, logout/revocation and graceful listener shutdown. Launch
dispatch is claimed only through its broker role. Adapter and broker completions
in this harness are explicitly identified QA fixtures.

The Chrome suite uses the live client, real WebCrypto and native approval, with
seven emulated viewports from 320×568 to 1440×960. It verifies inert user content,
non-exportable private keys, reconnection, owned execution Deny, persistent consent
while status changes, heartbeat monitoring, revisioned schedules, revocation and
the offline shell. An earlier whole-desktop review found Chrome's Save Password
popup covering the invitation flow. The later recorded run includes the invitation
fix and the reviewed desktop image has no overlay. The history
control is additionally exercised by the main-account harness. None of these checks certifies physical
Android/iPhone/Safari behavior.

The first headed main-account flow found that `Referrer-Policy: no-referrer`
caused normal forms to send `Origin: null`, failing the page's origin/CSRF check.
Both editions now use `same-origin`; actual browser enrollment passed in both.
A later real cancellation check found that trailing progress incorrectly produced
`progress_unavailable`; the fixed run passed cancellation and subsequent chat in
both editions. Assistant HTML now becomes inert, once-decoded plain text; user
messages remain literal. The final driver restores and verifies each owned Chrome
window before interactions and screenshots, after a cross-window focus failure.
The final 129-check run passed account enrollment, real consumer chat through a
deterministic model boundary, saved history, Proceed/Deny, cancellation/recovery,
revocation and actual ORM deletion in both editions. It also established that
disabling/deleting one edition leaves the other connected and its history intact.
Screenshots from the final run were visually reviewed; phone approval details and
actions remain in the page scrolling flow. No real model inference is claimed.

## SCM and package scope

`tests/integration/rc_scm.ps1` ran the real RC executable as the fixed temporary
`Tlachihuani.RemoteController.Acceptance` service under a dedicated virtual account.
It verified ready HTTP/client delivery, stop with process/port exit, restart with
a new process, a second stop and retained state. The original Tlachihuani and Jobs
registrations were unchanged. The temporary service was removed. The prior UAC
and selected-console difficulties are resolved; no UAC action is pending.

That run used the earlier `artifacts/rc-implementation/publish/RCServer` output.
It does not certify the newest broker/main-application integration as installed.
The final ZIP at
`artifacts/rc-implementation/setup-output/0.3.3/Tlachihuani-0.3.3-windows-x64.zip`
was rebuilt from the final service/client source and passed cabinet verification
as r5. Package construction is separate from clean-install and upgrade acceptance.

## Remaining gates

Post-quantum remote transport, independently trusted client distribution and voice
remain unavailable and fail closed. Clean installation, a real provider/model
conversation in newly installed main executables, interactive application launch
from an enrolled device, and physical-device qualification remain separate gates. No production service update
or main-application executable build is implied by the source checks. See the
[implementation boundary](../remote-controller.md).
