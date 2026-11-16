# Hosted Tlamatini-RC development verification · 15 November 2026

Created for Angela López Mendoza · XAIHT.

## Delivery boundary

The hosted browser client, cross-platform cloud sign-in/directory/relay and
Remote-Controller-owned outbound connector are implemented for an explicit
HTTPS loopback development lab. English and Spanish interfaces are included.
This is a tested development foundation; it is not the complete public remote
product described by the approved design.

No xaiht.org deployment, DNS change, installer, service registration, commit,
push or tag was performed. Installed applications and real account databases
were not patched. Torrenter remains outside this connector's ownership and its
endpoint is explicitly rejected as a relay destination.

## Implemented source

- src/Tlachihuani.RC.Cloud: confidential OIDC authorization code + PKCE,
  exact issuer/subject linking, opaque HttpOnly browser sessions, owner-filtered
  computer directory, durable cloud link denials and bounded WebSocket relay.
  A separate explicit fixture login supports deterministic tests.
- src/Tlachihuani.RC.Server/HostedConnector.cs and runtime integration: pinned
  outward control/data connections, selected local account bindings, native
  configure/disconnect controls, heartbeat, cancellation and shutdown. Only a
  closed RC route/method/header set can reach the local service. Local approved
  device keys, permissions and revocation remain authoritative.
- Canonical Tlamatini-RC/public assets and RC.Server/wwwroot mirror: hosted
  account selection, English/Spanish UI, isolated saved browser identities,
  rich response rendering and original-byte file downloads. Full workspace and
  voice stay unavailable in this hosted profile.
- Dockerfile and locked dependencies: Linux cloud image with digest-pinned
  SDK/runtime base images. Test execution uses a non-root user, a read-only
  root filesystem, dropped capabilities and no published cloud ports.
- scripts/Test-HostedDevelopment.ps1: one foreground runner, generated
  loopback TLS material, real headed-browser/OIDC tests, Docker verification,
  retained results and owned-process cleanup. Scratch/cache/profile directories
  belong to the artifacts tree outside Windows user profiles.

## Verification

| Campaign | Passed | Evidence |
|---|---:|---|
| Relay/API and authorization | 51 | [results](../../artifacts/rc-hosted-validation/20261115-111926-7a0442dd/relay-and-browser/results.json) |
| Hosted headed browser (15 English + 15 Spanish + 4 shared) | 34 | [results](../../artifacts/rc-hosted-validation/20261115-111926-7a0442dd/relay-and-browser/browser/results.json) |
| Real Keycloak/OIDC | 25 | [results](../../artifacts/rc-hosted-implementation/oidc-final/results.json) |
| Linux HTTPS/BFF container | 8 | [results](../../artifacts/rc-hosted-implementation/container-final/results.json) |
| Existing RC core | 285 | [results](../../artifacts/rc-hosted-implementation/core-regressions/results.json) |
| Final local rich responses (20 English + 20 Spanish) | 40 | [results](../../artifacts/rc-rich-responses/1794763700687/results.json) |

**443 distinct checks passed; zero failed checks in the final campaign records.**

The latest combined runner retained an orchestration failure after its 85 passing
relay/browser checks because the development-console foreground guard timed out.
OIDC, Docker, local-browser and source checks then completed from the same built
source in the existing console with the authorized focus helper before each stage.
The original failure record remains preserved; it is not relabeled as a pass.
An earlier all-options run also completed successfully before the final race fixes.

The combined evidence is [the retained verification summary](../../artifacts/rc-hosted-implementation/verification-summary.json).
The [English guide](../hosted-development.md) and
[Spanish guide](../hosted-development.es.md) explain reproduction:

```powershell
& 'C:\Development\XAIHT\repo\Tlachihuani\scripts\Test-HostedDevelopment.ps1' -IncludeBrowser -IncludeOidc -IncludeDocker
```

