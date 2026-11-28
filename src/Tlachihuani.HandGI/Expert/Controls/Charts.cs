// HandGI expert window (Angela López Mendoza · XAIHT), part of Tlachihuani. tlachihuani:trace-off (repaints and polls the engine every 15 ms; its actions are written to Tlachihuani.log through the HandGI log bridge)
using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace HandGI.App.Controls;

/// <summary>Live per-gesture probabilities (smoothed), with the enter and exit thresholds drawn on every bar.</summary>
internal sealed class ProbabilityBars : Control
{
    private string[] _names = Array.Empty<string>();
    private readonly float[] _values = new float[HandGI.Core.Vision.HandGraphs.MaxGestures];
    private int _active = -1;
    private int _candidate = -1;

    public ProbabilityBars()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
        Height = 120;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float EnterThreshold { get; set; } = 0.8f;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float ExitThreshold { get; set; } = 0.5f;

    private int RowHeight => LogicalToDeviceUnits(28);

    public void SetNames(IEnumerable<string> names)
    {
        _names = names.ToArray();
        Height = RowHeight * Math.Max(1, _names.Length) + LogicalToDeviceUnits(6);
        Invalidate();
    }

    public void SetValues(ReadOnlySpan<float> smoothed, int active, int candidate)
    {
        int n = Math.Min(smoothed.Length, _values.Length);
        for (int i = 0; i < n; i++) _values[i] = smoothed[i];
        _active = active;
        _candidate = candidate;
        Invalidate();
    }

