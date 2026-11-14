using System;
using System.IO;
using System.Reflection;
using System.ServiceProcess;
// Only compiled into the isolated packaging test. There is no product worker,
// network listener, shared pipe, or reference to real machine job state.
internal sealed class Fixture : ServiceBase {
    static void Main(){
        string file=Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().Location);
        if(file!="Tlachihuani.Jobs" && file!="Tlachihuani.Service" && file!="Tlachihuani.RC.Server")return;
        Run(new Fixture{ServiceName=Product.Name+(file.EndsWith(".RC.Server")?".RemoteController":file.EndsWith(".Jobs")?".Jobs":""),AutoLog=false});
    }
}
