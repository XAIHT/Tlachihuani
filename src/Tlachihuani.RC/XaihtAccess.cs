// Tlachihuani — Angela López Mendoza · XAIHT
// The website is an explicit additional trust boundary: ordinary HTTPS, not PQ/E2EE.
namespace Tlachihuani.RC;

/// <summary>Local operator approval for one website connection and immutable account.</summary>
public sealed class XaihtAccess(StateStore store, TimeProvider clock)
{
    public static XaihtIdentity Validate(XaihtIdentity identity)
    {
        if (identity.ConnectionId is not { Length: 64 } || identity.ConnectionId.Any(c => !char.IsAsciiHexDigit(c))
            || identity.ComputerId is < 1 or > 9007199254740991 || identity.UserId is < 1 or > 9007199254740991)
            throw new RcFault("invalid_request", "Invalid website connection identity.", 400);
        if (!Uri.TryCreate(identity.Origin, UriKind.Absolute, out var origin) || origin.UserInfo.Length != 0
            || origin.AbsolutePath != "/" || origin.Query.Length != 0 || origin.Fragment.Length != 0
            || (origin.Scheme != "https" && !(origin.Scheme == "http" && origin.IsLoopback))
            || identity.Origin != origin.GetLeftPart(UriPartial.Authority))
            throw new RcFault("invalid_request", "Website origin must be HTTPS (or local HTTP for development).", 400);
        return identity;
    }

    public string[] ApprovedBindings(XaihtIdentity identity, string windowsSid)
    {
        Validate(identity);
        return store.Read(s => s.XaihtGrants.Values.Where(g => g.Identity == identity
            && s.Bindings.TryGetValue(g.BindingId, out var b) && b.Enabled && b.WindowsSid == windowsSid
            && s.Devices.TryGetValue(g.DeviceId, out var d) && !d.Revoked && d.AuthenticationKind == "xaiht")
            .Select(g => g.BindingId).Distinct(StringComparer.Ordinal).ToArray());
    }

    public void SetAccounts(XaihtIdentity identity, string[] bindingIds, string windowsSid)
    {
        Validate(identity);
        if (bindingIds.Length > 16 || bindingIds.Distinct(StringComparer.Ordinal).Count() != bindingIds.Length
            || bindingIds.Any(id => !RcProtocol.IsId(id)))
            throw new RcFault("invalid_request", "Select at most 16 distinct enabled accounts.", 400);
        store.Write(s =>
        {
            foreach (var id in bindingIds)
                if (Access.Binding(s, id).WindowsSid != windowsSid)
                    throw new RcFault("forbidden", "Only the local account owner can authorize website access.", 403);
            foreach (var grant in s.XaihtGrants.Values.Where(g => g.Identity.ConnectionId == identity.ConnectionId).ToArray())
            {
                if (!s.Bindings.TryGetValue(grant.BindingId, out var binding) || binding.WindowsSid != windowsSid)
                    throw new RcFault("forbidden", "Website connection belongs to another Windows operator.", 403);
                if (grant.Identity != identity || !bindingIds.Contains(grant.BindingId, StringComparer.Ordinal)) Revoke(s, grant);
            }
            foreach (var id in bindingIds)
            {
                if (s.XaihtGrants.Values.Any(g => g.Identity == identity && g.BindingId == id
                    && s.Devices.TryGetValue(g.DeviceId, out var d) && !d.Revoked)) continue;
                if (s.Devices.Count >= 256 || s.XaihtGrants.Count >= 256)
                    throw new RcFault("device_limit", "Remote connection grant limit reached.", 429);
                var device = new DeviceGrant(RcProtocol.Id(), id, "xaiht.org · " + new Uri(identity.Origin).Host,
                    "", ["chat", "monitor"], clock.GetUtcNow(), AuthenticationKind: "xaiht");
                var grant = new XaihtGrant(RcProtocol.Id(), identity, id, device.Id);
                s.Devices.Add(device.Id, device); s.XaihtGrants.Add(grant.Id, grant);
                StateStore.Event(s, clock, "website.approved", id);
            }
            return true;
        });
    }

    public void RevokeConnection(string connectionId, string windowsSid) => store.Write(s =>
    {
        foreach (var grant in s.XaihtGrants.Values.Where(g => g.Identity.ConnectionId == connectionId).ToArray())
        {
            if (!s.Bindings.TryGetValue(grant.BindingId, out var b) || b.WindowsSid != windowsSid)
                throw new RcFault("forbidden", "Connection belongs to another Windows operator.", 403);
            Revoke(s, grant);
        }
        return true;
    });

    private void Revoke(DurableState state, XaihtGrant grant)
    {
        if (!state.Devices.TryGetValue(grant.DeviceId, out var device) || device.Revoked) return;
        state.Devices[device.Id] = device with { Revoked = true, Revision = device.Revision + 1 };
        foreach (var op in state.Operations.Values.Where(o => o.DeviceId == device.Id).ToArray())
        {
            if (op.State == "accepted") state.Operations[op.Id] = op with { State = "cancelled", Error = "device_revoked" };
            else if (op.PendingPermission is not null) state.Operations[op.Id] = op with { PendingPermission = null };
        }
        StateStore.Event(state, clock, "website.revoked", grant.BindingId);
    }

    public Principal Principal(XaihtIdentity identity, string bindingId, string windowsSid)
    {
        Validate(identity); RcProtocol.RequireId(bindingId, "account binding");
        return store.Read(s =>
        {
            var binding = Access.Binding(s, bindingId);
            if (binding.WindowsSid != windowsSid) throw new RcFault("forbidden", "Account belongs to another local operator.", 403);
            var grant = s.XaihtGrants.Values.FirstOrDefault(g => g.Identity == identity && g.BindingId == bindingId
                && s.Devices.TryGetValue(g.DeviceId, out var d) && !d.Revoked && d.AuthenticationKind == "xaiht");
            if (grant is null) throw new RcFault("website_not_approved", "Allow this account in XT → Services → xaiht.org → Accounts first.", 403);
            var device = Access.Device(s, grant.DeviceId);
            return new Principal("xaiht-" + grant.Id, device.Id, bindingId, device.Scopes, CancellationToken.None);
        });
    }

    public static string IdempotencyKey(XaihtIdentity identity, string requestId) => "xaiht:" + RcProtocol.Hash(
        identity.ConnectionId + "\n" + identity.ComputerId + "\n" + identity.UserId + "\n" + RcProtocol.RequireId(requestId, "request ID"));

    public OperationReceipt? Existing(Principal principal, string key) => store.Read(s =>
    {
        var device = Access.Device(s, principal.DeviceId);
        if (device.BindingId != principal.BindingId) throw new RcFault("forbidden", "Account mismatch.", 403);
        return s.Operations.Values.FirstOrDefault(o => o.BindingId == principal.BindingId && o.DeviceId == principal.DeviceId && o.Key == key);
    });
}
