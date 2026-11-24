# Website prompts to both Tlamatinis — 23 November 2026

The existing outgoing xaiht.org connection now carries typed prompt operations
to a locally approved Tlamatini account and returns the real chat engine's answer.
This record covers source and visible local acceptance, the subsequent real-MySQL
checks, and the authorized website/native rollout. Installed-account execution and physical-phone observations are recorded separately below; source tests are not substituted for phone acceptance.

## Observed results

| Check | Result |
|---|---|
| English adapter/runtime regressions | 309 passed; no failures, errors or skips |
| Spanish adapter/runtime regressions | 311 passed; no failures, errors or skips |
| Edition source syntax, packaged inclusion and runtime parity | Passed; intentional migration predecessors preserved |
| Website types, ESLint and production build | Passed; existing bundle-size advisory remains |
| Website automated tests | 203 passed |
| Real MySQL persistence acceptance | 7/7 passed on isolated MySQL 8.4.13; zero failures or skipped tests |
| Native RC core | 318/318 passed |
| XT transport, lifecycle and IPC checks | 11/11 passed |
| Native account-access dialog | Visible foreground, minimum-size layout, separate accounts, selected grant and revocation passed |
| Final headed hosted acceptance | 76/76 passed; 22 desktop/viewport screenshots |
| Source configuration integrity | All 110 English and 448 Spanish checked files remained byte-identical |
| Python acceptance harness lint | Passed before the final browser run |
| Production website rollout | Exact accepted image active; public HTTPS SPA SHA-256 matches; unauthenticated result POST returns 401 with Bearer challenge and no-store |
| Visible anonymous production browser | 30/30 passed; English/Spanish at 390px and 320px; four screenshots; both JS/CSS hashes match the candidate |
| Installed native payload update | 676/676 Tray/RCServer files match the fresh self-contained Release manifests; product version remains 0.39.0 |
| Installed RC service after Angela's Start action | Running, native health ready, listener ready, zero pending operations; existing website-key SHA-256 unchanged |
| Actual phone with installed English account | Genuine prompt/answer passed; reloading preserved the same conversation; real Executer approval display confirmed; denial/no-execution and cancellation still pending |

Both editions used their actual configured **deepseek-v4.1-flash:cloud** model.
English answered a fresh calculation/nonce prompt in 15.58 seconds; Spanish in
13.52 seconds. These observed times include the test's reload/poll checkpoints;
they are not performance guarantees. Each response contained 42 and the exact
fresh identifier, was saved in its account history, and did not alter the other
account. Reloading the page did not invoke the model again.

Separate deterministic fixtures exercised the actual execution-approval broker
(denial only), active cancellation epoch, and signed full-answer endpoint. Each
edition recovered an 86,473-character Unicode answer across six chunks, including
its final marker. These cases validate control and storage transport; they are
not additional model calls or successful execution of real tools.

## What the tests exercised

The headed browser used the real website page/API, the actual XT command
processor, native RC host and each edition's AccountWorker, RuntimeSession and
AgentConsumer/RAG path. Account selection was explicit. No account was selected
implicitly, and Send was unavailable until a ready approved account was selected.
The website language remained English while testing both Tlamatini editions.
Viewport screenshots were inspected for readable answers and horizontal overflow.

The native and transport regressions cover connection/account identity, grant
revocation, request deduplication, lost acknowledgements, stable result sequence,
stale leases, reconnecting receipts, bounded frames, and queue saturation with
separate capacity for cancellation/permission and account discovery. Queued
cancellation atomically cancels the owned operation even without a live adapter.
Full-response tests cover immutable snapshots when regenerated signed file links
would otherwise change the JSON between chunks, as well as ticket and grant
revalidation on each chunk.

Fresh source accounts, SQLite databases, an isolated authenticated website user
and an in-memory website store were used. Catalog/context were empty. Host
startup, speech, reaping, wizard and Torrenter side effects were suppressed.
All test state and browser profiles remained under workspace artifacts, outside
Windows user profiles. Owned adapters, relay and RC host stopped cleanly; the
final headed browser was retained for inspection.

## Evidence and reproduction

