# Integration checks

For **[1.0.0](../../docs/releases/v1.0.0.md)**, run the source tests from
one verified visible foreground console with the prepared Python environment and
the private libtorrent package on its module path:

```powershell
python tests/integration/test_transfer_recovery.py
python tests/integration/test_magnet_queue.py
python tests/integration/test_delivery.py
```

Recovery tests use real libtorrent sessions, an isolated journal and
application-owned scratch under `artifacts/verification`. Four local-peer payloads
must finish while two silent swarms compete for slots; hashes are independently
checked. Other cases isolate scheduler, state and disk-pause boundaries. Production
timers remain 180 seconds before yielding and at least 60 seconds before retry;
the test shortens them locally. Metadata and delivery suites cover their separate
queues and storage boundaries. No installed jobs are changed.

Run `test_release_version.py` for tag/stamp behavior. `check_setup_zip.py` and
`check_setup_payload.py` validate existing package outputs. `Test-SetupStartup.ps1`
performs actual isolated MSI installation/removal and needs its own authorized,
visible administrator session; it is not part of a documentation check.

Evidence: [10 November recovery verification](../../docs/verification/2026-11-10-torrenter-recovery.md).

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
[current integration guide](../../docs/xaiht-link.md) and [dated evidence](../../docs/verification/2026-11-23-hosted-prompts.md) before making
installation or release-acceptance claims. This pass changes documentation only;
existing version identifiers and installed services remain unchanged.

## Remote-Controller checks

`scripts/Build.ps1 -Mode Check` runs `Tlachihuani.RC.Checks`. `rc_scm.ps1` runs the
real RC executable under a fixed temporary SCM acceptance registration and leaves
the production registrations alone. The other `rc_*.mjs` / `rc_*.py` scripts drive
the browser client, main-application adapters, installed lifecycle, mobile views,
prompts and receipts; `rc_hosted_*.mjs` and `scripts/Test-HostedDevelopment.ps1`
cover the hosted lab. Read the matching record before running one: the
[Remote-Controller guide](../../docs/remote-controller.md#development-and-evidence),
[hosted development](../../docs/hosted-development.md) and `docs/verification/2026-11-13*` to `2026-11-15*`.

### Website prompts through XT to both Tlamatinis

`rc_hosted_prompts.py` exercises the actual website page, command relay, XT command
handler, native Remote-Controller and each edition's normal `AgentConsumer`/RAG
path. Prepare current RC.Server and Checks Debug outputs and the website's built
client assets first. Run from the repository only after the development parent
has verified its visible foreground console; `--visible-development` records that
precondition and does not replace the foreground gate.

```powershell
& 'C:\Development\XAIHT\Tlamatini\python\python.exe' -B tests/integration/rc_hosted_prompts.py `
  --rc-image 'C:\Development\XAIHT\repo\Tlachihuani\src\Tlachihuani.RC.Server\bin\Debug\net10.0-windows\Tlachihuani.RC.Server.exe' `
  --relay-driver 'C:\Development\XAIHT\repo\Tlachihuani\tests\Tlachihuani.Checks\bin\Debug\net10.0-windows\Tlachihuani.Checks.exe' `
  --output 'C:\Development\XAIHT\repo\Tlachihuani\artifacts\rc-hosted-prompts-new-run' `
  --visible-development
```

Choose a **new output directory for every run**: existing evidence is never
overwritten. The harness currently uses this developer workspace's absolute
paths: service source in `C:\Development\XAIHT\repo\Tlachihuani`, sibling
`Tlamatini`, `Tlamatini-Spanish` and `web-app` under `C:\Development\XAIHT`,
English's bundled Python/Playwright, and installed Chrome and Node.js under
Program Files. Both source configurations must resolve the same available model.
It requires unused loopback ports 12487–12489 and website port 8096 (the latter
can be changed with `--web-port`). All scratch, browser profile, SQLite databases
and staged binaries stay under the application-owned output directory.

The visible browser sends one genuine prompt through each edition's configured
model, verifies its saved answer, account isolation and reload without resubmission.
Separately labelled deterministic, no-effect fixtures test approval denial,
active cancellation and a long Unicode response through real signed tickets and
multiple relay chunks. Those fixtures are **not model responses or real tool
execution**. The website uses an isolated authenticated test identity and in-memory
relay; source accounts/databases are fresh, catalogs/context are empty, and host
startup, speech, reaping, wizard and Torrenter side effects are suppressed. This
does not test production Google login, Cloud SQL, an installed service or a
physical cellphone. It neither restarts nor modifies installed applications.

Evidence includes `results.json`, per-edition model evidence, foreground desktop
and browser-viewport PNGs, and verified SHA-256 hashes for the staged RC executable
and assemblies. Owned test hosts stop afterward; Chrome remains visible for
inspection. The isolated connection key is removed after its relay driver stops.

## Native acceptance

SCM transitions, named-pipe identities/ACLs, independent process lifetime and safe force-stop. Use an explicitly authorized Windows test installation. Never terminate unrelated user processes. See [the acceptance matrix](../../docs/verification-plan.md).
