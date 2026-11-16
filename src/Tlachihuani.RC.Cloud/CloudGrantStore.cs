// Tlachihuani — Angela López Mendoza · XAIHT
using System.Text.Json;

namespace Tlachihuani.RC.Cloud;

/// <summary>Durable deny records; changing cloud login never recreates a revoked machine link.</summary>
public sealed class CloudGrantStore
{
    private readonly object gate = new();
    private readonly string path;
    private GrantState state;
    public CloudGrantStore(CloudConfiguration configuration)
    {
        path = configuration.StatePath;
        CloudConfiguration.RejectProfilePath(path);
        if (!File.Exists(path)) { state = new(); return; }
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length > 1048576) throw new InvalidDataException("Cloud deny state is too large.");
        state = JsonSerializer.Deserialize<GrantState>(bytes, CloudConfiguration.Json)
            ?? throw new InvalidDataException("Invalid cloud deny state.");
        if (state.Schema != 1 || state.Revision < 0 || state.RevokedLinks is null || state.RevokedLinks.Count > 10000
            || state.RevokedLinks.Any(value => value.Split('/').Length != 3 || value.Split('/').Any(part => !CloudConfiguration.Id(part)))
            || state.RevokedLinks.Distinct(StringComparer.Ordinal).Count() != state.RevokedLinks.Count)
            throw new InvalidDataException("Invalid cloud deny state.");
    }
    public long Revision { get { lock (gate) return state.Revision; } }
    public bool Allows(string installation, string binding, string owner)
    { lock (gate) return !state.RevokedLinks.Contains(Key(installation, binding, owner), StringComparer.Ordinal); }
    public long Revoke(string installation, string binding, string owner)
    {
        lock (gate)
        {
            var key = Key(installation, binding, owner);
            if (state.RevokedLinks.Contains(key, StringComparer.Ordinal)) return state.Revision;
            if (state.RevokedLinks.Count >= 10000) throw new CloudFault("deny_store_full", 503);
            var next = new GrantState { Revision = checked(state.Revision + 1), RevokedLinks = [.. state.RevokedLinks, key] };
            try { Save(next); } // Durable denial precedes acknowledgement and route cancellation.
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            { throw new CloudFault("deny_persistence_unavailable", 503); }
            state = next;
            return state.Revision;
        }
    }
    private void Save(GrantState next)
    {
        CloudConfiguration.RejectProfilePath(path);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        CloudConfiguration.RejectProfilePath(temporary);
        try
        {
            using (var file = new FileStream(temporary, new FileStreamOptions
            { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.WriteThrough }))
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(next, CloudConfiguration.Json);
                file.Write(bytes);
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static string Key(string installation, string binding, string owner) => installation + "/" + binding + "/" + owner;
    private sealed class GrantState
    {
        public int Schema { get; set; } = 1;
        public long Revision { get; set; }
        public List<string> RevokedLinks { get; set; } = [];
    }
}
