# Torrenter recovery verification · 10 November 2026

This dated evidence is referenced by the [0.3.3 pre-release documentation](../releases/v0.3.3.md).
It covers source checks and a read-only installed audit. No product build,
installation, service restart, live torrent mutation or commit was performed.

## Automated source checks

| Suite | Passed | Scope |
|---|---:|---|
| `tests/integration/test_transfer_recovery.py` | 12 | Stalled slots, account/host limits, retained stop intent, partial files, disk-pause acknowledgment, revision edits and rapid restart |
| `tests/integration/test_magnet_queue.py` | 2 | Durable metadata queuing, including six magnets and real local metadata discovery |
| `tests/integration/test_delivery.py` | 8 | Delivery and fallback boundaries |
| `tests/unit/workspace_table.test.cjs` | 30 | Sorting, filtering, pagination, completion labels and eligible actions |
| English `agent.test_torrenter_recovery` | 6 | Client status normalization, recovery guidance and error preservation |
| Spanish `agent.test_torrenter_recovery` | 6 | Equivalent controller behavior with localized guidance |
| **Total** | **64** | Source verification; not installed release acceptance |

The recovery suite completed four **real BitTorrent payload transfers** from
controlled local peers while two silent swarms competed for slots. Saved bytes
and SHA-256 matched independently. Only the isolated test shortens scheduler
timers; production values remain 180/60 seconds. Unit cases also use fake handles
to isolate boundaries; those cases are not claimed as network transfers.

Python Ruff passed for the changed worker/API, client/activity and test files.
JavaScript ESLint passed for both editions' workspace assets. The mirrored
`table.js` and `workspace.js` matched the canonical Tlachihuani copies byte for byte.

## Visible source workspaces

Headed Chrome passed **48 checks: 24 per edition**, using each edition's actual
Django workspace, authentication and Jobs API/journal against isolated QA records.
Coverage includes Start/Stop and Recheck, completed/stopped editing controls,
one intentionally empty session read, all twelve sort columns, keyboard access,
filters, pagination, persisted views, account separation and 1600/390/320-pixel
viewports. Test records are historical fixtures, not downloaded books.

The API adapter bypasses installed native-pipe enrollment; it does not certify
Windows service integration. Test servers used ports 19431/19432 and closed with
their dedicated browser afterward. The verified foreground console stayed open.

## Installed audit — separate evidence

Angela's queue contained 24 records: 21 finished and three unfinished. Independent
SHA-256 checks matched all **432 delivered files** in the finished jobs. One finished
record appeared stopped; another retained an old verification error. Neither
proved a missing delivered file.

Two unfinished jobs were stopped at 0% and about 61%; another waited for peers at
0%. Reported seed counts did not establish connected sources. No Start, Stop,
Recheck or other write was sent to those jobs. A possible old disk-writer race was
identified in code, but the available log did not identify its exact historical writer.

## Reproduce and inspect

Use a verified visible foreground console, application-owned scratch, the prepared
Python/libtorrent runtime and headed Chrome. Commands and prerequisites are in
[the table harness guide](../../tests/desktop/torrenter-table.md) and
[integration checks](../../tests/integration/README.md).

Historical artifacts from the original 10 November run reside in
`C:\Development\XAIHT\Tlachihuani\artifacts\verification\torrenter-state-20261110`:

- `final-summary.json`, `session.log` and individual suite logs;
- `visual/results.json`, `visual/SUMMARY.html` and whole-desktop/page captures;
- `download-file-checks.json` and `live-queue-summary.txt` for existing file evidence.

Artifacts are ignored local verification output, not release contents. Reproduce
the checks on another workstation instead of assuming those local paths exist.

## Checkout relocation — 11 November 2026

The active source checkout is now `C:\Development\XAIHT\repo\Tlachihuani`.
The old artifact path above is retained only as the location of dated evidence.
On 11 November, byte comparisons confirmed that its tracked application sources,
workspace assets and test harnesses match the corresponding files in the original checkout.
The documentation checks in the new checkout are recorded [separately](2026-11-11-documentation.md).
The 64/48 results were not rerun or relabeled as new-checkout installation acceptance.
