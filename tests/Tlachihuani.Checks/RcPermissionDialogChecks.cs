// Tlachihuani — Angela López Mendoza · XAIHT
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Tlachihuani.Tray;

internal static class RcPermissionDialogChecks
{
    internal static int Run(string output)
    {
        output = Path.GetFullPath(output);
        Tlachihuani.RC.StateStore.RejectProfileOrReparse(output);
        Directory.CreateDirectory(output);
        var results = new List<object>(); Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); Application.EnableVisualStyles();
                using var owner = new Form { Text = "Development permission editor check", Size = new(640, 220), StartPosition = FormStartPosition.CenterScreen };
                owner.Show(); owner.Activate();
                foreach (var cancel in new[] { true, false })
                {
                    Exception? dialogFailure = null;
                    using var inspect = new System.Windows.Forms.Timer { Interval = 700 };
                    inspect.Tick += (_, _) =>
                    {
                        inspect.Stop();
                        var dialog = Application.OpenForms.Cast<Form>().SingleOrDefault(f => f.Text == "Remote-Controller · Permissions");
                        try
                        {
                            Require(dialog is not null && dialog.Visible && GetForegroundWindow() == dialog.Handle, "Visible foreground development dialog");
                            var controls = All(dialog!).ToArray();
                            var list = controls.OfType<CheckedListBox>().Single();
                            var save = controls.OfType<Button>().Single(b => b.Text == "Save permissions");
                            var dismiss = controls.OfType<Button>().Single(b => b.Text == "Cancel");
                            Require(list.Items.Count == 6 && list.CheckedItems.Count == 2, "Six separate choices preserve current selection");
                            var launch = Enumerable.Range(0,list.Items.Count).Single(i=>list.Items[i].ToString()=="Start Tlamatini");
                            var stop = Enumerable.Range(0,list.Items.Count).Single(i=>list.Items[i].ToString()!.StartsWith("Stop Tlamatini"));
                            list.SetItemChecked(launch,true);
                            Require(!list.GetItemChecked(stop), "Start does not imply Stop");
                            if (!cancel)
                            {
                                for(var i=0;i<list.Items.Count;i++)list.SetItemChecked(i,false);
                                Require(!save.Enabled,"Empty grant cannot be saved");
                                for(var i=0;i<list.Items.Count;i++)if(i==launch||list.Items[i].ToString() is "Conversation" or "View status")list.SetItemChecked(i,true);
                                Require(save.Enabled,"Valid choice enables Save");
                                Require(Screen.FromControl(dialog!).WorkingArea.Contains(dialog!.Bounds),"Dialog fits the desktop");
                                using var bitmap = new Bitmap(dialog.Width,dialog.Height);
                                using(var graphics=Graphics.FromImage(bitmap))graphics.CopyFromScreen(dialog.Location,Point.Empty,dialog.Size);
                                bitmap.Save(Path.Combine(output,"permission-editor.png"));
                            }
                            (cancel?dismiss:save).PerformClick();
                        }
                        catch(Exception ex){dialogFailure=ex;dialog?.Close();}
                    };
                    inspect.Start();
                    var selected=ServiceDialogs.RcDevicePermissions(owner,"Development browser · Test account · english",["monitor","chat"]);
                    if(dialogFailure is not null)throw dialogFailure;
                    Require(cancel?selected is null:selected is not null&&selected.Order().SequenceEqual(new[]{"chat","launch","monitor"}),cancel?"Cancel returns no change":"Save returns exactly the reviewed grant");
                }
                owner.Close();
                void Require(bool value,string name){if(!value)throw new Exception(name);results.Add(new{name,passed=true});Console.WriteLine("PASS permission UI: "+name);}
            }
            catch(Exception ex){failure=ex;}
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
        if(failure is not null){results.Add(new{name="Permission editor",passed=false,error=failure.ToString()});Console.Error.WriteLine(failure);}
        File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
        return failure is null?0:1;
    }
    private static IEnumerable<Control> All(Control parent){foreach(Control child in parent.Controls){yield return child;foreach(var nested in All(child))yield return nested;}}
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
}