Final evidence is in `artifacts/rc-prompts-live-20261123-05/`:

- `results.json`: 76 assertions and their real outcomes.
- `english-live-evidence.json` / `spanish-live-evidence.json`: fresh prompts,
  actual model identity, answers, history and model-call events.
- `english-real-answer-mobile-viewport.png` and
  `spanish-real-answer-mobile-viewport.png`: product-only mobile views.
- `runtime-source.json`: matching source/staged SHA-256 for the executable,
  `Tlachihuani.RC.Server.dll` and `Tlachihuani.RC.dll`.
- `source-configuration-integrity.json`: before/after hashes of 558 configuration
  files, without copying private values. Each edition's `fixture-isolation.json`
  records zero forbidden write attempts in the final run.

The visible console transcript and core/dialog results are under
`C:\Development\XAIHT\artifacts\rc-prompts-20261123`. Early failures remain
in their original evidence directories. The first hosted attempt failed the
fixture's artifact-owned-image requirement before invoking a model; the harness
now stages the complete freshly built runtime without replacing old evidence.
The final run used the corrected binaries, not an installed service image.

The closing audit also caught automatic MCP-default seeding in both source
catalogs during earlier fixture imports. Only the exact seed additions were
reversed after checking the clean starting snapshots and current contents; both
catalogs matched their original hashes. The fixture now redirects its mutable
MCP catalog, memory and runtime/cache paths into artifacts before imports. Its
write guard rejects source/profile mutations, permits harmless directory-creation
probes when that directory already exists, and checks configuration hashes at
teardown. The final rerun passed with these protections. Unrelated private
configuration and its preexisting whitespace were preserved.

