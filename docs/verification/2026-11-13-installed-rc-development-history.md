# Historical installed-RC development checkpoints

These earlier states are superseded by [the completed acceptance](2026-11-13-installed-rc-acceptance.md). Dated results remain historical.

# Installed RC acceptance · 13 November 2026 local

Scope: the actual local Windows installation, separate English and Spanish
accounts, mobile-sized headed Chrome, real model responses, and browser RC
start/stop. Tlachihuani remains **0.3.3 pre-release**; both frozen main editions
remain **1.93.5**. Firewall/LAN and physical-phone validation remain deferred.
No commit, push, tag or publication was authorized or performed.

## Final installed run in progress

Run `artifacts/rc-final-acceptance/20261114-043459` uses the corrected
`20261114-033543-1abcb41e` installation. English completed **50/50 real prompts
and 50/50 strict answer checks**, **85/85 mobile checks**, and independently
verified remote restart and final stop. Spanish onboarding and immediate restart
have passed; its 50-prompt batch is running. Do not infer full bilingual
completion until the final evidence verifier produces `summary.json`.

## Current session-renewal repair

The `20261114-0255` frozen main runtimes are installed: 7,870 English and 7,873
Spanish files verified, with protected database/configuration bytes preserved.
Run `20261114-030824` completed 50/50 English requests with matching execution
modes; 46 strict answer checks passed and four failed because the model added
text or wrappers. The original failures remain recorded without regrading.

The same run passed 78 mobile checks before the directory picker failed with
“Session expired or revoked.” The workspace opened near the end of a 15-minute
session and its first two-minute renewal timer fired too late. Mobile transport
now renews immediately, uses the signed session’s actual expiry, shares renewal
between concurrent requests, and renews on return to the foreground. It never
replays a mutation. A headed regression uses actual one-minute sessions and tests
revocation after renewal. Package `20261114-033543-1abcb41e` passed **267/267
core** and **42/42 gateway** checks. Uninstall-first installation completed at
03:41 UTC: both setup exits 0, all **1,475 runtime files matched**, and all three
protected identities were preserved. Gateway evidence is
`artifacts/rc-mobile-gateway-checks/8809a80011e14cc3a31f37c9a342d559/results.json`.

English stopped successfully through RC at 03:35 UTC with actual process and
HTTP shutdown verified. A fresh 50+50 installed batch remains required.

## Earlier runtime response repair

The corrected Tlachihuani package below is installed after uninstall with all
1,475 files matched. Fresh run `20261114-023937` passed English onboarding and
the immediate remote restart regression. It completed 50 prompts; its retained
report has 48 passes, one overstrict `No`/`no` assertion, and one actual literal
markup transport failure. The model returned `<b>safe</b>`, but conversion from
the formatted message yielded `safe`. English then stopped successfully via RC.
No Spanish batch prompt was sent in that run.

The main-edition formatter now gives the owned RC channel the original answer
before rich rendering consumes literal markup. RC keeps that inert text and
appends readable system reports/denials; the normal rich message stays unchanged.
Both editions passed **242/242 source checks**, including a real formatter-to-RC
path with isolated persistence, ownership, inert script text and bounded output.
Fresh frozen runtime builds `english-20261114-0255` and
`spanish-20261114-0255` were built and installed. A fresh complete bilingual acceptance
run is required afterward; do not convert the earlier literal mismatch into a pass.

## Corrected controller package

`20261114-023208-81091886` rebuilt the authoritative repository after a real
immediate stop/start failure. The broker had accepted the previous process's
still-valid heartbeat before checking whether that process existed. Spanish's
stop succeeded, but the next start returned “ready” without launching anything.

The broker now verifies the installed process first and accepts readiness only
from a heartbeat at or after that process's creation time, with the owned process
handle still live. A stopped edition must actually launch even while its old
heartbeat lease remains. The installed lifecycle harness also requires a new
process creation time after restart, a responding HTTP endpoint and live adapter
capabilities.

- Broker checks: **31 passed, 0 failed, 2 unavailable symbolic-link fixtures**,
  including seven added readiness regressions.
- RC core: **267/267 passed**.
- Isolated gateway: **40/40 passed**; these are boundary checks, not real prompts.
- Release builder completed package/hash verification.
- Corrected uninstall-first installation passed with both setup exits 0,
  1,475 matching runtime files and all three protected identities preserved.
  Complete acceptance still requires the main-runtime response repair above.

Evidence: `artifacts/release-builds/20261114-023208-81091886/build.log`,
`artifacts/rc-local-diagnosis/restart-broker-checks/results.json`, and the
installation record copied into each final acceptance run.

## Earlier installed run and retained failures

Run `artifacts/rc-final-acceptance/20261114-015347` used package
`20261114-012418-5334617f`, installed after uninstall with **1,475 matching runtime
files**, both setup exits 0 and all three protected installation identities
preserved. English completed **50/50 real batch prompts**, **85/85 mobile checks**
and verified remote start/final stop. Spanish passed normal onboarding and its
real smoke answer; its immediate restart then exposed the product defect above.
No Spanish batch prompt was dispatched in that run.

The retained evidence also records harness issues, not silently converted passes:

- Normal login focus failures stopped dependent browser work. Actual focus was
  restored before continuing; foreground guards were retained.
- The initial English smoke assertion inspected the whole receipt card instead
  of its answer. The existing answer `42` was observed and reconciled without
  sending another prompt; original failures remain alongside the reconciliation.
- The initial English test launcher created a process without a console. RC
  correctly refused its shutdown. That idle test process and its pinned,
  identity-verified descendants were explicitly cleaned up; this is **not**
  counted as remote stop. The launcher now gives each main edition its own
  console. English's later RC-started process stopped through RC successfully.
- English startup focus recovery reconciled the existing successful receipt,
  the new process created by installed XT, HTTP and capabilities without replay.
- The first corrected reinstall attempt timed out at the required foreground
  gate before uninstall. The same elevated console was retained and its queue
  restarted by Angela; no visibility requirement was removed.

## What the checks establish

Each edition's planned batch contains 50 distinct arithmetic, text, translation,
Unicode, JSON and literal-HTML prompts, sent to the installed application and
actual configured model. Every result records the prompt, expected/actual answer,
receipt state, elapsed time and effective execution modes. Smoke prompts are
separate and excluded from the batch count. Multiturn, Exec-Report, ACPX,
Self-Modify, Ask Execs and Step by step are selected and their effective values
checked. These deliberately tool-free prompts do not certify every external
agent integration or a self-modification transaction.

The 85 mobile checks cover the included menu inventory and three exclusions,
360/390/768-pixel layouts, touch navigation, photo upload, Canvas files and actual
saves, revision conflict/recovery, terminal output, dialogs, folder selection,
device renewal, both Torrenter pages with account queue readiness, and logout.
They do not certify every settings mutation, external dependency, physical phone
or network route. Mobile microphone controls currently operate the host's
microphone; phone microphone capture is not implemented.
