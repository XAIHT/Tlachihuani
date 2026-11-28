// HandGI engine (Angela López Mendoza · XAIHT), part of Tlachihuani. tlachihuani:trace-off (runs on every camera frame and in tight GPU and training loops; its own messages reach Tlachihuani.log through the HandGI log bridge)
using HandGI.Core.Gestures;
using HandGI.Core.Onnx;

namespace HandGI.Core.Vision;

/// <summary>
/// Google's MediaPipe gesture networks emitted as ONNX nodes, so they run inside HandGI's own GPU
/// graphs: the hand graph (landmarks → gestures in the same DirectML dispatch), the training graphs
/// (your own gestures learn on top of the same 128 numbers) and the self-test probe.
/// Every node here is a plain opset-11..13 operator; the weights are graph constants.
/// </summary>
public static class GestureGraphs
{
    public const int E = MediaPipeGestureModel.EmbeddingSize;
    public const int Ready = MediaPipeGestureModel.ClassCount;
    public const int Input = MediaPipeGestureModel.InputSize;

    /// <summary>
    /// HandGI landmarks in pixels [n,21,3] → MediaPipe's object-normalised embedder input [n,21,3]:
    /// x and y in units of the longer image side, z divided by 0.4 × width (MediaPipe's z scale),
    /// wrist at the origin, larger x/y extent scaled to 1 (+1e-5). The aspect-ratio shift of
    /// LandmarksToMatrix is a constant per axis, so it cancels when the wrist is subtracted.
    /// </summary>
    public static string EmitNormalize(OnnxGraph g, string pixels, int width, int height)
    {
        float m = Math.Max(width, height);
        var scaled = g.Mul(pixels, g.Const(new[] { 1f / m, 1f / m, 1f / (0.4f * width) }, 1, 1, 3));
        var relative = g.Sub(scaled, g.Slice(scaled, 0, 1, 1));
        return g.Div(relative, g.Add(MaxExtent(g, relative), g.Scalar(1e-5f)));
    }

    /// <summary>The gesture embedder: object-normalised landmarks [n,21,3] → 128 numbers [n,128].</summary>
    public static string EmitEmbedder(OnnxGraph g, string normalized, int n, MediaPipeGestureModel m)
    {
        var indices = g.ConstI64(m.CentreIndices.Select(i => (long)i).ToArray(), m.CentreIndices.Length);
        var centre = g.ReduceMean(g.Gather(normalized, indices, 1), 1, keepDims: true); // [n,1,3]
        var centred = g.Sub(normalized, centre);
        var unit = g.Div(centred, g.Add(MaxExtent(g, centred), g.Scalar(m.Epsilon)));
        var v = g.Reshape(unit, n, Input);

        var h = Dense(g, v, m.SkipW, m.SkipB, Input, E);
        var a = g.Relu(Dense(g, v, m.InW, m.InB, Input, E));
        for (int k = 0; k < MediaPipeGestureModel.Blocks; k++)
        {
            h = g.Add(h, Dense(g, a, m.BlockW[k], m.BlockB[k], E, E));
            a = g.Relu(g.Add(g.Mul(h, g.Const(m.BnMul[k], 1, E)), g.Const(m.BnAdd[k], 1, E)));
        }
        return g.Relu(Dense(g, a, m.OutW, m.OutB, E, E));
    }

    /// <summary>The canned classifier: embedding [n,128] → probabilities [n,8] (None + 7 gestures).</summary>
    public static string EmitReady(OnnxGraph g, string embedding, MediaPipeGestureModel m)
    {
        var z = g.Relu(g.Add(g.Mul(embedding, g.Const(m.CannedMul, 1, E)), g.Const(m.CannedAdd, 1, E)));
        return g.Softmax(Dense(g, z, m.CannedW, m.CannedB, E, Ready), 1);
    }

    /// <summary>
    /// Your own gestures' head: embedding [n,128] → probabilities [n,8] (index 0 = "not one of mine").
    /// The weights are graph inputs, so learning new gestures never rebuilds the hand graph.
    /// </summary>
    public static string EmitOwnHead(OnnxGraph g, string embedding, string mean, string invStd, string w1, string b1, string w2, string b2, string w3, string b3, string mask)
    {
        var xn = g.Mul(g.Sub(embedding, mean), invStd);
        var h1 = g.Relu(g.Add(g.MatMul(xn, w1), b1));
        var h2 = g.Relu(g.Add(g.MatMul(h1, w2), b2));
        return g.Softmax(g.Add(g.Add(g.MatMul(h2, w3), b3), mask), 1);
    }

    /// <summary>Self-test probe: pixel landmarks [n,21,3] → normalised input, embedding and ready-made probabilities.</summary>
    public static byte[] BuildProbe(MediaPipeGestureModel m, int n, int width, int height)
    {
        var g = new OnnxGraph("gp_");
        var pixels = g.Input("landmarks_px", OnnxType.Float, n, MediaPipeGestureModel.Landmarks, 3);
        var normalized = EmitNormalize(g, pixels, width, height);
        var embedding = EmitEmbedder(g, normalized, n, m);
        Out(g, g.Reshape(normalized, n, Input), "normalized", n, Input);
        Out(g, embedding, "embedding", n, E);
        Out(g, EmitReady(g, embedding, m), "ready", n, Ready);
        return g.ToModel("handgi_gesture_probe");
    }

    /// <summary>Batch embedding of recorded samples (63 object-normalised values per row) → [b,128] and ready-made probabilities [b,8].</summary>
    public static byte[] BuildEmbed(MediaPipeGestureModel m, int batch)
    {
        var g = new OnnxGraph("ge_");
        var x = g.Input("x", OnnxType.Float, batch, Input);
        var embedding = EmitEmbedder(g, g.Reshape(x, batch, MediaPipeGestureModel.Landmarks, 3), batch, m);
        Out(g, embedding, "embedding", batch, E);
        Out(g, EmitReady(g, embedding, m), "ready", batch, Ready);
        return g.ToModel("handgi_gesture_embed");
    }

    private static string MaxExtent(OnnxGraph g, string points)
    {
        var x = g.Slice(points, 0, 1, 2);
        var y = g.Slice(points, 1, 2, 2);
        var rangeX = g.Sub(g.ReduceMax(x, 1, keepDims: true), g.ReduceMin(x, 1, keepDims: true));
        var rangeY = g.Sub(g.ReduceMax(y, 1, keepDims: true), g.ReduceMin(y, 1, keepDims: true));
        return g.Max(rangeX, rangeY); // [n,1,1]
    }

    /// <summary>x [n,in] · Wᵀ + b with TensorFlow Lite's [out,in] weights transposed into a constant.</summary>
    private static string Dense(OnnxGraph g, string x, float[] w, float[] b, int inputs, int outputs) =>
        g.Add(g.MatMul(x, g.Const(MediaPipeGestureModel.Transposed(w, outputs, inputs), inputs, outputs)), g.Const(b, 1, outputs));

    private static void Out(OnnxGraph g, string tensor, string name, params long[] shape)
    {
        g.IdentityTo(tensor, name);
        g.Output(name, OnnxType.Float, shape);
    }
}
