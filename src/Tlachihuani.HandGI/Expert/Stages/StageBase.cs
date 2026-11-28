// HandGI expert window (Angela López Mendoza · XAIHT), part of Tlachihuani. tlachihuani:trace-off (repaints and polls the engine every 15 ms; its actions are written to Tlachihuani.log through the HandGI log bridge)
using HandGI.Core.Engine;
using HandGI.Core.Gestures;

namespace HandGI.App.Stages;

/// <summary>A scrollable column of cards; the window feeds it frames, gestures and a slow tick.</summary>
internal abstract class StageBase : UserControl
{
    private readonly TableLayoutPanel _stack;

    protected StageBase(AppController app, string title, string subtitle)
    {
        App = app;
        Dock = DockStyle.Fill;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        AutoScroll = true;
        Padding = new Padding(0, 0, 6, 0);

        _stack = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Background,
            Padding = new Padding(0, 0, 4, 0),
        };
        _stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(_stack);

        Add(new Label { Text = title, Font = Theme.Title, ForeColor = Theme.Text, AutoSize = true, Margin = new Padding(0, 0, 0, 0) });
        Add(new WrapLabel(subtitle, Theme.Muted, Theme.Body) { Margin = new Padding(0, 2, 0, 8) });
    }

    protected AppController App { get; }

    protected void Add(Control control)
    {
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        _stack.Controls.Add(control);
    }

    public virtual void OnShown() { }

    public virtual void OnReport(FrameReport report) { }

    public virtual void OnSlowTick() { }

    public virtual void OnGesture(GestureEvent gestureEvent) { }

    public virtual void OnProfileChanged() { }

    public virtual void OnEngineState(EngineState state, string message) { }

    protected void Fail(string message)
    {
        App.Log.Warn(message);
        MessageBox.Show(FindForm(), message, "HandGI", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
