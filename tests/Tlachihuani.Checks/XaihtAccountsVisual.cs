using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tlachihuani.RC;
using Tlachihuani.Tray;

internal static class XaihtAccountsVisual
{
    internal static int Run(string output)
    {
        output=Path.GetFullPath(output);StateStore.RejectProfileOrReparse(output);Directory.CreateDirectory(output);
        Exception? failure=null;var assertions=new List<string>();
        var thread=new Thread(()=>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);Application.EnableVisualStyles();
                using var resource=typeof(XaihtAccountsForm).Assembly.GetManifestResourceStream("Tlachihuani.XT.ico")!;
                using var icon=new Icon(resource);
                using var owner=new Form{Icon=icon,Text="XT website account access — visible QA",Size=new(650,200),StartPosition=FormStartPosition.CenterScreen};
                owner.Show();owner.Activate();
                var en=RcProtocol.Id();var es=RcProtocol.Id();string[] saved=[];
                Task<JsonObject> Native(JsonObject request,CancellationToken _)
                {
                    if((string?)request["op"]=="xaiht-set-accounts")
                    {saved=request["binding_ids"]!.Deserialize<string[]>()!;return Task.FromResult(new JsonObject{["saved"]=true});}
                    return Task.FromResult(new JsonObject{["approved"]=new JsonArray(),["accounts"]=new JsonArray(
                        new JsonObject{["id"]=en,["display_name"]="angela QA",["edition"]="english",["application_state"]="ready"},
                        new JsonObject{["id"]=es,["display_name"]="angela QA",["edition"]="spanish",["application_state"]="offline"})});
                }
                using var dialog=new XaihtAccountsForm(owner,new XaihtIdentity(new string('a',64),"https://xaiht.org",42,7),Native);
                using var timer=new System.Windows.Forms.Timer{Interval=1000};var phase=0;
                var focusWait=System.Diagnostics.Stopwatch.StartNew();var focusAttempts=0;
                timer.Tick+=(_,_)=>
                {
                    try
                    {
                        if(!dialog.Visible || GetForegroundWindow()!=dialog.Handle)
                        {
                            if(focusWait.Elapsed<TimeSpan.FromSeconds(15))
                            {
                                if(focusAttempts++<6) TryForeground(dialog);
                                return; // No tests or screenshots until foreground is observed.
                            }
                        }
                        Require(dialog.Visible && GetForegroundWindow()==dialog.Handle,"Accounts dialog is visibly foreground");
                        focusWait.Restart();focusAttempts=0;
                        var controls=All(dialog).ToArray();var list=controls.OfType<CheckedListBox>().Single();
                        var save=controls.OfType<Button>().Single(b=>b.Text=="Save account access");
                        if(phase==0)
                        {
                            Require(list.Items.Count==2 && list.CheckedItems.Count==0,"English and Spanish remain separate and unselected");
                            Require(Screen.FromControl(dialog).WorkingArea.Contains(dialog.Bounds),"Dialog fits visible desktop");
                            Capture(dialog,Path.Combine(output,"accounts-unselected.png"));
                            list.SetItemChecked(0,true);save.PerformClick();phase++;
                        }
                        else if(phase==1)
                        {
                            Require(saved.SequenceEqual(new[]{en}),"Save authorizes only selected immutable English binding");
                            dialog.Size=dialog.MinimumSize;Capture(dialog,Path.Combine(output,"accounts-minimum.png"));
                            list.SetItemChecked(0,false);save.PerformClick();phase++;
                        }
                        else
                        {Require(saved.Length==0,"Clearing selection saves complete website revocation");timer.Stop();dialog.Close();}
                    }
                    catch(Exception ex){failure=ex;timer.Stop();dialog.Close();}
                };
                dialog.Shown+=(_,_)=>{focusWait.Restart();TryForeground(dialog);timer.Start();};dialog.ShowDialog(owner);owner.Close();
                void Require(bool value,string label){if(!value)throw new InvalidOperationException(label);assertions.Add(label);Console.WriteLine("PASS "+label);}
            }
            catch(Exception ex){failure=ex;}
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
        File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{passed=failure is null,assertions,error=failure?.ToString()},new JsonSerializerOptions{WriteIndented=true}));
        if(failure is not null)Console.Error.WriteLine(failure);
        return failure is null?0:1;
    }
    private static IEnumerable<Control> All(Control root){foreach(Control child in root.Controls){yield return child;foreach(var item in All(child))yield return item;}}
    private static void Capture(Form form,string file)
    {
        Application.DoEvents();using var bitmap=new Bitmap(form.Width,form.Height);
        using(var graphics=Graphics.FromImage(bitmap))graphics.CopyFromScreen(form.Location,Point.Empty,form.Size);
        bitmap.Save(file);
    }
    [DllImport("user32.dll")]private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]private static extern bool BringWindowToTop(IntPtr window);
    [DllImport("user32.dll")]private static extern bool ShowWindow(IntPtr window,int command);
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
    [DllImport("kernel32.dll")]private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")]private static extern bool AttachThreadInput(uint source,uint target,bool attach);
    private static void TryForeground(Form dialog)
    {
        // QA only: a synchronous, bounded focus handoff, always detached before
        // returning to the message loop. No keyboard/mouse injection or Windows
        // foreground policy changes. Product windows use their normal behavior.
        ShowWindow(dialog.Handle,9);dialog.BringToFront();dialog.Activate();SetForegroundWindow(dialog.Handle);
        if(GetForegroundWindow()==dialog.Handle)return;
        var foreground=GetForegroundWindow();var current=GetCurrentThreadId();
        var other=GetWindowThreadProcessId(foreground,out _);var attached=false;
        try
        {
            if(other!=0 && other!=current) attached=AttachThreadInput(current,other,true);
            BringWindowToTop(dialog.Handle);SetForegroundWindow(dialog.Handle);
        }
        finally { if(attached)AttachThreadInput(current,other,false); }
    }
}
