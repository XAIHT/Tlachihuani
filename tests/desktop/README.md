# Visible desktop checks

For the **[0.36.0](../../docs/releases/v0.36.0.md)** workspace, use
[the headed table/recovery harness](torrenter-table.md). Its 48 passing checks
are documented in the [10 November record](../../docs/verification/2026-11-10-torrenter-recovery.md).
It does not build or install the product.

## Open workspace sign-in

`workspace_signin.py` checks that XT's **Open workspace** always reaches the
Tlamatini login page and that the queue then belongs only to the account that
signed in. It runs the source Tlamatini in-process with a throwaway database,
answers Jobs calls with a recorder, and serves the newly built launcher page with
`Tlachihuani.Jobs.exe --web`. It refuses ports 8000, 8052 and 12064. Build
`src/Tlachihuani.Jobs` first, then from a verified visible foreground console:

```powershell
python tests\desktop\workspace_signin.py
python tests\desktop\workspace_signin.py --tlamatini C:\Development\XAIHT\Tlamatini-Spanish\Tlamatini --port 8092 --launcher-port 12092
```

Results: [17 November record](../../docs/verification/2026-11-17-workspace-signin.md).

Real XT tray presence/menu, Explorer restart, per-session behavior, DPI, keyboard operation and host consent. Every harness must be visible, monitored and retain truthful evidence; no headless mode. See [the execution policy](../../docs/development-execution.md).

For the installer and uninstaller UI, use [the setup acceptance commands](../../packaging/setup/README.md#reproduce-a-build).

From a verified foreground PowerShell console, build the solution in Release, then run the native dialog audit at the desktop DPI and the Windows 96-DPI baseline. Both runs display the production forms, measure their controls at default and minimum size, and save screenshots. Credential and erasure views use fixtures.

```powershell
$checks = '.\tests\Tlachihuani.Checks\bin\Release\net10.0-windows\Tlachihuani.Checks.exe'
& $checks --tray-layout '.\artifacts\verification\dialogs' desktop
& $checks --tray-layout '.\artifacts\verification\dialogs' 96dpi
```
