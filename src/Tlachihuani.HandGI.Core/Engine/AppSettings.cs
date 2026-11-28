// HandGI engine (Angela López Mendoza · XAIHT), part of Tlachihuani. tlachihuani:trace-off (runs on every camera frame and in tight GPU and training loops; its own messages reach Tlachihuani.log through the HandGI log bridge)
using System.Text.Json;
using System.Text.Json.Serialization;
using HandGI.Core.Util;
using HandGI.Core.Vision;

namespace HandGI.Core.Engine;

/// <summary>User choices, saved to HandGI\settings.json in the user's Tlachihuani folder (AppPaths). Shared by the windows and the service.</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Adapter choice: -2 = automatic (best supported GPU), -1 = CPU compatibility mode, 0.. = DXGI index.</summary>
    public const int AutoAdapter = -2;

    public string? CameraGroupId { get; set; }
    public string? CameraSourceId { get; set; }
    public string? CameraFormatKey { get; set; }
    public bool SharedCamera { get; set; }
    public int Adapter { get; set; } = AutoAdapter;
    public string? AdapterName { get; set; }
    public bool AllowCpuFallback { get; set; }
    public bool Mirror { get; set; } = true;
    public bool Preview { get; set; } = true;
    public int PreviewWidth { get; set; }
    public YuvRange Range { get; set; } = YuvRange.Limited;
    public float PalmThreshold { get; set; } = 0.5f;
    public float PresenceThreshold { get; set; } = 0.5f;
    public SchedulingMode Scheduling { get; set; } = SchedulingMode.Auto;
    public bool ShowOsd { get; set; } = true;
    public string? LastProfile { get; set; }

    /// <summary>Hand control acts on Windows as soon as HandGI starts, instead of starting paused.</summary>
    public bool StartArmed { get; set; }

    /// <summary>Tlachihuani's Start Services also starts HandGI. Off by default, so the camera turns on only when asked.</summary>
    public bool StartWithServices { get; set; }

    public static AppSettings Load(ILog log)
    {
        try
        {
            string path = AppPaths.SettingsFile;
            if (!File.Exists(path)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            log.Warn($"Settings could not be read ({ex.Message}); using defaults.");
            return new AppSettings();
        }
    }

    public void Save(ILog log)
    {
        try
        {
            string path = AppPaths.SettingsFile;
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            log.Warn($"Settings could not be saved: {ex.Message}");
        }
    }
}
