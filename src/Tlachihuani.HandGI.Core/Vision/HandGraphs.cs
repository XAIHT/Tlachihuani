// HandGI engine (Angela López Mendoza · XAIHT), part of Tlachihuani. tlachihuani:trace-off (runs on every camera frame and in tight GPU and training loops; its own messages reach Tlachihuani.log through the HandGI log bridge)
using HandGI.Core.Gestures;
using HandGI.Core.Onnx;

namespace HandGI.Core.Vision;

/// <summary>
/// Builds the three GPU graphs HandGI runs. Each one is a single ONNX model, so each stage is one
/// DirectML dispatch with data resident on the GPU from the raw camera bytes to the final numbers:
/// <list type="bullet">
/// <item><b>Palm</b>: raw frame → letterbox 192² (GPU) → MediaPipe palm detector → sigmoid, top-4,
///   anchor decoding and un-letterboxing (GPU) → 4 candidates × 19 floats.</item>
/// <item><b>Hand</b>: raw frame → rotated 224² crop (GPU) → MediaPipe hand landmarks → landmarks mapped
///   back to image pixels → Google's gesture embedder (128 numbers), the ready-made gesture classifier
///   and the head of your own gestures, all on the GPU in the same run.</item>
/// <item><b>Preview</b>: raw frame → mirrored BGRA image for the window (GPU).</item>
/// </list>
/// </summary>
public static class HandGraphs
{
    public const int PalmSize = 192;
    public const int HandSize = 224;
    public const int Candidates = 4;
    public const int CandidateStride = 19;
    public const int LandmarkCount = 21;
    /// <summary>One recorded sample: the 21 landmarks in MediaPipe's object-normalised form (x, y, z).</summary>
    public const int GestureInputCount = 63;
    public const int EmbeddingSize = 128;
    /// <summary>Ready-made classifier outputs: None + 7 gestures.</summary>
    public const int ReadyCount = 8;
    /// <summary>Own-gesture head outputs: "not one of mine" + up to 7 gestures you taught HandGI.</summary>
    public const int MaxClasses = 8;
    public const int MaxOwnGestures = MaxClasses - 1;
    /// <summary>All gestures of a profile: the 7 ready-made ones plus yours.</summary>
    public const int MaxGestures = 7 + MaxOwnGestures;
    public const int Hidden1 = 64;
    public const int Hidden2 = 32;

    // I/O names shared by the graphs and the tracker
    public const string Frame = "frame";
    public const string PalmAffine = "palm_affine";
    public const string PalmOut = "palm_candidates";
    public const string RoiAffine = "roi_affine";
    public const string RoiScale = "roi_scale";
    public const string ClfMean = "clf_mean";
    public const string ClfInvStd = "clf_invstd";
    public const string ClfW1 = "clf_w1";
    public const string ClfB1 = "clf_b1";
    public const string ClfW2 = "clf_w2";
    public const string ClfB2 = "clf_b2";
    public const string ClfW3 = "clf_w3";
    public const string ClfB3 = "clf_b3";
    public const string ClfMask = "clf_mask";
    public const string OutLandmarks = "hand_landmarks";
    public const string OutPresence = "hand_presence";
    public const string OutHandedness = "hand_handedness";
    public const string OutGestureInput = "hand_gesture_input";
    public const string OutEmbedding = "hand_embedding";
    public const string OutReady = "hand_ready";
    public const string OutProbs = "hand_probs";
    public const string PreviewAffine = "preview_affine";
    public const string PreviewOut = "preview_bgra";

