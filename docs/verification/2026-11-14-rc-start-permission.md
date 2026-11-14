# Development fix: approved-browser Start permission

14 November 2026 · Tlachihuani 0.3.3 pre-release.

The reported disabled Start button was caused by the current browser having only
Conversation and View status permissions. Stopping the main application did not
remove a launch permission; it had not been granted. The client failed to explain
this and XT could not edit an existing browser's grant.

The development source now includes **Connections → Change permissions** in XT.
The editor identifies the browser, account and edition, preserves selected scopes,
and offers explicit Save/Cancel. Start and Stop remain independent. Saving uses a
trusted local operation with a revision check, preserves the device key and account,
ends old sessions/tickets, and requires reconnection without another invitation.
Removed authority cancels queued work and marks dispatched work uncertain; it does
not falsely report that an external action stopped. Existing schedules are retained
and cannot dispatch without their current permissions.

The RC client now explains unavailable Start/Stop controls and the correction path.
The server's packaged wwwroot and matching canonical files in
C:/Development/XAIHT/Tlamatini-RC/public were both updated, including the shell cache
version. This task does not complete the separate source-directory merge.

Development validation:

- Locked restore and solution build passed.
- [285/285 core checks](../../artifacts/rc-restart-fix/core/results.json), including
  18 new permission-update cases.
- [36/36 headed browser checks](../../artifacts/rc-connection-recovery/1794695001647/results.json)
  against an isolated real RC host with an explicit application/broker fixture.
  Adding Start, reconnecting with the same browser key, submitting the typed Start
  operation, keeping Stop disabled, and removing Start all passed. This fixture
  test does not claim another installed main-application launch.
- [11/11 visible permission-editor assertions](../../artifacts/rc-restart-fix/ui/results.json),
  including exact Save result, Cancel, independent Stop and refusal of an empty grant.
  The actual dialog capture was visually inspected for layout.

Before Angela clarified development-only scope, the already-installed English
Tlamatini was reopened once. After that instruction, no installed files, product
services or existing user browser permissions were changed. The correction is in
source; Angela will rebuild and reinstall it. Reinstallation deliberately does not
silently expand an existing browser's permissions. After installing the rebuilt
version, enable **Start Tlamatini** for that browser in **Connections → Change
permissions**, save, then reconnect. No new enrollment is needed.

No new installer, clean installation, commit, push, tag or publication was performed.
On Angela's request, test-runtime cleanup was verified and the walkthrough queue was
disabled. See [cleanup evidence](../../artifacts/rc-restart-fix/cleanup.json).
