// HandGI engine (Angela López Mendoza · XAIHT), part of Tlachihuani. tlachihuani:trace-off (runs on every camera frame and in tight GPU and training loops; its own messages reach Tlachihuani.log through the HandGI log bridge)
namespace HandGI.Core.Vision;

/// <summary>A rotated rectangle in image pixels. Rotation is clockwise in radians (image y points down).</summary>
public readonly record struct RotatedRect(float Cx, float Cy, float Width, float Height, float Rotation)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>The four corners (clockwise from top-left in the rectangle's own frame).</summary>
    public (float X, float Y)[] Corners()
    {
        float c = MathF.Cos(Rotation), s = MathF.Sin(Rotation);
        float hw = Width / 2, hh = Height / 2;
        var local = new[] { (-hw, -hh), (hw, -hh), (hw, hh), (-hw, hh) };
        var result = new (float, float)[4];
        for (int i = 0; i < 4; i++)
        {
            var (x, y) = local[i];
            result[i] = (Cx + x * c - y * s, Cy + x * s + y * c);
        }
        return result;
    }
}

/// <summary>
/// MediaPipe's hand-tracking geometry, ported faithfully: palm detection → hand ROI
/// (DetectionsToRects + RectTransformation scale 2.6, shift −0.5) and landmarks → next-frame ROI
/// (HandLandmarksToRect + RectTransformation scale 2.0, shift −0.1), all in pixel units.
/// This is ~100 scalar operations per frame; every pixel and network operation runs on the GPU.
/// </summary>
public static class HandGeometry
{
    private const float TargetAngle = MathF.PI / 2f;
    private static readonly int[] PartialLandmarks = { 0, 1, 2, 3, 5, 6, 9, 10, 13, 14, 17, 18 };

    public static float NormalizeRadians(float angle) =>
        angle - 2f * MathF.PI * MathF.Floor((angle + MathF.PI) / (2f * MathF.PI));

    /// <summary>
    /// ROI from one palm candidate laid out as
    /// [score, x1, y1, x2, y2, kp0x, kp0y, kp1x, kp1y, ... kp6x, kp6y] in image pixels.
    /// </summary>
    public static RotatedRect FromPalm(ReadOnlySpan<float> candidate)
    {
        float x1 = candidate[1], y1 = candidate[2], x2 = candidate[3], y2 = candidate[4];
        float cx = (x1 + x2) / 2f, cy = (y1 + y2) / 2f;
        float w = MathF.Abs(x2 - x1), h = MathF.Abs(y2 - y1);
        // keypoint 0 = wrist centre, keypoint 2 = middle-finger MCP
        float k0x = candidate[5], k0y = candidate[6];
        float k2x = candidate[9], k2y = candidate[10];
        float rotation = NormalizeRadians(TargetAngle - MathF.Atan2(-(k2y - k0y), k2x - k0x));
        return Transform(new RotatedRect(cx, cy, w, h, rotation), 2.6f, 0f, -0.5f, squareLong: true);
    }