    public static byte[] BuildPalm(byte[] palmModel, FrameLayout layout, YuvRange range)
    {
        var info = OnnxModelInfo.Parse(palmModel);
        var input = ImageInput(info, PalmSize);
        string regressors = info.Outputs.FirstOrDefault(o => o.Dims.Length > 0 && o.Dims[^1] == 18)?.Name
            ?? throw new InvalidDataException("Palm model: no [.,2016,18] regressor output.");
        string scores = info.Outputs.FirstOrDefault(o => o.Dims.Length > 0 && o.Dims[^1] == 1)?.Name
            ?? throw new InvalidDataException("Palm model: no [.,2016,1] score output.");

        var g = new OnnxGraph("hgi_");
        var frame = g.Input(Frame, OnnxType.UInt8, layout.ByteLength);
        var affine = g.Input(PalmAffine, OnnxType.Float, 2, 3);

        var rgb = SamplerGraph.SampleRgb(g, frame, layout, affine, PalmSize, PalmSize, bilinear: true, range);
        EmitModelInput(g, rgb, input, PalmSize);
        g.MarkModelPosition();

        var reg = g.Reshape(regressors, PalmAnchors.Count, 18);
        var prob = g.Sigmoid(g.Reshape(scores, PalmAnchors.Count));
        var (values, indices64) = g.TopK(prob, Candidates, 0);
        var indices = g.Cast(indices64, OnnxType.Int32);
        var selected = g.Gather(reg, indices, 0); // [4,18]
        var anchors = g.Gather(g.Const(PalmAnchors.Centers192(), PalmAnchors.Count, 2), indices, 0); // [4,2]

        var center = g.Add(g.Slice(selected, 0, 2, 1), anchors);
        var half = g.Mul(g.Slice(selected, 2, 4, 1), g.Scalar(0.5f));
        var p1 = g.Sub(center, half);
        var p2 = g.Add(center, half);
        var keypoints = g.Add(g.Reshape(g.Slice(selected, 4, 18, 1), Candidates, 7, 2), g.Reshape(anchors, Candidates, 1, 2));
        var points = g.Concat(1, g.Reshape(p1, Candidates, 1, 2), g.Reshape(p2, Candidates, 1, 2), keypoints); // [4,9,2]

        // map the 36 points from 192-px letterbox space to image pixels with the same affine (on the GPU)
        var flat = g.Reshape(points, Candidates * 9, 2);
        var homogeneous = g.Concat(1, flat, g.Ones(Candidates * 9, Candidates * 9, 1));
        var image = g.MatMul(homogeneous, g.Transpose(g.Reshape(affine, 2, 3), 1, 0));
        var result = g.Concat(1, g.Reshape(values, Candidates, 1), g.Reshape(image, Candidates, 18));
        g.IdentityTo(result, PalmOut);
        g.Output(PalmOut, OnnxType.Float, Candidates, CandidateStride);

        return OnnxModelSurgeon.Compose(palmModel, g, input.Name);
    }

    public static byte[] BuildHand(byte[] handModel, FrameLayout layout, YuvRange range, MediaPipeGestureModel gestures)
    {
        var info = OnnxModelInfo.Parse(handModel);
        var input = ImageInput(info, HandSize);
        var vectors = info.Outputs.Where(o => Elements(o) == 63).ToList();
        var scalars = info.Outputs.Where(o => Elements(o) == 1).ToList();
        if (vectors.Count < 1 || scalars.Count < 2)
            throw new InvalidDataException("Hand model: expected landmark [1,63] and two [1,1] outputs.");
        string landmarksOut = vectors[0].Name;
        string presenceOut = scalars[0].Name;
        string handednessOut = scalars[1].Name;

        var g = new OnnxGraph("hgi_");
        var frame = g.Input(Frame, OnnxType.UInt8, layout.ByteLength);
        var affine = g.Input(RoiAffine, OnnxType.Float, 2, 3);
        var scale = g.Input(RoiScale, OnnxType.Float, 1);
        var mean = g.Input(ClfMean, OnnxType.Float, 1, EmbeddingSize);
        var invStd = g.Input(ClfInvStd, OnnxType.Float, 1, EmbeddingSize);
        var w1 = g.Input(ClfW1, OnnxType.Float, EmbeddingSize, Hidden1);
        var b1 = g.Input(ClfB1, OnnxType.Float, 1, Hidden1);
        var w2 = g.Input(ClfW2, OnnxType.Float, Hidden1, Hidden2);
        var b2 = g.Input(ClfB2, OnnxType.Float, 1, Hidden2);
        var w3 = g.Input(ClfW3, OnnxType.Float, Hidden2, MaxClasses);
        var b3 = g.Input(ClfB3, OnnxType.Float, 1, MaxClasses);
        var mask = g.Input(ClfMask, OnnxType.Float, 1, MaxClasses);

        var rgb = SamplerGraph.SampleRgb(g, frame, layout, affine, HandSize, HandSize, bilinear: true, range);
        EmitModelInput(g, rgb, input, HandSize);
        g.MarkModelPosition();

        // landmarks: crop pixels → image pixels (x, y through the affine; z scaled by the crop scale)
        var lm = g.Reshape(landmarksOut, LandmarkCount, 3);
        var xy = g.Slice(lm, 0, 2, 1);
        var z = g.Slice(lm, 2, 3, 1);
        var homogeneous = g.Concat(1, xy, g.Ones(LandmarkCount, LandmarkCount, 1));
        var imageXy = g.MatMul(homogeneous, g.Transpose(g.Reshape(affine, 2, 3), 1, 0));
        var landmarks = g.Concat(1, imageXy, g.Mul(z, scale));
        g.IdentityTo(landmarks, OutLandmarks);
        g.Output(OutLandmarks, OnnxType.Float, LandmarkCount, 3);

        g.ReshapeTo(presenceOut, OutPresence, 1, 1);
        g.Output(OutPresence, OnnxType.Float, 1, 1);
        g.ReshapeTo(handednessOut, OutHandedness, 1, 1);
        g.Output(OutHandedness, OnnxType.Float, 1, 1);

        // Google's gesture networks in the same GPU run: MediaPipe's normalisation, the embedder
        // (21 landmarks -> 128 numbers) and the ready-made classifier (None + 7 gestures).
        var gestureInput = GestureGraphs.EmitNormalize(g, g.Reshape(landmarks, 1, LandmarkCount, 3), layout.Width, layout.Height);
        var embedding = GestureGraphs.EmitEmbedder(g, gestureInput, 1, gestures);
        g.IdentityTo(g.Reshape(gestureInput, 1, GestureInputCount), OutGestureInput);
        g.Output(OutGestureInput, OnnxType.Float, 1, GestureInputCount);
        g.IdentityTo(embedding, OutEmbedding);
        g.Output(OutEmbedding, OnnxType.Float, 1, EmbeddingSize);
        g.IdentityTo(GestureGraphs.EmitReady(g, embedding, gestures), OutReady);
        g.Output(OutReady, OnnxType.Float, 1, ReadyCount);

        // the head of your own gestures (weights are inputs: learning new gestures never rebuilds this graph)
        g.IdentityTo(GestureGraphs.EmitOwnHead(g, embedding, mean, invStd, w1, b1, w2, b2, w3, b3, mask), OutProbs);
        g.Output(OutProbs, OnnxType.Float, 1, MaxClasses);

        return OnnxModelSurgeon.Compose(handModel, g, input.Name);
    }

