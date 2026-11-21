// Tlachihuani — Angela López Mendoza · XAIHT
// tlachihuani:trace-off (the logger never traces itself)
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Tlachihuani.Contracts;

// Idle loops write nothing (Angela, 21 November 2026): "if Tlachihuani is IDLE the log must
// grow very slowly, or almost static; it grows as long as the system is active".
//
// Every polling loop (XT's status check, the services' pipe requests, the Torrenter tick,
// the web health check...) runs each pass inside TraceLog.Pass("name"). The lines of a pass
// are held back and compared, as a shape, with the previous pass of the same name: the
// sequence of statements and calls it went through and the text of its messages, with every
// number, id and time removed. A pass identical to the previous one is not written. Any
// difference (another branch taken, a message with different words, an error, a state
// change, a click) writes the whole pass, line for line with its own timestamps, after one
// line counting the identical passes that were left out. While nothing changes, each loop
// writes one line every ten minutes saying how many identical passes it left out.
public static partial class TraceLog
{
    private const int MaxPassEntries = 200_000;
    private static readonly TimeSpan NoteEvery = TimeSpan.FromMinutes(10);
    private static readonly AsyncLocal<PassState?> currentPass = new();
    private static readonly object historyGate = new();
    private static readonly Dictionary<string, PassHistory> history = new(StringComparer.Ordinal);
    private static volatile bool quietExit;

    /// <summary>
    /// Starts one pass of a polling loop. Dispose it at the end of the pass. Inside a pass
    /// that is already running (on this flow) it does nothing: the outer pass decides.
    /// acrossRuns compares with the previous run of a short process launched repeatedly
    /// (for example the control tool XT starts every few seconds).
    /// </summary>
    public static PassScope Pass(string name, bool acrossRuns = false)
    {
        // A finished pass inherited by a long-running task started inside it counts as none.
        if (Level == TraceLevel.Off || currentPass.Value is { Closed: false }) return default;
        var pass = new PassState(name, acrossRuns);
        currentPass.Value = pass;
        return new PassScope(pass);
    }

    public readonly struct PassScope : IDisposable
    {
        private readonly PassState? pass;
        internal PassScope(PassState pass) => this.pass = pass;
        /// <summary>Names the pass once its kind is known (for example the request's operation).</summary>
        public void Rename(string name) { if (pass is not null) pass.Name = name; }
        public void Dispose()
        {
            if (pass is null) return;
            if (ReferenceEquals(currentPass.Value, pass)) currentPass.Value = null;
            pass.Finish();
        }
    }

    internal sealed class Entry(TraceLevel level, string message, string file, int line, string member, string? block)
    {
        public DateTimeOffset Time { get; } = DateTimeOffset.Now;
        public int Thread { get; } = Environment.CurrentManagedThreadId;
        public TraceLevel Level { get; } = level;
        public string Message { get; } = message;
        public string File { get; } = file;
        public int Line { get; } = line;
        public string Member { get; } = member;
        public string? Block { get; } = block;
    }

    // kind: S statement, E method entered, X method left, M message, B message with a block.
    private static void Emit(Sink target, Entry entry, char kind, string key, int number, string? text)
    {
        var pass = currentPass.Value;
        if (pass is not null && pass.TryAdd(entry, kind, key, number, text)) return;
        target.Enqueue(Format(entry, target.Component));
    }

    // Shape: the previous pass. Recent: the last 16 distinct shapes. A pass made only of code
    // (statements, calls, exceptions the code expects) is left out when it matches any recent
    // shape, so a loop that also does something every 30 or 60 seconds stays quiet. A pass that
    // writes a real message (Debug or higher: a state change, a request, a click) is left out
    // only when it matches the previous pass exactly, so returning to an earlier state is written.
    private sealed class PassHistory
    {
        public const int RecentShapes = 16;
        public ulong Shape;
        public List<ulong> Recent = [];
        public long Repeats;
        public DateTimeOffset Since;
        public DateTimeOffset LastRepeat;
        public DateTimeOffset LastNote;