    public void Clear()
    {
        Array.Clear(_values);
        _active = _candidate = -1;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int labelWidth = LogicalToDeviceUnits(128);
        int valueWidth = LogicalToDeviceUnits(46);
        int row = RowHeight;
        int barLeft = labelWidth;
        int barWidth = Math.Max(20, Width - labelWidth - valueWidth - 4);
        for (int i = 0; i < _names.Length; i++)
        {
            int y = i * row + 3;
            var color = Theme.ClassColor(i);
            bool active = i == _active;
            TextRenderer.DrawText(g, _names[i], active ? Theme.BodyBold : Theme.Body, new Rectangle(0, y, labelWidth - 6, row - 6),
                active ? Theme.Text : Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            var track = new RectangleF(barLeft, y + row * 0.28f, barWidth, row * 0.44f);
            using (var trackPath = Theme.Rounded(track, track.Height / 2))
            using (var trackBrush = new SolidBrush(Theme.Input))
                g.FillPath(trackBrush, trackPath);
            float v = Math.Clamp(_values[i], 0f, 1f);
            if (v > 0.01f)
            {
                var fill = new RectangleF(track.X, track.Y, Math.Max(track.Height, track.Width * v), track.Height);
                using var fillPath = Theme.Rounded(fill, track.Height / 2);
                using var fillBrush = new SolidBrush(active ? color : Color.FromArgb(i == _candidate ? 200 : 120, color));
                g.FillPath(fillBrush, fillPath);
            }
            using (var enter = new Pen(Color.FromArgb(200, Theme.Text), 1.2f))
                g.DrawLine(enter, track.X + track.Width * EnterThreshold, track.Y - 3, track.X + track.Width * EnterThreshold, track.Bottom + 3);
            using (var exit = new Pen(Color.FromArgb(150, Theme.Muted), 1f) { DashStyle = DashStyle.Dot })
                g.DrawLine(exit, track.X + track.Width * ExitThreshold, track.Y - 3, track.X + track.Width * ExitThreshold, track.Bottom + 3);
            TextRenderer.DrawText(g, $"{v * 100:0}%", Theme.Small, new Rectangle(barLeft + barWidth + 4, y, valueWidth, row - 6),
                active ? Theme.Text : Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        }
    }
}

/// <summary>Training curves: loss (blue, own scale) and batch accuracy (green, 0-100%).</summary>
internal sealed class LineChart : Control
{
    private readonly List<(float Step, float Loss, float Accuracy)> _points = new();
    private float _maxStep = 1;

    public LineChart()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Input;
        Height = 150;
    }

    public void Reset(int steps)
    {
        _points.Clear();
        _maxStep = Math.Max(1, steps);
        Invalidate();
    }

    public void Add(float step, float loss, float accuracy)
    {
        _points.Add((step, loss, accuracy));
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var plot = new RectangleF(LogicalToDeviceUnits(40), LogicalToDeviceUnits(10), Width - LogicalToDeviceUnits(84), Height - LogicalToDeviceUnits(34));
        using (var grid = new Pen(Theme.Border))
        {
            for (int i = 0; i <= 4; i++)
            {
                float y = plot.Y + plot.Height * i / 4f;
                g.DrawLine(grid, plot.X, y, plot.Right, y);
            }
        }
        TextRenderer.DrawText(g, "loss", Theme.Small, new Point(4, (int)plot.Y), Theme.Info);
        TextRenderer.DrawText(g, "100%", Theme.Small, new Point((int)plot.Right + 6, (int)plot.Y - 2), Theme.Accent);
        TextRenderer.DrawText(g, "0%", Theme.Small, new Point((int)plot.Right + 6, (int)plot.Bottom - 12), Theme.Accent);
        if (_points.Count == 0)
        {
            TextRenderer.DrawText(g, "Training curves appear here.", Theme.Small, Rectangle.Round(plot), Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        float maxLoss = Math.Max(1e-3f, _points.Max(p => p.Loss));
        PointF P(float step, float value01) => new(plot.X + plot.Width * Math.Clamp(step / _maxStep, 0, 1), plot.Bottom - plot.Height * Math.Clamp(value01, 0, 1));
        if (_points.Count > 1)
        {
            using var lossPen = new Pen(Theme.Info, 2f);
            using var accPen = new Pen(Theme.Accent, 2f);
            g.DrawLines(lossPen, _points.Select(p => P(p.Step, p.Loss / maxLoss)).ToArray());
            g.DrawLines(accPen, _points.Select(p => P(p.Step, p.Accuracy)).ToArray());
        }
        var last = _points[^1];
        TextRenderer.DrawText(g, $"step {last.Step:0}  ·  loss {last.Loss:0.000}  ·  batch accuracy {last.Accuracy:P1}", Theme.Small,
            new Point((int)plot.X, (int)plot.Bottom + 4), Theme.Muted);
    }
}

/// <summary>Validation confusion matrix: rows are the true gesture, columns what HandGI predicted.</summary>
internal sealed class ConfusionView : Control
{
    private string[] _names = Array.Empty<string>();
    private int[][] _matrix = Array.Empty<int[]>();

    public ConfusionView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Input;
        Height = 170;
    }

    public void SetData(IEnumerable<string> names, int[][] matrix)
    {
        _names = names.ToArray();
        _matrix = matrix;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        int n = Math.Min(_names.Length, _matrix.Length);
        if (n == 0)
        {
            TextRenderer.DrawText(g, "After training, this shows which gestures HandGI confuses (rows: performed, columns: recognised).", Theme.Small,
                Rectangle.Inflate(ClientRectangle, -12, -12), Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }

        int label = LogicalToDeviceUnits(120);
        int top = LogicalToDeviceUnits(22);
        int cellW = Math.Max(24, (Width - label - 8) / n);
        int cellH = Math.Max(18, (Height - top - 6) / n);
        for (int j = 0; j < n; j++)
            TextRenderer.DrawText(g, Short(_names[j]), Theme.Caption, new Rectangle(label + j * cellW, 2, cellW, top - 4), Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
        for (int i = 0; i < n; i++)
        {
            int total = Math.Max(1, _matrix[i].Take(n).Sum());
            TextRenderer.DrawText(g, _names[i], Theme.Small, new Rectangle(4, top + i * cellH, label - 8, cellH), Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            for (int j = 0; j < n; j++)
            {
                int count = j < _matrix[i].Length ? _matrix[i][j] : 0;
                float share = count / (float)total;
                var baseColor = i == j ? Theme.Accent : Theme.Danger;
                int alpha = count == 0 ? 0 : (int)(40 + 200 * share);
                var cell = new Rectangle(label + j * cellW + 1, top + i * cellH + 1, cellW - 2, cellH - 2);
                using (var brush = new SolidBrush(Color.FromArgb(alpha, baseColor)))
                    g.FillRectangle(brush, cell);
                TextRenderer.DrawText(g, count.ToString(), Theme.Small, cell, count == 0 ? Theme.Border : Theme.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }

    private static string Short(string name) => name.Length <= 10 ? name : name[..9] + "…";
}
