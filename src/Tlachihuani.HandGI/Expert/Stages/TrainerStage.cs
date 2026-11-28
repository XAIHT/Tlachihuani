// HandGI expert window (Angela López Mendoza · XAIHT), part of Tlachihuani. tlachihuani:trace-off (repaints and polls the engine every 15 ms; its actions are written to Tlachihuani.log through the HandGI log bridge)
using System.Diagnostics;
using HandGI.App.Controls;
using HandGI.Core.Engine;
using HandGI.Core.Gestures;
using HandGI.Core.Training;
using HandGI.Core.Util;
using HandGI.Core.Vision;

namespace HandGI.App.Stages;

/// <summary>
/// Stage 2: Google's seven ready-made gestures (choose what each does, switch it off, or replace it
/// with your own) and your own gestures (record examples from the live camera, learn them on the GPU).
/// </summary>
internal sealed class TrainerStage : StageBase
{
    private const int MinSamplesPerGesture = 10;

    private sealed record Choice<T>(T Value, string Text)
    {
        public override string ToString() => Text;
    }

    private readonly PreviewView _preview;
    private readonly TextBox _profileName = Ui.TextBox("", 260);
    private readonly ListView _classes;
    private readonly TextBox _className = Ui.TextBox("", 220);
    private readonly ComboBox _action = Ui.Combo(260);
    private readonly WrapLabel _hint = new("");
    private readonly NumericUpDown _seconds = Ui.Number(2, 20, 4, 1);
    private readonly ProgressBar _recordProgress = Ui.Progress();
    private readonly WrapLabel _recordNote = new("Only YOUR gestures (★) are recorded: select one, press Record, then hold it in front of the camera. Move a little while recording: nearer, farther, slightly turned. Three or four recordings per gesture work best. Google's gestures need no recording.");
    private readonly NumericUpDown _steps = Ui.Number(100, 5000, 600, 100, width: 96);
    private readonly ProgressBar _trainProgress = Ui.Progress();
    private readonly WrapLabel _trainNote = new("");
    private readonly LineChart _chart = new();
    private readonly ConfusionView _confusion = new();
    private readonly Button _add;
    private readonly Button _replace;
    private readonly Button _toggle;
    private readonly Button _remove;
    private readonly Button _clear;
    private readonly Button _record;
    private readonly Button _train;
    private readonly List<Control> _lockWhileBusy = new();
    private bool _updating;
    private bool _busy;

