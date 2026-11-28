# Build the Windows release ZIP

Latest version: **[1.0.1](releases/v1.0.1.md)**, a fix release for 1.0.0, the first official release.

Open a visible Command Prompt in the Tlachihuani repository root and run:

```bat
Build-Release.cmd
```

Keep the console visible and in front until it reports **READY**. From PowerShell use
`.\Build-Release.cmd`, or `& 'C:\Development\XAIHT\repo\Tlachihuani\Build-Release.cmd'`
from any folder. Administrator permission is not needed to build. Do not double-click
the file: its window closes as soon as it ends, taking the result with it.

The builder checks that its own window is in front at several points (when it starts,
before the checks, before publishing, before the Jobs runtime and before setup). If
another window is in front it prints **Waiting for this PowerShell window to be in
front. Click inside it to continue (up to 90 seconds)** and stops if nobody clicks
inside it in time. A run takes about four minutes once its packages are cached.

## Hosted prompt integration · 23 November review

The website path now carries real prompts and answers to an explicitly approved
running Tlamatini account through XT's outgoing HTTPS connection and the local
Remote-Controller adapter. It does not open an inbound port or add website launch,
shutdown or phone-audio controls. Diagnostics remain inert and separate from typed
commands. Retries retain identity, receipts survive reload, and full answers use
validated account-bound chunks. Ask Execs follows the existing selective policy;
Netstat is exempt, whereas the tested Executer action displayed approval.

Physical-phone English prompt/answer, reload and approval display were observed.
Real denial/no-execution and cancellation were not completed before tests stopped.
The wording-only website candidate was built but not promoted. Read the
[current integration guide](xaiht-link.md) and [dated evidence](verification/2026-11-23-hosted-prompts.md) before making
installation or release-acceptance claims. This pass changes documentation only;
existing version identifiers and installed services remain unchanged.

## Build-machine prerequisites

Use Windows x64 with Git, Python 3.10 or later on PATH, the .NET SDK pinned in
`global.json`, and the Windows .NET Framework 4.x compiler. Dependency restore,
the pinned WiX tool and private CPython/libtorrent downloads need network access
on their first use. Caches and scratch space stay inside `artifacts`; client
machines need none of these development tools.

The builder keeps its own NuGet package folder, `artifacts\release-cache\packages`,
separate from your usual `%USERPROFILE%\.nuget\packages`. A package your development
builds already have is therefore downloaded again the first time a release build
needs it. HandGI's graphics-card packages are large: `Microsoft.ML.OnnxRuntime.DirectML`,
`Microsoft.AI.DirectML`, `Microsoft.ML.OnnxRuntime.Managed`, `System.Numerics.Tensors`
and the Windows SDK projection `Microsoft.Windows.SDK.NET.Ref`.

## When restore stalls

"Restore" means downloading the packages the code needs; it does not change or undo
any of your files. If the console shows `Tlachihuani.slnx  Restore (…s)` counting for
many minutes, a download is crawling or stuck. Ctrl+C may not stop it; close the
window instead. Then copy the packages your development builds already have into the
builder's folder and run `Build-Release.cmd` again:

```powershell
$from = "$env:USERPROFILE\.nuget\packages"; $to = 'C:\Development\XAIHT\repo\Tlachihuani\artifacts\release-cache\packages'; foreach ($p in 'microsoft.ai.directml\1.15.4','microsoft.ml.onnxruntime.directml\1.24.4','microsoft.ml.onnxruntime.managed\1.24.4','system.numerics.tensors\9.0.0','microsoft.windows.sdk.net.ref\10.0.19041.57') { robocopy "$from\$p" "$to\$p" /E /R:2 /W:2 /NFL /NDL /NJH /NJS /NP | Out-Null; if ($LASTEXITCODE -lt 8) { "OK      $p" } else { "FAILED  $p (code $LASTEXITCODE)" } }
```

