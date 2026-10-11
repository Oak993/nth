// PORT-ONLY: Tactus, PC control (OD-0140, OD-0141, OD-0144, ED-NEW-pcctl-1, ED-NEW-pcctl-2, ED-NEW-pcctl-6,
// ED-NEW-pcctl-7, ED-NEW-pcctl-8; DESIGN.md 2, 4). The engine behind the nth-pc tools, one per chat thread (the lead chat or
// a Hydra head). Every call passes the same gates in order: Tactus on, a run the user is watching, not stopped this turn,
// the stop key armed (fail closed), no Windows prompt in front, the window alive and still the same window (a reused handle
// is refused), not nth's own, an allowed app that is not elevated, not held by another chat, and the user's approval for
// seeing or acting in that app. Then the call runs in that app's own stream (PcLanes), so several apps are driven side by
// side and one app's calls never overlap. No call blocks for more than 25 s: past that it answers waiting_for_user with a
// token (PcPendingSteps). Targets are found again by identity just before acting, every input goes down the action ladder
// without activating any window, the window is waited on by its change events, and every action is checked locally
// before its call returns. A paused app's steps hold in its stream until the user resumes it (PcPauseControl,
// ED-NEW-pcctl-14). Each call's phase timings go to the audit log.
using System.Globalization;
using System.Text.Json.Nodes;
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl;

/// <summary>
/// Who is calling: the chat thread, whether the run is attended now (checked at every call: a Hydra head stops starting
/// steps when the user is away, critique C14), whether the model takes images, the Hydra head's name when it is not the
/// lead chat, and the model's own screenshot grid when it differs from the setting (critique C27).
/// </summary>
public sealed record PcCaller(string Thread, Func<bool> Attended, bool Vision)
{
    public string? Head { get; init; }
    public PcGridProfile? Grid { get; init; }

    /// <summary>The model's name, for a question's label ("GPT-6.1 Sol asks").</summary>
    public string? Model { get; init; }

    /// <summary>The name busy_window and the cards show for this chat.</summary>
    public string Label => Head ?? "another chat";
}

/// <summary>Everything the engine drives. The app binds the real parts; tests bind fakes. Lanes, run control and ownership are shared by every chat.</summary>
public sealed class PcEngineParts
{
    public required IWindowCatalog Catalog { get; init; }
    public required ISecurityProbe Security { get; init; }
    public required IUiaReader Reader { get; init; }
    public required IPcChangeFeed Feed { get; init; }
    public required PcActionLadder Ladder { get; init; }
    public required IForeground Foreground { get; init; }
    public required IOwnWindows Own { get; init; }
    public required IPcApprover Approver { get; init; }
    public required IPcConfirmer Confirmer { get; init; }
    public required IPcStopKey StopKey { get; init; }
    public required PcRunControl Control { get; init; }

    /// <summary>Pause and Resume, shared by every chat like the run control (DESIGN.md 10.1).</summary>
    public required PcPauseControl Pause { get; init; }

    /// <summary>Every pending ask, from every chat (DESIGN.md 10.4, 10.7): pc_ask questions go here.</summary>
    public required PcAskBroker Asks { get; init; }

    public required IPcAudit Audit { get; init; }
    public required IPcClock Clock { get; init; }
    public required IAppLauncher Launcher { get; init; }
    public required PcLanes Lanes { get; init; }
    public required PcWindowOwnership Ownership { get; init; }
    public required Func<PcSettings> Settings { get; init; }
    public PcCapturePipeline? Capture { get; init; }

    /// <summary>The clock of the waits for the user (the 25 s budget, the 60 s token expiry); the engine clock when null.</summary>
    public IPcClock? WaitClock { get; init; }

    public Func<DateTimeOffset> Now { get; init; } = () => DateTimeOffset.Now;
}

/// <summary>
/// One look at a window: its number, the window's facts then, its tree (stable element numbers), the change version the
/// tree was read at, the screenshot when there was one, and the windows of the app then (for the stale-view rule).
/// </summary>
public sealed record PcObservation(long Number, PcWindowInfo Window, PcTree Tree, long Version)
{
    public string Id => PcObsId.Format(Number);
    public PcShot? Shot { get; init; }
    public IReadOnlySet<nint> AppWindows { get; init; } = new HashSet<nint>();

    /// <summary>When Tactus looked (engine clock): input in the window after this makes the look dirty (DESIGN.md 2.9).</summary>
    public long At { get; init; }
}

