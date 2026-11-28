// HandGI engine (Angela López Mendoza · XAIHT), part of Tlachihuani. tlachihuani:trace-off (runs on every camera frame and in tight GPU and training loops; its own messages reach Tlachihuani.log through the HandGI log bridge)
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using HandGI.Core.Vision;

namespace HandGI.Core.Gestures;

/// <summary>What a recognised gesture does.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GestureAction
{
    /// <summary>Recognised but does nothing.</summary>
    None,
    /// <summary>Volume up by one step on entry, then repeats while held (accelerating).</summary>
    VolumeUp,
    /// <summary>Volume down by one step on entry, then repeats while held.</summary>
    VolumeDown,
    /// <summary>Toggles mute once per entry.</summary>
    MuteToggle,
    /// <summary>Continuous: while held, moving the hand up or down sets the volume like a lever.</summary>
    VolumeLever,
}

/// <summary>Where a gesture comes from.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GestureSource
{
    /// <summary>One of Google's seven gestures: it works at once, nothing to record.</summary>
    ReadyMade,
    /// <summary>A gesture you taught HandGI by showing it to the camera.</summary>
    Mine,
}

public sealed class GestureClass
{
    public string Name { get; set; } = "";
    public GestureAction Action { get; set; }
    public string Hint { get; set; } = "";
    public GestureSource Source { get; set; } = GestureSource.Mine;
    /// <summary>Ready-made: Google's label (e.g. "Thumb_Up"). Yours: the ready-made gesture it replaces, if any.</summary>
    public string? ReadyMadeId { get; set; }
    /// <summary>A switched-off gesture is never recognised (a replaced ready-made gesture is switched off).</summary>
    public bool Enabled { get; set; } = true;

    public GestureClass() { }

    public GestureClass(string name, GestureAction action, string hint, GestureSource source = GestureSource.Mine, string? readyMadeId = null)
    {
        Name = name;
        Action = action;
        Hint = hint;
        Source = source;
        ReadyMadeId = readyMadeId;
    }

    [JsonIgnore] public bool IsReadyMade => Source == GestureSource.ReadyMade;
    [JsonIgnore] public ReadyGesture? Ready => IsReadyMade ? ReadyGestures.Find(ReadyMadeId) : null;
    [JsonIgnore] public string Emoji => Ready?.Emoji ?? "★";
    [JsonIgnore] public string DisplayName => $"{Emoji}  {Name}";
}

/// <summary>
/// Your own gestures, learned on top of Google's 128 numbers: the 128 → 64 → 32 → 8 head (class 0 is
/// "not one of mine", classes 1..7 are your gestures), the normalisation of the 128 numbers, and per
/// gesture the centre of its examples with the lowest similarity that still counts as that gesture.
/// </summary>
public sealed class ClassifierWeights
{
    private const int E = HandGraphs.EmbeddingSize;
    private const int K = HandGraphs.MaxClasses;

    /// <summary>Your gestures + 1 (the "not one of mine" class).</summary>
    public int ClassCount { get; set; }
    public float[] Mean { get; set; } = new float[E];
    public float[] InvStd { get; set; } = new float[E];
    public float[] W1 { get; set; } = new float[E * HandGraphs.Hidden1];
    public float[] B1 { get; set; } = new float[HandGraphs.Hidden1];
    public float[] W2 { get; set; } = new float[HandGraphs.Hidden1 * HandGraphs.Hidden2];
    public float[] B2 { get; set; } = new float[HandGraphs.Hidden2];
    public float[] W3 { get; set; } = new float[HandGraphs.Hidden2 * K];
    public float[] B3 { get; set; } = new float[K];
    /// <summary>Unit vectors in the whitened embedding space, one per gesture of yours: the centre of its examples.</summary>
    public float[] Centroids { get; set; } = new float[HandGraphs.MaxOwnGestures * E];
    /// <summary>Per gesture of yours: the lowest cosine similarity to its centre that still counts as that gesture.</summary>
    public float[] Floors { get; set; } = new float[HandGraphs.MaxOwnGestures];
    /// <summary>
    /// Per gesture of yours: the centre of its examples as the OTHER hand shows them (their mirror image),
    /// so a gesture recorded with one hand works with the other one too. All zero: only the recorded hand.
    /// </summary>
    public float[] MirrorCentroids { get; set; } = new float[HandGraphs.MaxOwnGestures * E];
    /// <summary>Per gesture of yours: the lowest similarity to its other-hand centre that still counts (0 = the other hand is not used).</summary>
    public float[] MirrorFloors { get; set; } = new float[HandGraphs.MaxOwnGestures];
    /// <summary>Learned by a HandGI that knows both hands (weights learned before only cover the hand that was recorded).</summary>
    public bool HandsChecked { get; set; }

