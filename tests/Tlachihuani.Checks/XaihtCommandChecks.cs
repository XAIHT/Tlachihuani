using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Tlachihuani.RC;
using Tlachihuani.Tray;

internal static class XaihtCommandChecks
{
    private sealed class Capture : HttpMessageHandler
    {
        internal readonly List<JsonObject> Values = [];
        internal bool LoseFirst;
        internal HttpStatusCode Status = HttpStatusCode.OK;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var value = JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!.AsObject();
            lock (Values) Values.Add(value);
            if (LoseFirst) { LoseFirst = false; throw new HttpRequestException("Injected lost receipt acknowledgement."); }
            return new HttpResponseMessage(Status) { Content = new StringContent("{}") };
        }
    }
    private sealed class VaryingContent : HttpMessageHandler
    {
        internal int Calls;
        internal string? First;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Calls++;
            var json=System.Text.Json.JsonSerializer.Serialize(new {text="Saved response",html=$"<a data-rc-file=\"issued-{Calls}\">"+new string('x',30000)+"</a>"});
            First??=json;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json,Encoding.UTF8,"application/json")});
        }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static JsonArray Envelope(string binding, string request, string delivery) => new(new JsonObject
    {
        ["id"] = 1, ["delivery_id"] = delivery,
        ["payload"] = new JsonObject { ["schema"] = "xaiht.tlamatini-rc.command/1", ["request_id"] = request,
            ["operation"] = "conversation.submit", ["binding_id"] = binding, ["payload"] = new JsonObject { ["text"] = "Accents: á — emoji: 🦋" } }
    });
    private static async Task Finish(XaihtCommands relay, CancellationToken token)
    {
        while (relay.ActiveCount != 0) await Task.Delay(20,token);
    }
    internal static async Task Run()
    {
        var identity = new XaihtIdentity(new string('a',64),"http://127.0.0.1:19400",1,1);
        var settings = new XaihtLinkSettings(identity.Origin,"xrc_"+new string('a',43));
        var binding=RcProtocol.Id(); var request=RcProtocol.Id(); var operation=RcProtocol.Id();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token=timeout.Token;
        using(var frame=new MemoryStream())
        {
            var multilingual=new JsonObject{["text"]=new string('界',7999)};
            await Tlachihuani.Contracts.Protocol.WriteAsync(frame,multilingual,token,tracePayload:false,compactUtf8:true);
            Require(frame.Length<25000,"Unicode request expanded beyond its UTF-8 budget.");
            frame.Position=0;
            var back=await Tlachihuani.Contracts.Protocol.ReadAsync<JsonObject>(frame,token,tracePayload:false);
            Require((string?)back["text"]==new string('界',7999),"Native Unicode roundtrip changed text.");
        }
        using(var handler=new Capture{LoseFirst=true})
        using(var http=new HttpClient(handler))
        {
            var calls=0;
            var relay=new XaihtCommands((local,_) =>
            {
                calls++; Require((string?)local["command"]?["request_id"]==request,"Original request ID lost.");
                return Task.FromResult(new JsonObject{["operation"]=new JsonObject{["id"]=operation,["state"]="completed",["result"]=new JsonObject{["text"]="Respuesta: á 🦋"}}});
            });
            var batch=Envelope(binding,request,RcProtocol.Id());
            batch.Insert(0,new JsonObject{["id"]="not-a-number",["delivery_id"]=12,["payload"]=new JsonArray()});
            batch.Insert(1,new JsonObject{["id"]=1.5,["delivery_id"]=RcProtocol.Id(),["payload"]=new JsonObject()});
            batch.Insert(2,new JsonObject{["id"]=-1,["delivery_id"]=RcProtocol.Id(),["payload"]=new JsonObject()});
            relay.Receive(http,settings,identity,batch,token,token);
            await Finish(relay,token);
            Require(calls==1,"Lost website ACK executed native work twice.");
            Require(handler.Values.Count==2,"Lost ACK was not retried.");
            Require(handler.Values[0].ToJsonString()==handler.Values[1].ToJsonString(),"Retry changed sequence or payload.");
            Require(!handler.Values[0].ContainsKey("error"),"Success must omit error, never emit null.");
            Require((string?)handler.Values[0]["result"]?["operation"]?["result"]?["text"]=="Respuesta: á 🦋","Unicode answer changed.");
        }
        using(var handler=new Capture())
        using(var http=new HttpClient(handler))
        {
            var calls=0;
            var relay=new XaihtCommands((_,_) =>
            {
                calls++;
                return Task.FromResult(new JsonObject{["operation"]=new JsonObject{["id"]=operation,["state"]=calls==1?"running":"completed",
                    ["pending_permission"]=calls==1?new JsonObject{["request_id"]="approval-1",["type"]="permission"}:null}});
            });
            relay.Receive(http,settings,identity,Envelope(binding,request,RcProtocol.Id()),token,token);
            await Finish(relay,token);
            Require(handler.Values.Count==2,"Receipt watch missed completion.");
            Require((string?)handler.Values[0]["status"]=="running" && (string?)handler.Values[1]["status"]=="completed","Watch lifecycle wrong.");
            Require((string?)handler.Values[0]["result"]?["operation"]?["pending_permission"]?["request_id"]=="approval-1","Pending consent was lost.");
            Require((long?)handler.Values[1]["sequence"]==2,"Watch sequence did not advance.");
        }
        using(var handler=new Capture{Status=HttpStatusCode.Conflict})
        using(var http=new HttpClient(handler))
        {
            var calls=0;
            var relay=new XaihtCommands((_,_)=>{calls++;return Task.FromResult(new JsonObject{["operation"]=new JsonObject{["id"]=operation,["state"]="running"}});});
            relay.Receive(http,settings,identity,Envelope(binding,request,RcProtocol.Id()),token,token);
            await Finish(relay,token); Require(calls==1 && handler.Values.Count==1,"Stale lease did not stop its watcher.");
        }
        using(var handler=new Capture())
        using(var http=new HttpClient(handler))
        {
            var relay=new XaihtCommands((_,_)=>throw new RcFault("website_not_approved","Select this account locally.",403));
            relay.Receive(http,settings,identity,Envelope(binding,request,RcProtocol.Id()),token,token);
            await Finish(relay,token);
            Require((bool?)handler.Values.Single()["ok"]==false && !handler.Values[0].ContainsKey("result"),"Error envelope is not exclusive.");
        }
        using(var content=new StringContent("12345",Encoding.UTF8))
        {
            try {await XaihtCommands.ReadBounded(content,4,token);throw new InvalidOperationException("Oversize body accepted.");}
            catch(InvalidDataException){}
        }
        using(var handler=new Capture())
        using(var http=new HttpClient(handler))
        {
            var release=new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelDelivered=false;var discoveryDelivered=false;
            var relay=new XaihtCommands((local,cancel)=>
            {
                if((string?)local["command"]?["operation"]=="accounts.list")
                {discoveryDelivered=true;return Task.FromResult(new JsonObject{["accounts"]=new JsonArray()});}
                if((string?)local["command"]?["operation"]=="conversation.cancel")
                {
                    cancelDelivered=true;
                    return Task.FromResult(new JsonObject{["operation"]=new JsonObject{["id"]=RcProtocol.Id(),["state"]="completed"}});
                }
                return release.Task.WaitAsync(cancel);
            });
            var held=new JsonArray();
            for(var i=1;i<=32;i++)
            {
                var envelope=Envelope(binding,RcProtocol.Id(),RcProtocol.Id())[0]!.DeepClone().AsObject();
                envelope["id"]=i;held.Add(envelope);
            }
            relay.Receive(http,settings,identity,held,token,token);
            Require(relay.ActiveCount==32,"Full watcher lane was not established.");
            var batch=Envelope(binding,RcProtocol.Id(),RcProtocol.Id());batch[0]!["id"]=100;
            var control=Envelope(binding,RcProtocol.Id(),RcProtocol.Id())[0]!.DeepClone();control["id"]=101;
            control["payload"]!["operation"]="conversation.cancel";batch.Add(control);
            relay.Receive(http,settings,identity,batch,token,token);
            Require(cancelDelivered,"Cancel was starved behind 32 active prompt watchers.");
            Require(relay.ActiveCount<=33,"Normal work bypassed the bounded watcher lane.");
            var discovery=Envelope(binding,RcProtocol.Id(),RcProtocol.Id());discovery[0]!["id"]=102;
            discovery[0]!["payload"]!["operation"]="accounts.list";
            relay.Receive(http,settings,identity,discovery,token,token);
            Require(discoveryDelivered,"Account discovery was starved after reload with 32 active prompt watchers.");
            Require(relay.ActiveCount<=34,"Discovery bypassed its independent bounded lane.");
            release.SetResult(new JsonObject{["operation"]=new JsonObject{["id"]=operation,["state"]="completed"}});
            await Finish(relay,token);
            Require(handler.Values.Any(value=>(long?)value["id"]==101),"Reserved control did not publish its result.");
            Require(handler.Values.Any(value=>(long?)value["id"]==102 && value["result"]?["accounts"] is JsonArray),"Reserved discovery did not publish its result.");
        }
        using(var handler=new Capture())
        using(var local=new VaryingContent())
        using(var http=new HttpClient(handler))
        {
            const string endpoint="http://127.0.0.1:19401/agent/remote-controller/response-content/?ticket=qa-ticket";
            var relay=new XaihtCommands((_,_)=>Task.FromResult(new JsonObject{["endpoint"]=endpoint}),()=>new HttpClient(local,disposeHandler:false));
            try
            {
                async Task<JsonObject> Chunk(int offset,int id,string ticket="qa-ticket")
                {
                    var batch=Envelope(binding,RcProtocol.Id(),RcProtocol.Id());batch[0]!["id"]=id;
                    batch[0]!["payload"]!["operation"]="response.content";
                    batch[0]!["payload"]!["payload"]=new JsonObject{["ticket"]=ticket,["offset"]=offset};
                    relay.Receive(http,settings,identity,batch,token,token);await Finish(relay,token);
                    return handler.Values.Last();
                }
                var first=(await Chunk(0,200))["result"]!;
                var second=(await Chunk((int)first["next_offset"]!,201))["result"]!;
                Require(local.Calls==2,"Cached chunk skipped actual signed-ticket validation.");
                Require((string?)first["sha256"]==(string?)second["sha256"],"Regenerated file tickets changed the immutable answer digest.");
                var assembled=Convert.FromBase64String((string)first["data"]!).Concat(Convert.FromBase64String((string)second["data"]!)).ToArray();
                Require(assembled.SequenceEqual(Encoding.UTF8.GetBytes(local.First!)),"Chunks did not reconstruct the first complete JSON snapshot.");
                Require((bool?)second["done"]==true,"Final snapshot chunk was not marked done.");
                relay.ClearSnapshots();
                var missing=await Chunk(24576,202);
                Require((string?)missing["error"]?["code"]=="response_snapshot_expired","Missing snapshot silently restarted at a nonzero offset.");
            }
            finally {relay.ClearSnapshots();}
        }
        Console.WriteLine("PASS website relay: lost ACK, stable sequence, Unicode, pending approval, terminal watch, stale lease, denial, bounded body.");
    }
}
