// HandGI expert window (Angela López Mendoza · XAIHT), part of Tlachihuani. tlachihuani:trace-off (repaints and polls the engine every 15 ms; its actions are written to Tlachihuani.log through the HandGI log bridge)
using HandGI.Core.Audio;
using HandGI.Core.Capture;
using HandGI.Core.Engine;
using HandGI.Core.Gestures;
using HandGI.Core.Gpu;
using HandGI.Core.Util;

namespace HandGI.App;

/// <summary>
/// Everything HandGI's windows share: log, settings, the volume endpoint, the gesture profile, devices
/// and the engine. The HandGI service owns it; the windows only read and drive it.
/// </summary>
internal sealed class AppController
{
    public const int NoAdapter = int.MinValue;

    /// <param name="log">Inside Tlachihuani the log writes no file of its own: LogBridge sends every line to Tlachihuani.log.</param>
    public AppController(HandGiLog log)
    {
        Log = log;
        Log.Info($"HandGI {Version} starting · {Environment.OSVersion} · {Environment.ProcessorCount} logical processors · .NET {Environment.Version}");
        Settings = AppSettings.Load(Log);
        Volume = new VolumeController(Log);
        Profile = LoadStartupProfile();
    }

    public static string Version => typeof(AppController).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";

    public HandGiLog Log { get; }
    public AppSettings Settings { get; }
    public VolumeController Volume { get; }
    public GestureProfile Profile { get; private set; }
    public GestureEngine? Engine { get; private set; }
    public IReadOnlyList<GpuInfo> Gpus { get; private set; } = Array.Empty<GpuInfo>();
    public IReadOnlyList<CameraInfo> Cameras { get; private set; } = Array.Empty<CameraInfo>();
    public string? ModelProblem { get; private set; }
    public string PreviewDecision { get; private set; } = "";

    public event Action? ProfileChanged;

    /// <summary>Raised on whichever thread the engine changed state on.</summary>
    public event Action<EngineState, string>? EngineStateChanged;

    public void RefreshGpus()
    {
        try
        {
            Gpus = GpuCatalog.Enumerate();
        }
        catch (Exception ex)
        {
            Log.Error("GPU enumeration failed", ex);
            Gpus = Array.Empty<GpuInfo>();
        }
        foreach (var gpu in Gpus) Log.Info($"GPU [{gpu.DxgiIndex}] {gpu.Summary}: {gpu.CheckSupport().Reason}");
    }

    public async Task RefreshCamerasAsync()
    {
        try
        {
            Cameras = await CameraCatalog.ListAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Camera enumeration failed", ex);
            Cameras = Array.Empty<CameraInfo>();
        }
        Log.Info($"{Cameras.Count} camera stream(s): {string.Join(", ", Cameras.Select(c => c.DisplayName))}");
    }

    /// <summary>The adapter the next start will use, its display name and an explanation for the user.</summary>
    public (int Adapter, string Name, string Note) ResolveAdapter()
    {
        if (Settings.Adapter == OrtFactory.CpuAdapter)
            return (OrtFactory.CpuAdapter, "CPU (compatibility mode)", "Compatibility mode: the CPU does the work. It is slower and not GPU-exclusive.");

        if (Settings.Adapter >= 0)
        {
            var chosen = Gpus.FirstOrDefault(g => g.DxgiIndex == Settings.Adapter);
            if (chosen is not null)
            {
                var (ok, reason) = chosen.CheckSupport();
                return (chosen.DxgiIndex, chosen.Name, ok ? $"{reason}." : $"Not on HandGI's supported list ({reason}); used because you chose it.");
            }
        }

        var best = GpuCatalog.PickBest(Gpus);
        return best is null
            ? (NoAdapter, "", "No supported NVIDIA GPU (GTX 1060 or newer) was found. HandGI runs on the GPU; choose CPU compatibility mode to run anyway.")
            : (best.DxgiIndex, best.Name, $"Automatic: {best.CheckSupport().Reason}.");
    }

    /// <summary>Google's gesture networks, read and verified once (the Trainer needs them even before the camera starts).</summary>
    public MediaPipeGestureModel GestureModel => _gestureModel ??= ModelCatalog.LoadGestures();
    private MediaPipeGestureModel? _gestureModel;

