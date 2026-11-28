// HandGI engine (Angela López Mendoza · XAIHT), part of Tlachihuani. tlachihuani:trace-off (runs on every camera frame and in tight GPU and training loops; its own messages reach Tlachihuani.log through the HandGI log bridge)
namespace HandGI.Core.Vision;

/// <summary>
/// SSD anchors of the MediaPipe palm detector (192×192 input): strides [8, 16, 16, 16],
/// fixed anchor size, offset 0.5. Stride 8 gives a 24×24 grid with 2 anchors per cell; the three
/// stride-16 layers merge into a 12×12 grid with 6 anchors per cell: 1152 + 864 = 2016 anchors,
/// enumerated y, then x, then anchor (MediaPipe's SsdAnchorsCalculator order).
/// </summary>
public static class PalmAnchors
{
    public const int Count = 2016;
    public const int InputSize = 192;

    /// <summary>Anchor centres in 192-pixel input space, [x0, y0, x1, y1, ...].</summary>
    public static float[] Centers192()
    {
        var result = new float[Count * 2];
        int k = 0;
        foreach (var (grid, perCell) in new[] { (24, 2), (12, 6) })
        {
            for (int y = 0; y < grid; y++)
            {
                for (int x = 0; x < grid; x++)
                {
                    float cx = (x + 0.5f) / grid * InputSize;
                    float cy = (y + 0.5f) / grid * InputSize;
                    for (int a = 0; a < perCell; a++)
                    {
                        result[k++] = cx;
                        result[k++] = cy;
                    }
                }
            }
        }
        if (k != Count * 2) throw new InvalidOperationException("Palm anchor generation mismatch.");
        return result;
    }
}