    /// <summary>Your gesture number <paramref name="mine"/> (0, 1, 2…) is recognised with both hands.</summary>
    public bool BothHands(int mine) => mine >= 0 && mine < MirrorFloors.Length && MirrorFloors[mine] > 0;

    public bool IsValid() =>
        ClassCount >= 2 && ClassCount <= K &&
        Mean.Length == E && InvStd.Length == E &&
        W1.Length == E * HandGraphs.Hidden1 && B1.Length == HandGraphs.Hidden1 &&
        W2.Length == HandGraphs.Hidden1 * HandGraphs.Hidden2 && B2.Length == HandGraphs.Hidden2 &&
        W3.Length == HandGraphs.Hidden2 * K && B3.Length == K &&
        Centroids.Length == HandGraphs.MaxOwnGestures * E && Floors.Length == HandGraphs.MaxOwnGestures &&
        MirrorCentroids.Length == HandGraphs.MaxOwnGestures * E && MirrorFloors.Length == HandGraphs.MaxOwnGestures;
}

/// <summary>Timing and threshold rules that turn per-frame probabilities into deliberate actions.</summary>
public sealed class DecisionSettings
{
    /// <summary>Exponential smoothing of class probabilities (1 = no smoothing).</summary>
    public float Smoothing { get; set; } = 0.55f;
    /// <summary>A gesture starts when its smoothed probability reaches this value…</summary>
    public float EnterThreshold { get; set; } = 0.65f;
    /// <summary>…and ends when it drops below this one (hysteresis).</summary>
    public float ExitThreshold { get; set; } = 0.40f;
    /// <summary>Consecutive frames above the enter threshold before a gesture starts.</summary>
    public int EnterFrames { get; set; } = 2;
    public int RepeatMs { get; set; } = 250;
    public int AccelerateAfterMs { get; set; } = 1000;
    public float Step { get; set; } = 0.04f;
    public float AcceleratedStep { get; set; } = 0.06f;
    public int CooldownMs { get; set; } = 150;
    /// <summary>Volume change per full frame height of hand movement in lever mode.</summary>
    public float LeverGain { get; set; } = 1.6f;
    public float MaxVolume { get; set; } = 1.0f;

    public DecisionSettings Clone() => (DecisionSettings)MemberwiseClone();
}

/// <summary>
/// Recorded examples of your own gestures: per sample the 21 landmarks in MediaPipe's
/// object-normalised form (63 numbers) and the gesture it shows. Images are never stored.
/// The label is the gesture's position among your own gestures (0, 1, 2…).
/// </summary>
public sealed class SampleSet
{
    public const int Dimension = HandGraphs.GestureInputCount;

    [JsonIgnore]
    public List<float[]> Features { get; } = new();

    public List<int> Labels { get; set; } = new();

    /// <summary>Samples packed as little-endian float32 in Base64 (compact JSON).</summary>
    public string Data
    {
        get
        {
            var all = new float[Features.Count * Dimension];
            for (int i = 0; i < Features.Count; i++) Features[i].CopyTo(all, i * Dimension);
            return Convert.ToBase64String(MemoryMarshal.AsBytes(all.AsSpan()));
        }
        set
        {
            Features.Clear();
            if (string.IsNullOrEmpty(value)) return;
            var floats = MemoryMarshal.Cast<byte, float>(Convert.FromBase64String(value));
            for (int i = 0; i + Dimension <= floats.Length; i += Dimension)
                Features.Add(floats.Slice(i, Dimension).ToArray());
        }
    }

    public int Count => Math.Min(Features.Count, Labels.Count);

