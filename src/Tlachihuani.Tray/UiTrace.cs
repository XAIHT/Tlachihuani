// Tlachihuani — Angela López Mendoza · XAIHT
// tlachihuani:trace-off (one clear line per UI event instead of the handler's statements)
using Tlachihuani.Contracts;

namespace Tlachihuani.Tray;

// Every user action and visible change in XT's windows and tray menu goes into
// Tlachihuani.log: clicks, keys, focus, text and state labels changing, buttons being
// enabled or disabled, check boxes, lists, windows opening, moving, resizing, closing.
// Secret fields (keys, passwords, tokens) are logged by length only.
internal static class UiTrace
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, object> attached = new();

    // A control that changes on its own many times a second (the log viewer) is left out.
    internal static void Ignore(Control control) { if (!attached.TryGetValue(control, out _)) attached.Add(control, control); }

    internal static void Attach(Control root)
    {
        if (attached.TryGetValue(root, out _)) return;
        attached.Add(root, root);
        string Who() => Describe(root);
        root.Click += (_, _) => Log($"click {Who()}");
        root.DoubleClick += (_, _) => Log($"double-click {Who()}");
        root.MouseDown += (_, e) => Log($"mouse {e.Button} down on {Who()} at {e.Location}");
        root.MouseUp += (_, e) => Log($"mouse {e.Button} up on {Who()} at {e.Location}");
        root.KeyDown += (_, e) => Log($"key down {(Secret(root) ? "(hidden)" : e.KeyData.ToString())} in {Who()}");
        root.Enter += (_, _) => Log($"focus enters {Who()}");
        root.Leave += (_, _) => Log($"focus leaves {Who()}");
        root.EnabledChanged += (_, _) => Log($"{(root.Enabled ? "enabled" : "disabled")}: {Who()}");
        root.VisibleChanged += (_, _) => Log($"{(root.Visible ? "shown" : "hidden")}: {Who()}");
        root.TextChanged += (_, _) => Log($"text changed: {Who()}");
        switch (root)
        {
            case CheckBox box: box.CheckedChanged += (_, _) => Log($"{(box.Checked ? "checked" : "unchecked")}: {Who()}"); break;
            case RadioButton radio: radio.CheckedChanged += (_, _) => Log($"{(radio.Checked ? "selected" : "deselected")}: {Who()}"); break;
            case CheckedListBox list: list.ItemCheck += (_, e) => Log($"item {e.Index} \"{list.Items[e.Index]}\" {e.CurrentValue} → {e.NewValue} in {Who()}"); break;
            case ListBox list: list.SelectedIndexChanged += (_, _) => Log($"selected {list.SelectedIndex} \"{list.SelectedItem}\" in {Who()}"); break;
            case ComboBox combo: combo.SelectedIndexChanged += (_, _) => Log($"selected {combo.SelectedIndex} \"{combo.SelectedItem}\" in {Who()}"); break;
        }
        if (root is Form form)
        {
            form.Load += (_, _) => Log($"window loading: {Who()} size {form.Size} at {form.Location}");
            form.Shown += (_, _) => Log($"window shown: {Who()} size {form.Size} at {form.Location}, DPI {form.DeviceDpi}");
            form.Activated += (_, _) => Log($"window activated: {Who()}");
            form.Deactivate += (_, _) => Log($"window deactivated: {Who()}");
            form.ResizeEnd += (_, _) => Log($"window moved or resized: {Who()} size {form.Size} at {form.Location}");
            form.SizeChanged += (_, _) => Log($"window size {form.Size}, state {form.WindowState}: {Who()}");
            form.FormClosing += (_, e) => Log($"window closing ({e.CloseReason}{(e.Cancel ? ", cancelled" : "")}): {Who()} result {form.DialogResult}");
            form.FormClosed += (_, e) => Log($"window closed ({e.CloseReason}): {Who()} result {form.DialogResult}");
        }
        foreach (Control child in root.Controls) Attach(child);
        root.ControlAdded += (_, e) => { if (e.Control is not null) { Log($"control added to {Who()}: {Describe(e.Control)}"); Attach(e.Control); } };
        root.ControlRemoved += (_, e) => { if (e.Control is not null) Log($"control removed from {Who()}: {Describe(e.Control)}"); };
    }

    internal static void Attach(ToolStrip strip, string name)
    {
        if (attached.TryGetValue(strip, out _)) return;
        attached.Add(strip, strip);
        if (strip is ContextMenuStrip menu)
        {
            menu.Opening += (_, e) => Log($"{name} opening{(e.Cancel ? " (cancelled)" : "")}");
            menu.Opened += (_, _) => Log($"{name} opened: {string.Join(" | ", menu.Items.Cast<ToolStripItem>().Select(Item))}");
            menu.Closed += (_, e) => Log($"{name} closed ({e.CloseReason})");
        }
        strip.ItemClicked += (_, e) => Log($"{name} item clicked: {Item(e.ClickedItem!)}");
        foreach (ToolStripItem item in strip.Items) Attach(item, name);
        strip.ItemAdded += (_, e) => { Log($"{name} item added: {Item(e.Item!)}"); Attach(e.Item!, name); };
    }

    private static void Attach(ToolStripItem item, string name)
    {
        item.EnabledChanged += (_, _) => Log($"{name} item {(item.Enabled ? "enabled" : "disabled")}: {Item(item)}");
        item.TextChanged += (_, _) => Log($"{name} item text changed: {Item(item)}");
        item.VisibleChanged += (_, _) => Log($"{name} item {(item.Visible ? "shown" : "hidden")}: {Item(item)}");
        if (item is ToolStripMenuItem menuItem) menuItem.CheckedChanged += (_, _) => Log($"{name} item {(menuItem.Checked ? "checked" : "unchecked")}: {Item(item)}");
    }

    internal static void Log(string text) => TraceLog.Write(TraceLevel.Info, "UI " + text, "UI", "", 0);

    private static string Item(ToolStripItem item) => $"\"{item.Text}\"{(item.Enabled ? "" : " (disabled)")}";

    private static bool Secret(Control control) => control is TextBox { UseSystemPasswordChar: true } or TextBox { PasswordChar: not '\0' }
        || (control.AccessibleName ?? "").Contains("key", StringComparison.OrdinalIgnoreCase)
        || (control.AccessibleName ?? "").Contains("token", StringComparison.OrdinalIgnoreCase)
        || (control.AccessibleName ?? "").Contains("password", StringComparison.OrdinalIgnoreCase)
        || (control.AccessibleName ?? "").Contains("invitation", StringComparison.OrdinalIgnoreCase)
        || (control.AccessibleName ?? "").Contains("credential", StringComparison.OrdinalIgnoreCase);

    internal static string Describe(Control control)
    {
        string window = control.FindForm() is { } form && form != control ? form.Text + " ▸ " : "";
        string kind = control is StudioButton b ? (b.Selected ? "tab (selected)" : "button") : control.GetType().Name;
        string text;
        if (Secret(control)) text = $"(secret, {control.Text.Length} characters)";
        else if (control is TextBoxBase { Multiline: true, ReadOnly: true } pane) text = $"({pane.TextLength} characters)";
        else text = control.Text.Length > 300 ? control.Text[..300] + "…" : control.Text;
        string name = control.AccessibleName is { Length: > 0 } accessible && accessible != control.Text ? $" [{accessible}]" : "";
        return $"{window}{kind} \"{text.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal)}\"{name}";
    }
}
