// Tlachihuani — Angela López Mendoza · XAIHT
using System.Text.Json;

namespace Tlachihuani.RC;

/// <summary>Single writer, copy-on-write snapshots. A failed durable commit never mutates live state.</summary>
public sealed class StateStore : IDisposable
{
    private readonly object gate = new();
    private readonly string path;
    private readonly FileStream ownership;
    private DurableState state;
    public const int MaxStateBytes = 32 * 1024 * 1024;
    public StateStore(string directory)
    {
        var root = Path.GetFullPath(directory);
        RejectProfileOrReparse(root);
        Directory.CreateDirectory(root);
        RejectProfileOrReparse(root);
        var lockPath = Path.Combine(root, "rc.lock");
        RejectProfileOrReparse(lockPath);
        ownership = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        path = Path.Combine(root, "state.json");
        try
        {
            if (File.Exists(path))
            {
                if (new FileInfo(path).Length > MaxStateBytes) throw new InvalidDataException("RC state exceeds the storage limit.");
                RejectProfileOrReparse(path);
                state = JsonSerializer.Deserialize<DurableState>(File.ReadAllBytes(path), RcProtocol.Json) ?? throw new InvalidDataException("RC state is empty.");
                if (state.Schema != 1 || !RcProtocol.IsId(state.InstallationId)) throw new InvalidDataException("Unsupported or invalid RC state.");
            }
            else { state = new(); Commit(state); }
        }
        catch { ownership.Dispose(); throw; }
    }
    public static void RejectProfileOrReparse(string path)
    {
        var full = Path.GetFullPath(path);
        foreach (var profile in ProfileRoots())
        {
            var excluded = Path.TrimEndingDirectorySeparator(Path.GetFullPath(profile));
            if (full.Equals(excluded, StringComparison.OrdinalIgnoreCase) || full.StartsWith(excluded + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("RC application state and scratch must be outside Windows user profiles.");
        }
        for (var current = full; current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("RC-owned paths must not contain reparse points.");
    }
    private static IEnumerable<string> ProfileRoots()
    {
        var current = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(current)) yield return current;
        if (!OperatingSystem.IsWindows()) yield break;
        yield return Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "Users");
        // Windows supports relocated profile roots and individual profiles on other
        // volumes. The service account's own profile does not identify those users.
        using var machine = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64);
        using var profiles = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList");
        if (profiles is null) throw new InvalidDataException("Windows profile locations could not be verified.");
        if (profiles.GetValue("ProfilesDirectory") is string parent && !string.IsNullOrWhiteSpace(parent))
            yield return Environment.ExpandEnvironmentVariables(parent);
        foreach (var name in profiles.GetSubKeyNames())
        {
            using var key = profiles.OpenSubKey(name);
            if (key?.GetValue("ProfileImagePath") is string directory && !string.IsNullOrWhiteSpace(directory))
                yield return Environment.ExpandEnvironmentVariables(directory);
        }
    }
    public T Read<T>(Func<DurableState, T> read)
    {
        lock (gate) return read(Clone(state));
    }
    public T Write<T>(Func<DurableState, T> mutation)
    {
        lock (gate)
        {
            var candidate = Clone(state);
            var value = mutation(candidate);
            Commit(candidate);
            state = candidate;
            return value;
        }
    }
    private static DurableState Clone(DurableState source) => JsonSerializer.Deserialize<DurableState>(JsonSerializer.SerializeToUtf8Bytes(source, RcProtocol.Json), RcProtocol.Json)!;
    private void Commit(DurableState candidate)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(candidate, RcProtocol.Json);
        if (bytes.Length > MaxStateBytes) throw new RcFault("storage_limit", "RC storage limit reached.", 507);
        var staging = path + "." + Guid.NewGuid().ToString("N") + ".pending";
        try
        {
            using (var file = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { file.Write(bytes); file.Flush(true); }
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    // Same-directory rename publishes the flushed snapshot in one step.
                    // Both files inherit the service-owned directory ACL. Do not use
                    // ReplaceFile's metadata-merging sequence, which has partial-failure states.
                    File.Move(staging, path, true);
                    break;
                }
                catch (Exception ex) when (attempt < 39 && (ex is IOException or UnauthorizedAccessException) && (ex.HResult & 0xffff) is 5 or 32 or 33)
                {
                    // Windows scanners can briefly deny replacement. Keep the previous
                    // durable snapshot/live state unchanged and bound the retry to one second.
                    Thread.Sleep(25);
                }
            }
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }
    public static void Event(DurableState state, TimeProvider clock, string kind, string? binding = null, string? operation = null, string outcome = "succeeded")
    {
        state.Events.Add(new(++state.Cursor, clock.GetUtcNow(), kind, binding, operation, outcome));
        if (state.Events.Count > 512) state.Events.RemoveRange(0, state.Events.Count - 512);
    }
    public void Dispose() => ownership.Dispose();
}
