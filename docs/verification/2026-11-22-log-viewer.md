# Log viewer browsing correction · 22 November 2026

Angela reported that continuous trace output made it impossible to inspect the
production message sent from xaiht.org. The old viewer inferred following from
the text selection, even though scrolling a native textbox does not move its
caret. Its paused append branch also bypassed the text-size limit.

## Change

`LogViewerForm` now has explicit live/paused state. Native scroll, mouse selection
and keyboard browsing pause before changing the view. Paused mode does not read
or append incoming output. Older/newer pages, beginning, retained-file selection,
cancellable directional search and explicit **Follow live** replace the growing
textbox. Clipboard copying is limited to the displayed page or selected text.

`LogFileReader` uses offset reads and one Windows file handle per opened file. Each page is at most
256 KiB of source, search chunks are 64 KiB plus bounded overlap, and file identity
survives rotation. Normal lines and UTF-8 boundaries are preserved; very long
lines are represented by bounded fragments. Navigation replaces old pages. The
viewer opts out of its own statement/UI tracing to avoid feedback.

**Search all log files** is now a visible GUI option, enabled by default, as
Angela requested after single-file search found no match. `LogHistorySearch`
opens a bounded snapshot of the current/retained files, then scans with at most
four parallel workers. Each worker uses the same bounded chunk reader. The
snapshot retains metadata and at most one candidate offset per file, not whole
contents or an unlimited match list. Search releases all handles except the
displayed match. New queries cover the complete collection independently of the
selected file; further searches continue in chronological order. A still-open
file removed by retention is also included. The GUI shows file/byte progress,
names the matching file, supports cancellation and names changed/unreadable
files when coverage is incomplete. Clearing the option restores selected-file
search.

## Visible verification

The foreground console guard passed before the build and focused checks. The
actual WinForms viewer was shown on Angela's desktop and its foreground state
was checked before UI assertions and screen captures. Consoles were left open.

Initial paging run: `artifacts/log-viewer-20261122-203139-f82123` (local Mexico City time).

- Release build with locked restore: exit 0, **0 warnings, 0 errors**.
- Focused reader/viewer verification: **7 check groups passed**, exit 0.
- Source whitespace check: exit 0.
- Synthetic files, scratch directories and transcripts stayed under repository
  `artifacts`; no temporary folders were created under a user profile.

The groups covered complete forward/backward Unicode paging; search at chunk
boundaries and EOF; directional/overlapping matches; cancellation; empty files
and a partially written UTF-8 character; giant lines; file rotation and retention
deletion; native scrolling with the caret at EOF; navigation/search outside the
initial page; and returning to the new current file with live updates resumed.
Visible buttons also passed containment checks at the desktop's current DPI.

While the viewer was paused, the fixture writer appended **67,108,864 bytes
(64 MiB)**. The displayed **261,994 characters**, selection and first visible
line were unchanged after more than two timer ticks. The measured retained
managed-memory change was **1,048,296 bytes** (below the 8 MiB regression limit).
This is a focused managed-memory regression measurement, not a process-wide
working-set guarantee or a long-duration soak test. The fixed page/chunk bounds
provide the architectural limit on retained reader data.

Evidence: `build.log`, `checks.log`, `verification.log`, `result.json`, and the
actual desktop captures `fixtures/paused.png` and
`fixtures/historical-match.png` in that run directory. An earlier successful
run is retained at `artifacts/log-viewer-20261122-202747-413c52`; the final run
also checks deletion of an opened archive and records the memory measurement.

## Final parallel-search verification

Final run: `artifacts/log-viewer-20261122-204917-dae53b`.

- Optimized, isolated configuration `LogViewer202611222049172b27a0`, locked
  restore: **0 warnings, 0 errors**, build exit 0. Separate outputs preserve
  any already-open preview and the installed application.
- **12 check groups passed**, checks exit 0; source whitespace check exit 0.
- Actual parallel scan: **4 concurrent workers**, **33,554,465 bytes** scanned
  across **5/5 files**. Mid-scan cancellation released every search handle.
- Results traversed archives `.1`, `.10` and `.40` in both directions, without
  returning the previous match or using worker completion order as chronology.
- Locked files were named as incomplete coverage. Rotation preserved captured
  file identity. A still-open expired archive remained searchable, and displaying
  it did not exclude newer files from a fresh query.
- The visible GUI option was checked by default. All-file search opened and
  highlighted an archived match and updated the selector. Disabling it restricted
  search to the selected file. GUI cancellation preserved the displayed page.
- The paused-reader regression again appended 64 MiB with unchanged page,
  selection and viewport; retained managed-memory change was **1,048,272 bytes**.

Final evidence includes `verification.log`, `build.log`, `checks.log`,
`result.json`, and actual screen capture `fixtures/archive-match.png`.
The preview launcher reads `artifacts/log-viewer-preview.json`, which is updated
only after a successful run, to open this verified build.

An intermediate 11-group parallel run passed at
`artifacts/log-viewer-20261122-204523-47e49f`. A later attempt at
`artifacts/log-viewer-20261122-204749-e25ad2` returned checks exit 1 after the
reader/search groups; its UI error text was not captured, so that failure's cause
is unconfirmed. The harness now preserves exceptions in stdout and `failure.txt`
and waits for foreground visibility before UI assertions. The final 12-group
run above completed with no failure report. These are focused checks, not an
extended production soak test.

## Installation and manual acceptance

The latest released version remains **0.38.0**. This work rebuilt development
outputs only. Installed XT, services, the xaiht.org connection, website deployment
and production log were not replaced or changed. No commit, tag or push occurred.

`scripts/Open-LogViewer.ps1` opens the rebuilt read-only viewer against the user's
existing log through the focused checks executable. It does not launch another
XT instance or start services. This allows Angela to try the repair before a
package update. The preview still needs her manual acceptance.

The website screenshot reports production message **#1** as written, but finding
`RC-TEST-001` in the actual log remains the next manual test. The synthetic search
fixture is deliberately labelled as synthetic and does not certify that delivery.
