// HandGI engine (Angela López Mendoza · XAIHT), part of Tlachihuani. tlachihuani:trace-off (runs on every camera frame and in tight GPU and training loops; its own messages reach Tlachihuani.log through the HandGI log bridge)
using System.Diagnostics;
using HandGI.Core.Gestures;
using HandGI.Core.Gpu;
using HandGI.Core.Vision;
using Microsoft.ML.OnnxRuntime;

namespace HandGI.Core.Training;

public sealed class TrainingOptions
{
    public int Steps { get; set; } = 600;
    public float LearningRate { get; set; } = 3e-3f;
    public float WeightDecay { get; set; } = 1e-4f;
    public float LabelSmoothing { get; set; } = 0.05f;
    public float ValidationFraction { get; set; } = 0.15f;
    public int Seed { get; set; } = 20261125;
    public float RotationDegrees { get; set; } = 12f;
    /// <summary>Landmark jitter in MediaPipe's normalised units (a hand is about 1 wide).</summary>
    public float Noise { get; set; } = 0.01f;
    /// <summary>Generated "not one of yours" hand shapes (see <see cref="SyntheticHands.NegativeBank"/>).</summary>
    public int Negatives { get; set; } = 1600;
    /// <summary>
    /// Your gestures work with either hand: every example also teaches its mirror image (the other hand),
    /// examples recorded with the other hand are folded in with the rest, and two of your gestures that are
    /// mirror images of each other keep to the hand they were recorded with. False: the recorded hand only.
    /// </summary>
    public bool EitherHand { get; set; } = true;
}

public sealed record TrainingProgress(int Step, int Steps, float Loss, float BatchAccuracy, double ElapsedMs);

/// <summary>What Google's ready-made classifier sees in one of your gestures (its most likely gesture and how sure it is).</summary>
public sealed record Lookalike(int MineOrdinal, int ReadyLabel, float MeanProbability);

public sealed class TrainingResult
{
    public required ClassifierWeights Weights { get; init; }
    public float TrainAccuracy { get; init; }
    public float ValidationAccuracy { get; init; }
    /// <summary>Rows: what was shown; columns: what HandGI answered. Row/column 0 is "not one of yours".</summary>
    public int[][] Confusion { get; init; } = Array.Empty<int[]>();
    public int TrainCount { get; init; }
    public int ValidationCount { get; init; }
    public int Samples { get; init; }
    public int Negatives { get; init; }
    public int NegativesDropped { get; init; }
    public int Steps { get; init; }
    public double TotalMs { get; init; }
    public double MsPerStep { get; init; }
    public IReadOnlyList<TrainingProgress> Curve { get; init; } = Array.Empty<TrainingProgress>();
    public IReadOnlyList<Lookalike> Lookalikes { get; init; } = Array.Empty<Lookalike>();
    /// <summary>Per gesture of yours: recognised with both hands (false: only with the hand it was recorded with).</summary>
    public bool[] BothHands { get; init; } = Array.Empty<bool>();
    /// <summary>Pairs of your gestures that are mirror images of each other: each keeps to the hand it was recorded with.</summary>
    public IReadOnlyList<(int First, int Second)> MirrorTwins { get; init; } = Array.Empty<(int, int)>();
    /// <summary>Examples recorded with the other hand than most examples of their gesture, folded in with the rest.</summary>
    public int OtherHandExamples { get; init; }
    /// <summary>Accuracy on the held-out examples shown as the other hand (NaN when no gesture uses both hands).</summary>
    public float OtherHandAccuracy { get; init; } = float.NaN;
}

/// <summary>
/// Learns your own gestures entirely on the GPU through DirectML, on top of Google's gesture
/// embedder: the embeddings, every AdamW step and the evaluation are ONNX graphs. Counter-examples
/// ("not one of yours") are generated hand shapes, so you never record a resting hand. The CPU fills
/// batches, draws the augmentation random numbers and swaps two parameter buffers between steps.
/// </summary>
public static class GpuTrainer
{
    public const int Batch = 2048;
    public const int MinSamples = 10;
    private const int X = TrainingGraphs.X;
    private const int E = TrainingGraphs.E;
    private const int K = TrainingGraphs.K;
    private const int R = HandGraphs.ReadyCount;

