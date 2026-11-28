// HandGI expert window (Angela López Mendoza · XAIHT), part of Tlachihuani. tlachihuani:trace-off (repaints and polls the engine every 15 ms; its actions are written to Tlachihuani.log through the HandGI log bridge)
using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using HandGI.Core.Engine;

namespace HandGI.App.Controls;

/// <summary>
/// Shows the GPU-rendered preview (already scaled, mirrored and converted to BGRA by the GPU) with the
/// tracked hand drawn on top. Display only: it never sits on the decision path.
/// </summary>
internal sealed class PreviewView : Control
{
    private static readonly int[][] Bones =
    {
        new[] { 0, 1, 2, 3, 4 },
        new[] { 0, 5, 6, 7, 8 },
        new[] { 9, 10, 11, 12 },
        new[] { 13, 14, 15, 16 },
        new[] { 0, 17, 18, 19, 20 },
        new[] { 5, 9, 13, 17 },
    };

    private readonly FrameReport _report = new();
    private Bitmap? _frame;
    private bool _hasReport;
    private string[] _names = Array.Empty<string>();

    public PreviewView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.PreviewBack;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Placeholder { get; set; } = "Starting...";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? Countdown { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color CountdownColor { get; set; } = Theme.Text;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowSkeleton { get; set; } = true;

    public void SetClassNames(IEnumerable<string> names)
    {
        _names = names.ToArray();
        Invalidate();
    }

    public unsafe void UpdateFrame(ReadOnlySpan<byte> bgra, int width, int height)
    {
        if (_frame is null || _frame.Width != width || _frame.Height != height)
        {
            _frame?.Dispose();
            _frame = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        }
        var data = _frame.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        try
        {
            int row = width * 4;
            for (int y = 0; y < height; y++)
                bgra.Slice(y * row, row).CopyTo(new Span<byte>((byte*)data.Scan0 + (long)y * data.Stride, row));
        }
        finally
        {
            _frame.UnlockBits(data);
        }
        Invalidate();
    }

    public void UpdateReport(FrameReport report)
    {
        report.CopyTo(_report);
        _hasReport = true;
        Invalidate();
    }

    public void ClearFrame()
    {
        _frame?.Dispose();
        _frame = null;
        _hasReport = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        var client = ClientRectangle;
        if (_frame is null)
        {
            var box = Rectangle.Inflate(client, -40, -40);
            TextRenderer.DrawText(g, Placeholder, Theme.Heading, box, Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            DrawCountdown(g, client);
            return;
        }

        var dest = Fit(_frame.Size, client);
        g.InterpolationMode = InterpolationMode.Bilinear;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImage(_frame, dest);
        g.CompositingMode = CompositingMode.SourceOver;
        g.PixelOffsetMode = PixelOffsetMode.Default;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (_hasReport && _report.HasHand && ShowSkeleton && _report.Width > 0) DrawHand(g, dest);
        DrawHud(g, dest);
        DrawCountdown(g, client);
    }

    private PointF Map(float x, float y, RectangleF dest)
    {
        float u = x / _report.Width, v = y / _report.Height;
        if (_report.Mirror) u = 1f - u;
        return new PointF(dest.X + u * dest.Width, dest.Y + v * dest.Height);
    }

    private void DrawHand(Graphics g, RectangleF dest)
    {
        var roi = _report.Roi.Corners().Select(c => Map(c.X, c.Y, dest)).ToArray();
        using (var roiPen = new Pen(Color.FromArgb(110, Theme.Info), 1.4f) { DashStyle = DashStyle.Dash })
            g.DrawPolygon(roiPen, roi);

        var points = new PointF[21];
        for (int i = 0; i < 21; i++) points[i] = Map(_report.Landmarks[i * 3], _report.Landmarks[i * 3 + 1], dest);
        var color = _report.ActiveClass >= 0 ? Theme.ClassColor(_report.ActiveClass) : Theme.Accent;
        float width = Math.Max(2f, dest.Width / 320f);
        using var bone = new Pen(Color.FromArgb(230, color), width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        foreach (var chain in Bones) g.DrawLines(bone, chain.Select(i => points[i]).ToArray());
        using var joint = new SolidBrush(Color.FromArgb(245, 255, 255, 255));
        float r = width * 1.25f;
        foreach (var p in points) g.FillEllipse(joint, p.X - r, p.Y - r, 2 * r, 2 * r);
    }

    private void DrawHud(Graphics g, RectangleF dest)
    {
        if (!_hasReport) return;
        string gesture = !_report.HasHand
            ? "No hand"
            : _report.ActiveClass >= 0 && _report.ActiveClass < _names.Length ? _names[_report.ActiveClass] : "Hand tracked";
        var color = _report.ActiveClass >= 0 ? Theme.ClassColor(_report.ActiveClass) : _report.HasHand ? Theme.Muted : Theme.Border;
        Pill(g, new PointF(dest.X + 12, dest.Y + 12), gesture, color, Theme.Heading);

        double latency = double.IsNaN(_report.CameraToDecisionMs) ? _report.ArrivalToDecisionMs : _report.CameraToDecisionMs;
        string stats = $"{_report.Fps:0} fps  ·  {latency:0.0} ms {(double.IsNaN(_report.CameraToDecisionMs) ? "arrival" : "capture")}→decision  ·  GPU {_report.GpuMs:0.0} ms";
        Pill(g, new PointF(dest.X + 12, dest.Bottom - 40), stats, Theme.SurfaceHigh, Theme.Small);

        if (_report.Recording)
        {
            string rec = $"REC  {_report.RecordedSamples}";
            var size = TextRenderer.MeasureText(rec, Theme.BodyBold);
            Pill(g, new PointF(dest.Right - size.Width - 40, dest.Y + 12), rec, Theme.Danger, Theme.BodyBold);
        }
    }

    private static void Pill(Graphics g, PointF at, string text, Color color, Font font)
    {
        var size = TextRenderer.MeasureText(g, text, font, Size.Empty, TextFormatFlags.NoPadding);
        var rect = new RectangleF(at.X, at.Y, size.Width + 22, size.Height + 10);
        using var path = Theme.Rounded(rect, rect.Height / 2);
        using var fill = new SolidBrush(Color.FromArgb(200, 10, 12, 16));
        g.FillPath(fill, path);
        using var edge = new Pen(Color.FromArgb(220, color), 1.6f);
        g.DrawPath(edge, path);
        TextRenderer.DrawText(g, text, font, Rectangle.Round(rect), Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private void DrawCountdown(Graphics g, Rectangle client)
    {
        if (string.IsNullOrEmpty(Countdown)) return;
        var size = TextRenderer.MeasureText(g, Countdown, Theme.Big, Size.Empty, TextFormatFlags.NoPadding);
        var rect = new RectangleF(client.X + (client.Width - size.Width - 60) / 2f, client.Y + (client.Height - size.Height - 36) / 2f, size.Width + 60, size.Height + 36);
        using var path = Theme.Rounded(rect, 18);
        using var fill = new SolidBrush(Color.FromArgb(215, 10, 12, 16));
        g.FillPath(fill, path);
        using var edge = new Pen(CountdownColor, 2f);
        g.DrawPath(edge, path);
        TextRenderer.DrawText(g, Countdown, Theme.Big, Rectangle.Round(rect), CountdownColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private static RectangleF Fit(Size image, Rectangle area)
    {
        float scale = Math.Min(area.Width / (float)image.Width, area.Height / (float)image.Height);
        float w = image.Width * scale, h = image.Height * scale;
        return new RectangleF(area.X + (area.Width - w) / 2f, area.Y + (area.Height - h) / 2f, w, h);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _frame?.Dispose();
        base.Dispose(disposing);
    }
}
