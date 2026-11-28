// HandGI engine (Angela López Mendoza · XAIHT), part of Tlachihuani. tlachihuani:trace-off (runs on every camera frame and in tight GPU and training loops; its own messages reach Tlachihuani.log through the HandGI log bridge)
using HandGI.Core.Gestures;
using HandGI.Core.Onnx;
using HandGI.Core.Vision;

namespace HandGI.Core.Training;

/// <summary>
/// Your own gestures are learned on the GPU too, on top of Google's gesture embedder (transfer
/// learning): one training step (landmark augmentation, the frozen embedder, the head's forward pass,
/// softmax cross-entropy with label smoothing, hand-derived backpropagation and AdamW) is one ONNX
/// graph executed by DirectML. The CPU only feeds batches and swaps two parameter buffers.
/// Head: 128 → 64 (ReLU) → 32 (ReLU) → 8 (softmax over "not one of yours" + your gestures).
/// </summary>
public static class TrainingGraphs
{
    public const int X = HandGraphs.GestureInputCount;
    public const int E = HandGraphs.EmbeddingSize;
    public const int H1 = HandGraphs.Hidden1;
    public const int H2 = HandGraphs.Hidden2;
    public const int K = HandGraphs.MaxClasses;

    public static readonly string[] ParamNames = { "w1", "b1", "w2", "b2", "w3", "b3" };