    /// <summary>Next-frame ROI from 21 landmarks [x, y, z] in image pixels.</summary>
    public static RotatedRect FromLandmarks(ReadOnlySpan<float> landmarks)
    {
        float x0 = landmarks[0], y0 = landmarks[1];
        float x1 = (landmarks[5 * 3] + landmarks[13 * 3]) / 2f;
        float y1 = (landmarks[5 * 3 + 1] + landmarks[13 * 3 + 1]) / 2f;
        x1 = (x1 + landmarks[9 * 3]) / 2f;
        y1 = (y1 + landmarks[9 * 3 + 1]) / 2f;
        float rotation = NormalizeRadians(TargetAngle - MathF.Atan2(-(y1 - y0), x1 - x0));
        float reverse = NormalizeRadians(-rotation);

        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (int i in PartialLandmarks)
        {
            float x = landmarks[i * 3], y = landmarks[i * 3 + 1];
            minX = MathF.Min(minX, x);
            maxX = MathF.Max(maxX, x);
            minY = MathF.Min(minY, y);
            maxY = MathF.Max(maxY, y);
        }
        float acx = (minX + maxX) / 2f, acy = (minY + maxY) / 2f;

        float cr = MathF.Cos(reverse), sr = MathF.Sin(reverse);
        minX = minY = float.MaxValue;
        maxX = maxY = float.MinValue;
        foreach (int i in PartialLandmarks)
        {
            float ox = landmarks[i * 3] - acx, oy = landmarks[i * 3 + 1] - acy;
            float px = ox * cr - oy * sr;
            float py = ox * sr + oy * cr;
            minX = MathF.Min(minX, px);
            maxX = MathF.Max(maxX, px);
            minY = MathF.Min(minY, py);
            maxY = MathF.Max(maxY, py);
        }
        float pcx = (minX + maxX) / 2f, pcy = (minY + maxY) / 2f;
        float c = MathF.Cos(rotation), s = MathF.Sin(rotation);
        float cx = pcx * c - pcy * s + acx;
        float cy = pcx * s + pcy * c + acy;
        var rect = new RotatedRect(cx, cy, maxX - minX, maxY - minY, rotation);
        return Transform(rect, 2.0f, 0f, -0.1f, squareLong: true);
    }

    /// <summary>MediaPipe RectTransformationCalculator in pixel units.</summary>
    public static RotatedRect Transform(RotatedRect r, float scale, float shiftX, float shiftY, bool squareLong)
    {
        float w = r.Width, h = r.Height, cx = r.Cx, cy = r.Cy;
        if (r.Rotation == 0f)
        {
            cx += w * shiftX;
            cy += h * shiftY;
        }
        else
        {
            float c = MathF.Cos(r.Rotation), s = MathF.Sin(r.Rotation);
            cx += w * shiftX * c - h * shiftY * s;
            cy += w * shiftX * s + h * shiftY * c;
        }
        if (squareLong)
        {
            float side = MathF.Max(w, h);
            w = side;
            h = side;
        }
        return new RotatedRect(cx, cy, w * scale, h * scale, r.Rotation);
    }

    /// <summary>
    /// Affine [a, b, tx, c, d, ty] mapping continuous crop coordinates (0..outSize) to image
    /// coordinates for a square crop of the rectangle, plus the pixel scale k (image px per crop px).
    /// </summary>
    public static (float[] Affine, float Scale) CropAffine(RotatedRect r, int outSize)
    {
        float side = MathF.Max(r.Width, r.Height);
        float k = side / outSize;
        float c = MathF.Cos(r.Rotation), s = MathF.Sin(r.Rotation);
        float half = outSize / 2f;
        float a = k * c, b = -k * s, d = k * s, e = k * c;
        float tx = r.Cx - (a * half + b * half);
        float ty = r.Cy - (d * half + e * half);
        return (new[] { a, b, tx, d, e, ty }, k);
    }

    /// <summary>Letterbox affine: square detector input (size×size) centred on a W×H frame, black bars outside.</summary>
    public static float[] Letterbox(int width, int height, int size)
    {
        float k = MathF.Max(width, height) / (float)size;
        return new[] { k, 0f, width / 2f - k * size / 2f, 0f, k, height / 2f - k * size / 2f };
    }

    /// <summary>Preview affine (optionally mirrored like a selfie view).</summary>
    public static float[] Preview(int width, int height, int previewWidth, int previewHeight, bool mirror)
    {
        float kx = width / (float)previewWidth, ky = height / (float)previewHeight;
        return mirror
            ? new[] { -kx, 0f, (float)width, 0f, ky, 0f }
            : new[] { kx, 0f, 0f, 0f, ky, 0f };
    }

    /// <summary>A ROI is usable when it is not degenerate and overlaps the frame.</summary>
    public static bool IsUsable(RotatedRect r, int width, int height)
    {
        float side = MathF.Max(r.Width, r.Height);
        if (!(side >= 24f) || side > 4f * MathF.Max(width, height)) return false;
        if (float.IsNaN(r.Cx) || float.IsNaN(r.Cy)) return false;
        return r.Cx > -side / 2 && r.Cy > -side / 2 && r.Cx < width + side / 2 && r.Cy < height + side / 2;
    }
}
