// Tlachihuani — Angela López Mendoza · XAIHT
// tlachihuani:trace-off (reading the log must never add entries to it)
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Tlachihuani.Tray;

internal sealed record LogPage(long Start, long End, string Text, bool PartialStart, bool PartialEnd)
{
    internal int CharacterAt(long offset) => Encoding.UTF8.GetCharCount(
        Encoding.UTF8.GetBytes(Text).AsSpan(0, (int)Math.Clamp(offset - Start, 0, End - Start)));
}

// One open file, not a growing in-memory history. A shared-delete handle follows the
// SAME file through renames/rotation, so browsing cannot silently switch to another log.
internal sealed class LogFileReader : IDisposable
{
    internal const int PageBytes = 256 * 1024;
    internal const int SearchBytes = 64 * 1024;
    internal const int MaxQueryChars = 512;
    private readonly SafeFileHandle handle;
    internal string Path { get; }
    internal (ulong Volume, ulong Low, ulong High) Identity { get; }
    internal long Length => RandomAccess.GetLength(handle);

    internal LogFileReader(string path)
    {
        Path = path;
        handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, FileOptions.RandomAccess);
        try
        {
            if (!GetFileInformationByHandleEx(handle, 18 /* FileIdInfo */, out var id, 24))
                throw new IOException("The log file identity could not be read.", new Win32Exception(Marshal.GetLastWin32Error()));
            Identity = (id.Volume, id.Low, id.High);
        }
        catch { handle.Dispose(); throw; }
    }

    internal LogPage Before(long end)
    {
        end = Math.Clamp(end, 0, Length);
        return ReadPage(Math.Max(0, end - PageBytes), end, alignStart: true, alignEnd: false);
    }

    internal LogPage After(long start)
    {
        long size = Length;
        start = Math.Clamp(start, 0, size);
        return ReadPage(start, Math.Min(size, start + PageBytes), alignStart: false, alignEnd: true);
    }

    internal LogPage Containing(long offset)
    {
        long size = Length;
        offset = Math.Clamp(offset, 0, size);
        long start = Math.Max(0, offset - PageBytes / 2);
        return ReadPage(start, Math.Min(size, start + PageBytes), true, true, offset);
    }

    private LogPage ReadPage(long start, long end, bool alignStart, bool alignEnd, long? mustInclude = null)
    {
        var bytes = Read(start, (int)(end - start));
        int first = 0, last = bytes.Length;
        bool boundary = start == 0 || Read(start - 1, 1).FirstOrDefault() == (byte)'\n';
        if (alignStart && !boundary)
        {
            int newline = Array.IndexOf(bytes, (byte)'\n');
            if (newline >= 0 && newline + 1 < last && (mustInclude is null || start + newline < mustInclude))
                first = newline + 1;
        }
        if (alignEnd && end < Length)
        {
            int newline = Array.LastIndexOf(bytes, (byte)'\n');
            if (newline >= first && (mustInclude is null || start + newline > mustInclude + MaxQueryChars * 4))
                last = newline + 1;
        }
        AlignUtf8(bytes, ref first, ref last);
        bool partialStart = first == 0 ? !boundary : bytes[first - 1] != (byte)'\n';
        bool partialEnd = last > first && bytes[last - 1] != (byte)'\n';
        return new(start + first, start + last, Encoding.UTF8.GetString(bytes, first, last - first), partialStart, partialEnd);
    }

    // Capture the bound once: a search finishes even if the producer never stops.
    // Chunks overlap by the encoded query length. No line index, match list,
    // whole-file string or unbounded ReadLine is retained.
    internal long? Find(string query, long from, bool backwards, CancellationToken cancellation,
        long? snapshotLength = null, Action<long>? scanned = null)
    {
        if (query.Length is 0 or > MaxQueryChars) throw new ArgumentException("Enter 1–512 characters.", nameof(query));
        long bound = snapshotLength ?? Length;
        if (Length < bound) throw new IOException("The log was shortened during search.");
        long cursor = Math.Clamp(from, 0, bound);
        int overlap = Encoding.UTF8.GetMaxByteCount(query.Length) + 4;
        while (backwards ? cursor > 0 : cursor < bound)
        {
            cancellation.ThrowIfCancellationRequested();
            long low = backwards ? Math.Max(0, cursor - SearchBytes) : cursor;
            long high = backwards ? cursor : Math.Min(bound, cursor + SearchBytes);
            long readAt = Math.Max(0, low - 4);
            var bytes = Read(readAt, (int)(Math.Min(bound, high + overlap) - readAt));
            int first = 0, last = bytes.Length;
            AlignUtf8(bytes, ref first, ref last);
            var text = Encoding.UTF8.GetString(bytes, first, last - first);
            scanned?.Invoke(high - low);
            int index = backwards ? text.LastIndexOf(query, StringComparison.OrdinalIgnoreCase)
                : text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            while (index >= 0)
            {
                long found = readAt + first + Encoding.UTF8.GetByteCount(text.AsSpan(0, index));
                if (found >= low && found < high) return found;
                if (backwards)
                    index = index == 0 ? -1 : text.AsSpan(0, index + query.Length - 1)
                        .LastIndexOf(query.AsSpan(), StringComparison.OrdinalIgnoreCase);
                else
                    index = text.IndexOf(query, index + 1, StringComparison.OrdinalIgnoreCase);
            }
            cursor = backwards ? low : high;
        }
        cancellation.ThrowIfCancellationRequested();
        if (Length < bound) throw new IOException("The log was shortened during search.");
        return null;
    }

    private byte[] Read(long offset, int count)
    {
        var bytes = new byte[count];
        int done = 0;
        while (done < bytes.Length)
        {
            int n = RandomAccess.Read(handle, bytes.AsSpan(done), offset + done);
            if (n == 0) break;
            done += n;
        }
        return done == count ? bytes : bytes[..done];
    }

    private static void AlignUtf8(byte[] bytes, ref int first, ref int last)
    {
        while (first < last && (bytes[first] & 0xc0) == 0x80) first++;
        if (last <= first) return;
        int lead = last - 1;
        while (lead > first && (bytes[lead] & 0xc0) == 0x80) lead--;
        int expected = bytes[lead] < 0x80 ? 1 : bytes[lead] < 0xe0 ? 2 : bytes[lead] < 0xf0 ? 3 : 4;
        if (last - lead < expected) last = lead;
    }

    public void Dispose() => handle.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct FileId { public ulong Volume, Low, High; }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int informationClass, out FileId information, uint size);
}
