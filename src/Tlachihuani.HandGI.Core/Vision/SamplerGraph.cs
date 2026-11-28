// HandGI engine (Angela López Mendoza · XAIHT), part of Tlachihuani. tlachihuani:trace-off (runs on every camera frame and in tight GPU and training loops; its own messages reach Tlachihuani.log through the HandGI log bridge)
using HandGI.Core.Onnx;

namespace HandGI.Core.Vision;

/// <summary>
/// Emits ONNX nodes that read pixels straight out of the raw camera buffer on the GPU.
/// For every output pixel the graph computes its source position through a 2×3 affine matrix
/// (letterbox, rotated hand crop, preview scale/mirror), gathers the raw bytes it needs
/// (NV12 / YUY2 / BGRA / RGBA / Gray), interpolates bilinearly and converts YUV to RGB.
/// Only the sampled pixels are ever converted; the full frame is never touched by the CPU.
///
/// Convention: the affine maps continuous output coordinates (pixel centres at i + 0.5) to
/// continuous source coordinates; pixel indices are therefore source − 0.5.
/// </summary>
internal static class SamplerGraph
{
    /// <summary>Returns the name of a float [P,3] tensor of RGB values in [0,1], P = outW × outH (row-major).</summary>
    public static string SampleRgb(OnnxGraph g, string frame, FrameLayout layout, string affine, int outW, int outH, bool bilinear, YuvRange range)
    {
        var s = new Sampler(g, frame, layout, outW * outH, range);
        var (sx, sy) = s.SourceCoords(affine, outW, outH);
        var (r, gr, b) = s.Rgb01(sx, sy, bilinear);
        return g.Concat(1, r, gr, b);
    }

    /// <summary>Returns the name of a uint8 [outH, outW, 4] BGRA image (nearest sampling) for display.</summary>
    public static string SampleBgraBytes(OnnxGraph g, string frame, FrameLayout layout, string affine, int outW, int outH, YuvRange range)
    {
        int p = outW * outH;
        var s = new Sampler(g, frame, layout, p, range);
        var (sx, sy) = s.SourceCoords(affine, outW, outH);
        var (r, gr, b) = s.Rgb01(sx, sy, bilinear: false);
        var alpha = g.Expand(g.Scalar(1f), p, 1);
        var bgra = g.Concat(1, b, gr, r, alpha);
        var scaled = g.Min(g.Floor(g.Add(g.Mul(bgra, g.Scalar(255f)), g.Scalar(0.5f))), g.Scalar(255f));
        return g.Reshape(g.Cast(scaled, OnnxType.UInt8), outH, outW, 4);
    }

    private sealed class Sampler
    {
        private readonly OnnxGraph _g;
        private readonly string _frame;
        private readonly FrameLayout _l;
        private readonly int _p;
        private readonly YuvRange _range;

        public Sampler(OnnxGraph g, string frame, FrameLayout layout, int pixelCount, YuvRange range)
        {
            _g = g;
            _frame = frame;
            _l = layout;
            _p = pixelCount;
            _range = range;
        }

        private string S(float v) => _g.Scalar(v);

        public (string X, string Y) SourceCoords(string affine, int outW, int outH)
        {
            var grid = new float[_p * 3];
            int k = 0;
            for (int v = 0; v < outH; v++)
            {
                for (int u = 0; u < outW; u++)
                {
                    grid[k++] = u + 0.5f;
                    grid[k++] = v + 0.5f;
                    grid[k++] = 1f;
                }
            }
            var gridConst = _g.Const(grid, _p, 3);
            var mt = _g.Transpose(_g.Reshape(affine, 2, 3), 1, 0);
            var src = _g.Sub(_g.MatMul(gridConst, mt), S(0.5f));
            return (_g.Slice(src, 0, 1, 1), _g.Slice(src, 1, 2, 1));
        }

        public (string R, string G, string B) Rgb01(string sx, string sy, bool bilinear)
        {
            var xr = _g.Floor(_g.Add(sx, S(0.5f)));
            var yr = _g.Floor(_g.Add(sy, S(0.5f)));
            var inside = Valid(xr, yr);
            var xrc = ClampX(xr);
            var yrc = ClampY(yr);
            Taps? taps = bilinear ? new Taps(this, sx, sy) : null;

            string r, gr, b;
            switch (_l.Kind)
            {
                case PixelLayoutKind.Bgra8:
                case PixelLayoutKind.Rgba8:
                {
                    int rOff = _l.Kind == PixelLayoutKind.Bgra8 ? 2 : 0;
                    int bOff = _l.Kind == PixelLayoutKind.Bgra8 ? 0 : 2;
                    Func<string, string, int, string> px = (x, y, c) => Fetch(Lin(x, 4, y, _l.Stride0, _l.Offset0 + c));
                    if (taps is not null)
                    {
                        r = taps.Sample((x, y) => px(x, y, rOff));
                        gr = taps.Sample((x, y) => px(x, y, 1));
                        b = taps.Sample((x, y) => px(x, y, bOff));
                    }
                    else
                    {
                        r = px(xrc, yrc, rOff);
                        gr = px(xrc, yrc, 1);
                        b = px(xrc, yrc, bOff);
                    }
                    r = Div255(r);
                    gr = Div255(gr);
                    b = Div255(b);
                    break;
                }
                case PixelLayoutKind.Gray8:
                {
                    var y = taps is not null ? taps.Sample(Luma) : Luma(xrc, yrc);
                    r = gr = b = Div255(y);
                    break;
                }
                default:
                {
                    var y = taps is not null ? taps.Sample(Luma) : Luma(xrc, yrc);
                    var (u, v) = Chroma(xrc, yrc);
                    (r, gr, b) = YuvToRgb01(y, u, v);
                    break;
                }
            }

            return (Clamp01(_g.Mul(r, inside)), Clamp01(_g.Mul(gr, inside)), Clamp01(_g.Mul(b, inside)));
        }