Use the existing foreground PowerShell console. The browser is visible and
all commands preserve real exit codes. No system certificate trust is added.
The test identity provider uses a separate loopback IP because browser cookies
are not scoped by port. OIDC tests use a real Keycloak container and actual
browser code exchange; tokens are not retained in browser storage.

## Defects found and corrected during validation

- Chrome HTTP/2 WebSockets use extended CONNECT. The relay now accepts GET and
  CONNECT through the same authentication, Origin and single-use ticket checks.
- Browser-normalized lowercase headers now preserve authorization and request
  idempotency; conflicting case-folded header duplicates are rejected.
- OIDC issuer mapping uses the validated token issuer after framework claim
  processing, while cloud sessions are created only after full handler checks.
- Account changes cannot redirect delayed IndexedDB approval writes, delayed
  key generation, stale lifecycle/disconnect callbacks or prior chat completion
  into a newly selected account. A real cloud identity change clears prior
  private content. Usernames stay literal; execution labels are localized.
- Connector enable/disable transitions are serialized; shutdown rejects late
  upgraded control sockets. The regression stresses eight concurrent upgrades
  and requires graceful completion before the full server drain timeout.
- Windows TLS private keys use process-owned MachineKeySet import without
  PersistKeySet; graceful parent-stdin shutdown disposes certificate ownership.
  Linux uses ephemeral keys. No UserKeySet fallback or profile scratch is used.

Earlier failed runs remain under artifacts/rc-hosted-validation and
artifacts/rc-hosted-implementation. The final figures above count each distinct
check once; repeated campaigns are not added to inflate the total.

## What the evidence does and does not prove

The relay, cloud server, native RC control path, browser cryptographic keys,
OIDC middleware/provider, file streaming and Linux application are real. Main
Tlamatini account adapters and reply/file data in these campaigns are explicit
synthetic fixtures. They do not prove a live language-model conversation or
installed SCM acceptance. Desktop mobile-width checks are not physical-phone
or external-network tests. The Linux container campaign checks HTTPS/BFF
behavior; the complete relay campaign runs on Windows with the real Windows
connector.

The development relay can read its WSS JSON traffic. There is no inner
end-to-end encryption or post-quantum claim. Both runtime and UI report this;
public/nonloopback and post-quantum-required configurations fail closed.
The production design still needs the reviewed ML-KEM-1024 / ML-DSA-87 session
protocol and independently trusted client distribution (or a separately
approved trust profile), consumer PC/account linking and provisioning,
production identity/metadata/recovery operations, and installed, live-model,
physical-phone and external-network acceptance. These remain implementation
and acceptance work, not merely permission to flip a deployment switch.

## Source and cleanup evidence

All 18 packaged assets match byte-for-byte across the canonical
client, RC.Server mirror, built RC server and built cloud host. JavaScript syntax
checks passed for 10 client scripts and 5 harnesses. See
[asset hashes](../../artifacts/rc-hosted-implementation/asset-verification.json) and
[tested runtime/source hashes](../../artifacts/rc-hosted-implementation/tested-source-hashes.json).

Every final campaign cleanup record reports its owned processes/sockets/browser
closed. The final [process, container and port inventory](../../artifacts/rc-hosted-implementation/final-cleanup.json)
and [visible transcript](../../artifacts/rc-hosted-implementation/visible.log) record cleanup and compiler-server
shutdown. The development console is retained for inspection; its command queue
is ended. Existing applications and Docker Desktop are left alone.

Source whitespace verification passed; no repository history was changed.

Pre-existing version/package-lock changes and MobileRoutes.cs edits were
preserved. This task made no version change. The canonical Tlamatini-RC source
still exists separately and remains required by the existing source workflow.

References: [approved design](../design/tlamatini-rc-internet-design.html),
[cloud contract](../../src/Tlachihuani.RC.Cloud/README.md),
[ASP.NET Core WebSockets and HTTP/2 CONNECT](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/websockets?view=aspnetcore-10.0),
[Keycloak container documentation](https://www.keycloak.org/server/containers),
[Windows ephemeral certificate/SslStream issue](https://github.com/dotnet/runtime/issues/23749).