/// <summary>
/// One call's running context: its phase timings, its audit line, its token (cancelled by a stop), its wait state (shared
/// by a batch's steps), and in a batch the look the step was planned from.
/// </summary>
public sealed class PcCall(PcTimings timings, PcAuditDraft audit, CancellationToken token, PcWaitState wait)
{
    public PcTimings Timings { get; } = timings;
    public PcAuditDraft Audit { get; } = audit;
    public CancellationToken Token { get; } = token;
    public PcWaitState Wait { get; } = wait;
    public bool InBatch { get; init; }

    /// <summary>In a batch: the window's look before the batch. Element numbers mean what they meant there.</summary>
    public PcObservation? Base { get; init; }

    /// <summary>What the step produced for later batch steps; shared by the copies <see cref="With"/> makes.</summary>
    public PcStepOutput Output { get; private init; } = new();

    /// <summary>The app's pause count when the step started (or last resumed): a different count means it was paused since.</summary>
    public long PauseEpoch { get; set; }

    /// <summary>Whether the step has started an action: from then on a pause holds it in place instead of starting it again.</summary>
    public bool Acted { get; set; }

    /// <summary>The resumed block of the step's latest hold, carried by its result.</summary>
    public PcResumed? Resumed { get; set; }

    public PcCall With(CancellationToken other) => new(Timings, Audit, other, Wait) { InBatch = InBatch, Base = Base, Output = Output, PauseEpoch = PauseEpoch };
}

/// <summary>What one step produced for later batch steps: the text a pc_read read, the windows an action opened.</summary>
public sealed class PcStepOutput
{
    public string? ReadText { get; set; }
    public IReadOnlyList<PcWindowInfo> Opened { get; set; } = [];
}

/// <summary>What the audit line says about one call, filled in as the call runs.</summary>
public sealed class PcAuditDraft(string tool)
{
    public string Tool { get; } = tool;
    public string? App { get; set; }
    public string? Window { get; set; }
    public string? Element { get; set; }
    public string? Role { get; set; }
    public string? Rung { get; set; }
    public string? Text { get; set; }
}

public sealed partial class PcControlEngine : IDisposable
{
    /// <summary>The looks kept for obs ids (points, zoom).</summary>
    public const int KeptObservations = 64;

    /// <summary>How long a cached tree is trusted with no change event (a guard for apps that send none).</summary>
    public const int WarmTreeMaxAgeMs = 2000;

    /// <summary>The user counts as working in a window for 2 s after their last input there (DESIGN.md 2.9).</summary>
    public const int UserActiveMs = 2000;

    /// <summary>How long a write waits while the user types in its target control before user_typing_here.</summary>
    public const int YieldMs = 10_000;

    private readonly PcEngineParts parts;
    private readonly PcCaller caller;
    private readonly PcSettle settle;
    private readonly PcPendingSteps pending;
    private readonly Lock gate = new();
    private readonly Dictionary<nint, PcObservation> latest = [];
    private readonly Dictionary<long, PcObservation> observations = [];
    private readonly Dictionary<nint, PcNumbering> numbering = [];
    private readonly Dictionary<nint, (PcTree Tree, long Version, long At)> warm = [];
    private readonly Dictionary<(string App, PcCapability Capability), PcAsking> approvals = [];
    private readonly Dictionary<nint, PcWindowInfo> known = [];
    private readonly HashSet<nint> opened = [];
    private readonly HashSet<string> stoppedApps = new(StringComparer.Ordinal);
    private long nextObservation;
    private bool stoppedForTurn;

    public PcControlEngine(PcEngineParts parts, PcCaller caller)
    {
        this.parts = parts;
        this.caller = caller;
        settle = new PcSettle(parts.Feed, parts.Clock);
        pending = new PcPendingSteps(parts.WaitClock ?? parts.Clock);
        parts.Control.Stopped += OnStopped;
    }

    /// <summary>The apps with Tactus work in flight (for the pill), across every chat.</summary>
    public IReadOnlyList<string> ActiveApps => parts.Lanes.Active;

    /// <summary>The steps of this chat waiting for the user past the 25 s budget.</summary>
    public int Waiting => pending.Count;

    /// <summary>The chat's Stop button: this chat's Tactus actions stop, running, queued and waiting.</summary>
    public void StopTurn()
    {
        parts.Control.Stop(PcScope.Thread(caller.Thread), PcStopSource.Chat);
        pending.CancelAll();
    }

