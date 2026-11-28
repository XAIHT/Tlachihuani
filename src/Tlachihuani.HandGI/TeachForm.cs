// Tlachihuani — Angela López Mendoza · XAIHT
// tlachihuani:trace-off (counts down and repaints the camera picture many times a second; each step is written explicitly)
using System.Diagnostics;
using System.Globalization;
using HandGI.App.Controls;
using HandGI.Core.Engine;
using HandGI.Core.Gestures;
using Tlachihuani.Contracts;
using Tlachihuani.Tray;

namespace HandGI.App;

// Teaching HandGI a sign of your own, in three steps: name it and say what it does, show it to the
// camera three times, and HandGI learns it. Cancel, at any moment, leaves the signs as they were.
internal sealed class TeachForm : StudioForm
{
    private const int Rounds = 3;
    private const double RoundSeconds = 4;

    private readonly IHandGIHost host;
    private readonly AppController app;
    private readonly PreviewView preview = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly TextBox name = new() { MaxLength = 40 };
    private readonly ComboBox action = SettingsForm.Choice("What the sign does", Padding.Empty);
    private readonly Label nameNote = Note("Next, show your sign to the camera three times, about four seconds each, with your whole hand "
        + "inside the picture. Change it a little each time: nearer, further, a little turned.");
    private readonly Label heading = Studio.Label("", Studio.Strong, Studio.Text);
    private readonly Label message = Note("");
    private readonly ProgressBar progress = new() { Dock = DockStyle.Fill, Maximum = 1000, Style = ProgressBarStyle.Continuous, Margin = Padding.Empty };
    private readonly TableLayoutPanel namePage, showPage;
    private readonly StudioButton start, cancel;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 33 };
    private readonly FrameReport report = new();
    private readonly CancellationTokenSource stop = new();
    private long reportSeq, previewSeq;
    private bool running, closeWhenDone;

    internal TeachForm(IHandGIHost host)
        : base(null, "HandGI · teach a sign", "HandGI   /   Teach a sign", "Teach HandGI a new sign", new Size(940, 600), new Size(860, 560))
    {
        this.host = host;
        app = host.App;
        UiTrace.Ignore(preview);
        UiTrace.Ignore(progress);
        name.Text = "My sign " + (app.Profile.MineCount + 1).ToString(CultureInfo.InvariantCulture);
        foreach (var choice in SettingsForm.Choices) action.Items.Add(SettingsForm.Describe(choice));
        action.SelectedIndex = Array.IndexOf(SettingsForm.Choices, GestureAction.VolumeUp);

        namePage = Studio.Grid([-1], [28, 42, 16, 28, 40, 16, -1]);
        namePage.Controls.Add(Studio.Label("What do you want to call it?", Studio.Strong, Studio.Text), 0, 0);
        namePage.Controls.Add(Studio.Field(name, "Name of the sign"), 0, 1);
        namePage.Controls.Add(Studio.Label("What should it do?", Studio.Strong, Studio.Text), 0, 3);
        namePage.Controls.Add(action, 0, 4);
        namePage.Controls.Add(nameNote, 0, 6);

        showPage = Studio.Grid([-1], [28, -1, 12, 22, -1]);
        showPage.Controls.Add(heading, 0, 0);
        message.Font = Studio.Regular;
        message.ForeColor = Studio.Body;
        showPage.Controls.Add(message, 0, 1);
        showPage.Controls.Add(progress, 0, 3);
        showPage.Visible = false;

        var body = Studio.Grid([-1, 16, 400], [-1]);
        var pages = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        pages.Controls.Add(namePage);
        pages.Controls.Add(showPage);
        body.Controls.Add(pages, 0, 0);
        var frame = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Color.Black };
        frame.Controls.Add(preview);
        body.Controls.Add(Studio.Card(frame, new Padding(8)), 2, 0);
        BodyArea.Controls.Add(body);

        cancel = Command("Cancel", OnCancel);
        start = Command("Start", () => _ = TeachAsync(), ButtonTone.Primary);
        Commands.Controls.Add(cancel);
        Commands.Controls.Add(start);
        AcceptButton = start;
        CancelButton = cancel;
        FooterNote.Text = "HandGI keeps no pictures: only where your fingers were.";

        preview.Placeholder = "The camera is off.";
        timer.Tick += (_, _) => Tick();
        Shown += (_, _) => { timer.Start(); name.Focus(); name.SelectAll(); };
        FormClosing += (_, e) =>
        {
            if (!running) return;
            // Stop the teaching first; it undoes its changes and then closes this window.
            e.Cancel = true;
            closeWhenDone = true;
            stop.Cancel();
        };
        FormClosed += (_, _) => timer.Stop();
    }

    private static Label Note(string text)
    {
        var label = Studio.Label(text, Studio.Small, Studio.Muted);
        label.AutoEllipsis = false;
        label.TextAlign = ContentAlignment.TopLeft;
        return label;
    }

    private void OnCancel()
    {
        if (running)
        {
            closeWhenDone = true;
            stop.Cancel();
            return;
        }
        Close();
    }

    private async Task TeachAsync()
    {
        if (running) return;
        string text = name.Text.Trim();
        if (text.Length == 0)
        {
            name.Focus();
            return;
        }
        var engine = app.Engine;
        if (engine is null || engine.State != EngineState.Running)
        {
            nameNote.Text = "Turning the camera on…";
            await host.StartCameraAsync();
            engine = app.Engine;
            if (engine is null || engine.State != EngineState.Running)
            {
                nameNote.Text = host.ProblemText.Length > 0 ? host.ProblemText : "The camera is off. Turn it on in HandGI's Camera page, then try again.";
                return;
            }
        }

        running = true;
        start.Enabled = false;
        namePage.Visible = false;
        showPage.Visible = true;
        var profile = app.Profile;
        var weights = profile.Weights;
        var training = profile.Training;
        var chosen = SettingsForm.Choices[Math.Max(0, action.SelectedIndex)];
        int index = profile.AddMine(text, chosen);
        app.ProfileEdited();
        int label = profile.MineOrdinal(index);
        TraceLog.I($"HandGI teaching starts: \"{text}\" ({SettingsForm.Describe(chosen).ToLowerInvariant()})");
        bool kept = false;
        try
        {
            int round = 1, misses = 0;
            while (round <= Rounds)
            {
                heading.Text = $"Show your sign   ·   {round} of {Rounds}";
                message.Text = round == 1 ? "Hold your sign in front of the camera when the count ends."
                    : "Once more, a little different: nearer, further, or a little turned.";
                for (int n = 3; n >= 1; n--)
                {
                    preview.Countdown = n.ToString(CultureInfo.InvariantCulture);
                    preview.Invalidate();
                    await Task.Delay(800, stop.Token);
                }
                engine.BeginRecording(label);
                var clock = Stopwatch.StartNew();
                while (clock.Elapsed.TotalSeconds < RoundSeconds)
                {
                    preview.Countdown = "Hold it";
                    progress.Value = (int)Math.Clamp((round - 1 + clock.Elapsed.TotalSeconds / RoundSeconds) / Rounds * 1000, 0, 1000);
                    await Task.Delay(50, stop.Token);
                }
                var samples = engine.EndRecording();
                preview.Countdown = null;
                preview.Invalidate();
                if (samples.Count == 0)
                {
                    TraceLog.W($"HandGI saw no hand while teaching \"{text}\" (round {round})");
                    if (++misses >= 3)
                        throw new InvalidOperationException("HandGI could not see your hand. Keep your whole hand inside the picture, in good light, and try again.");
                    message.Text = "I could not see your hand. Keep your whole hand inside the picture.";
                    await Task.Delay(1500, stop.Token);
                    continue;
                }
                foreach (var sample in samples) profile.Samples.Add(sample, label);
                TraceLog.I($"HandGI teaching \"{text}\": round {round} gave {samples.Count} examples");
                message.Text = "Got it.";
                round++;
                await Task.Delay(900, stop.Token);
            }

            heading.Text = "HandGI is learning your sign…";
            message.Text = "This takes a few seconds.";
            progress.Value = 0;
            var outcome = await SignLearning.LearnAsync(app, new Progress<double>(p => progress.Value = (int)Math.Clamp(p * 1000, 0, 1000)));
            if (!outcome.Learned) throw new InvalidOperationException(outcome.Message);
            kept = true;
            heading.Text = "Done!";
            message.Text = outcome.Message + " Try it now: under the picture, HandGI says what it sees.";
            progress.Value = 1000;
            cancel.Text = "Close";
            start.Visible = false;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            TraceLog.I($"HandGI teaching of \"{text}\" was cancelled");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            TraceLog.E($"HandGI could not learn \"{text}\"", ex);
            heading.Text = "HandGI could not learn this sign";
            message.Text = ex.Message;
            cancel.Text = "Close";
        }
        finally
        {
            if (engine.IsRecording) engine.EndRecording();
            preview.Countdown = null;
            if (!kept) Undo(index, weights, training, text);
            running = false;
        }
        if (closeWhenDone) Close();
    }

    // Leaves the signs exactly as they were before this window started teaching.
    private void Undo(int index, ClassifierWeights? weights, TrainingSummary? training, string text)
    {
        var profile = app.Profile;
        if (index < profile.Classes.Count && !profile.Classes[index].IsReadyMade) profile.RemoveMine(index);
        profile.Weights = weights;
        profile.Training = training;
        app.ProfileEdited();
        TraceLog.I($"HandGI undid the teaching of \"{text}\"; the signs are as they were");
    }

    private void Tick()
    {
        var engine = app.Engine;
        if (engine is null || !engine.IsRunning) return;
        engine.TryCopyPreview(ref previewSeq, preview.UpdateFrame);
        if (engine.TryGetReport(report, ref reportSeq)) preview.UpdateReport(report);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Dispose();
            stop.Dispose();
        }
        base.Dispose(disposing);
    }
}
