// HandGI engine (Angela López Mendoza · XAIHT), part of Tlachihuani. tlachihuani:trace-off (runs on every camera frame and in tight GPU and training loops; its own messages reach Tlachihuani.log through the HandGI log bridge)
using HandGI.Core.Gestures;
using HandGI.Core.Vision;

namespace HandGI.Core.Training;

/// <summary>
/// Hand shapes drawn from a small 3-D hand model (a palm, four three-joint fingers and a thumb).
/// Google's model recognises these drawings as the real gestures (open palm, fist, pointing up,
/// victory, I love you, thumb up), so they serve as counter-examples when HandGI learns your own
/// gestures: "this is NOT one of yours". That is how HandGI learns your gestures without ever asking
/// you to record an empty or resting hand. They also drive the self-test and the manual's figures.
/// </summary>
public static class SyntheticHands
{
    public enum Finger { Straight, Half, Curled }

    /// <summary>A hand shape: index, middle, ring and little finger, the thumb, the spread and the orientation (degrees).</summary>
    public sealed record Pose(string Name, Finger[] Fingers, float ThumbAbduction, float[] ThumbFlex, float[] Splay, float Roll, float Yaw);

    private static readonly float[][] Bases =
    {
        new[] { 0.22f, 0.18f, 0.05f },   // thumb base (CMC)
        new[] { 0.20f, 0.95f, 0f },      // index MCP
        new[] { 0f, 1f, 0f },            // middle MCP
        new[] { -0.18f, 0.93f, 0f },     // ring MCP
        new[] { -0.34f, 0.82f, 0f },     // little MCP
    };

    private static readonly float[][] Lengths =
    {
        new[] { 0.33f, 0.27f, 0.22f },
        new[] { 0.38f, 0.22f, 0.18f },
        new[] { 0.42f, 0.26f, 0.19f },
        new[] { 0.39f, 0.24f, 0.18f },
        new[] { 0.30f, 0.18f, 0.16f },
    };

    private static readonly float[] Narrow = { 0.10f, 0f, -0.08f, -0.18f };
    private static readonly float[] ThumbTucked = { 38, 55, 40 };
    private static readonly float[] ThumbOut = { 0, 4, 4 };
    private const Finger S = Finger.Straight, C = Finger.Curled, H = Finger.Half;

    public static readonly Pose OpenPalm = new("Open palm", new[] { S, S, S, S }, 55, ThumbOut, new[] { 0.20f, 0.03f, -0.12f, -0.28f }, 0, 0);
    public static readonly Pose ClosedFist = new("Closed fist", new[] { C, C, C, C }, 18, ThumbTucked, Narrow, 0, 0);
    public static readonly Pose PointingUp = new("Pointing up", new[] { S, C, C, C }, 18, ThumbTucked, new[] { 0.05f, 0f, -0.08f, -0.18f }, 0, 0);
    public static readonly Pose Victory = new("Victory", new[] { S, S, C, C }, 18, ThumbTucked, new[] { 0.26f, -0.10f, -0.08f, -0.18f }, 0, 0);
    public static readonly Pose ILoveYou = new("I love you", new[] { S, C, C, S }, 72, ThumbOut, new[] { 0.15f, 0f, -0.08f, -0.30f }, 0, 0);
    public static readonly Pose ThumbUp = new("Thumb up", new[] { C, C, C, C }, 85, ThumbOut, Narrow, 90, 0);
    public static readonly Pose ThumbDown = new("Thumb down", new[] { C, C, C, C }, 85, ThumbOut, Narrow, -90, 0);
    public static readonly Pose PinkyUp = new("Little finger up", new[] { C, C, C, S }, 18, ThumbTucked, new[] { 0.10f, 0f, -0.08f, -0.20f }, 0, 0);
    public static readonly Pose ThreeFingers = new("Three fingers", new[] { S, S, S, C }, 18, ThumbTucked, new[] { 0.18f, 0f, -0.14f, -0.18f }, 0, 0);
    public static readonly Pose Relaxed = new("Relaxed hand", new[] { H, H, H, H }, 35, new float[] { 10, 20, 15 }, Narrow, 0, 0);

