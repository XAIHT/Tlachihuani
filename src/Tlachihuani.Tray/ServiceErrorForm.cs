// Tlachihuani — Angela López Mendoza · XAIHT
// tlachihuani:trace-off (the sanitized report is saved explicitly, not on every UI event)
using System.Runtime.InteropServices;

namespace Tlachihuani.Tray;

internal sealed class ServiceErrorForm : StudioForm
{
    internal RichTextBox Details { get; }
    internal StudioButton CopyReport { get; }
    internal string CopyStatus => FooterNote.Text;

    internal ServiceErrorForm(Form? owner, ServiceErrorReport report)
        : base(owner?.Icon, "Tlachihuani · Service action failed", "Tlachihuani   /   Diagnostics",
            "Service action failed", new(820, 570), new(640, 400))
    {
        if (owner?.Visible != true) { StartPosition = FormStartPosition.CenterScreen; ShowInTaskbar = true; }
        var layout = Studio.Grid([-1], [76, 24, -1, 42]);
        var summary = new RichTextBox { Text = report.Summary, ReadOnly = true, BorderStyle = BorderStyle.None,
            DetectUrls = false, BackColor = Studio.Surface, ForeColor = Studio.Body, Font = Studio.Regular,
            Dock = DockStyle.Fill, AccessibleName = "Failure summary" };
        layout.Controls.Add(summary, 0, 0);
        layout.Controls.Add(Studio.Label("Diagnostic details — select text or copy the complete report", Studio.Small), 0, 1);
        Details = new RichTextBox { Text = report.Text, ReadOnly = true, DetectUrls = false, WordWrap = false,
            ScrollBars = RichTextBoxScrollBars.Both, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill,
            BackColor = Studio.Pane, ForeColor = Studio.Text, Font = new Font("Consolas", 9),
            AccessibleName = "Complete service error report", HideSelection = false };
        Details.Disposed += (_, _) => Details.Font.Dispose();
        layout.Controls.Add(Studio.Card(Details, new Padding(10)), 0, 2);
        var saved = Studio.Label(report.Save(), Studio.Small, Studio.Muted);
        saved.AutoEllipsis = false;
        layout.Controls.Add(saved, 0, 3);
        BodyArea.Controls.Add(layout);
        CopyReport = Command("Copy report", () =>
        {
            try { Clipboard.SetText(report.Text); FooterNote.Text = "Report copied"; }
            catch (ExternalException) { FooterNote.Text = "Clipboard busy; try again"; }
        }, ButtonTone.Primary);
        var close = Command("Close", Close);
        Commands.Controls.AddRange([close, CopyReport]);
        CancelButton = close;
        FooterNote.Text = "Report " + report.Id;
    }
}