    /// <summary>The turn ended: waiting steps are cancelled, held windows are freed, and a stop no longer applies.</summary>
    public void EndTurn()
    {
        pending.CancelAll();
        parts.Ownership.Release(caller.Thread);
        parts.Pause.EndTurn(caller.Thread);
        parts.Asks.EndTurn(caller.Thread);
        lock (gate)
        {
            stoppedForTurn = false;
            stoppedApps.Clear();
            // Frames are kept only while a turn may still zoom into them.
            foreach (var (number, observation) in observations.Where(o => o.Value.Shot is not null).ToList())
                observations[number] = observation with { Shot = null };
            foreach (var (window, observation) in latest.Where(o => o.Value.Shot is not null).ToList()) latest[window] = observation with { Shot = null };
        }
    }

    public void Dispose()
    {
        parts.Control.Stopped -= OnStopped;
        pending.CancelAll();
    }

    private void OnStopped(PcScope scope, PcStopSource source)
    {
        if (source == PcStopSource.Sleep) return;
        lock (gate)
        {
            if (scope.Kind == PcScopeKind.All || (scope.Kind == PcScopeKind.Thread && scope.Key == caller.Thread)) stoppedForTurn = true;
            else if (scope.Kind == PcScopeKind.App) stoppedApps.Add(scope.Key!);
        }
    }

