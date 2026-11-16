# Hosted RC hardening review — 15 November 2026

Development-only review of the recent connector, client, execution-mode and
main-application bridge changes. The review found two reproducible defects and
corrected both in development. This is not mission-critical certification.

## Findings and corrections

1. **High: retry calculation overflowed during a prolonged outage.** Both main
   editions calculated `2.0 ** failures` before applying the 15-second cap.
   At failure 1,024, Python raised `OverflowError`. This can interrupt normal
   adapter polling and revocation retry scheduling after approximately four
   hours of repeated failures. The calculation now saturates before exponentiation.
   Tests cover the boundary, one million failures, and an actual failed adapter
   poll moving from failure 1,023 to 1,024 while preserving cancellation and its
   next retry time. This is simulated outage coverage, not a four-hour soak.
2. **High: a specific error could conceal an uncertain execution result.** The
   receipt formatter handled `account_disabled` before `outcome_unknown`, saying
   access was disabled *before* execution. The server can legitimately mark a
   running request `outcome_unknown/account_disabled`; its action may already
   have happened. The formatter now prioritizes the uncertain state and asks
   the person to check Tlamatini before resubmitting. English and Spanish tests
   exercise seven error-code combinations and preserve the different wording
   for a request actually cancelled before execution. Canonical and packaged
   clients match; the service-worker cache was advanced to v12.

Sources: both editions of `agent/remote_controller_bridge.py`, canonical
`Tlamatini-RC/public/remote.js`, and the packaged `RC.Server/wwwroot` mirror.
Pre-fix reproductions are retained in
`artifacts/rc-hardening-review/retry-before.json` and `receipts-before.json`.

## Improvements reviewed

- The connector validates current local account authority per forwarded request,
  bounds replay tracking, and keeps independent accounts working when one is
  disabled. Configuration serialization prevents stale connection state from
  overriding a later disconnect. Development certificate pins include validity
  checks; unsupported public/PQ profiles remain refused.
- The browser queues requests when its four active slots are occupied, bounds
  their wait, and can cancel requests before sending them. A bad reply fails its
  own request without discarding unrelated work.
- Step by step requires Multiturn in the client and server. An untouched
  Self-Modify control follows the desktop setting instead of overwriting it.
- Main-app polling and account revocation use backoff. Re-enabling an account
  finishes its pending revocation before binding it again. Failed response-file
  snapshotting preserves the answer without inventing a downloadable artifact.

The comparison used the previous hosted validation's source hashes where
available, the current Git diffs, and the existing test evidence. It does not
claim a complete historical baseline for every previously untracked file.

## Verification and videos

The final regression stages passed **1,013 checks**, counting the repeated client
suite only once:

| Stage | Passed |
| --- | ---: |
| English main-app regressions, isolated database | 294 |
| Spanish main-app regressions, isolated database | 296 |
| RC core | 286 |
| Client modules and receipt wording | 15 |
| Hosted API/relay | 53 |
| Actual headed hosted browser | 36 |
| Real Keycloak/OIDC, clean retry | 25 |
| Linux container | 8 |

Locked dependency restore and Release builds passed. Container checks used a
non-root, read-only Linux cloud image with no published host ports.

Evidence is under `artifacts/rc-hardening-review`, with the API/browser campaign
under `artifacts/rc-hosted-validation/20261115-130746-3a797b84`.
The first OIDC run failed desktop/document foreground checks and consequent
login-dependent assertions; it is retained as failed. A fresh, unchanged OIDC
suite passed 25/25 in `rc-hardening-review/oidc-retry`. No visibility check was
disabled. The initial core-check invocation omitted its required output argument
and was refused; the corrected invocation passed. The first revised recording
omitted expanding the collapsed execution options; that recording is retained
as failed and the script was corrected before recording again.

The revised recording adds execution-mode dependencies and a real heartbeat
expiry through the development server. It verifies an uncertain receipt, then
a late completion of that same request without resubmission. Both languages
retain sign-in, device approval, formatted responses, history, original-byte
CSV/SVG/PDF downloads, responsive layout, reconnect and sign-out.

The new recording passed **36 assertions**, with 12 chapters per language:

- [Offline bilingual player](../../artifacts/rc-hardening-review/watch/Watch.html).
- [English video, 256.08 seconds](../../artifacts/rc-hardening-review/watch/Tlamatini-RC-English.mp4).
- [Spanish video, 272.50 seconds](../../artifacts/rc-hardening-review/watch/Tlamatini-RC-Español.mp4).
- [Share ZIP](../../artifacts/rc-hardening-review/Tlamatini-RC-video-tutorials.zip).

The MP4 files passed a complete FFmpeg decode, H.264/AAC and chapter checks.
Headed playback checked both languages, chapter seeking, loaded captions and
the offline file player. All 24 narration tracks contain non-silent audio.
The ZIP includes only finished media, captions, player and verification files;
test credentials, keys and browser profiles are excluded. Original videos and
failed attempts remain in their original evidence folders.

Final auditing confirmed that all 75 recorded source hashes were unchanged and
all 18 shipped client assets matched the canonical files, packaged source and
both development builds. Whitespace checks passed. `final-cleanup.json` records
no remaining owned test workers, test-port listeners or test containers. The
verification console remains open and idle; its queue is ended with `finish.ready`.

## Remaining qualification gates

The hosted profile is still an HTTPS loopback development lab. Its relay can
read lab traffic. Public deployment remains blocked pending endpoint encryption,
independently trusted client delivery, production identity/provisioning and
operational design. Installed-service recovery and reconciliation of external
side effects after crashes, real phone/public-network
acceptance, long-duration fault/soak testing, and an independent security review
remain necessary. Deterministic sample replies do not validate live-model
correctness or the safety of model-directed actions.

No installed application, private account database, Torrenter service, xaiht.org
deployment, release version, Git commit or tag was changed by this review.
