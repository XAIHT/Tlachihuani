// Tlachihuani — Angela López Mendoza · XAIHT
// tlachihuani:trace-off (prints a line per second while it runs; every printed line also goes to Tlachihuani.log)
using System.Globalization;
using System.Runtime.InteropServices;
using HandGI.Core.Audio;
using HandGI.Core.Capture;
using HandGI.Core.Engine;
using HandGI.Core.Gestures;
using HandGI.Core.Gpu;
using HandGI.Core.Onnx;
using HandGI.Core.SelfTest;
using HandGI.Core.Util;
using Tlachihuani.Contracts;

namespace HandGI.App;

// The engine's own checks, for a console (the standalone HandGI's HandGI.Agent commands):
//
//   Tlachihuani.HandGI.exe gpus                          graphics cards and whether HandGI supports them
//   Tlachihuani.HandGI.exe cameras                       cameras
//   Tlachihuani.HandGI.exe formats [--camera N]          a camera's capture modes
//   Tlachihuani.HandGI.exe inspect                       verify the models HandGI ships with
//   Tlachihuani.HandGI.exe selftest [--gpu N | --cpu] [--no-training]
//   Tlachihuani.HandGI.exe run [--camera N] [--format KEY|auto] [--gpu N | --cpu] [--shared]
//                              [--profile PATH] [--armed] [--seconds S]
//
// Started from a console, the output appears in it; every line is also written to Tlachihuani.log.
// The exit code is 0 for success, 1 for a failure and 3 when no camera or supported GPU exists.
internal static class Tools
{
    private static readonly string[] Commands = ["gpus", "cameras", "formats", "inspect", "selftest", "run", "help", "--help"];

    internal static bool Handles(string command) => Commands.Contains(command.ToLowerInvariant());

    internal static int Run(string[] args)
    {
        AttachConsole(-1);
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch (IOException) { }
        string command = args[0].ToLowerInvariant();
        using var log = new HandGiLog(null) { MinimumLevel = LogLevel.Debug };
        LogBridge.Attach(log);
        Native.DisableProcessPowerThrottling();
        TraceLog.I($"HandGI tool: {string.Join(' ', args)}");
        try
        {
            int code = command switch
            {
                "gpus" => Gpus(),
                "cameras" => Cameras().GetAwaiter().GetResult(),
                "formats" => Formats(args).GetAwaiter().GetResult(),
                "inspect" => Inspect(),
                "selftest" => SelfTest(args, log).GetAwaiter().GetResult(),
                "run" => RunEngine(args, log).GetAwaiter().GetResult(),
                _ => Help(),
            };
            TraceLog.I($"HandGI tool {command} finished with exit code {code}");
            return code;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Write(ConsoleColor.Red, $"Error: {ex.Message}");
            TraceLog.E($"HandGI tool {command} failed", ex);
            return 1;
        }
    }

    private static int Help()
    {
        Print("""
            Tlachihuani.HandGI.exe - HandGI, Tlachihuani's hand-gesture service

              --service                    run in the background (XT does this)
              --show signs|camera|try|expert
                                           open a HandGI window

              gpus                         list graphics cards and whether HandGI supports them
              cameras                      list cameras
              formats [--camera N]         list a camera's capture modes
              inspect                      verify the models and print their structure
              selftest [--gpu N | --cpu] [--no-training]
                                           run the self-tests
              run [--camera N] [--format KEY|auto] [--gpu N | --cpu] [--shared]
                  [--profile PATH] [--armed] [--seconds S]
                                           run the engine without a window (Ctrl+C stops)

            Without --armed every decision is a dry run: nothing changes the volume.
            """);
        return 2;
    }

    private static int Gpus()
    {
        var gpus = GpuCatalog.Enumerate();
        if (gpus.Count == 0) Write(ConsoleColor.Yellow, "No DXGI adapters found.");
        var best = GpuCatalog.PickBest(gpus);
        foreach (var gpu in gpus)
        {
            var (ok, reason) = gpu.CheckSupport();
            Write(ok ? ConsoleColor.Green : ConsoleColor.DarkGray,
                $"[{gpu.DxgiIndex}] {gpu.Summary}  ->  {(ok ? "supported" : "not supported")}: {reason}{(gpu == best ? "   (default)" : "")}");
        }
        Print($"NVML: {(NvmlProbe.Available ? "available" : "not available")}");
        return best is null ? 3 : 0;
    }