The versions are the ones the lock files pin today; check
`src/Tlachihuani.HandGI/packages.lock.json` after a version change. Locked restore
still compares each copied package's content hash with the lock file, so a wrong or
damaged copy is refused rather than used. This happened on 27 November 2026; see the
[verification record](verification/2026-11-27-handgi.md#the-101-package-with-handgi).

If an earlier run was cancelled with Ctrl+C, its failure can read "SDK could not be
resolved by the SDK resolver because the worker node was shut down". That message
comes from the cancellation itself, not from a fault in the build.

## Version and source

The numeric product version is **1.0.1**. The existing builder selects the
`v1.0.1` tag under the rules below. A documentation edit or GitHub channel
label does not select or stamp the binary version. Do not expect a suffixed
`v1.0.1-rc.1` tag to be selected. Tagging and packaging are separate steps.

Each invocation reads local Git tags again. The highest numeric stable tag in
`vX.Y.Z` or `X.Y.Z` form supplies the version; creation dates and lexical sorting
do not determine it. Prerelease tags are ignored. There must be a stable tag,
its commit must be an ancestor of HEAD, and its version must fit MSI's
255.255.65535 limits. Keep your local tags current before building; the command
does not fetch, create or move them.

The current working tree supplies the code, including local edits. The builder
updates `Directory.Build.props` and internal Tlachihuani project dependency
ranges in lockfiles to that version. External package pins and hashes stay
unchanged. No Git commit, push or upload is performed. A local `source.json`
records the tag, checkout commit and whether local edits were present.

## What the command does

1. Acquires a build lock and creates fresh staging under `artifacts/release-builds`.
2. Tests numeric tag selection and version stamping, then stamps the product.
3. Restores locked dependencies, builds and runs the lifecycle checks.
4. Publishes Service, Control, Tray, Jobs, RCServer and HandGI (with its three models)
   for Windows x64 with private runtimes.
5. Adds hash-pinned CPython and libtorrent, regenerates setup and the offline guide.
6. Checks every MSI cabinet file against the published bytes, the startup-choice
   condition, shortcuts, service startup types and Windows registration metadata.
7. Checks the six-file ZIP allowlist, CRC, manifest, file sizes and SHA-256 hashes.
8. Copies the verified ZIP and checksum into `dist`. Failed builds leave previous
   release files intact. Each successful rebuild replaces the same version's ZIP;
   other versions remain. Logs and staging are retained for inspection.

Upload `dist/Tlachihuani-<version>-windows-x64.zip` and its `.zip.sha256` to the
GitHub Release you create. Do not upload the checkout or `artifacts` directory.

## New-user experience

The user extracts the ZIP and opens Installer.exe from their normal Windows
session. Setup asks whether XT should appear at Windows sign-in. That optional
common Startup shortcut applies to all Windows users on the PC. Start menu and
Desktop shortcuts are always installed. The review page shows the exact choice.
UAC is requested only for installation/removal; the original Windows operator
remains the delegated service operator when another administrator approves it.

After successful setup, the original unelevated wizard launches XT once. It is
gray, with all three services stopped. If the entire wizard was run elevated, the
result directs the user to the normal Desktop shortcut instead of launching an
elevated tray. Login startup never starts services. All three services are Manual
and stay stopped after reboot until the operator chooses **Start Services**, which
also starts HandGI only when its Camera settings say **Start with the other services**.
Signing out/in while services are already running does not stop them; use
**Stop Services** for that. **Exit tray** only closes XT.

Windows Installed apps and Control Panel offer native removal. Uninstall removes
package-owned shortcuts, registry entries, services and binaries, while keeping
downloads, protected identities and job history. See the [setup guide](../packaging/setup/README.md).

## Release acceptance

Package verification is automatic. On a visible clean Windows QA machine, also
test installation with startup checked and unchecked, ordinary-user XT launch,
Desktop/Start reopening, a real reboot, service start/stop, and Control Panel
removal. The [visible setup harness](../scripts/Test-Setup.ps1) supports isolated
SCM tests and Windows Sandbox. An isolated fixture does not certify the real
Torrenter engine. The package is unsigned until the publisher supplies a signing
certificate; checksums are integrity checks, not publisher authentication.
