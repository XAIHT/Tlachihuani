# Flow Pills

Flow Pills opens the flows Tlamatini makes, draws them as Tlamatini drew them, and runs
each one on its own, for as long as it runs:

- **Agentic flows (`.flw`)** from Tlamatini's Agentic Control Panel run with Tlamatini's
  own agents, exactly as its **Start** button runs them. A flow that never ends keeps
  running.
- **Flow pills (`.fpmt`)** from Tlamatini's Prompt Flow Panel run with the models
  Tlamatini is configured with, by the rules of its own runner.

Up to **256 flows** run at once by default, each on its own thread, with its own agent
processes and its own logs, tailed independently. Flow Pills replaces the unfinished
Tlamatini-FlowPills application, which could open `.flw` files but never ran them and
never opened `.fpmt` files.

## Open it

- **XT tray → Flow Pills…**, or **Show panel → Services… → Flow Pills…**.
- From a command line: `Tlachihuani.Tray.exe --flow-pills [file.flw] [file.fpmt] …`

Flow Pills runs in your Windows session, not as a Session 0 service: Tlamatini's agents
need your desktop, your profile and your models. There is one window per Windows user. A
second launch hands its files to the open window and brings it forward. **Opening a file
never runs it.**

It is a process of its own: exiting XT's tray does not stop your flows.

## The window

| Area | What it shows |
| :--- | :--- |
| Header | **Open…**, **Run**, **Pause** / **Resume**, **Stop** (**Stop now** while stopping), **Fit** |
| FLOWS | Every open flow, its nodes and each node's settings. Right-click a flow to run, reload, show it in its folder or close it. |
| INSPECTOR | The selected flow or node: file, kind, nodes, wires, runs, and a node's state in the run you follow. It refreshes while flows run. |
| Drawing | One tab per flow. Agents keep the Agentic Control Panel's colors, connectors and wires; pills keep the Prompt Flow Panel's shapes, Yes/No outputs and comment bubbles. The followed run colors it live. **Ctrl+wheel** zooms, dragging or the wheel pans, a double-click fits, a click selects a node. |
| RUNS | Every run: number, flow, state, status, elapsed time, busy agents, peak memory and kind. Right-click to pause, resume, stop, open its folders or remove it from the list. |
| LOGS | The selected run's **Run events**, a pill's **Transcript**, and every agent's log, each followed on its own. **Follow the end** keeps the newest line visible; **Open file** opens the file itself. |
| Reply bar | Appears when a pill asks something: type an answer and press **Enter**, or choose **Yes** / **No** for a decision. |
| Footer | Flows running and waiting, the limit, free memory, what stays free for Windows, and the Tlamatini agents in use. **Runs folder** opens the records. |

Keyboard: **Ctrl+O** opens files, **F5** runs the selected flow, **Shift+F5** stops the
followed run, **Ctrl+Tab** and **Ctrl+Shift+Tab** move between flows.

## Agentic flows

**Run** compiles the flow with Tlamatini's wiring rules into a folder of its own,
launches its Starters (and FlowHypervisor when the flow has one), and from then on the
agents start one another exactly as they do under Tlamatini.

- **Pause** is Tlamatini's own pause: the running agents are recorded and ended;
  **Resume** starts them again, and they keep their logs and state.
- **Stop** runs the flow's **Ender**, as Tlamatini's Stop button does, and after at most
  20 seconds ends whatever is left. **Stop now** ends everything at once.
- The agents come from the Tlamatini that publishes itself in
  `HKCU\Software\XAIHT\Tlamatini`, run under that Tlamatini's Python, and read its
  `config.json`. Drives are never scanned.

Every run owns its processes: each agent starts inside the run's own Windows job, so
whatever it starts stays there too. Stopping a run ends exactly that run's processes.
After a run completes, anything the flow started on purpose (an application, a server)
keeps running, as it would under Tlamatini.

## Flow pills

A pill is checked with the same limits and the same refusal sentences as Tlamatini's
Prompt Flow Panel, then played with its rules: the step limit, pause between
operations, delays that count only active time, absolute scheduled times,
`{{last_output}}`, Yes/No decisions, user input, the last 16 history messages, **Feed** and
**Flush embeddings** and **Clean History**. Comments are never run.

