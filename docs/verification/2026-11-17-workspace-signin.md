# XT Open workspace forced sign-in · 17 November 2026

Version: **[0.36.0](../releases/v0.36.0.md)**. Requested by Angela López Mendoza.

## Change

XT **Services → Runtime → REST workspace → Open workspace** now opens
`http://127.0.0.1:12064/?signin=1`. With `signin` present, the Jobs launcher page
(`src/Tlachihuani.Jobs/wwwroot/open-workspace.js`) sends the browser to the
edition's login page, `/?next=/agent/torrenter/workspace/`, instead of straight
into the workspace. That is the address Django itself uses when nobody is signed
in. Tlamatini's login page always shows the form, and signing in replaces the
browser's session for that edition, so the queue shown belongs only to the
account that just signed in. With two editions running, each chooser link leads
to its own login page. **Access → Open downloads** still opens
`http://127.0.0.1:12064/` and keeps the current login.

No Tlamatini change was required: both editions already honor `next` on their
login page. `/controllers` and the workspace contract are unchanged.

## Build

`dotnet build` (Debug) of `Tlachihuani.Jobs` and `Tlachihuani.Tray`: 0 warnings,
0 errors. The lock files were not rewritten. No package was published or installed.

## Headed test

`tests/desktop/workspace_signin.py`, run from a verified visible foreground
console with headed Chrome; Shoter photographed each step while the page was
verified in front. Isolation:

- Tlamatini ran in-process from source with a throwaway test database and two
  throwaway accounts, on port **8091** (English) and **8092** (Spanish).
- Its Jobs calls were answered by a recorder, so no test account reached the
  installed Jobs service; the recorder proves which account each Jobs request
  was made for.
- The newly built `Tlachihuani.Jobs.exe --web` served the real launcher page on
  **12091** / **12092**. The browser's `/controllers` answer pointed at the test
  Tlamatini, so installed editions were never probed.
- Ports 8000, 8052 and 12064 are refused by the script. The installed Tlamatini
  and Jobs kept running unchanged.

| # | Check | English | Spanish |
|---|---|---|---|
| 1 | Another account (bob) signed in with his workspace open | PASS | PASS |
| 2 | Open workspace lands on the login page while bob is signed in | PASS | PASS |
| 3 | A wrong password stays on the login page | PASS | PASS |
| 4 | After signing in, the workspace and every Jobs request belong to alice only | PASS | PASS |
| 5 | Bob's old tab reports "Session changed" and hides its queue | PASS | PASS |
| 6 | The login page appears again even when alice is already signed in | PASS | PASS |
| 7 | Open downloads (no `signin`) keeps the current login | PASS | PASS |
| 8 | With two editions, every chooser link is a login link; without `signin` the direct links remain | PASS | PASS |

Result: **20/20 English, 20/20 Spanish** (the totals include the launcher check and
each photograph's foreground verification). Photographs and `results.json` are
under `artifacts/workspace-signin/` (ignored by Git).

## Limits

This is source evidence for the launcher page and both login flows. The XT button
click itself was checked by build and code review only: the installed tray still
runs its previous build until a 0.36.0 package containing this change is installed.
Installing Jobs restarts that service, which checkpoints active transfers.
