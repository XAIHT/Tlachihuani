// HandGI engine (Angela López Mendoza · XAIHT), part of Tlachihuani. tlachihuani:trace-off (runs on every camera frame and in tight GPU and training loops; its own messages reach Tlachihuani.log through the HandGI log bridge)
using System.IO.Compression;
using System.Text;
using HandGI.Core.Onnx;

namespace HandGI.Core.Gestures;

/// <summary>One of the seven gestures Google's MediaPipe model recognises out of the box.</summary>
public sealed record ReadyGesture(int Label, string Id, string Name, string Emoji, GestureAction DefaultAction, string Hint);

/// <summary>The ready-made gestures in the order HandGI lists them, with their default actions.</summary>
public static class ReadyGestures
{
    public static readonly IReadOnlyList<ReadyGesture> All = new[]
    {
        new ReadyGesture(5, "Thumb_Up", "Thumb up", "\U0001F44D", GestureAction.VolumeUp, "Make a fist and point your thumb up."),
        new ReadyGesture(4, "Thumb_Down", "Thumb down", "\U0001F44E", GestureAction.VolumeDown, "Make a fist and point your thumb down."),
        new ReadyGesture(1, "Closed_Fist", "Closed fist", "✊", GestureAction.MuteToggle, "Close all your fingers into a fist, facing the camera."),
        new ReadyGesture(2, "Open_Palm", "Open palm", "✋", GestureAction.None, "Open all five fingers, palm facing the camera."),
        new ReadyGesture(3, "Pointing_Up", "Pointing up", "☝", GestureAction.None, "Only your index finger up, the others closed."),
        new ReadyGesture(6, "Victory", "Victory", "✌", GestureAction.None, "Index and middle fingers up in a V."),
        new ReadyGesture(7, "ILoveYou", "I love you", "\U0001F91F", GestureAction.None, "Thumb, index and little finger out; middle and ring fingers closed."),
    };

    public static ReadyGesture? Find(string? id) => All.FirstOrDefault(g => g.Id == id);
}

/// <summary>
/// Google's MediaPipe gesture recognizer (Apache-2.0), read directly from the official
/// gesture_recognizer.task bundle: the gesture embedder (21 hand landmarks → 128 numbers) and the
/// canned classifier (128 numbers → None + 7 gestures). HandGI rebuilds both networks as ONNX for
/// DirectML (<see cref="Vision.GestureGraphs"/>); this class also holds a plain CPU reference of the
/// same maths for the self-test. Verified against TensorFlow Lite to 1e-6 (see the self-test vectors).
/// </summary>
public sealed class MediaPipeGestureModel
{
    public const int Landmarks = 21;
    public const int InputSize = Landmarks * 3;
    public const int EmbeddingSize = 128;
    public const int ClassCount = 8;
    public const int Blocks = 6;

    /// <summary>Label order of the canned classifier (index 0 is "no gesture").</summary>
    public static readonly string[] Labels = { "None", "Closed_Fist", "Open_Palm", "Pointing_Up", "Thumb_Down", "Thumb_Up", "Victory", "ILoveYou" };

    private MediaPipeGestureModel() { }

    /// <summary>Landmarks averaged for the embedder's centre: wrist, thumb base and the four finger bases.</summary>
    public int[] CentreIndices { get; private init; } = Array.Empty<int>();
    public float Epsilon { get; private init; }
    // Fully connected weights keep TensorFlow Lite's layout: [out, in], row-major.
    public float[] SkipW { get; private init; } = Array.Empty<float>();
    public float[] SkipB { get; private init; } = Array.Empty<float>();
    public float[] InW { get; private init; } = Array.Empty<float>();
    public float[] InB { get; private init; } = Array.Empty<float>();
    public float[][] BlockW { get; private init; } = Array.Empty<float[]>();
    public float[][] BlockB { get; private init; } = Array.Empty<float[]>();
    public float[][] BnMul { get; private init; } = Array.Empty<float[]>();
    public float[][] BnAdd { get; private init; } = Array.Empty<float[]>();
    public float[] OutW { get; private init; } = Array.Empty<float>();
    public float[] OutB { get; private init; } = Array.Empty<float>();
    public float[] CannedMul { get; private init; } = Array.Empty<float>();
    public float[] CannedAdd { get; private init; } = Array.Empty<float>();
    public float[] CannedW { get; private init; } = Array.Empty<float>();
    public float[] CannedB { get; private init; } = Array.Empty<float>();
    /// <summary>The labels found inside the bundle (labels.txt), or empty when the bundle carries none.</summary>
    public string[] BundleLabels { get; private init; } = Array.Empty<string>();
    public long EmbedderBytes { get; private init; }
    public long ClassifierBytes { get; private init; }

