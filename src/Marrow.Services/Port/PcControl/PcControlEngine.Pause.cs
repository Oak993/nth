// PORT-ONLY: Tactus, PC control (OD-0145, ED-NEW-pcctl-12, ED-NEW-pcctl-14; DESIGN.md 10.1). Pause in the engine. A step
// of a paused app holds in its app's stream before it starts, so the app's later steps stay queued behind it in order and
// nothing is dropped. Pause takes effect at the next boundary: before a step, between batch steps, between 64-character
// text chunks, between the two actions of one step (a pc_type and its submit), and before an action whose confirmation or
// typing wait the pause interrupted. A step that has not acted yet starts again from the top after the hold (it reads its
// target again and asks a guarded action's question again); one that has acted holds where it is. While paused nothing is
// read in the app except the post-check of the action in flight. At the Resume the window is read again before the held
// step goes on (its identity, the grants, the safety gates, a cold tree), its points from before the pause are stale, a
// target that changed while paused stops the step, and its result carries the resumed block.
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl;

public sealed partial class PcControlEngine
{
    // The app's stream, with Pause around the step.
    private async Task<PcResult> Stream(PcWindowInfo info, PcCapability capability, PcApproval approval, PcCall call, Func<PcWindowInfo, PcCall, Task<PcResult>> run)
    {
        call.Timings.Mark("queue");
        var app = info.App.Key;
        while (true)
        {
            var epoch = parts.Pause.Epoch(app);
            if (parts.Pause.IsPaused(app))
            {
                if (await Hold(info, capability, approval, call).ConfigureAwait(false) is { } refused) return refused with { Resumed = call.Resumed };
                continue;
            }
            // The facts may have changed while the call waited for its turn.
            if (parts.Catalog.Window(info.Handle) is not { } live || !info.SameWindow(live))
                return PcResult.Fail(PcErrorCode.WindowGone, "the window is gone") with { Resumed = call.Resumed };
            call.PauseEpoch = epoch;
            try
            {
                var result = await run(live, call).ConfigureAwait(false);
                return call.Resumed is null ? result : result with { Resumed = call.Resumed };
            }
            catch (PcHeld) when (!call.Acted && !call.Token.IsCancellationRequested)
            {
                // Paused before the step acted: it holds at the top of the loop and starts again.
            }
        }
    }

    // Holds until the app is resumed (a stop cancels the wait), then reads the window again (DESIGN.md 10.1, Resume items 1
    // to 5). A refusal ends the step; null lets it run.
    private async Task<PcResult?> Hold(PcWindowInfo info, PcCapability capability, PcApproval approval, PcCall call)
    {
        var app = info.App.Key;
        await parts.Pause.Held(app, call.Token).ConfigureAwait(false);
        call.Token.ThrowIfCancellationRequested();
        var resumedAt = parts.Pause.LastPause(app)?.EndMs ?? parts.Clock.NowMs;
        var (refused, changed) = await Reread(info, capability, approval, call).ConfigureAwait(false);
        call.Resumed = new PcResumed(Math.Max(0, parts.Clock.NowMs - resumedAt), true, changed);
        call.Timings.Mark("resume");
        return refused;
    }

    // The re-read before a held step: a stop that came meanwhile, the window's identity, the grants (an app removed in
    // Settings during the pause), the safety gates, then a cold read of the tree. Returns the refusal, if any, and how many
    // elements changed since the look before the pause.
    private async Task<(PcResult? Refused, int Changed)> Reread(PcWindowInfo info, PcCapability capability, PcApproval approval, PcCall call)
    {
        if (StoppedHere(info.App.Key) is { } stopped) return (stopped, 0);
        if (parts.Catalog.Window(info.Handle) is not { } live || !info.SameWindow(live))
            return (PcResult.Fail(PcErrorCode.WindowGone, "the window closed while Tactus was paused"), 0);
        if (approval == PcApproval.Always && parts.Approver.Standing(info.App, capability) != PcApproval.Always)
            return (PcResult.Fail(PcErrorCode.NotApproved, "the app was removed from Tactus's apps while Tactus was paused"), 0);
        var settings = parts.Settings();
        if (!settings.Enabled) return (PcResult.Fail(PcErrorCode.PcOff, "Tactus is off"), 0);
        if (parts.Security.SecureDesktop()) return (PcResult.Fail(PcErrorCode.SecureDesktop, "a Windows security prompt is open"), 0);
        var verdict = PcPolicy.Classify(info.App, settings.UserDeniedApps);
        if (verdict.Denied) return (PcResult.Fail(PcErrorCode.DeniedApp, verdict.Reason ?? "this app is not allowed"), 0);
        if (PcPolicy.IsElevated(parts.Security.Process(info.ProcessId))) return (PcResult.Fail(PcErrorCode.Elevated, "the app runs with admin rights"), 0);
        return (null, await Cold(info, call).ConfigureAwait(false));
    }