        public void Remember(ulong shape)
        {
            Shape = shape;
            Recent.Remove(shape);
            Recent.Add(shape);
            if (Recent.Count > RecentShapes) Recent.RemoveAt(0);
        }
    }

    internal sealed class PassState(string name, bool acrossRuns)
    {
        private readonly object gate = new();
        private readonly List<Entry> entries = [];
        private ulong shape = 14695981039346656037UL;
        private bool overflowed, closed, significant;
        public string Name { get; set; } = name;
        public bool Closed { get { lock (gate) return closed; } }

        public bool TryAdd(Entry entry, char kind, string key, int number, string? text)
        {
            lock (gate)
            {
                if (closed || overflowed) return false;
                if (entries.Count >= MaxPassEntries)
                {
                    // Too big to hold back: this pass is clearly busy. Write what it has and stream the rest.
                    overflowed = true;
                    var target = sink;
                    if (target is not null) foreach (var held in entries) target.Enqueue(Format(held, target.Component));
                    entries.Clear();
                    return false;
                }
                shape = Mix(Mix(Mix(shape, kind), key), number);
                if (text is not null) shape = Mix(shape, Shape().Replace(text, "#"));
                if (kind is 'M' or 'B' && entry.Level >= TraceLevel.Debug) significant = true;
                entries.Add(entry);
                return true;
            }
        }

        public void Finish()
        {
            lock (gate) { if (closed) return; closed = true; }
            var target = sink;
            if (target is null) return;
            var now = DateTimeOffset.Now;
            lock (historyGate)
            {
                using var shared = acrossRuns ? new SharedHistory(target.Path, Name) : null;
                var known = shared is not null ? shared.Load() : history.GetValueOrDefault(Name);
                if (overflowed)
                {
                    Save(shared, new PassHistory { Shape = 0, Recent = known?.Recent ?? [], Since = now, LastRepeat = now, LastNote = now });
                    return;
                }
                if (known is not null && (significant ? known.Shape == shape : known.Recent.Contains(shape)))
                {
                    known.Remember(shape);
                    known.Repeats++;
                    known.LastRepeat = now;
                    if (now - known.LastNote >= NoteEvery)
                    {
                        Note(target, $"(no change) {Name}: {known.Repeats} identical passes since {Clock(known.Since)} were not written");
                        known.Repeats = 0;
                        known.Since = now;
                        known.LastNote = now;
                    }
                    Save(shared, known);
                    if (acrossRuns) quietExit = true;
                    return;
                }
                if (known is not null && known.Repeats > 0)
                    Note(target, $"(no change) {Name}: {known.Repeats} identical passes between {Clock(known.Since)} and {Clock(known.LastRepeat)} were not written; the next pass differs");
                foreach (var entry in entries) target.Enqueue(Format(entry, target.Component));
                var next = new PassHistory { Recent = known?.Recent ?? [], Since = now, LastRepeat = now, LastNote = now };
                next.Remember(shape);
                Save(shared, next);
                if (acrossRuns) quietExit = false;
            }
        }

        private void Save(SharedHistory? shared, PassHistory value)
        {
            if (shared is not null) shared.Save(value);
            else history[Name] = value;
        }

        private static void Note(Sink target, string text)
            => target.Enqueue(Format(new Entry(TraceLevel.Info, text, "", 0, "", null), target.Component));

        private static string Clock(DateTimeOffset time) => time.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }

    // The previous run's shape for a short process XT launches again and again, kept in
    // <log folder>\log-passes\<name>.txt under a per-file lock.
    private sealed class SharedHistory : IDisposable
    {
        private readonly string file;
        private readonly Mutex mutex;
        private readonly bool locked;

