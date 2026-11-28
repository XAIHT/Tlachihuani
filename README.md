<!-- Tlachihuani — Created for Angela López Mendoza · XAIHT · @angelahack1 -->

<p align="center">
  <img src="assets/branding/readme-hero.svg" alt="Tlachihuani connects the XT tray, Windows services, Flow Pills, HandGI, Remote-Controller, Torrenter and the local REST API." width="1280" />
</p>

<h1 align="center">Tlachihuani</h1>

<p align="center">
  <b>The Windows companion platform for Tlamatini.</b><br />
  Manage independent Windows services from the <b>XT</b> tray, run up to 256 concurrent <b>Flow Pills</b>,<br />
  send prompts from your phone through <b>xaiht.org</b>, run resilient <b>Torrenter</b> queues,<br />
  and turn the volume up or down with <b>HandGI</b> hand signs—all outside your browser.
</p>

<p align="center">
  <a href="docs/releases/v1.0.1.md"><img src="assets/branding/badge-version.svg" alt="Tlachihuani v1.0.1, latest release — release notes" /></a>
  <a href="#installation"><img src="assets/branding/badge-windows.svg" alt="Windows 11 x64 installation" /></a>
  <a href="packaging/setup/README.md"><img src="assets/branding/badge-package.svg" alt="Offline ZIP installation package" /></a>
  <a href="#build-from-source"><img src="assets/branding/badge-dotnet.svg" alt="Build with .NET 10" /></a>
  <a href="docs/torrenter.md"><img src="assets/branding/badge-rest.svg" alt="REST API v1 guide" /></a>
</p>

<p align="center">
  <b>Latest release: <a href="docs/releases/v1.0.1.md">v1.0.1</a></b> · first official release <a href="docs/releases/v1.0.0.md">v1.0.0</a> (after pre-releases 0.1.0–0.40.1) ·
  <a href="LICENSE"><b>MIT License</b></a><br />
  Offline Windows installer &amp; uninstaller · Self-contained .NET 10 + CPython/libtorrent · Optional XT at sign-in · Control Panel removal
</p>

<p align="center">
  <a href="#at-a-glance">At a glance</a> ·
  <a href="#architecture">Architecture</a> ·
  <a href="#quick-start">Quick start</a> ·
  <a href="#tray-controls">Tray controls</a> ·
  <a href="#security-and-current-boundaries">Security</a> ·
  <a href="#installation">Install &amp; uninstall</a> ·
  <a href="#build-from-source">Build</a> ·
  <a href="#documentation">Documentation</a>
</p>

---

## At a glance

<table>
  <tr>
    <td width="50%" valign="top">
      <h3>Flow Pills</h3>
      <p>Open, draw and run Tlamatini's <code>.flw</code> agentic flows and <code>.fpmt</code> prompt flow pills in a dedicated desktop window—without modifying or running them on open.</p>
      <ul>
        <li>Up to <b>256 concurrent flows</b> by default (configurable <code>1–4096</code>) with RAM-aware admission that protects Windows free memory.</li>
        <li>Live-colored graph canvas matching Tlamatini's palettes and shapes, interactive <b>Reply bar</b> (<code>Send</code> / <code>Yes</code> / <code>No</code>), and independent live log tails.</li>
        <li>Kill-on-close <b>Windows Job Objects</b> per run, drag-and-drop loading, and <b>Keep running</b> background mode.</li>
      </ul>
      <p><a href="docs/flow-pills.md">Read the Flow Pills guide →</a></p>
    </td>
    <td width="50%" valign="top">
      <h3>Remote-Controller &amp; Phone Prompts</h3>
      <p>Host <b>Tlamatini-RC</b> locally as an independent service and connect your computer to <b>xaiht.org</b> with zero open ports or firewall rules.</p>
      <ul>
        <li><b>Outgoing-only HTTPS</b> long polling (25 s) reaches your PC behind any home router, office firewall or hotspot.</li>
        <li>Send real prompts to an explicitly approved English or Spanish Tlamatini account from a phone, with synchronized <b>Ask Execs</b> approvals, cancellation and reload-safe receipts.</li>
        <li>Bilingual EN/ES mobile PWA workspace, P-256 browser enrollment, rich HTML replies, verified file downloads and schedules.</li>
      </ul>
      <p><a href="docs/xaiht-link.md">xaiht.org link →</a> · <a href="docs/remote-controller.md">Remote-Controller →</a></p>
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <h3>Torrenter Jobs &amp; REST API v1</h3>
      <p>Background torrent downloads owned by the independent <code>Tlachihuani.Jobs</code> service—keep downloading even after you close the browser or Tlamatini.</p>
      <ul>
        <li>Private, hash-pinned <b>CPython 3.12.10 + libtorrent 2.1.1</b> engine with a single-writer SQLite WAL journal and local loopback REST + SSE API (<code>127.0.0.1:12064</code>).</li>
        <li>4-slot FIFO magnet metadata queue, 180-second stalled-slot rotation, and disk-complete <b>SHA-256 verified delivery</b> to <b>Downloads → Torrenter</b>.</li>
        <li>12-column sortable, filterable, per-account workspace with native XT review before file erasure.</li>
      </ul>
      <p><a href="docs/torrenter.md">Read the Torrenter &amp; REST API guide →</a></p>
    </td>
    <td width="50%" valign="top">
      <h3>XT Tray &amp; Unified Diagnostics</h3>
      <p>Operate three least-privilege virtual-account Windows services from a single unelevated system tray companion.</p>
      <ul>
        <li>All services install <b>Manual / Stopped</b> and stay stopped after reboot until you select <b>Start Services</b>.</li>
        <li>Compile-time Roslyn <b>TraceWeaver</b> instruments every process into secret-masked trace logs while staying quiet when idle (one line per loop every 10 minutes).</li>
        <li>Rotation-safe <b>See log</b> viewer (256 KiB paging, 4-worker parallel search across 40 retained files) plus copyable <code>Last-service-error.txt</code> failure reports.</li>
      </ul>
      <p><a href="docs/log-viewer.md">Log viewer →</a> · <a href="docs/panel-and-tray.md">Panel &amp; tray →</a></p>
    </td>
  </tr>
  <tr>
    <td colspan="2" valign="top">
      <h3>HandGI hand signs</h3>
      <p>Control the sound volume with simple hand signs in front of a camera—ready-made signs, nothing to train, and pictures never leave the PC.</p>
      <ul>
        <li><b>Thumb up</b> and <b>Thumb down</b> turn the volume up and down and a <b>Closed fist</b> mutes; teach your own sign by showing it three times.</li>
        <li>A per-user process in the signed-in session (never Session 0); <code>Ctrl+Alt+H</code> pauses or resumes hand control from any program.</li>
        <li>Runs its models on an NVIDIA GTX 1060 or newer through DirectML, with an opt-in CPU compatibility mode.</li>
      </ul>
      <p><a href="docs/handgi.md">Read the HandGI guide →</a></p>
    </td>
  </tr>
