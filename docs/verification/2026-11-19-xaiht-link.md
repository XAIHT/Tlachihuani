# xaiht.org link and Tlachihuani.log · 19 November 2026

Version: **[0.37.0](../releases/v0.37.0.md)**. Requested by Angela López Mendoza.

## Change

XT writes **Tlachihuani.log** and shows it with **Services → See log**. A new
**xaiht.org** tab connects the computer to the **Tlamatini-RC** page on xaiht.org,
which sends JSON messages that XT writes to the log. See the
[guide](../xaiht-link.md).

## Builds and source checks

- `dotnet build` (Debug) of `Tlachihuani.Tray` and `Tlachihuani.Checks`: 0
  warnings, 0 errors.
- `dotnet publish` of `Tlachihuani.Tray` in Release, win-x64, self-contained, with
  `RestoreLockedMode`, the command the release builder uses: succeeded; the output
  contains the new XT code and `System.Security.Cryptography.ProtectedData.dll`.
  No new package was added.
- XAIHT `web-app`: `tsc -b` passed, ESLint passed on every new and changed file,
  and `vitest run` passed **171 tests in 11 files**, including 8 new Tlamatini-RC
  tests (keys, refusal without a key, online state, exact JSON, immediate wake on
  SEND, redelivery, expiry and per-user isolation).

## Visible end-to-end check

`tests/desktop/xaiht_link.py`, run from a verified visible foreground console with
headed Chrome; Shoter photographed every step while the named window was verified
in front. Isolation:

- The real Tlamatini-RC page, tRPC operations and `/api/rc` routes from the
  web-app source ran on port **8095** through `web-app/scripts/rc-local-server.ts`,
  with a memory store instead of Cloud SQL and one fixed test user instead of
  Google sign-in.
- The newly built XT ran with `TLACHIHUANI_USER_DIR` pointing at
  `artifacts/xaiht-link/user`, so this Windows account's real log and key were
  not touched (checked afterwards: no `%LOCALAPPDATA%\XAIHT\Tlachihuani` folder
  was created).
- XT was driven through its own buttons by window messages: Services…, xaiht.org,
  Connect…, See log, Close and Exit tray.
- Ports 8000, 8052, 12064 and 3000 are refused.

| # | Check | Result |
|---|---|---|
| 1 | The signed-in menu has Tlamatini-RC | PASS |
| 2 | xaiht.org creates a computer key (`xrc_` + 43 characters) | PASS |
| 3 | Before XT connects, the computer is not online ("Waiting for Tlachihuani") | PASS |
| 4 | XT's Connect dialog saves the key, protected (not in plain text) | PASS |
| 5 | The page shows the computer online once XT asks | PASS |
| 6 | XT's xaiht.org tab says Online | PASS |
| 7 | The page's preview is the JSON that will be sent | PASS |
| 8 | SEND reaches Tlachihuani.log and the page says so (2.4 s) | PASS |
| 9 | Tlachihuani.log holds the exact JSON once, accents and emoji as typed | PASS |
| 10 | See log shows the received message | PASS |
| 11 | After XT exits, the page shows the computer offline (41 s) | PASS |
| 12 | A message sent while offline waits for the computer | PASS |
| 13 | When XT starts again, the waiting message is written to the log once | PASS |

Result: **21/21** (the total includes eight photographs, each with its
foreground verification). The final build passed **21/21** twice. Photographs and
`results.json` are under `artifacts/xaiht-link/` (ignored by Git).

The first run found two real faults, both fixed before the passing runs: XT stayed
on "Connecting…" until its first 25-second request ended (the first request now
answers at once), and emoji were written as `😀` escapes (they are now
written as typed; the JSON stays valid).

## Limits

- Source evidence only. xaiht.org itself, its Cloud SQL tables and Google sign-in
  were not exercised; Angela's production test is the first against them.
- No installed 0.37.0 package was built or tested here.
- Whether a firewall in front of xaiht.org lets XT's requests through is unknown
  until the production test; XT reports a 403 as such.
