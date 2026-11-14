# Development fix: Start button follows application state

14 November 2026 · Tlachihuani 0.3.3 pre-release.

The Monitoring page enabled Start solely from the browser's launch permission,
even when its latest snapshot said the application was ready. This was a client
state bug. The trusted launch broker already checks for the installed running
process; Start is not a request to restart it.

The development client now disables Start when Tlamatini is observed ready or
loading. A start/stop HTTP submission, accepted receipt or running receipt locks
both lifecycle controls, with Starting/Stopping text. Periodic snapshot and receipt
polling cannot unlock them prematurely. The event handlers recheck the same guards,
and completed receipts ignore late responses from an earlier concurrent poll.
Failure releases the pending lock while preserving the application-state and
permission checks. Cancelling the Stop confirmation submits nothing.

The client still distinguishes missing heartbeat from proof of process exit.
An offline/unknown observation does not claim that Tlamatini stopped. The existing
broker verifies process identity before an allowed Start or Stop request. No new
process-termination or service-control behavior was introduced.

Changed development assets: remote.js and remote-sw.js in the RC server wwwroot,
plus their canonical copies in C:/Development/XAIHT/Tlamatini-RC/public. Shell cache
version is v7. The existing server project copies wwwroot into build and publish
output. No main English or Spanish application rebuild is required for this fix.

Verification:

- Development RC server build passed.
- [49/49 headed browser checks](../../artifacts/rc-connection-recovery/1794696858453/results.json)
  passed against the real isolated RC host, using an explicit account/broker fixture.
  This includes ready/loading, queued/running Start, polling, duplicate dispatch,
  failed Start retry, successful Start, separate Stop permission, cancelled Stop,
  pending/failed Stop, reconnect, revocation and offline-cache recovery.
- The [ready-state screenshot](../../artifacts/rc-connection-recovery/1794696858453/ready-start-disabled.png)
  shows disabled Start and available Stop. Layout was visually reviewed.
- Source whitespace and SHA-256 equality for remote.js, remote.html, remote.css and
  remote-sw.js across canonical source, server source and built output passed.
- The first 49-check run passed its browser cases but its final asset audit could
  not invoke Get-FileHash in this PowerShell environment. The final runner uses
  .NET SHA-256; the complete run was repeated after the late-response guard.
- [Final run](../../artifacts/rc-lifecycle-buttons/final-visible.log),
  [successful audit marker](../../artifacts/rc-lifecycle-buttons/final-checks.result),
  [owned-process cleanup](../../artifacts/rc-lifecycle-buttons/final-cleanup.json).

This is a development-source correction. No installed files, product services,
existing browser permissions or main application processes were changed. Angela
will rebuild/reinstall Tlachihuani, then reload the RC page. Test browsers, isolated
hosts and compiler servers were shut down; no helper queue was restarted. No commit,
push, tag or publication was performed.
