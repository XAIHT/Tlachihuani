// Tlachihuani — Angela López Mendoza · XAIHT
// tlachihuani:trace-off (searching the log must not write to it)
using System.Diagnostics;

namespace Tlachihuani.Tray;

internal sealed record LogSearchProgress(int Completed, int Total, long BytesRead);
internal sealed record LogSearchResult(LogFileReader? Reader, long? Offset, int Searched, string[] SkippedFiles, int PeakWorkers)
{
    internal int Unavailable => SkippedFiles.Length;
}

// Retention bounds metadata and handles. Each worker scans fixed-size chunks and
// retains at most one offset per file, never all lines or all matches. Handles are
// opened before scanning, so rotation cannot swap their contents mid-search.
internal static class LogHistorySearch
{
    internal const int MaxWorkers = 4;
    private sealed record Entry(LogFileReader Reader, long Length, bool Owned);

    internal static IEnumerable<string> Paths(string current)
    {
        yield return current;
        string folder = Path.GetDirectoryName(current)!;
        string name = Path.GetFileNameWithoutExtension(current);
        for (int i = 1; i <= Tlachihuani.Windows.LogPaths.UserKeepFiles; i++)
            yield return Path.Combine(folder, $"{name}.{i}.log");
    }

    // With no starting offset a new query searches the ENTIRE collection, regardless of
    // the displayed page. Subsequent searches continue before/after that result.
    // Ownership of a returned reader transfers to the caller unless it is anchor.
    internal static LogSearchResult Find(string current, string query, bool backwards,
        LogFileReader? anchor, long? from, CancellationToken cancellation,
        IProgress<LogSearchProgress>? progress = null)
    {
        if (query.Length is 0 or > LogFileReader.MaxQueryChars) throw new ArgumentException("Enter 1–512 characters.", nameof(query));
        var entries = new List<Entry>();
        var identities = new HashSet<(ulong, ulong, ulong)>();
        LogFileReader? keep = null;
        var skipped = new System.Collections.Concurrent.ConcurrentBag<string>();
        int completed = 0, active = 0, peak = 0;
        long bytesRead = 0;
        try
        {
            foreach (string file in Paths(current))
            {
                cancellation.ThrowIfCancellationRequested();
                LogFileReader? opened = null;
                try
                {
                    opened = new LogFileReader(file);
                    long size = opened.Length;
                    if (!identities.Add(opened.Identity)) { skipped.Add(Path.GetFileName(file)); continue; } // rotation during enumeration: report incomplete coverage
                    entries.Add(new(opened, size, true));
                    opened = null;
                }
                catch (FileNotFoundException) { if (file == current) skipped.Add(Path.GetFileName(file)); }
                catch (DirectoryNotFoundException) { if (file == current) skipped.Add(Path.GetFileName(file)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped.Add(Path.GetFileName(file)); }
                finally { opened?.Dispose(); }
            }
            // A paused handle may outlive retention. It is older than the files
            // still named in the directory and remains searchable while open.
            if (anchor is not null && identities.Add(anchor.Identity)) entries.Add(new(anchor, anchor.Length, false));
            int anchorIndex = anchor is null || !from.HasValue ? -1 : entries.FindIndex(e => e.Reader.Identity == anchor.Identity);
            var candidates = entries.Where((_, i) => anchorIndex < 0 || (backwards ? i >= anchorIndex : i <= anchorIndex));
            var ordered = (backwards ? candidates : candidates.Reverse()).ToArray();
            var hits = new long?[ordered.Length];
            var reportGate = new object();
            long reportedAt = 0;
            void Report(bool force = false)
            {
                lock (reportGate)
                {
                    long now = Stopwatch.GetTimestamp();
                    if (!force && Stopwatch.GetElapsedTime(reportedAt, now).TotalMilliseconds < 100) return;
                    reportedAt = now;
                    progress?.Report(new(Volatile.Read(ref completed), ordered.Length, Interlocked.Read(ref bytesRead)));
                }
            }
            Report(true);
            Parallel.For(0, ordered.Length, new ParallelOptions { MaxDegreeOfParallelism = MaxWorkers, CancellationToken = cancellation }, i =>
            {
                int workers = Interlocked.Increment(ref active);
                lock (reportGate) peak = Math.Max(peak, workers);
                try
                {
                    var file = ordered[i];
                    bool isAnchor = anchor is not null && file.Reader.Identity == anchor.Identity;
                    long start = isAnchor && from.HasValue ? from.Value : backwards ? file.Length : 0;
                    hits[i] = file.Reader.Find(query, start, backwards, cancellation, file.Length,
                        count => { Interlocked.Add(ref bytesRead, count); Report(); });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped.Add(Path.GetFileName(ordered[i].Reader.Path)); }
                finally { Interlocked.Decrement(ref active); Interlocked.Increment(ref completed); Report(true); }
            });
            cancellation.ThrowIfCancellationRequested();
            Report(true);
            cancellation.ThrowIfCancellationRequested();
            string[] unavailable = skipped.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
            // Completion order is deliberately ignored. Find older/newer must
            // choose the nearest chronological match, not the fastest disk read.
            for (int i = 0; i < hits.Length; i++)
                if (hits[i] is long offset)
                {
                    keep = ordered[i].Reader;
                    return new(keep, offset, completed, unavailable, peak);
                }
            return new(null, null, completed, unavailable, peak);
        }
        finally
        {
            foreach (var entry in entries)
                if (entry.Owned && !ReferenceEquals(entry.Reader, keep)) entry.Reader.Dispose();
        }
    }
}
