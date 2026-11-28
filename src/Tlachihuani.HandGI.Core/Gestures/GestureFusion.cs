// HandGI engine (Angela López Mendoza · XAIHT), part of Tlachihuani. tlachihuani:trace-off (runs on every camera frame and in tight GPU and training loops; its own messages reach Tlachihuani.log through the HandGI log bridge)
using HandGI.Core.Vision;

namespace HandGI.Core.Gestures;

/// <summary>
/// Turns the GPU's answers for one frame into one probability per gesture of the profile, the way
/// MediaPipe combines a custom and the canned classifier: your own gestures are asked first, and
/// whatever they leave goes to Google's ready-made gestures. A switched-off (or replaced) gesture
/// always gets 0. Your gesture also has to look like its recorded examples (cosine similarity to
/// their centre), so a hand shape HandGI never saw cannot be taken for one of yours. A gesture
/// learned for both hands is also compared with the mirror image of its examples (the other hand);
/// the hand it resembles more counts.
/// Pure CPU bookkeeping: about 2,000 multiplications per frame.
/// </summary>
public sealed class GestureFusion
{
    /// <summary>Above its floor, a gesture's similarity needs this much more to count fully.</summary>
    public const float GateRamp = 0.05f;

    private readonly float[] _white = new float[HandGraphs.EmbeddingSize];

    /// <summary>The share of this frame claimed by your own gestures (0 = all left to the ready-made ones).</summary>
    public float OwnShare { get; private set; }

    /// <summary>Cosine similarity of this frame to the centre of each of your gestures (by your-gesture position), for the hand it resembles more.</summary>
    public float[] Similarity { get; } = new float[HandGraphs.MaxOwnGestures];

    public void Fuse(GestureProfile profile, MediaPipeGestureModel model, ReadOnlySpan<float> ready, ReadOnlySpan<float> own, ReadOnlySpan<float> embedding, Span<float> fused)
    {
        fused.Clear();
        Array.Clear(Similarity);
        var classes = profile.Classes;
        float ownShare = 0;

        if (profile.IsTrained)
        {
            var w = profile.Weights!;
            model.Whiten(embedding, _white);
            double norm = 0;
            foreach (float v in _white) norm += v * v;
            norm = Math.Sqrt(norm) + 1e-9;

            var mine = profile.MineIndices;
            for (int j = 0; j < mine.Length && j < HandGraphs.MaxOwnGestures; j++)
            {
                int offset = j * HandGraphs.EmbeddingSize;
                float similarity = Cosine(w.Centroids, offset, norm);
                float margin = similarity - w.Floors[j];
                if (w.BothHands(j))
                {
                    float other = Cosine(w.MirrorCentroids, offset, norm);
                    if (other - w.MirrorFloors[j] > margin)
                    {
                        similarity = other;
                        margin = other - w.MirrorFloors[j];
                    }
                }
                Similarity[j] = similarity;
                if (!classes[mine[j]].Enabled) continue;
                float gate = Math.Clamp(margin / GateRamp, 0f, 1f);
                float p = own[j + 1] * gate;
                fused[mine[j]] = p;
                ownShare += p;
            }
        }

        float rest = Math.Max(0f, 1f - ownShare);
        for (int i = 0; i < classes.Count && i < fused.Length; i++)
        {
            var c = classes[i];
            if (!c.IsReadyMade || !c.Enabled || c.Ready is not { } r) continue;
            fused[i] = rest * ready[r.Label];
        }
        OwnShare = ownShare;
    }

    /// <summary>Cosine similarity of the whitened frame to one unit-length centre.</summary>
    private float Cosine(float[] centres, int offset, double norm)
    {
        double dot = 0;
        for (int k = 0; k < HandGraphs.EmbeddingSize; k++) dot += _white[k] * centres[offset + k];
        return (float)(dot / norm);
    }
}