    public void Add(float[] features, int label)
    {
        if (features.Length != Dimension) throw new ArgumentException($"A sample has {Dimension} values, not {features.Length}.", nameof(features));
        Features.Add((float[])features.Clone());
        Labels.Add(label);
    }

    public int CountOf(int label) => Labels.Count(l => l == label);

    public void RemoveClass(int label, bool shiftHigherLabels)
    {
        for (int i = Labels.Count - 1; i >= 0; i--)
        {
            if (Labels[i] == label)
            {
                Labels.RemoveAt(i);
                Features.RemoveAt(i);
            }
            else if (shiftHigherLabels && Labels[i] > label)
            {
                Labels[i]--;
            }
        }
    }

    public void Clear()
    {
        Features.Clear();
        Labels.Clear();
    }
}

public sealed class TrainingSummary
{
    public DateTime TrainedUtc { get; set; }
    public string Device { get; set; } = "";
    public int Samples { get; set; }
    /// <summary>Hand shapes HandGI generated as "not one of yours" counter-examples.</summary>
    public int Negatives { get; set; }
    public int Steps { get; set; }
    public double TotalMs { get; set; }
    public float TrainAccuracy { get; set; }
    public float ValidationAccuracy { get; set; }
    public int[][] Confusion { get; set; } = Array.Empty<int[]>();
}

/// <summary>
/// A named set of gestures: Google's seven ready-made ones (each can do something, nothing, or be
/// replaced) and your own ones with their recorded examples, the learned weights and the decision rules.
/// </summary>
public sealed class GestureProfile
{
    public const int CurrentVersion = 2;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public int Version { get; set; } = CurrentVersion;
    public string Name { get; set; } = "My gestures";
    /// <summary>Every gesture: the ready-made ones first, then yours.</summary>
    public List<GestureClass> Classes { get; set; } = new();
    public SampleSet Samples { get; set; } = new();
    public ClassifierWeights? Weights { get; set; }
    public TrainingSummary? Training { get; set; }
    public DecisionSettings Decision { get; set; } = new();
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public string? FilePath { get; set; }

    /// <summary>Set when the file was upgraded from an older version (what was kept, what to redo).</summary>
    [JsonIgnore]
    public string? MigrationNote { get; private set; }

    /// <summary>Positions in <see cref="Classes"/> of your own gestures, in order (your gesture n is head class n+1).</summary>
    [JsonIgnore]
    public int[] MineIndices => Classes.Select((c, i) => (c, i)).Where(x => !x.c.IsReadyMade).Select(x => x.i).ToArray();

    [JsonIgnore]
    public int MineCount => Classes.Count(c => !c.IsReadyMade);

    /// <summary>True when your own gestures were learned for exactly the current list of them.</summary>
    [JsonIgnore]
    public bool IsTrained => MineCount > 0 && Weights is not null && Weights.IsValid() && Weights.ClassCount == MineCount + 1;

    [JsonIgnore]
    public bool HasReadyMade => Classes.Any(c => c.IsReadyMade && c.Enabled);

    /// <summary>Something can be recognised: an enabled ready-made gesture or your learned ones.</summary>
    [JsonIgnore]
    public bool IsUsable => HasReadyMade || IsTrained;

    /// <summary>You added gestures of your own that have not been learned yet.</summary>
    [JsonIgnore]
    public bool NeedsTraining => MineCount > 0 && !IsTrained;

    /// <summary>The position of a gesture among your own ones (its sample label), or -1 for a ready-made one.</summary>
    public int MineOrdinal(int classIndex) => Array.IndexOf(MineIndices, classIndex);

    public static GestureProfile CreateDefault()
    {
        var profile = new GestureProfile { Name = "My gestures" };
        foreach (var r in ReadyGestures.All)
            profile.Classes.Add(new GestureClass(r.Name, r.DefaultAction, r.Hint, GestureSource.ReadyMade, r.Id));
        return profile;
    }