    // ------------------------------------------------------------------ loading

    /// <summary>Reads gesture_recognizer.task (a zip holding hand_gesture_recognizer.task, itself a zip with the two .tflite files).</summary>
    public static MediaPipeGestureModel FromTask(byte[] task)
    {
        byte[] inner = ReadEntry(task, "hand_gesture_recognizer.task");
        byte[] embedder = ReadEntry(inner, "gesture_embedder.tflite");
        byte[] classifier = ReadEntry(inner, "canned_gesture_classifier.tflite");
        return FromTfLite(embedder, classifier);
    }

    public static MediaPipeGestureModel FromTfLite(byte[] embedderFile, byte[] classifierFile)
    {
        var e = TfLiteModel.Parse(embedderFile);
        var c = TfLiteModel.Parse(classifierFile);

        var ops = e.Operators;
        var expected = new List<TfLiteOp>
        {
            TfLiteOp.Gather, TfLiteOp.Mean, TfLiteOp.Sub, TfLiteOp.Unpack, TfLiteOp.ReduceMax, TfLiteOp.ReduceMax,
            TfLiteOp.ReduceMin, TfLiteOp.Sub, TfLiteOp.ReduceMin, TfLiteOp.Sub, TfLiteOp.Maximum, TfLiteOp.Reshape,
            TfLiteOp.StridedSlice, TfLiteOp.Add, TfLiteOp.Div, TfLiteOp.Reshape, TfLiteOp.FullyConnected, TfLiteOp.FullyConnected,
        };
        for (int k = 0; k < Blocks; k++) expected.AddRange(new[] { TfLiteOp.FullyConnected, TfLiteOp.Add, TfLiteOp.Mul, TfLiteOp.Add });
        expected.Add(TfLiteOp.FullyConnected);
        if (ops.Count != expected.Count || ops.Where((op, i) => op.Op != expected[i]).Any())
            throw new InvalidDataException($"The gesture embedder's structure is not the one HandGI was written for ({ops.Count} operators: {string.Join(" ", ops.Select(o => o.Op))}).");

        float[] F(int op, int input, int elements)
        {
            var t = e.Tensors[ops[op].Inputs[input]];
            var data = t.Floats();
            if (data.Length != elements) throw new InvalidDataException($"Embedder tensor {t} has {data.Length} values, expected {elements}.");
            return data;
        }
        void Relu(int op, bool expectRelu)
        {
            bool relu = ops[op].FusedActivation == TfLiteOperator.ActivationRelu;
            if (relu != expectRelu) throw new InvalidDataException($"Embedder operator {op} ({ops[op].Op}) has an unexpected activation.");
        }

        var centre = e.Tensors[ops[0].Inputs[1]].Ints();
        if (centre.Length != 6 || centre.Any(i => i < 0 || i >= Landmarks)) throw new InvalidDataException("The embedder's centre landmarks are not 6 valid indices.");
        float eps = e.Tensors[ops[13].Inputs[1]].Floats()[0];
        Relu(16, false);
        Relu(17, true);
        var blockW = new float[Blocks][];
        var blockB = new float[Blocks][];
        var bnMul = new float[Blocks][];
        var bnAdd = new float[Blocks][];
        for (int k = 0; k < Blocks; k++)
        {
            int b = 18 + 4 * k;
            Relu(b, false);
            Relu(b + 1, false);
            Relu(b + 3, true);
            blockW[k] = F(b, 1, EmbeddingSize * EmbeddingSize);
            blockB[k] = F(b, 2, EmbeddingSize);
            bnMul[k] = F(b + 2, 1, EmbeddingSize);
            bnAdd[k] = F(b + 3, 1, EmbeddingSize);
        }
        Relu(42, true);

        var cops = c.Operators;
        if (cops.Count != 4 || cops[0].Op != TfLiteOp.Mul || cops[1].Op != TfLiteOp.Add || cops[2].Op != TfLiteOp.FullyConnected || cops[3].Op != TfLiteOp.Softmax
            || cops[1].FusedActivation != TfLiteOperator.ActivationRelu || cops[2].FusedActivation != TfLiteOperator.ActivationNone)
            throw new InvalidDataException("The canned gesture classifier's structure is not the one HandGI was written for.");
        float[] C(int op, int input, int elements)
        {
            var t = c.Tensors[cops[op].Inputs[input]];
            var data = t.Floats();
            if (data.Length != elements) throw new InvalidDataException($"Classifier tensor {t} has {data.Length} values, expected {elements}.");
            return data;
        }

        var model = new MediaPipeGestureModel
        {
            CentreIndices = centre,
            Epsilon = eps,
            SkipW = F(16, 1, EmbeddingSize * InputSize),
            SkipB = F(16, 2, EmbeddingSize),
            InW = F(17, 1, EmbeddingSize * InputSize),
            InB = F(17, 2, EmbeddingSize),
            BlockW = blockW,
            BlockB = blockB,
            BnMul = bnMul,
            BnAdd = bnAdd,
            OutW = F(42, 1, EmbeddingSize * EmbeddingSize),
            OutB = F(42, 2, EmbeddingSize),
            CannedMul = C(0, 1, EmbeddingSize),
            CannedAdd = C(1, 1, EmbeddingSize),
            CannedW = C(2, 1, ClassCount * EmbeddingSize),
            CannedB = C(2, 2, ClassCount),
            BundleLabels = TryReadLabels(classifierFile),
            EmbedderBytes = embedderFile.LongLength,
            ClassifierBytes = classifierFile.LongLength,
        };
        if (model.BundleLabels.Length > 0 && !model.BundleLabels.SequenceEqual(Labels))
            throw new InvalidDataException($"The classifier's labels ({string.Join(", ", model.BundleLabels)}) are not the ones HandGI expects.");
        return model;
    }