    /// <summary>Runs one tool call and returns its result; never throws for a refused or failed call.</summary>
    public async Task<PcResult> Call(string tool, JsonObject? args, CancellationToken cancellationToken)
    {
        args ??= [];
        try
        {
            if (tool == PcToolSchemas.Wait && args["until"] is JsonValue until && until.TryGetValue<string>(out var mode) && mode == "step")
                return await Resume(args, cancellationToken).ConfigureAwait(false);
        }
        catch (PcArgsException error) { return PcResult.Fail(PcErrorCode.BadArgs, error.Message); }
        catch (OperationCanceledException) { return PcResult.Fail(PcErrorCode.StoppedByUser, "the call was cancelled"); }
        var wait = new PcWaitState();
        try
        {
            return await pending.Run(token => Execute(tool, args, wait, token), wait, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return PcResult.Fail(PcErrorCode.StoppedByUser, "the call was cancelled"); }
    }

    // pc_wait until "step": the same queued step, never a second one.
    private async Task<PcResult> Resume(JsonObject args, CancellationToken cancellationToken)
    {
        var token = PcArgs.Str(args, "token") ?? throw new PcArgsException("token is required for until step (the token of a waiting_for_user result)");
        return await pending.Resume(token, cancellationToken).ConfigureAwait(false)
            ?? PcResult.Fail(PcErrorCode.BadArgs, "no step waits under " + token + " (its result was already returned, it expired after 60 s, or it was cancelled)");
    }

    // One call, with its errors mapped to results and its audit line written when it ends (also after a waiting_for_user).
    private async Task<PcResult> Execute(string tool, JsonObject args, PcWaitState wait, CancellationToken token)
    {
        var call = new PcCall(new PcTimings(parts.Clock), new PcAuditDraft(tool), token, wait);
        var result = await Mapped(() => Dispatch(tool, args, call)).ConfigureAwait(false);
        WriteAudit(call, result);
        return result;
    }

    private static async Task<PcResult> Mapped(Func<Task<PcResult>> run)
    {
        try { return await run().ConfigureAwait(false); }
        catch (PcArgsException error) { return PcResult.Fail(PcErrorCode.BadArgs, error.Message); }
        catch (PcUiaTimeoutException error) { return PcResult.Fail(PcErrorCode.Timeout, error.Message); }
        catch (PcLaneFullException) { return PcResult.Fail(PcErrorCode.Busy, "this app already has Tactus actions waiting"); }
        catch (OperationCanceledException) { return PcResult.Fail(PcErrorCode.StoppedByUser, "the call was cancelled"); }
    }

    private void WriteAudit(PcCall call, PcResult result)
    {
        var audit = call.Audit;
        var outcome = result.Error is { } code ? PcErrorCodes.Wire(code) : result.Status switch
        {
            PcStatus.Unverified => "unverified",
            PcStatus.WaitingForUser => "waiting_for_user",
            _ => "ok",
        };
        parts.Audit.Write(new PcAuditEntry(parts.Now(), audit.Tool, audit.App, audit.Window, audit.Element, audit.Role, outcome, call.Timings.Total)
        {
            Rung = audit.Rung,
            TextLength = audit.Text?.Length,
            Text = audit.Text is not null && parts.Settings().LogTypedText ? audit.Text : null,
            Steps = call.Timings.Steps,
        });
    }

    private async Task<PcResult> Dispatch(string tool, JsonObject args, PcCall call)
    {
        if (PcToolSchemas.Find(tool) is not { } spec) return PcResult.Fail(PcErrorCode.BadArgs, "there is no tool " + tool);
        var settings = parts.Settings();
        if (tool == PcToolSchemas.Delegate)
        {
            return settings.FastHelper
                ? PcResult.Fail(PcErrorCode.NotSupported, "the fast helper is not part of this version; run the plan's steps yourself, or as one pc_batch")
                : PcResult.Fail(PcErrorCode.BadArgs, "pc_delegate is offered only when the user turns on the fast helper");
        }
        if (!settings.Enabled) return PcResult.Fail(PcErrorCode.PcOff, "Tactus is off");
        if (!caller.Attended())
            return PcResult.Fail(PcErrorCode.Unattended, caller.Head is null ? "this run is not one the user is watching" : "the user is away, so Hydra heads do not start PC steps");
        lock (gate)
        {
            if (stoppedForTurn) return PcResult.Fail(PcErrorCode.StoppedByUser, "the user stopped Tactus for this turn");
        }
        switch (tool)
        {
            case PcToolSchemas.ListApps:
                return ListApps(PcArgs.Bool(args, "installed", false));
            case PcToolSchemas.LaunchApp:
                return await Launch(args, call).ConfigureAwait(false);
            case PcToolSchemas.Zoom:
            {
                var obs = Observation(PcArgs.Str(args, "obs")) ?? throw new PcArgsException("obs is not one of this chat's looks; call pc_observe");
                return await InWindow(obs.Window.Handle, PcCapability.See, call, (info, inner) => Zoom(info, obs, args, inner)).ConfigureAwait(false);
            }
            case PcToolSchemas.Batch:
                return await Batch(args, call).ConfigureAwait(false);
            case PcToolSchemas.Ask:
                return await Ask(args, call).ConfigureAwait(false);
        }
        var handle = Window(PcArgs.Str(args, "window"));
        var capability = spec.ReadOnly ? PcCapability.See : PcCapability.Act;
        return await InWindow(handle, capability, call, (info, inner) => Step(tool, info, args, inner)).ConfigureAwait(false);
    }

    private static nint Window(string? text) =>
        PcWindowRef.TryParse(text, out var handle) ? handle : throw new PcArgsException("window must be a window id from pc_list_apps (w:...)");

    // The window gates, the approval, the window's hold, then the app's own stream.
    private async Task<PcResult> InWindow(nint handle, PcCapability capability, PcCall call, Func<PcWindowInfo, PcCall, Task<PcResult>> run)
    {
        if (parts.Security.SecureDesktop()) return PcResult.Fail(PcErrorCode.SecureDesktop, "a Windows security prompt is open");
        if (parts.Catalog.Window(handle) is not { } info) return PcResult.Fail(PcErrorCode.WindowGone, "the window is gone");
        if (Reused(info)) return PcResult.Fail(PcErrorCode.WindowGone, "the window closed and Windows gave its id to another window");
        call.Audit.App = info.ExeName;
        call.Audit.Window = info.Title;
        if (parts.Own.Owns(handle)) return PcResult.Fail(PcErrorCode.OwnWindow, "this is nth's own window");
        var verdict = PcPolicy.Classify(info.App, parts.Settings().UserDeniedApps);
        if (verdict.Denied) return PcResult.Fail(PcErrorCode.DeniedApp, verdict.Reason ?? "this app is not allowed");
        if (PcPolicy.IsRunDialog(info.ExeName, info.ClassName, info.Title)) return PcResult.Fail(PcErrorCode.DeniedApp, "the Run dialog runs commands; Tactus never drives it");
        if (PcPolicy.IsElevated(parts.Security.Process(info.ProcessId))) return PcResult.Fail(PcErrorCode.Elevated, "the app runs with admin rights");
        lock (gate)
        {
            if (stoppedApps.Contains(info.App.Key)) return PcResult.Fail(PcErrorCode.StoppedByUser, "the user stopped Tactus in this app for this turn");
        }
        if (parts.Ownership.Holder(handle, caller.Thread) is { } holder) return PcResult.Fail(PcErrorCode.BusyWindow, holder + " is using this window");
        if (!parts.StopKey.Arm())
            return PcResult.Fail(PcErrorCode.NotSupported, "the stop key " + parts.StopKey.Chord + " is taken by another app, so Tactus cannot start. Ask the user to pick another stop key in Settings, Tactus");
        Remember(info);
        // The call counts as the chat's in this app (the chat's Pause covers it), and while the app is paused it answers
        // waiting_for_user "paused" at once, also while its approval card waits (the card stays answerable).
        using var joined = parts.Pause.Join(caller.Thread, info.App.Key, call.Wait);
        call.Timings.Mark("guard");
        PcApproval approval;
        using (call.Wait.Waiting(PcWaitReason.Approval)) approval = await Approve(info.App, AppName(info), capability, call.Token).ConfigureAwait(false);
        call.Timings.Mark("approve");
        if (approval is not (PcApproval.Chat or PcApproval.Always))
            return PcResult.Fail(PcErrorCode.NotApproved, approval == PcApproval.TimedOut ? "the user did not answer" : "the user did not allow Tactus to " + (capability == PcCapability.See ? "see" : "use") + " this app");
        // A stop that came while the card was up still holds for the rest of the turn.
        if (StoppedHere(info.App.Key) is { } stopped) return stopped;
        if (capability == PcCapability.Act && parts.Ownership.Claim(handle, caller.Thread, caller.Label) is { } other)
            return PcResult.Fail(PcErrorCode.BusyWindow, other + " is using this window");
        parts.Feed.Watch(handle, info.ProcessId);
        // The watch is made before the call queues, so a stop also cancels calls still waiting for their turn.
        using var watch = parts.Control.Watch(call.Token, caller.Thread, info.App.Key, handle);
        try
        {
            return await parts.Lanes.Run(info.App.Key, laneToken => Stream(info, capability, approval, call.With(laneToken), run), watch.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (watch.Stopped)
        {
            return watch.StoppedBy == PcStopSource.Sleep
                ? PcResult.Fail(PcErrorCode.Locked, "the PC went to sleep")
                : PcResult.Fail(PcErrorCode.StoppedByUser, "the user stopped Tactus");
        }
    }

    // A handle Windows gave to a new window after the one Tactus knew closed (critique C17).
    private bool Reused(PcWindowInfo live)
    {
        lock (gate) return known.TryGetValue(live.Handle, out var seen) && !seen.SameWindow(live);
    }

    private void Remember(PcWindowInfo info)
    {
        lock (gate) known.TryAdd(info.Handle, info);
    }

    // One sheet per app and capability, shared by every call of this chat that needs it. The sheet belongs to no single
    // call: it stays up while any call waits on it and is withdrawn when the last one ends (a cancelled call, an expired
    // or stopped step), so a later call asks afresh instead of inheriting a cancelled sheet.
    private async Task<PcApproval> Approve(PcAppIdentity app, string name, PcCapability capability, CancellationToken cancellationToken)
    {
        var key = (app.Key, capability);
        PcAsking asking;
        lock (gate)
        {
            if (!approvals.TryGetValue(key, out asking!))
            {
                asking = new PcAsking();
                asking.Answer = Ask(app, name, capability, asking.Withdraw.Token);
                approvals[key] = asking;
            }
            asking.Waiters++;
        }
        try
        {
            var answer = await asking.Answer.WaitAsync(cancellationToken).ConfigureAwait(false);
            // An unanswered sheet is asked again next time.
            if (answer == PcApproval.TimedOut) Forget(key, asking);
            return answer;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var last = false;
            lock (gate) last = asking.Waiters == 1 && !asking.Answer.IsCompletedSuccessfully;
            if (last)
            {
                Forget(key, asking);
                _ = asking.Withdraw.CancelAsync();
            }
            throw;
        }
        finally
        {
            lock (gate) asking.Waiters--;
            if (asking.Answer.IsFaulted || asking.Answer.IsCanceled) Forget(key, asking);
        }
    }

    private void Forget((string App, PcCapability Capability) key, PcAsking asking)
    {
        lock (gate)
        {
            if (approvals.TryGetValue(key, out var current) && ReferenceEquals(current, asking)) approvals.Remove(key);
        }
    }

    private sealed class PcAsking
    {
        public CancellationTokenSource Withdraw { get; } = new();
        public Task<PcApproval> Answer { get; set; } = Task.FromResult(PcApproval.TimedOut);
        public int Waiters { get; set; }
    }

    // A Hydra head never uses a standing "Always" grant and is offered "This chat" only (critique C14).
    private async Task<PcApproval> Ask(PcAppIdentity app, string name, PcCapability capability, CancellationToken cancellationToken)
    {
        if (caller.Head is null && parts.Approver.Standing(app, capability) is { } standing && standing != PcApproval.TimedOut) return standing;
        var answer = await parts.Approver.Ask(new PcAskSource(caller.Thread, name, caller.Head), app, name, capability, PcPolicy.IsRisky(app), caller.Head is null, cancellationToken)
            .ConfigureAwait(false);
        return caller.Head is not null && answer == PcApproval.Always ? PcApproval.Chat : answer;
    }

    private static string AppName(PcWindowInfo info) => Path.GetFileNameWithoutExtension(info.ExeName);

    private PcAskSource Source(PcWindowInfo info) => new(caller.Thread, AppName(info), caller.Head);

    // ----- observations and the warm tree -----

    private PcObservation? Observation(string? id)
    {
        if (!PcObsId.TryParse(id, out var number)) return null;
        lock (gate) return observations.GetValueOrDefault(number);
    }

    private PcObservation? Latest(nint window)
    {
        lock (gate) return latest.GetValueOrDefault(window);
    }

    private PcObservation? LatestShot(nint window)
    {
        lock (gate) return observations.Values.Where(o => o.Window.Handle == window && o.Shot is not null).MaxBy(o => o.Number);
    }

    private PcObservation Record(PcWindowInfo info, PcTree tree, long version, PcShot? shot)
    {
        var windows = AppWindows(info);
        lock (gate)
        {
            var observation = new PcObservation(++nextObservation, info, tree, version) { Shot = shot, AppWindows = windows, At = parts.Clock.NowMs };
            if (shot is not null)
            {
                // One frame per window is kept (points and zoom use the latest screenshot only).
                foreach (var (number, old) in observations.Where(o => o.Value.Window.Handle == info.Handle && o.Value.Shot is not null).ToList())
                    observations[number] = old with { Shot = null };
            }
            latest[info.Handle] = observation;
            observations[observation.Number] = observation;
            foreach (var old in observations.Keys.Where(n => n <= observation.Number - KeptObservations).ToList()) observations.Remove(old);
            return observation;
        }
    }

    /// <summary>The window's tree with stable element numbers: from the warm cache while no change event came since it was read.</summary>
    private async Task<PcTree?> Tree(nint window, bool fresh, CancellationToken cancellationToken)
    {
        var version = parts.Feed.Version(window);
        lock (gate)
        {
            if (!fresh && warm.TryGetValue(window, out var cached) && cached.Version == version && parts.Clock.NowMs - cached.At <= WarmTreeMaxAgeMs)
                return cached.Tree;
        }
        var root = await parts.Reader.Tree(window, cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (gate)
        {
            if (root is null)
            {
                warm.Remove(window);
                return null;
            }
            if (!numbering.TryGetValue(window, out var numbers)) numbering[window] = numbers = new PcNumbering();
            var tree = numbers.Apply(TreeFormatter.Format(root));
            warm[window] = (tree, version, parts.Clock.NowMs);
            return tree;
        }
    }

    // The tree Tactus last read (before it is refreshed), for telling what changed on its own.
    private PcTree? Cached(nint window)
    {
        lock (gate) return warm.TryGetValue(window, out var cached) ? cached.Tree : null;
    }

    private long CachedVersion(nint window)
    {
        lock (gate) return warm.TryGetValue(window, out var cached) ? cached.Version : parts.Feed.Version(window);
    }

    private PcWindowLine Line(PcWindowInfo info, PcObservation? observation) =>
        observation?.Shot is { } shot
            ? new PcWindowLine(info.Title, info.ExeName, observation.Id, shot.Grid.Width, shot.Grid.Height, shot.Grid.Scale)
            : new PcWindowLine(info.Title, info.ExeName, observation?.Id, 0, 0, 0);

    private PcGridProfile Grid() => caller.Grid ?? parts.Settings().Grid;

    private HashSet<nint> AppWindows(PcWindowInfo info) =>
        parts.Catalog.Windows().Where(w => w.ProcessId == info.ProcessId || w.App.Key == info.App.Key).Select(w => w.Handle).ToHashSet();

    private List<PcWindowInfo> NewWindows(PcWindowInfo info, IReadOnlySet<nint> before) =>
        parts.Catalog.Windows().Where(w => !before.Contains(w.Handle) && (w.ProcessId == info.ProcessId || w.App.Key == info.App.Key)).ToList();

    internal static string Count(long value) => value.ToString(CultureInfo.InvariantCulture);
}