    /// <summary>Adds a gesture of your own (to record and learn). Returns its position in <see cref="Classes"/>.</summary>
    public int AddMine(string name, GestureAction action, string? replaces = null, string? hint = null)
    {
        if (MineCount >= HandGraphs.MaxOwnGestures)
            throw new InvalidOperationException($"HandGI learns up to {HandGraphs.MaxOwnGestures} gestures of your own.");
        Classes.Add(new GestureClass(name, action, hint ?? "Your own gesture: show it steadily while HandGI records.", GestureSource.Mine, replaces));
        Weights = null; // the list of your gestures changed: they must be learned again
        return Classes.Count - 1;
    }

    /// <summary>Switches a ready-made gesture off and adds your own one in its place, doing the same thing.</summary>
    public int ReplaceWithMine(int readyIndex)
    {
        var ready = Classes[readyIndex];
        if (!ready.IsReadyMade) throw new InvalidOperationException("Only a ready-made gesture can be replaced.");
        ready.Enabled = false;
        return AddMine($"My {ready.Name.ToLowerInvariant()}", ready.Action, ready.ReadyMadeId,
            $"Your own way of doing \"{ready.Name}\" ({ActionText(ready.Action)}). Show it steadily while HandGI records.");
    }

    /// <summary>Removes one of your gestures with its examples; a ready-made gesture it replaced comes back.</summary>
    public void RemoveMine(int classIndex)
    {
        int ordinal = MineOrdinal(classIndex);
        if (ordinal < 0) throw new InvalidOperationException("Ready-made gestures cannot be removed; switch them off instead.");
        string? replaced = Classes[classIndex].ReadyMadeId;
        Samples.RemoveClass(ordinal, shiftHigherLabels: true);
        Classes.RemoveAt(classIndex);
        Weights = null;
        if (replaced is not null && !Classes.Any(c => !c.IsReadyMade && c.ReadyMadeId == replaced))
        {
            var ready = Classes.FirstOrDefault(c => c.IsReadyMade && c.ReadyMadeId == replaced);
            if (ready is not null) ready.Enabled = true;
        }
    }

    /// <summary>
    /// A copy for the engine's own thread, so the window can keep editing this profile (adding,
    /// switching off, renaming) while frames are being decided. The decision rules stay shared, so
    /// a slider still takes effect at once; anything else takes effect when the copy is handed over.
    /// </summary>
    public GestureProfile RuntimeCopy() => new()
    {
        Version = Version,
        Name = Name,
        Classes = Classes.Select(c => new GestureClass(c.Name, c.Action, c.Hint, c.Source, c.ReadyMadeId) { Enabled = c.Enabled }).ToList(),
        Weights = Weights,
        Training = Training,
        Decision = Decision,
        ModifiedUtc = ModifiedUtc,
        FilePath = FilePath,
    };

    public static string ActionText(GestureAction action) => action switch
    {
        GestureAction.VolumeUp => "Volume up",
        GestureAction.VolumeDown => "Volume down",
        GestureAction.MuteToggle => "Mute / unmute",
        GestureAction.VolumeLever => "Volume lever",
        _ => "Nothing",
    };

    public void Save(string path)
    {
        Version = CurrentVersion;
        ModifiedUtc = DateTime.UtcNow;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(tmp, path, overwrite: true);
        FilePath = path;
    }

    public static GestureProfile Load(string path)
    {
        string json = File.ReadAllText(path);
        var root = JsonNode.Parse(json) ?? throw new InvalidDataException("Empty profile file.");
        int version = root["Version"]?.GetValue<int>() ?? 1;
        var profile = version < 2
            ? MigrateFromVersion1(root)
            : JsonSerializer.Deserialize<GestureProfile>(json, JsonOptions) ?? throw new InvalidDataException("Empty profile file.");
        profile.FilePath = path;
        if (profile.Weights is not null && !profile.Weights.IsValid()) profile.Weights = null;
        profile.EnsureReadyMade();
        return profile;
    }

    /// <summary>Every ready-made gesture appears exactly once, first and in the standard order.</summary>
    private void EnsureReadyMade()
    {
        var mine = Classes.Where(c => !c.IsReadyMade).ToList();
        var ready = new List<GestureClass>();
        foreach (var r in ReadyGestures.All)
        {
            var existing = Classes.FirstOrDefault(c => c.IsReadyMade && c.ReadyMadeId == r.Id);
            ready.Add(existing ?? new GestureClass(r.Name, GestureAction.None, r.Hint, GestureSource.ReadyMade, r.Id));
        }
        Classes = ready.Concat(mine).ToList();
    }

