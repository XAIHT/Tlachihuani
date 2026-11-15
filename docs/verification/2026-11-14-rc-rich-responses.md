# RC rich responses and file downloads · 14 November 2026

Development-only implementation is complete. Installed applications, services,
account grants and live databases were not updated. Angela will rebuild/reinstall.
Torrenter remains independent. No commit, tag, push, installer or publication was made.

## Result

Live replies and recent conversation use the main application's prepared history
HTML. RC preserves safe inline formatting, headings, code, tables, diagrams and
execution-report styling. The answer and execution report are parsed separately.
The renderer strips executable markup, handlers, unsafe URLs and network CSS;
user messages stay literal. Large saved replies load completely through a signed
content ticket instead of being cut into malformed HTML. This does not introduce
a different Markdown parser from the main application.

Save file / Guardar archivo replaces response Load in canvas actions in both RC
surfaces. Files are delivered as attachments and verified by byte count and
SHA-256 before browser saving. Filenames preserve Unicode and extensions, including
long emoji names. Failed or corrupt downloads trigger no save and remain retryable.
The displayed success says a download started; it does not claim the user completed
a browser Save As dialog.

Both main editions add AgentMessage.response_files and additive migration
0225_remote_response_files. Records come from the real response parser's created
programs/snippets and supported successful tool outputs. Downloads require these
message-owned records, pinned artifact identity and original bytes. A guessed
artifact name or model-written file URL is not authority. File-Creator now emits
successful output metadata, alongside the existing document/media producers.
The main build/source inventories carry the new module and migration.

Legacy messages keep their formatted text. Attachments without authoritative
ownership records ask to be regenerated. Changed attachments require a new reply.
File links expire after 30 minutes or main-app restart; recent conversation renews
eligible links. Files are limited to 64 MiB and complete-response JSON to 8 MiB.

## Verification

All executions used the retained, verified foreground PowerShell console and
headed Chrome on Angela's desktop. Scratch and browser profiles stayed in the
application-owned artifacts tree. The browser campaign used a real isolated RC
host and explicitly controlled adapter/file endpoint fixtures. The main-app suites
used real models, isolated SQLite, actual response-parser saving and actual file
bytes, with no language-model request or production AppConfig startup.

| Check | Result |
| --- | --- |
| Requested English browser cases | 20 / 20 |
| Requested Spanish browser cases | 20 / 20 |
| Extra full-workspace browser checks | 12 / 12 |
| Main English regressions | 275 / 275; no skips |
| Main Spanish regressions | 277 / 277; no skips |
| Existing RC core checks | 285 / 285 |
| RC server development build | passed; zero warnings/errors |
| Canonical / server / built web assets | 15 identical SHA-256 hashes |
| Source whitespace audit | passed in all three checkouts for touched scope |

Each main suite includes 20 new response-file/security cases, two real-parser
integration cases covering the five code-saving paths and typed tool outputs,
and eight File-Creator receipt cases. File tests also cover XML, scripts, raw
binary, XLSX, PPTX, extensionless files, expiry, tampering, changed/deleted files,
reparse refusal, same-name artifact collisions and account isolation. They verify
delivery integrity; they are not Office/PDF editor acceptance tests.

### Requested twenty cases per language

| Case | Behavior | English | Spanish |
| --- | --- | --- | --- |
| 01 | Live canonical HTML preserves headings, cards and inline presentation | PASS | PASS |
| 02 | Live progress and final code retain formatting without executing code | PASS | PASS |
| 03 | Wide tables and ASCII code stay usable at mobile width | PASS | PASS |
| 04 | Malformed answer tables cannot absorb the separate execution report | PASS | PASS |
| 05 | Recent history renders each assistant response and keeps user markup literal | PASS | PASS |
| 06 | Active markup, handlers, unsafe URLs and network CSS are inert | PASS | PASS |
| 07 | Ordinary external links stay safe and readable | PASS | PASS |
| 08 | Long rich responses arrive completely and genuine truncation is clearly identified | PASS | PASS |
| 09 | Save original .txt bytes and Unicode filename | PASS | PASS |
| 10 | Save original .md bytes and Unicode filename | PASS | PASS |
| 11 | Save original .html bytes and Unicode filename | PASS | PASS |
| 12 | Save original .svg bytes and Unicode filename | PASS | PASS |
| 13 | Save original .csv bytes and Unicode filename | PASS | PASS |
| 14 | Save original .json bytes and Unicode filename | PASS | PASS |
| 15 | Save original .pdf bytes and Unicode filename | PASS | PASS |
| 16 | Save original .png bytes and Unicode filename | PASS | PASS |
| 17 | Save original .zip bytes and Unicode filename | PASS | PASS |
| 18 | Save original .docx bytes and Unicode filename | PASS | PASS |
| 19 | Unavailable files show an error, save nothing, and allow an explicit retry | PASS | PASS |
| 20 | Downloads enforce authentication, account binding and revocation | PASS | PASS |

### Evidence

- [40-case browser results](../../artifacts/rc-rich-responses/1794698673422/results.json)
- [12-case final mobile results](../../artifacts/rc-rich-responses/workspace-1794699482402/results.json)
- [English main results](../../artifacts/rc-rich-responses/main-en-verified.json)
- [Spanish main results](../../artifacts/rc-rich-responses/main-es-verified.json)
- [Asset SHA-256 inventory](../../artifacts/rc-rich-responses/asset-parity.json)
- [Visible execution transcript](../../artifacts/rc-rich-responses/visible.log)
- [English visual fixture](../../artifacts/rc-rich-responses/workspace-1794699482402/en-main-gradient-and-save.png)
- [Spanish visual fixture](../../artifacts/rc-rich-responses/workspace-1794699482402/es-main-gradient-and-save.png)

Earlier failed runs remain preserved. They exposed UTF-8 input decoded with the
Windows code page (fixed in the native control entry point), a test reset race,
a transient download-directory count assertion, test database lifetime handling,
and outdated test fixtures. No failed assertion was relabeled as passing.
The final main suites, requested browser suite and extra mobile suite all passed.

## Delivery and cleanup

Rebuild/reinstall the English Tlamatini and Spanish Tlamatini applications first,
then Tlachihuani. Either order between the two main editions is valid. Use their
normal release workflow; no destructive main build was run in this task.
Tlamatini-RC/public remains the separate canonical client source and is mirrored
into Tlachihuani's server wwwroot; do not delete that source directory.

Both browser harnesses confirm their owned Chrome instances and HTTP fixtures are
closed; the isolated RC host exited through development-stop. The development
command loop ended with finish.ready, and .NET build servers were shut down.
The retained PowerShell console is idle. Existing user applications were untouched.

This record does not certify a rebuilt installed release, a real phone, LAN access
or new live model responses. Those remain distinct from these development checks.
