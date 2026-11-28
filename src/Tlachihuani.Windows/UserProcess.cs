// Tlachihuani — Angela López Mendoza · XAIHT
// tlachihuani:trace-off (read on every refresh of the services panel; the callers log what they decide and do)
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Tlachihuani.Contracts;

namespace Tlachihuani.Windows;

// A Tlachihuani service that runs in the signed-in user's session (HandGI, Flow Pills) writes who
// it is into its own folder: its PID, the time Windows created that process and its executable.
// XT acts on the process only while all three still match, so a stale file or a PID that Windows
// has given to another program is never touched.
public sealed record UserProcessRecord(int Pid, long Created, string Image, DateTimeOffset Started, string Detail = "")
{
    private const int MaxBytes = 4096;

    public static UserProcessRecord ForThisProcess(string detail = "")
    {
        using var self = ProcessIdentity.Open(Environment.ProcessId);
        return new(self.Id, self.CreationTime, self.ImagePath, DateTimeOffset.Now, detail);
    }

    // Written beside the old record and then moved over it, so a reader never sees half a record.
    public void Publish(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string partial = path + "." + Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + ".part";
        File.WriteAllText(partial, JsonSerializer.Serialize(this, Protocol.Json), new UTF8Encoding(false));
        File.Move(partial, path, overwrite: true);
    }

    public static UserProcessRecord? Read(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length is 0 or > MaxBytes) return null;
            var record = JsonSerializer.Deserialize<UserProcessRecord>(File.ReadAllText(path), Protocol.Json);
            return record is { Pid: > 0, Created: > 0 } && !string.IsNullOrWhiteSpace(record.Image) ? record : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    // Removes the record only while it still names the given process.
    public static void Clear(string path, int pid)
    {
        if (Read(path)?.Pid != pid) return;
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    // The live process this record names, if it is still that same process (same PID and creation
    // time) and runs an executable with the expected file name. Development and installed builds
    // live in different folders, so the folder is not compared; the creation time already rules
    // out a reused PID. terminate also asks for the right to end it (a user's own processes grant
    // it). Never returns the calling process.
    public ProcessIdentity? OpenVerified(string expectedFileName, bool terminate = false)
    {
        if (Pid == Environment.ProcessId) return null;
        ProcessIdentity identity;
        try { identity = ProcessIdentity.Open(Pid, terminate); }
        catch (Win32Exception ex) when (ex.NativeErrorCode is 5 or 87) { return null; }
        bool same = identity.CreationTime == Created && !identity.HasExited
            && SamePath(identity.ImagePath, Image)
            && string.Equals(Path.GetFileName(identity.ImagePath), expectedFileName, StringComparison.OrdinalIgnoreCase);
        if (same) return identity;
        identity.Dispose();
        return null;
    }

    public static bool SamePath(string left, string right)
    {
        try { return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }
}
