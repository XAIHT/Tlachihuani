# Installed RC connection walkthrough and bilingual tutorials

Date: 14 November 2026. Tlachihuani **0.3.3 pre-release**; installed main applications **1.93.5**.

## Result

The local-PC onboarding procedure was followed through the installed interfaces:
XT service start, real main-account enablement, invitation, fresh browser request,
full fingerprint comparison, native approval, connection and a real model reply.
The approved browser subsequently reconnected without another invitation. A second
real reply succeeded after restarting the main application without first opening
its chat page. Stopping and starting RC through XT preserved that main process and
the initial stopped state of the primary service and Torrenter Jobs host.

The user authorized the visible native UI harness because the desktop inventory
could not observe XT, automatic restoration of the existing development console,
changes to both main editions, and the administrator update. Development commands
and browser actions ran through the retained visible foreground console and owned
headed Chrome profile. Enrollment approval used the actual installed controls;
it was not created by a test API. The existing seeded QA account was used, not
Angela's personal account. Tutorial viewers must use their own account name.

## Corrections delivered

- XT offers **Remote-Controller…** directly in its tray menu. Both repeated
  **--show** and **--remote-controller** invocations restore their existing window.
  Services uses the same RC launcher instead of opening another independent panel.
- Both main Welcome pages now have a direct setup link: **Set up Tlamatini-RC**
  and **Configurar Tlamatini-RC**. Installed account pages explain the direct XT route.
- XT distinguishes **Ready**, **Connected** and **App not connected** account
  entries. Connected means the app is available and the first message can prepare
  its model. Offline bindings retain their immutable identities; matching names
  are not merged. Refresh no longer rebuilds an open account selector.
- A fresh RC client opens Connection & trust. Disconnected chat offers setup or
  connection guidance. Invalid invitations, disabled accounts and unavailable
  service errors explain the next action. Revoked/unknown browser registration can
  be reset after confirmation; normal session expiry preserves its approval key.
- Browser offline events alone do not invalidate a working loopback connection.
- A scoped authenticated capabilities endpoint supports chat/workspace permissions
  without requiring the separate monitoring permission. Native account status
  includes application readiness while preserving offline bindings.

Canonical client assets in C:/Development/XAIHT/Tlamatini-RC/public, their server
mirror and the installed RC assets match. Both installed main setup templates match
their authorized source editions. No dependency was added for these corrections.

## Installed walkthrough evidence

Evidence is under [artifacts/rc-walkthrough](../../artifacts/rc-walkthrough/).

1. Start the installed RC service using XT and observe Running/ready.
2. Sign in to the existing QA account in installed English Tlamatini, follow the
   Welcome setup shortcut and confirm account enablement with its password.
3. Select the current account binding in XT, create and copy an invitation.
4. In a fresh owned Chrome profile, name the device Tutorial verification browser,
   paste the invitation and request approval.
5. Select the actual pending request in XT, open Review and approve, compare the
   full displayed fingerprint with the browser and approve the matching device.
6. Observe automatic connection and a successful real answer to 17 + 25.
7. Reopen the same QA browser profile and connect using the retained approval.
8. After restarting main Tlamatini, submit 19 + 23 through RC without visiting the
   main chat first. The account initializes its model and returns 42.
9. Use the real XT stop confirmation (one session, zero pending operations), stop
   RC and start it again. Reconnect the previously approved browser.
10. Open each installed main Welcome/setup page and verify its English or Spanish
    route. Minimize each XT window and verify a repeated shortcut restores that
    same process and window to the foreground.

Primary evidence:

- [Full fingerprint comparison](../../artifacts/rc-walkthrough/fingerprint-comparison.json)
- [First real model reply](../../artifacts/rc-walkthrough/browser/real-chat-answer.json)
- [Cold-account real model reply](../../artifacts/rc-walkthrough/browser/cold-start-real-chat-answer.json)
- [Final connected browser](../../artifacts/rc-walkthrough/browser/final-installed-connected.json)
- [Repeated installed shortcut results](../../artifacts/rc-walkthrough/launcher-results.json)
- [Final consistency/state checks](../../artifacts/rc-walkthrough/final-verification.json)

## Checks and their scope

| Verification | Result | Scope |
| --- | --- | --- |
| Locked restore, NuGet audit and build | Passed | Source build; no new release package |
| Lifecycle checks | 11/11 | Existing lifecycle suite with direct RC menu coverage |
| RC core checks | 267/267 | Core authorization, operations and state regressions |
| Headed connection/recovery suite | 30/30 | Real isolated RC host with an explicit application-adapter fixture |
| Final installed consistency/state checks | 15/15 | Asset/template hashes, service independence, main identity, actual reply evidence |
| Repeated installed XT shortcuts | Both passed | Same HWND/process, restored, visible and foreground |
| Bilingual tutorial players | Both passed | Local MP4 playback, chapter jump, 24 chapters, 1920×1080 |

