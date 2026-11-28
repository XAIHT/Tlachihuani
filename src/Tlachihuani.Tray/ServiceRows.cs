// Tlachihuani — Angela López Mendoza · XAIHT
// tlachihuani:trace-off (paint and layout code; the panel logs its actions, not every repaint)
using System.Drawing.Drawing2D;

namespace Tlachihuani.Tray;

// How a service is doing, before any word is read: running, off, changing, or in trouble.
// Plain draws no light (a settings line under its service).
internal enum Mood { On, Off, Busy, Problem, Plain }

// The small light before a service's name: filled when it runs, a ring when it is off.
internal sealed class StatusLamp : Control
{
    private Mood mood = Mood.Busy;

    internal StatusLamp()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false; Dock = DockStyle.Fill; Margin = Padding.Empty;
        AccessibleRole = AccessibleRole.Graphic;
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal Mood Mood
    {
        get => mood;
        set { if (mood == value) return; mood = value; AccessibleName = value.ToString(); Invalidate(); }
    }

    internal static Color Ink(Mood mood) => mood switch
    {
        Mood.On => Studio.AccentHover, Mood.Problem => Studio.Danger, Mood.Busy => Studio.Body, _ => Studio.Muted
    };

    protected override void OnPaint(PaintEventArgs e)
    {
        if (mood == Mood.Plain) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float size = LogicalToDeviceUnits(10), stroke = Math.Max(1.5f, LogicalToDeviceUnits(2) * 0.8f);
        var dot = new RectangleF(LogicalToDeviceUnits(3), (Height - size) / 2f, size, size);
        var color = mood == Mood.On ? Studio.Accent : Ink(mood);
        if (mood == Mood.Off)
        {
            dot.Inflate(-stroke / 2, -stroke / 2);
            using var pen = new Pen(color, stroke);
            g.DrawEllipse(pen, dot);
            return;
        }
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, dot);
        if (mood == Mood.On)
        {
            // A soft halo, so "running" reads at a glance even on a dim screen.
            using var halo = new Pen(Color.FromArgb(70, color), stroke);
            var ring = dot; ring.Inflate(stroke * 1.4f, stroke * 1.4f);
            g.DrawEllipse(halo, ring);
        }
    }
}

// One line of the Services panel: the light, the name, one status sentence in plain words, and the
// buttons, always in the same places. Every service uses it, so no section looks unlike another.
internal sealed class ServiceLine
{
    private readonly ToolTip tips;
    private readonly bool lamp;
    internal StatusLamp Lamp { get; } = new();
    internal Label Title { get; }
    internal Label Status { get; }

    internal ServiceLine(string title, string status, ToolTip tips, bool lamp = true)
    {
        this.tips = tips; this.lamp = lamp;
        Title = Studio.Label(title, Studio.Strong, Studio.Text);
        Status = Studio.Label(status, Studio.Small, Studio.Muted);
        Lamp.Mood = lamp ? Mood.Busy : Mood.Plain;
        Status.AccessibleName = title + " status";
        tips.SetToolTip(Status, status);
    }

    internal void Show((Mood Mood, string Text) look) => Show(look.Mood, look.Text);

    internal void Show(Mood mood, string text)
    {
        if (lamp) Lamp.Mood = mood;
        if (Status.Text != text) { Status.Text = text; tips.SetToolTip(Status, text); }
        Status.ForeColor = mood == Mood.Plain ? Studio.Muted : StatusLamp.Ink(mood);
    }

    // Name and status on the left, the buttons on the right, centered on the line.
    internal Control Build(params Control[] actions)
    {
        var text = lamp ? Studio.Grid([20, -1], [-1, 22, 20, -1]) : Studio.Grid([-1], [-1, 22, 20, -1]);
        int column = 0;
        if (lamp) { text.Controls.Add(Lamp, 0, 1); column = 1; }
        text.Controls.Add(Title, column, 1); text.Controls.Add(Status, column, 2);
        var row = Studio.Grid([-1, actions.Sum(x => x.Width + x.Margin.Horizontal)], [-1]);
        row.Controls.Add(text, 0, 0);
        var strip = Studio.Actions(actions);
        strip.Dock = DockStyle.None; strip.Anchor = AnchorStyles.Right;
        strip.AutoSize = true; strip.Margin = Padding.Empty;
        row.Controls.Add(strip, 1, 0);
        return row;
    }
}
