# HandGI

Hand signs in front of a camera control the sound volume. `Tlachihuani.HandGI.exe`
runs in the signed-in user's session, never in Session 0, because the camera, the
sound and its **Ctrl+Alt+H** shortcut belong to that session. Read
[HandGI](../../docs/handgi.md) for what it does and how XT controls it.

- [Program.cs](Program.cs) reads the command line (`--service`, `--camera-off`,
  `--show signs|camera|try|expert`); a second launch hands over to the running HandGI
  and leaves. [Tools.cs](Tools.cs) holds the engine tools: `selftest`, `gpus`,
  `cameras`, `formats`, `inspect` and `run`.
- [HandGIService.cs](HandGIService.cs) is the per-user process: its `service.json`
  record, the camera, the signs and the volume.
- [ControlPipe.cs](ControlPipe.cs) is the pipe only this user can open. XT sends
  `status`, `arm`, `disarm`, `show signs|camera|try|expert` or `stop`.
- [SettingsForm.cs](SettingsForm.cs), [TeachForm.cs](TeachForm.cs),
  [ConfirmForm.cs](ConfirmForm.cs) and [SignLearning.cs](SignLearning.cs) are the Signs,
  Camera and Try it pages, teaching a sign and the questions they ask. They use XT's
  window design (`StudioForm.cs`, linked from `Tlachihuani.Tray`).
- [HotkeyWindow.cs](HotkeyWindow.cs) owns Ctrl+Alt+H; [LogBridge.cs](LogBridge.cs)
  writes HandGI's messages to Tlachihuani.log as component `handgi`;
  [LegacyImport.cs](LegacyImport.cs) copies a standalone HandGI's settings and signs
  from `%LOCALAPPDATA%\HandGI` once, without moving or deleting them.
- [Expert](Expert) holds the expert view (setup, trainer and tester), which turns the
  camera on, and the volume display ([VolumeOsd.cs](Expert/VolumeOsd.cs)), which never
  takes the keyboard.

The engine itself is [Tlachihuani.HandGI.Core](../Tlachihuani.HandGI.Core/README.md).
Its checks are [Tlachihuani.HandGI.Checks](../../tests/README.md) (windows layout,
camera off) and `tests/desktop/ServicesAndHandGI.ps1`.