The recovery suite covers scoped capabilities, invalid/declined invitation retry,
reconnect, revocation, reset cancellation/confirmation, offline events, stopped
service guidance and absence of JavaScript exceptions. Its fixture responses are
not real model evidence. The two installed English requests above provide that
separate evidence. This session did not repeat the previous 100-request bilingual
batch or certify exhaustive coverage.

Detailed results:
[core](../../artifacts/rc-checks/results.json),
[recovery](../../artifacts/rc-connection-recovery/1794676809737/results.json),
[players](../../artifacts/rc-walkthrough/tutorial-player-results.json).
Root whitespace checks and both touched main template checks passed. A whole-tree
Spanish whitespace check detected existing unrelated issues; those were preserved.
The standalone canonical RC asset directory is not a Git checkout; hash equality
was used to verify its copies.

## Local installed update

This was a **local validation overlay**, not a new MSI release or clean-install
certification. The first successful RC/XT overlay changed 15 files. The final
account-label overlay changed one more file. Each verified all 643 files in its
RCServer/Tray runtime scope. Primary Service, Jobs and Control runtimes and protected
ProgramData identity data were not updated. Backup files, hashes and ACL information
are retained in the timestamped overlay directories.

Latest [overlay record](../../artifacts/rc-walkthrough/overlay-result.json):
finished 2026-11-14 17:43:42 UTC; rollback directory
artifacts/rc-walkthrough/overlay-20261114-174328-295e2cea.
The earlier successful backup is
artifacts/rc-walkthrough/overlay-20261114-173215-5dbe0ce7.
Both main editions received only the two externally carried HTML templates, their
source carriage and runtime-asset hashes, with rollback under
artifacts/rc-walkthrough/main-template-rollback. No database/backup/restore mechanic
or frozen main executable was changed for this template update.

Initial failed build and update attempts remain recorded. The old TrayMenu test
constructor was corrected before the successful build. The first updater failed
before mutation because PowerShell marshaled null as an empty SCM machine name;
a native wrapper corrected that. A later running-service preflight failed before
mutation; the successful update followed an actual XT stop. It does not prove that
the running-update branch passed. One unexpected stopped RC observation remains
unexplained; its event query found no matching SCM/error event. Do not attribute
that observation to an unverified cause.

Final observed state: RC Running/ready, English main running, Spanish main stopped,
primary Tlachihuani and Jobs stopped as at this walkthrough's baseline, approved QA
browser connected. Developer consoles are retained. No command or update is queued.
The earlier dated installed acceptance remains historical, including its Spanish
49/50 strict answer result; today's checks do not erase that failure.

## English and Spanish tutorial delivery

These are narrated screenshot walkthroughs with clearly labeled examples, not
continuous screen recordings. Actual installed captures replace the corrected menu,
account picker, setup links, approval and successful connection. Invitation values
are masked; unrelated private conversation screens are excluded. Spanish narration
explains the existing English XT/RC control names and uses the actual Spanish main
Welcome/setup page. XT and RC were not fully translated by this tutorial task.

| Language | Duration | Video | Chapter player | Shareable ZIP |
| --- | --- | --- | --- | --- |
| Español | 7m 33s | [MP4](../../artifacts/rc-video-tutorial/es/Tlamatini-RC-conexion-es.mp4) | [Ver tutorial](../../artifacts/rc-video-tutorial/es/Ver-tutorial.html) | [ZIP](../../artifacts/rc-video-tutorial/es/Tlamatini-RC-tutorial-es.zip) |
| English | 7m 27s | [MP4](../../artifacts/rc-video-tutorial/en-verified/Tlamatini-RC-connection-verified.mp4) | [Watch tutorial](../../artifacts/rc-video-tutorial/en-verified/Watch-tutorial.html) | [ZIP](../../artifacts/rc-video-tutorial/en-verified/Tlamatini-RC-tutorial-verified.zip) |

Each video has 24 chapters, narrated instructions, visible captions and a separate
SRT, at 1920×1080 H.264/AAC. Full video decode, non-silent narration, exported audio
levels and ZIP CRC checks passed. Both contact sheets were visually reviewed; the
local HTML players were then tested in visible Chrome by selecting a chapter and
observing actual playback. Offline Windows voices are Sabina (Spanish Mexico) and
Zira (English United States). Rendering provenance is retained beside each video.
Share the ZIP and extract it before opening the chapter player; it needs the MP4
beside it. The older English tutorial in the parent directory is superseded by
**en-verified** for this procedure.

## Boundaries

This build supports connections on the same PC. No physical-phone, LAN or
post-quantum remote-transport acceptance is claimed. The first enrollment still
requires local account authorization and device approval; the tutorial does not
make that a one-click flow. Reconnection reuses the approved browser. The installed
walkthrough and recovery fixes do not replace clean packaging, signing, reboot,
long-running or full security qualification. No commit, push, tag or publication
was performed.