    public static long[] ParamShape(string name) => name switch
    {
        "w1" => new long[] { E, H1 },
        "b1" => new long[] { 1, H1 },
        "w2" => new long[] { H1, H2 },
        "b2" => new long[] { 1, H2 },
        "w3" => new long[] { H2, K },
        "b3" => new long[] { 1, K },
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    public static int ParamSize(string name)
    {
        var s = ParamShape(name);
        return (int)(s[0] * s[1]);
    }

    /// <summary>One AdamW training step over a fixed batch of <paramref name="batch"/> rows (padding rows have weight 0).</summary>
    public static byte[] BuildTrainStep(int batch, MediaPipeGestureModel model)
    {
        long b = batch;
        var g = new OnnxGraph("t_");
        var x = g.Input("x", OnnxType.Float, b, X);
        var y = g.Input("y", OnnxType.Float, b, K);
        var wn = g.Input("wn", OnnxType.Float, b, 1);
        var valid = g.Input("valid", OnnxType.Float, b, 1);
        var augCos = g.Input("aug_cos", OnnxType.Float, b, 1);
        var augSin = g.Input("aug_sin", OnnxType.Float, b, 1);
        var augFlip = g.Input("aug_flip", OnnxType.Float, b, 1);
        var augNoise = g.Input("aug_noise", OnnxType.Float, b, X);
        var mean = g.Input("mean", OnnxType.Float, 1, E);
        var invStd = g.Input("invstd", OnnxType.Float, 1, E);
        var mask = g.Input("mask", OnnxType.Float, 1, K);
        var smooth = g.Input("smooth", OnnxType.Float, 1, K);
        var keep = g.Input("keep", OnnxType.Float, 1);
        var lrT = g.Input("lr_t", OnnxType.Float, 1);
        var lrWd = g.Input("lr_wd", OnnxType.Float, 1);

        var p = new Dictionary<string, string>();
        var m = new Dictionary<string, string>();
        var v = new Dictionary<string, string>();
        foreach (var name in ParamNames)
        {
            p[name] = g.Input(name, OnnxType.Float, ParamShape(name));
            m[name] = g.Input("m_" + name, OnnxType.Float, ParamShape(name));
            v[name] = g.Input("v_" + name, OnnxType.Float, ParamShape(name));
        }

        // --- augmentation: landmark jitter, the other hand (aug_flip = -1 mirrors x about the wrist),
        //     then a small turn of the whole hand about the wrist
        var landmarks = g.Reshape(g.Add(x, augNoise), b, HandGraphs.LandmarkCount, 3);
        var px = g.Mul(g.Slice(landmarks, 0, 1, 2), g.Reshape(augFlip, b, 1, 1));
        var py = g.Slice(landmarks, 1, 2, 2);
        var pz = g.Slice(landmarks, 2, 3, 2);
        var c3 = g.Reshape(augCos, b, 1, 1);
        var s3 = g.Reshape(augSin, b, 1, 1);
        var nx = g.Sub(g.Mul(c3, px), g.Mul(s3, py));
        var ny = g.Add(g.Mul(s3, px), g.Mul(c3, py));
        var turned = g.Concat(2, nx, ny, pz);

        // --- Google's embedder (frozen: its weights are constants), then the head
        var embedding = GestureGraphs.EmitEmbedder(g, turned, batch, model);
        var xn = g.Mul(g.Sub(embedding, mean), invStd);
        var z1 = g.Add(g.MatMul(xn, p["w1"]), p["b1"]);
        var a1 = g.Relu(z1);
        var z2 = g.Add(g.MatMul(a1, p["w2"]), p["b2"]);
        var a2 = g.Relu(z2);
        var z3 = g.Add(g.Add(g.MatMul(a2, p["w3"]), p["b3"]), mask);
        var prob = g.Softmax(z3, 1);

        // --- loss (weighted mean cross-entropy with label smoothing) and accuracy
        var targets = g.Add(g.Mul(y, keep), smooth);
        var onesK = g.Ones(K, K, 1);
        var logp = g.Log(g.Add(prob, g.Scalar(1e-9f)));
        var rowLoss = g.MatMul(g.Mul(targets, logp), onesK); // [B,1], negative
        var loss = g.Mul(g.MatMul(g.Transpose(wn, 1, 0), rowLoss), g.Scalar(-1f)); // [1,1]
        var pTrue = g.MatMul(g.Mul(y, prob), onesK);
        var pMax = g.ReduceMax(prob, 1, keepDims: true);
        var hit = g.Cast(g.Not(g.Less(pTrue, pMax)), OnnxType.Float);
        var correct = g.MatMul(g.Transpose(valid, 1, 0), hit); // [1,1]

        // --- backward (the embedder is frozen: gradients stop at its output)
        var onesRow = g.Ones(batch, 1, b);
        var dz3 = g.Mul(g.Sub(prob, targets), wn);
        var gw3 = g.MatMul(g.Transpose(a2, 1, 0), dz3);
        var gb3 = g.MatMul(onesRow, dz3);
        var da2 = g.MatMul(dz3, g.Transpose(p["w3"], 1, 0));
        var dz2 = g.Mul(da2, g.Cast(g.Greater(z2, g.Scalar(0f)), OnnxType.Float));
        var gw2 = g.MatMul(g.Transpose(a1, 1, 0), dz2);
        var gb2 = g.MatMul(onesRow, dz2);
        var da1 = g.MatMul(dz2, g.Transpose(p["w2"], 1, 0));
        var dz1 = g.Mul(da1, g.Cast(g.Greater(z1, g.Scalar(0f)), OnnxType.Float));
        var gw1 = g.MatMul(g.Transpose(xn, 1, 0), dz1);
        var gb1 = g.MatMul(onesRow, dz1);

        var grads = new Dictionary<string, string> { ["w1"] = gw1, ["b1"] = gb1, ["w2"] = gw2, ["b2"] = gb2, ["w3"] = gw3, ["b3"] = gb3 };

        // --- AdamW (β1 0.9, β2 0.999); lr_t carries the bias corrections, lr_wd = lr × weight decay
        foreach (var name in ParamNames)
        {
            var grad = grads[name];
            var m2 = g.Add(g.Mul(m[name], g.Scalar(0.9f)), g.Mul(grad, g.Scalar(0.1f)));
            var v2 = g.Add(g.Mul(v[name], g.Scalar(0.999f)), g.Mul(g.Mul(grad, grad), g.Scalar(0.001f)));
            var update = g.Div(m2, g.Add(g.Sqrt(v2), g.Scalar(1e-8f)));
            var p2 = g.Sub(p[name], g.Mul(update, lrT));
            if (name.StartsWith('w')) p2 = g.Sub(p2, g.Mul(p[name], lrWd));
            Out(g, p2, name + "_out", ParamShape(name));
            Out(g, m2, "m_" + name + "_out", ParamShape(name));
            Out(g, v2, "v_" + name + "_out", ParamShape(name));
        }
        Out(g, loss, "loss", 1, 1);
        Out(g, correct, "correct", 1, 1);
        return g.ToModel("handgi_train_step");
    }

    /// <summary>Forward pass for evaluation: probabilities for a batch of landmark rows (no augmentation).</summary>
    public static byte[] BuildEval(int batch, MediaPipeGestureModel model)
    {
        long b = batch;
        var g = new OnnxGraph("e_");
        var x = g.Input("x", OnnxType.Float, b, X);
        var mean = g.Input("mean", OnnxType.Float, 1, E);
        var invStd = g.Input("invstd", OnnxType.Float, 1, E);
        var mask = g.Input("mask", OnnxType.Float, 1, K);
        var p = ParamNames.ToDictionary(n => n, n => g.Input(n, OnnxType.Float, ParamShape(n)));
        var embedding = GestureGraphs.EmitEmbedder(g, g.Reshape(x, b, HandGraphs.LandmarkCount, 3), batch, model);
        var probs = GestureGraphs.EmitOwnHead(g, embedding, mean, invStd, p["w1"], p["b1"], p["w2"], p["b2"], p["w3"], p["b3"], mask);
        Out(g, probs, "probs", b, K);
        return g.ToModel("handgi_eval");
    }

    private static void Out(OnnxGraph g, string tensor, string name, params long[] shape)
    {
        g.IdentityTo(tensor, name);
        g.Output(name, OnnxType.Float, shape);
    }
}
