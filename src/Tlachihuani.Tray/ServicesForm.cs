// Tlachihuani — Angela López Mendoza · XAIHT
using System.Diagnostics;
using System.Text.Json.Nodes;
using Tlachihuani.Core;
using Tlachihuani.Windows;

namespace Tlachihuani.Tray;

internal sealed partial class ServicesForm : StudioForm
{
    private readonly CancellationTokenSource closing = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 4000 };
    private readonly SemaphoreSlim gate = new(1);
    private bool changing;
    private string impact = "", jobsProblem = "";
    private int active, records;
    private bool enabled, engine, web, catalogAvailable;
    private string host = "unknown", rcState = "unknown";
    private (Mood Mood, string Text) rcLook = (Mood.Busy, "Checking…");
    private (bool Running, int Runs) flow;
    private HandGIControl.State? hand;

    private async Task Run(Func<Task>? action = null, string actionName = "Refresh Services")
    {
        if (changing || closing.IsCancellationRequested) return;
        var token = closing.Token;
        if (action is null && !gate.Wait(0)) return;
        if (action is not null)
        {
            changing = true; UpdateActions();
            try { await gate.WaitAsync(token); } catch (OperationCanceledException) { return; }
        }
        var startedAt = DateTimeOffset.Now;
        bool actionCompleted = false;
        try
        {
            if (action is not null) { await action(); actionCompleted = true; }
            if (!token.IsCancellationRequested) await RefreshState();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (!token.IsCancellationRequested)
            {
                if (action is not null) ServiceDialogs.Failure(this, ServiceErrorReport.Create(actionName, ex,
                    startedAt: startedAt, context: $"Action completed={actionCompleted}; last observed Jobs SCM={host}; "
                        + $"catalog available={catalogAvailable}; enabled={enabled}; engine={engine}; web={web}; "
                        + $"Remote-Controller SCM={rcState}; Flow Pills open={flow.Running}; HandGI={hand?.Word ?? "unknown"}. "
                        + "These are the last panel observations and may be stale. "
                        + (actionCompleted ? "The action completed; the subsequent status refresh failed. Do not repeat the action based on this refresh failure." : "The action did not return successfully.")));
                else { PaintState(); FooterNote.Text = "Status could not be refreshed   ·   " + ex.GetType().Name; }
            }
        }
        finally
        {
            gate.Release();
            if (action is not null) changing = false;
            if (!token.IsCancellationRequested) { PaintState(); UpdateActions(); }
        }
    }
    private Task<JsonObject> Call(JsonObject request) => Call(request, TimeSpan.FromSeconds(8));
    private async Task<JsonObject> Call(JsonObject request, TimeSpan limit)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(closing.Token);
        timeout.CancelAfter(limit);
        return await Task.Run(() => JobsEndpoint.CallInstalledAsync(request, timeout.Token), timeout.Token);
    }

    // Each service is asked on its own, at the same time; one that does not answer only marks its
    // own section.
    private async Task RefreshState()
    {
        var token = closing.Token;
        var jobs = ObserveJobs(token);
        var remote = ObserveRemoteController(token);
        var gestures = HandGIControl.ObserveAsync(token);
        var pills = Task.Run(FlowPillsLauncher.Observe, token);
        try { await Task.WhenAll(jobs, remote, gestures, pills); } catch (Exception ex) when (ex is not OutOfMemoryException) { }
        if (token.IsCancellationRequested) return;
        (rcState, rcLook) = remote.IsCompletedSuccessfully ? remote.Result : ("unknown", (Mood.Problem, "Status unavailable"));
        hand = gestures.IsCompletedSuccessfully ? gestures.Result
            : new("unreachable", true, false, false, 0, "", gestures.Exception?.InnerException?.Message ?? "");
        flow = pills.IsCompletedSuccessfully ? pills.Result : (false, 0);
        PaintState(); FooterNote.Text = $"Observed {DateTime.Now:HH:mm:ss}";
    }

    private async Task ObserveJobs(CancellationToken token)
    {
        enabled = engine = web = catalogAvailable = false; impact = jobsProblem = ""; active = records = 0;
        try
        {
            host = (await Task.Run(() => ScmBackend.ForJobs().Query(), token)).State;
            if (host != "running") return;
            var catalog = await Call(new() { ["op"] = "catalog" });
            var result = catalog["providers"]!.AsArray().First(item => (string?)item?["provider"] == "torrenter")!;
            catalogAvailable = true;
            enabled = (bool?)result["enabled"] == true; engine = (bool?)result["engine"] == true; web = (bool?)result["web"] == true;
            if (result["impact"] is JsonObject counts)
            {
                active = Count(counts["active"]); records = Count(counts["records"]);
                impact = $"{counts["active"]} active transfers across {counts["records"]} records.";
            }
        }
        catch (Exception ex) when (!token.IsCancellationRequested && ex is not OutOfMemoryException)
        {
            if (host == "unknown") host = "unavailable";
            jobsProblem = ex is OperationCanceledException or TimeoutException ? "not answering" : ex.Message;
        }
    }

    private static async Task<(string State, (Mood Mood, string Text) Look)> ObserveRemoteController(CancellationToken token)
    {
        string state;
        try { state = (await Task.Run(() => ScmBackend.ForRemoteController().Query(), token)).State; }
        catch (Exception ex) when (!token.IsCancellationRequested && ex is not OutOfMemoryException) { return ("unknown", (Mood.Problem, "Status unavailable")); }
        switch (state)
        {
            case "not_installed": return (state, (Mood.Off, "Not installed"));
            case "stopped": return (state, (Mood.Off, "Stopped"));
            case "start_pending": return (state, (Mood.Busy, "Starting…"));
            case "stop_pending": return (state, (Mood.Busy, "Stopping…"));
            case "running": break;
            default: return (state, (Mood.Problem, StatusForm.Pretty(state)));
        }
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(4));
            var status = await RcEndpoint.CallInstalledAsync(new() { ["op"] = "status" }, deadline.Token);
            bool ready = (bool?)status["listener"] == true && (string?)status["health"] == "ready";
            int sessions = Count(status["sessions"]);
            if (!ready) return (state, (Mood.Busy, "Running   ·   " + ((string?)status["health"] ?? "not ready yet")));
            return (state, (Mood.On, "Running   ·   ready" + (sessions == 0 ? "" : sessions == 1 ? "   ·   1 remote session" : $"   ·   {sessions} remote sessions")));
        }
        catch (Exception ex) when (!token.IsCancellationRequested && ex is not OutOfMemoryException)
        {
            return (state, (Mood.Problem, "Running   ·   not answering"));
        }
    }

    private static int Count(JsonNode? node) => node is JsonValue value && value.TryGetValue(out int count) ? count : 0;

    private void UpdateActions()
    {
        bool idle = !changing, jobsUp = host == "running";
        buttons["jobs.start"].Enabled = idle && host == "stopped";
        buttons["jobs.stop"].Enabled = idle && jobsUp;
        buttons["engine.start"].Enabled = idle && (host == "stopped" || (jobsUp && catalogAvailable && !enabled));
        buttons["engine.stop"].Enabled = buttons["engine.restart"].Enabled = buttons["web.restart"].Enabled = idle && jobsUp && enabled;
        buttons["web.open"].Enabled = buttons["access.downloads"].Enabled = idle && web;
        foreach (string key in new[] { "access.erasure", "access.issue", "access.revoke" })
            buttons[key].Enabled = idle && engine;
        buttons["rc.start"].Enabled = idle && rcState == "stopped";
        buttons["rc.stop"].Enabled = idle && rcState is "running" or "paused";
        buttons["flow.stop"].Enabled = idle && flow.Running;
        var h = hand;
        bool installed = h is not null && h.Word != "not_installed";
        buttons["handgi.start"].Enabled = idle && installed && h is { Running: false };
        buttons["handgi.stop"].Enabled = idle && h is { Running: true };
        var toggle = buttons["handgi.toggle"];
        bool on = h is { HandControlOn: true };
        toggle.Enabled = idle && h is { Answering: true, Word: "watching" };
        toggle.Text = on ? "Pause" : "Turn on";
        toggle.AccessibleName = on ? "Pause hand control" : "Turn hand control on";
        tips.SetToolTip(toggle, toggle.AccessibleName);
        foreach (string key in new[] { "signs.list", "signs.camera", "signs.try" })
            buttons[key].Enabled = idle && installed;
    }

    // ---- Jobs host, Torrent engine and REST workspace --------------------------------------
    private async Task HostAction(string op)
    {
        if (op == "stop" && !ServiceDialogs.Confirm(this, "Stop the Jobs host?", "Active transfers will be checkpointed. " + impact, "Stop Jobs")) return;
        if (op == "start") ForcedStop.CancelRestartGuards();
        jobsLine.Show(Mood.Busy, op == "stop" ? "Stopping…" : "Starting…");
        await Task.Run(async () => { var scm = ScmBackend.ForJobs(); scm.ValidateJobsInstallation(); await new Lifecycle(scm).ExecuteAsync(op, TimeSpan.FromSeconds(30), closing.Token); });
    }
    // The engine lives in the Jobs host, so Start brings the host up first when it is stopped.
    private async Task StartEngine()
    {
        if (host != "running")
        {
            engineLine.Show(Mood.Busy, "Starting the Jobs host first…");
            await HostAction("start");
        }
        engineLine.Show(Mood.Busy, "Starting…");
        await Call(new() { ["op"] = "start" }, TimeSpan.FromSeconds(40));
    }
    private async Task Provider(string op)
    {
        if (!ServiceDialogs.Confirm(this, op == "stop" ? "Stop Torrenter?" : "Restart Torrenter?", "Active transfers will be checkpointed. " + impact, op == "stop" ? "Stop provider" : "Restart provider")) return;
        engineLine.Show(Mood.Busy, op == "stop" ? "Stopping…" : "Restarting…");
        await Call(new() { ["op"] = op });
    }
    private async Task RestartWeb()
    {
        webLine.Show(Mood.Busy, "Restarting…");
        await Call(new() { ["op"] = "restart-web" });
    }
    // "Open workspace" always passes through the Tlamatini login page (?signin=1),
    // so the queue shown belongs only to the account that signs in there, even if
    // the browser is already signed in with another account. "Open downloads"
    // keeps the current login, as its Access-tab label says.
    private async Task OpenWorkspace(bool signIn)
    {
        var current = await Call(new() { ["op"] = "status" });
        if ((bool?)current["web"] != true) throw new InvalidOperationException("The workspace is offline.");
        Process.Start(new ProcessStartInfo(signIn ? "http://127.0.0.1:12064/?signin=1" : "http://127.0.0.1:12064/") { UseShellExecute = true });
    }

    // ---- Remote-Controller, Flow Pills and HandGI -----------------------------------------
    private async Task RemoteController(string op)
    {
        if (op == "stop" && !ServiceDialogs.Confirm(this, "Stop Remote-Controller?", "Remote clients will disconnect and RC schedules will pause. Tlamatini and Torrenter keep running.", "Stop server")) return;
        if (op == "start") ForcedStop.CancelRestartGuards();
        rcLine.Show(Mood.Busy, op == "stop" ? "Stopping…" : "Starting…");
        await RcLifecycle.ExecuteAsync(op, closing.Token);
    }
    private void OpenFlowPills()
    {
        FlowPillsLauncher.Open(this);
        if (!flow.Running) flowLine.Show(Mood.Busy, "Opening…");
    }
    private async Task StopFlowPills()
    {
        if (flow.Runs > 0 && !ServiceDialogs.Confirm(this, "Stop Flow Pills?", Flows(flow.Runs, "", "No flow") + " will be stopped the way Tlamatini's Stop does, then Flow Pills closes.", "Stop flows")) return;
        flowLine.Show(Mood.Busy, "Stopping…");
        await FlowPillsLauncher.StopAsync(closing.Token);
    }
    private async Task StartHandGI()
    {
        handLine.Show(Mood.Busy, "Starting…");
        await HandGIControl.StartAsync(closing.Token);
    }
    private async Task StopHandGI()
    {
        handLine.Show(Mood.Busy, "Stopping…");
        await HandGIControl.StopAsync(closing.Token);
    }
    private Task ToggleHandControl() => HandGIControl.SetHandControlAsync(hand is not { HandControlOn: true }, closing.Token);
    private async Task ShowHandGI(string page)
    {
        if (hand is not { Running: true }) handLine.Show(Mood.Busy, "Starting…");
        await HandGIControl.ShowAsync(page, closing.Token);
    }

    // ---- Access ------------------------------------------------------------------------------
    private async Task Revoke()
    {
        if (ServiceDialogs.Confirm(this, "Revoke account access?", "This revokes all REST sessions and enrolled gateways for your Windows account.", "Revoke access"))
            await Call(new() { ["op"] = "revoke" });
    }
    private async Task Issue()
    {
        var request = ServiceDialogs.RequestCredential(this);
        if (request is null) return;
        var result = await Call(new() { ["op"] = "issue", ["profile"] = request.Value.Account, ["scope"] = request.Value.Scope });
        if (!closing.IsCancellationRequested) ServiceDialogs.ShowCredential(this, (string?)result["access_token"] ?? throw new InvalidDataException("No credential was returned."));
    }
    private async Task ReviewDeletion()
    {
        var pending = await Call(new() { ["op"] = "pending-deletions" });
        if (closing.IsCancellationRequested) return;
        var items = pending["items"]!.AsArray();
        if (items.Count == 0) { ServiceDialogs.Notice(this, "File erasure", "There are no pending file-erasure requests."); return; }
        foreach (var item in items)
        {
            var preview = await Call(new() { ["op"] = "deletion-details", ["preview_id"] = (string?)item!["preview_id"] });
            if (closing.IsCancellationRequested) return;
            if (ServiceDialogs.ReviewFiles(this, preview))
                await Call(new() { ["op"] = "approve-delete", ["preview_id"] = (string?)item["preview_id"], ["manifest_digest"] = (string?)preview["manifest_digest"] });
        }
    }
    protected override void Dispose(bool disposing) { if (disposing) { timer.Dispose(); closing.Dispose(); tips.Dispose(); } base.Dispose(disposing); }
}