Use [the reproducible harness instructions](../../tests/integration/README.md#website-prompts-through-xt-to-both-tlamatinis).
The repository harness is preserved from the exact script used in the final run.

## Real MySQL persistence acceptance

After the source/browser run, the website's `api/rc/command.mysql.test.ts` passed
**all seven tests against actual MySQL 8.4.13**, with no failures or skipped tests.
The dedicated instance listened on loopback port **13316**, with its data,
temporary files and exports under
`C:\Development\XAIHT\artifacts\rc-rollout-20261123\mysql-qa-3b0058db`.
This exercised the SQL store rather than substituting an in-memory database.

The seven checks covered atomic payload publication and concurrent deduplication;
account ownership on reads, mutations and results; lost acknowledgements and
stale delivery authority; transactional prompt/discovery/control capacity;
queued cancellation racing delivery; active cancellation identity retention;
and dispatch expiry plus computer-revocation cleanup.

The [MySQL run and cleanup result](../../../../artifacts/rc-rollout-20261123/mysql-latest-result.json)
records exit code zero, successful server shutdown and **no remaining test
schemas**. The [Vitest assertion report](../../../../artifacts/rc-rollout-20261123/mysql-qa-3b0058db/vitest.json)
records seven passed assertions. Evidence files remain in the artifact directory;
the production database and installed services were not used by this SQL test.
The earlier source/browser evidence remains unchanged.

## Authorized website and native rollout

Angela authorized deployment after the seven real-MySQL checks. The visible
rollout ran on **23 November local time**, completing the website promotion at
**2026-11-24 02:48:09 UTC**. Evidence is under
`C:\Development\XAIHT\artifacts\rc-rollout-20261123`.
No commit, push, tag or additional version bump was made.

The website was built from the current working tree's allowlisted source context,
including the uncommitted implementation. Private configuration and test/runtime
artifacts were excluded. The receipt identifies the exact deployed content:

| Provenance | Recorded value |
|---|---|
| Cloud Build ID | `b0ba924a-2c59-4deb-8150-ba65b1b66984` |
| Source-manifest SHA-256 | `8f871649fababbcfa2b5fbb449a4f9b5e1ca127c0afe0c91c46869876ba8ff29` |
| Accepted/running image digest | `sha256:6c2229b1409a9caeb0a82b3fab20c39eb46831273ba93978bfc3e25e6ff89ae9` |
| Public HTML SHA-256 | `0265eacdbd99f41cb017717f6282a28ce53252955972727d7d96d35d79e92620` |
| Previous running image / rollback target | `sha256:223aa812593d5bedd64e727dc9aa6aece90c99d21dae38a6adf707068c60046e` |

The separately named candidate passed its SPA, authentication-challenge and
cache-policy checks and was removed before promotion. The existing registry
`latest` tag and `xaiht-app` systemd service were then updated; the live container
image and public HTTPS HTML matched the accepted candidate. An anonymous
`POST /api/rc/result` returned **401**, the expected RC **Bearer** challenge and
**Cache-Control: no-store**. These are production deployment and authentication-
boundary observations. They do not prove an authenticated production prompt.

The [production rollout receipt](../../../../artifacts/rc-rollout-20261123/website-rollout.json)
records `PRODUCTION_VERIFIED`, the previous running/registry image identities,
candidate cleanup and public checks. The existing previous image is the recorded
rollback target; `rollback-website.ps1` restores it through the existing service
without dropping command tables or user data. A production rollback was not
required or performed. `candidate-assets.json` records the matching candidate's
JavaScript/CSS hashes for the separate browser verification.

The subsequent **headed production-browser check passed 30/30 assertions** and
saved four screenshots in `website-visible-01`. English and Spanish anonymous
views at 390px and 320px reached the sign-in gate, could not submit prompts, and
had no horizontal overflow. Served HTML and both JavaScript/CSS assets matched
the intended SHA-256 values; the published bundle contained the typed remote-chat
and complete-answer controls. The result endpoint rejected anonymous access with
401/Bearer/no-store; the Google authentication route returned its expected 302
without following login. No uncaught page JavaScript errors were observed.
See [the production browser result](../../../../artifacts/rc-rollout-20261123/website-visible-01/results.json).
This run accessed no user cookies, performed no Google sign-in, and tested no
authenticated account, real prompt/tool execution or physical cellphone. It is
separate from the earlier 76-check source harness with live model calls.

The native update replaced only **Tray (281 files)** and **RCServer (395 files)**
under `C:\Program Files\XAIHT\Tlachihuani`. Both complete self-contained
Release/win-x64 payloads retained the already declared **0.39.0** product version.
Every installed file matched the fresh SHA-256 manifest after the elevated update
and again in the verification pass. The generation was
`9f9f76a3ddfc486da1a62d4b81128959`; see
[the apply receipt](../../../../artifacts/rc-rollout-20261123/native-update-result.json)
and [the payload verification](../../../../artifacts/rc-rollout-20261123/native-verify-result.json).

Installation identities and the existing RC state, including account bindings,
were preserved. Jobs, its queue/downloads, Windows registration and the saved
website-key file were not changed by the updater. Previous component folders and
a protected consistent RC-state/ACL snapshot remain available for rollback.
Rollback restores the old state with the old binaries because the updated state
can contain fields that the previous strict reader does not understand.

All three Windows services retained **Stopped / Manual**. XT was subsequently
launched from the original unelevated operator session; its startup trace records
the overview being shown and activated. The inspected Start/Desktop shortcuts
still target the installed tray with `--show`, and no user-directory environment
override was present. The overview inherits `ShowInTaskbar=false`, so a zero
`Process.MainWindowHandle` or omission from the automation window list did not
prove startup failure. Startup visibility and service readiness were checked
separately.

Angela then opened the installed RC panel and selected **Start server**. The
authenticated read-only observation at **2026-11-24 02:52:01 UTC** confirmed RC
**Running**, health **ready**, listener **true**, and **zero pending operations**.
The main and Jobs services remained Stopped / Manual. All **11 preserved account
bindings** were returned, but their application adapters were **offline** because
the corresponding Tlamatini sessions were not running yet. The encrypted website
key file's SHA-256 remained identical to the prelaunch observation. See
[installed RC health](../../../../artifacts/rc-rollout-20261123/installed-rc-health.json).
After Angela opened the installed English Tlamatini chat, the subsequent
**02:53:35 UTC** native observation reported **angela / English ready**. The other
ten preserved bindings remained offline, RC remained ready, and the saved website
key was unchanged. Account readiness was established before the subsequent
website-account selection and phone tests below.

The [rollout audit](../../../../artifacts/rc-rollout-20261123/final-rollout-audit.json)
records unchanged Git HEADs and zero whitespace-check exits in all four examined
repositories: website, Tlachihuani, English Tlamatini and Spanish Tlamatini.
It also records successful MySQL cleanup, native deployment, production website
verification and the then-pending physical-phone round trip. The phone results
observed afterward are recorded separately below.

## Physical-phone progress and approval-policy finding

Angela selected the ready installed **angela / English** account on the website,
sent a genuine prompt from her cellphone and received the local Tlamatini answer.
**Prompt/answer round trip passed. Reloading preserved the same conversation.**
These are actual-phone observations, distinct from the source harness and its
mobile viewport screenshots. They do not certify every account or control path.

With **Ask before executing tools** checked, Angela then sent this prompt at
**21:06:28.970 local time on 23 November**:

> Tell me how many connections are active in this system?, use Monitor Netstat agent please, go

Netstat ran without presenting an approval request. The installed runtime trace
showed **Ask Execs enabled** and the approval broker registered. Native transport
review confirmed that `options.ask_execs_enabled` is forwarded unchanged through
XT, the durable operation and `adapter-next`; native RC does not automatically
approve tool execution. The runtime's existing selective policy explicitly
exempts this read-only Netstat operation. The observation was therefore **not a
lost checkbox flag**, but it exposed that the website's blanket wording promised
more than the actual policy. The earlier deterministic broker-denial fixture
proved the approval transport, not that every real tool enters the gate.

A genuine policy-gated tool must still be tested to demonstrate an approval
request, denial with no execution, and cancellation. No completion of those
acceptance steps is claimed by the successful read-only Netstat call.

## Remaining live acceptance

The matching website and native payloads are deployed, and Angela has started
Remote-Controller through **XT → Services → Remote-Controller → Start server**.
The intended installed English **angela** account was selected, and the actual
phone prompt/answer plus reload checks passed. Continue with a genuinely gated
operation to verify approval denial and no execution, followed by cancellation.
Clarify the approval setting's actual scope before claiming its user-facing
contract is satisfied. Reuse `inspect-installed-rc.ps1` from the visible rollout
console to confirm readiness during these checks. Do not use the standalone
panel's direct-browser invitation flow for this website relay, or substitute
desktop viewport emulation for remaining phone observations.

This path uses authenticated HTTPS. The website can handle and store prompt and
answer plaintext; it is not the separately planned end-to-end/PQ transport.

## Documentation closeout after tests were stopped

Angela subsequently confirmed that this exact physical-phone prompt showed an
approval request: `Use the Executer agent to run this exact read-only command once:
cmd.exe /d /c ver. Do not substitute another tool.` The next requested step was
Deny, but Angela answered **Stop tests**. Therefore approval display passed;
denial/no-execution and cancellation remain unverified. The installed log later
recorded the command completing successfully and the broker closing. This is not
evidence of a denied command executing: no corresponding Deny was established.

The corrected website label/help passed TypeScript, focused ESLint, **25/25**
transport/response tests and a production asset build. Cloud candidate
`0c291be3-539a-4d20-a4a6-a43188260c77` finished successfully before the stop could
cancel it. Its source manifest was
`d05c625b2931bdb68c821c0ad64ec23ec78742e8fac67220e0324f2fa416daf9`.
**It was not promoted.** The prior verified production image remains the deployment
recorded above. No native update was necessary for the Netstat exemption.

Saved, unrun files `test_remote_ask_execs_dispatch.py` in both editions and
`tests/integration/rc_ask_execs_checks.py` are not passing evidence. They include
a possible tool-alias gate regression found by source inspection. Runtime code
and the existing allowlist were not changed. Review/run that case when testing
is authorized again; do not certify it fixed or merge failing reproducers blindly.

On 23 November Angela separately authorized a documentation-only reconciliation
across all four repositories. Existing version values, private configurations,
runtime sources and Git history are preserved; no functional tests are resumed
by that documentation work. A later release/version operation needs her separate
instruction.
