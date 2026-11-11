# Visible desktop checks

For the **[0.3.3 pre-release](../../docs/releases/v0.3.3.md)** workspace, use
[the headed table/recovery harness](torrenter-table.md). Its 48 passing checks
are documented in the [10 November record](../../docs/verification/2026-11-10-torrenter-recovery.md).
It does not build or install the product.

Real XT tray presence/menu, Explorer restart, per-session behavior, DPI, keyboard operation and host consent. Every harness must be visible, monitored and retain truthful evidence; no headless mode. See [the execution policy](../../docs/development-execution.md).

For the installer and uninstaller UI, use [the setup acceptance commands](../../packaging/setup/README.md#reproduce-a-build).

From a verified foreground PowerShell console, build the solution in Release, then run the native dialog audit at the desktop DPI and the Windows 96-DPI baseline. Both runs display the production forms, measure their controls at default and minimum size, and save screenshots. Credential and erasure views use fixtures.

```powershell
$checks = '.\tests\Tlachihuani.Checks\bin\Release\net10.0-windows\Tlachihuani.Checks.exe'
& $checks --tray-layout '.\artifacts\verification\dialogs' desktop
& $checks --tray-layout '.\artifacts\verification\dialogs' 96dpi
```
