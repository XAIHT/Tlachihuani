// HandGI expert window (Angela López Mendoza · XAIHT), part of Tlachihuani. tlachihuani:trace-off (repaints and polls the engine every 15 ms; its actions are written to Tlachihuani.log through the HandGI log bridge)
using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace HandGI.App;

/// <summary>The dark HandGI look: colours, fonts and the small set of themed control builders the stages use.</summary>
internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(14, 16, 22);
    public static readonly Color Surface = Color.FromArgb(22, 25, 33);
    public static readonly Color SurfaceHigh = Color.FromArgb(33, 37, 49);
    public static readonly Color Input = Color.FromArgb(38, 43, 56);
    public static readonly Color Border = Color.FromArgb(52, 58, 74);
    public static readonly Color Text = Color.FromArgb(234, 238, 246);
    public static readonly Color Muted = Color.FromArgb(146, 156, 176);
    public static readonly Color Accent = Color.FromArgb(46, 204, 113);
    public static readonly Color Info = Color.FromArgb(77, 166, 255);
    public static readonly Color Warn = Color.FromArgb(255, 184, 77);
    public static readonly Color Danger = Color.FromArgb(255, 94, 94);
    public static readonly Color PreviewBack = Color.FromArgb(8, 9, 12);

    public static readonly Font Body = new("Segoe UI", 9.75f);
    public static readonly Font BodyBold = new("Segoe UI Semibold", 9.75f);
    public static readonly Font Small = new("Segoe UI", 9f);
    public static readonly Font Caption = new("Segoe UI Semibold", 8.25f);
    public static readonly Font Heading = new("Segoe UI Semibold", 12f);
    public static readonly Font Title = new("Segoe UI Semibold", 17f);
    public static readonly Font Big = new("Segoe UI Semibold", 24f);
    public static readonly Font Mono = new("Consolas", 9f);

    /// <summary>One colour per gesture: Google's seven first (thumb up, thumb down, fist, palm, pointing, victory, I love you), then up to seven of yours.</summary>
    private static readonly Color[] ClassColors =
    {
        Color.FromArgb(46, 204, 113),
        Color.FromArgb(255, 94, 94),
        Color.FromArgb(255, 184, 77),
        Color.FromArgb(77, 166, 255),
        Color.FromArgb(0, 210, 210),
        Color.FromArgb(186, 120, 255),
        Color.FromArgb(255, 120, 200),
        Color.FromArgb(240, 220, 80),
        Color.FromArgb(160, 230, 60),
        Color.FromArgb(60, 190, 160),
        Color.FromArgb(255, 140, 110),
        Color.FromArgb(140, 140, 255),
        Color.FromArgb(220, 170, 60),
        Color.FromArgb(230, 90, 150),
    };

    public static Color ClassColor(int index) => ClassColors[Math.Abs(index) % ClassColors.Length];

    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Max(1f, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Asks Windows 10 2004+/11 to draw the title bar dark (ignored where unsupported).</summary>
    public static void DarkTitleBar(IntPtr hwnd)
    {
        try
        {
            int on = 1;
            DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }
}

internal static class Ui
{
    public static Label Label(string text, Font? font = null, Color? color = null) => new()
    {
        Text = text,
        AutoSize = true,
        Font = font ?? Theme.Body,
        ForeColor = color ?? Theme.Text,
        Margin = new Padding(0, 7, 8, 4),
    };

    public static Label Field(string text, int width = 96) => new()
    {
        Text = text,
        AutoSize = false,
        Width = width,
        Height = 24,
        Font = Theme.Body,
        ForeColor = Theme.Muted,
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(0, 4, 6, 4),
    };

    public static Button Button(string text, EventHandler onClick, bool primary = false)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Theme.Accent : Theme.SurfaceHigh,
            ForeColor = primary ? Color.FromArgb(10, 20, 14) : Theme.Text,
            Font = primary ? Theme.BodyBold : Theme.Body,
            Padding = new Padding(10, 3, 10, 3),
            Margin = new Padding(0, 3, 8, 3),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };
        b.FlatAppearance.BorderColor = primary ? Theme.Accent : Theme.Border;
        b.FlatAppearance.MouseOverBackColor = primary ? ControlPaint.Light(Theme.Accent, 0.2f) : Theme.Input;
        b.FlatAppearance.MouseDownBackColor = primary ? ControlPaint.Dark(Theme.Accent, 0.1f) : Theme.Border;
        b.Click += onClick;
        return b;
    }

    public static ComboBox Combo(int width) => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = width,
        BackColor = Theme.Input,
        ForeColor = Theme.Text,
        FlatStyle = FlatStyle.Flat,
        Font = Theme.Body,
        Margin = new Padding(0, 3, 8, 3),
    };

    public static CheckBox Check(string text, bool value) => new()
    {
        Text = text,
        Checked = value,
        AutoSize = true,
        ForeColor = Theme.Text,
        Font = Theme.Body,
        Margin = new Padding(0, 5, 16, 3),
    };

    public static NumericUpDown Number(decimal min, decimal max, decimal value, decimal step, int decimals = 0, int width = 84) => new()
    {
        Minimum = min,
        Maximum = max,
        Value = Math.Clamp(value, min, max),
        Increment = step,
        DecimalPlaces = decimals,
        Width = width,
        BackColor = Theme.Input,
        ForeColor = Theme.Text,
        BorderStyle = BorderStyle.FixedSingle,
        Font = Theme.Body,
        Margin = new Padding(0, 3, 12, 3),
    };

    public static TextBox TextBox(string text, int width) => new()
    {
        Text = text,
        Width = width,
        BackColor = Theme.Input,
        ForeColor = Theme.Text,
        BorderStyle = BorderStyle.FixedSingle,
        Font = Theme.Body,
        Margin = new Padding(0, 3, 8, 3),
    };

    public static ProgressBar Progress() => new()
    {
        Height = 6,
        Style = ProgressBarStyle.Continuous,
        Minimum = 0,
        Maximum = 1000,
        Margin = new Padding(0, 6, 0, 6),
    };

    /// <summary>A horizontal row that wraps when the stage is narrow.</summary>
    public static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 1, 0, 1),
            Padding = Padding.Empty,
            BackColor = Color.Transparent,
        };
        row.Controls.AddRange(controls);
        return row;
    }
}