    public static TrainingResult Train(
        SampleSet samples,
        int mineCount,
        MediaPipeGestureModel model,
        TrainingOptions options,
        int dxgiIndex,
        bool allowCpuFallback,
        IProgress<TrainingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (mineCount < 1 || mineCount > HandGraphs.MaxOwnGestures)
            throw new ArgumentOutOfRangeException(nameof(mineCount), $"HandGI learns 1 to {HandGraphs.MaxOwnGestures} gestures of your own.");
        for (int c = 0; c < mineCount; c++)
        {
            int count = samples.CountOf(c);
            if (count < 3) throw new InvalidOperationException($"Your gesture #{c + 1} has only {count} examples; record a few seconds of it.");
        }
        int classCount = mineCount + 1;

        var owned = new List<IDisposable>();
        (float[] B, OrtValue V) T(params long[] shape)
        {
            var t = OrtFactory.PinnedFloat(shape);
            owned.Add(t.Value);
            return t;
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var embed = OrtFactory.CreateSession(GestureGraphs.BuildEmbed(model, Batch), dxgiIndex, allowCpuFallback, "train-embed");
            using var step = OrtFactory.CreateSession(TrainingGraphs.BuildTrainStep(Batch, model), dxgiIndex, allowCpuFallback, "train-step");
            using var eval = OrtFactory.CreateSession(TrainingGraphs.BuildEval(Batch, model), dxgiIndex, allowCpuFallback, "train-eval");
            using var run = new RunOptions();

            var x = T(Batch, X);
            var embeddingOut = T(Batch, E);
            var readyOut = T(Batch, R);

            // ---- 1. Google's 128 numbers for each example of yours and for the generated counter-examples,
            //         each one also as the other hand shows it (its mirror image)
            var own = Enumerable.Range(0, samples.Count).Where(i => samples.Labels[i] >= 0 && samples.Labels[i] < mineCount).ToList();
            var ownRows = own.Select(i => samples.Features[i]).ToList();
            var ownLabels = own.Select(i => samples.Labels[i]).ToList();
            var (ownEmbedding, ownReady) = Embed(embed, run, x, embeddingOut, readyOut, ownRows);
            bool eitherHand = options.EitherHand;
            var mirrorRows = ownRows.Select(r => MediaPipeGestureModel.Mirrored(r)).ToList();
            var (mirrorEmbedding, _) = Embed(embed, run, x, embeddingOut, readyOut, mirrorRows);
            var negatives = SyntheticHands.NegativeBank(options.Negatives, options.Seed ^ 0x5EED);
            var (negativeEmbedding, _) = Embed(embed, run, x, embeddingOut, readyOut, negatives);
            var negativeMirrors = negatives.Select(r => MediaPipeGestureModel.Mirrored(r)).ToList();
            var (negativeMirrorEmbedding, _) = Embed(embed, run, x, embeddingOut, readyOut, negativeMirrors);
            cancellationToken.ThrowIfCancellationRequested();

            // ---- 2. per gesture of yours: the centre of its examples and how far a hand may stray from it.
            //         For both hands, examples made with the other hand are first folded into their mirror
            //         image (so the centre stays as tight as with one hand), and the mirror image of all the
            //         examples gives a second centre that recognises the other hand.
            var weights = new ClassifierWeights { ClassCount = classCount, HandsChecked = eitherHand };
            var ownUnit = ownEmbedding.Select(e => Unit(model, e)).ToList();
            var mirrorUnit = mirrorEmbedding.Select(e => Unit(model, e)).ToList();
            var negativeUnit = negativeEmbedding.Select(e => Unit(model, e)).ToList();
            var negativeMirrorUnit = negativeMirrorEmbedding.Select(e => Unit(model, e)).ToList();
            var folded = new bool[ownRows.Count];
            float[] Shown(int i) => folded[i] ? mirrorUnit[i] : ownUnit[i];
            float[] OtherHand(int i) => folded[i] ? ownUnit[i] : mirrorUnit[i];
            var membersOf = new List<int>[mineCount];
            Array.Fill(weights.Floors, 1f);
            for (int j = 0; j < mineCount; j++)
            {
                membersOf[j] = Enumerable.Range(0, ownLabels.Count).Where(i => ownLabels[i] == j).ToList();
                if (eitherHand) Fold(membersOf[j], ownUnit, mirrorUnit, folded);
                var centre = Centre(membersOf[j], Shown);
                centre.CopyTo(weights.Centroids, j * E);
                weights.Floors[j] = FloorOf(membersOf[j].Select(i => Dot(Shown(i), centre)));
                if (!eitherHand) continue;
                var other = Centre(membersOf[j], OtherHand);
                other.CopyTo(weights.MirrorCentroids, j * E);
                weights.MirrorFloors[j] = FloorOf(membersOf[j].Select(i => Dot(OtherHand(i), other)));
            }

            // ---- 2b. two gestures of yours that are mirror images of each other (one hand's sign looks like
            //          the other one with the other hand): each keeps to the hand it was recorded with,
            //          otherwise one would be taken for the other
            var twins = new List<(int First, int Second)>();
            if (eitherHand)
            {
                bool Reaches(int from, int to)
                {
                    var centre = weights.MirrorCentroids.AsSpan(from * E, E).ToArray();
                    float floor = weights.MirrorFloors[from];
                    if (Dot(centre, weights.Centroids.AsSpan(to * E, E)) >= Math.Min(floor, weights.Floors[to])) return true;
                    int inside = membersOf[to].Count(i => Dot(Shown(i), centre) >= floor);
                    return inside >= Math.Max(1, membersOf[to].Count / 20);
                }
                for (int j = 0; j < mineCount; j++)
                    for (int k = j + 1; k < mineCount; k++)
                        if (Reaches(j, k) || Reaches(k, j)) twins.Add((j, k));
                foreach (var (first, second) in twins)
                {
                    foreach (int t in new[] { first, second })
                    {
                        Array.Clear(weights.MirrorCentroids, t * E, E);
                        weights.MirrorFloors[t] = 0f;
                        // a gesture that keeps to its hand keeps every example exactly as it was recorded:
                        // a folded one would look like its mirror-image twin
                        foreach (int i in membersOf[t]) folded[i] = false;
                        var centre = Centre(membersOf[t], Shown);
                        centre.CopyTo(weights.Centroids, t * E);
                        weights.Floors[t] = FloorOf(membersOf[t].Select(i => Dot(Shown(i), centre)));
                    }
                }
            }

            // ---- 3. counter-examples that look like one of your gestures (with either hand) are left out.
            //         They are also shown as the other hand only when one of your gestures uses it: a
            //         gesture that keeps to its hand learns exactly as before.
            bool anyBothHands = Enumerable.Range(0, mineCount).Any(j => weights.BothHands(j));
            bool Near(float[] unit)
            {
                for (int j = 0; j < mineCount; j++)
                {
                    if (Dot(unit, weights.Centroids.AsSpan(j * E, E)) >= weights.Floors[j] - 0.02f) return true;
                    if (weights.BothHands(j) && Dot(unit, weights.MirrorCentroids.AsSpan(j * E, E)) >= weights.MirrorFloors[j] - 0.02f) return true;
                }
                return false;
            }
            var keptNegatives = new List<int>();
            for (int n = 0; n < negatives.Count; n++)
            {
                if (!Near(negativeUnit[n]) && (!anyBothHands || !Near(negativeMirrorUnit[n]))) keptNegatives.Add(n);
            }

            // ---- 4. training rows: your examples (class n+1, folded to the hand most of them show) and the
            //         counter-examples (class 0). A row that may be shown as the other hand is mirrored at
            //         random while learning (aug_flip), so the head learns both hands from the same examples.
            var rows = new List<float[]>();
            var labels = new List<int>();
            var rowEmbedding = new List<float[]>();
            var rowMirrorEmbedding = new List<float[]?>();
            var flippable = new List<bool>();
            for (int i = 0; i < ownRows.Count; i++)
            {
                bool both = weights.BothHands(ownLabels[i]);
                rows.Add(folded[i] ? mirrorRows[i] : ownRows[i]);
                labels.Add(ownLabels[i] + 1);
                rowEmbedding.Add(folded[i] ? mirrorEmbedding[i] : ownEmbedding[i]);
                rowMirrorEmbedding.Add(both ? (folded[i] ? ownEmbedding[i] : mirrorEmbedding[i]) : null);
                flippable.Add(both);
            }
            foreach (int n in keptNegatives)
            {
                rows.Add(negatives[n]);
                labels.Add(0);
                rowEmbedding.Add(negativeEmbedding[n]);
                rowMirrorEmbedding.Add(anyBothHands ? negativeMirrorEmbedding[n] : null);
                flippable.Add(anyBothHands);
            }

            var rng = new Random(options.Seed);
            var (trainRows, validationRows) = Split(labels, classCount, options.ValidationFraction, rng);
            var classWeight = new double[K];
            for (int c = 0; c < classCount; c++)
            {
                int count = trainRows.Count(i => labels[i] == c);
                classWeight[c] = count > 0 ? 1.0 / count : 0;
            }

            // ---- 5. normalisation of the 128 numbers (training rows, every class weighing the same)
            var mean = T(1, E);
            var invStd = T(1, E);
            double total = trainRows.Sum(i => classWeight[labels[i]]);
            for (int k = 0; k < E; k++)
            {
                // a row that is also learned as the other hand counts half as it is, half mirrored
                double m = 0, v = 0;
                foreach (int i in trainRows)
                {
                    double cw = classWeight[labels[i]];
                    m += rowMirrorEmbedding[i] is { } mirror ? cw * 0.5 * (rowEmbedding[i][k] + mirror[k]) : cw * rowEmbedding[i][k];
                }
                m /= total;
                foreach (int i in trainRows)
                {
                    double cw = classWeight[labels[i]];
                    double d = rowEmbedding[i][k] - m;
                    if (rowMirrorEmbedding[i] is { } mirror)
                    {
                        double dm = mirror[k] - m;
                        v += cw * 0.5 * (d * d + dm * dm);
                    }
                    else
                    {
                        v += cw * d * d;
                    }
                }
                v /= total;
                mean.B[k] = (float)m;
                invStd.B[k] = (float)(1.0 / Math.Sqrt(v + 1e-4));
            }
            mean.B.CopyTo(weights.Mean, 0);
            invStd.B.CopyTo(weights.InvStd, 0);

            // ---- 6. the head, trained on the GPU
            var y = T(Batch, K);
            var wn = T(Batch, 1);
            var valid = T(Batch, 1);
            var augCos = T(Batch, 1);
            var augSin = T(Batch, 1);
            var augFlip = T(Batch, 1);
            var augNoise = T(Batch, X);
            var mask = T(1, K);
            var smooth = T(1, K);
            var keep = T(1);
            var lrT = T(1);
            var lrWd = T(1);
            var loss = T(1, 1);
            var correct = T(1, 1);
            var probs = T(Batch, K);
            for (int c = 0; c < K; c++)
            {
                mask.B[c] = c < classCount ? 0f : -1e4f;
                smooth.B[c] = c < classCount ? options.LabelSmoothing / classCount : 0f;
            }
            keep.B[0] = 1f - options.LabelSmoothing;

            var setA = new Dictionary<string, (float[] B, OrtValue V)>();
            var setB = new Dictionary<string, (float[] B, OrtValue V)>();
            foreach (var name in TrainingGraphs.ParamNames)
            {
                foreach (var prefix in new[] { "", "m_", "v_" })
                {
                    setA[prefix + name] = T(TrainingGraphs.ParamShape(name));
                    setB[prefix + name] = T(TrainingGraphs.ParamShape(name));
                }
            }
            foreach (var name in TrainingGraphs.ParamNames)
            {
                var shape = TrainingGraphs.ParamShape(name);
                var buffer = setA[name].B;
                if (name.StartsWith('w'))
                {
                    double sigma = Math.Sqrt(2.0 / shape[0]);
                    for (int i = 0; i < buffer.Length; i++) buffer[i] = (float)(SyntheticHands.Gaussian(rng) * sigma);
                }
            }

            OrtIoBinding Bind((Dictionary<string, (float[] B, OrtValue V)> input, Dictionary<string, (float[] B, OrtValue V)> output) sets)
            {
                var b = step.CreateIoBinding();
                b.BindInput("x", x.V);
                b.BindInput("y", y.V);
                b.BindInput("wn", wn.V);
                b.BindInput("valid", valid.V);
                b.BindInput("aug_cos", augCos.V);
                b.BindInput("aug_sin", augSin.V);
                b.BindInput("aug_flip", augFlip.V);
                b.BindInput("aug_noise", augNoise.V);
                b.BindInput("mean", mean.V);
                b.BindInput("invstd", invStd.V);
                b.BindInput("mask", mask.V);
                b.BindInput("smooth", smooth.V);
                b.BindInput("keep", keep.V);
                b.BindInput("lr_t", lrT.V);
                b.BindInput("lr_wd", lrWd.V);
                foreach (var kv in sets.input) b.BindInput(kv.Key, kv.Value.V);
                foreach (var kv in sets.output) b.BindOutput(kv.Key + "_out", kv.Value.V);
                b.BindOutput("loss", loss.V);
                b.BindOutput("correct", correct.V);
                owned.Add(b);
                return b;
            }
            var bindAb = Bind((setA, setB));
            var bindBa = Bind((setB, setA));

            bool fullBatch = trainRows.Count <= Batch;
            List<int> batchRows = trainRows;
            if (fullBatch) FillRows(rows, labels, batchRows, x.B, y.B, wn.B, valid.B, classWeight, classCount);

            var curve = new List<TrainingProgress>();
            double rotation = options.RotationDegrees * Math.PI / 180.0;
            int steps = Math.Max(10, options.Steps);
            int warmup = Math.Min(30, steps / 10);
            Dictionary<string, (float[] B, OrtValue V)> latest = setA;

            for (int t = 1; t <= steps; t++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!fullBatch)
                {
                    batchRows = new List<int>(Batch);
                    for (int r = 0; r < Batch; r++) batchRows.Add(trainRows[rng.Next(trainRows.Count)]);
                    FillRows(rows, labels, batchRows, x.B, y.B, wn.B, valid.B, classWeight, classCount);
                }
                for (int r = 0; r < Batch; r++)
                {
                    double angle = (rng.NextDouble() * 2 - 1) * rotation;
                    augCos.B[r] = (float)Math.Cos(angle);
                    augSin.B[r] = (float)Math.Sin(angle);
                }
                // half of the rows that may be shown with the other hand are shown with it this step
                for (int r = 0; r < Batch; r++)
                    augFlip.B[r] = r < batchRows.Count && flippable[batchRows[r]] && rng.NextDouble() < 0.5 ? -1f : 1f;
                for (int i = 0; i < augNoise.B.Length; i++) augNoise.B[i] = (float)(SyntheticHands.Gaussian(rng) * options.Noise);

                double lr = t <= warmup
                    ? options.LearningRate * t / warmup
                    : options.LearningRate * (0.05 + 0.95 * 0.5 * (1 + Math.Cos(Math.PI * (t - warmup) / Math.Max(1, steps - warmup))));
                lrT.B[0] = (float)(lr * Math.Sqrt(1 - Math.Pow(0.999, t)) / (1 - Math.Pow(0.9, t)));
                lrWd.B[0] = (float)(lr * options.WeightDecay);

                bool odd = (t & 1) == 1;
                var binding = odd ? bindAb : bindBa;
                // ONNX Runtime copies a CPU input to the GPU when it is bound: bind again everything that
                // changed since this binding last ran (parameters, moments, augmentation, learning rate).
                if (!fullBatch || t <= 2)
                {
                    binding.BindInput("x", x.V);
                    binding.BindInput("y", y.V);
                    binding.BindInput("wn", wn.V);
                    binding.BindInput("valid", valid.V);
                }
                binding.BindInput("aug_cos", augCos.V);
                binding.BindInput("aug_sin", augSin.V);
                binding.BindInput("aug_flip", augFlip.V);
                binding.BindInput("aug_noise", augNoise.V);
                binding.BindInput("lr_t", lrT.V);
                binding.BindInput("lr_wd", lrWd.V);
                if (t <= 2)
                {
                    binding.BindInput("mean", mean.V);
                    binding.BindInput("invstd", invStd.V);
                }
                foreach (var kv in odd ? setA : setB) binding.BindInput(kv.Key, kv.Value.V);
                step.RunWithBinding(run, binding);
                latest = odd ? setB : setA;

                if (t % 10 == 0 || t == steps)
                {
                    int used = Math.Min(batchRows.Count, Batch);
                    var p = new TrainingProgress(t, steps, loss.B[0], used > 0 ? correct.B[0] / used : 0, stopwatch.Elapsed.TotalMilliseconds);
                    curve.Add(p);
                    progress?.Report(p);
                }
            }
            double trainMs = stopwatch.Elapsed.TotalMilliseconds;

            // ---- 7. evaluation on the GPU (training and validation rows, no augmentation)
            using var evalBinding = eval.CreateIoBinding();
            evalBinding.BindInput("x", x.V);
            evalBinding.BindInput("mean", mean.V);
            evalBinding.BindInput("invstd", invStd.V);
            evalBinding.BindInput("mask", mask.V);
            foreach (var name in TrainingGraphs.ParamNames) evalBinding.BindInput(name, latest[name].V);
            evalBinding.BindOutput("probs", probs.V);

            var confusion = new int[classCount][];
            for (int c = 0; c < classCount; c++) confusion[c] = new int[classCount];
            float trainAccuracy = Evaluate(rows, labels, trainRows, x.B, x.V, probs.B, eval, evalBinding, run, classCount, null);
            float validationAccuracy = validationRows.Count > 0
                ? Evaluate(rows, labels, validationRows, x.B, x.V, probs.B, eval, evalBinding, run, classCount, confusion)
                : trainAccuracy;
            if (validationRows.Count == 0) Evaluate(rows, labels, trainRows, x.B, x.V, probs.B, eval, evalBinding, run, classCount, confusion);

            // ---- 7b. the held-out examples shown with the other hand (gestures learned for both hands)
            float otherHandAccuracy = float.NaN;
            if (eitherHand)
            {
                var shownAsOther = new List<int>();
                foreach (int i in validationRows.Count > 0 ? validationRows : trainRows)
                {
                    if (labels[i] < 1 || !weights.BothHands(labels[i] - 1)) continue;
                    rows.Add(MediaPipeGestureModel.Mirrored(rows[i]));
                    labels.Add(labels[i]);
                    shownAsOther.Add(rows.Count - 1);
                }
                if (shownAsOther.Count > 0)
                    otherHandAccuracy = Evaluate(rows, labels, shownAsOther, x.B, x.V, probs.B, eval, evalBinding, run, classCount, null);
            }

            weights.W1 = (float[])latest["w1"].B.Clone();
            weights.B1 = (float[])latest["b1"].B.Clone();
            weights.W2 = (float[])latest["w2"].B.Clone();
            weights.B2 = (float[])latest["b2"].B.Clone();
            weights.W3 = (float[])latest["w3"].B.Clone();
            weights.B3 = (float[])latest["b3"].B.Clone();

            // ---- 8. what Google's ready-made classifier sees in each of your gestures
            var lookalikes = new List<Lookalike>();
            for (int j = 0; j < mineCount; j++)
            {
                var members = Enumerable.Range(0, ownLabels.Count).Where(i => ownLabels[i] == j).ToList();
                var average = new float[R];
                foreach (int i in members)
                    for (int c = 0; c < R; c++) average[c] += ownReady[i][c] / members.Count;
                int best = 0;
                for (int c = 1; c < R; c++)
                    if (average[c] > average[best]) best = c;
                lookalikes.Add(new Lookalike(j, best, average[best]));
            }

            return new TrainingResult
            {
                Weights = weights,
                TrainAccuracy = trainAccuracy,
                ValidationAccuracy = validationAccuracy,
                Confusion = confusion,
                TrainCount = trainRows.Count,
                ValidationCount = validationRows.Count,
                Samples = ownRows.Count,
                Negatives = keptNegatives.Count,
                NegativesDropped = negatives.Count - keptNegatives.Count,
                Steps = steps,
                TotalMs = stopwatch.Elapsed.TotalMilliseconds,
                MsPerStep = trainMs / steps,
                Curve = curve,
                Lookalikes = lookalikes,
                BothHands = Enumerable.Range(0, mineCount).Select(j => weights.BothHands(j)).ToArray(),
                MirrorTwins = twins,
                OtherHandExamples = folded.Count(f => f),
                OtherHandAccuracy = otherHandAccuracy,
            };
        }
        finally
        {
            foreach (var d in owned) d.Dispose();
        }
    }

    /// <summary>Runs Google's embedder on the GPU for a list of normalised landmark rows (128 numbers + ready-made probabilities each).</summary>
    public static (List<float[]> Embeddings, List<float[]> Ready) Embed(InferenceSession session, RunOptions run,
        (float[] B, OrtValue V) x, (float[] B, OrtValue V) embedding, (float[] B, OrtValue V) ready, IReadOnlyList<float[]> rows)
    {
        var embeddings = new List<float[]>(rows.Count);
        var readyList = new List<float[]>(rows.Count);
        using var binding = session.CreateIoBinding();
        binding.BindOutput("embedding", embedding.V);
        binding.BindOutput("ready", ready.V);
        for (int start = 0; start < rows.Count; start += Batch)
        {
            int n = Math.Min(Batch, rows.Count - start);
            Array.Clear(x.B);
            for (int i = 0; i < n; i++) rows[start + i].CopyTo(x.B, i * X);
            binding.BindInput("x", x.V); // the CPU rows reach the GPU when bound
            session.RunWithBinding(run, binding);
            for (int i = 0; i < n; i++)
            {
                embeddings.Add(embedding.B.AsSpan(i * E, E).ToArray());
                readyList.Add(ready.B.AsSpan(i * R, R).ToArray());
            }
        }
        return (embeddings, readyList);
    }

    private static float[] Unit(MediaPipeGestureModel model, float[] embedding)
    {
        var white = new float[E];
        model.Whiten(embedding, white);
        Normalize(white);
        return white;
    }

    /// <summary>
    /// Folds the examples of one gesture that were made with the other hand into their mirror image, so
    /// all of them show the same hand: starting from the first example, each example takes the form (as
    /// recorded or mirrored) closer to the centre of the others, until nothing changes. The hand most
    /// examples were recorded with is kept.
    /// </summary>
    private static void Fold(List<int> members, List<float[]> asRecorded, List<float[]> mirrored, bool[] folded)
    {
        if (members.Count == 0) return;
        var centre = (float[])asRecorded[members[0]].Clone();
        for (int round = 0; round < 10; round++)
        {
            bool changed = false;
            foreach (int i in members)
            {
                bool flip = Dot(mirrored[i], centre) > Dot(asRecorded[i], centre);
                if (flip == folded[i]) continue;
                folded[i] = flip;
                changed = true;
            }
            centre = Centre(members, i => folded[i] ? mirrored[i] : asRecorded[i]);
            if (!changed && round > 0) break;
        }
        if (members.Count(i => folded[i]) * 2 > members.Count)
            foreach (int i in members) folded[i] = !folded[i];
    }

    /// <summary>The unit-length centre of some examples' unit vectors.</summary>
    private static float[] Centre(List<int> members, Func<int, float[]> unitOf)
    {
        var centre = new float[E];
        foreach (int i in members)
        {
            var u = unitOf(i);
            for (int k = 0; k < E; k++) centre[k] += u[k];
        }
        Normalize(centre);
        return centre;
    }

    /// <summary>The lowest similarity that still counts as the gesture: 0.10 below its 2nd-percentile example (kept within 0.50…0.95).</summary>
    private static float FloorOf(IEnumerable<float> similarities) =>
        Math.Clamp(Percentile(similarities.OrderBy(s => s).ToArray(), 0.02) - 0.10f, 0.5f, 0.95f);

    private static void Normalize(float[] v)
    {
        double n = 0;
        foreach (float a in v) n += a * a;
        n = Math.Sqrt(n) + 1e-9;
        for (int i = 0; i < v.Length; i++) v[i] = (float)(v[i] / n);
    }

    private static float Dot(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        double s = 0;
        for (int i = 0; i < a.Length; i++) s += a[i] * b[i];
        return (float)s;
    }

    private static float Percentile(float[] sorted, double q) =>
        sorted.Length == 0 ? 0 : sorted[(int)Math.Round((sorted.Length - 1) * q)];

    private static float Evaluate(List<float[]> rows, List<int> labels, List<int> subset, float[] x, OrtValue xValue, float[] probs,
        InferenceSession eval, OrtIoBinding binding, RunOptions run, int classCount, int[][]? confusion)
    {
        if (subset.Count == 0) return 0;
        int hits = 0;
        for (int start = 0; start < subset.Count; start += Batch)
        {
            var chunk = subset.GetRange(start, Math.Min(Batch, subset.Count - start));
            Array.Clear(x);
            for (int r = 0; r < chunk.Count; r++) rows[chunk[r]].CopyTo(x, r * X);
            binding.BindInput("x", xValue); // the CPU rows reach the GPU when bound
            eval.RunWithBinding(run, binding);
            for (int r = 0; r < chunk.Count; r++)
            {
                int best = 0;
                for (int c = 1; c < classCount; c++)
                    if (probs[r * K + c] > probs[r * K + best]) best = c;
                int truth = labels[chunk[r]];
                if (best == truth) hits++;
                if (confusion is not null) confusion[truth][best]++;
            }
        }
        return hits / (float)subset.Count;
    }

    private static void FillRows(List<float[]> rows, List<int> labels, List<int> batchRows, float[] x, float[] y, float[] wn, float[] valid, double[] classWeight, int classCount)
    {
        Array.Clear(x);
        Array.Clear(y);
        Array.Clear(wn);
        Array.Clear(valid);
        double sum = 0;
        int count = Math.Min(batchRows.Count, Batch);
        for (int r = 0; r < count; r++)
        {
            int i = batchRows[r];
            rows[i].CopyTo(x, r * X);
            int label = Math.Clamp(labels[i], 0, classCount - 1);
            y[r * K + label] = 1f;
            valid[r] = 1f;
            wn[r] = (float)classWeight[label];
            sum += classWeight[label];
        }
        if (sum > 0)
        {
            for (int r = 0; r < count; r++) wn[r] = (float)(wn[r] / sum);
        }
    }

    private static (List<int> Train, List<int> Validation) Split(List<int> labels, int classCount, float fraction, Random rng)
    {
        var train = new List<int>();
        var validation = new List<int>();
        for (int c = 0; c < classCount; c++)
        {
            var rows = Enumerable.Range(0, labels.Count).Where(i => labels[i] == c).OrderBy(_ => rng.Next()).ToList();
            int nVal = rows.Count >= 8 ? Math.Max(1, (int)Math.Round(rows.Count * fraction)) : 0;
            validation.AddRange(rows.Take(nVal));
            train.AddRange(rows.Skip(nVal));
        }
        return (train, validation);
    }
}
