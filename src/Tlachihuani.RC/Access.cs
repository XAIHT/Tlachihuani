// Tlachihuani — Angela López Mendoza · XAIHT
using System.Security.Cryptography;
using System.Text;

namespace Tlachihuani.RC;

public sealed record Principal(string SessionId, string DeviceId, string BindingId, string[] Scopes, CancellationToken Revoked);
public sealed record SessionResult(string AccessToken, DateTimeOffset ExpiresAt, string DeviceId, string BindingId, string[] Scopes);
public sealed record ChallengeResult(string Id, string Challenge, DateTimeOffset ExpiresAt);
public sealed record InvitationResult(string Id, string Invitation, DateTimeOffset ExpiresAt);
public sealed record EnrollmentResult(string Id, string PollSecret, string Fingerprint, DateTimeOffset ExpiresAt);

public sealed class Access(StateStore store, RcConfiguration config, TimeProvider clock) : IDisposable
{
    private sealed record Challenge(string DeviceId, byte[] Bytes, DateTimeOffset Expires);
    private sealed record Session(string Id, string Device, DateTimeOffset Expires, CancellationTokenSource Cancellation);
    private readonly object gate = new();
    private readonly Dictionary<string, Challenge> challenges = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Session> sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string TokenHash, DateTimeOffset Expires)> tickets = new(StringComparer.Ordinal);

    public AccountBinding RegisterBinding(string edition, string accountId, string displayName, string windowsSid, string adapterId)
    {
        edition = RcProtocol.Edition(edition);
        accountId = RcProtocol.RequireId(accountId, "immutable account ID");
        displayName = RcProtocol.Text(displayName, "account display name", 80);
        adapterId = RcProtocol.RequireId(adapterId, "adapter ID");
        if (string.IsNullOrWhiteSpace(windowsSid) || !windowsSid.StartsWith("S-1-", StringComparison.Ordinal) || windowsSid.Length > 184) throw new RcFault("invalid_request", "Invalid Windows identity.", 400);
        return store.Write(s =>
        {
            var existing = s.Bindings.Values.FirstOrDefault(b => b.Edition == edition && b.AccountId == accountId);
            if (existing is not null)
            {
                if (existing.WindowsSid != windowsSid || existing.AdapterId != adapterId) throw new RcFault("binding_conflict", "Account is already bound to another trusted adapter.");
                var enabled = existing with { Enabled = true, DisplayName = displayName, Revision = existing.Enabled ? existing.Revision : existing.Revision + 1 };
                s.Bindings[existing.Id] = enabled;
                return enabled;
            }
            if (s.Bindings.Count >= 128) throw new RcFault("binding_limit", "Account binding limit reached.", 429);
            var binding = new AccountBinding(RcProtocol.Id(), edition, accountId, displayName, windowsSid, adapterId);
            s.Bindings.Add(binding.Id, binding);
            StateStore.Event(s, clock, "account.bound", binding.Id);
            return binding;
        });
    }

    public void DisableBinding(string bindingId, string adapterId, string windowsSid)
    {
        var devices = store.Write(s =>
        {
            if (!s.Bindings.TryGetValue(bindingId, out var binding)) throw new RcFault("not_found", "Account binding not found.", 404);
            if (binding.AdapterId != adapterId || binding.WindowsSid != windowsSid) throw new RcFault("forbidden", "Account binding belongs to another adapter.", 403);
            s.Bindings[bindingId] = binding with { Enabled = false, Revision = binding.Revision + 1 };
            var ids = s.Devices.Values.Where(d => d.BindingId == bindingId).Select(d => d.Id).ToArray();
            foreach (var id in ids) s.Devices[id] = s.Devices[id] with { Revoked = true };
            var invitations = s.Invitations.Values.Where(i => i.BindingId == bindingId).Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var id in invitations) s.Invitations[id] = s.Invitations[id] with { Used = true };
            foreach (var enrollment in s.Enrollments.Values.Where(e => invitations.Contains(e.InvitationId) && e.Status == "pending").ToArray())
                s.Enrollments[enrollment.Id] = enrollment with { Status = "declined" };
            foreach (var item in s.Operations.Values.Where(o => o.BindingId == bindingId && o.State == "accepted").ToArray())
                s.Operations[item.Id] = item with { State = "cancelled", Error = "account_disabled" };
            // Revocation cannot prove an already dispatched external action stopped.
            // Record uncertainty before the runtime removes this adapter's heartbeat.
            foreach (var item in s.Operations.Values.Where(o => o.BindingId == bindingId && o.State == "running").ToArray())
            {
                s.Operations[item.Id] = item with { State = "outcome_unknown", Error = "account_disabled", PendingPermission = null };
                StateStore.Event(s, clock, "operation.uncertain", bindingId, item.Id, "outcome_unknown");
            }
            StateStore.Event(s, clock, "account.disabled", bindingId);
            return ids;
        });
        lock (gate)
            foreach (var key in sessions.Where(p => devices.Contains(p.Value.Device, StringComparer.Ordinal)).Select(p => p.Key).ToArray()) RemoveSession(key);
    }

    public InvitationResult CreateInvitation(string bindingId, string[] scopes, string windowsSid)
    {
        scopes = RcProtocol.ValidateScopes(scopes);
        var secret = RcProtocol.Token();
        var expires = clock.GetUtcNow().AddMinutes(5);
        return store.Write(s =>
        {
            var binding = Binding(s, bindingId);
            if (binding.WindowsSid != windowsSid) throw new RcFault("forbidden", "This Windows identity does not own the binding.", 403);
            foreach (var id in s.Invitations.Where(p => p.Value.ExpiresAt <= clock.GetUtcNow() && !s.Enrollments.Values.Any(e => e.InvitationId == p.Key && e.ExpiresAt > clock.GetUtcNow())).Select(p => p.Key).ToArray()) s.Invitations.Remove(id);
            if (s.Invitations.Count >= 64) throw new RcFault("invitation_limit", "Too many pending invitations.", 429);
            var invite = new Invitation(RcProtocol.Id(), RcProtocol.Hash(secret), binding.Id, scopes, expires);
            s.Invitations.Add(invite.Id, invite);
            StateStore.Event(s, clock, "enrollment.invited", binding.Id);
            return new InvitationResult(invite.Id, secret, expires);
        });
    }

    public EnrollmentResult Enroll(string invitation, string name, string publicKey)
    {
        if (invitation.Length != 64) throw new RcFault("invalid_invitation", "Invitation is invalid or expired.", 403);
        name = RcProtocol.Text(name, "device name", 80);
        var fingerprint = ValidateKey(publicKey);
        var poll = RcProtocol.Token();
        return store.Write(s =>
        {
            var invite = s.Invitations.Values.FirstOrDefault(i => i.TokenHash == RcProtocol.Hash(invitation));
            if (invite is null || invite.Used || invite.ExpiresAt <= clock.GetUtcNow()) throw new RcFault("invalid_invitation", "Invitation is invalid or expired.", 403);
            _ = Binding(s, invite.BindingId);
            foreach (var id in s.Enrollments.Where(p => p.Value.ExpiresAt <= clock.GetUtcNow()).Select(p => p.Key).ToArray()) s.Enrollments.Remove(id);
            if (s.Enrollments.Count >= 64) throw new RcFault("enrollment_limit", "Too many pending enrollments.", 429);
            var pending = new Enrollment(RcProtocol.Id(), RcProtocol.Hash(poll), invite.Id, name, publicKey, fingerprint, clock.GetUtcNow().AddMinutes(5));
            s.Invitations[invite.Id] = invite with { Used = true };
            s.Enrollments.Add(pending.Id, pending);
            StateStore.Event(s, clock, "enrollment.pending", invite.BindingId);
            return new EnrollmentResult(pending.Id, poll, fingerprint, pending.ExpiresAt);
        });
    }

    public object PollEnrollment(string id, string secret) => store.Read<object>(s =>
    {
        if (secret.Length != 64 || !s.Enrollments.TryGetValue(id, out var p) || p.PollHash != RcProtocol.Hash(secret) || p.ExpiresAt <= clock.GetUtcNow())
            throw new RcFault("enrollment_unavailable", "Enrollment is unavailable.", 404);
        return new { p.Status, p.DeviceId };
    });

    public DeviceGrant Approve(string enrollmentId, string fingerprint, string windowsSid)
    {
        return store.Write(s =>
        {
            if (!s.Enrollments.TryGetValue(enrollmentId, out var pending) || pending.Status != "pending" || pending.ExpiresAt <= clock.GetUtcNow()) throw new RcFault("enrollment_unavailable", "Enrollment is unavailable.", 404);
            var invite = s.Invitations[pending.InvitationId];
            var binding = Binding(s, invite.BindingId);
            if (binding.WindowsSid != windowsSid) throw new RcFault("forbidden", "This Windows identity does not own the account.", 403);
            if (fingerprint != pending.Fingerprint) throw new RcFault("identity_changed", "Device identity comparison does not match.");
            if (s.Devices.Count >= 256) throw new RcFault("device_limit", "Device limit reached.", 429);
            var device = new DeviceGrant(RcProtocol.Id(), binding.Id, pending.Name, pending.PublicKey, invite.Scopes, clock.GetUtcNow());
            s.Devices.Add(device.Id, device);
            s.Enrollments[pending.Id] = pending with { Status = "approved", DeviceId = device.Id };
            StateStore.Event(s, clock, "device.approved", binding.Id);
            return device;
        });
    }

    public void Decline(string enrollmentId, string windowsSid) => store.Write(s =>
    {
        if (!s.Enrollments.TryGetValue(enrollmentId, out var pending)) throw new RcFault("enrollment_unavailable", "Enrollment is unavailable.", 404);
        if (Binding(s, s.Invitations[pending.InvitationId].BindingId).WindowsSid != windowsSid) throw new RcFault("forbidden", "Wrong local account.", 403);
        if (pending.Status != "pending") throw new RcFault("enrollment_closed", "Enrollment already completed.");
        s.Enrollments[pending.Id] = pending with { Status = "declined" };
        return true;
    });

    public ChallengeResult CreateChallenge(string deviceId)
    {
        var device = store.Read(s => Device(s, deviceId));
        lock (gate)
        {
            Prune();
            if (challenges.Count >= 64) throw new RcFault("challenge_limit", "Too many authentication attempts.", 429);
            var id = RcProtocol.Id();
            var expires = clock.GetUtcNow().AddSeconds(60);
            var installation = store.Read(s => s.InstallationId);
            var bytes = Encoding.UTF8.GetBytes($"Tlachihuani-RC login v1\n{installation}\n{config.PublicOrigin}\n{device.Id}\n{id}\n{expires.ToUnixTimeSeconds()}\n{RcProtocol.Token()}");
            challenges.Add(id, new(device.Id, bytes, expires));
            return new(id, Convert.ToBase64String(bytes), expires);
        }
    }

    public SessionResult Establish(string deviceId, string challengeId, string signature)
    {
        lock (gate)
        {
            Prune();
            if (!challenges.Remove(challengeId, out var challenge) || challenge.DeviceId != deviceId || challenge.Expires <= clock.GetUtcNow())
                throw new RcFault("authentication_failed", "Authentication challenge is invalid or expired.", 401);
            var device = store.Read(s => Device(s, deviceId));
            bool valid;
            try
            {
                if (signature.Length > 256) throw new FormatException();
                using var key = ECDsa.Create();
                key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(device.PublicKey), out _);
                valid = key.VerifyData(challenge.Bytes, Convert.FromBase64String(signature), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException) { valid = false; }
            if (!valid) throw new RcFault("authentication_failed", "Device proof was refused.", 401);
            if (sessions.Count >= config.MaxSessions || sessions.Values.Count(s => store.Read(data => data.Devices.GetValueOrDefault(s.Device)?.BindingId) == device.BindingId) >= config.MaxAccountSessions)
                throw new RcFault("session_limit", "Active session limit reached.", 429);
            var token = RcProtocol.Token();
            var expires = clock.GetUtcNow().AddMinutes(config.SessionMinutes);
            sessions.Add(RcProtocol.Hash(token), new(RcProtocol.Id(), device.Id, expires, new()));
            return new(token, expires, device.Id, device.BindingId, device.Scopes);
        }
    }

    public Principal Authenticate(string? token, string? scope = null)
    {
        if (token is not { Length: 64 }) throw new RcFault("unauthorized", "A valid device session is required.", 401);
        lock (gate)
        {
            Prune();
            if (!sessions.TryGetValue(RcProtocol.Hash(token), out var session)) throw new RcFault("unauthorized", "Session expired or revoked.", 401);
            var device = store.Read(s => Device(s, session.Device));
            if (scope is not null && !device.Scopes.Contains(scope, StringComparer.Ordinal)) throw new RcFault("forbidden", "This device lacks the required permission.", 403);
            return new(session.Id, device.Id, device.BindingId, device.Scopes, session.Cancellation.Token);
        }
    }

    public void Revoke(string deviceId, string windowsSid)
    {
        store.Write(s =>
        {
            if (!s.Devices.TryGetValue(deviceId, out var device)) throw new RcFault("not_found", "Device not found.", 404);
            if (Binding(s, device.BindingId).WindowsSid != windowsSid) throw new RcFault("forbidden", "Wrong local account.", 403);
            s.Devices[device.Id] = device with { Revoked = true, Revision = device.Revision + 1 };
            foreach (var op in s.Operations.Values.Where(o => o.DeviceId == deviceId && o.State == "accepted").ToArray()) s.Operations[op.Id] = op with { State = "cancelled", Error = "device_revoked" };
            // A second valid device must not approve work whose originating
            // device just lost authority. Cancellation/late completion remain
            // possible; clearing consent does not assert that execution stopped.
            foreach (var op in s.Operations.Values.Where(o => o.DeviceId == deviceId && o.PendingPermission is not null).ToArray())
                s.Operations[op.Id] = op with { PendingPermission = null };
            StateStore.Event(s, clock, "device.revoked", device.BindingId);
            return true;
        });
        lock (gate)
            foreach (var key in sessions.Where(p => p.Value.Device == deviceId).Select(p => p.Key).ToArray()) RemoveSession(key);
    }

    public void Close(string token) { lock (gate) RemoveSession(RcProtocol.Hash(token)); }
    public string IssueTicket(string token)
    {
        _ = Authenticate(token);
        lock (gate)
        {
            Prune();
            if (tickets.Count >= 64) throw new RcFault("ticket_limit", "Too many pending connections.", 429);
            var ticket = RcProtocol.Token();
            tickets.Add(RcProtocol.Hash(ticket), (RcProtocol.Hash(token), clock.GetUtcNow().AddSeconds(10)));
            return ticket;
        }
    }
    public Principal ConsumeTicket(string ticket)
    {
        lock (gate)
        {
            Prune();
            if (ticket.Length != 64 || !tickets.Remove(RcProtocol.Hash(ticket), out var value) || value.Expires <= clock.GetUtcNow() || !sessions.TryGetValue(value.TokenHash, out var session))
                throw new RcFault("unauthorized", "Connection ticket expired or consumed.", 401);
            var device = store.Read(s => Device(s, session.Device));
            return new(session.Id, device.Id, device.BindingId, device.Scopes, session.Cancellation.Token);
        }
    }
    public int SessionCount { get { lock (gate) { Prune(); return sessions.Count; } } }
    public void EnsureSession(Principal principal)
    {
        lock (gate)
        {
            Prune();
            if (!sessions.Values.Any(s => s.Id == principal.SessionId && s.Device == principal.DeviceId)) throw new RcFault("unauthorized", "Session ended.", 401);
            _ = store.Read(s => Device(s, principal.DeviceId));
        }
    }
    private void Prune()
    {
        var now = clock.GetUtcNow();
        foreach (var key in challenges.Where(p => p.Value.Expires <= now).Select(p => p.Key).ToArray()) challenges.Remove(key);
        foreach (var key in tickets.Where(p => p.Value.Expires <= now).Select(p => p.Key).ToArray()) tickets.Remove(key);
        foreach (var key in sessions.Where(p => p.Value.Expires <= now).Select(p => p.Key).ToArray()) RemoveSession(key);
    }
    private void RemoveSession(string key)
    {
        if (!sessions.Remove(key, out var session)) return;
        session.Cancellation.Cancel(); session.Cancellation.Dispose();
        foreach (var ticket in tickets.Where(p => p.Value.TokenHash == key).Select(p => p.Key).ToArray()) tickets.Remove(ticket);
    }
    public static AccountBinding Binding(DurableState state, string id) => state.Bindings.TryGetValue(id, out var value) && value.Enabled
        ? value : throw new RcFault("account_unavailable", "Account binding is unavailable.", 403);
    public static DeviceGrant Device(DurableState state, string id)
    {
        if (!state.Devices.TryGetValue(id, out var device) || device.Revoked) throw new RcFault("device_unavailable", "Device is unknown or revoked.", 401);
        _ = Binding(state, device.BindingId);
        return device;
    }
    public static string ValidateKey(string publicKey)
    {
        try
        {
            if (publicKey.Length > 256) throw new FormatException();
            var bytes = Convert.FromBase64String(publicKey);
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(bytes, out var read);
            if (read != bytes.Length || key.KeySize != 256 || key.ExportParameters(false).Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value) throw new CryptographicException();
            return Convert.ToHexStringLower(SHA256.HashData(bytes));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException) { throw new RcFault("invalid_key", "A P-256 SPKI device key is required for the local development profile.", 400); }
    }
    public void Dispose() { lock (gate) { foreach (var key in sessions.Keys.ToArray()) RemoveSession(key); challenges.Clear(); tickets.Clear(); } }
}
