// Tlachihuani — Angela López Mendoza · XAIHT
using Tlachihuani.Tray;

namespace HandGI.App;

// A question in Tlachihuani's own look, for a change that cannot be undone with one click.
internal sealed class ConfirmForm : StudioForm
{
    private ConfirmForm(string title, string text, string verb)
        : base(null, "HandGI", "HandGI", title, new Size(540, 236), new Size(480, 226))
    {
        var note = Studio.Label(text, Studio.Regular, Studio.Body);
        note.AutoEllipsis = false;
        note.TextAlign = ContentAlignment.TopLeft;
        BodyArea.Controls.Add(note);
        var cancel = Command("Cancel", () => { DialogResult = DialogResult.Cancel; Close(); });
        Commands.Controls.Add(cancel);
        Commands.Controls.Add(Command(verb, () => { DialogResult = DialogResult.OK; Close(); }, ButtonTone.Danger));
        CancelButton = cancel;
    }

    internal static bool Ask(IWin32Window owner, string title, string text, string verb)
    {
        using var form = new ConfirmForm(title, text, verb);
        return form.ShowDialog(owner) == DialogResult.OK;
    }
}
