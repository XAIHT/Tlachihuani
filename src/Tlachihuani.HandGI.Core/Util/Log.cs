// HandGI engine (Angela López Mendoza · XAIHT), part of Tlachihuani. tlachihuani:trace-off (runs on every camera frame and in tight GPU and training loops; its own messages reach Tlachihuani.log through the HandGI log bridge)
using System.Collections.Concurrent;

namespace HandGI.Core.Util;

public enum LogLevel { Debug, Info, Warn, Error }

/// <summary>Minimal logging contract so the engine never depends on a UI or a hosting framework.</summary>
public interface ILog
{
    void Write(LogLevel level, string message);
}

public static class LogExtensions
{
    public static void Debug(this ILog log, string message) => log.Write(LogLevel.Debug, message);
    public static void Info(this ILog log, string message) => log.Write(LogLevel.Info, message);
    public static void Warn(this ILog log, string message) => log.Write(LogLevel.Warn, message);
    public static void Error(this ILog log, string message) => log.Write(LogLevel.Error, message);
    public static void Error(this ILog log, string message, Exception ex) => log.Write(LogLevel.Error, $"{message}: {ex.GetType().Name}: {ex.Message}");
}

/// <summary>
/// Thread-safe log: one file per day, an in-memory tail for the UI and a live event.
/// Logging must never break the real-time path, so every failure is swallowed.
/// </summary>
public sealed class HandGiLog : ILog, IDisposable
{
    private readonly object _gate = new();
    private readonly string? _directory;
    private readonly bool _echoConsole;
    private readonly ConcurrentQueue<string> _tail = new();
    private StreamWriter? _writer;
    private DateOnly _day;

    public event Action<LogLevel, string>? Logged;

    /// <summary>
    /// The bare message (no time or level prefix), raised for every line that passes MinimumLevel.
    /// Tlachihuani's HandGI service forwards it into Tlachihuani.log.
    /// </summary>
    public event Action<LogLevel, string>? Message;
    public LogLevel MinimumLevel { get; set; } = LogLevel.Info;

    public HandGiLog(string? directory, bool echoConsole = false)
    {
        _directory = directory;
        _echoConsole = echoConsole;
    }

    public string? CurrentFile { get; private set; }

    public void Write(LogLevel level, string message)
    {
        if (level < MinimumLevel) return;
        string line = $"{DateTime.Now:HH:mm:ss.fff} {Tag(level)} {message}";
        try
        {
            lock (_gate)
            {
                EnsureWriter();
                _writer?.WriteLine(line);
                _writer?.Flush();
            }
        }
        catch
        {
            // never throw from logging
        }

        _tail.Enqueue(line);
        while (_tail.Count > 400 && _tail.TryDequeue(out _)) { }

        if (_echoConsole)
        {
            try
            {
                var old = Console.ForegroundColor;
                Console.ForegroundColor = level switch
                {
                    LogLevel.Error => ConsoleColor.Red,
                    LogLevel.Warn => ConsoleColor.Yellow,
                    LogLevel.Debug => ConsoleColor.DarkGray,
                    _ => old,
                };
                Console.WriteLine(line);
                Console.ForegroundColor = old;
            }
            catch
            {
                // console may be gone
            }
        }

        try { Message?.Invoke(level, message); } catch { /* the bridge must not break logging */ }
        try { Logged?.Invoke(level, line); } catch { /* listeners must not break logging */ }
    }

    public IReadOnlyList<string> Tail() => _tail.ToArray();

    private void EnsureWriter()
    {
        if (_directory is null) return;
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (_writer is not null && today == _day) return;
        _writer?.Dispose();
        Directory.CreateDirectory(_directory);
        CurrentFile = Path.Combine(_directory, $"handgi-{today:yyyyMMdd}.log");
        _writer = new StreamWriter(new FileStream(CurrentFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite));
        _day = today;
    }

    private static string Tag(LogLevel level) => level switch
    {
        LogLevel.Debug => "DBG",
        LogLevel.Info => "INF",
        LogLevel.Warn => "WRN",
        _ => "ERR",
    };

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}

/// <summary>A log that drops everything (tests, probes).</summary>
public sealed class NullLog : ILog
{
    public static readonly NullLog Instance = new();
    public void Write(LogLevel level, string message) { }
}
