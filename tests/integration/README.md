# Integration checks

For **[0.3.3 pre-release](../../docs/releases/v0.3.3.md)**, run the source tests from
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

## Native acceptance

SCM transitions, named-pipe identities/ACLs, independent process lifetime and safe force-stop. Use an explicitly authorized Windows test installation. Never terminate unrelated user processes. See [the acceptance matrix](../../docs/verification-plan.md).
