# Exploring Tlachihuani.log

Open **Services → See log**. The viewer starts at the newest entries.

Scroll, select text, type in the search field, or choose **Pause live** to stop
the view moving. Logging continues normally. Your page, selection and scroll
position stay in place until you choose a navigation action.

- **Older / Newer** read the previous or next page from the opened file.
- **Beginning** goes to its first page.
- The file selector opens the current log or a retained numbered log.
- **Search all log files** is enabled by default. **Find older / Find newer**
  search the current log and all retained numbered logs in parallel, ignoring
  case. A new query searches the entire collection, independently of the page
  displayed. Further clicks continue through matches in chronological order.
  The viewer opens the matching file and highlights its text automatically.
- Clear **Search all log files** to restrict search to the selected file. Search
  displays file/byte progress and offers **Cancel search**. Unreadable files are
  reported as incomplete coverage, rather than a complete no-match result.
- **Follow live** returns to the current log's newest entries and resumes updates.
- **Copy page** copies the displayed page. Select text and press **Ctrl+C** to
  copy a smaller part.

Keyboard shortcuts: **Ctrl+F** focuses search, **F3 / Shift+F3** find newer/older,
**Ctrl+Home** goes to the beginning, **Ctrl+PageUp / Ctrl+PageDown** change pages,
and **Ctrl+End** returns to live updates. **Esc** cancels an active operation;
otherwise it closes the viewer.

The footer identifies the opened file and the displayed byte range. A very long
line can continue across pages. When a log rotates, the paused view keeps reading
the same opened file. **Follow live** deliberately switches to the current file.
History remains limited to the files the logger retains; the viewer does not
create an unlimited archive.

## Memory and responsiveness

The viewer reads at most **256 KiB of UTF-8 source per page**. The decoded text,
native text control and temporary buffers add a bounded amount of memory; this
is not a claim that the entire XT process uses only 256 KiB. Pausing stops reads
and display updates completely. It does not accumulate new entries in a queue.
Old pages are replaced as the reader navigates, instead of being retained.

Search uses at most **four parallel file workers**, each reading 64 KiB chunks
with a small overlap for matches that cross a boundary. A search keeps one
candidate offset per file, not a full-file string, line index or unlimited result
list. It captures file lengths before scanning; continuing writes cannot make
that search run forever. Only one search operation runs at a time. UTF-8
characters and ordinary line boundaries are preserved; giant lines are split
into bounded fragments.

Read-only, shared-delete handles identify Windows files through rotation. Normal
browsing keeps one opened file; an all-file search briefly opens the retained
set (bounded by the logger's 40-file retention setting, plus the current file
and any still-open historical file). The search releases every handle except
the displayed match when it finishes or is cancelled. Windows can retain a
deleted file's disk allocation until its reader closes. The viewer excludes
itself from statement and UI-event tracing, avoiding feedback into the log.

## Focused verification and preview

From a visible PowerShell console, run `scripts/Test-LogViewer.ps1`. It verifies
the foreground console before building the locked dependencies and runs only
the log-reader/viewer checks, with synthetic files under `artifacts`. It opens
the real viewer visibly, preserves evidence, and does not start XT or services.

Builds use a separate optimized output directory so an already open preview is
not overwritten. After checks pass, `scripts/Open-LogViewer.ps1` opens the latest
verified build against the
existing user log. This preview reads the log without replacing the installed XT
or changing the website connection. An optional `-LogFile` selects another log.
Scratch space and transcripts stay under the repository's `artifacts` directory.

The source correction and its preview do not update installed binaries. See the
[22 November verification record](verification/2026-11-22-log-viewer.md).
