// PORT-ONLY: Tactus, the separate workspace (unit P18, preview; OD-0140, OD-0145, ED-NEW-pcws-1, ED-NEW-pcws-3,
// ED-NEW-pcws-4, ED-NEW-pcws-8; DESIGN.md 2.2, 2.13). The engine's side of the workspace. pc_launch_app routes by place
// (WorkspaceRouter): a workspace that cannot be used is refused with not_in_workspace before the user is asked anything,
// and an app asked for the workspace that opens on the user's desk instead (one copy per user across sessions) is reported
// with not_in_workspace and its desk windows, and is kept on the desk from then on. pc_list_apps lists the workspace's apps
// after the desk's, each window marked "in the workspace". A step on a workspace window (its id carries bit 62), a zoom
// into a look taken there and a batch in its windows pass the same gates as on the desk (Tactus on, attended, not stopped,
// the stop key armed, the deny list, admin rights, the user's approval, Pause before it starts, the app's own stream, the
// audit line) and then run in the agent's engine inside the child session; the agent's asks come back here and show on the
// pill and in the chat. While the user has taken over in "Watch workspace" the step waits (hands off) until they give it
// back. nth never changes a Windows setting here.
using System.Text;
using System.Text.Json.Nodes;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl.Workspace;

namespace Marrow.Services.Port.PcControl;

public sealed partial class PcControlEngine
{
    // Looks taken inside the workspace (obs number to the window's tagged handle), for pc_zoom.
    private readonly Dictionary<long, nint> workspaceLooks = [];

    // ----- pc_launch_app -----

    private WorkspaceRoute RouteLaunch(string place, PcAppIdentity app, PcSettings settings)
    {
        if (parts.InWorkspace || place == "desk") return WorkspaceRoute.Desk;
        var host = parts.Workspace;
        var released = host is { Released: true };
        if (!released && place == "auto") return WorkspaceRoute.Desk;
        var status = released ? host!.Status() : WorkspaceStatus.NotSetUp;
        var openOnDesk = place == "auto" && parts.Catalog.Windows().Any(w => w.App.Key == app.Key && !parts.Own.Owns(w.Handle));
        return parts.Router.Route(place, settings.WorkspaceUse, released, status, openOnDesk, app.Key);
    }

    private async Task<PcResult> LaunchInWorkspace(PcAppIdentity app, string target, HashSet<nint> before, PcCall call)
    {
        var host = parts.Workspace!;
        call.Audit.Rung = "workspace";
        var launch = await host.Launch(target, call.Token).ConfigureAwait(false);
        call.Timings.Mark("launch");
        if (!launch.Started)
            return PcResult.Fail(PcErrorCode.NotInWorkspace, "nothing was started in the separate workspace: " + (launch.Problem ?? "it gave no reason"));
        var inside = launch.Windows.Select(w => w.Tagged()).ToList();
        if (inside.Count > 0)
        {
            var text = new StringBuilder("started in the separate workspace. new windows:");
            foreach (var window in inside)
            {
                Remember(window);
                text.Append("\n  ").Append(PcWindowRef.Format(window.Handle)).Append(' ').Append(PcWindowLine.Quote(TreeFormatter.Clean(window.Title, 120)));
            }
            return PcResult.Success(body: text.ToString());
        }
        // Nothing opened inside: an app that allows one copy per user hands the start to its copy on the desk.
        var desk = parts.Catalog.Windows().Where(w => !before.Contains(w.Handle) && !parts.Own.Owns(w.Handle) && w.App.Key == app.Key).ToList();
        if (desk.Count > 0)
        {
            parts.Router.OpenedOnDesk(app.Key);
            foreach (var window in desk) Remember(window);
            return PcResult.Fail(PcErrorCode.NotInWorkspace, "the app opened on the user's desk, not in the separate workspace (it allows one copy per user): " + WorkspaceRouter.Windows(desk));
        }
        return PcResult.Success(body: "started in the separate workspace; no window yet. Call pc_list_apps in a moment.");
    }

    // ----- pc_list_apps -----