    private static byte[] ReadEntry(byte[] zip, string name)
    {
        using var archive = new ZipArchive(new MemoryStream(zip, writable: false), ZipArchiveMode.Read);
        var entry = archive.GetEntry(name) ?? throw new InvalidDataException($"The gesture bundle has no '{name}'.");
        using var stream = entry.Open();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>TFLite metadata appends a zip with labels.txt to the model file; read it when it is there.</summary>
    private static string[] TryReadLabels(byte[] tflite)
    {
        try
        {
            using var archive = new ZipArchive(new MemoryStream(tflite, writable: false), ZipArchiveMode.Read);
            var entry = archive.GetEntry("labels.txt");
            if (entry is null) return Array.Empty<string>();
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        catch (Exception)
        {
            return Array.Empty<string>(); // a model without readable metadata keeps the known label order
        }
    }

    /// <summary>A [rows, cols] row-major matrix transposed to [cols, rows] (TFLite [out,in] → ONNX MatMul [in,out]).</summary>
    public static float[] Transposed(float[] w, int rows, int cols)
    {
        var t = new float[w.Length];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                t[c * rows + r] = w[r * cols + c];
        return t;
    }

    // ------------------------------------------------------------------ CPU reference (self-test, migration)

    /// <summary>
    /// HandGI landmarks (x, y in image pixels; z in the same pixel units, as the tracker reports them)
    /// → MediaPipe's object-normalised embedder input, exactly as MediaPipe prepares it:
    /// normalised coordinates with z scaled by 1/0.4 (TensorsToLandmarks), the aspect-ratio correction
    /// of LandmarksToMatrix, then wrist at the origin and the larger x/y extent scaled to 1 (+1e-5).
    /// </summary>
    public static float[] Normalize(ReadOnlySpan<float> pixels, int width, int height)
    {
        double m = Math.Max(width, height);
        var p = new double[InputSize];
        for (int i = 0; i < Landmarks; i++)
        {
            p[i * 3] = (pixels[i * 3] / (double)width - 0.5) * width / m + 0.5;
            p[i * 3 + 1] = (pixels[i * 3 + 1] / (double)height - 0.5) * height / m + 0.5;
            p[i * 3 + 2] = pixels[i * 3 + 2] / (double)width / 0.4;
        }
        double wx = p[0], wy = p[1], wz = p[2];
        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        for (int i = 0; i < Landmarks; i++)
        {
            p[i * 3] -= wx;
            p[i * 3 + 1] -= wy;
            p[i * 3 + 2] -= wz;
            minX = Math.Min(minX, p[i * 3]);
            maxX = Math.Max(maxX, p[i * 3]);
            minY = Math.Min(minY, p[i * 3 + 1]);
            maxY = Math.Max(maxY, p[i * 3 + 1]);
        }
        double s = Math.Max(maxX - minX, maxY - minY) + 1e-5;
        var result = new float[InputSize];
        for (int i = 0; i < InputSize; i++) result[i] = (float)(p[i] / s);
        return result;
    }

    /// <summary>
    /// The same hand shape made with the other hand: MediaPipe's normalised landmarks with x negated
    /// about the wrist. A left hand is the mirror image of a right hand, so this is how a sign recorded
    /// with one hand looks when it is shown with the other (the wrist stays at the origin, the extent at 1).
    /// </summary>
    public static float[] Mirrored(ReadOnlySpan<float> normalized)
    {
        var mirrored = normalized.ToArray();
        for (int i = 0; i < mirrored.Length; i += 3) mirrored[i] = -mirrored[i];
        return mirrored;
    }

    /// <summary>The embedder on the CPU: 63 normalised landmark values → 128 numbers.</summary>
    public float[] Embed(ReadOnlySpan<float> normalized)
    {
        var x = new double[InputSize];
        double cx = 0, cy = 0, cz = 0;
        foreach (int i in CentreIndices)
        {
            cx += normalized[i * 3];
            cy += normalized[i * 3 + 1];
            cz += normalized[i * 3 + 2];
        }
        cx /= CentreIndices.Length;
        cy /= CentreIndices.Length;
        cz /= CentreIndices.Length;
        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        for (int i = 0; i < Landmarks; i++)
        {
            x[i * 3] = normalized[i * 3] - cx;
            x[i * 3 + 1] = normalized[i * 3 + 1] - cy;
            x[i * 3 + 2] = normalized[i * 3 + 2] - cz;
            minX = Math.Min(minX, x[i * 3]);
            maxX = Math.Max(maxX, x[i * 3]);
            minY = Math.Min(minY, x[i * 3 + 1]);
            maxY = Math.Max(maxY, x[i * 3 + 1]);
        }
        double s = Math.Max(maxX - minX, maxY - minY) + Epsilon;
        for (int i = 0; i < InputSize; i++) x[i] /= s;

        var h = Dense(x, SkipW, SkipB, InputSize, EmbeddingSize, relu: false);
        var a = Dense(x, InW, InB, InputSize, EmbeddingSize, relu: true);
        for (int k = 0; k < Blocks; k++)
        {
            var d = Dense(a, BlockW[k], BlockB[k], EmbeddingSize, EmbeddingSize, relu: false);
            for (int j = 0; j < EmbeddingSize; j++)
            {
                h[j] += d[j];
                a[j] = Math.Max(0, h[j] * BnMul[k][j] + BnAdd[k][j]);
            }
        }
        var e = Dense(a, OutW, OutB, EmbeddingSize, EmbeddingSize, relu: true);
        return e.Select(v => (float)v).ToArray();
    }

    /// <summary>The canned classifier on the CPU: 128 numbers → 8 probabilities (None + 7 gestures).</summary>
    public float[] Classify(ReadOnlySpan<float> embedding)
    {
        var z = new double[EmbeddingSize];
        for (int j = 0; j < EmbeddingSize; j++) z[j] = Math.Max(0, embedding[j] * CannedMul[j] + CannedAdd[j]);
        var logits = Dense(z, CannedW, CannedB, EmbeddingSize, ClassCount, relu: false);
        double max = logits.Max(), sum = 0;
        for (int c = 0; c < ClassCount; c++)
        {
            logits[c] = Math.Exp(logits[c] - max);
            sum += logits[c];
        }
        return logits.Select(v => (float)(v / sum)).ToArray();
    }

    /// <summary>The classifier's batch normalisation applied to an embedding: each of the 128 numbers on a comparable scale.</summary>
    public void Whiten(ReadOnlySpan<float> embedding, Span<float> destination)
    {
        for (int j = 0; j < EmbeddingSize; j++) destination[j] = embedding[j] * CannedMul[j] + CannedAdd[j];
    }

    private static double[] Dense(double[] x, float[] w, float[] b, int inputs, int outputs, bool relu)
    {
        var y = new double[outputs];
        for (int o = 0; o < outputs; o++)
        {
            double s = b[o];
            int row = o * inputs;
            for (int i = 0; i < inputs; i++) s += x[i] * w[row + i];
            y[o] = relu ? Math.Max(0, s) : s;
        }
        return y;
    }

    public string Describe() =>
        $"gesture embedder {EmbedderBytes:N0} bytes ({Blocks + 3} dense layers, {Blocks} residual blocks, centre landmarks {string.Join(",", CentreIndices)}), " +
        $"canned classifier {ClassifierBytes:N0} bytes, labels {(BundleLabels.Length > 0 ? "read from the bundle" : "built in")}: {string.Join(", ", Labels)}";
}