        public SharedHistory(string logPath, string name)
        {
            var folder = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(logPath)!, "log-passes");
            var safe = new StringBuilder(name.Length);
            foreach (char c in name) safe.Append(char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_');
            file = System.IO.Path.Combine(folder, safe + ".txt");
            mutex = new Mutex(false, @"Local\XAIHT.TraceLog.Pass." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(file.ToUpperInvariant())))[..32]);
            try { locked = mutex.WaitOne(2000); } catch (AbandonedMutexException) { locked = true; }
            try { Directory.CreateDirectory(folder); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        public PassHistory? Load()
        {
            try
            {
                if (!File.Exists(file)) return null;
                var parts = File.ReadAllText(file).Split('|');
                return new PassHistory
                {
                    Shape = ulong.Parse(parts[0], CultureInfo.InvariantCulture),
                    Repeats = long.Parse(parts[1], CultureInfo.InvariantCulture),
                    Since = new DateTimeOffset(long.Parse(parts[2], CultureInfo.InvariantCulture), TimeSpan.Zero),
                    LastRepeat = new DateTimeOffset(long.Parse(parts[3], CultureInfo.InvariantCulture), TimeSpan.Zero),
                    LastNote = new DateTimeOffset(long.Parse(parts[4], CultureInfo.InvariantCulture), TimeSpan.Zero),
                    Recent = parts.Length > 5 && parts[5].Length > 0
                        ? parts[5].Split(';').Select(x => ulong.Parse(x, CultureInfo.InvariantCulture)).ToList() : []
                };
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or IndexOutOfRangeException or OverflowException) { return null; }
        }

        public void Save(PassHistory value)
        {
            try
            {
                File.WriteAllText(file, string.Join('|', value.Shape.ToString(CultureInfo.InvariantCulture), value.Repeats.ToString(CultureInfo.InvariantCulture),
                    value.Since.UtcTicks.ToString(CultureInfo.InvariantCulture), value.LastRepeat.UtcTicks.ToString(CultureInfo.InvariantCulture),
                    value.LastNote.UtcTicks.ToString(CultureInfo.InvariantCulture),
                    string.Join(';', value.Recent.Select(x => x.ToString(CultureInfo.InvariantCulture)))));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        public void Dispose()
        {
            if (locked) mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }

    private static readonly Dictionary<string, (long Count, DateTimeOffset Written)> repeating = new(StringComparer.Ordinal);

    /// <summary>
    /// For framework lines written outside any pass (for example ASP.NET's own lines for each
    /// request): a line identical to one written in the last ten minutes, numbers and ids aside,
    /// is not written again; after ten minutes it is written once more with the count left out.
    /// Inside a pass it is an ordinary line (the pass decides).
    /// </summary>
    public static void WriteRepeating(TraceLevel level, string message, string member)
    {
        if (currentPass.Value is { Closed: false }) { Write(level, message, member, "", 0); return; }
        var key = Shape().Replace(message, "#");
        var now = DateTimeOffset.Now;
        long left = 0;
        lock (repeating)
        {
            if (repeating.TryGetValue(key, out var seen) && now - seen.Written < NoteEvery)
            {
                repeating[key] = (seen.Count + 1, seen.Written);
                return;
            }
            if (repeating.Count > 5000) repeating.Clear();
            left = seen.Count;
            repeating[key] = (0, now);
        }
        Write(level, left > 0 ? message + $"  (the same line {left} more times in the last ten minutes, not written)" : message, member, "", 0);
    }

    // FNV-1a: the same value in every process, so a shape can be compared across runs.
    private static ulong Mix(ulong hash, char value) => (hash ^ value) * 1099511628211UL;
    private static ulong Mix(ulong hash, int value) => (hash ^ (uint)value) * 1099511628211UL;
    private static ulong Mix(ulong hash, string value)
    {
        foreach (char c in value) hash = (hash ^ c) * 1099511628211UL;
        return (hash ^ 0xFFFF) * 1099511628211UL;
    }

    // Any word containing a digit (numbers, ids, times, sizes, process ids) counts as the
    // same: they change on every pass without anything really happening.
    [GeneratedRegex(@"[\w.\-]*\d[\w.\-]*")] private static partial Regex Shape();
}
