// Tlachihuani — Angela López Mendoza · XAIHT
using System.Text;

namespace Tlachihuani.Tray;

// Tlachihuani.log: XT's own readable log, one per Windows user, shown by
// Services → See log. XT writes its start and exit, the xaiht.org link's state
// changes and every message that arrives from xaiht.org. At 5 MB the file moves
// to Tlachihuani.1.log, so one previous file is kept.
internal static class TlachihuaniLog
{
    private const long RotateBytes = 5L * 1024 * 1024;
    private static readonly object gate = new();
    private static readonly UTF8Encoding utf8 = new(false);

    // TLACHIHUANI_USER_DIR lets a test write somewhere other than the real profile.
    internal static string Folder
    {
        get
        {
            var custom = Environment.GetEnvironmentVariable("TLACHIHUANI_USER_DIR");
            return string.IsNullOrWhiteSpace(custom)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XAIHT", "Tlachihuani")
                : Path.GetFullPath(custom);
        }
    }
    internal static string FilePath => Path.Combine(Folder, "Tlachihuani.log");

    internal static void Info(string source, string text, string? block = null) => Write("INFO", source, text, block);
    internal static void Warn(string source, string text, string? block = null) => Write("WARN", source, text, block);
    internal static void Error(string source, string text, string? block = null) => Write("ERROR", source, text, block);

    // One header line, then an optional block (a JSON message) indented by four
    // spaces. The block is copied exactly, line by line, so it stays valid JSON.
    internal static void Write(string level, string source, string text, string? block = null)
    {
        var entry = new StringBuilder()
            .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", System.Globalization.CultureInfo.InvariantCulture))
            .Append("  ").Append(level.PadRight(5)).Append("  ").Append(source).Append("  ")
            .Append(text.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal))
            .Append(Environment.NewLine);
        if (block is not null)
            foreach (var line in block.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
                entry.Append("    ").Append(line).Append(Environment.NewLine);
        try
        {
            lock (gate)
            {
                Directory.CreateDirectory(Folder);
                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length >= RotateBytes)
                    File.Move(FilePath, Path.Combine(Folder, "Tlachihuani.1.log"), overwrite: true);
                using var stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                var bytes = utf8.GetBytes(entry.ToString());
                stream.Write(bytes, 0, bytes.Length);
            }
        }
        // A log line must never stop XT itself.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    // The newest part of the log for the viewer, without holding the file.
    internal static (string Text, long Size, bool Truncated) ReadTail(int maxBytes)
    {
        if (!File.Exists(FilePath)) return ("", 0, false);
        using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        long size = stream.Length;
        bool truncated = size > maxBytes;
        if (truncated) stream.Seek(size - maxBytes, SeekOrigin.Begin);
        var buffer = new byte[(int)Math.Min(size, maxBytes)];
        int read = 0;
        while (read < buffer.Length)
        {
            int count = stream.Read(buffer, read, buffer.Length - read);
            if (count == 0) break;
            read += count;
        }
        var text = utf8.GetString(buffer, 0, read);
        if (truncated)
        {
            int start = text.IndexOf('\n', StringComparison.Ordinal);
            text = start >= 0 ? text[(start + 1)..] : text;
        }
        return (text, size, truncated);
    }
}