    public TrainerStage(AppController app, PreviewView preview)
        : base(app, "Trainer", "Google's 7 ready-made gestures work at once: choose what each one does, switch it off, or replace it with your own. You can also teach HandGI new gestures of your own (★). Only 63 numbers per frame are stored for them (the shape of the hand), never images, and learning runs on the GPU in a few seconds.")
    {
        _preview = preview;

        _action.Items.Add(new Choice<GestureAction>(GestureAction.None, "Nothing"));
        _action.Items.Add(new Choice<GestureAction>(GestureAction.VolumeUp, "Volume up (one step, repeats while held)"));
        _action.Items.Add(new Choice<GestureAction>(GestureAction.VolumeDown, "Volume down (one step, repeats while held)"));
        _action.Items.Add(new Choice<GestureAction>(GestureAction.MuteToggle, "Mute / unmute"));
        _action.Items.Add(new Choice<GestureAction>(GestureAction.VolumeLever, "Lever: raise or lower the hand to set the volume"));

        _classes = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            BackColor = Theme.Input,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.None,
            Height = 300,
            OwnerDraw = true,
            Font = Theme.Body,
            Margin = new Padding(0, 4, 0, 6),
        };
        _classes.Columns.Add("", 30);
        _classes.Columns.Add("Gesture", 170);
        _classes.Columns.Add("From", 80);
        _classes.Columns.Add("Action", 140);
        _classes.Columns.Add("Status", 150);
        _classes.DrawColumnHeader += (_, e) =>
        {
            using var brush = new SolidBrush(Theme.SurfaceHigh);
            e.Graphics.FillRectangle(brush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", Theme.Caption, Rectangle.Inflate(e.Bounds, -6, 0), Theme.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        };
        _classes.DrawItem += (_, _) => { };
        _classes.DrawSubItem += (_, e) =>
        {
            bool selected = e.Item?.Selected == true;
            using (var background = new SolidBrush(selected ? Theme.SurfaceHigh : Theme.Input))
                e.Graphics.FillRectangle(background, e.Bounds);
            int index = e.ItemIndex;
            var classes = App.Profile.Classes;
            bool off = index >= 0 && index < classes.Count && !classes[index].Enabled;
            if (e.ColumnIndex == 0)
            {
                int d = Math.Min(e.Bounds.Height - 10, 12);
                var dot = new Rectangle(e.Bounds.X + 9, e.Bounds.Y + (e.Bounds.Height - d) / 2, d, d);
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                if (off)
                {
                    using var ring = new Pen(Theme.Muted, 1.5f);
                    e.Graphics.DrawEllipse(ring, dot);
                }
                else
                {
                    using var fill = new SolidBrush(Theme.ClassColor(index));
                    e.Graphics.FillEllipse(fill, dot);
                }
                return;
            }
            var color = off || e.ColumnIndex == 4 ? Theme.Muted : Theme.Text;
            if (selected && !off) color = Theme.Text;
            TextRenderer.DrawText(e.Graphics, e.SubItem?.Text ?? "", Theme.Body, Rectangle.Inflate(e.Bounds, -6, 0), color,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
        _classes.Resize += (_, _) => FitColumns();
        _classes.SelectedIndexChanged += (_, _) => ShowSelectedClass();

        var newButton = Ui.Button("New", (_, _) => NewProfile());
        var openButton = Ui.Button("Open...", (_, _) => OpenProfile());
        var saveButton = Ui.Button("Save", (_, _) => SaveProfile(saveAs: false));
        var saveAsButton = Ui.Button("Save as...", (_, _) => SaveProfile(saveAs: true));
        _add = Ui.Button("★  Add my own gesture", (_, _) => AddMine());
        _replace = Ui.Button("Replace with my own", (_, _) => ReplaceSelected());
        _toggle = Ui.Button("Switch off", (_, _) => ToggleSelected());
        _remove = Ui.Button("Remove", (_, _) => RemoveSelected());
        _clear = Ui.Button("Clear examples", (_, _) => ClearSamples());
        _record = Ui.Button("●  Record my gesture", async (_, _) => await RecordAsync(), primary: true);
        _train = Ui.Button("Learn my gestures on the GPU", async (_, _) => await TrainAsync(), primary: true);
        _lockWhileBusy.AddRange(new Control[] { newButton, openButton, saveButton, saveAsButton, _add, _replace, _toggle, _remove, _clear, _record, _train, _classes, _className, _action, _profileName });

        var profile = new Card("Profile");
        profile.Add(Ui.Row(Ui.Field("Name"), _profileName, newButton, openButton, saveButton, saveAsButton));
        Add(profile);

        var gestures = new Card("Gestures");
        gestures.Add(_classes, stretch: true);
        gestures.Add(Ui.Row(_add, _replace, _toggle, _remove, _clear));
        gestures.Add(Ui.Row(Ui.Field("Name"), _className));
        gestures.Add(Ui.Row(Ui.Field("Action"), _action));
        gestures.Add(_hint, stretch: true);
        Add(gestures);

        var record = new Card("Record examples of your gesture");
        record.Add(Ui.Row(Ui.Field("Seconds"), _seconds, _record));
        record.Add(_recordProgress, stretch: true);
        record.Add(_recordNote, stretch: true);
        Add(record);

        var train = new Card("Learn your gestures on the GPU");
        train.Add(Ui.Row(Ui.Field("Steps"), _steps, _train));
        train.Add(_trainProgress, stretch: true);
        train.Add(_trainNote, stretch: true);
        train.Add(_chart, stretch: true);
        train.Add(_confusion, stretch: true);
        Add(train);

        _profileName.TextChanged += (_, _) =>
        {
            if (_updating) return;
            App.Profile.Name = _profileName.Text.Trim().Length == 0 ? "My gestures" : _profileName.Text.Trim();
        };
        _className.TextChanged += (_, _) =>
        {
            if (_updating || SelectedIndex is not int i || App.Profile.Classes[i].IsReadyMade) return;
            App.Profile.Classes[i].Name = _className.Text.Trim().Length == 0 ? $"My gesture {App.Profile.MineOrdinal(i) + 1}" : _className.Text.Trim();
            RefreshRow(i);
            App.ProfileEdited();
        };
        _action.SelectedIndexChanged += (_, _) =>
        {
            if (_updating || SelectedIndex is not int i || _action.SelectedItem is not Choice<GestureAction> c) return;
            App.Profile.Classes[i].Action = c.Value;
            RefreshRow(i);
            App.ProfileEdited();
            AutoSave();
        };

        LoadProfileIntoView();
    }

    private int? SelectedIndex => _classes.SelectedIndices.Count > 0 ? _classes.SelectedIndices[0] : null;

    // ------------------------------------------------------------------ view

    private void LoadProfileIntoView()
    {
        _updating = true;
        try
        {
            var p = App.Profile;
            _profileName.Text = p.Name;
            int keep = SelectedIndex ?? 0;
            _classes.BeginUpdate();
            _classes.Items.Clear();
            for (int i = 0; i < p.Classes.Count; i++)
            {
                var item = new ListViewItem("");
                item.SubItems.Add("");
                item.SubItems.Add("");
                item.SubItems.Add("");
                item.SubItems.Add("");
                _classes.Items.Add(item);
                FillRow(i);
            }
            _classes.EndUpdate();
            if (_classes.Items.Count > 0) _classes.Items[Math.Clamp(keep, 0, _classes.Items.Count - 1)].Selected = true;
            _preview.SetClassNames(p.Classes.Select(c => c.Name));
            ShowTrainingState();
        }
        finally
        {
            _updating = false;
        }
        ShowSelectedClass();
        FitColumns();
    }

    private void ShowTrainingState()
    {
        var p = App.Profile;
        if (p.Training is { } t && p.IsTrained)
        {
            _confusion.SetData(ConfusionNames(p), t.Confusion);
            _trainNote.ForeColor = Theme.Muted;
            _trainNote.Text = $"Learned {t.TrainedUtc.ToLocalTime():g} on {t.Device}: {t.ValidationAccuracy:P1} right on examples it had not seen " +
                              $"({t.Samples} of your examples + {t.Negatives:N0} generated \"not yours\" hands, {t.Steps} steps, {t.TotalMs:0} ms).";
        }
        else
        {
            _confusion.SetData(Array.Empty<string>(), Array.Empty<int[]>());
            _trainNote.ForeColor = p.NeedsTraining ? Theme.Warn : Theme.Muted;
            _trainNote.Text = p.MineCount == 0
                ? "You are using Google's gestures only: there is nothing to learn. Add a gesture of your own (★) to teach HandGI something new."
                : "Your gestures are not learned yet: record each one, then press Learn. Google's gestures keep working meanwhile.";
        }
        if (p.MigrationNote is not null) _trainNote.Text = p.MigrationNote + " " + _trainNote.Text;
    }

    private static IEnumerable<string> ConfusionNames(GestureProfile p) =>
        new[] { "Not yours" }.Concat(p.MineIndices.Select(i => p.Classes[i].Name));

    private void FillRow(int i)
    {
        var p = App.Profile;
        var c = p.Classes[i];
        var item = _classes.Items[i];
        item.SubItems[1].Text = c.DisplayName;
        item.SubItems[2].Text = c.IsReadyMade ? "Google" : "You";
        item.SubItems[3].Text = GestureProfile.ActionText(c.Action);
        item.SubItems[4].Text = StatusOf(p, i);
    }

    private static string StatusOf(GestureProfile p, int i)
    {
        var c = p.Classes[i];
        if (c.IsReadyMade)
        {
            if (c.Enabled) return "Ready";
            var by = p.Classes.FirstOrDefault(m => !m.IsReadyMade && m.ReadyMadeId == c.ReadyMadeId);
            return by is null ? "Off" : $"Off · replaced by yours";
        }
        int examples = p.Samples.CountOf(p.MineOrdinal(i));
        string count = $"{examples} example{(examples == 1 ? "" : "s")}";
        if (!c.Enabled) return $"Off · {count}";
        if (examples == 0) return "Record it";
        if (!p.IsTrained) return $"{count} · learn";
        var w = p.Weights!;
        return w.BothHands(p.MineOrdinal(i)) ? $"Learned · {count} · both hands"
             : w.HandsChecked ? $"Learned · {count} · one hand"
             : $"Learned · {count}";
    }

    private void RefreshRow(int i)
    {
        if (i < 0 || i >= _classes.Items.Count) return;
        FillRow(i);
        _classes.Invalidate();
        _preview.SetClassNames(App.Profile.Classes.Select(c => c.Name));
    }

    private void RefreshAllRows()
    {
        for (int i = 0; i < _classes.Items.Count && i < App.Profile.Classes.Count; i++) FillRow(i);
        _classes.Invalidate();
    }

    private void ShowSelectedClass()
    {
        UpdateButtons();
        if (SelectedIndex is not int i || i >= App.Profile.Classes.Count)
        {
            _hint.Text = "";
            return;
        }
        _updating = true;
        try
        {
            var p = App.Profile;
            var c = p.Classes[i];
            _className.Text = c.Name;
            for (int k = 0; k < _action.Items.Count; k++)
                if (_action.Items[k] is Choice<GestureAction> choice && choice.Value == c.Action) _action.SelectedIndex = k;

            if (c.IsReadyMade)
            {
                var by = p.Classes.FirstOrDefault(m => !m.IsReadyMade && m.ReadyMadeId == c.ReadyMadeId);
                _hint.Text = $"{c.Hint} Google's ready-made gesture: nothing to record or learn." +
                             (c.Enabled ? "" : by is null ? " It is switched off." : $" It is switched off: your gesture \"{by.Name}\" does its job.");
            }
            else
            {
                var replaced = ReadyGestures.Find(c.ReadyMadeId);
                _hint.Text = $"{c.Hint}" + (replaced is null ? "" : $" It replaces Google's \"{replaced.Name}\".") +
                             (p.Samples.CountOf(p.MineOrdinal(i)) < MinSamplesPerGesture ? $" Record at least {MinSamplesPerGesture} examples, then press Learn." : "") +
                             HandsHint(p, i);
            }
        }
        finally
        {
            _updating = false;
        }
    }

    /// <summary>Every button only where it makes sense for the selected gesture.</summary>
    private void UpdateButtons()
    {
        if (_busy) return;
        var p = App.Profile;
        bool any = SelectedIndex is int s && s < p.Classes.Count;
        var c = any ? p.Classes[SelectedIndex!.Value] : null;
        bool ready = c?.IsReadyMade == true;
        bool mine = c is { IsReadyMade: false };
        bool replaced = ready && p.Classes.Any(m => !m.IsReadyMade && m.ReadyMadeId == c!.ReadyMadeId);
        bool room = p.MineCount < HandGraphs.MaxOwnGestures;

        _add.Enabled = room;
        _replace.Enabled = ready && !replaced && room;
        _toggle.Enabled = any;
        _toggle.Text = c is { Enabled: false } ? "Switch on" : "Switch off";
        _remove.Enabled = mine;
        _clear.Enabled = mine && p.Samples.CountOf(p.MineOrdinal(SelectedIndex!.Value)) > 0;
        _record.Enabled = mine;
        _className.Enabled = mine;
        _action.Enabled = any;
        _train.Enabled = p.MineCount > 0;
    }

    private void Select(int index)
    {
        if (index < 0 || index >= _classes.Items.Count) return;
        _classes.Items[index].Selected = true;
        _classes.EnsureVisible(index);
    }

    private void FitColumns()
    {
        if (_classes.Columns.Count < 5) return;
        int c0 = LogicalToDeviceUnits(30), c2 = LogicalToDeviceUnits(72), c3 = LogicalToDeviceUnits(130), c4 = LogicalToDeviceUnits(160);
        _classes.Columns[0].Width = c0;
        _classes.Columns[2].Width = c2;
        _classes.Columns[3].Width = c3;
        _classes.Columns[4].Width = c4;
        _classes.Columns[1].Width = Math.Max(LogicalToDeviceUnits(110), _classes.ClientSize.Width - c0 - c2 - c3 - c4 - 2);
    }

    // ------------------------------------------------------------------ profile

    private void NewProfile()
    {
        if (App.Profile.Samples.Count > 0 &&
            MessageBox.Show(FindForm(), "Start a new profile? Unsaved examples of the current one are lost.", "HandGI", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;
        App.SetProfile(GestureProfile.CreateDefault());
    }

    private void OpenProfile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Open a HandGI profile",
            Filter = "HandGI profiles (*.handgi.json)|*.handgi.json|JSON (*.json)|*.json",
            InitialDirectory = AppPaths.ProfilesDir,
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            App.SetProfile(GestureProfile.Load(dialog.FileName));
            App.Settings.LastProfile = dialog.FileName;
            App.Settings.Save(App.Log);
        }
        catch (Exception ex)
        {
            Fail($"The profile could not be opened: {ex.Message}");
        }
    }

    private void SaveProfile(bool saveAs)
    {
        string? path = App.Profile.FilePath;
        if (saveAs || path is null)
        {
            using var dialog = new SaveFileDialog
            {
                Title = "Save the HandGI profile",
                Filter = "HandGI profiles (*.handgi.json)|*.handgi.json",
                InitialDirectory = AppPaths.ProfilesDir,
                FileName = GestureProfile.SafeFileName(App.Profile.Name) + ".handgi.json",
            };
            if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
            path = dialog.FileName;
        }
        try
        {
            App.SaveProfile(path);
            _recordNote.Text = $"Saved {Path.GetFileName(path)}.";
        }
        catch (Exception ex)
        {
            Fail($"The profile could not be saved: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------ gestures

    private void AddMine()
    {
        var p = App.Profile;
        if (p.MineCount >= HandGraphs.MaxOwnGestures)
        {
            Fail($"HandGI learns up to {HandGraphs.MaxOwnGestures} gestures of your own.");
            return;
        }
        int index = p.AddMine($"My gesture {p.MineCount + 1}", GestureAction.None);
        App.ProfileEdited();
        AutoSave();
        Select(index);
        _recordNote.ForeColor = Theme.Muted;
        _recordNote.Text = "Give your gesture a name and an action, then press Record and show it to the camera.";
        _className.Focus();
        _className.SelectAll();
    }

    private void ReplaceSelected()
    {
        if (SelectedIndex is not int i) return;
        var p = App.Profile;
        var c = p.Classes[i];
        if (!c.IsReadyMade) return;
        if (p.MineCount >= HandGraphs.MaxOwnGestures)
        {
            Fail($"HandGI learns up to {HandGraphs.MaxOwnGestures} gestures of your own; remove one first.");
            return;
        }
        int index = p.ReplaceWithMine(i);
        App.ProfileEdited();
        AutoSave();
        Select(index);
        _recordNote.ForeColor = Theme.Muted;
        _recordNote.Text = $"Google's \"{c.Name}\" is switched off. Now record YOUR way of doing it: press Record and show your gesture, then press Learn.";
    }

    private void ToggleSelected()
    {
        if (SelectedIndex is not int i) return;
        var c = App.Profile.Classes[i];
        c.Enabled = !c.Enabled;
        App.ProfileEdited();
        AutoSave();
        RefreshAllRows();
        ShowSelectedClass();
    }

    private void RemoveSelected()
    {
        var p = App.Profile;
        if (SelectedIndex is not int i || p.Classes[i].IsReadyMade) return;
        var c = p.Classes[i];
        int samples = p.Samples.CountOf(p.MineOrdinal(i));
        if (samples > 0 && MessageBox.Show(FindForm(), $"Remove '{c.Name}' and its {samples} examples?", "HandGI",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;
        var replaced = ReadyGestures.Find(c.ReadyMadeId);
        p.RemoveMine(i);
        App.ProfileEdited();
        AutoSave();
        _recordNote.ForeColor = Theme.Muted;
        _recordNote.Text = replaced is null
            ? $"'{c.Name}' removed." + (p.MineCount > 0 ? " Press Learn again for the gestures you kept." : "")
            : $"'{c.Name}' removed: Google's \"{replaced.Name}\" is back on." + (p.MineCount > 0 ? " Press Learn again for the gestures you kept." : "");
    }

    private void ClearSamples()
    {
        if (SelectedIndex is not int i) return;
        var p = App.Profile;
        int ordinal = p.MineOrdinal(i);
        if (ordinal < 0) return;
        int samples = p.Samples.CountOf(ordinal);
        if (samples == 0) return;
        if (MessageBox.Show(FindForm(), $"Delete the {samples} examples of '{p.Classes[i].Name}'?", "HandGI", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;
        p.Samples.RemoveClass(ordinal, shiftHigherLabels: false);
        RefreshRow(i);
        ShowSelectedClass();
        _recordNote.Text = "Examples deleted. Record the gesture again, then press Learn.";
        AutoSave();
    }

    /// <summary>Recorded examples are the user's work: keep them on disk at once, not only after learning.</summary>
    private void AutoSave()
    {
        try
        {
            App.SaveProfile();
        }
        catch (Exception ex)
        {
            App.Log.Warn($"The profile could not be saved automatically: {ex.Message}");
            Fail($"Your changes are made but could not be saved: {ex.Message}. Use Save as... to keep them.");
        }
    }

    // ------------------------------------------------------------------ record

    private async Task RecordAsync()
    {
        var engine = App.Engine;
        if (_busy || SelectedIndex is not int index) return;
        int label = App.Profile.MineOrdinal(index);
        if (label < 0)
        {
            Fail("Google's gestures need no recording. Select one of your own gestures (★), or press \"Replace with my own\".");
            return;
        }
        if (engine is null || engine.State != EngineState.Running)
        {
            Fail("Start the camera in Setup first.");
            return;
        }
        string name = App.Profile.Classes[index].Name;
        SetBusy(true);
        try
        {
            _preview.CountdownColor = Theme.ClassColor(index);
            for (int n = 3; n >= 1; n--)
            {
                _preview.Countdown = $"{name}  ·  {n}";
                _preview.Invalidate();
                await Task.Delay(800);
            }
            double seconds = (double)_seconds.Value;
            engine.BeginRecording(label);
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < seconds)
            {
                _recordProgress.Value = (int)Math.Clamp(sw.Elapsed.TotalSeconds / seconds * 1000, 0, 1000);
                _preview.Countdown = $"Recording {name}  ·  {engine.RecordedCount}";
                _preview.Invalidate();
                await Task.Delay(50);
            }
            var samples = engine.EndRecording();
            foreach (var s in samples) App.Profile.Samples.Add(s, label);
            _recordProgress.Value = 1000;
            RefreshRow(index);
            int total = App.Profile.Samples.CountOf(label);
            _recordNote.ForeColor = samples.Count == 0 ? Theme.Warn : Theme.Muted;
            _recordNote.Text = samples.Count == 0
                ? "No hand was seen during the recording. Keep the whole hand inside the picture and try again."
                : $"+{samples.Count} examples of '{name}' ({total} in total). " +
                  (total < MinSamplesPerGesture ? "Record a little more." : "Record your other gestures too, then press Learn.");
            App.Log.Info($"Recorded {samples.Count} examples of '{name}' ({total} in total).");
            if (samples.Count > 0) AutoSave();
        }
        finally
        {
            if (engine.IsRecording) engine.EndRecording();
            _preview.Countdown = null;
            _preview.Invalidate();
            SetBusy(false);
        }
    }

    // ------------------------------------------------------------------ learn

    private async Task TrainAsync()
    {
        if (_busy) return;
        var p = App.Profile;
        var mine = p.MineIndices;
        if (mine.Length == 0)
        {
            Fail("There is nothing to learn: Google's gestures work without learning. Add a gesture of your own (★) first.");
            return;
        }
        var missing = mine.Select((ci, ordinal) => (p.Classes[ci].Name, Count: p.Samples.CountOf(ordinal))).Where(x => x.Count < MinSamplesPerGesture).ToList();
        if (missing.Count > 0)
        {
            Fail($"Record at least {MinSamplesPerGesture} examples of each of your gestures first. Missing: {string.Join(", ", missing.Select(m => $"{m.Name} ({m.Count})"))}.");
            return;
        }
        MediaPipeGestureModel model;
        try
        {
            model = App.GestureModel;
        }
        catch (Exception ex)
        {
            Fail($"Google's gesture model is not available: {ex.Message}");
            return;
        }
        var (adapter, deviceName, note) = App.ResolveAdapter();
        if (adapter == AppController.NoAdapter)
        {
            Fail(note);
            return;
        }

        SetBusy(true);
        int steps = (int)_steps.Value;
        _chart.Reset(steps);
        _trainProgress.Value = 0;
        _trainNote.ForeColor = Theme.Text;
        _trainNote.Text = $"Learning on {deviceName}...";
        var progress = new Progress<TrainingProgress>(t =>
        {
            _trainProgress.Value = (int)Math.Clamp(t.Step * 1000.0 / t.Steps, 0, 1000);
            _chart.Add(t.Step, t.Loss, t.BatchAccuracy);
        });
        try
        {
            var samples = p.Samples;
            int mineCount = mine.Length;
            var options = new TrainingOptions { Steps = steps };
            bool fallback = App.Settings.AllowCpuFallback;
            var result = await Task.Run(() => GpuTrainer.Train(samples, mineCount, model, options, adapter, fallback, progress));

            p.Weights = result.Weights;
            p.Training = new TrainingSummary
            {
                TrainedUtc = DateTime.UtcNow,
                Device = deviceName,
                Samples = result.Samples,
                Negatives = result.Negatives,
                Steps = result.Steps,
                TotalMs = result.TotalMs,
                TrainAccuracy = result.TrainAccuracy,
                ValidationAccuracy = result.ValidationAccuracy,
                Confusion = result.Confusion,
            };
            App.ProfileEdited();
            string saved = App.SaveProfile();
            _trainProgress.Value = 1000;
            bool good = result.ValidationAccuracy >= 0.9f;
            _trainNote.ForeColor = good ? Theme.Accent : Theme.Warn;
            _trainNote.Text = $"{result.ValidationAccuracy:P1} right on examples it had not seen (training {result.TrainAccuracy:P1}) · " +
                              $"{result.Samples} of your examples + {result.Negatives:N0} generated \"not yours\" hands · {result.Steps} AdamW steps in {result.TotalMs:0} ms on {deviceName} ({result.MsPerStep:0.00} ms/step) · saved to {Path.GetFileName(saved)}." +
                              Lookalikes(p, result) +
                              Hands(p, result) +
                              (good ? " Open the Tester to try it." : " Record more varied examples of the gestures the table mixes up, then learn again.");
            _confusion.SetData(ConfusionNames(p), result.Confusion);
            App.Log.Info($"Learned '{p.Name}': {mineCount} gesture(s) of your own, held-out accuracy {result.ValidationAccuracy:P1}, {result.TotalMs:0} ms on {deviceName}.");
        }
        catch (Exception ex)
        {
            _trainNote.ForeColor = Theme.Danger;
            _trainNote.Text = $"Learning failed: {ex.Message}";
            App.Log.Error("Learning failed", ex);
        }
        finally
        {
            SetBusy(false);
            RefreshAllRows();
        }
    }

    /// <summary>Says when Google also recognises one of your gestures as one of its own (yours wins, but it is good to know).</summary>
    private static string Lookalikes(GestureProfile p, TrainingResult result)
    {
        var mine = p.MineIndices;
        var notes = new List<string>();
        foreach (var l in result.Lookalikes)
        {
            if (l.ReadyLabel == 0 || l.MeanProbability < 0.5f || l.MineOrdinal >= mine.Length) continue;
            var ready = ReadyGestures.All.FirstOrDefault(r => r.Label == l.ReadyLabel);
            var own = p.Classes[mine[l.MineOrdinal]];
            if (ready is null || own.ReadyMadeId == ready.Id) continue;
            notes.Add($"Google sees your \"{own.Name}\" as its \"{ready.Name}\" ({l.MeanProbability:P0}): yours wins when you show it");
        }
        return notes.Count == 0 ? "" : " " + string.Join("; ", notes) + ".";
    }

    /// <summary>Which hands one of your learned gestures works with.</summary>
    private static string HandsHint(GestureProfile p, int classIndex)
    {
        if (!p.IsTrained || p.Weights is not { } w) return "";
        if (w.BothHands(p.MineOrdinal(classIndex))) return " It works with both hands: you can record it with either one.";
        return w.HandsChecked
            ? " It works only with the hand you recorded it with, because another gesture of yours is its mirror image."
            : " It works with the hand you recorded it with; press Learn again so it works with both hands.";
    }

    /// <summary>Says which of your gestures work with both hands, which pairs keep to one hand, and how the other hand did.</summary>
    private static string Hands(GestureProfile p, TrainingResult result)
    {
        var mine = p.MineIndices;
        string Name(int ordinal) => ordinal < mine.Length ? $"\"{p.Classes[mine[ordinal]].Name}\"" : $"#{ordinal + 1}";
        var notes = new List<string>();
        if (result.BothHands.Length > 0 && result.BothHands.All(b => b)) notes.Add("All your gestures work with both hands");
        else if (result.BothHands.Any(b => b))
            notes.Add("Both hands: " + string.Join(", ", result.BothHands.Select((b, j) => (b, j)).Where(x => x.b).Select(x => Name(x.j))));
        foreach (var (first, second) in result.MirrorTwins)
            notes.Add($"{Name(first)} and {Name(second)} are mirror images of each other, so each works only with the hand you recorded it with");
        if (!float.IsNaN(result.OtherHandAccuracy)) notes.Add($"the other hand: {result.OtherHandAccuracy:P1} right");
        if (result.OtherHandExamples > 0)
            notes.Add($"{result.OtherHandExamples} example{(result.OtherHandExamples == 1 ? "" : "s")} made with the other hand were folded in");
        return notes.Count == 0 ? "" : " " + string.Join("; ", notes) + ".";
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        foreach (var c in _lockWhileBusy) c.Enabled = !busy;
        if (!busy) ShowSelectedClass();
    }

    public override void OnProfileChanged() => LoadProfileIntoView();

    public override void OnShown() => LoadProfileIntoView();
}
