# Verification

Latest version: **[0.38.0](../docs/releases/v0.38.0.md)**. The documentation incorporates
the [10 November source recovery results](../docs/verification/2026-11-10-torrenter-recovery.md):
64 automated checks and 48 visible browser checks. Those dated results are not
certification of a newly built installer.

`Tlachihuani.Checks` is a dependency-free executable suite invoked by the visible `scripts/Build.ps1 -Mode Check`. It covers framing attacks, protocol bounds, event retention, lifecycle idempotence/order/timeouts, startup races, native local IPC, server-image rejection, 32 concurrent real-host clients, heartbeat, correlated version refusal and graceful console shutdown.

The unit, desktop and integration directories contain the table, real-peer,
metadata queue, delivery, release packaging and setup tests. Start with the
[integration guide](integration/README.md) and [headed workspace guide](desktop/torrenter-table.md).
The [foundation results](../docs/verification/2026-10-11-foundation.md) remain
historical evidence. SCM registration, cross-account security and shell tray
behavior require separate installed acceptance.

`scripts/Test-ServiceReadiness.ps1` builds a separate optimized configuration in
a verified visible console and runs `--service-readiness <evidence directory>`.
It covers the real Jobs handler under a held lifecycle lock, independent pipe
and HTTP deadlines, transient recovery, persistent failures, cancellation,
bounded response/history storage, required/optional service gates and complete
redacted reports. A native foreground window verifies selection, Copy report
and the persistent saved report. It then runs the existing lifecycle/IPC checks.
Fixtures live under repository artifacts, and installed services are not started,
stopped or changed. The test reference to Jobs reuses its existing pinned
dependencies; no new product dependency was introduced.

`Tlachihuani.RC.Checks` (run by `scripts/Build.ps1 -Mode Check`) and
`Tlachihuani.RC.ContainerChecks` cover the Remote-Controller; the `rc_*` scripts in
[integration](integration/README.md#remote-controller-checks) cover its browser,
installed and hosted flows. The XT **Open workspace** sign-in has its own headed
check, [workspace_signin.py](desktop/README.md#open-workspace-sign-in), and the xaiht.org
link has [xaiht_link.py](desktop/README.md#xaihtorg-link-and-tlachihuanilog).

The executable website prompt channel adds RC core checks for exact account and
website identity, explicit approval/revocation, durable request reconciliation,
browser-authentication separation and multilingual byte limits. The default XT
checks exercise the actual command processor with controlled HTTP/native inputs:
lost acknowledgements, stable sequence numbers, progress/permission preservation,
terminal receipts, stale leases, error envelopes and bounded responses.

Run `Tlachihuani.Checks.exe --xaiht-accounts-dialog <evidence directory>` in the
verified visible development console for the real account-selection dialog at
normal/minimum size. The dialog uses isolated inputs; its screenshots do not prove
a production phone conversation. `--xaiht-relay` is a separate local integration
driver using the actual XT command processor and verified `XAIHT.RC.Dev.<nonce>`
pipe. Its required pairs are `--server`, `--key-file`, `--pipe`, `--pid`, `--image`,
`--bindings-file` (JSON array of one or two explicit QA binding IDs) and `--output`.
Only a loopback website and development RC pipe are accepted. All fixture files
must remain outside Windows user profiles. It writes `ready.json` and stops on
the output directory's `stop` sentinel; it never approves execution/tool requests.

The [23 November hosted-prompt record](../docs/verification/2026-11-23-hosted-prompts.md)
documents the final native, website and headed EN/ES model results, including the
unrun real-MySQL and physical-phone boundaries. Use the
[reproducible hosted harness](integration/README.md#website-prompts-through-xt-to-both-tlamatinis)
for the next source acceptance run.

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
[current integration guide](../docs/xaiht-link.md) and [dated evidence](../docs/verification/2026-11-23-hosted-prompts.md) before making
installation or release-acceptance claims. This pass changes documentation only;
existing version identifiers and installed services remain unchanged.