</table>

---

## Architecture

Tlachihuani separates the user session (XT, Flow Pills and HandGI) from Session 0 background services. Each Windows service runs as its own virtual account (`NT SERVICE\*`), and remote phone prompts travel exclusively over XT's outgoing HTTPS connection.

<p align="center">
  <img src="assets/branding/readme-architecture.svg" alt="Tlachihuani 1.0.1 architecture diagram: outgoing HTTPS link to xaiht.org with zero open ports, unelevated Windows User Session (XT System Tray, HandGI hand-sign volume control with its camera, hand models on the DirectML GPU and Windows volume, Local Browser and Tlamatini Panels, Flow Pills Window), three Session 0 virtual-account Windows services (Tlachihuani, Tlachihuani.Jobs on 127.0.0.1:12064, Tlachihuani.RemoteController on 127.0.0.1:12481), Installed Tlamatini (English and Spanish), plus unified trace logging, native Control CLI, and isolated ProgramData state." width="1280" />
</p>

<details>
<summary><b>Plain-text architecture schematic (for terminals &amp; raw Markdown viewers)</b></summary>

```text
  ╭──────────────────────────╮                      ┌──────────────────────────────────────────────────────────────────────────────┐
  │ ANY NETWORK · INTERNET   │                      │ WINDOWS USER SESSION · UNELEVATED                                            │
  │                          │                      │                                                                              │
  │  xaiht.org · TlamatiniRC │◄── 25s HTTPS poll ───┤  XT System Tray & Control CLI         Local Browser & Tlamatini Panels       │
  │  Phone & browser prompts │    (outgoing only ·  │  Tlachihuani.Tray.exe · Control.exe   Panels → Torrenter · Tlamatini-RC PWA  │
  │  HTTPS relay · not E2EE  │     no open port)    │  Services · RC · See log · DPAPI      12-col workspace · P-256 browser keys  │
  ╰──────────────────────────╯                      │     │                                                                        │
                                                    │     ├── per-user pipe ──►  HandGI · Tlachihuani.HandGI.exe                   │
                                                    │     │                      Hand signs set the sound volume · Ctrl+Alt+H      │
                                                    │     │                      Camera → 3 hand models (DirectML GPU) → volume    │
                                                    └─────┬───────────────────────────────────┬───────────────┬────────────────────┘
                                                          │ ACL Named Pipes (local DACL)      │ :12064        │ :12481
                    ┌─────────────────────────────────────┼───────────────────────────────┐   │ HTTP/SSE      │ HTTP/WS
                    │                                     │               ┌───────────────┼───┘               │
                    ▼                                     ▼               ▼               ▼                   ▼
  ╔════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════════╗
  ║ SESSION 0 · THREE INDEPENDENT WINDOWS SERVICES  (Own-process · NT SERVICE\* · Manual / Stopped on install & reboot)            ║
  ║                                                                                                                                ║
  ║  ┌──────────────────────────────┐     ┌─────────────────────────────────────┐     ┌─────────────────────────────────────────┐  ║
  ║  │ Tlachihuani        [PRIMARY] │     │ Tlachihuani.Jobs           [:12064] │     │ Tlachihuani.RemoteController   [:12481] │  ║
  ║  │ Service\Tlachihuani.Service  │     │ Jobs\Tlachihuani.Jobs · 127.0.0.1   │     │ RCServer\Tlachihuani.RC.Server          │  ║
  ║  │ Core lifecycle & 2s pulse    │     │ Provider registry & Kestrel API/SSE │     │ P-256 grants, receipts & schedules      │  ║
  ║  └──────────────┬───────────────┘     └──────────────────┬──────────────────┘     └────────────────────┬────────────────────┘  ║
  ╚═════════════════╪════════════════════════════════════════╪═════════════════════════════════════════════╪═══════════════════════╝
                    ┆ Service trace logs                     │ Owned Windows Job Object                    │ Approved account adapter
                    ┆ (copied into Tlachihuani.log by XT)    ▼                                             ▼
  ┌─────────────────┴───────────────┐     ┌─────────────────────────────────────┐     ┌─────────────────────────────────────────┐
  │ Unified Diagnostics & Logs      │     │ Private Torrenter Worker  [SHA-256] │     │ Installed Tlamatini           [EN & ES] │
  │ Tlachihuani.log (256 MB × 40)   │     │ CPython 3.12.10 · libtorrent 2.1.1  │     │ HKCU\Software\XAIHT\Tlamatini           │
  │ Roslyn TraceWeaver · masked     │     │ SQLite WAL · 4-slot FIFO magnets    │     │  • Chat engine, Ask Execs & cancel      │
  │ Quiet idle · 4-worker See log   │     │ 180s stall rotation → Downloads\... │     │  • Panels: Torrenter & Remote-Control   │
  └─────────────────────────────────┘     └─────────────────────────────────────┘     │  • Agent templates & Python (.flw)      │
                                                                                      │  • Configured Ollama models (.fpmt)     │
  ╭───────────────────────────────────────────────────────────────────────╮           │                                         │
  │ Flow Pills Window (--flow-pills)      [USER SESSION · NOT SESSION 0]  │  Per-run  │                                         │
  │ .flw agentic flows & .fpmt prompt pills · up to 256 concurrent runs   ├───Jobs───►│  (Independent lifetime · never owned    │
  │ RAM-aware admission · Kill-on-close Job Objects · Live log tails      │  & Ollama │   by Windows SCM services)              │
  ╰───────────────────────────────────────────────────────────────────────╯           └─────────────────────────────────────────┘
```