        // ---------------------------------------------------------------- pixel addressing

        private string Lin(string x, float xMul, string y, float yMul, float add) =>
            _g.Add(_g.Add(_g.Mul(x, S(xMul)), _g.Mul(y, S(yMul))), S(add));

        private string Luma(string x, string y) => _l.Kind switch
        {
            PixelLayoutKind.Yuy2 => Fetch(Lin(x, 2, y, _l.Stride0, _l.Offset0)),
            _ => Fetch(Lin(x, 1, y, _l.Stride0, _l.Offset0)),
        };

        private (string U, string V) Chroma(string x, string y)
        {
            var hx = _g.Floor(_g.Mul(x, S(0.5f)));
            if (_l.Kind == PixelLayoutKind.Nv12)
            {
                var hy = _g.Floor(_g.Mul(y, S(0.5f)));
                return (Fetch(Lin(hx, 2, hy, _l.Stride1, _l.Offset1)), Fetch(Lin(hx, 2, hy, _l.Stride1, _l.Offset1 + 1)));
            }
            // YUY2: Y0 U Y1 V per pair of pixels
            return (Fetch(Lin(hx, 4, y, _l.Stride0, _l.Offset0 + 1)), Fetch(Lin(hx, 4, y, _l.Stride0, _l.Offset0 + 3)));
        }

        /// <summary>Gathers one byte per output pixel from the raw frame; index is an integer-valued float [P,1].</summary>
        private string Fetch(string indexFloat)
        {
            var flat = _g.Reshape(_g.Cast(indexFloat, OnnxType.Int32), _p);
            var bytes = _g.Gather(_frame, flat, 0);
            return _g.Reshape(_g.Cast(bytes, OnnxType.Float), _p, 1);
        }

        // ---------------------------------------------------------------- helpers

        public string Valid(string x, string y)
        {
            var vx = _g.And(_g.Greater(x, S(-0.5f)), _g.Less(x, S(_l.Width - 0.5f)));
            var vy = _g.And(_g.Greater(y, S(-0.5f)), _g.Less(y, S(_l.Height - 0.5f)));
            return _g.Cast(_g.And(vx, vy), OnnxType.Float);
        }

        public string ClampX(string x) => _g.Max(_g.Min(x, S(_l.Width - 1)), S(0f));

        public string ClampY(string y) => _g.Max(_g.Min(y, S(_l.Height - 1)), S(0f));

        private string Div255(string x) => _g.Mul(x, S(1f / 255f));

        private string Clamp01(string x) => _g.Max(_g.Min(x, S(1f)), S(0f));

        private (string R, string G, string B) YuvToRgb01(string y, string u, string v)
        {
            var uu = _g.Sub(u, S(128f));
            var vv = _g.Sub(v, S(128f));
            string yy;
            float cr, cgu, cgv, cb;
            if (_range == YuvRange.Limited)
            {
                yy = _g.Mul(_g.Sub(y, S(16f)), S(1.164383f));
                cr = 1.596027f;
                cgu = 0.391762f;
                cgv = 0.812968f;
                cb = 2.017232f;
            }
            else
            {
                yy = y;
                cr = 1.402f;
                cgu = 0.344136f;
                cgv = 0.714136f;
                cb = 1.772f;
            }
            var r = _g.Add(yy, _g.Mul(vv, S(cr)));
            var gr = _g.Sub(_g.Sub(yy, _g.Mul(uu, S(cgu))), _g.Mul(vv, S(cgv)));
            var b = _g.Add(yy, _g.Mul(uu, S(cb)));
            return (Div255(r), Div255(gr), Div255(b));
        }

        /// <summary>The four bilinear taps around each sample: clamped coordinates and weights (zero outside the frame).</summary>
        private sealed class Taps
        {
            private readonly Sampler _s;
            private readonly string _x0, _x1, _y0, _y1, _w00, _w10, _w01, _w11;

            public Taps(Sampler s, string sx, string sy)
            {
                _s = s;
                var g = s._g;
                var one = s.S(1f);
                var x0 = g.Floor(sx);
                var y0 = g.Floor(sy);
                var fx = g.Sub(sx, x0);
                var fy = g.Sub(sy, y0);
                var x1 = g.Add(x0, one);
                var y1 = g.Add(y0, one);
                var gx = g.Sub(one, fx);
                var gy = g.Sub(one, fy);
                _w00 = g.Mul(g.Mul(gx, gy), s.Valid(x0, y0));
                _w10 = g.Mul(g.Mul(fx, gy), s.Valid(x1, y0));
                _w01 = g.Mul(g.Mul(gx, fy), s.Valid(x0, y1));
                _w11 = g.Mul(g.Mul(fx, fy), s.Valid(x1, y1));
                _x0 = s.ClampX(x0);
                _x1 = s.ClampX(x1);
                _y0 = s.ClampY(y0);
                _y1 = s.ClampY(y1);
            }

            public string Sample(Func<string, string, string> fetch)
            {
                var g = _s._g;
                var a = g.Add(g.Mul(fetch(_x0, _y0), _w00), g.Mul(fetch(_x1, _y0), _w10));
                var b = g.Add(g.Mul(fetch(_x0, _y1), _w01), g.Mul(fetch(_x1, _y1), _w11));
                return g.Add(a, b);
            }
        }
    }
}
