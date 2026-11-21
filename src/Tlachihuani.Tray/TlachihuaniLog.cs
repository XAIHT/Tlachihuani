// Tlachihuani — Angela López Mendoza · XAIHT
// tlachihuani:trace-off (this file only forwards to TraceLog)
using Tlachihuani.Contracts;
using Tlachihuani.Windows;

namespace Tlachihuani.Tray;

// Tlachihuani.log: XT's verbose log, one per Windows user, shown by Services → See log.
// Everything goes through TraceLog (configured in Program.Main), so XT's own lines,
// every UI event, every woven statement and the copied service logs share one file.
internal static class TlachihuaniLog
{
    internal static string Folder => LogPaths.UserFolder;
    internal static string FilePath => TraceLog.FilePath ?? LogPaths.UserLog;

    internal static void Info(string source, string text, string? block = null) => Write(TraceLevel.Info, source, text, block);
    internal static void Warn(string source, string text, string? block = null) => Write(TraceLevel.Warn, source, text, block);
    internal static void Error(string source, string text, string? block = null) => Write(TraceLevel.Error, source, text, block);

    private static void Write(TraceLevel level, string source, string text, string? block)
    {
        if (block is null) TraceLog.Write(level, $"[{source}] {text}", source, "", 0);
        else TraceLog.Block(level, $"[{source}] {text}", block, source, "", 0);
    }
}
