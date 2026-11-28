# Audit repairs · 27 November 2026

The three defects reproduced during the source audit were repaired in the development
checkout. The pre-existing `1.1.0` version and package-lock edits were preserved. This
work did not commit, tag, generate an installer, deploy, or update installed services.

## Corrections

- **Torrenter:** the three-minute hung check now checks the reply queue first. A
  response that already arrived while its caller was no longer waiting is discarded
  normally; its age does not cause a healthy worker to be closed. A genuinely missing,
  overdue response still causes recovery.
- **HandGI:** dispatch and cancellation atomically claim queued UI work. A command
  canceled before execution has no side effects. Cancellation after execution starts
  cannot undo it, so dispatch retains its actual result. The transport deadline still
  applies; this does not guarantee delivery of a reply after that deadline.
- **Flow Pills:** the compiler starts suspended, enters the run's kill-on-close
  Windows job, then resumes. Standard streams use explicitly inherited pipes and
  concurrent output readers. Stop, force stop, disposal and parent exit terminate
  compiler descendants. Compilation observes cancellation and retains the two-minute
  failure timeout. Temporary-directory environment values use Tlamatini's own `Temp`
  folder; no new profile scratch location or file-backed transport was introduced.
- **RC documentation:** the closed operation list now includes `application.stop`
  and its `stop` scope, while keeping the hosted website's narrower capability set
  explicit.

See the [Torrenter guide](../torrenter.md#a-busy-engine),
[HandGI guide](../handgi.md#how-xt-and-handgi-work-together),
[Flow Pills guide](../flow-pills.md#agentic-flows), and
[RC operation list](../remote-controller.md#durable-operations-and-schedules).

## Regression evidence

The new worker, compiler and HandGI tests first ran against the unchanged implementation.
All three failed at their respective confirmed defects. After the repairs:

| Check | Observed result |
|---|---|
| Full .NET solution build | Passed; zero warnings and errors |
| Foundation/component suite | 22 checks passed, including worker and compiler regression groups and a real isolated console host |
| Worker late-response and missing-response cases | Both passed; subsequent replies remain associated with the correct request |
| Worker with actual idle time | Passed after waiting more than 182 real seconds; worker stayed alive and the next request received its own response |
| Compiler lifetime/result cases | Seven passed: force stop, ordinary stop, disposal, success with large stderr, malformed response, explicit refusal, and parent exit |
| Real Python compiler | Passed with a harmless Starter template and paths containing spaces and accented characters |
| HandGI dispatch | Four passed: normal, canceled while queued, already canceled, and cancellation after execution begins |
| Remote-Controller core | 318/318 passed |
| Python integration | 31 passed, including six real local magnets and the new RC operation-list comparison |
| HandGI / Flow Pills lifecycle | Seven / four passed in isolated user-state folders |

Development checks ran through a verified visible console titled
**Tlachihuani defect repairs - KEEP OPEN**. HandGI's camera stayed off. The real Python
case used Tlamatini's interpreter with isolated templates; it did not run normal user
agents or change a Tlamatini configuration. The fast worker regression advances the
stored request timestamp; the separate real-idle test used the actual clock.

The new real-Python test entry point initially collided with an existing local variable
name in the test program. That test-only build error was corrected; the subsequent full
build and all listed checks passed.

## Repeatable checks

Run from the repository root in a visible console after a Release-configuration source
build. Set `TEMP`, `TMP` and `TLACHIHUANI_USER_DIR` to application-owned test folders
outside Windows user profiles before running the suites.

- [WorkerChannelChecks.cs](../../tests/Tlachihuani.Checks/WorkerChannelChecks.cs):
  `Tlachihuani.Checks.exe --worker-channel-checks`; the slower actual-clock case is
  `--worker-channel-real-idle`.
- [CompilerLifetimeChecks.cs](../../tests/Tlachihuani.Checks/CompilerLifetimeChecks.cs):
  `Tlachihuani.Checks.exe --compiler-lifetime-checks <evidence-directory>`;
  actual Python is `--compiler-real-python <evidence-directory> <python.exe>`.
- [ControlPipeChecks.cs](../../tests/Tlachihuani.HandGI.Checks/ControlPipeChecks.cs):
  `Tlachihuani.HandGI.Checks.exe --control-pipe`. This is also included in
  [ServicesAndHandGI.ps1](../../tests/desktop/ServicesAndHandGI.ps1).
- [test_rc_contract_docs.py](../../tests/integration/test_rc_contract_docs.py): included
  in normal Python integration-test discovery.

The normal component suite includes the fast worker and compiler lifetime cases.
Execution logs, before/after results and preservation hashes are retained at
`C:\Development\XAIHT\artifacts\tlachihuani-repairs-20261127`.

These results establish source-level repairs and isolated runtime behavior. They do not
replace clean-machine installation, installed-service upgrade/reboot acceptance,
camera-on gesture tests or coordinated physical-phone tests.
