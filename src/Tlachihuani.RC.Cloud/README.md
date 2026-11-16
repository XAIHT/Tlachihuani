# Tlachihuani-RC Cloud development host

Created for Angela López Mendoza · XAIHT.

This separate cross-platform .NET 10 host implements the development BFF,
computer directory and bounded opaque WebSocket relay. It does not execute
Tlamatini commands. The local Remote-Controller remains the authorization and
execution boundary; Torrenter is independent.

## Explicit boundary

Only --development <absolute-config.json>, optionally followed by
--parent-stdin for a harness-owned lifetime, is accepted. The configuration must
select development-loopback, an exact HTTPS loopback-IP origin and an explicit
PFX certificate. Both listeners and peers must be loopback. There is no HTTP,
public bind, cloud password fallback or default account. Optional OIDC uses a
separately pinned HTTPS loopback identity provider and never falls back to
fixture credentials.
No request URL, credential, ticket or content is routine telemetry.

This is a runnable development foundation, not production Internet acceptance.
Public deployment acceptance, independently trusted client distribution and
the reviewed ML-KEM-1024 / ML-DSA-87 transport remain release gates. OIDC code
flow and durable local link-denial records are implemented for the isolated lab. The host reports post_quantum:false and public_remote_enabled:false.
Fixture-mode credentials are explicit test data, never copied Tlamatini
passwords. OIDC users map by exact issuer and immutable subject. Sessions/routes
are lost on process restart; persisted link denials remain denied.

## Configuration contract

Schema 1 JSON has these fields (the computer list may be empty):

- authentication_mode: fixture (default for the explicit development launch)
  or oidc; never an automatic fallback.
- state_path: optional absolute application-owned deny-state file. Default:
  the supplied configuration path plus .state.json.

- schema: 1; security_profile: development-loopback.
- origin: for example https://127.0.0.1:12490, with no path/trailing slash.
- certificate_path: absolute PFX path outside Windows user profiles;
  certificate_password: the supplied isolated certificate password.
- users: {id,username,password_salt,password_hash} records. IDs are 32
  lowercase hex digits. Password salt is base64 (16–64 bytes), and password hash
  is base64 PBKDF2-HMAC-SHA256, 210,000 iterations, 32 output bytes. These two
  password fields apply only in fixture mode. In OIDC mode each user instead
  requires oidc_issuer and oidc_subject, unique as an exact pair.
- computers: {installation_id,name,connector_token,accounts} records.
  Connector credentials are independent 64-character lowercase hex secrets.
  Account records are {binding_id,owner_id,edition,display_name}; edition is
  english or spanish, and immutable IDs use the same 32-hex format.

The harness must generate its own random credentials/certificate under the
application-owned artifacts tree. It pins that certificate explicitly; nothing
is installed in the machine certificate store. On Windows, Schannel requires a
CryptoAPI private-key import with MachineKeySet (without PersistKeySet); its
OS-managed key material lives outside user profiles while the certificate is
owned by this process and is released on certificate disposal, including startup
failure. Linux uses EphemeralKeySet. No UserKeySet fallback is allowed.
No scratch storage under user
profiles, %TEMP%, %TMP% or AppData is used by this host.

## Browser/BFF contract

GET /api/hosted/bootstrap establishes an anonymous pre-auth session if needed
and returns {mode,profile,post_quantum,public_remote_enabled,user,csrf_token}.
The mode is hosted, and user is null or {id,username}. The response also gives
authentication_mode and login_url (/auth/login for OIDC, otherwise null). Anonymous bootstrap
also returns a CSRF token so login itself is protected.

- POST /api/hosted/login: JSON {username,password}, exact Origin and
  X-CSRF-Token; rotates the session and CSRF token and returns bootstrap shape.
- POST /api/hosted/logout: exact Origin and CSRF; invalidates the session,
  tickets and active routes immediately; returns 204.
- GET /api/hosted/computers: authenticated owner-only directory. Other owners'
  accounts are filtered even on a shared computer. Cloud directory membership
  does not grant the local RC device any permission.
- POST /api/hosted/connections: {installation_id,binding_id,device_public_key,
  profile} with Origin/CSRF. Public key is base64 P-256 SPKI. Returns 201 and
  {ticket,expires_at}. This single-use routing ticket expires in 60 seconds
  and is bound to the exact browser session, owner, account, key and profile.

Session cookie: __Host-tlachihuani-hosted, Secure, HttpOnly, SameSite=Strict,
Path=/, no Domain attribute. Anonymous sessions expire after 10 minutes; signed-in
sessions expire after 8 hours. Sessions are random opaque server-side IDs.
There are at most 512 sessions, two concurrent fixture password checks, 30 fixture
password login attempts per minute and 200 total requests per second in this
isolated host. OIDC authorization also depends on the identity provider's sign-in
policy; the fixture password limit does not throttle /auth/login.

## Relay contract

All three WebSockets require subprotocol tlachihuani.relay.v1.

1. PC control: /relay/connector, Authorization: Bearer <connector_token> and
   X-Installation-Id. One active control connection per configured computer.
   PC sends {type:"heartbeat"} every 15 seconds (the cloud also accepts ping). Cloud answers
   {type:"pong"}. The lease expires after 60 seconds without a heartbeat.
2. Browser: /relay/client, authenticated session cookie and exact Origin.
   First text message must be {ticket} within three seconds. The relay emits
   {type:"open",channel_id,ticket,binding_id,device_public_key} to the PC; this
   ticket is a new PC-only credential, not the browser routing ticket.