    private static async Task<int> Cameras()
    {
        var cameras = await CameraCatalog.ListAsync();
        if (cameras.Count == 0) Write(ConsoleColor.Yellow, "No cameras found.");
        for (int i = 0; i < cameras.Count; i++) Print($"[{i}] {cameras[i].DisplayName}");
        return cameras.Count == 0 ? 3 : 0;
    }

    private static async Task<int> Formats(string[] args)
    {
        var camera = await PickCamera(args);
        var formats = await CameraCatalog.ListFormatsAsync(camera);
        var recommended = CameraFormat.Recommend(formats);
        Print($"{camera.DisplayName}: {formats.Count} modes");
        foreach (var f in formats)
            Write(f == recommended ? ConsoleColor.Green : ConsoleColor.Gray, $"  {f.Key,-28} {f}{(f == recommended ? "   (recommended)" : "")}");
        return 0;
    }

    private static int Inspect()
    {
        int code = 0;
        foreach (var model in ModelCatalog.All)
        {
            var status = ModelCatalog.Check(model);
            Write(status.Verified ? ConsoleColor.Green : ConsoleColor.Red, $"{model.FileName}: {status.Message}");
            if (status.Path is null)
            {
                code = 1;
                continue;
            }
            if (model.Kind == ModelKind.MediaPipeTask)
            {
                Print(MediaPipeGestureModel.FromTask(File.ReadAllBytes(status.Path)).Describe());
                continue;
            }
            Print(OnnxModelInfo.Parse(File.ReadAllBytes(status.Path)).Describe());
        }
        return code;
    }

    private static async Task<int> SelfTest(string[] args, ILog log)
    {
        var options = new SelfTestOptions
        {
            Adapter = Has(args, "--cpu") ? OrtFactory.CpuAdapter : IntArg(args, "--gpu") ?? AppSettings.AutoAdapter,
            IncludeTraining = !Has(args, "--no-training"),
        };
        Print("HandGI self-test");
        var results = await SelfTests.RunAsync(options, log, new Progress<SelfTestResult>(_ => { }));
        foreach (var r in results)
        {
            var color = r.Outcome switch
            {
                SelfTestOutcome.Pass => ConsoleColor.Green,
                SelfTestOutcome.Warn => ConsoleColor.Yellow,
                SelfTestOutcome.Fail => ConsoleColor.Red,
                _ => ConsoleColor.DarkGray,
            };
            Write(color, $"{r.Badge}  {r.Name} ({r.Ms:0} ms)");
            Print($"      {r.Detail}");
        }
        int failed = results.Count(r => r.Outcome == SelfTestOutcome.Fail);
        int warned = results.Count(r => r.Outcome == SelfTestOutcome.Warn);
        Write(failed == 0 ? ConsoleColor.Green : ConsoleColor.Red,
            $"{results.Count(r => r.Outcome == SelfTestOutcome.Pass)} passed, {warned} warnings, {failed} failed, {results.Count(r => r.Outcome == SelfTestOutcome.Skip)} skipped.");
        return failed == 0 ? 0 : 1;
    }