    /// <summary>The seven ready-made gestures as drawings, keyed by Google's label.</summary>
    public static readonly IReadOnlyDictionary<string, Pose> ReadyMade = new Dictionary<string, Pose>
    {
        ["Open_Palm"] = OpenPalm,
        ["Closed_Fist"] = ClosedFist,
        ["Pointing_Up"] = PointingUp,
        ["Victory"] = Victory,
        ["ILoveYou"] = ILoveYou,
        ["Thumb_Up"] = ThumbUp,
        ["Thumb_Down"] = ThumbDown,
    };

    private static float[] Flex(Finger f) => f switch
    {
        Finger.Straight => new float[] { 4, 4, 3 },
        Finger.Curled => new float[] { 88, 100, 62 },
        _ => new float[] { 40, 45, 25 },
    };

    /// <summary>The 21 landmarks in the hand's own frame (palm length 1, x toward the thumb, y along the fingers, z toward the camera).</summary>
    public static float[] Model(Pose pose, Random? rng = null, float fingerJitter = 0, float thumbJitter = 0)
    {
        var p = new float[HandGraphs.LandmarkCount * 3];
        int n = 1;
        void Put(double x, double y, double z)
        {
            p[n * 3] = (float)x;
            p[n * 3 + 1] = (float)y;
            p[n * 3 + 2] = (float)z;
            n++;
        }
        double J(float sigma) => rng is null || sigma == 0 ? 0 : Gaussian(rng) * sigma;

        // thumb: base, then three segments folding from its own axis toward the palm and the camera
        Put(Bases[0][0], Bases[0][1], Bases[0][2]);
        double a = (pose.ThumbAbduction + J(thumbJitter)) * Math.PI / 180;
        double[] e1 = { Math.Sin(a), Math.Cos(a), 0 };
        double[] e2 = { -0.6, 0, 0.8 };
        double dot = e2[0] * e1[0] + e2[1] * e1[1] + e2[2] * e1[2];
        for (int i = 0; i < 3; i++) e2[i] -= e1[i] * dot;
        double len = Math.Sqrt(e2[0] * e2[0] + e2[1] * e2[1] + e2[2] * e2[2]);
        for (int i = 0; i < 3; i++) e2[i] /= len;
        double tx = Bases[0][0], ty = Bases[0][1], tz = Bases[0][2], phi = 0;
        for (int s = 0; s < 3; s++)
        {
            phi += (pose.ThumbFlex[s] + J(thumbJitter)) * Math.PI / 180;
            double c = Math.Cos(phi), sn = Math.Sin(phi), l = Lengths[0][s];
            tx += l * (c * e1[0] + sn * e2[0]);
            ty += l * (c * e1[1] + sn * e2[1]);
            tz += l * (c * e1[2] + sn * e2[2]);
            Put(tx, ty, tz);
        }

        // fingers: base, then three segments curling toward the camera
        for (int f = 0; f < 4; f++)
        {
            var b = Bases[f + 1];
            Put(b[0], b[1], b[2]);
            var flex = Flex(pose.Fingers[f]);
            double splay = pose.Splay[f], cs = Math.Cos(splay), ss = Math.Sin(splay);
            double x = b[0], y = b[1], z = b[2];
            phi = 0;
            for (int s = 0; s < 3; s++)
            {
                phi += (flex[s] + J(fingerJitter)) * Math.PI / 180;
                double l = Lengths[f + 1][s];
                x += l * Math.Cos(phi) * ss;
                y += l * Math.Cos(phi) * cs;
                z += l * Math.Sin(phi);
                Put(x, y, z);
            }
        }
        return p;
    }

