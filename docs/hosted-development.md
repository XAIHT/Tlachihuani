# Hosted Tlamatini-RC development verification

This guide validates the new hosted client, cloud BFF and outbound PC connector
inside an isolated development environment. It does not install anything,
change xaiht.org, expose the PC to the Internet or touch the running Tlamatini
accounts. Torrenter remains independent.

## Run the verification

Use an existing PowerShell console on Angela's desktop. Bring it to the
foreground and leave it open; the runner checks the console before each step.
No second console is created.

1. Confirm the required tools are already installed: the .NET SDK selected by
   global.json, Node.js, and OpenSSL (PATH, Git for Windows or OpenSSL-Win64).
   Chrome is required for browser/OIDC checks. Docker with Linux containers is
   required for OIDC (its isolated Keycloak provider) and for the Docker option. The runner installs no tool.
2. From that PowerShell console, run:

       & 'C:\Development\XAIHT\repo\Tlachihuani\scripts\Test-HostedDevelopment.ps1' -IncludeBrowser -IncludeOidc -IncludeDocker

3. Read the live output. Browser checks use a visible browser. If the console
   visibility guard waits after a browser check, select the same console again.
   Existing test-port listeners cause a clear stop; no process is killed to
   obtain a port.
4. At completion, open the evidence directory printed by the runner. The
   console stays open. A failure produces a failing summary and a PowerShell
   error; it is not relabeled as a pass.

For the API/connector campaign without a browser, run the same script without
switches. Add -IncludeBrowser for the English/Spanish UI campaign, -IncludeOidc
for the real OIDC protocol campaign using an isolated Keycloak container, and -IncludeDocker for the Linux image
build, default-startup guard and real Linux HTTPS/BFF checks. These options are independent. Omitting an option
does not count its checks as passed.

## What the runner does

- Creates a unique directory under artifacts/rc-hosted-validation, outside
  Windows profiles. Scratch, generated keys and temporary caches stay there.
- Restores the RC/server and cloud dependency graphs in locked mode, preserving
  their package-lock expectations, then builds development Release outputs.
  No release installer, Git tag or publication is created.
- Generates a two-day self-signed RSA/SHA-256 TLS certificate with IP SANs for
  127.0.0.1 and 127.0.0.2. Clients pin this isolated certificate; nothing is added
  to the Windows certificate trust store. Windows Schannel uses an OS-managed
  MachineKeySet private-key import outside user profiles, without PersistKeySet;
  the process releases it when disposing the certificate. Linux keeps the key
  ephemeral. There is no UserKeySet fallback. SDK automatic developer-certificate
  generation is disabled for the run. The PFX password is passed to OpenSSL through
  an environment variable rather than printed in a command line.
- Runs the real isolated RC host and cloud BFF with explicit synthetic accounts
  and a deterministic main-application adapter. The PC connector authenticates
  outward to the cloud relay. Tests preserve the local account/device permission
  checks and verify opaque bounded relay routing.
- Optionally runs the existing English/Spanish headed-browser checks and the
  OIDC campaign. Their own result/cleanup records are retained independently.
- Optionally builds the Linux cloud image, then verifies that starting it without
  explicit development configuration exits with code 2. It also starts the real
  Linux application as a non-root user with a read-only root filesystem and
  checks pinned HTTPS, packaged assets, session rotation, CSRF, Origin, directory
  and logout inside its isolated network namespace. No host port is published.
  These BFF checks are not full container relay or production acceptance.
- Restores the caller's environment variables and current directory in finally.
  Harnesses close their own sockets, fixture servers, browsers and processes.
  Native cloud processes use the development-only --parent-stdin option: the
  owning harness sends the exact stop line (or closes stdin) and waits for normal
  host shutdown, so Windows certificate/key disposal completes. Forced termination
  is only an owned-process timeout fallback and must be reported.
  Unrelated services and applications are never selected by name for termination.

PowerShell 5 treats native stderr as ErrorRecords, including benign OpenSSL
progress output. The runner temporarily uses Continue for native execution and
checks LASTEXITCODE explicitly, so useful stderr is retained without hiding a
nonzero native exit.

## Read the evidence

Each run contains summary.json, transcript.txt and per-step logs. The native
campaign writes relay-and-browser/results.json and cleanup.json. Optional OIDC
writes oidc/results.json and cleanup.json. Optional Docker writes
container/results.json and cleanup.json. Browser screenshots and download
bytes are retained beneath their campaign directory when those checks ran.

The summary lists requested options, actual step exit codes and failures.
Synthetic adapter replies prove the transport/authorization behavior exercised
by the harness; they do not prove a live model response, installed release,
physical-phone behavior or Internet routing. Do not combine old and new runs
without preserving their origin and failures.

## Identity and security boundaries

The cloud is a cross-platform .NET application under src/Tlachihuani.RC.Cloud.
Its only enabled runtime profile is development-loopback: HTTPS, loopback
listener, loopback peers and explicit configuration. It reports
public_remote_enabled:false and post_quantum:false. A public address, plaintext
origin, absent configuration or strict post-quantum profile fails closed.

The lab relay is not end-to-end encrypted. Application JSON travels over two WSS
connections whose TLS terminates at the cloud relay, so the relay can inspect
that traffic. Opaque routing means the relay does not interpret the messages;
it does not mean their contents are hidden from the relay. The authenticated,
encrypted browser-to-PC handshake remains a production gate.

Fixture login uses generated test credentials. Optional OIDC instead performs
standard confidential authorization-code login with PKCE, nonce/state checks,
pinned provider TLS and signature/issuer/audience/lifetime validation. It never
falls back to fixture login. The issuer and immutable subject must already be
explicitly linked to a cloud user; matching an email or display name is never
sufficient. Tokens are not stored in browser storage.

The OIDC provider must use a different loopback IP from the cloud (for example
127.0.0.2 versus 127.0.0.1), because cookies are not isolated by port. The fixed
callback is the cloud origin plus /signin-oidc. The browser sees the same
opaque HttpOnly cloud session whether it signed in through fixtures or OIDC.

Cloud link removal is durable: owner-authenticated DELETE of a linked account
persists its deny record before acknowledging success, ends its routes and
survives cloud restart. A website sign-in cannot recreate the local PC's device
grant. Corrupted or unreadable deny state fails closed. Distributed storage,
backup rollback protection and public identity deployment require their own
release verification.

## Before publishing anything

The container definition is a build artifact, not permission or readiness to
publish. The stronger remote design still requires the reviewed ML-KEM-1024 /
ML-DSA-87 session profile and independently trusted client distribution, or an
explicitly accepted alternative hosted-code trust profile. A normal website or
installed PWA cannot make its own mutable JavaScript independently trustworthy.

The SDK and runtime container base images are pinned to exact SHA-256 digests.
The final release artifact still needs signing and a pinned published image
digest as part of its deployment acceptance.

Public DNS/TLS, real provider registration, production secrets, cloud isolation,
external-network/physical-phone tests, independent security review and installed
end-to-end acceptance are separate gates. The current development host must not
be exposed by weakening its loopback checks or adding an unrestricted proxy.

See [cloud implementation contract](../src/Tlachihuani.RC.Cloud/README.md),
[the hosted design](design/tlamatini-rc-internet-design.html), and
[visible execution policy](development-execution.md).

Verification from 15 November 2026: [443 distinct passing checks, evidence and remaining boundaries](verification/2026-11-15-rc-hosted-development.md).