- Prompts use Tlamatini's Ollama server, token and models from its `config.json`. The
  token goes only to that server.
- **Terminater** steps type into Tlamatini's visible terminals. They need Tlamatini
  running; without it, the step says so.
- A step with **Multi-Turn** or **ACPX** needs Tlamatini's own tool loop. Such a pill is
  refused before its first operation, naming the step.

## How many run at once

- At most **256** flows run at once; more wait in line, and each waiting run says why.
- The first flow always starts. Pills wait only for the count limit: their models run in
  Ollama.
- Each additional agentic flow starts only while free memory covers **192 MB** for its
  agents plus what stays free for Windows: 5% of the memory, at least 512 MB and at most
  2 GB.

`%LOCALAPPDATA%\XAIHT\Tlachihuani\FlowPills\settings.json` can set these. Every value is
optional, and a value written there is obeyed exactly:

```json
{
  "max_runs": 64,
  "reserve_mb": 1024,
  "python": "",
  "agents_root": ""
}
```

`max_runs` accepts 1–4096. `python` and `agents_root` choose another Tlamatini
interpreter and agents folder.

## Closing the window

While flows run or wait, closing the window asks:

- **Keep running**: the window hides and the flows go on. **XT → Flow Pills…** brings it
  back. When the last flow ends, the hidden window closes itself.
- **Stop and close**: every flow is stopped the way Tlamatini's Stop does. The window
  closes once they have ended and written their records, or after 30 seconds.
- **Cancel**: nothing changes.

A close from outside the window (Task Manager, signing out, shutting down) ends every
flow: their jobs end their processes.

## Where things are kept

Under `%LOCALAPPDATA%\XAIHT\Tlachihuani\FlowPills\`:

| Folder | Contents |
| :--- | :--- |
| `runs\{run}\` | The flow exactly as it ran, `events.log`, a pill's `transcript.log`, and `run.json` (how it ended) |
| `pools\{run}\` | The compiled agents, in Tlamatini's own pool layout |
| `inbox\` | Files a second launch asked the open window to load |

Flow Pills writes to `Tlachihuani.log` as `xt-flow-pills`.

## For maintainers

- The engine is `src/Tlachihuani.FlowPills`; the window is XT's flow-pills mode in
  `src/Tlachihuani.Tray` (`FlowPillsHost`, `FlowPillsForm`, `FlowCanvas`). The compiler
  `workers/flowpills/flowpills_compile.py` is a standalone port of Tlamatini's Flow
  Compiler; `PromptFlowValidator` mirrors `validate_flow`; `acp-palette.json` holds the
  Agentic Control Panel's colors. Keep them in step with Tlamatini.
- Opening, reloading and drawing never run anything. Runs use only agent templates of the
  Tlamatini installation found as described above.
- End processes only through the run's job, by membership at that moment; never by image
  name or a stale PID.
- A run can be **Stopped** a moment before it has written `run.json`. Wait for
  `FlowRegistry.Idle` (every run finished and settled) before closing or exiting.
- Code polled several times a second is `trace-off`, and the window keeps its footer, log
  and inspector out of UI tracing, so following runs does not flood `Tlachihuani.log`.

## Verification · 25 November 2026

Source checks only; no installed build was tested.

- `scripts\Build.ps1 -Mode Check`: 13 checks passed, including *Flow Pills: Tlamatini's
  flow rules, independent log tails, owned agent processes and machine limits* (validator,
  decisions, documents, log tails, job ownership, admission, and a pill against a stand-in
  Ollama server). Remote-Controller checks: 318/318.
- The real window, in visible runs with the agents of `C:\Tlamatini`: an agentic flow
  completed; an endless flow ran until stopped; flows ran concurrently; a pill asked for
  input and took the Yes branch of a decision; a pill whose Ollama was not running failed
  with that reason; **Keep running**, reopening and **Stop and close** behaved as
  described, with the run ended before the window closed, no agent process left behind
  and `run.json` reading `Stopped`.
- A publish with the release settings contains `Tlachihuani.FlowPills.dll` and
  `flowpills\flowpills_compile.py`.

Not yet verified: a pill answered by a real model, Terminater steps, 256 real flows at
once (the limit and admission rules were checked, not that load), and an installed MSI.
