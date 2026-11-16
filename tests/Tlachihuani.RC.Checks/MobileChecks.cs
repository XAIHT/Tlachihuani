// Tlachihuani — Angela López Mendoza · XAIHT
using System.Text.Json;
using Tlachihuani.RC;
using Tlachihuani.RC.Server;
internal static class MobileChecks
{
    private static JsonElement J(string value) => JsonDocument.Parse(value).RootElement.Clone();
    private static void Require(bool value) { if (!value) throw new Exception("Mobile boundary assertion failed."); }
    private static void Refuse(string code, Action action)
    { try { action(); } catch (RcFault error) when (error.Code == code) { return; } throw new Exception("Expected refusal: " + code); }
    internal static void Register(Action<string, Action<Fixture>> test)
    {
        int number = 222;
        void Add(string name, Action<Fixture> action) => test($"{number++:000} mobile: {name}", action);
        Add("legacy chat payload remains accepted", _ => Operations.ValidateOperation("conversation.submit", J("{\"text\":\"hello\"}")));
        foreach (var key in new[] { "multi_turn_enabled", "exec_report_enabled", "acpx_enabled", "self_modify_enabled", "ask_execs_enabled", "step_by_step_enabled" })
        {
            Add(key + " is carried as a boolean", f => { var payload = JsonSerializer.SerializeToElement(new { text = "hello", options = new Dictionary<string, bool> { [key] = true } }); var receipt = f.Ops.Submit(f.Principal(), key, "conversation.submit", payload); Require(receipt.Payload.GetProperty("options").GetProperty(key).GetBoolean()); });
            Add(key + " rejects string coercion", _ => Refuse("invalid_request", () => Operations.ValidateOperation("conversation.submit", JsonSerializer.SerializeToElement(new { text = "hello", options = new Dictionary<string, string> { [key] = "false" } }))));
        }
        Add("unknown mode is refused", _ => Refuse("invalid_request", () => Operations.ValidateOperation("conversation.submit", J("{\"text\":\"x\",\"options\":{\"command\":true}}"))));
        Add("duplicate mode is refused", _ => Refuse("invalid_request", () => Operations.ValidateOperation("conversation.submit", J("{\"text\":\"x\",\"options\":{\"acpx_enabled\":true,\"acpx_enabled\":false}}"))));
        foreach (var key in new[] { "exec_report_enabled", "acpx_enabled", "ask_execs_enabled", "step_by_step_enabled" })
            Add(key + " requires multiturn", _ => Refuse("invalid_request", () => Operations.ValidateOperation("conversation.submit", JsonSerializer.SerializeToElement(new { text = "hello", options = new Dictionary<string, bool> { [key] = true, ["multi_turn_enabled"] = false } }))));
        Add("changing modes cannot reuse an execution key", f => { var p=f.Principal();f.Ops.Submit(p,"same","conversation.submit",J("{\"text\":\"x\",\"options\":{\"acpx_enabled\":false}}"));Refuse("idempotency_conflict",()=>f.Ops.Submit(p,"same","conversation.submit",J("{\"text\":\"x\",\"options\":{\"acpx_enabled\":true}}"))); });
        Add("offline queued requests expire without an adapter", f => {var p=f.Principal();var receipt=f.Ops.Submit(p,"offline","conversation.submit",J("{\"text\":\"x\"}"),1);f.Clock.Advance(TimeSpan.FromSeconds(2));f.Ops.Tick();Require(f.Ops.Get(p,receipt.Id).State=="failed");Require(f.Ops.Get(p,receipt.Id).Error=="deadline_exceeded");Require(f.Ops.TakeNext(f.Adapter,f.Sid,"application") is null);});
        Add("a dispatched request is never called failed just because it expired", f => {var receipt=f.Ops.Submit(f.Principal(),"running","conversation.submit",J("{\"text\":\"x\"}"),1);f.Ops.TakeNext(f.Adapter,f.Sid,"application");f.Clock.Advance(TimeSpan.FromSeconds(2));f.Ops.Tick();Require(f.Store.Read(s=>s.Operations[receipt.Id].State)=="running");});
        Add("existing chat grants do not acquire full workspace", f => Refuse("forbidden",()=>Operations.Permit(f.Principal(),"workspace")));
        Add("workspace is an explicit approvable grant", f => {var pending=f.Pending(["monitor","workspace"]);var device=f.Access.Approve(pending.Id,pending.Fingerprint,f.Sid);Operations.Permit(f.Access.Authenticate(f.Login(device.Id).AccessToken),"workspace");});
        foreach(var path in new[]{"/admin/","/agent/agentic_control_panel/","/agent/prompt_flow_panel/","/api/rc/v1/operations","/static/../config.json","/static/%2e%2e/config.json","/static/..\\config.json","//example.com/","/unknown/"})
            Add("route refused: "+path,_=>Require(!MobileRoutes.Allows(path,"GET",false)));
        foreach(var path in new[]{"/agent/agent/","/agent/torrenter/workspace/","/agent/torrenter/sources/","/agent/skills/","/agent/mobile/files/","/agent/mobile/canvas/save/","/agent/external_mcps/","/agent/contacts/"})
            Add("owned route admitted: "+path,_=>Require(MobileRoutes.Allows(path,"GET",false)));
        Add("assets cannot be written",_=>Require(!MobileRoutes.Allows("/static/agent/app.js","POST",false)));
        Add("unsupported HTTP verbs are refused",_=>Require(!MobileRoutes.Allows("/agent/agent/","CONNECT",false)));
        Add("only known live channels are admitted",_=>{Require(MobileRoutes.Allows("/ws/agent/","GET",true));Require(MobileRoutes.Allows("/ws/chat-voice/","GET",true));Require(!MobileRoutes.Allows("/ws/prompt-flow/","GET",true));Require(!MobileRoutes.Allows("/agent/agent/","GET",true));});
        foreach(var path in new[]{"/agent/pick_backup_directory/","/agent/pick_db_sqlite_file/","/agent/pick_context_directory/"})
            Add("host-native picker is replaced by mobile selection: "+path,_=>Require(!MobileRoutes.Allows(path,"POST",false)));
    }
}