    // The workspace's line and its apps, after the desk's (DESIGN.md 2.13: "pc_list_apps shows both places").
    private async Task ListWorkspace(StringBuilder text, PcSettings settings, CancellationToken cancellationToken)
    {
        if (parts.InWorkspace || parts.Workspace is not { Released: true } host) return;
        var status = host.Status();
        text.Append("separate workspace: ").Append(status.State switch
        {
            WorkspaceState.Running => "running",
            WorkspaceState.Ready => "ready (start an app there with pc_launch_app place workspace)",
            _ => "not set up",
        }).Append('\n');
        if (status.State != WorkspaceState.Running) return;
        var windows = await host.Windows(cancellationToken).ConfigureAwait(false);
        foreach (var app in windows.Select(w => (Window: w, Info: w.Tagged())).GroupBy(w => w.Info.App.Key))
        {
            var first = app.First();
            var verdict = PcPolicy.Classify(first.Info.App, settings.UserDeniedApps);
            var approved = verdict.Denied ? "blocked (" + verdict.Reason + ")" : first.Window.Elevated ? "blocked (runs with admin rights)" : Granted(first.Info.App);
            text.Append("app ").Append(AppName(first.Info)).Append(" exe ").Append(first.Info.ExeName);
            if (first.Info.Aumid is { Length: > 0 } aumid) text.Append(" app_id ").Append(PcWindowLine.Quote(aumid));
            text.Append(" approved: ").Append(approved).Append(", state: ").Append(PcPauseText.Wire(State(first.Info.App.Key))).Append(", place: workspace\n");
            foreach (var (_, info) in app)
            {
                lock (gate) known[info.Handle] = info;
                text.Append("  ").Append(PcWindowRef.Format(info.Handle)).Append(' ').Append(PcWindowLine.Quote(TreeFormatter.Clean(info.Title, 120))).Append(", in the workspace");
                if (info.Minimized) text.Append(", minimized");
                if (parts.Ownership.Holder(info.Handle, caller.Thread) is { } holder) text.Append(", in use by ").Append(holder);
                text.Append('\n');
            }
        }
    }

    // ----- steps in the workspace -----

    // A look taken inside the workspace: the window it was taken in; null for a look of the desk's.
    private nint? WorkspaceLook(string? obs)
    {
        if (parts.InWorkspace || !WorkspaceRefs.IsWorkspaceObservation(obs)) return null;
        PcObsId.TryParse(obs, out var number);
        lock (gate)
        {
            return workspaceLooks.TryGetValue(number, out var window) ? window : throw new PcArgsException("obs is not one of this chat's looks; call pc_observe");
        }
    }

    // A batch in the workspace's windows: its main window; null for a batch on the desk. A batch runs in one place.
    private nint? WorkspaceBatch(JsonObject args)
    {
        if (parts.InWorkspace) return null;
        var main = Window(PcArgs.Str(args, "window"));
        var inside = WorkspaceRefs.IsWorkspace(main);
        if (args["actions"] is JsonArray actions)
        {
            foreach (var step in actions.OfType<JsonObject>())
            {
                if (PcArgs.Str(step, "window") is { } window && !window.StartsWith('$') && PcWindowRef.TryParse(window, out var handle) && WorkspaceRefs.IsWorkspace(handle) != inside)
                    throw new PcArgsException("a batch runs either on the user's desk or in the separate workspace, not both; send one batch for each");
            }
        }
        return inside ? main : null;
    }

