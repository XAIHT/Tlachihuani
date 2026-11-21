using System;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Web.Script.Serialization;
using System.Collections.Generic;
using Microsoft.Win32;
using System.Diagnostics;

// Verbose setup log, in the same line format as the services' trace logs. The wizard (run by
// a person) appends to that person's Tlachihuani.log; the Windows Installer actions (run as
// LocalSystem) append to %ProgramData%\XAIHT\Tlachihuani\logs\setup.log, which XT copies in.
internal static class SetupLog {
    static readonly object gate=new object();
    static readonly int pid=Process.GetCurrentProcess().Id;
    internal static void Write(string message,[System.Runtime.CompilerServices.CallerMemberName] string member="",[System.Runtime.CompilerServices.CallerFilePath] string file="",[System.Runtime.CompilerServices.CallerLineNumber] int line=0){Emit("INFO ",message,member,file,line);}
    internal static void Warn(string message,[System.Runtime.CompilerServices.CallerMemberName] string member="",[System.Runtime.CompilerServices.CallerFilePath] string file="",[System.Runtime.CompilerServices.CallerLineNumber] int line=0){Emit("WARN ",message,member,file,line);}
    internal static void Error(string message,[System.Runtime.CompilerServices.CallerMemberName] string member="",[System.Runtime.CompilerServices.CallerFilePath] string file="",[System.Runtime.CompilerServices.CallerLineNumber] int line=0){Emit("ERROR",message,member,file,line);}
    static string Target(){
        var me=WindowsIdentity.GetCurrent().User;
        if(me!=null && me.IsWellKnown(WellKnownSidType.LocalSystemSid)){
            string folder=Path.Combine(Ownership.Common,Product.Name,"logs");
            return Directory.Exists(folder)?Path.Combine(folder,"setup.log"):null;
        }
        string custom=Environment.GetEnvironmentVariable("TLACHIHUANI_USER_DIR");
        string user=String.IsNullOrWhiteSpace(custom)?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"XAIHT","Tlachihuani"):Path.GetFullPath(custom);
        Directory.CreateDirectory(user);
        return Path.Combine(user,"Tlachihuani.log");
    }
    static void Emit(string level,string message,string member,string file,int line){
        try{
            string path=Target();
            if(path==null)return;
            path=Path.GetFullPath(path);
            string text=DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.ffffff zzz",System.Globalization.CultureInfo.InvariantCulture)+"  "+level+"  setup  ["+pid+":"+System.Threading.Thread.CurrentThread.ManagedThreadId+"]  "
                +Path.GetFileName(file)+":"+line+" "+member+"  "+(message??"").Replace("\r"," ").Replace("\n"," ")+"\r\n";
            var bytes=new System.Text.UTF8Encoding(false).GetBytes(text);
            lock(gate){
                // Same named lock as TraceLog, so lines from XT and setup never interleave.
                using(var mutex=new System.Threading.Mutex(false,MutexName(path))){
                    bool locked=false;
                    try{
                        try{locked=mutex.WaitOne(2000);}catch(System.Threading.AbandonedMutexException){locked=true;}
                        using(var stream=new FileStream(path,FileMode.Append,FileAccess.Write,FileShare.ReadWrite|FileShare.Delete))stream.Write(bytes,0,bytes.Length);
                    }finally{if(locked)mutex.ReleaseMutex();}
                }
            }
        }catch(Exception){}
    }
    static string MutexName(string path){
        using(var sha=System.Security.Cryptography.SHA256.Create()){
            var hash=sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(path.ToUpperInvariant()));
            var hex=new System.Text.StringBuilder();
            for(int i=0;i<16;i++)hex.Append(hash[i].ToString("X2"));
            return @"Local\XAIHT.TraceLog."+hex;
        }
    }
}

