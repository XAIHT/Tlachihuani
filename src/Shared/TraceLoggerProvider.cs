// Tlachihuani — Angela López Mendoza · XAIHT
// tlachihuani:trace-off (forwards framework logging into TraceLog)
using Microsoft.Extensions.Logging;
using Tlachihuani.Contracts;
using TraceLevel = Tlachihuani.Contracts.TraceLevel;

namespace Tlachihuani.Shared;

// Every Microsoft.Extensions.Logging message (hosting, service control, Kestrel
// connections and requests, routing, Tlachihuani's own ILogger calls) goes into the
// process's trace log at Trace level and above.
internal sealed class TraceLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new TraceLogger(categoryName);
    public void Dispose() { }

    private sealed class TraceLogger(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && TraceLog.IsOn(Map(logLevel));
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            string text = $"[{category}{(eventId.Id != 0 ? " #" + eventId.Id : "")}] {formatter(state, exception)}";
            if (exception is not null) text += " | " + exception;
            // ASP.NET's own lines for each request and connection come before Tlachihuani's code
            // (and its passes) sees the request: a framework line identical to one already written
            // (numbers aside) is written again only when it changes, or counted every ten minutes.
            if (logLevel < LogLevel.Warning && (category.StartsWith("Microsoft.", StringComparison.Ordinal) || category.StartsWith("System.", StringComparison.Ordinal)))
                TraceLog.WriteRepeating(Map(logLevel), text, "ILogger");
            else TraceLog.Write(Map(logLevel), text, "ILogger", "", 0);
        }
        private static TraceLevel Map(LogLevel level) => level switch
        {
            LogLevel.Trace => TraceLevel.Trace, LogLevel.Debug => TraceLevel.Debug, LogLevel.Information => TraceLevel.Info,
            LogLevel.Warning => TraceLevel.Warn, _ => TraceLevel.Error
        };
    }
}

internal static class TraceLoggingExtensions
{
    // Keeps the existing providers at their own levels (the Event Log stays quiet) and adds
    // the trace log with every category at Trace.
    public static ILoggingBuilder AddTlachihuaniTrace(this ILoggingBuilder logging)
    {
        logging.AddProvider(new TraceLoggerProvider());
        logging.AddFilter<TraceLoggerProvider>(null, LogLevel.Trace);
        return logging;
    }
}