    // A cold read of the window (its change events were dropped during the pause); the elements that changed since the
    // look before the pause.
    private async Task<int> Cold(PcWindowInfo info, PcCall call)
    {
        var look = Latest(info.Handle);
        lock (gate) warm.Remove(info.Handle);
        var tree = await Tree(info.Handle, fresh: true, call.Token).ConfigureAwait(false);
        return look is null || tree is null ? 0 : PcVerify.Changed(look.Tree, tree).Count;
    }

    // A boundary inside a step: when the app paused since the step started, a step that has not acted yet starts again
    // after the hold (PcHeld); one that has acted holds here and goes on.
    private async Task Boundary(PcWindowInfo info, PcCall call)
    {
        if (!parts.Pause.PausedSince(info.App.Key, call.PauseEpoch)) return;
        if (!call.Acted) throw new PcHeld();
        await HoldInPlace(info, call).ConfigureAwait(false);
    }

    // Holds where the step is (it has acted), then reads the window again and goes on from there.
    private async Task HoldInPlace(PcWindowInfo info, PcCall call)
    {
        var app = info.App.Key;
        await parts.Pause.Held(app, call.Token).ConfigureAwait(false);
        call.Token.ThrowIfCancellationRequested();
        call.PauseEpoch = parts.Pause.Epoch(app);
        var resumedAt = parts.Pause.LastPause(app)?.EndMs ?? parts.Clock.NowMs;
        var changed = await Cold(info, call).ConfigureAwait(false);
        call.Resumed = new PcResumed(Math.Max(0, parts.Clock.NowMs - resumedAt), true, changed);
        call.Timings.Mark("resume");
    }

    // Between two 64-character runs of a pc_type (DESIGN.md 10.1): when the app paused, the field is read once (the
    // post-check of the run in flight), the typing holds, and at the Resume the typed part is read back first. The rest is
    // typed only when the field reads as it did; otherwise the step ends, saying how much was typed.
    private async Task<string?> TextBoundary(PcWindowInfo info, PcUiNode? field, int typed, int total, PcCall call, CancellationToken cancellationToken)
    {
        if (!parts.Pause.PausedSince(info.App.Key, call.PauseEpoch)) return null;
        var before = await FieldText(info, field, cancellationToken).ConfigureAwait(false);
        await HoldInPlace(info, call).ConfigureAwait(false);
        var after = await FieldText(info, field, cancellationToken).ConfigureAwait(false);
        if (before is not null && after is not null && string.Equals(before, after, StringComparison.Ordinal)) return null;
        return "changed while paused: typed " + Count(typed) + " of " + Count(total) + " characters; look again before typing the rest";
    }

    private async Task<string?> FieldText(PcWindowInfo info, PcUiNode? field, CancellationToken cancellationToken)
    {
        var node = field is not null
            ? await parts.Reader.Element(info.Handle, field.RuntimeId, cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false)
            : await parts.Reader.Focused(info.Handle, cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
        return node is null ? null : PcVerify.Text(node.Value);
    }

    // A stop for this turn (all, this chat, or this app), which may have come while the call waited.
    private PcResult? StoppedHere(string app)
    {
        lock (gate)
        {
            if (stoppedForTurn) return PcResult.Fail(PcErrorCode.StoppedByUser, "the user stopped Tactus for this turn");
            if (stoppedApps.Contains(app)) return PcResult.Fail(PcErrorCode.StoppedByUser, "the user stopped Tactus in this app for this turn");
        }
        return null;
    }

    // The app's latest pause that began after a look taken at <paramref name="at"/> (engine clock): that look's points are stale.
    private PcPauseSpan? PausedAfter(string app, long at) => parts.Pause.LastPause(app) is { } span && span.StartMs >= at ? span : null;

    // A step found its app paused before it acted: it holds in its stream and starts again.
    private sealed class PcHeld() : Exception("the app was paused before the step acted");
}
