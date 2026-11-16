# Hosted RC recording

Actual headed Chrome screen capture of the development lab in English and Spanish.
The account provider is a real Keycloak container; cloud, relay and RC connector
are the development binaries. Main-application responses are deterministic test
fixtures. The tutorial control page is an explicitly labelled local test harness,
not an XT screen or a promised production setup wizard.

From a visible PowerShell in the repository, run:

```powershell
./scripts/Record-HostedWalkthrough.ps1 -OutputRoot 'C:/Development/XAIHT/repo/Tlachihuani/artifacts/rc-hosted-video/new-recording'
```

Prerequisites: Release binaries for RC.Server and RC.Cloud, Node, Docker Desktop
with the pinned Keycloak image used in `record_hosted_walkthrough.mjs`, and the
existing Tlamatini Python distribution with cryptography. No real user account,
installed service, Windows trust store, or Torrenter process is changed.

The runner records sign-in, computer selection, one-use invitation, fingerprint
comparison and approval, execution-mode dependencies, rich responses, history,
CSV/SVG/PDF saves, responsive layout, uncertain results after heartbeat loss,
late completion without resubmission, offline/reconnect, and logout. It compares downloaded byte hashes and
Unicode filenames. Owned servers and the container stop afterward. Do not run
another harness on ports 12487, 12490, 12492 or 12494 at the same time.

To produce narrated MP4s and a chapter player from a successful recording:

```powershell
& 'C:/Development/XAIHT/Tlamatini/python/python.exe' tests/tutorial/render_hosted_video.py 'C:/Development/XAIHT/repo/Tlachihuani/artifacts/rc-hosted-video/new-recording' --output 'C:/Development/XAIHT/repo/Tlachihuani/artifacts/rc-hosted-video/new-watch'
```

Rendering requires the already installed ffpyplayer FFmpeg, Pillow, pywin32 and
Windows Zira/Sabina voices. It preserves captured action timing and holds the
last frame when narration needs more time. MP4s contain H.264 video, AAC narration
and twelve chapters. VTT/SRT captions and JSON verification accompany them.

Verify the finished output in a visible browser, then verify narration and create
the share ZIP (both commands accept the output folder):

```powershell
node tests/tutorial/verify_video_player.mjs 'C:/Development/XAIHT/repo/Tlachihuani/artifacts/rc-hosted-video/new-watch'
& 'C:/Development/XAIHT/Tlamatini/python/python.exe' tests/tutorial/verify_video_audio.py 'C:/Development/XAIHT/repo/Tlachihuani/artifacts/rc-hosted-video/new-watch'
```

Share the `watch` output folder only. Recording folders contain temporary lab
credentials, private keys and Chrome profiles and must not be published. All
scratch belongs under the workspace, outside Windows user profiles.

This validates a loopback development workflow. It does not establish public
Internet access, end-to-end/PQ encryption, installed SCM acceptance, physical
phone behavior, live-model correctness or production account provisioning.
