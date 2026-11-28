// HandGI engine (Angela López Mendoza · XAIHT), part of Tlachihuani. tlachihuani:trace-off (runs on every camera frame and in tight GPU and training loops; its own messages reach Tlachihuani.log through the HandGI log bridge)
namespace HandGI.Core.Vision;

/// <summary>Pixel layouts HandGI reads straight from the camera buffer (no CPU colour conversion).</summary>
public enum PixelLayoutKind
{
    Nv12,
    Yuy2,
    Bgra8,
    Rgba8,
    Gray8,
}

/// <summary>YUV quantisation range. Webcams almost always deliver "limited" (16–235) video range.</summary>
public enum YuvRange
{
    Limited,
    Full,
}

/// <summary>
/// The exact byte layout of a camera frame buffer: planes, offsets and strides. The GPU graphs are
/// generated for one layout, so strides and plane offsets become constants inside the graph.
/// </summary>
public sealed record FrameLayout(
    PixelLayoutKind Kind,
    int Width,
    int Height,
    int ByteLength,
    int Offset0,
    int Stride0,
    int Offset1,
    int Stride1)
{
    public string Key => $"{Kind}:{Width}x{Height}:{ByteLength}:{Offset0}/{Stride0}:{Offset1}/{Stride1}";

    public bool IsYuv => Kind is PixelLayoutKind.Nv12 or PixelLayoutKind.Yuy2;

    public override string ToString() => $"{Kind} {Width}×{Height}";

    /// <summary>A tightly packed layout (used by tests and synthetic frames).</summary>
    public static FrameLayout Packed(PixelLayoutKind kind, int width, int height) => kind switch
    {
        PixelLayoutKind.Nv12 => new FrameLayout(kind, width, height, width * height * 3 / 2, 0, width, width * height, width),
        PixelLayoutKind.Yuy2 => new FrameLayout(kind, width, height, width * height * 2, 0, width * 2, 0, 0),
        PixelLayoutKind.Bgra8 or PixelLayoutKind.Rgba8 => new FrameLayout(kind, width, height, width * height * 4, 0, width * 4, 0, 0),
        PixelLayoutKind.Gray8 => new FrameLayout(kind, width, height, width * height, 0, width, 0, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