    /// <summary>
    /// Version 1 profiles stored 66 hand-shape features per sample and a classifier on them. The shape
    /// part (63 wrist-relative coordinates divided by the palm length) converts exactly to MediaPipe's
    /// object-normalised form, so recorded examples are kept; the weights are not (learn again).
    /// </summary>
    private static GestureProfile MigrateFromVersion1(JsonNode root)
    {
        var profile = CreateDefault();
        profile.Name = root["Name"]?.GetValue<string>() ?? profile.Name;
        var decision = root["Decision"]?.Deserialize<DecisionSettings>(JsonOptions);
        if (decision is not null)
        {
            if (MathF.Abs(decision.EnterThreshold - 0.80f) < 1e-4f && MathF.Abs(decision.ExitThreshold - 0.50f) < 1e-4f)
            {
                decision.EnterThreshold = 0.65f; // the old defaults, tuned for the old classifier
                decision.ExitThreshold = 0.40f;
            }
            profile.Decision = decision;
        }

        var labels = root["Samples"]?["Labels"]?.Deserialize<List<int>>() ?? new List<int>();
        var rows = new List<float[]>();
        if (root["Samples"]?["Data"]?.GetValue<string>() is string data && data.Length > 0)
        {
            var floats = MemoryMarshal.Cast<byte, float>(Convert.FromBase64String(data));
            for (int i = 0; i + 66 <= floats.Length; i += 66) rows.Add(floats.Slice(i, 66).ToArray());
        }

        int kept = 0, samples = 0;
        if (root["Classes"] is JsonArray classes)
        {
            for (int c = 0; c < classes.Count && profile.MineCount < HandGraphs.MaxOwnGestures; c++)
            {
                var mine = Enumerable.Range(0, Math.Min(labels.Count, rows.Count)).Where(i => labels[i] == c).ToList();
                if (mine.Count == 0) continue;
                var node = classes[c]!;
                var action = Enum.TryParse<GestureAction>(node["Action"]?.ToString(), out var a) ? a : GestureAction.None;
                profile.AddMine(node["Name"]?.GetValue<string>() ?? $"Gesture {c + 1}", action, null, node["Hint"]?.GetValue<string>());
                int ordinal = profile.MineCount - 1;
                foreach (int i in mine) profile.Samples.Add(FromVersion1Features(rows[i]), ordinal);
                kept++;
                samples += mine.Count;
            }
        }
        profile.Weights = null;
        profile.MigrationNote = kept == 0
            ? "This profile was upgraded: it now starts with Google's 7 ready-made gestures (it had no recorded examples to keep)."
            : $"This profile was upgraded: Google's 7 ready-made gestures were added and {kept} gesture(s) of yours with {samples} examples were kept. Learn them again in the Trainer.";
        return profile;
    }

    /// <summary>63 v1 shape values (wrist-relative, ÷ palm length) → MediaPipe object-normalised landmarks.</summary>
    public static float[] FromVersion1Features(ReadOnlySpan<float> v1)
    {
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i < HandGraphs.LandmarkCount; i++)
        {
            minX = MathF.Min(minX, v1[i * 3]);
            maxX = MathF.Max(maxX, v1[i * 3]);
            minY = MathF.Min(minY, v1[i * 3 + 1]);
            maxY = MathF.Max(maxY, v1[i * 3 + 1]);
        }
        float s = MathF.Max(1e-6f, MathF.Max(maxX - minX, maxY - minY));
        var result = new float[SampleSet.Dimension];
        for (int i = 0; i < HandGraphs.LandmarkCount; i++)
        {
            result[i * 3] = v1[i * 3] / s;
            result[i * 3 + 1] = v1[i * 3 + 1] / s;
            result[i * 3 + 2] = 2.5f * v1[i * 3 + 2] / s; // MediaPipe divides z by 0.4
        }
        return result;
    }

    public static string SafeFileName(string name)
    {
        var chars = name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray();
        var s = new string(chars).Trim();
        return string.IsNullOrEmpty(s) ? "profile" : s;
    }
}