/// <summary>A word-wrapping label for explanations (wraps to the column width inside a TableLayoutPanel).</summary>
internal sealed class WrapLabel : Label
{
    public WrapLabel(string text, Color? color = null, Font? font = null)
    {
        Text = text;
        AutoSize = true;
        ForeColor = color ?? Theme.Muted;
        Font = font ?? Theme.Small;
        Margin = new Padding(0, 2, 0, 6);
    }
}

/// <summary>A titled, bordered section of a stage.</summary>
internal sealed class Card : TableLayoutPanel
{
    public Card(string title)
    {
        ColumnCount = 1;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = Theme.Surface;
        Padding = new Padding(14, 10, 14, 10);
        Margin = new Padding(0, 4, 0, 10);
        DoubleBuffered = true;
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(new Label
        {
            Text = title.ToUpperInvariant(),
            Font = Theme.Caption,
            ForeColor = Theme.Muted,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6),
        });
    }

    /// <summary>Adds a control; stretched controls take the full width (wrapping labels, bars, charts).</summary>
    public T Add<T>(T control, bool stretch = false) where T : Control
    {
        control.Anchor = stretch ? AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top : AnchorStyles.Left | AnchorStyles.Top;
        Controls.Add(control);
        return control;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}

/// <summary>Left navigation button with a selected marker.</summary>
internal sealed class NavButton : Button
{
    private bool _selected;

    public NavButton(string number, string text)
    {
        Text = $"  {number}   {text}";
        TextAlign = ContentAlignment.MiddleLeft;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        FlatAppearance.MouseOverBackColor = Theme.SurfaceHigh;
        FlatAppearance.MouseDownBackColor = Theme.Input;
        BackColor = Theme.Surface;
        ForeColor = Theme.Muted;
        Font = Theme.Heading;
        Height = 48;
        Dock = DockStyle.Top;
        Cursor = Cursors.Hand;
        UseVisualStyleBackColor = false;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            BackColor = value ? Theme.SurfaceHigh : Theme.Surface;
            ForeColor = value ? Theme.Text : Theme.Muted;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!_selected) return;
        using var brush = new SolidBrush(Theme.Accent);
        e.Graphics.FillRectangle(brush, 0, 6, 4, Height - 12);
    }
}
