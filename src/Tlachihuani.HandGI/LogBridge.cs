// Tlachihuani — Angela López Mendoza · XAIHT
// tlachihuani:trace-off (forwards each of HandGI's own messages; a woven copy would write every message twice)
using HandGI.Core.Util;
using Tlachihuani.Contracts;

namespace HandGI.App;

// HandGI's own messages (camera, signs, learning, every gesture it acts on, its errors with their
// details) go into Tlachihuani.log as component "handgi", at their own level. Inside Tlachihuani,
// HandGI keeps no log file of its own.
internal static class LogBridge
{
    internal static void Attach(HandGiLog log) => log.Message += (level, message) => TraceLog.Write(level switch
    {
        LogLevel.Debug => TraceLevel.Debug,
        LogLevel.Info => TraceLevel.Info,
        LogLevel.Warn => TraceLevel.Warn,
        _ => TraceLevel.Error,
    }, message, "HandGI", "", 0);
}