    // The desk's gates for a workspace window, then the step in the agent's engine, in the app's own stream here.
    private async Task<PcResult> InWorkspace(nint handle, PcCapability capability, PcCall call, string tool, JsonObject args)
    {
        if (parts.Workspace is not { Released: true } host) return PcResult.Fail(PcErrorCode.WindowGone, "the separate workspace is not running");
        if (parts.Security.SecureDesktop()) return PcResult.Fail(PcErrorCode.SecureDesktop, "a Windows security prompt is open");
        if (await host.Window(WorkspaceRefs.Untag(handle), call.Token).ConfigureAwait(false) is not { } window)
            return PcResult.Fail(PcErrorCode.WindowGone, "the window is gone");
        var info = window.Tagged();
        if (Reused(info)) return PcResult.Fail(PcErrorCode.WindowGone, "the window closed and Windows gave its id to another window");
        call.Audit.App = info.ExeName;
        call.Audit.Window = info.Title;
        call.Audit.Rung = "workspace";
        var verdict = PcPolicy.Classify(info.App, parts.Settings().UserDeniedApps);
        if (verdict.Denied) return PcResult.Fail(PcErrorCode.DeniedApp, verdict.Reason ?? "this app is not allowed");
        if (PcPolicy.IsRunDialog(info.ExeName, info.ClassName, info.Title)) return PcResult.Fail(PcErrorCode.DeniedApp, "the Run dialog runs commands; Tactus never drives it");
        if (window.Elevated) return PcResult.Fail(PcErrorCode.Elevated, "the app runs with admin rights");
        lock (gate)
        {
            if (stoppedApps.Contains(info.App.Key)) return PcResult.Fail(PcErrorCode.StoppedByUser, "the user stopped Tactus in this app for this turn");
        }
        if (parts.Ownership.Holder(handle, caller.Thread) is { } holder) return PcResult.Fail(PcErrorCode.BusyWindow, holder + " is using this window");
        if (!parts.StopKey.Arm())
            return PcResult.Fail(PcErrorCode.NotSupported, "the stop key " + parts.StopKey.Chord + " is taken by another app, so Tactus cannot start. Ask the user to pick another stop key in Settings, Tactus");
        Remember(info);
        using var joined = parts.Pause.Join(caller.Thread, info.App.Key, call.Wait);
        call.Timings.Mark("guard");
        PcApproval approval;
        using (call.Wait.Waiting(PcWaitReason.Approval)) approval = await Approve(info.App, AppName(info), capability, call.Token).ConfigureAwait(false);
        call.Timings.Mark("approve");
        if (approval is not (PcApproval.Chat or PcApproval.Always))
        {
            var reason = approval == PcApproval.TimedOut ? null : parts.Approver.Reason(caller.Thread, info.App, capability);
            return PcResult.Fail(PcErrorCode.NotApproved, approval == PcApproval.TimedOut ? "the user did not answer"
                : reason == PcBrokerApprover.SeeOnly ? reason
                : "the user did not allow Tactus to " + (capability == PcCapability.See ? "see" : "use") + " this app" + (reason is null ? "" : ". The user said: " + reason));
        }
        if (StoppedHere(info.App.Key) is { } stopped) return stopped;
        if (capability == PcCapability.Act && parts.Ownership.Claim(handle, caller.Thread, caller.Label) is { } other)
            return PcResult.Fail(PcErrorCode.BusyWindow, other + " is using this window");
        using var watch = parts.Control.Watch(call.Token, caller.Thread, info.App.Key, handle);
        try
        {
            return await parts.Lanes.Run(info.App.Key, async laneToken =>
            {
                var queued = call.With(laneToken);
                queued.Timings.Mark("queue");
                var resumed = await HoldInWorkspace(host, info, capability, approval, queued).ConfigureAwait(false);
                if (resumed.Refused is { } refused) return refused;
                var forwarded = new WorkspaceCall(caller.Thread, tool, args)
                {
                    Settings = parts.Settings(),
                    Vision = caller.Vision,
                    Grid = caller.Grid,
                    Head = caller.Head,
                    Model = caller.Model,
                };
                var result = await host.Call(forwarded, new ForwardedAsks(this, queued), laneToken).ConfigureAwait(false);
                queued.Timings.Mark("workspace");
                RememberLook(handle, result);
                // the typed text joins the audit line as on the desk (its own setting decides); a password field never does
                if (tool is PcToolSchemas.Type or PcToolSchemas.SetValue && result.Error != PcErrorCode.PasswordField)
                    call.Audit.Text = PcArgs.Str(args, tool == PcToolSchemas.Type ? "text" : "value");
                return resumed.Resumed is null ? result : result with { Resumed = resumed.Resumed };
            }, watch.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (watch.Stopped)
        {
            return watch.StoppedBy == PcStopSource.Sleep
                ? PcResult.Fail(PcErrorCode.Locked, "the PC went to sleep")
                : PcResult.Fail(PcErrorCode.StoppedByUser, "the user stopped Tactus");
        }
    }

    // Before a step in the workspace starts: a paused app holds here (DESIGN.md 10.1) and the facts are checked again at
    // the Resume; while the user drives the workspace in "Watch workspace" the step waits, hands off, until they give it
    // back (2.13). A step forwarded to the agent runs there as one unit; the next one holds here.
    private async Task<(PcResult? Refused, PcResumed? Resumed)> HoldInWorkspace(IWorkspaceHost host, PcWindowInfo info, PcCapability capability, PcApproval approval, PcCall call)
    {
        var app = info.App.Key;
        PcResumed? resumed = null;
        while (true)
        {
            if (parts.Pause.IsPaused(app))
            {
                await parts.Pause.Held(app, call.Token).ConfigureAwait(false);
                call.Token.ThrowIfCancellationRequested();
                var resumedAt = parts.Pause.LastPause(app)?.EndMs ?? parts.Clock.NowMs;
                if (StoppedHere(app) is { } stopped) return (stopped, null);
                var settings = parts.Settings();
                if (!settings.Enabled) return (PcResult.Fail(PcErrorCode.PcOff, "Tactus is off"), null);
                var verdict = PcPolicy.Classify(info.App, settings.UserDeniedApps);
                if (verdict.Denied) return (PcResult.Fail(PcErrorCode.DeniedApp, verdict.Reason ?? "this app is not allowed"), null);
                if (await host.Window(WorkspaceRefs.Untag(info.Handle), call.Token).ConfigureAwait(false) is not { } live || !info.SameWindow(live.Tagged()))
                    return (PcResult.Fail(PcErrorCode.WindowGone, "the window closed while Tactus was paused"), null);
                if (live.Elevated) return (PcResult.Fail(PcErrorCode.Elevated, "the app runs with admin rights"), null);
                resumed = new PcResumed(Math.Max(0, parts.Clock.NowMs - resumedAt), false, 0);
                call.Timings.Mark("resume");
                continue;
            }
            var takenOver = host.WhileTakenOver(call.Token);
            if (!takenOver.IsCompleted)
            {
                using (call.Wait.Waiting(PcWaitReason.HandsOff)) await takenOver.ConfigureAwait(false);
                continue;
            }
            await takenOver.ConfigureAwait(false);
            if (approval == PcApproval.Always && parts.Approver.Standing(info.App, capability) != PcApproval.Always)
                return (PcResult.Fail(PcErrorCode.NotApproved, "the app was removed from Tactus's apps while the step waited"), null);
            return (null, resumed);
        }
    }

    // A look the agent took (its id above 2^40) is kept with its window for pc_zoom; the oldest goes first.
    private void RememberLook(nint handle, PcResult result)
    {
        var obs = result.Window?.Obs ?? result.Image?.ObsId;
        if (!PcObsId.TryParse(obs, out var number) || number <= WorkspaceRefs.ObservationBase) return;
        lock (gate)
        {
            workspaceLooks[number] = handle;
            while (workspaceLooks.Count > KeptObservations) workspaceLooks.Remove(workspaceLooks.Keys.Min());
        }
    }

    /// <summary>The asks of one step in the workspace: the chat's approvals and guarded-action cards, marked on the step's wait.</summary>
    private sealed class ForwardedAsks(PcControlEngine engine, PcCall call) : IWorkspaceAsks
    {
        public async Task<WorkspaceApproval> Approve(PcAppIdentity app, string appName, PcCapability capability, CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, call.Token);
            var verdict = PcPolicy.Classify(app, engine.parts.Settings().UserDeniedApps);
            if (verdict.Denied) return new WorkspaceApproval(PcApproval.Denied, null);
            PcApproval answer;
            using (call.Wait.Waiting(PcWaitReason.Approval)) answer = await engine.Approve(app, appName, capability, linked.Token).ConfigureAwait(false);
            var said = answer is PcApproval.Chat or PcApproval.Always ? null : engine.parts.Approver.Reason(engine.caller.Thread, app, capability);
            return new WorkspaceApproval(answer, said);
        }

        public async Task<WorkspaceConfirm> Confirm(string word, string elementName, string appName, CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, call.Token);
            bool allowed;
            using (call.Wait.Waiting(PcWaitReason.ConfirmCard))
                allowed = await engine.parts.Confirmer.Confirm(new PcAskSource(engine.caller.Thread, appName, engine.caller.Head), word, elementName, appName, linked.Token).ConfigureAwait(false);
            return new WorkspaceConfirm(allowed, allowed ? null : engine.parts.Confirmer.Note(engine.caller.Thread));
        }
    }
}