    private static async Task<int> RunEngine(string[] args, ILog log)
    {
        var camera = await PickCamera(args);
        var formats = await CameraCatalog.ListFormatsAsync(camera);
        string? formatKey = StringArg(args, "--format");
        var format = formatKey is null || formatKey == "auto"
            ? CameraFormat.Recommend(formats)
            : formats.FirstOrDefault(f => f.Key.Equals(formatKey, StringComparison.OrdinalIgnoreCase))
              ?? throw new ArgumentException($"Format '{formatKey}' not offered. Run 'formats' to list them.");

        int adapter;
        string deviceName;
        if (Has(args, "--cpu"))
        {
            adapter = OrtFactory.CpuAdapter;
            deviceName = "CPU (compatibility mode)";
        }
        else
        {
            var gpus = GpuCatalog.Enumerate();
            int? requested = IntArg(args, "--gpu");
            var gpu = requested is int index ? gpus.FirstOrDefault(g => g.DxgiIndex == index) : GpuCatalog.PickBest(gpus);
            if (gpu is null)
            {
                Write(ConsoleColor.Red, "No supported NVIDIA GPU found (GTX 1060 or newer). Use --gpu N to force an adapter or --cpu for compatibility mode.");
                return 3;
            }
            adapter = gpu.DxgiIndex;
            deviceName = gpu.Name;
        }

        var profile = StringArg(args, "--profile") is string path ? GestureProfile.Load(path) : GestureProfile.CreateDefault();
        if (!profile.IsUsable) Write(ConsoleColor.Yellow, $"Profile '{profile.Name}' has every gesture switched off: hands are tracked, no gesture will be recognised.");
        else if (profile.NeedsTraining) Write(ConsoleColor.Yellow, $"Profile '{profile.Name}': your own gestures are not learned yet; Google's ready-made gestures work.");
        bool armed = Has(args, "--armed");
        double seconds = DoubleArg(args, "--seconds") ?? double.PositiveInfinity;

        var palm = ModelCatalog.LoadVerified(ModelCatalog.Palm);
        var hand = ModelCatalog.LoadVerified(ModelCatalog.Hand);
        var gestures = ModelCatalog.LoadGestures();
        using var volume = new VolumeController(log);
        await using var engine = new GestureEngine(palm, hand, gestures, volume, log, profile) { Armed = armed };
        engine.StateChanged += (state, message) => Write(state == EngineState.Faulted ? ConsoleColor.Red : ConsoleColor.Cyan, $"[{state}] {message}");

        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Cancel();
        };

        await engine.StartAsync(camera, format, Has(args, "--shared"), new EngineOptions
        {
            DxgiIndex = adapter,
            DeviceName = deviceName,
            Preview = false,
        });
        Print(armed ? "ARMED: gestures change the volume. Ctrl+C stops." : "Dry run: gestures are reported, the volume never changes. Ctrl+C stops.");

        var started = DateTime.UtcNow;
        var report = new FrameReport();
        long seen = 0;
        while (!stop.IsCancellationRequested && (DateTime.UtcNow - started).TotalSeconds < seconds && engine.State != EngineState.Faulted)
        {
            try
            {
                await Task.Delay(1000, stop.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            while (engine.TryDequeueEvent(out var e))
            {
                if (e.Kind == GestureEventKind.Action || e.Kind == GestureEventKind.Started)
                    Write(e.DryRun ? ConsoleColor.Gray : ConsoleColor.Green, $"  {e.When:HH:mm:ss.fff}  {e.ClassName,-14} {e.Description}{(e.DryRun ? "  (dry run)" : "")}  {e.FrameToDecisionMs:0} ms");
            }
            var latency = engine.GetLatency();
            bool hand0 = engine.TryGetReport(report, ref seen) && report.HasHand;
            Print($"{latency.Fps,5:0.0} fps · {(latency.CameraClock ? "capture" : "arrival")}->decision p50 {latency.P50:0.0} ms p95 {latency.P95:0.0} ms · GPU p50 {latency.GpuP50:0.00} ms · {(hand0 ? "hand" : "no hand")} · frames {latency.Frames} (replaced {latency.Replaced})");
        }

        await engine.StopAsync();
        return engine.State == EngineState.Faulted ? 1 : 0;
    }

    private static async Task<CameraInfo> PickCamera(string[] args)
    {
        var cameras = await CameraCatalog.ListAsync();
        if (cameras.Count == 0) throw new InvalidOperationException("No cameras found.");
        int index = IntArg(args, "--camera") ?? 0;
        if (index < 0 || index >= cameras.Count) throw new ArgumentException($"Camera {index} does not exist ({cameras.Count} found).");
        return cameras[index];
    }

    private static bool Has(string[] args, string flag) => args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));

    private static string? StringArg(string[] args, string name)
    {
        int i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static int? IntArg(string[] args, string name) => int.TryParse(StringArg(args, name), out int v) ? v : null;

    private static double? DoubleArg(string[] args, string name) =>
        double.TryParse(StringArg(args, name), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : null;

    private static void Print(string text)
    {
        try { Console.WriteLine(text); } catch (IOException) { }
        TraceLog.I(text);
    }

    private static void Write(ConsoleColor color, string text)
    {
        try
        {
            var previous = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ForegroundColor = previous;
        }
        catch (IOException) { }
        TraceLog.I(text);
    }

    [DllImport("kernel32.dll")] private static extern bool AttachConsole(int processId);
}