    public bool EnsureEngine()
    {
        if (Engine is not null) return true;
        try
        {
            var palm = ModelCatalog.LoadVerified(ModelCatalog.Palm);
            var hand = ModelCatalog.LoadVerified(ModelCatalog.Hand);
            var gestures = GestureModel;
            Log.Info($"Google's gestures ready: {gestures.Describe()}.");
            Engine = new GestureEngine(palm, hand, gestures, Volume, Log, Profile);
            Engine.StateChanged += (state, message) => EngineStateChanged?.Invoke(state, message);
            ModelProblem = null;
            return true;
        }
        catch (Exception ex)
        {
            ModelProblem = ex.Message;
            Log.Error("The hand models are not available", ex);
            return false;
        }
    }

    public CameraInfo? SavedCamera() =>
        Cameras.FirstOrDefault(c => c.GroupId == Settings.CameraGroupId && c.SourceId == Settings.CameraSourceId)
        ?? Cameras.FirstOrDefault(c => c.Kind == CameraKind.Color)
        ?? Cameras.FirstOrDefault();

    public async Task StartAsync(CameraInfo camera, CameraFormat? format)
    {
        if (!EnsureEngine()) throw new InvalidOperationException(ModelProblem);
        var (adapter, name, note) = ResolveAdapter();
        if (adapter == NoAdapter) throw new InvalidOperationException(note);

        bool explicitWidth = Settings.PreviewWidth > 0;
        var (width, reason) = SchedulingPolicy.PreviewWidth(explicitWidth ? Settings.PreviewWidth : 960, Environment.ProcessorCount, explicitWidth);
        PreviewDecision = reason;
        Log.Info(reason);

        Settings.CameraGroupId = camera.GroupId;
        Settings.CameraSourceId = camera.SourceId;
        Settings.CameraFormatKey = format?.Key;
        Settings.Save(Log);

        await Engine!.StartAsync(camera, format, Settings.SharedCamera, new EngineOptions
        {
            DxgiIndex = adapter,
            DeviceName = name,
            AllowCpuFallback = Settings.AllowCpuFallback,
            Range = Settings.Range,
            PalmThreshold = Settings.PalmThreshold,
            PresenceThreshold = Settings.PresenceThreshold,
            Preview = Settings.Preview,
            PreviewMaxWidth = width,
            Mirror = Settings.Mirror,
            Scheduling = Settings.Scheduling,
        });
    }

    public Task StopAsync() => Engine?.StopAsync() ?? Task.CompletedTask;

    public void SetProfile(GestureProfile profile)
    {
        Profile = profile;
        Engine?.SetProfile(profile);
        ProfileChanged?.Invoke();
    }

    /// <summary>Call after editing the current profile (classes, samples, weights, decision rules).</summary>
    public void ProfileEdited()
    {
        Engine?.SetProfile(Profile);
        ProfileChanged?.Invoke();
    }

    public string SaveProfile(string? path = null)
    {
        path ??= Profile.FilePath ?? Path.Combine(AppPaths.ProfilesDir, GestureProfile.SafeFileName(Profile.Name) + ".handgi.json");
        Profile.Save(path);
        Settings.LastProfile = path;
        Settings.Save(Log);
        Log.Info($"Profile saved: {path}");
        return path;
    }

    private GestureProfile LoadStartupProfile()
    {
        if (Settings.LastProfile is string path && File.Exists(path))
        {
            try
            {
                var profile = GestureProfile.Load(path);
                string mine = profile.MineCount == 0 ? "only Google's gestures"
                    : $"{profile.MineCount} of your own gestures with {profile.Samples.Count} examples, {(profile.IsTrained ? "learned" : "not learned yet")}";
                Log.Info($"Profile loaded: {path} ({mine})");
                if (profile.MigrationNote is not null) Log.Info(profile.MigrationNote);
                return profile;
            }
            catch (Exception ex)
            {
                Log.Warn($"The last profile could not be loaded ({ex.Message}); starting a new one.");
            }
        }
        return GestureProfile.CreateDefault();
    }

    public void Shutdown()
    {
        try
        {
            // The message loop is gone: stop on the thread pool so no continuation waits for it.
            Task.Run(StopAsync).Wait(TimeSpan.FromSeconds(6));
        }
        catch (Exception ex)
        {
            Log.Warn($"Stopping the engine: {ex.Message}");
        }
        Settings.Save(Log);
        Volume.Dispose();
        Log.Info("HandGI closed.");
        Log.Dispose();
    }
}