3. PC data: /relay/connector/session, Authorization: Bearer <PC-only ticket>
   and X-Installation-Id. Ticket is single-use and pairing must finish within
   ten seconds. A second socket avoids multiplexing or arbitrary destination
   routing. Browser then receives {type:"ready",binding_id,profile}.
4. After ready, the relay transfers opaque complete binary/text messages with
   backpressure; it does not parse, persist or queue their content. Each message,
   including all fragments, is limited to 128 KiB. Here, opaque describes routing
   behavior, not cryptographic confidentiality: the lab sends application JSON
   over two WSS connections whose TLS terminates at the relay. The relay can
   inspect that traffic. There is no browser-to-PC end-to-end encryption in this
   development profile. Local account/device authorization is still checked.

Production acceptance requires a separately reviewed, authenticated encrypted
browser-to-PC session handshake, including the required post-quantum profile and
independently trusted client distribution. WSS alone does not satisfy that gate.

At most four pending/active routes per owner and sixteen total are accepted.
Offline computers reject connection requests immediately. Connector loss,
heartbeat expiry, session expiry/logout, pairing timeout or malformed/oversized
traffic closes both data sockets. There is no automatic replay, command queue,
remote shell, generic HTTP proxy or intranet forwarding capability.

## Build/container boundary

The project links the exact RC.Server/wwwroot assets at build/publish time;
no second web-source tree is created. The canonical Tlamatini-RC source still
must be mirrored by the existing development workflow. The project overrides
the solution's Windows target with net10.0. Its additional package is pinned
Microsoft.AspNetCore.Authentication.OpenIdConnect 10.0.12; lock files and NuGet
audit belong to the normal restore/review workflow.

Dockerfile is an image-build entry point using repository-root context.
It does not publish, provision DNS, change xaiht.org, register a service or start
anything by itself. Its default invocation deliberately fails closed without
an explicit development configuration; loopback binding cannot be used as a
public container port. A normal Docker port mapping does not turn this into
an Internet service. The SDK and runtime base images are already pinned to exact
SHA-256 digests. Reviewed production transport/authentication, signing the final
release artifact and pinning its published image digest remain public-deployment
gates.

All development commands/builds/tests on Angela's desktop must use the verified
visible foreground workflow in ../../docs/development-execution.md. Do not run
headless checks or reuse an old queue without authorization.

## OIDC development integration

With authentication_mode set to oidc, configure oidc.authority, client_id,
client_secret and certificate_sha256. The authority must use HTTPS on a distinct
loopback IP from the RC origin (for example 127.0.0.2 rather than 127.0.0.1).
Cookies are not port-scoped, so a second port on the same host is deliberately
rejected. The SHA-256 pin is 64 lowercase hex digits for the exact test leaf
certificate. Certificate validity dates and the certificate name are checked;
the self-signed chain is accepted only for that exact pin. Backchannel proxy,
redirects and cookies are disabled; all discovery, key and token requests remain
within the configured authority origin.

Register the confidential OIDC client's sole callback as the RC origin plus
/signin-oidc. GET /auth/login starts the standard authorization-code flow with
PKCE, state/correlation and nonce checks. Token validation checks signature,
issuer, audience and expiry. Only RS256, PS256 and ES256 are accepted in this
classical development profile. Tokens remain server-side transient values and
are not saved in the browser session. Email/name matches never create a link.
Unlisted issuer/subject pairs receive no cloud session. Fixture password login
is disabled while OIDC is selected.

Cloud sessions are minted in OnTicketReceived, after the middleware's complete
validation, then the browser returns to /remote.html. Invalid sign-in returns a
fixed error code; tokens, provider details and exception messages are not put
in the URL. Data-protection keys used for the temporary OIDC transaction are
ephemeral and never written to a user-profile directory. A host restart safely
invalidates an unfinished login.

Reference: Microsoft Learn, Configure OpenID Connect Web authentication in
ASP.NET Core 10: https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-oidc-web-authentication?view=aspnetcore-10.0

## Durable cloud link denial

DELETE /api/hosted/computers/{installationId}/accounts/{bindingId} requires an
authenticated owner, exact Origin and CSRF token. It persists a monotonic local
grant_revision and link denial before acknowledging success; active routes and
unused tickets for that link are then closed. Fresh directory requests omit the
link and connection attempts are denied. Restarting the host or signing in again
does not restore it. There is no browser endpoint to recreate the grant.

The development state is an atomically replaced application-owned JSON file.
Unreadable, malformed, excessive or linked/reparse state paths fail closed. This
is a single-host persistence implementation; production distributed storage,
backup/rollback protection and independent local-PC grant revocation still need
separate release acceptance. A local-PC grant is never created by this endpoint
or by a successful website login.

## Parent-owned development lifetime

A test harness may append --parent-stdin after --development <config.json>.
This optional switch requires redirected stdin and is available only in the
explicit development launch. The exact line stop, or EOF from the owning
parent, requests normal host shutdown and certificate disposal. Other lines
are ignored; the bounded reader accepts no commands or arbitrary execution.
An unrelated graceful host stop does not wait for a blocked stdin reader.

Standard launches and Docker arguments are unchanged. Harnesses should send
stop followed by a newline and wait for normal exit; forced termination is an
owned-process fallback after a reported timeout, not the normal cleanup path.
This matters on Windows because normal certificate disposal releases the
process-owned MachineKeySet import used by Schannel.
