# Windows integration

Fixed-name own-process SCM adapter, protected installation-manifest discovery, named-pipe creation with explicit DACL/remote denial, held process-image identity and cross-process lifecycle mutex. Native installed-service ACL/elevation testing remains pending; see [verification](../../docs/verification/2026-10-11-foundation.md).

## Stopping and per-user processes · after 1.0.1, not yet committed

- `ForcedStop` is XT's **Stop Services**. A Windows service is asked to stop first; one still
  running 3 seconds later is ended through the process the Service Control Manager names for
  it, after its executable is checked against the installation, never by image name or a
  remembered PID. A service installed before it allowed this is waited for instead, and its
  outcome says so. If Windows restarts an ended service by itself, XT stops it again for up to
  75 seconds. A per-user process (HandGI, Flow Pills) is ended at once through its record.
- `OperatorProcessAccess.AllowOperatorStop` lets the operator end a service's process; each
  service grants it when it starts. It adds no right to read or change the process.
- `ProcessIdentity` (in `LocalPipe.cs`) can be opened to end that exact process: the handle
  belongs to it, so a reused PID cannot be hit.
- `UserProcessRecord` (`UserProcess.cs`) is a per-user process's PID, creation time and
  executable. The process publishes it; XT checks it before talking to the process or ending it.
- `HandGIEndpoint` is HandGI's folder (`service.json`, `settings.json`, `profiles`), its pipe,
  which only the signed-in user can open, and the calls XT makes through it.
- `LogPaths.UserFolder` is the per-user folder. `TLACHIHUANI_USER_DIR` moves it for tests and
  gives that folder its own window and event names (`LogPaths.UserScope`).

Results: [27 November verification](../../docs/verification/2026-11-27-handgi.md).
