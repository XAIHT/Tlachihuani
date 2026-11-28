// HandGI expert window (Angela López Mendoza · XAIHT), part of Tlachihuani. tlachihuani:trace-off (repaints and polls the engine every 15 ms; its actions are written to Tlachihuani.log through the HandGI log bridge)
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace HandGI.App;

/// <summary>
/// A small on-screen volume display at the bottom of the screen. It never takes focus and clicks pass
/// through it, so it can appear over any application while a gesture changes the volume.
/// </summary>
internal sealed class VolumeOsd : Form
{
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExTopmost = 0x00000008;
    private const int WsExTransparent = 0x00000020;
    private const uint SwpNoSize = 0x0001, SwpNoMove = 0x0002, SwpNoActivate = 0x0010;
    private static readonly IntPtr HwndTopmost = new(-1);

    private readonly System.Windows.Forms.Timer _hide = new() { Interval = 1400 };
    private float _level;
    private bool _muted;
    private string _caption = "";

    public VolumeOsd()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        // In front of every window through WS_EX_TOPMOST and SetWindowPos, not through TopMost: Windows
        // Forms gives a TopMost window the keyboard each time it is shown, and this one must never take it.
        StartPosition = FormStartPosition.Manual;
        BackColor = Theme.Surface;
        DoubleBuffered = true;
        Size = new Size(380, 78);
        _hide.Tick += (_, _) =>
        {
            _hide.Stop();
            Hide();
        };
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WsExNoActivate | WsExToolWindow | WsExTopmost | WsExTransparent;
            return cp;
        }
    }

    public void ShowLevel(float level, bool muted, string caption)
    {
        _level = Math.Clamp(level, 0f, 1f);
        _muted = muted;
        _caption = caption;
        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        Size = new Size(LogicalToDeviceUnits(380), LogicalToDeviceUnits(78));
        Location = new Point(screen.Left + (screen.Width - Width) / 2, screen.Bottom - Height - LogicalToDeviceUnits(72));
        if (!Visible) Show();
        SetWindowPos(Handle, HwndTopmost, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoActivate);
        Invalidate();
        _hide.Stop();
        _hide.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Surface);
        using (var edge = new Pen(Theme.Border, 1.5f))
            g.DrawRectangle(edge, 0, 0, Width - 1, Height - 1);

        int pad = LogicalToDeviceUnits(14);
        string head = _muted ? "MUTED" : $"Volume {_level:P0}";
        TextRenderer.DrawText(g, head, Theme.Heading, new Point(pad, pad - 4), _muted ? Theme.Danger : Theme.Text);
        TextRenderer.DrawText(g, _caption, Theme.Small, new Rectangle(pad + LogicalToDeviceUnits(150), pad - 1, Width - pad * 2 - LogicalToDeviceUnits(150), LogicalToDeviceUnits(20)),
            Theme.Muted, TextFormatFlags.Right | TextFormatFlags.EndEllipsis);

        var track = new RectangleF(pad, Height - pad - LogicalToDeviceUnits(14), Width - pad * 2, LogicalToDeviceUnits(10));
        using (var trackPath = Theme.Rounded(track, track.Height / 2))
        using (var trackBrush = new SolidBrush(Theme.Input))
            g.FillPath(trackBrush, trackPath);
        if (_level > 0.005f && !_muted)
        {
            var fill = new RectangleF(track.X, track.Y, Math.Max(track.Height, track.Width * _level), track.Height);
            using var fillPath = Theme.Rounded(fill, track.Height / 2);
            using var fillBrush = new SolidBrush(Theme.Accent);
            g.FillPath(fillBrush, fillPath);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _hide.Dispose();
        base.Dispose(disposing);
    }

    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
