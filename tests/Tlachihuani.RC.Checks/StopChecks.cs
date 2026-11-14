// Tlachihuani — Angela López Mendoza · XAIHT
using System.Text.Json;
using Tlachihuani.RC;

internal static class StopChecks
{
    private static JsonElement Empty => JsonSerializer.SerializeToElement(new { });
    private static Principal Grant(Fixture f)
    {
        var pending = f.Pending(["monitor", "stop"]);
        var device = f.Access.Approve(pending.Id, pending.Fingerprint, f.Sid);
        return f.Access.Authenticate(f.Login(device.Id).AccessToken);
    }
    private static void Require(bool value) { if (!value) throw new Exception("Stop boundary assertion failed."); }
    private static void Refuse(string code, Action action)
    {
        try { action(); } catch (RcFault fault) when (fault.Code == code) { return; }
        throw new Exception("Expected refusal: " + code);
    }
    internal static void Register(Action<string, Action<Fixture>> test)
    {
        test("213 stop: launch permission does not authorize shutdown", f => Refuse("forbidden", () => f.Ops.Submit(f.Principal(), "stop", "application.stop", Empty)));
        test("214 stop: browser cannot supply a process identifier", f => Refuse("invalid_request", () => f.Ops.Submit(Grant(f), "stop", "application.stop", JsonSerializer.SerializeToElement(new { pid = 1234 }))));
        test("215 stop: application adapter cannot claim shutdown", f => { f.Ops.Submit(Grant(f), "stop", "application.stop", Empty); Require(f.Ops.TakeNext(f.Adapter, f.Sid, "application") is null); });
        test("216 stop: only the account Windows broker claims shutdown", f => { var operation = f.Ops.Submit(Grant(f), "stop", "application.stop", Empty); Require(f.Ops.TakeNext("", "S-1-5-21-9", "broker") is null); Require(f.Ops.TakeNext("", f.Sid, "broker")?.Id == operation.Id); });
        test("217 stop: application loss does not obscure the broker outcome", f => { var p = Grant(f); var operation = f.Ops.Submit(p, "stop", "application.stop", Empty); f.Ops.TakeNext("", f.Sid, "broker"); f.Ops.AdapterDisconnected(f.Adapter); Require(f.Ops.Get(p, operation.Id).State == "running"); });
        test("218 stop: application adapter cannot forge shutdown success", f => { var operation = f.Ops.Submit(Grant(f), "stop", "application.stop", Empty); f.Ops.TakeNext("", f.Sid, "broker"); Refuse("forbidden", () => f.Ops.CompleteApplication(operation.Id, f.Adapter, f.Sid, Empty, null)); });
        test("219 stop: broker completion reconciles one durable receipt", f => { var p = Grant(f); var operation = f.Ops.Submit(p, "stop", "application.stop", Empty); f.Ops.TakeNext("", f.Sid, "broker"); f.Ops.Complete(operation.Id, "", f.Sid, Empty, null, true); Require(f.Ops.Get(p, operation.Id).State == "succeeded"); Require(f.Ops.Submit(p, "stop", "application.stop", Empty).Id == operation.Id); Require(f.Ops.TakeNext("", f.Sid, "broker") is null); });
        test("220 stop: revoked device cannot dispatch shutdown", f => { var p = Grant(f); var operation = f.Ops.Submit(p, "stop", "application.stop", Empty); f.Access.Revoke(p.DeviceId, f.Sid); Require(f.Ops.TakeNext("", f.Sid, "broker") is null); Require(f.Store.Read(s => s.Operations[operation.Id].State) == "cancelled"); });
        test("221 stop: shutdown cannot be scheduled accidentally", f => { var p = Grant(f); Refuse("forbidden", () => f.Ops.SaveSchedule(p, f.Schedule(p) with { Operation = "application.stop" }, null)); });
    }
}
