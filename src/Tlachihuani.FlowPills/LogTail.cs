// Tlachihuani — Angela López Mendoza · XAIHT
// tlachihuani:trace-off (reads every agent log of every run twice a second)
using System.Text;

namespace Tlachihuani.FlowPills;

// Follows one log file the way tail -f does, independently of every other log: it reads only
// what was appended since the last look, never keeps the file open between looks (an agent that
// truncates or deletes its own log is never blocked by us), survives the file being recreated,
// and keeps the newest lines in memory, so a log stays readable after a Cleaner deletes it.
public sealed class LogTail
{
    public const int KeepLines = 4000;
    private const int MaxReadBytes = 1 << 20, FirstLookBytes = 256 * 1024, MaxLineChars = 16 * 1024;
    private readonly object gate = new();
    private readonly Queue<string> lines = new();
    private readonly StringBuilder partial = new();
    private Decoder decoder = NewDecoder();
    private long offset = -1;
    private long version;

    public LogTail(string name, string filePath)
    {
        Name = name;
        FilePath = filePath;
    }

    public string Name { get; }
    public string FilePath { get; }
    public bool Exists { get; private set; }
    public long Version => Interlocked.Read(ref version);

    // Reads what is new; true when the visible text changed.
    public bool Poll()
    {
        long length;
        try
        {
            var info = new FileInfo(FilePath);
            if (!info.Exists) { Exists = false; return false; }
            length = info.Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        Exists = true;
        lock (gate)
        {
            bool changed = false, skipFragment = false;
            if (offset < 0)
            {
                offset = Math.Max(0, length - FirstLookBytes);
                if (offset > 0) { Add("… the earlier lines are in " + FilePath); skipFragment = changed = true; }
            }
            else if (length < offset)
            {
                offset = 0;
                decoder = NewDecoder();
                partial.Clear();
                Add("— the log started again —");
                changed = true;
            }
            if (length > offset)
            {
                byte[] buffer;
                int read = 0;
                try
                {
                    using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
                    if (stream.Length < offset) return Changed(changed);
                    stream.Seek(offset, SeekOrigin.Begin);
                    int count = (int)Math.Min(stream.Length - offset, MaxReadBytes);
                    buffer = new byte[count];
                    while (read < count)
                    {
                        int got = stream.Read(buffer, read, count - read);
                        if (got == 0) break;
                        read += got;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Changed(changed); }
                if (read > 0)
                {
                    offset += read;
                    Append(buffer, read, skipFragment);
                    changed = true;
                }
            }
            return Changed(changed);
        }
    }

    // The newest lines, the unfinished last line included.
    public string[] Lines(int max = KeepLines)
    {
        lock (gate)
        {
            int extra = partial.Length > 0 ? 1 : 0;
            int skip = Math.Max(0, lines.Count + extra - max);
            var all = new List<string>(Math.Min(max, lines.Count + extra));
            foreach (var line in lines)
            {
                if (skip > 0) { skip--; continue; }
                all.Add(line);
            }
            if (extra == 1) all.Add(partial.ToString());
            return all.ToArray();
        }
    }

    private bool Changed(bool changed)
    {
        if (changed) Interlocked.Increment(ref version);
        return changed;
    }

    private void Append(byte[] buffer, int count, bool skipFragment)
    {
        var chars = new char[decoder.GetCharCount(buffer, 0, count, false)];
        decoder.GetChars(buffer, 0, count, chars, 0, false);
        int start = 0;
        if (skipFragment)
        {
            int newline = Array.IndexOf(chars, '\n');
            start = newline < 0 ? chars.Length : newline + 1;
        }
        for (int i = start; i < chars.Length; i++)
        {
            char c = chars[i];
            if (c == '\n')
            {
                if (partial.Length > 0 && partial[partial.Length - 1] == '\r') partial.Length--;
                Add(partial.ToString());
                partial.Clear();
            }
            else if (partial.Length < MaxLineChars) partial.Append(c);
        }
    }

    private void Add(string line)
    {
        lines.Enqueue(line);
        while (lines.Count > KeepLines) lines.Dequeue();
    }

    private static Decoder NewDecoder() => new UTF8Encoding(false, false).GetDecoder();
}