    public static byte[] BuildPreview(FrameLayout layout, int previewWidth, int previewHeight, YuvRange range)
    {
        var g = new OnnxGraph("hgi_");
        var frame = g.Input(Frame, OnnxType.UInt8, layout.ByteLength);
        var affine = g.Input(PreviewAffine, OnnxType.Float, 2, 3);
        var image = SamplerGraph.SampleBgraBytes(g, frame, layout, affine, previewWidth, previewHeight, range);
        g.IdentityTo(image, PreviewOut);
        g.Output(PreviewOut, OnnxType.UInt8, previewHeight, previewWidth, 4);
        return g.ToModel("handgi_preview");
    }

    /// <summary>A standalone sampler graph (used by the self-test to compare GPU sampling with a CPU reference).</summary>
    public static byte[] BuildSamplerProbe(FrameLayout layout, int outW, int outH, bool bilinear, YuvRange range)
    {
        var g = new OnnxGraph("hgi_");
        var frame = g.Input(Frame, OnnxType.UInt8, layout.ByteLength);
        var affine = g.Input(RoiAffine, OnnxType.Float, 2, 3);
        var rgb = SamplerGraph.SampleRgb(g, frame, layout, affine, outW, outH, bilinear, range);
        g.IdentityTo(rgb, "rgb");
        g.Output("rgb", OnnxType.Float, outW * outH, 3);
        return g.ToModel("handgi_sampler_probe");
    }

    private static long Elements(OnnxValueInfo v)
    {
        long n = 1;
        foreach (long d in v.Dims) n *= Math.Max(1, d);
        return v.Dims.Length == 0 ? 0 : n;
    }

    private static OnnxValueInfo ImageInput(OnnxModelInfo info, int size)
    {
        var input = info.Inputs.FirstOrDefault(i => i.Dims.Length == 4)
            ?? throw new InvalidDataException("The model has no 4-D image input.");
        bool nhwc = input.Dims[3] == 3 && input.Dims[1] == size;
        bool nchw = input.Dims[1] == 3 && input.Dims[2] == size;
        if (!nhwc && !nchw)
            throw new InvalidDataException($"Unexpected image input shape {input}; expected [1,{size},{size},3] or [1,3,{size},{size}].");
        return input;
    }

    private static void EmitModelInput(OnnxGraph g, string rgbP3, OnnxValueInfo input, int size)
    {
        bool nhwc = input.Dims[3] == 3;
        if (nhwc)
        {
            g.ReshapeTo(rgbP3, input.Name, 1, size, size, 3);
        }
        else
        {
            var nhwcTensor = g.Reshape(rgbP3, 1, size, size, 3);
            g.NodeTo("Transpose", new[] { nhwcTensor }, new[] { input.Name }, OnnxAttr.Ints("perm", 0, 3, 1, 2));
        }
    }
}
