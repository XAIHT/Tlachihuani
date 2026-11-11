# Tlachihuani handoff · 11 November 2026 · 0.3.3 pre-release

Work in `C:\Development\XAIHT\repo\Tlachihuani`. The separate Tlamatini reference checkout is `C:\Development\XAIHT\Tlamatini`. Read AGENTS.md, CLAUDE.md, [the Torrenter operating guide](docs/torrenter.md) and [implementation status](docs/implementation-status.md). The design baseline is Revision 05 of `TlachihuaniKickoffDesign.html` on Angela’s Desktop.

The checkout path above supersedes the original location. Product metadata and
internal project lock references already declare **0.3.3**; this documentation
pass preserves those existing edits. See the [11 November checkout verification](docs/verification/2026-11-11-documentation.md).

## Current pre-release and installation evidence

**Current documentation: [0.3.3 pre-release](docs/releases/v0.3.3.md).** Angela explicitly requested this channel. It covers torrent queue recovery, accurate completion/error reporting, sortable account workspaces and the existing offline setup. Use the [Windows setup guide](packaging/setup/README.md) and [verification status](docs/implementation-status.md). The 0.1.0 workstation installation of 3 November and isolated 0.2.1 MSI checks of 4 November remain historical evidence; they do not certify a 0.3.3 installation.

The [10 November recovery record](docs/verification/2026-11-10-torrenter-recovery.md)
records 64 automated checks and 48 headed-browser checks across both development
editions. Four real local-peer transfers completed despite two silent swarms.
A read-only installed audit matched all 432 delivered files from 21 completed
jobs. Three unfinished jobs were left unchanged. Preserve those exact scopes.

The workspace uses the signed-in Tlamatini account without pairing codes. Keep
its five canonical assets synchronized with both controller editions when those
changes are authorized. Start resumes an incomplete stopped job; Resume continues
a paused one; Recheck validates a failure. Completion is separate from stopped
peer activity, and reported seeds are separate from connected peers. Jobs yields
a blocked slot after 180 seconds without payload progress and retries after at
least 60 seconds. It waits for disk-complete pause acknowledgment before hashing.
The service and controller fixes require their respective installed updates.

* On ANGYSLAP both services are set to Manual. The delegated operator is `ANGYSLAP\angel`. XT starts independently at user sign-in. **Start Services** brings up the services and Torrenter on port 12064. **Stop Services** leaves only XT running. Jobs has a separate manifest from the original service.
* XT is gray until both services are running and Torrenter's API responds. The context menu also offers **Show panel** and **Exit tray**. The exit label reads **Exit tray (service keeps running)** until XT confirms both services are stopped or absent and any pending change finishes. The optional historical commands are preserved in `src/Tlachihuani.Tray/TrayMenu.cs`.
* `scripts/Build.ps1 -Mode Check` covers the original lifecycle. Publish, then run `scripts/Test-Jobs.ps1` for a real private swarm and crash/recovery checks. Supply a Playwright-capable Python and an installed Chrome for the explicitly headed workflow. Follow [the visible-execution policy](docs/development-execution.md).
* The installed XT/Chrome run on 31 October exercised the native code approval, upload, all detail tabs, stop, filter and record removal. The delegated account exercised actual SCM stop and start. The original Tlachihuani registration remained stopped. Installation and update retained the independent Jobs identity.

## Next work

The root release command is now `Build-Release.cmd`; read [release building](docs/release-building.md). It rebuilds the current checkout at the latest numeric local Git-tag version and emits a checked ZIP/checksum in `dist`. Setup now asks for the optional common login shortcut and always creates Start/Desktop shortcuts. Services remain Manual/stopped after installation and reboot. The normal unelevated installer opens XT once after success; an elevated launch directs the operator to the normal Desktop shortcut.

For 0.3.3, the builder must select numeric `v0.3.3` or `0.3.3`; GitHub's pre-release
flag is independent, and suffixed tags are ignored. This documentation update
does not stamp product metadata, build, install, modify services, create tags,
commit or publish. The separate **Panels → Tlachihuani** lifecycle panel remains
a design target; **Panels → Torrenter Workspace** is implemented.

1. Complete the release acceptance gates with a sign-out/Explorer restart, reboot, power interruption, multi-day transfer, a second real Windows login and packaged signing/rollback qualification. Keep the original service’s force-stop consent helper on its own backlog.
2. When authorized, qualify a fresh 0.3.3 package and the scheduler/disk changes on a clean Windows QA machine. Extend tracker error telemetry and rate-history visualization, and qualify large queues, network failures and disk exhaustion. The current REST and UI surfaces are listed in the [Torrenter guide](docs/torrenter.md).
3. Keep the existing Tlamatini wrapped Torrenter client compatible with the scoped REST contract. The future Tlamatini Panels integration and agent catalog belong to that checkout. Preserve its protected database mechanics.

Tlachihuani upstream is [angelahack1/Tlachihuani](https://github.com/angelahack1/Tlachihuani). Tlamatini upstream is [XAIHT/Tlamatini](https://github.com/XAIHT/Tlamatini). Keep the implementation uncommitted until Angela requests otherwise. Snyk remains disabled.