</details>

| Plane | Binary / Service | Identity & Startup | Endpoint / Responsibility | Reference |
| :--- | :--- | :--- | :--- | :--- |
| **Primary service** | `Tlachihuani` (`Service/Tlachihuani.Service.exe`) | `NT SERVICE\Tlachihuani` · Manual | Core lifecycle, heartbeat and ACL named pipe (v1) | [Lifecycle](docs/lifecycle.md) |
| **Jobs host** | `Tlachihuani.Jobs` (`Jobs/Tlachihuani.Jobs.exe`) | `NT SERVICE\Tlachihuani.Jobs` · Manual | Provider registry, private Torrenter worker and REST/SSE facade on `127.0.0.1:12064` | [Torrenter](docs/torrenter.md) |
| **Remote-Controller** | `Tlachihuani.RemoteController` (`RCServer/Tlachihuani.RC.Server.exe`) | `NT SERVICE\Tlachihuani.RemoteController` · Manual (opt-in for Start Services) | Live EN/ES RC web client, device grants, sessions, durable receipts and schedules on `127.0.0.1:12481` | [Remote-Controller](docs/remote-controller.md) |
| **XT companion** | `Tray/Tlachihuani.Tray.exe` | Signed-in Windows user · Unelevated | System tray, Services & RC panels, unified `Tlachihuani.log`, launch broker and outgoing `xaiht.org` link | [Panel & tray](docs/panel-and-tray.md) |
| **Flow Pills** | `Tray/Tlachihuani.Tray.exe --flow-pills` | Signed-in Windows user · Separate process | Visual `.flw` / `.fpmt` runner (up to 256 concurrent runs), isolated Windows jobs and live log tails | [Flow Pills](docs/flow-pills.md) |
| **HandGI** | `HandGI/Tlachihuani.HandGI.exe --service` | Signed-in Windows user · Separate process (opt-in for Start Services) | Hand signs in front of a camera control the sound volume; ready-made signs, nothing to train, and pictures are never saved or sent | [HandGI](docs/handgi.md) |
| **Native CLI** | `Control/Tlachihuani.Control.exe` | Caller session · Transient | Synchronous JSON/SCM controller (`status`, `events`, `start`, `stop`, `restart`) | [Control protocol](docs/control-protocol.md) |

---

## Quick start

