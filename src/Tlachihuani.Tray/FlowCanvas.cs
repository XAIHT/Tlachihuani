// Tlachihuani — Angela López Mendoza · XAIHT
// tlachihuani:trace-off (paints every node and wire on every repaint)
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tlachihuani.FlowPills;

namespace Tlachihuani.Tray;

// One flow, drawn as Tlamatini drew it. A .flw shows the Agentic Control Panel's agents: their
// gradients, connector triangles (grey on an agent that never starts the next one), LEDs and
// curved white wires. A .fpmt shows the Prompt Flow Panel's operation shapes, Yes/No outputs
// and speech-bubble comments. Read-only: Ctrl+wheel zooms at the pointer, dragging or the wheel
// pans, a double-click fits, and a click selects a node. The followed run colors it live.
internal sealed class FlowCanvas : Control
{
    private static readonly Color Ground = Hex("#161d28"), Dots = Hex("#263142"), Wire = Color.White, LiveWire = Hex("#22c55e");
    private static readonly Color Gold = Hex("#ffcc00"), Green = Hex("#22c55e"), Red = Hex("#ef4444"), Amber = Hex("#f59e0b"), Grey = Hex("#8b94a3"), Yellow = Hex("#facc15");
    private static readonly Color Muted = Hex("#555555");

    private sealed record Shape(Color Stroke, Color Fill, PointF[] Outline, PointF In, PointF Out, PointF Yes, PointF No);

    // The Prompt Flow Panel's operations (prompt-flow-panel-model.js), in its 200 x 128 frame.
    private static readonly Dictionary<string, Shape> Shapes = new(StringComparer.Ordinal)
    {
        ["prompt"] = new(Hex("#83c0f3"), Hex("#304965"), Box(4, 20, 196, 108), new(4, 64), new(196, 64), default, default),
        ["programmed_prompt"] = new(Hex("#d7b9f5"), Hex("#4d3867"), Box(4, 20, 196, 108), new(4, 64), new(196, 64), default, default),
        ["decision"] = new(Hex("#f0cf72"), Hex("#61512e"), [new(100, 4), new(196, 64), new(100, 124), new(4, 64)],
            new(4, 64), default, new(161.184f, 42.24f), new(163.232f, 84.48f)),
        ["feed_embeddings"] = new(Hex("#77d8b2"), Hex("#285849"), [new(100, 4), new(196, 120), new(4, 120)],
            new(100 - 96 * 60 / 116f, 64), new(100 + 96 * 60 / 116f, 64), default, default),
        ["flush_embeddings"] = new(Hex("#f0a88b"), Hex("#684237"), [new(4, 8), new(196, 8), new(100, 124)],
            new(4 + 96 * 56 / 116f, 64), new(196 - 96 * 56 / 116f, 64), default, default),
        ["clean_history"] = new(Hex("#90cddc"), Hex("#315665"), [new(4, 14), new(196, 14), new(160, 114), new(40, 114)],
            new(22, 64), new(178, 64), default, default),
        ["user_input"] = new(Hex("#eaaed6"), Hex("#653d59"), [new(52, 4), new(100, 38), new(148, 4), new(148, 88), new(100, 124), new(52, 88)],
            new(52, 64), new(148, 64), default, default),
        ["terminater"] = new(Hex("#7ee8a6"), Hex("#1e4d38"), [new(52, 20), new(196, 20), new(148, 108), new(4, 108)],
            new(28, 64), new(172, 64), default, default),
    };

    private readonly Dictionary<(string, float, FontStyle), Font> fonts = [];
    private readonly ToolTip tip = new() { InitialDelay = 450, ReshowDelay = 150, AutoPopDelay = 12000 };
    private readonly System.Windows.Forms.Timer flasher = new() { Interval = 70 };
    private FlowDocument? document;
    private FlowRun? run;
    private float zoom = 1f;
    private PointF offset = new(20, 20);
    private string selected = "", flashing = "", hovered = "";
    private DateTime flashUntil;
    private Point? dragFrom;
    private PointF dragOffset;
    private bool fitPending;

    internal event Action<string>? NodeClicked;