    /// <summary>
    /// Draws a pose as HandGI's tracker would report it: x, y in image pixels, z in the same pixel
    /// units (negative = toward the camera). With jitter: random joint angles, orientation, size,
    /// position and landmark noise.
    /// </summary>
    public static float[] Render(Pose pose, Random rng, int width, int height, bool jitter = true)
    {
        var p = Model(pose, jitter ? rng : null, jitter ? 5f : 0f, jitter ? 5.5f : 0f);
        double roll = pose.Roll, yaw = pose.Yaw, pitch = 0, scale = 160, cx = width * 0.5, cy = height * 0.62;
        if (jitter)
        {
            roll += Gaussian(rng) * 10;
            yaw += Gaussian(rng) * 15;
            pitch = Gaussian(rng) * 10;
            scale = 110 + rng.NextDouble() * 90;
            cx = width * (0.3 + rng.NextDouble() * 0.4);
            cy = height * (0.45 + rng.NextDouble() * 0.3);
        }
        var r = Rotation(roll, yaw, pitch);
        var px = new float[p.Length];
        for (int i = 0; i < HandGraphs.LandmarkCount; i++)
        {
            double x = p[i * 3], y = p[i * 3 + 1], z = p[i * 3 + 2];
            double rx = r[0] * x + r[1] * y + r[2] * z;
            double ry = r[3] * x + r[4] * y + r[5] * z;
            double rz = r[6] * x + r[7] * y + r[8] * z;
            px[i * 3] = (float)(cx + scale * rx + (jitter ? Gaussian(rng) * 1.2 : 0));
            px[i * 3 + 1] = (float)(cy - scale * ry + (jitter ? Gaussian(rng) * 1.2 : 0));
            px[i * 3 + 2] = (float)(-scale * rz + (jitter ? Gaussian(rng) * 2.0 : 0));
        }
        return px;
    }

    /// <summary>
    /// Counter-examples for learning your gestures: every combination of straight, half-bent and
    /// curled fingers with three thumb positions, mostly upright and sometimes turned sideways or
    /// upside down, each drawn with random variation. Returned in MediaPipe's normalised form.
    /// </summary>
    public static List<float[]> NegativeBank(int count, int seed)
    {
        var rng = new Random(seed);
        var states = new[] { S, H, C };
        var thumbs = new (float Abduction, float[] Flex)[] { (60, ThumbOut), (18, ThumbTucked), (35, new float[] { 10, 20, 15 }) };
        var bases = new List<Pose>();
        foreach (var i in states)
            foreach (var m in states)
                foreach (var r in states)
                    foreach (var l in states)
                        foreach (var t in thumbs)
                            bases.Add(new Pose("counter-example", new[] { i, m, r, l }, t.Abduction, t.Flex, Narrow, 0, 0));
        var turns = new[] { 90f, -90f, 180f };
        var bank = new List<float[]>(count);
        for (int k = 0; k < count; k++)
        {
            var b = bases[k % bases.Count];
            float roll = rng.NextDouble() < 0.7 ? (float)(Gaussian(rng) * 20) : turns[rng.Next(turns.Length)] + (float)(Gaussian(rng) * 15);
            var pose = b with { Roll = roll, Yaw = (float)(Gaussian(rng) * 20), Splay = rng.NextDouble() < 0.5 ? Narrow : new[] { 0.22f, 0.04f, -0.12f, -0.28f } };
            bank.Add(MediaPipeGestureModel.Normalize(Render(pose, rng, 1280, 720), 1280, 720));
        }
        return bank;
    }

    /// <summary>Row-major 3×3 rotation Rz(roll) · Ry(yaw) · Rx(pitch), angles in degrees.</summary>
    private static double[] Rotation(double rollDeg, double yawDeg, double pitchDeg)
    {
        double r = rollDeg * Math.PI / 180, y = yawDeg * Math.PI / 180, p = pitchDeg * Math.PI / 180;
        double cr = Math.Cos(r), sr = Math.Sin(r), cy = Math.Cos(y), sy = Math.Sin(y), cp = Math.Cos(p), sp = Math.Sin(p);
        // Rz · Ry
        double[] a = { cr * cy, -sr, cr * sy, sr * cy, cr, sr * sy, -sy, 0, cy };
        // (Rz · Ry) · Rx
        return new[]
        {
            a[0], a[1] * cp + a[2] * sp, -a[1] * sp + a[2] * cp,
            a[3], a[4] * cp + a[5] * sp, -a[4] * sp + a[5] * cp,
            a[6], a[7] * cp + a[8] * sp, -a[7] * sp + a[8] * cp,
        };
    }

    public static double Gaussian(Random rng)
    {
        double u1 = 1.0 - rng.NextDouble();
        double u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