> [!TIP]
> **Already installed?** Right-click the gray **XT** icon beside the clock and choose **Start Services**. When XT turns color, [http://127.0.0.1:12064/api/v1/health](http://127.0.0.1:12064/api/v1/health) returns `{"api_version":1,"engine":"ready"}` and **Panels → Torrenter Workspace** is ready in Tlamatini.

### Run Flow Pills

1. **Open the window.** Right-click **XT → Flow Pills…**, open **Show panel → Services… → Runtime → Flow Pills → Open**, or launch `Tlachihuani.Tray.exe --flow-pills [file.flw] [file.fpmt]`. You can also drag and drop `.flw` or `.fpmt` files onto the window. **Opening a file never runs it.**
2. **Inspect and run.** Review nodes, wires and settings on the live canvas (**Ctrl+wheel** zooms, drag or wheel pans, double-click fits). Press **F5** (or **Run**) to start; press **Shift+F5** (or **Stop**) to stop cleanly.
3. **Follow live & reply.** Watch **Run events**, a pill's **Transcript**, and every agent's own log tail in real time. When a flow pill asks a question, answer in the **Reply bar** (**Enter**, or **Yes / No** for a decision).
4. **Keep running in the background.** Closing the window while flows run lets you choose **Keep running** (reopen anytime from **XT → Flow Pills…**) or **Stop and close**. Configure `max_runs` (`1–4096`) and `reserve_mb` in `%LOCALAPPDATA%\XAIHT\Tlachihuani\FlowPills\settings.json`.

See [Flow Pills](docs/flow-pills.md) for limits, admission rules and agent discovery.

### Send a real prompt from your phone

1. **Start Tlamatini and Remote-Controller on the PC.** Sign in to your English or Spanish Tlamatini account and enable it in **Panels → Tlachihuani · Remote-Controller**. Start **Remote-Controller** from XT.
2. **Connect this PC once.** Sign in at **xaiht.org → Tlamatini-RC**, add your computer to generate a one-time `xrc_` key, then paste it in **XT → Services… → xaiht.org → Connect…**.
3. **Approve the exact account.** Open **XT → Services… → xaiht.org → Accounts…**, check the English or Spanish account you want to expose, and save. Nothing is selected automatically.
4. **Chat from your phone.** Open **xaiht.org → Tlamatini-RC**, choose your computer and approved account, and send a prompt. Refresh keeps the existing conversation; full answers larger than 8 MB are refused with a named reason (`response_too_large`).

**Ask Execs** requests approval for selected protected actions (such as Executer), not every tool; Monitor Netstat is a read-only exemption. See [setup and privacy](docs/xaiht-link.md) and the [actual phone acceptance record](docs/verification/2026-11-23-hosted-prompts.md).

### First download

1. **Open your workspace.** Choose **Panels → Torrenter Workspace** in Tlamatini, or **Services… → Runtime → REST workspace → Open** in XT. XT always opens the Tlamatini login page first so the queue shown belongs strictly to the account that signs in—even if the browser was signed in elsewhere. When both editions are available, choose the edition, then sign in.
2. **Inspect metadata.** Select **Add torrent**, paste a magnet URI or choose a `.torrent` file, then select **Inspect metadata**. Jobs resolves up to four magnets at once; additional magnets queue in FIFO order and start their 180-second peer timeout only when a slot opens.
3. **Start the transfer.** Pick the files you want and choose **Download selected files**. You can close the browser or Tlamatini while `Tlachihuani.Jobs` continues downloading.
4. **Verify your files.** Select a completed transfer and open **Verified output** for its saved folder and per-file SHA-256 checksums. Files are delivered atomically to **Downloads → Torrenter** (or the application's `content_generated` fallback if Windows refuses delivery there).

> [!NOTE]
> **Remove record** keeps downloaded files on disk. To delete files permanently, choose **Erase files…**, review the exact file list, and approve the operation in XT.

---

## Tray controls

**Remote-Controller** (`Tlachihuani.RemoteController`) is managed independently as its own service plane. It is **Manual / Stopped** on installation, joins **Start Services** only by explicit opt-in in its Network tab, and is always included in **Stop Services**. Torrenter stays isolated in its own Jobs plane.

| What you want to do | Where to go | What happens |
| :--- | :--- | :--- |
| Bring Torrenter online | **XT → Start Services** | Starts `Tlachihuani`, `Tlachihuani.Jobs` (and RC or HandGI if opted in). The icon turns color when services and the API are ready. |
| Check a slow download | **XT → Show panel → Services…** | Inspect Jobs, engine and web readiness, or open the workspace for progress, trackers and peers. |
| Run `.flw` flows & `.fpmt` pills | **XT → Flow Pills…** | Opens the Flow Pills window in its own process; runs up to 256 flows concurrently with live logs. |
| Control the volume with hand signs | **Services… → Runtime → HandGI** | Start, Stop, Pause or Turn on hand control (or press `Ctrl+Alt+H`); **Signs…**, **Camera…** and **Try it…** open HandGI's own windows. See [HandGI](docs/handgi.md). |
| Manage Tlamatini-RC locally | **XT → Remote-Controller…** | Start/stop/restart RC, invite & approve browsers by fingerprint, edit permissions and open the local RC client. |
| Connect to xaiht.org | **Services… → xaiht.org → Connect…** | Saves your `xrc_` key (DPAPI-protected) and opens the outgoing HTTPS link. See [xaiht.org guide](docs/xaiht-link.md). |
| Allow phone prompts | **Services… → xaiht.org → Accounts…** | Explicitly approve an English or Spanish Tlamatini account for real answers, approvals and cancellation. See [account setup](docs/xaiht-link.md#allow-an-account-and-send-a-real-prompt). |
| Inspect unified logs | **Services… → See log** | Opens `Tlachihuani.log` inside XT, pauses on scroll/select, and searches across all retained log files in parallel. See [log viewer guide](docs/log-viewer.md). |
| Stop background work | **XT → Stop Services** | Ends HandGI and Flow Pills at once and stops RC, Jobs (with owned workers) and the primary service, ending any still running after three seconds. The gray XT icon remains available. |
| Close only the tray | **XT → Exit tray** | Closes XT. When any service is running, the menu reads **Exit tray (service keeps running)**. |

<details>
<summary><b>CLI switches and keyboard shortcuts</b></summary>

#### Command-line entry points

| Command | Behavior |
| :--- | :--- |
| `Tlachihuani.Tray.exe` | Starts the XT tray companion in the current user session. |
| `Tlachihuani.Tray.exe --show` | Starts XT (or brings the running instance forward) and opens the service overview. |
| `Tlachihuani.Tray.exe --remote-controller` | Opens the Remote-Controller panel directly. |
| `Tlachihuani.Tray.exe --flow-pills [files…]` | Opens the Flow Pills window and loads any supplied `.flw` / `.fpmt` files without executing them. |
| `Tlachihuani.HandGI.exe [--service] [--camera-off] [--show PAGE]` | Starts HandGI for the signed-in user, or hands over to the one already running. `--service` opens no window; `--show` opens `signs`, `camera`, `try` or `expert`; no arguments opens the signs. `--camera-off` starts without the camera. |
| `Tlachihuani.Control.exe [status \| events [--cursor N] \| start \| stop \| restart]` | Runs a single synchronous JSON/SCM control command against the primary service. |

#### Keyboard shortcuts

* **Flow Pills window:** `Ctrl+O` open files · `F5` run selected flow · `Shift+F5` stop followed run · `Ctrl+Tab` / `Ctrl+Shift+Tab` switch flows · `Ctrl+wheel` zoom canvas · double-click fit canvas.
* **HandGI:** `Ctrl+Alt+H` pauses or turns on hand control from any program.
* **See log viewer:** `Ctrl+F` focus search · `F3` / `Shift+F3` find newer/older across retained logs · `Ctrl+Home` beginning · `Ctrl+PageUp` / `Ctrl+PageDown` page through file · `Ctrl+End` resume live follow · `Esc` cancel search or close.

</details>

---

## Security and current boundaries

| Area | Implemented guarantee | Current 1.0.1 boundary |
| :--- | :--- | :--- |
| **Service privilege & startup** | Three separate own-process virtual accounts (`NT SERVICE\*`), Manual/stopped on install and reboot. Optional login shortcut starts XT only—never services or downloads. | Clean-Windows installation and reboot acceptance of the 1.0.1 package remain open gates ([1.0.1 notes](docs/releases/v1.0.1.md)). |
| **Closed operation surface** | ACL-protected local named pipes and loopback-only HTTP (`Host`, `Origin`, `Sec-Fetch-Site`, CSP, `no-store`, rate limiting). Never executes arbitrary commands, executable paths, or browser-supplied PIDs. XT's **Stop Services** ends a service only through the process Windows names for it, after checking its executable against the installation. | `Tlachihuani.Elevated` force-stop helper and the separate **Panels → Tlachihuani** lifecycle modal remain planned design targets. |
| **xaiht.org phone prompts** | Outgoing-only HTTPS long polling (no open ports/firewall rules), DPAPI-protected computer keys, explicit per-account immutable bindings, durable idempotency and secret-masked logs (prompt/answer bodies excluded from XT logs). | Uses ordinary authenticated HTTPS where **xaiht.org is trusted with plaintext**—not end-to-end or post-quantum encrypted. Real-phone denial/no-execution and cancellation were not completed before tests stopped; the wording-only website candidate was built but not promoted ([23 Nov evidence](docs/verification/2026-11-23-hosted-prompts.md)). |
| **Local Remote-Controller** | Loopback-only (`127.0.0.1:12481`), P-256 non-exportable browser signing keys, 5-minute single-use invitations with full fingerprint comparison, DOMPurify HTML sanitization, `outcome_unknown` on disconnect instead of replay. | Direct LAN/VPN/Internet transport, ML-KEM-1024 / ML-DSA-87 post-quantum profile (`post-quantum-required` fails closed) and phone audio capture remain disabled ([Remote-Controller](docs/remote-controller.md)). |
| **Flow Pills & Torrenter** | Flow Pills uses only the installed Tlamatini's agents/Python/models and terminates processes solely via per-run Windows Job Objects. Torrenter sanitizes metainfo paths/symlinks, verifies SHA-256 after disk-complete pause, and requires native XT approval to erase files. | Flow Pills passed visible source runs with installed Tlamatini agents; a real Ollama-answered pill and Terminater steps are not yet verified ([Flow Pills](docs/flow-pills.md#verification--25-november-2026)). |
| **HandGI** | Runs only in the signed-in user's session. XT reaches it through a pipe only that user can open, whose server must be the process `service.json` names, and ends it only after checking that record's PID, start time and executable. Camera pictures are never saved or sent. | In the source after 1.0.1 and not yet committed. Verified in visible source runs with the camera off and installed from a locally built 1.0.1 package; real signs on the installed package and a computer without a supported NVIDIA card are not yet tested ([27 Nov evidence](docs/verification/2026-11-27-handgi.md)). |
| **Packaging & verification** | Self-contained offline ZIP with `manifest.json`, `SHA256SUMS.txt` and `.zip.sha256`; MSI cabinet payloads are verified byte-for-byte against published files. | Current builds are **unsigned**; Authenticode signing requires a publisher certificate. |

---

## Installation

Requires **Windows 11 x64**. The offline package bundles private **.NET 10** and hash-pinned **CPython 3.12.10 + libtorrent 2.1.1** runtimes—no separate SDK or runtime installation is needed. Setup registers three independent services (`Tlachihuani`, `Tlachihuani.Jobs`, `Tlachihuani.RemoteController`), installs HandGI with its models, and runs **XT** in the signed-in Windows session.

### Install with the Windows wizard

1. **Extract the release archive.** Download `Tlachihuani-1.0.1-windows-x64.zip` (and its `.zip.sha256`) from the GitHub release, right-click → **Extract All**, and open **START-HERE.html** for the offline illustrated guide. If Tlachihuani is already installed, even 1.0.0, [uninstall it](#uninstall-and-keep-your-downloads) first: the installer does not install over an existing installation. Your downloads and job journal remain on disk.
2. **Run Installer.exe as your normal Windows operator.** Choose whether **XT should appear at Windows sign-in** (applies to all users on the PC). Select **Review**, verify version **1.0.1**, operator, destination and startup choice, then choose **Install** and approve the Windows administrator prompt.
3. **Locate XT.** After **Installation complete**, XT opens gray in your normal session (check the hidden-icons arrow beside the clock). **Start menu** and **Desktop** shortcuts always reopen it. If the wizard itself was launched elevated, launch XT from either shortcut in your normal desktop session.
4. **Start services on demand.** All three services remain **Stopped / Manual** after installation and reboot. Signing in opens XT only if you opted in and never starts services or downloads. Choose **XT → Start Services** when you want them online.

### Uninstall and keep your downloads

1. In XT, choose **Stop Services**, wait for the icon to turn gray, then choose **Exit tray** (and close XT in any other signed-in session).
2. Remove Tlachihuani via **Control Panel → Programs → Programs and Features → Tlachihuani → Uninstall**, **Settings → Apps → Installed apps**, **Uninstall Tlachihuani** in Start, or the version-matched **Uninstaller.exe → Review → Uninstall**, then approve UAC.
3. Your delivered files, protected installation identities and Jobs journal remain on disk. Reinstall as the same Windows operator to reuse them. If Windows requests a restart, restart before reinstalling.

> [!IMPORTANT]
> **Legacy PowerShell installation?** The MSI wizard refuses to overwrite script-managed deployments. Follow the [Windows packaging guide](packaging/windows/README.md) and [Jobs operating guide](docs/torrenter.md#windows-installation) first. See the [Windows setup guide](packaging/setup/README.md) for prerequisites, recovery, MSI logging and visible acceptance harnesses.

<details>
<summary><b>Troubleshooting first launch &amp; failed service actions</b></summary>

* **XT icon missing after setup:** Open **Tlachihuani XT** from Start or Desktop, or press **Win+R** and run:
  ```bat
  "C:\Program Files\XAIHT\Tlachihuani\Tray\Tlachihuani.Tray.exe" --show
  ```
* **Overview shows Stopped on first launch:** That is expected—services install Manual/stopped. Right-click the gray XT icon (check the hidden-icons overflow beside the clock) and choose **Start Services**. Confirm [http://127.0.0.1:12064/api/v1/health](http://127.0.0.1:12064/api/v1/health) returns `{"api_version":1,"engine":"ready"}`.
* **Service action failed:** The diagnostic dialog lets you inspect and **Copy report**. The latest redacted report is also saved beside `Tlachihuani.log` as `Last-service-error.txt` in `%LOCALAPPDATA%\XAIHT\Tlachihuani\`.

</details>

---

## Build from source

Create the complete, verified offline distributable from a visible Command Prompt at the repository root:

```bat
Build-Release.cmd
```

Or run `.\Build-Release.cmd` in a visible foreground PowerShell console (see [visible development execution](docs/development-execution.md)). Each run selects the highest numeric local `vX.Y.Z` tag (for 1.0.1, `v1.0.1`; suffixed tags like `v1.0.1-rc.1` are ignored), stamps product metadata and internal project locks, runs checks, publishes the **six** self-contained x64 components (`Service`, `Control`, `Tray`, `Jobs`, `RCServer`, `HandGI`) into fresh staging, builds and verifies the WiX MSI and ZIP allowlist, and writes:

* `dist\Tlachihuani-<version>-windows-x64.zip`
* `dist\Tlachihuani-<version>-windows-x64.zip.sha256`

The builder never commits, creates tags, or uploads anything; logs and source provenance stay under `artifacts/release-builds/`. See the [release command guide](docs/release-building.md) for build-machine prerequisites. The first build that includes HandGI downloads its graphics-card packages into the builder's own package folder; if that restore stalls, see [when restore stalls](docs/release-building.md#when-restore-stalls).

<details>
<summary><b>PowerShell component commands, tracing knobs &amp; solution layout</b></summary>

Use the .NET SDK pinned in [global.json](global.json) inside a visible foreground PowerShell console at the repository root:

```powershell
.\scripts\Build.ps1 -Mode Check
.\scripts\Build.ps1 -Mode Publish
.\scripts\Build-Setup.ps1
.\artifacts\publish\Control\Tlachihuani.Control.exe status
.\artifacts\publish\Tray\Tlachihuani.Tray.exe --show
```

* **Build modes:** `Check` restores locked dependencies, builds Release and runs the native + RC test suites; `Publish` outputs the six self-contained win-x64 components (`Service`, `Control`, `Tray`, `Jobs`, `RCServer`, `HandGI`) plus the pinned Python/libtorrent runtime under `artifacts/publish/`; `Build-Setup.ps1` packages the wizards and MSI.
* **Compile-time tracing (`tools/Tlachihuani.TraceWeaver`):** Wired in via `Directory.Build.targets` without altering source files (`#line` preserved). Override with `-p:TlachihuaniTrace=methods` or `-p:TlachihuaniTrace=off`, and adjust runtime verbosity with `TLACHIHUANI_LOG_LEVEL` (`trace`, `debug`, `info`, `warn`, `error`, `off`).
* **Solution layout (`Tlachihuani.slnx`):**
  * `src/Tlachihuani.{Contracts,Core,Windows,Client}` — shared protocol, lifecycle, SCM/ACL and pipe libraries
  * `src/Tlachihuani.{Service,Control,Jobs,RC,RC.Server,Tray,FlowPills,HandGI,HandGI.Core}` — the three services, CLI, tray companion, Flow Pills engine and HandGI
  * `src/Tlachihuani.RC.Cloud` — cross-platform development lab host (not part of the Windows MSI)
  * `workers/torrent` & `workers/flowpills` — private Python libtorrent worker and standalone `.flw` compiler
  * `tools/Tlachihuani.TraceWeaver` — Roslyn statement/method trace weaver
  * `packaging/setup` & `packaging/windows` — WiX 4 MSI wizards and PowerShell deployment scripts

</details>

<details>
<summary><b>Hosted Tlamatini-RC development lab (isolated HTTPS loopback)</b></summary>

Separate from the live outgoing HTTPS `xaiht.org` prompt channel, `Tlachihuani.RC.Cloud` provides an isolated HTTPS loopback lab for the hosted English/Spanish client, real OIDC sign-in, bounded WebSocket relay, and Linux container checks:

* [English validation guide](docs/hosted-development.md)
* [Guía de validación en español](docs/hosted-development.es.md)

This development profile does not enable Internet access, modify installed applications, or deploy to `xaiht.org`. Production end-to-end/post-quantum encryption and trusted-client gates stay closed; Torrenter remains independent.

</details>

---

## What's new in 1.0.1

Tlachihuani **1.0.1** fixes **Start Services** when the Torrenter engine is slow to answer after a start. A slow answer now means busy, not dead: Jobs keeps the engine and the web workspace running instead of restarting them until XT's readiness check gave up, and the engine traces far less. See the [1.0.1 release notes](docs/releases/v1.0.1.md).

**In the source after 1.0.1 (not yet committed):** [HandGI](docs/handgi.md) hand signs, a **Stop Services** that ends every service at once, and **Services… → Runtime** sections that give every service the same light, status line and buttons. See the [27 November verification](docs/verification/2026-11-27-handgi.md).

### 1.0.0 · first official release

Tlachihuani **1.0.0** is the first official release after pre-releases `0.1.0` through `0.40.1`. See the [1.0.0 release notes](docs/releases/v1.0.0.md).

* **Flow Pills:** **XT → Flow Pills…** opens, draws and executes Tlamatini `.flw` agentic flows and `.fpmt` prompt flow pills—up to 256 at once—with owned Windows jobs, live log tails and an interactive Reply bar ([Flow Pills](docs/flow-pills.md)).
* **Prompts from your phone:** Send typed prompts through **xaiht.org → Tlamatini-RC** to an approved local Tlamatini account with real answers, selective **Ask Execs** approvals and cancellation (`0.40.0`). Answers over 8 MB fail cleanly with `response_too_large` (`0.40.1`).
* **Accurate startup & diagnostics:** **Start Services** confirms readiness without false timeouts, and failed service actions provide a resizable, copyable diagnostic report saved to `Last-service-error.txt` (`0.40.0`).
* **Stable, multi-file log browsing:** **See log** pauses on scroll or selection, reads bounded 256 KiB pages through log rotation, and searches all retained log files in parallel (`0.39.0`; [log viewer guide](docs/log-viewer.md)).

<details>
<summary><b>Pre-release milestones carried forward (0.3.3 – 0.38.0)</b></summary>

#### 0.38.0 · Unified trace logging & quiet idle loops
* Every process—XT, the three services, the Torrenter engine, setup and scripts—writes secret-masked statement-level traces merged into `Tlachihuani.log`.
* Polling loops run inside `TraceLog.Pass` / `tracing.pass_scope`, suppressing identical passes so an idle system writes one line per loop every ten minutes. See [0.38.0 notes](docs/releases/v0.38.0.md).

#### 0.37.0 · xaiht.org link & `Tlachihuani.log`
* Connect this PC to **xaiht.org → Tlamatini-RC** from **Services → xaiht.org** over outgoing HTTPS with no open ports; diagnostic messages arrive as JSON in `Tlachihuani.log`. See [0.37.0 notes](docs/releases/v0.37.0.md) and [xaiht.org link guide](docs/xaiht-link.md).

#### 0.36.0 · Remote-Controller & account-scoped workspace sign-in
* Added the third Windows service, `Tlachihuani.RemoteController`, with per-account XT permissions and synchronized approvals across English, Spanish and RC clients.
* XT's **Open workspace** opens the Tlamatini login page first so the displayed queue always matches the account that just signed in. See [0.36.0 notes](docs/releases/v0.36.0.md).

#### 0.3.3 · Resilient Torrenter queues, recovery & 12-column workspace
* Unfinished transfers stalled for 180 s yield their slot when eligible queued work is blocked (retaining partial files; minimum 60 s retry wait; respecting user Paused/Stopped intent).
* Clear **Start / Resume / Recheck** recovery, completed state preserved when peer sessions stop, and historical errors separated from active or delivery failures.
* Sort all twelve data columns (Shift-click multi-sort), combine search and filters, customize columns/density per account, and verify SHA-256 hashes after libtorrent's disk-complete pause alert.
* See [0.3.3 notes](docs/releases/v0.3.3.md) (64 automated checks, 48 visible browser checks and four real local-peer transfers in source verification).

</details>

---

## Documentation

| For operators & users | For developers & verification |
| :--- | :--- |
| [1.0.1 release notes](docs/releases/v1.0.1.md) · [1.0.0](docs/releases/v1.0.0.md) · [0.38.0](docs/releases/v0.38.0.md) · [0.37.0](docs/releases/v0.37.0.md) · [0.36.0](docs/releases/v0.36.0.md) · [0.3.3](docs/releases/v0.3.3.md) | [Architecture & process planes](docs/architecture.md) |
| [Flow Pills guide](docs/flow-pills.md) · [HandGI hand signs](docs/handgi.md) | [Controller commands & JSON protocol](docs/control-protocol.md) |
| [xaiht.org link, phone prompts & Tlachihuani.log](docs/xaiht-link.md) | [Release building guide](docs/release-building.md) · [Visible execution policy](docs/development-execution.md) |
| [Exploring Tlachihuani.log (log viewer)](docs/log-viewer.md) | [Tlamatini integration design](docs/tlamatini-integration.md) |
| [Remote-Controller (Tlamatini-RC)](docs/remote-controller.md) · [Mobile workspace parity](docs/remote-mobile-parity.md) | [Implementation status & verification evidence](docs/implementation-status.md) |
| [Torrenter workspace & REST API v1](docs/torrenter.md) | [Hosted RC lab (English)](docs/hosted-development.md) · [Guía en español](docs/hosted-development.es.md) |
| [Panel and tray](docs/panel-and-tray.md) · [Service lifecycle](docs/lifecycle.md) | [HandGI and the 1.0.1 package (27 Nov)](docs/verification/2026-11-27-handgi.md) · [Real prompts (23 Nov)](docs/verification/2026-11-23-hosted-prompts.md) · [Log viewer (22 Nov)](docs/verification/2026-11-22-log-viewer.md) · [xaiht.org link (19 Nov)](docs/verification/2026-11-19-xaiht-link.md) |
| [Windows setup & offline ZIP](packaging/setup/README.md) · [Privileges & local IPC](docs/security-and-installation.md) · [Predeployment checklist](docs/remote-control-predeployment-checklist.md) | [Workspace sign-in (17 Nov)](docs/verification/2026-11-17-workspace-signin.md) · [Remote-Controller (13 Nov)](docs/verification/2026-11-13-remote-controller.md) · [Torrenter recovery (10 Nov)](docs/verification/2026-11-10-torrenter-recovery.md) |

<details>
<summary><b>Under the hood · Local API discovery, repositories &amp; contributor references</b></summary>

The browser workspace and Tlamatini's wrapped Torrenter agent share the same account-scoped Jobs API. With `Tlachihuani.Jobs` running, [open local API discovery](http://127.0.0.1:12064/api/v1). Both Tlamatini editions currently implement **Panels → Torrenter Workspace**, **Panels → Torrenter Sources**, and **Panels → Tlachihuani · Remote-Controller**; the separate **Panels → Tlachihuani** lifecycle modal remains a design target.

| Reference | Location |
| :--- | :--- |
| Authoritative checkout | `C:\Development\XAIHT\repo\Tlachihuani` |
| Tlachihuani on GitHub | [github.com/angelahack1/Tlachihuani](https://github.com/angelahack1/Tlachihuani) |
| Tlachihuani Git origin | `https://github.com/angelahack1/Tlachihuani.git` |
| Tlamatini source checkout | `C:\Development\XAIHT\Tlamatini` |
| Tlamatini Django application | `C:\Development\XAIHT\Tlamatini\Tlamatini` |
| Tlamatini on GitHub | [github.com/XAIHT/Tlamatini](https://github.com/XAIHT/Tlamatini) |
| Verified upstream origin | `https://github.com/XAIHT/Tlamatini.git` |
| Design reference revision | `c18d50815cfe8b1681f805fd4e76c66fbaeabaf6` (`v1.78.0-4-gc18d5081`) |

For the next development session, read [AGENTS.md](AGENTS.md), [CLAUDE.md](CLAUDE.md), and the [session handoff](NEXT_SESSION.md). The [roadmap](docs/implementation-plan.md), [verification plan](docs/verification-plan.md), [project tree](docs/project-tree.md), [source review](docs/source-review.md), and [skill inventory](docs/skill-inventory.md) cover the rest of the checkout. Changes to either repository and publication require their own authorization.

</details>

---

## License

Released under the [MIT License](LICENSE) (Copyright © 2026 XAIHT). See [assets/branding/README.md](assets/branding/README.md) for provenance and usage notes regarding the original XAIHT **XT** emblem.

<p align="center">
  <a href="assets/branding/README.md"><img src="assets/branding/xt-preview.png" alt="Original XAIHT XT emblem" width="64" height="64" /></a><br />
  <b>Created by Angela López Mendoza · XAIHT</b><br />
  <a href="https://github.com/angelahack1/Tlachihuani">Tlachihuani</a> · <a href="https://github.com/XAIHT/Tlamatini">Tlamatini</a> · <a href="LICENSE">MIT License</a>
</p>