    internal FlowCanvas()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
            | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Ground;
        TabStop = true;
        AccessibleName = "Flow drawing";
        AccessibleRole = AccessibleRole.Graphic;
        flasher.Tick += (_, _) =>
        {
            if (DateTime.UtcNow > flashUntil) { flashing = ""; flasher.Stop(); }
            Invalidate();
        };
    }

    internal FlowDocument? Flow => document;
    internal FlowRun? Followed => run;
    internal float ZoomFactor => zoom;

    internal void Display(FlowDocument flow)
    {
        document = flow;
        run = null;
        selected = "";
        fitPending = true;
        if (Width > 60 && Height > 60) Fit();
        Invalidate();
    }

    internal void Follow(FlowRun? followed)
    {
        run = followed;
        Invalidate();
    }

    internal void Highlight(string nodeId, bool reveal)
    {
        selected = nodeId;
        if (reveal) Reveal(nodeId);
        flashing = nodeId;
        flashUntil = DateTime.UtcNow.AddMilliseconds(1100);
        flasher.Start();
        Invalidate();
    }

    internal void Fit()
    {
        fitPending = false;
        if (document is null || document.Nodes.Count == 0 || Width < 60 || Height < 60)
        {
            zoom = 1;
            offset = new PointF(20, 20);
            Invalidate();
            return;
        }
        var bounds = ModelBounds();
        bounds.Inflate(40, 40);
        zoom = Math.Clamp(Math.Min(Width / bounds.Width, Height / bounds.Height), 0.12f, 1.5f);
        offset = new PointF((Width - bounds.Width * zoom) / 2 - bounds.Left * zoom, (Height - bounds.Height * zoom) / 2 - bounds.Top * zoom);
        Invalidate();
    }

    internal void ZoomBy(float factor) => ZoomAt(new PointF(Width / 2f, Height / 2f), factor);

    private void ZoomAt(PointF screen, float factor)
    {
        float next = Math.Clamp(zoom * factor, 0.1f, 4f);
        var model = new PointF((screen.X - offset.X) / zoom, (screen.Y - offset.Y) / zoom);
        zoom = next;
        offset = new PointF(screen.X - model.X * zoom, screen.Y - model.Y * zoom);
        Invalidate();
    }

    private void Reveal(string nodeId)
    {
        if (document?.Find(nodeId) is not { } node) return;
        float x = (float)(node.X + node.Width / 2) * zoom + offset.X, y = (float)(node.Y + node.Height / 2) * zoom + offset.Y;
        if (x < 40 || y < 40 || x > Width - 40 || y > Height - 40)
            offset = new PointF(offset.X + Width / 2f - x, offset.Y + Height / 2f - y);
    }

    private RectangleF ModelBounds()
    {
        float left = float.MaxValue, top = float.MaxValue, right = float.MinValue, bottom = float.MinValue;
        foreach (var node in document!.Nodes)
        {
            left = Math.Min(left, (float)node.X - 12);
            top = Math.Min(top, (float)node.Y - 22);
            right = Math.Max(right, (float)(node.X + node.Width) + 12);
            bottom = Math.Max(bottom, (float)(node.Y + node.Height) + 4);
        }
        return RectangleF.FromLTRB(left, top, right, bottom);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (fitPending && Width > 60 && Height > 60) Fit();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Ground);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        if (document is null)
        {
            Centered(g, "Open a Tlamatini flow (.flw) or flow pill (.fpmt), or drop it here.");
            return;
        }
        PaintDots(g);
        var state = g.Save();
        g.TranslateTransform(offset.X, offset.Y);
        g.ScaleTransform(zoom, zoom);
        if (document.Kind == FlowKind.Agents) PaintAgents(g, document);
        else PaintPill(g, document);
        g.Restore(state);
        PaintHeadline(g);
    }

    private void PaintDots(Graphics g)
    {
        float step = 20 * zoom;
        if (step < 9) return;
        using var brush = new SolidBrush(Dots);
        float startX = offset.X % step, startY = offset.Y % step;
        for (float x = startX; x < Width; x += step)
            for (float y = startY; y < Height; y += step)
                g.FillRectangle(brush, x, y, 1.4f, 1.4f);
    }

    // ---- .flw: the Agentic Control Panel ------------------------------------------------

    private void PaintAgents(Graphics g, FlowDocument flow)
    {
        var palette = AcpPalette.Shared;
        using (var wire = new Pen(Wire, 2))
        using (var live = new Pen(LiveWire, 2.6f))
        {
            foreach (var edge in flow.Edges)
            {
                if (flow.Find(edge.Source) is not { } from || flow.Find(edge.Target) is not { } to) continue;
                var start = new PointF((float)(from.X + from.Width) + 5, (float)from.Y + (float)from.Height * Fraction(AcpPalette.Outputs(LookName(from)), edge.OutputSlot));
                var end = new PointF((float)to.X - 5, (float)to.Y + (float)to.Height * Fraction(palette.Inputs(LookName(to)), edge.InputSlot));
                bool fired = run is not null && run.NodeStateOf(from.Id) != NodeState.Idle && run.NodeStateOf(to.Id) != NodeState.Idle;
                Curve(g, fired ? live : wire, start, end);
            }
        }
        foreach (var node in flow.Nodes) PaintAgent(g, node, palette);
    }

    private void PaintAgent(Graphics g, FlowNode node, AcpPalette palette)
    {
        string name = LookName(node);
        var look = palette.LookFor(name);
        var box = new RectangleF((float)node.X, (float)node.Y, (float)node.Width, (float)node.Height);
        Halo(g, node.Id, box, null);
        using (var path = Rounded(box, 4))
        {
            using (var fill = Gradient(box, look)) g.FillPath(fill, path);
            using var border = new Pen(Color.White, 1);
            g.DrawPath(border, path);
        }
        using (var ink = new SolidBrush(Argb(look.Text)))
        using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisWord })
            g.DrawString(node.Label, FontFor("Segoe UI", 12, FontStyle.Bold), ink, RectangleF.Inflate(box, -8, -6), format);

        var state = run?.NodeStateOf(node.Id) ?? NodeState.Idle;
        var led = new RectangleF(box.Right - 14, box.Top + 4, 10, 10);
        using (var lamp = new SolidBrush(state switch
        {
            NodeState.Running => Green, NodeState.Done or NodeState.Failed => Red,
            NodeState.Paused => Yellow, NodeState.Waiting => Amber, _ => Grey,
        }))
            g.FillEllipse(lamp, led);
        using (var rim = new Pen(Color.FromArgb(160, 0, 0, 0), 1)) g.DrawEllipse(rim, led);

        int inputs = palette.Inputs(name), outputs = AcpPalette.Outputs(name);
        using var white = new SolidBrush(Color.White);
        using var grey = new SolidBrush(Muted);
        foreach (float at in Positions(inputs)) Triangle(g, white, box.Left, box.Top + box.Height * at);
        var outputBrush = palette.NeverStartsOthers(name) ? grey : white;
        var positions = Positions(outputs);
        var (first, second) = AcpPalette.OutputLabels(name);
        for (int i = 0; i < positions.Length; i++)
        {
            float y = box.Top + box.Height * positions[i];
            Triangle(g, outputBrush, box.Right + 10, y);
            if (positions.Length == 2)
                using (var label = new SolidBrush(Color.FromArgb(220, 255, 255, 255)))
                    g.DrawString(i == 0 ? first : second, FontFor("Segoe UI", 9, FontStyle.Bold), label, box.Right + 11, y - 15);
        }
    }

    private static float[] Positions(int count) => count switch { 0 => [], 2 => [0.33f, 0.66f], _ => [0.5f] };

    // The ACP's connector anchors: the middle, or 33% / 66% for the two connectors of Asker,
    // Forker, Counter (outputs) and AND / OR (inputs).
    private static float Fraction(int connectors, int slot) => connectors == 2 ? (slot == 2 ? 0.66f : 0.33f) : 0.5f;

    private static string LookName(FlowNode node) => node.Label.Length > 0 ? node.Label : node.Type;

    // ---- .fpmt: the Prompt Flow Panel ---------------------------------------------------

    private void PaintPill(Graphics g, FlowDocument flow)
    {
        using (var wire = new Pen(Wire, 2))
        using (var live = new Pen(LiveWire, 2.6f))
        {
            foreach (var edge in flow.Edges)
            {
                if (flow.Find(edge.Source) is not { } from || flow.Find(edge.Target) is not { } to) continue;
                if (!Shapes.TryGetValue(from.Type, out var source) || !Shapes.TryGetValue(to.Type, out var target)) continue;
                PointF port = edge.Branch switch { "yes" => source.Yes, "no" => source.No, _ => source.Out };
                var start = new PointF((float)from.X + port.X + 5, (float)from.Y + port.Y);
                var end = new PointF((float)to.X + target.In.X - 5, (float)to.Y + target.In.Y);
                Curve(g, run?.WasTraversed(edge.Id) == true ? live : wire, start, end);
            }
        }
        foreach (var node in flow.Nodes)
        {
            if (node.IsComment) PaintComment(g, node);
            else if (Shapes.TryGetValue(node.Type, out var shape)) PaintOperation(g, node, shape, node.Id == flow.Start);
        }
    }

    private void PaintOperation(Graphics g, FlowNode node, Shape shape, bool start)
    {
        var points = shape.Outline.Select(p => new PointF((float)node.X + p.X, (float)node.Y + p.Y)).ToArray();
        using var path = new GraphicsPath();
        path.AddPolygon(points);
        var box = new RectangleF((float)node.X, (float)node.Y, (float)node.Width, (float)node.Height);
        Halo(g, node.Id, box, path);
        using (var fill = new SolidBrush(shape.Fill)) g.FillPath(fill, path);
        var state = run?.NodeStateOf(node.Id) ?? NodeState.Idle;
        Color stroke = state switch
        {
            NodeState.Running => Gold, NodeState.Done => Green, NodeState.Failed => Red, NodeState.Waiting => Amber,
            NodeState.Paused => Yellow, _ => shape.Stroke,
        };
        using (var pen = new Pen(stroke, state == NodeState.Idle ? 2 : 3.2f)) g.DrawPath(pen, path);
        var text = node.Type switch
        {
            "decision" => RectangleF.FromLTRB(box.Left + 46, box.Top + 30, box.Right - 46, box.Bottom - 30),
            "feed_embeddings" => RectangleF.FromLTRB(box.Left + 50, box.Top + 54, box.Right - 50, box.Bottom - 10),
            "flush_embeddings" => RectangleF.FromLTRB(box.Left + 50, box.Top + 12, box.Right - 50, box.Top + 74),
            "user_input" => RectangleF.FromLTRB(box.Left + 56, box.Top + 40, box.Right - 56, box.Bottom - 30),
            _ => RectangleF.FromLTRB(box.Left + 26, box.Top + 26, box.Right - 26, box.Bottom - 24),
        };
        using (var ink = new SolidBrush(Color.White))
        using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisWord })
            g.DrawString(node.Label, FontFor("Segoe UI", 13, FontStyle.Bold), ink, text, format);

        using var white = new SolidBrush(Color.White);
        Triangle(g, white, (float)node.X + shape.In.X, (float)node.Y + shape.In.Y);
        if (node.Type == "decision")
        {
            foreach (var (port, mark) in new[] { (shape.Yes, "Y"), (shape.No, "N") })
            {
                Triangle(g, white, (float)node.X + port.X + 10, (float)node.Y + port.Y);
                g.DrawString(mark, FontFor("Segoe UI", 10, FontStyle.Bold), white, (float)node.X + port.X + 12, (float)node.Y + port.Y - 16);
            }
        }
        else Triangle(g, white, (float)node.X + shape.Out.X + 10, (float)node.Y + shape.Out.Y);

        if (start)
        {
            var badge = new RectangleF((float)node.X + 70, (float)node.Y - 18, 60, 17);
            using var pill = Rounded(badge, 8);
            using (var accent = new SolidBrush(Studio.Accent)) g.FillPath(accent, pill);
            using var ink = new SolidBrush(Studio.AccentInk);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("START", FontFor("Segoe UI", 10, FontStyle.Bold), ink, badge, format);
        }
    }

    // A static comment: its speech bubble, and its text in the styles of each formatted run.
    private void PaintComment(Graphics g, FlowNode node)
    {
        var config = node.Config;
        var box = new RectangleF((float)node.X, (float)node.Y, (float)node.Width, Math.Max(40, (float)node.Height - 18));
        using var path = Rounded(box, 14);
        path.FillMode = FillMode.Winding;
        path.AddPolygon(new PointF[] { new(box.Left + 30, box.Bottom - 2), new(box.Left + 22, box.Bottom + 18), new(box.Left + 56, box.Bottom - 2) });
        Halo(g, node.Id, box, path);
        var fill = Hex(Str(config["color"]), "#fbcfe8");
        using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
        using (var outline = new Pen(ControlPaint.Dark(fill, 0.25f), 1.5f)) g.DrawPath(outline, path);
        PaintRuns(g, config, RectangleF.Inflate(box, -14, -12));
    }

    private void PaintRuns(Graphics g, JsonObject config, RectangleF area)
    {
        var words = new List<(string Text, Font Font, Color Color, bool Break)>();
        IEnumerable<JsonNode?> runs = config["runs"] is JsonArray array && array.Count > 0 ? array
            : [new JsonObject { ["text"] = Str(config["text"]) }];
        foreach (var item in runs)
        {
            if (item is not JsonObject part) continue;
            string family = First(Str(part["font_family"]), Str(config["font_family"]), "Segoe UI");
            float size = (float)Math.Clamp(Num(part["font_size"], Num(config["font_size"], 16)), 8, 64);
            var style = (Bool(part["bold"]) ?? Bool(config["bold"]) ?? false ? FontStyle.Bold : FontStyle.Regular)
                | (Bool(part["italic"]) ?? Bool(config["italic"]) ?? false ? FontStyle.Italic : FontStyle.Regular)
                | (Bool(part["underline"]) ?? Bool(config["underline"]) ?? false ? FontStyle.Underline : FontStyle.Regular);
            var font = FontFor(family, size, style);
            var color = Hex(First(Str(part["text_color"]), Str(config["text_color"])), "#202938");
            string text = Str(part["text"]).Replace("\r\n", "\n", StringComparison.Ordinal);
            var lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) words.Add(("", font, color, true));
                foreach (var word in Words(lines[i])) words.Add((word, font, color, false));
            }
        }
        string align = Str(config["align"]);
        float y = area.Top;
        var line = new List<(string Text, Font Font, Color Color, float Width)>();
        float lineWidth = 0, lineHeight = 0;
        using var format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.MeasureTrailingSpaces };
        void Flush()
        {
            if (line.Count == 0) { y += lineHeight > 0 ? lineHeight : 16; return; }
            float x = align switch { "center" => area.Left + (area.Width - lineWidth) / 2, "right" => area.Right - lineWidth, _ => area.Left };
            foreach (var (text, font, color, width) in line)
            {
                using var brush = new SolidBrush(color);
                g.DrawString(text, font, brush, x, y, format);
                x += width;
            }
            y += lineHeight * 1.22f;
            line.Clear();
            lineWidth = lineHeight = 0;
        }
        foreach (var (text, font, color, isBreak) in words)
        {
            if (y > area.Bottom) break;
            if (isBreak) { Flush(); continue; }
            float width = g.MeasureString(text, font, PointF.Empty, format).Width;
            if (line.Count > 0 && lineWidth + width > area.Width && text.Trim().Length > 0) Flush();
            line.Add((text, font, color, width));
            lineWidth += width;
            lineHeight = Math.Max(lineHeight, font.GetHeight(g));
        }
        if (y <= area.Bottom) Flush();
    }

    // Words with their trailing spaces, so a wrapped line keeps the author's spacing.
    private static IEnumerable<string> Words(string text)
    {
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != ' ') continue;
            int end = i;
            while (end + 1 < text.Length && text[end + 1] == ' ') end++;
            yield return text[start..(end + 1)];
            start = end + 1;
            i = end;
        }
        if (start < text.Length) yield return text[start..];
    }

    // ---- shared drawing --------------------------------------------------------------------

    private void Halo(Graphics g, string id, RectangleF box, GraphicsPath? path)
    {
        bool flash = flashing == id && (DateTime.UtcNow.Millisecond / 140) % 2 == 0;
        if (selected != id && !flash) return;
        using var glow = new Pen(Color.FromArgb(flash ? 230 : 150, Gold), flash ? 9 : 7) { LineJoin = LineJoin.Round };
        if (path is not null) g.DrawPath(glow, path);
        else using (var rounded = Rounded(RectangleF.Inflate(box, 2, 2), 6)) g.DrawPath(glow, rounded);
    }

    private static void Curve(Graphics g, Pen pen, PointF from, PointF to)
    {
        float d = Math.Abs(to.X - from.X) * 0.5f;
        g.DrawBezier(pen, from, new PointF(from.X + d, from.Y), new PointF(to.X - d, to.Y), to);
    }

    // A 10 x 16 connector triangle pointing right, its tip at (tipX, y).
    private static void Triangle(Graphics g, Brush brush, float tipX, float y)
        => g.FillPolygon(brush, new PointF[] { new(tipX - 10, y - 8), new(tipX, y), new(tipX - 10, y + 8) });

    private static LinearGradientBrush Gradient(RectangleF box, AgentLook look)
    {
        var stops = look.Stops.OrderBy(stop => stop.Position).ToList();
        var brush = new LinearGradientBrush(box, Argb(stops[0].Argb), Argb(stops[^1].Argb), look.Angle - 90f, true);
        var colors = new List<Color>();
        var positions = new List<float>();
        if (stops[0].Position > 0) { colors.Add(Argb(stops[0].Argb)); positions.Add(0); }
        foreach (var stop in stops)
        {
            float at = positions.Count == 0 ? 0 : Math.Min(1, Math.Max(stop.Position, positions[^1] + 0.0001f));
            colors.Add(Argb(stop.Argb));
            positions.Add(at);
        }
        if (positions[^1] < 1) { colors.Add(colors[^1]); positions.Add(1); }
        positions[0] = 0;
        positions[^1] = 1;
        brush.InterpolationColors = new ColorBlend { Colors = [.. colors], Positions = [.. positions] };
        return brush;
    }

    private static GraphicsPath Rounded(RectangleF box, float radius)
    {
        var path = new GraphicsPath();
        path.AddRoundedRectangle(box, new SizeF(radius, radius));
        return path;
    }

    private void PaintHeadline(Graphics g)
    {
        string text = run is null ? $"{document!.KindLabel} · {document.Nodes.Count} nodes · not running"
            : run.Status.StartsWith(run.State.ToString(), StringComparison.Ordinal) ? $"Run {run.Id} · {run.Status}" : $"Run {run.Id} · {run.State} · {run.Status}";
        if (run is AgentFlowRun agents && agents.Alert.Length > 0) text += "   ·   FlowHypervisor: " + agents.Alert;
        var font = Studio.Small;
        var size = g.MeasureString(text, font);
        var box = new RectangleF(10, 8, Math.Min(Width - 20, size.Width + 18), size.Height + 8);
        using (var back = new SolidBrush(Color.FromArgb(210, Studio.Chrome))) g.FillRectangle(back, box);
        using var ink = new SolidBrush(run?.State == RunState.Failed ? Studio.Danger : Studio.Body);
        using var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap, LineAlignment = StringAlignment.Center };
        g.DrawString(text, font, ink, RectangleF.Inflate(box, -9, 0), format);
    }

    private void Centered(Graphics g, string text)
    {
        using var ink = new SolidBrush(Studio.Muted);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(text, Studio.Regular, ink, ClientRectangle, format);
    }

    private Font FontFor(string family, float pixels, FontStyle style)
    {
        var key = (family, MathF.Round(pixels * 2) / 2, style);
        if (!fonts.TryGetValue(key, out var font))
        {
            try { font = new Font(family, key.Item2, style, GraphicsUnit.Pixel); }
            catch (ArgumentException) { font = new Font("Segoe UI", key.Item2, style, GraphicsUnit.Pixel); }
            fonts[key] = font;
        }
        return font;
    }

    // ---- input -----------------------------------------------------------------------------

    private FlowNode? NodeAt(Point point)
    {
        if (document is null) return null;
        float x = (point.X - offset.X) / zoom, y = (point.Y - offset.Y) / zoom;
        for (int i = document.Nodes.Count - 1; i >= 0; i--)
        {
            var node = document.Nodes[i];
            if (x >= node.X && x <= node.X + node.Width && y >= node.Y && y <= node.Y + node.Height) return node;
        }
        return null;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        if (e.Button == MouseButtons.Left && NodeAt(e.Location) is { } node)
        {
            selected = node.Id;
            Invalidate();
            NodeClicked?.Invoke(node.Id);
        }
        else if (e.Button is MouseButtons.Left or MouseButtons.Middle)
        {
            dragFrom = e.Location;
            dragOffset = offset;
            Cursor = Cursors.SizeAll;
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (dragFrom is { } from)
        {
            offset = new PointF(dragOffset.X + e.X - from.X, dragOffset.Y + e.Y - from.Y);
            Invalidate();
        }
        else
        {
            var node = NodeAt(e.Location);
            string id = node?.Id ?? "";
            if (id != hovered)
            {
                hovered = id;
                Cursor = node is null ? Cursors.Default : Cursors.Hand;
                tip.SetToolTip(this, node is null ? "" : Describe(node));
            }
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        dragFrom = null;
        Cursor = hovered.Length > 0 ? Cursors.Hand : Cursors.Default;
        base.OnMouseUp(e);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        if (NodeAt(e.Location) is null) Fit();
        base.OnMouseDoubleClick(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if ((ModifierKeys & Keys.Control) != 0) ZoomAt(e.Location, e.Delta > 0 ? 1.12f : 1 / 1.12f);
        else if ((ModifierKeys & Keys.Shift) != 0) { offset = new PointF(offset.X + e.Delta / 2f, offset.Y); Invalidate(); }
        else { offset = new PointF(offset.X, offset.Y + e.Delta / 2f); Invalidate(); }
        base.OnMouseWheel(e);
    }

    protected override bool IsInputKey(Keys keyData)
        => (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Add or Keys.Oemplus: ZoomBy(1.15f); break;
            case Keys.Subtract or Keys.OemMinus: ZoomBy(1 / 1.15f); break;
            case Keys.D0 or Keys.NumPad0: Fit(); break;
            case Keys.Left: offset = new PointF(offset.X + 40, offset.Y); Invalidate(); break;
            case Keys.Right: offset = new PointF(offset.X - 40, offset.Y); Invalidate(); break;
            case Keys.Up: offset = new PointF(offset.X, offset.Y + 40); Invalidate(); break;
            case Keys.Down: offset = new PointF(offset.X, offset.Y - 40); Invalidate(); break;
        }
        base.OnKeyDown(e);
    }

    private string Describe(FlowNode node)
    {
        var lines = new List<string> { node.Label, node.Type.Replace('_', ' ') + (node.PoolName.Length > 0 ? " · " + node.PoolName : "") };
        if (node.Purpose.Length > 0) lines.Add(node.Purpose);
        string text = Str(node.Config["text"]);
        if (text.Length > 0) lines.Add(text.Length > 240 ? text[..240] + "…" : text);
        if (run is not null) lines.Add("State: " + run.NodeStateOf(node.Id));
        return string.Join("\n", lines);
    }

    // ---- values ----------------------------------------------------------------------------

    private static PointF[] Box(float left, float top, float right, float bottom) => [new(left, top), new(right, top), new(right, bottom), new(left, bottom)];
    private static Color Hex(string hex) => ColorTranslator.FromHtml(hex);
    private static Color Hex(string hex, string fallback) => AcpPalette.TryColor(hex, out uint argb) ? Argb(argb) : Hex(fallback);
    private static Color Argb(uint argb) => Color.FromArgb(unchecked((int)argb));
    private static string First(params string[] values) => values.FirstOrDefault(value => value.Length > 0) ?? "";
    private static string Str(JsonNode? node) => node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : "";
    private static double Num(JsonNode? node, double fallback) => node is JsonValue value && value.GetValueKind() == JsonValueKind.Number ? value.GetValue<double>() : fallback;
    private static bool? Bool(JsonNode? node) => node is JsonValue value ? value.GetValueKind() switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null } : null;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            flasher.Dispose();
            tip.Dispose();
            foreach (var font in fonts.Values) font.Dispose();
            fonts.Clear();
        }
        base.Dispose(disposing);
    }
}