internal static class Ownership {
    internal static readonly string Program = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "XAIHT", Product.Name);
    internal static readonly string Common = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "XAIHT");
    internal static string Image(bool jobs) { return Path.Combine(Program, jobs ? "Jobs" : "Service", jobs ? "Tlachihuani.Jobs.exe" : "Tlachihuani.Service.exe"); }
    internal static string Name(bool jobs) { return Product.Name + (jobs ? ".Jobs" : ""); }
    static string PlaneName(int plane) { return plane==2 ? Product.Name+".RemoteController" : Name(plane==1); }
    static string PlaneImage(int plane) { return plane==2 ? Path.Combine(Program,"RCServer","Tlachihuani.RC.Server.exe") : Image(plane==1); }
    static void Plain(string path) {
        for (var d = new DirectoryInfo(path); d != null; d = d.Parent) {
            try { if ((File.GetAttributes(d.FullName) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("A redirected path cannot be used: " + d.FullName); }
            catch (DirectoryNotFoundException) {} catch (FileNotFoundException) {}
        }
    }
    internal static void Inspect(bool remove, bool managed, string operatorSid) {
        Plain(Program); Plain(Common);
        string tray=Path.Combine(Program,"Tray","Tlachihuani.Tray.exe");
        if(!remove) foreach(var candidate in Process.GetProcessesByName("Tlachihuani.Tray")) using(candidate){
            try{
                if(!remove && String.Equals(candidate.MainModule.FileName,tray,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Choose Exit tray in XT before continuing.");
            }catch(System.ComponentModel.Win32Exception){throw new InvalidOperationException("Could not verify the running XT tray. Close XT in other signed-in sessions and retry.");}
        }
        if (!remove && !managed && Directory.Exists(Program)) throw new InvalidOperationException("This program folder already exists. Use the existing installation's maintenance tools. Setup will not overwrite it.");
        foreach (int plane in new[]{0,1,2}) {
            string name=PlaneName(plane);
            using(var k=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\"+name)) {
                if(k!=null) {
                    if(!managed || !String.Equals((string)k.GetValue("ImagePath"),"\""+PlaneImage(plane)+"\"",StringComparison.OrdinalIgnoreCase) ||
                       !String.Equals((string)k.GetValue("ObjectName"),@"NT SERVICE\"+name,StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("The existing "+name+" registration belongs to a different installation. Use its original maintenance tools.");
                    using(var svc=new ServiceController(name)) if(!remove && svc.Status!=ServiceControllerStatus.Stopped)
                        throw new InvalidOperationException("Choose XT → Stop Services, then Exit tray before continuing.");
                }
            }
            string data=Path.Combine(Common,name);
            Plain(data);
            if(!remove && Directory.Exists(data)) CheckExisting(data,plane!=0,operatorSid);
            if(remove && managed && Directory.Exists(data)) CheckExisting(data,plane!=0,null);
        }
        // Self-contained published payloads are verified by the package builder.
        // MSI ServiceControl stops the services during removal; Restart Manager
        // handles locked applications. Do not reject a normal Control Panel /x
        // because the services are still running before StopServices executes.
    }
    static DirectorySecurity Permissions(string service,string op,bool state) {
        var acl=new DirectorySecurity();
        string rights=state ? "(A;OICI;0x1301bf;;;"+service+")" : "(A;OICI;GRGX;;;"+service+")(A;OICI;GRGX;;;"+op+")";
        acl.SetSecurityDescriptorSddlForm("O:BAG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)"+rights);
        return acl;
    }
    static DirectorySecurity RcSettingsPermissions(string service,string op) {
        var acl=Permissions(service,op,true);
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(op),FileSystemRights.ReadAndExecute,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
        return acl;
    }
    static void Trusted(string path,bool sharedParent=false) {
        Plain(path);
        var acl=Directory.GetAccessControl(path);
        var owner=acl.GetOwner(typeof(SecurityIdentifier)).Value;
        if(owner!="S-1-5-18" && owner!="S-1-5-32-544") throw new InvalidOperationException("Unexpected owner of "+path);
        foreach(FileSystemAccessRule r in acl.GetAccessRules(true,true,typeof(SecurityIdentifier))) {
            if(r.AccessControlType!=AccessControlType.Allow || (r.PropagationFlags & PropagationFlags.InheritOnly)!=0)continue;
            string id=r.IdentityReference.Value;
            // ProgramData's shared vendor directory may grant create-child rights.
            // It must not grant replacement of an existing protected child.
            var unsafeRights=FileSystemRights.Delete|FileSystemRights.DeleteSubdirectoriesAndFiles|FileSystemRights.ChangePermissions|FileSystemRights.TakeOwnership;
            if(!sharedParent)unsafeRights|=FileSystemRights.Write;
            if(id!="S-1-5-18" && id!="S-1-5-32-544" && (r.FileSystemRights & unsafeRights)!=0)
                throw new InvalidOperationException("Untrusted write access on "+path);
        }
    }
    static void CheckExisting(string data,bool jobs,string op) {
        Trusted(data);
        var marker=Path.Combine(data,"setup-owner.txt"); var manifest=Path.Combine(data,"installation.json");
        Plain(marker);Plain(manifest);
        if(!File.Exists(marker)||File.ReadAllText(marker)!=Product.Family || !File.Exists(manifest))
            throw new InvalidOperationException("Unmanaged or incomplete installation state at "+data+". Preserve it for recovery.");
        var doc=new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(File.ReadAllText(manifest));
        Guid id;
        if(!Guid.TryParse(doc["installation_id"],out id)||id==Guid.Empty||(op!=null && doc["operator_sid"]!=op) ||
           (!jobs&&!String.Equals(doc["service_path"],Image(false),StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Retained state belongs to a different operator or installation.");
    }
    internal static void Provision(string op) {
        var sid=new SecurityIdentifier(op);
        if(!sid.IsAccountSid())throw new InvalidOperationException("A Windows operator account is required.");
        Plain(Common);
        if(!Directory.Exists(Common)) Directory.CreateDirectory(Common,Permissions("S-1-5-32-545",op,false));
        Trusted(Common,true);
        foreach(int plane in new[]{0,1,2}) {
            string name=PlaneName(plane), data=Path.Combine(Common,name);
            ServiceIdentity.Configure(name,op);
            string svc=((SecurityIdentifier)new NTAccount(@"NT SERVICE\"+name).Translate(typeof(SecurityIdentifier))).Value;
            SetupLog.Write("service "+name+" (account "+svc+") configured for operator "+op+"; data folder "+data+(Directory.Exists(data)?" (existing)":" (new)"));
            Plain(data);
            if(Directory.Exists(data)) CheckExisting(data,plane!=0,op);
            else {
                string stage=Path.Combine(Common,".setup-"+Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(stage,Permissions(svc,op,false));
                var doc=new Dictionary<string,string>{{"installation_id",Guid.NewGuid().ToString()},{"operator_sid",op}};
                if(plane==0)doc["service_path"]=Image(false);
                File.WriteAllText(Path.Combine(stage,"installation.json"),new JavaScriptSerializer().Serialize(doc));
                File.WriteAllText(Path.Combine(stage,"setup-owner.txt"),Product.Family);
                if(plane!=0)Directory.CreateDirectory(Path.Combine(stage,"state"),Permissions(svc,op,true));
                if(plane==2)Directory.CreateDirectory(Path.Combine(stage,"settings"),RcSettingsPermissions(svc,op));
                Directory.Move(stage,data);
            }
            // Verbose trace logs (service.log, jobs.log, web.log, torrent.log, rc.log): the service
            // writes, the operator reads (XT copies every line into the user's Tlachihuani.log).
            // Created for new and existing installations alike; its permissions are always reset.
            string logs=Path.Combine(data,"logs");
            Plain(logs);
            if(!Directory.Exists(logs)) Directory.CreateDirectory(logs,LogsPermissions(svc,op));
            else Directory.SetAccessControl(logs,LogsPermissions(svc,op));
            SetupLog.Write("log folder ready: "+logs+" (service "+svc+" writes, operator "+op+" reads)");
        }
    }
    static DirectorySecurity LogsPermissions(string service,string op) {
        var acl=new DirectorySecurity();
        acl.SetSecurityDescriptorSddlForm("O:BAG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;0x1301bf;;;"+service+")(A;OICI;GRGX;;;"+op+")");
        return acl;
    }
}
