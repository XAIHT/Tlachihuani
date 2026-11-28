// HandGI engine (Angela López Mendoza · XAIHT), part of Tlachihuani. tlachihuani:trace-off (runs on every camera frame and in tight GPU and training loops; its own messages reach Tlachihuani.log through the HandGI log bridge)
namespace HandGI.Core.Util;

/// <summary>
/// Where HandGI keeps its files. Inside Tlachihuani everything user-specific lives under
/// %LOCALAPPDATA%\XAIHT\Tlachihuani\HandGI (TLACHIHUANI_USER_DIR moves it, like Tlachihuani.log).
/// The standalone prototype used %LOCALAPPDATA%\HandGI; see <see cref="LegacyDataRoot"/>.
/// </summary>
public static class AppPaths
{
    public static string DataRoot { get; } = Path.Combine(UserFolder(), "HandGI");

    /// <summary>The standalone prototype's folder; the service imports its settings and profiles once.</summary>
    public static string LegacyDataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HandGI");

    private static string UserFolder()
    {
        var custom = Environment.GetEnvironmentVariable("TLACHIHUANI_USER_DIR");
        return string.IsNullOrWhiteSpace(custom)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XAIHT", "Tlachihuani")
            : Path.GetFullPath(custom);
    }

    public static string ProfilesDir => Ensure(Path.Combine(DataRoot, "profiles"));
    public static string LogsDir => Ensure(Path.Combine(DataRoot, "logs"));
    public static string UserModelsDir => Ensure(Path.Combine(DataRoot, "models"));
    public static string SettingsFile => Path.Combine(Ensure(DataRoot), "settings.json");

    /// <summary>Models shipped next to the executable (copied by the build).</summary>
    public static string AppModelsDir => Path.Combine(AppContext.BaseDirectory, "models");

    public static string Ensure(string dir)
    {
        Directory.CreateDirectory(dir);
        return dir;
    }
}
