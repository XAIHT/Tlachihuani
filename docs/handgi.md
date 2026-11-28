# HandGI · hand gestures

HandGI lets a person control Windows with simple hand signs in front of a camera. Nothing has to
be set up or taught first: it comes with ready-made signs. It is part of Tlachihuani and runs as a
service of the signed-in user, because the camera, the sound volume and its keyboard shortcut
belong to that user's session (never Session 0).

## Using it

XT → **Show panel** → **Services…** → **Runtime** → **HandGI**:

| Button | What it does |
|---|---|
| **Start** / **Stop** | Starts or stops HandGI. Closing a HandGI window never stops it. |
| **Pause** / **Turn on** | Pauses hand control (HandGI still watches and shows what it sees, but changes nothing) or turns it back on. **Ctrl+Alt+H** does the same. |
| **Signs…** | What each sign does: volume up, volume down, mute, a volume lever, or nothing. Turn a sign off, or teach your own by showing it three times. |
| **Camera…** | Which camera, mirror the picture, the volume display, start paused, start with the other services, or turn the camera off. |
| **Try it…** | Shows the signs HandGI recognises, as you make them. |

Ready-made signs: **Thumb up** turns the volume up, **Thumb down** turns it down and a **Closed
fist** mutes or unmutes. **Open palm**, **Pointing up**, **Victory** and **I love you** do nothing
until you choose an action for them. HandGI works only on this computer; it never saves or sends
pictures of you. The expert view (from the Signs window) keeps the full engine tools and turns
the camera on.

The Signs page gives each sign's name and hint the lines they need and scrolls when the window
is small, so nothing is cut off. The volume display appears at the bottom of the main screen and
never takes the keyboard from the program you are using.

**Start Services** starts HandGI only when Camera… says **Start with the other services** (off
when installed). **Stop Services** ends HandGI at once, together with every other service.

## What it needs

- A camera.
- An NVIDIA graphics card, GTX 1060 or newer (DirectX 12). HandGI runs its models on the card
  through DirectML; no CUDA installation is needed. **Automatic** picks the best supported card.
- On a computer without one, HandGI says so (*No supported NVIDIA GPU (GTX 1060 or newer) was
  found*) and does not start the camera. The expert view's setup can choose **CPU compatibility
  mode** instead: slower, at most four processor threads, and always one core left for the window.
- Its three models ship with it under the Apache License 2.0; see
  [assets/handgi](../assets/handgi/README.md) for their sources and hashes.

## How XT and HandGI work together

- **Folder:** `%LOCALAPPDATA%\XAIHT\Tlachihuani\HandGI\` holds `service.json` (the running
  process: PID, start time and executable), `settings.json` and the taught signs (`profiles\`).
  A standalone HandGI's settings and signs are copied there once, never moved or deleted.
- **Pipe:** one per user and session, which only that user can open. XT sends `status`, `arm`,
  `disarm`, `show signs|camera|try|expert` or `stop`; any other request is refused before it is
  sent. A reply counts only when the pipe's server is the process `service.json` names.
- **Stop Services** ends the recorded process after checking its start time and executable, so a
  recycled PID or another program is never touched.
- **Command line:** `Tlachihuani.HandGI.exe [--service] [--camera-off] [--show signs|camera|try|expert]`.
  A second launch hands over to the running HandGI and leaves. The engine's own tools stay
  available: `selftest`, `gpus`, `cameras`, `formats`, `inspect`, `run`.
- **Log:** everything HandGI does is written to `Tlachihuani.log` as component `handgi`
  (its tools as `handgi-tool`). XT's **See log** shows it.
- **Installed** in `…\XAIHT\Tlachihuani\HandGI\` with its models. A development build of XT uses the
  HandGI build of the same configuration; `TLACHIHUANI_HANDGI_EXE` names another for tests.

## Checks

`tests\desktop\ServicesAndHandGI.ps1` runs, in a visible window, the unit checks for Stop Services,
a real HandGI and a real Flow Pills in test folders of their own (HandGI with its camera off), and
the layout audits of the Services panel and of HandGI's windows, at the desktop DPI and at 96 DPI.
Tlamatini's Shoter agent takes whole-desktop photos at each step. It refuses to run on a locked screen.

`Tlachihuani.HandGI.Checks.exe --layout desktop|96dpi` opens, on the main screen, Signs, Camera and
Try it at their usual and smallest sizes and again after being made smaller and bigger, Teach a sign
(also its camera-off note), the Remove question and the volume display, which must not take the
keyboard. They meet the rules XT's windows meet (`DesktopChecks.cs`): no cut
or covered text, check boxes and every drop-down choice shown in full, nothing scrolling sideways,
and at the usual size every sign showing without scrolling. The camera stays off: the check never
creates HandGI's engine and does not open the expert view, which turns the camera on.

Dated results: [27 November 2026](verification/2026-11-27-handgi.md) (the three window fixes, the
whole visible suite and the 1.0.1 package with HandGI).
