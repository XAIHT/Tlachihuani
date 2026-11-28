// Tlachihuani — Angela López Mendoza · XAIHT
// tlachihuani:trace-off (reports progress many times a second; the outcome is written explicitly)
using HandGI.Core.Gestures;
using HandGI.Core.Training;
using Tlachihuani.Contracts;

namespace HandGI.App;

// What HandGI's windows need from the HandGI service.
internal interface IHandGIHost
{
    AppController App { get; }
    bool HandControlOn { get; }
    bool Starting { get; }
    string ProblemText { get; }
    void SetHandControl(bool on, string source);
    Task StartCameraAsync();
    Task StopCameraAsync();
    void ShowExpert();
}

// Learning the signs a user taught: the expert view's "Learn" in one call, its outcome in plain words.
internal static class SignLearning
{
    internal const int MinExamples = 10;

    internal sealed record Outcome(bool Learned, bool Reliable, string Message);

    internal static async Task<Outcome> LearnAsync(AppController app, IProgress<double>? progress = null)
    {
        var profile = app.Profile;
        var mine = profile.MineIndices;
        if (mine.Length == 0) return new(true, true, "There is nothing to learn.");
        var missing = mine.Select((index, ordinal) => (profile.Classes[index].Name, Count: profile.Samples.CountOf(ordinal)))
            .Where(item => item.Count < MinExamples).ToList();
        if (missing.Count > 0)
            return new(false, false, $"Show {string.Join(", ", missing.Select(m => "\"" + m.Name + "\""))} to the camera a little longer first.");
        var (adapter, device, note) = app.ResolveAdapter();
        if (adapter == AppController.NoAdapter) return new(false, false, note);
        var model = app.GestureModel;
        var samples = profile.Samples;
        int count = mine.Length;
        bool fallback = app.Settings.AllowCpuFallback;
        var report = new Progress<TrainingProgress>(t => progress?.Report(t.Steps == 0 ? 0 : (double)t.Step / t.Steps));
        TraceLog.I($"HandGI learns the user's {count} sign(s) on {device}");
        var result = await Task.Run(() => GpuTrainer.Train(samples, count, model, new TrainingOptions(), adapter, fallback, report));
        profile.Weights = result.Weights;
        profile.Training = new TrainingSummary
        {
            TrainedUtc = DateTime.UtcNow,
            Device = device,
            Samples = result.Samples,
            Negatives = result.Negatives,
            Steps = result.Steps,
            TotalMs = result.TotalMs,
            TrainAccuracy = result.TrainAccuracy,
            ValidationAccuracy = result.ValidationAccuracy,
            Confusion = result.Confusion,
        };
        app.ProfileEdited();
        app.SaveProfile();
        bool reliable = result.ValidationAccuracy >= 0.9f;
        TraceLog.I($"HandGI learned the user's {count} sign(s): {result.ValidationAccuracy:P1} right on examples it had not seen, {result.TotalMs:0} ms on {device}");
        return new(true, reliable, reliable
            ? "HandGI knows your signs well."
            : "HandGI sometimes mixes your signs up. Teaching them again, with a little more variety, helps.");
    }
}
