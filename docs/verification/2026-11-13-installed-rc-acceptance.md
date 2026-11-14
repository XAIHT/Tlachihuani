# Installed RC acceptance · 13 November 2026, Mexico City

**Completed local acceptance:** 100 real requests, **99/100 exact-answer checks**, **170/170 mobile checks**, and independent remote restart/final stop for English and Spanish. Final state verification passed at 23:28 local. Tlachihuani remains **0.3.3 pre-release**; both main editions remain **1.93.5**.

[Machine-readable final summary](../../artifacts/rc-final-acceptance/20261114-051121/summary.json). Artifact directory names use UTC timestamps. No commit, push, tag or publication was performed.

## Final results

| Check | English | Spanish |
| --- | ---: | ---: |
| Normal onboarding, approved-device connection and real smoke reply | 10/10 | 10/10 |
| Real batch requests completed with matching execution modes | 50/50 | 50/50 |
| Exact-answer checks | 50/50 | 49/50 |
| Mobile interaction checks | 85/85 | 85/85 |
| Immediate remote stop/start with a verified new process | Passed | Passed |
| Final remote stop, absent process and closed HTTP listener | Passed | Passed |

All six modes were exercised both on and off: **Multiturn, Exec-Report, ACPX, Self-Modify, Ask Execs and Step by step**. Every request retains its prompt, expected/actual answer, effective modes, outcome and elapsed time. Fixture responses and smoke prompts are excluded from the 100.

The remaining failed answer is **ES050**: the expected answer was only `lluvia`, but the model returned `Cae lluvia de las nubes.` The request completed normally with matching modes. This remains a failure: the result is **not 100/100 answer passes**.

## Installed builds and provenance

- **Tlachihuani:** `20261114-033543-1abcb41e`, rebuilt from the authoritative `C:/Development/XAIHT/repo/Tlachihuani` checkout. The existing installation was uninstalled first. Both setup helpers returned 0; **1,475 runtime files** matched the package, and all three protected installation identities survived.
- **English main:** `english-20261114-0255`, **7,870 frozen files** verified.
- **Spanish main:** `spanish-20261114-0457`, **7,873 frozen files** verified; installed at 23:10 local after the input-validation repair. Thirteen runtime/source-overlay files were updated, with rollback copies retained.
- Main runtime installation preserved database and configuration bytes. These were non-destructive runtime updates, not full main-application installers. The destructive main `build.py` release entry point was not run.
- Eight live RC/mobile assets matched across canonical `Tlamatini-RC/public`, the repository server mirror and the installed server.

English evidence originates in `20261114-043459`. Its tested runtime and the RC package were unchanged by the Spanish-only repair. The final directory carries that evidence with [SHA-256 provenance](../../artifacts/rc-final-acceptance/20261114-051121/english-evidence-origin.json). The verifier checks its original installation record and every copied file. Spanish was run afresh in `20261114-051121` after the corrected runtime was installed.

Final evidence:

- [Controller installation](../../artifacts/rc-final-acceptance/20261114-051121/installation.json) and [main runtime installations](../../artifacts/rc-final-acceptance/20261114-051121/main-installation.json).
- [English prompts](../../artifacts/rc-final-acceptance/20261114-051121/english-installed/prompt-results.json) and [Spanish prompts](../../artifacts/rc-final-acceptance/20261114-051121/spanish-installed/prompt-results.json).
- [English mobile checks](../../artifacts/rc-final-acceptance/20261114-051121/english-installed/mobile-results.json) and [Spanish mobile checks](../../artifacts/rc-final-acceptance/20261114-051121/spanish-installed/mobile-results.json).
- [Mobile asset identity](../../artifacts/rc-final-acceptance/20261114-051121/mobile-assets.json) and [final controller services](../../artifacts/rc-final-acceptance/20261114-051121/final-services.json).

## Defects found and repaired

1. **Immediate restart accepted a stale heartbeat.** The broker now verifies a live installed process and requires readiness observed at or after its creation time. Real lifecycle checks require a new process, responsive HTTP and current adapter capabilities.
2. **Literal markup disappeared from RC answers.** Both main editions now pass the original response to the owned RC channel before rich formatting, retaining literal text and readable execution reports/denials.
3. **A workspace opened near expiry could fail before its first renewal.** Mobile transport now renews on entry, uses actual expiry, shares renewal between requests and renews on foreground recovery. Actions are never replayed. A headed regression crossed real one-minute session lifetimes and confirmed that device revocation still prevents renewal.
4. **Spanish one-shot instructions beginning with `Devuelve` were rejected.** The Spanish prompt-shape vocabulary now recognizes ordinary text-transformation imperatives and infinitives. Regressions cover these forms without an English POS tagger; the separate access validator remains in place. Corrected installed case ES016 returned exactly `verde` with all six modes off.

Supporting checks: **267/267 RC core**, **42/42 isolated gateway**, **31 broker checks passed** with two unavailable symbolic-link fixtures, **242/242 earlier English source checks**, and **247/247 corrected Spanish source checks**. These checks are separate from the real prompt and mobile totals.

## Mobile coverage and limits

The mobile checks exercised the included menu inventory and three requested exclusions; touch navigation at 360, 390 and 768 pixels; execution switches; photo upload/preview/removal; Canvas open/save and conflict recovery; terminal output; dialogs; host-directory selection; session renewal; Torrenter Sources and Workspace with account queue readiness; and logout. The English and Spanish 390-pixel menu screenshots were also inspected visually.

Only **Agentic Control Panel, Prompt Flow Panel and Admin Panel** are excluded. Existing permissions and execution approvals remain effective. The prompts were deliberately tool-free: selecting every mode does not certify every external agent integration or a self-modification transaction. Dialog coverage does not certify every destructive settings mutation or third-party service.

This acceptance is **local to this PC**. LAN/firewall and physical-phone checks remain deferred. Workspace voice/microphone controls operate the host computer; phone microphone capture is not implemented. No firewall rule was changed.

## Retained failures and final state

The original bilingual run `20261114-043459` completed all 100 requests and 170 mobile checks, with 97 answer checks passing. Spanish ES016 exposed the repaired validation defect; ES028 and ES050 produced extra words. That run remains intact. The corrected Spanish rerun passed 49/50 answers and confirmed the validation fix. ES050 remains failed in the final set.

Earlier failed runs and intermediate builds are preserved in the [development history](2026-11-13-installed-rc-development-history.md), including stale-heartbeat, literal-text and session-expiry failures. No failed record was rewritten into a pass. A two-second final socket probe returned Windows 10035; the strict connection-refused check passed with a five-second deadline. A timeout was never accepted as proof of shutdown. An evidence-copy handoff failed because `Get-FileHash` was unavailable; .NET SHA-256 replaced it before any new prompt was dispatched.

The final verifier passed. Both main editions have zero running installed processes and closed HTTP listeners. `Tlachihuani`, `Tlachihuani.Jobs` and `Tlachihuani.RemoteController` remain running, with RC ready for the next start. The installer finish marker is set; development consoles and headed browsers are retained. Script syntax and repository whitespace checks passed with native CRLF accepted. Diagnostic diff source lines were redacted; unrelated local configuration files were preserved.
