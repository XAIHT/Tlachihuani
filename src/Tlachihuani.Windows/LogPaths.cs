// Tlachihuani — Angela López Mendoza · XAIHT
// tlachihuani:trace-off (path helpers asked several times a second; their lines say nothing)
using System.Security.AccessControl;
using System.Security.Principal;
using Tlachihuani.Contracts;

namespace Tlachihuani.Windows;

// Where every Tlachihuani process writes its trace log.
//
//   XT, the control tools, setup:  %LOCALAPPDATA%\XAIHT\Tlachihuani\Tlachihuani.log
//   Tlachihuani service:           %ProgramData%\XAIHT\Tlachihuani\logs\service.log
//   Jobs, its web facade, engine:  %ProgramData%\XAIHT\Tlachihuani.Jobs\logs\jobs.log, web.log, torrent.log
//   Remote-Controller:             %ProgramData%\XAIHT\Tlachihuani.RemoteController\logs\rc.log
//
// Setup creates each service's logs folder (service: modify, operator: read). A service
// installed before 0.38 writes under its state folder instead, in a logs folder it
// creates readable by the operator. XT copies every new service line into Tlachihuani.log.
public static class LogPaths
{
    public const long UserRotateBytes = 256L * 1024 * 1024;
    public const int UserKeepFiles = 40;            // up to about 10 GB of Tlachihuani.log
    public const long ServiceRotateBytes = 256L * 1024 * 1024;
    public const int ServiceKeepFiles = 4;          // XT keeps the full copy in Tlachihuani.log

    public static string UserFolder
    {
        get
        {
            var custom = Environment.GetEnvironmentVariable("TLACHIHUANI_USER_DIR");
            return string.IsNullOrWhiteSpace(custom)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XAIHT", "Tlachihuani")
                : Path.GetFullPath(custom);
        }
    }
    // A different user folder (TLACHIHUANI_USER_DIR, as tests use) also gets its own window, event
    // and pipe names, so a test HandGI or Flow Pills never meets the user's real one. Empty otherwise.
    public static string UserScope
    {
        get
        {
            var custom = Environment.GetEnvironmentVariable("TLACHIHUANI_USER_DIR");
            if (string.IsNullOrWhiteSpace(custom)) return "";
            var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(custom).ToUpperInvariant()));
            return "." + Convert.ToHexString(hash, 0, 6);
        }
    }
    public static string UserLog => Path.Combine(UserFolder, "Tlachihuani.log");
    public static string ScriptLogs => Path.Combine(UserFolder, "script-logs");

    private static string Common => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "XAIHT");
    public static string ServiceFolder => Path.Combine(Common, "Tlachihuani", "logs");
    public static string JobsFolder => Path.Combine(Common, "Tlachihuani.Jobs", "logs");
    public static string JobsStateFolder => Path.Combine(Common, "Tlachihuani.Jobs", "state", "logs");
    public static string RcFolder => Path.Combine(Common, "Tlachihuani.RemoteController", "logs");
    public static string RcStateFolder => Path.Combine(Common, "Tlachihuani.RemoteController", "state", "logs");

    // Every folder whose service logs XT copies into Tlachihuani.log.
    public static IReadOnlyList<string> CopiedFolders => [ServiceFolder, JobsFolder, JobsStateFolder, RcFolder, RcStateFolder, ScriptLogs];

    // Configures this process's trace log to the user's Tlachihuani.log.
    public static void UseUserLog(string component)
        => TraceLog.Configure(component, [UserLog], UserRotateBytes, UserKeepFiles);

    // Configures a service's trace log: the setup-created folder first, then a folder of its
    // own under its state (readable by the operator), then nothing (Windows Event Log only).
    public static void UseServiceLog(string component, string fileName, string operatorSid, params string[] folders)
    {
        var candidates = new List<string>();
        foreach (var folder in folders)
        {
            try { candidates.Add(Path.Combine(Prepare(folder, operatorSid), fileName)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException) { }
        }
        TraceLog.Configure(component, candidates, ServiceRotateBytes, ServiceKeepFiles);
    }

    // Creates a missing logs folder: full control for SYSTEM, Administrators and this
    // process's account, read for the operator (so XT can copy the lines).
    public static string Prepare(string folder, string operatorSid)
    {
        if (Directory.Exists(folder)) return folder;
        string self = WindowsIdentity.GetCurrent().User!.Value;
        _ = new SecurityIdentifier(operatorSid);
        var acl = new DirectorySecurity();
        acl.SetSecurityDescriptorSddlForm($"D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;FA;;;{self})(A;OICI;GRGX;;;{operatorSid})");
        new DirectoryInfo(folder).Create(acl);
        return folder;
    }
}
