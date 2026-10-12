// PORT-ONLY: Tactus, PC control (OD-0140, OD-0141, OD-0144, ED-NEW-pcctl-6, ED-NEW-pcctl-7, ED-NEW-pcctl-8,
// ED-NEW-pcctl-19; DESIGN.md 2.6, 2.8, 2.9, 2.11, 2.12, 4.3; critique C5, C6, C7, C13, C16, C21, C25 to C28). Acting. The
// target is found again by identity just before acting (a recycled container or a look-alike is never acted on; two
// matches are refused), and a target the user changed since the model's look stops the step. The action goes down the
// ladder without activating any window: before each way the guarded-word check runs on what that way will press (name,
// AutomationId, default action, class, menu text; an unnamed button in a mail or chat app), a write into the user's
// control waits, an app-global way waits for a still moment while the user works in the app, and around each way the
// user's foreground, pointer and focus are compared (a way that moved them is not used again). Whose input it was comes
// from the physical input monitor. The window is then waited on by its change events until the action's check holds, and
// the check decides ok, unverified or expect_failed; a failed check tries the next way once.
using System.Text;
using System.Text.Json.Nodes;
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl;

public sealed partial class PcControlEngine
{
    /// <summary>A resolved target: the tree its number is in, its number, the live element, the screen point (point targets).</summary>
    private sealed record PcTarget(PcTree Tree, int Number, PcUiNode? Node, PcPoint? Point);

    /// <summary>
    /// The element a call names (an element number, a target locator, or a point with its obs), found again in the live
    /// app just before acting. A changed, vanished or doubly matched element is refused; Tactus never guesses.
    /// </summary>
    private async Task<(PcTarget? Target, PcResult? Failure)> Resolve(PcWindowInfo info, JsonObject args, bool pointAllowed, bool required, PcCall call)
    {
        await Boundary(info, call).ConfigureAwait(false);
        if (call.NeedsFreshTarget)
        {
            await Tree(info.Handle, fresh: true, call.Token).ConfigureAwait(false);
            call.NeedsFreshTarget = false;
        }
        var element = PcArgs.Int(args, "element");
        var locator = PcArgs.Locator(args);
        var point = PcArgs.Pair(args["point"], "point");
        var given = (element is null ? 0 : 1) + (locator is null ? 0 : 1) + (point is null ? 0 : 1);
        if (given > 1) throw new PcArgsException("give only one of element, target and point");
        if (point is not null && !pointAllowed) throw new PcArgsException("this tool takes element or target, not a point");
        if (given == 0)
        {
            if (required) throw new PcArgsException("give element (a number from the tree) or target");
            return (null, null);
        }
        PcElementIdentity identity;
        PcObservation? looked = null;
        PcPoint? screen = null;
        if (element is { } number)
        {
            // The look the model saw: the batch's starting look in a batch, else the latest (whose diff it was given).
            looked = call.Base ?? Latest(info.Handle);
            if (looked is null) return (null, PcResult.Fail(PcErrorCode.StaleObs, "Tactus has not looked at this window yet; call pc_observe"));
            if (!looked.Tree.Elements.ContainsKey(number)) return (null, PcResult.Fail(PcErrorCode.StaleElement, "element " + Count(number) + " was not in your last look at this window"));
            identity = PcElementMatch.Identity(looked.Tree, number);
        }
        else if (locator is not null)
        {
            var tree = await Tree(info.Handle, fresh: false, call.Token).ConfigureAwait(false);
            if (tree is null) return (null, PcResult.Fail(PcErrorCode.WindowGone, "the window is gone"));
            var match = PcElementMatch.Find(tree, locator);
            if (!match.Found) return (null, Unmatched(match, tree));
            identity = PcElementMatch.Identity(tree, match.Index);
        }
        else
        {
            var observation = Observation(PcArgs.Str(args, "obs"));
            if (observation is null || observation.Window.Handle != info.Handle || observation.Shot is null || LatestShot(info.Handle)?.Number != observation.Number)
                return (null, PcResult.Fail(PcErrorCode.StaleObs, "a point needs the obs of this window's latest screenshot"));
            // Every point from a look before a pause is stale (DESIGN.md 10.1, Resume item 5).
            if (PausedAfter(info.App.Key, observation.At) is { } pause)
                return (null, PcResult.Fail(PcErrorCode.StaleObs, PcPauseText.StaleAfterPause(TimeSpan.FromMilliseconds(pause.LengthMs))));
            var (x, y) = point!.Value;
            var grid = observation.Shot.Grid;
            if (!CoordinateMapper.OnGrid(grid, x, y)) return (null, PcResult.Fail(PcErrorCode.OffScreen, "the point is outside the screenshot"));
            if (info.Minimized) return (null, PcResult.Fail(PcErrorCode.OffScreen, "the window is minimized; use an element number"));
            if (CoordinateMapper.Compare(observation.Window.Frame, info.Frame) == PcFrameChange.Resized)
                return (null, PcResult.Fail(PcErrorCode.StaleObs, "the window changed size since " + observation.Id));
            // A moved window is followed: the point keeps its place inside the window.
            var at = CoordinateMapper.ToScreen(info.Frame, grid.Scale, x, y);
            screen = at;
            if (parts.Own.Rects().Any(r => r.Contains(at))) return (null, PcResult.Fail(PcErrorCode.OwnWindow, "the point is on nth's own window"));
            if (!parts.Catalog.Monitors().Any(m => m.Bounds.Contains(at))) return (null, PcResult.Fail(PcErrorCode.OffScreen, "the point is on no screen"));
            var then = CoordinateMapper.ToScreen(observation.Window.Frame, grid.Scale, x, y);
            looked = observation;
            var under = PcElementMatch.AtPoint(observation.Tree, then);
            if (under == 0)
            {
                if (StaleView(info, looked) is { } staleness) return (null, staleness);
                var now = await Tree(info.Handle, fresh: false, call.Token).ConfigureAwait(false);
                return now is null ? (null, PcResult.Fail(PcErrorCode.WindowGone, "the window is gone")) : (new PcTarget(now, 0, null, at), null);
            }
            identity = PcElementMatch.Identity(observation.Tree, under);
        }
        if (looked is not null && StaleView(info, looked) is { } stale) return (null, stale);
        call.Timings.Mark("resolve");
        // Just before acting: the element itself, live (one cheap call); a full read only when it is not the same any more.
        var live = await parts.Reader.Element(info.Handle, identity.RuntimeId, call.Token).WaitAsync(call.Token).ConfigureAwait(false);
        var current = await Tree(info.Handle, fresh: false, call.Token).ConfigureAwait(false);
        if (current is null) return (null, PcResult.Fail(PcErrorCode.WindowGone, "the window is gone"));
        PcUiNode node;
        int resolved;
        if (live is not null && PcElementMatch.Matches(live, identity) && current.IndexOf(identity.RuntimeId) is { } index)
        {
            node = live;
            resolved = index;
        }
        else
        {
            var fresh = await Tree(info.Handle, fresh: true, call.Token).ConfigureAwait(false);
            if (fresh is null) return (null, PcResult.Fail(PcErrorCode.WindowGone, "the window is gone"));
            var match = PcElementMatch.Resolve(fresh, identity);
            if (!match.Found) return (null, Unmatched(match, fresh));
            node = fresh.Elements[match.Index];
            resolved = match.Index;
            current = fresh;
        }
        if (screen is { } p && !node.Bounds.IsEmpty && !node.Bounds.Contains(p))
            return (null, PcResult.Fail(PcErrorCode.StaleObs, identity.Describe() + " is no longer under the point"));
        if (looked is not null && identity.Seen is { } seen && UserChanged(info, looked, seen, node) is { } edited)
            return (null, PcResult.Fail(PcErrorCode.ExpectFailed, edited));
        call.Audit.Element = node.Name;
        call.Audit.Role = node.Role;
        call.Timings.Mark("verify-target");
        return (new PcTarget(current, resolved, node, screen), null);
    }

    // Never acts on old facts (DESIGN.md 2.9): when the user pressed keys or buttons in this window after the model's look
    // (the physical input monitor counts them; Tactus's own input never counts), a target whose value or state differs
    // from that look was changed by the user, and the step stops instead of acting on what the model saw. Tactus's own
    // earlier writes do not count unless the user also pressed there, which errs on the side of stopping.
    // A look taken before a pause counts as old facts whoever changed the target (DESIGN.md 10.1, Resume item 6): the step
    // returns "changed while paused" instead of acting.
    private string? UserChanged(PcWindowInfo info, PcObservation looked, PcUiNode seen, PcUiNode now)
    {
        var paused = PausedAfter(info.App.Key, looked.At) is not null;
        if (!paused && parts.Input.Presses(info.Handle) == looked.Presses) return null;
        var what = PcVerify.Describe(now);
        return Difference(seen, now, paused ? "changed while paused: " + what : "the user changed " + what + " since your look");
    }

    // How the target differs from what was seen, led by who changed it; null when its value and watched states are the same.
    private static string? Difference(PcUiNode seen, PcUiNode now, string lead)
    {
        if (PcElementMatch.Editable(seen) && !seen.Has(PcStates.Password)
            && !string.Equals(PcVerify.Text(seen.Value), PcVerify.Text(now.Value), StringComparison.Ordinal))
            return lead + ": it now shows " + PcWindowLine.Quote(TreeFormatter.Clean(now.Value ?? "", 80)) + "; look again before acting";
        const PcStates watched = PcStates.Checked | PcStates.Selected | PcStates.Expanded | PcStates.Collapsed;
        if ((seen.States & watched) != (now.States & watched))
            return lead + ": it is now " + StateWords(seen.States, now.States) + "; look again before acting";
        return null;
    }

    // The states that changed, as they are now: "checked", "not selected", "expanded".
    private static string StateWords(PcStates seen, PcStates now)
    {
        var words = new List<string>();
        if ((seen ^ now).HasFlag(PcStates.Checked)) words.Add(now.HasFlag(PcStates.Checked) ? "checked" : "unchecked");
        if ((seen ^ now).HasFlag(PcStates.Selected)) words.Add(now.HasFlag(PcStates.Selected) ? "selected" : "not selected");
        if ((seen ^ now).HasFlag(PcStates.Expanded) || (seen ^ now).HasFlag(PcStates.Collapsed))
            words.Add(now.HasFlag(PcStates.Expanded) ? "expanded" : "collapsed");
        return string.Join(", ", words);
    }

    // Never a stale view (DESIGN.md 2.9): a window of the app that opened since the model looked (and that no Tactus step
    // opened) may cover or change what the step depends on.
    private PcResult? StaleView(PcWindowInfo info, PcObservation looked)
    {
        List<PcWindowInfo> appeared;
        lock (gate) appeared = NewWindows(info, looked.AppWindows).Where(w => w.Handle != info.Handle && !opened.Contains(w.Handle)).ToList();
        if (appeared.Count == 0) return null;
        var first = appeared[0];
        return PcResult.Fail(PcErrorCode.ExpectFailed, "a window of the app opened since your look at this window: " + PcWindowRef.Format(first.Handle) + " "
            + PcWindowLine.Quote(TreeFormatter.Clean(first.Title, 80)) + "; look at it before acting here");
    }

    private static PcResult Unmatched(PcMatch match, PcTree tree)
    {
        if (match.Kind == PcMatchKind.Ambiguous)
        {
            var list = string.Join("; ", match.Candidates.Take(8).Select(i => "[" + Count(i) + "] " + TreeFormatter.Body(tree.Elements[i]) + " at " + Bounds(tree.Elements[i].Bounds)));
            return PcResult.Fail(PcErrorCode.AmbiguousTarget, (match.Detail ?? "more than one element matches") + ": " + list);
        }
        return PcResult.Fail(PcErrorCode.StaleElement, match.Detail ?? "the element is gone");
    }

    private static string Bounds(PcRect r) => Count(r.X) + "," + Count(r.Y) + " " + Count(r.Width) + "x" + Count(r.Height);

    /// <summary>
    /// Runs one input down the ladder and checks it. <paramref name="typed"/> is the text of a pc_type (read back after).
    /// </summary>
    private async Task<PcResult> Act(PcWindowInfo info, PcInputRequest request, PcExpect? expect, PcCall call, string? typed = null)
    {
        while (true)
        {
            try
            {
                await Boundary(info, call).ConfigureAwait(false);
                return await ActOnce(info, request, expect, call, typed).ConfigureAwait(false);
            }
            catch (PcHeld) when (call.Acted && !call.Token.IsCancellationRequested)
            {
                // Paused between two actions of one step (a pc_type and its submit) before this one acted: it holds here,
                // then starts again from its own top (a fresh read, and its confirmation asked again).
                await HoldInPlace(info, call).ConfigureAwait(false);
            }
            catch (PcGrantRemoved exception)
            {
                return PcResult.Fail(PcErrorCode.NotApproved, exception.Message);
            }
        }
    }

    private async Task<PcResult> ActOnce(PcWindowInfo info, PcInputRequest request, PcExpect? expect, PcCall call, string? typed)
    {
        var handle = info.Handle;
        var textSent = 0;
        var typedField = request.Element;
        if (request.Kind == PcInputKind.Type)
        {
            request = request with { Boundary = (done, total, token) =>
            {
                textSent = done;
                return TextBoundary(info, typedField, done, total, call, token);
            } };
        }
        // What changed on its own since Tactus last looked (a clock, a caret, an animation) is noise for the check.
        var cached = Cached(handle);
        var before = await Tree(handle, fresh: false, call.Token).ConfigureAwait(false);
        if (before is null) return PcResult.Fail(PcErrorCode.WindowGone, "the window is gone");
        var user = parts.Foreground.Read();
        var inFront = user.Foreground == handle;
        // The user is active in this window: their own input went into it within 2 s (DESIGN.md 2.9).
        var active = AppStateGuard.ActiveIn(parts.Input, [handle], parts.Clock.NowMs);
        var noise = cached is not null && !ReferenceEquals(cached, before) ? PcVerify.Changed(cached, before) : new HashSet<string>(StringComparer.Ordinal);
        if (active && before.Focused is { } focusedIndex) noise.Add(before.Elements[focusedIndex].RuntimeId);
        request = request with { Tree = before, InFront = inFront, UserActive = active, ModifiersDown = user.ModifiersDown, Waiting = call.Wait.Waiting };
        var plan = parts.Ladder.Plan(request);
        call.Timings.Mark("plan");
        if (plan.Refused)
        {
            var code = plan.Refusal ?? PcErrorCode.NeedsRealInput;
            if (code == PcErrorCode.NeedsRealInput && parts.Security.Locked())
                return PcResult.Fail(PcErrorCode.Locked, "the PC is locked, and this step needs the real mouse or keyboard");
            return PcResult.Fail(code, plan.Reason ?? PcRealInputReason.Off, Line(info, Latest(handle)));
        }
        // A write into the control the user is typing in waits (DESIGN.md 2.9); keyless keys act on the app's own focus.
        var writes = request.Kind == PcInputKind.Type || (request.Kind == PcInputKind.Key && request.Element is null) || plan.Plans.Any(p => p.Writes);
        if (writes && await Yield(info, request.Element, call).ConfigureAwait(false) is { } yielded) return yielded;
        var confirmed = new HashSet<string>(StringComparer.Ordinal);
        PcResult? last = null;
        var tried = 0;
        foreach (var offered in plan.Plans)
        {
            var way = offered;
            var intended = way.Target ?? request.Element;
            var focusedKey = intended is null && request.Kind == PcInputKind.Key
                && request.Chord is { Modifiers.Count: 0 } chord && chord.Key is KeyChordParser.Return or 0x20
                && before.Focused is not null;
            if (focusedKey) intended = before.Elements[before.Focused!.Value];
            var identity = intended is not null && request.Tree!.IndexOf(intended.RuntimeId) is { } intendedIndex
                ? PcElementMatch.Identity(request.Tree, intendedIndex) : null;
            if (tried == 2) break;
            // Paused after a way acted and failed its check: the step ends with that result instead of trying another way.
            if (tried > 0 && parts.Pause.PausedSince(info.App.Key, call.PauseEpoch)) return last!;
            if (Clipboard(request, way) is { } clipboard) return clipboard;
            if (await Guard(info, request, way, before, confirmed, call).ConfigureAwait(false) is { } refused) return refused;
            var pressesBefore = parts.Input.Presses(handle);
            var (deferred, waited) = await Share(info, request, way, call).ConfigureAwait(false);
            if (deferred is not null) return deferred;
            // A pause while the confirmation (or the typing wait) was up: an answer given then is asked again after the
            // Resume, with the target read again (DESIGN.md 10.1).
            if (tried == 0 && parts.Pause.PausedSince(info.App.Key, call.PauseEpoch)) throw new PcHeld();
            call.NeedsFreshTarget |= waited;
            if (waited)
            {
                // The user worked while the step waited: the window is read again (their input made it dirty), and a target
                // they changed stops the step.
                if (active && before.Focused is { } userFocus) noise.Add(before.Elements[userFocus].RuntimeId);
                if (request.Element is { } element && parts.Input.Presses(handle) != pressesBefore
                    && await ChangedWhileWaiting(info, element, call).ConfigureAwait(false) is { } changed)
                    return changed;
            }
            if (identity is null && call.NeedsFreshTarget)
            {
                before = await Tree(handle, fresh: true, call.Token).ConfigureAwait(false) ?? before;
                request = request with { Tree = before };
                call.NeedsFreshTarget = false;
            }
            // ED-NEW-pcctl-cx-11: a moved window preserves a point's place inside it; a resize invalidates its grid.
            if (request.Point is { } oldPoint)
            {
                if (parts.Catalog.Window(handle) is not { } liveWindow || !info.SameWindow(liveWindow))
                    return PcResult.Fail(PcErrorCode.WindowGone, "the window was replaced while the step waited");
                if (CoordinateMapper.Compare(request.Window.Frame, liveWindow.Frame) == PcFrameChange.Resized)
                    return PcResult.Fail(PcErrorCode.StaleObs, "the window changed size while the step waited");
                var dx = liveWindow.Frame.X - request.Window.Frame.X;
                var dy = liveWindow.Frame.Y - request.Window.Frame.Y;
                request = request with { Window = liveWindow, Point = new PcPoint(oldPoint.X + dx, oldPoint.Y + dy),
                    Path = request.Path.Select(p => new PcPoint(p.X + dx, p.Y + dy)).ToList() };
                info = liveWindow;
                if (PointRefusal(request.Point.Value) is { } pointFailure) return pointFailure;
                if (identity is null)
                {
                    var remapped = parts.Ladder.Plan(request);
                    var mappedWay = remapped.Plans.FirstOrDefault(p => p.Rung == way.Rung && p.Method == way.Method);
                    if (mappedWay is null) return PcResult.Fail(remapped.Refusal ?? PcErrorCode.StaleObs, remapped.Reason ?? "the point action is no longer available");
                    way = mappedWay;
                }
            }
            // Confirmation/yield may have outlived the user's 2 s activity window. Revalidate content, not recency.
            if (identity is not null && call.NeedsFreshTarget)
            {
                var fresh = await Tree(handle, fresh: true, call.Token).ConfigureAwait(false);
                if (fresh is null) return PcResult.Fail(PcErrorCode.WindowGone, "the window is gone");
                var match = PcElementMatch.Resolve(fresh, identity);
                if (!match.Found) return PcResult.Fail(match.Kind == PcMatchKind.Ambiguous ? PcErrorCode.AmbiguousTarget : PcErrorCode.StaleElement,
                    match.Detail ?? "the intended element changed while Tactus waited");
                var live = fresh.Elements[match.Index];
                if (request.Point is { } point && !live.Bounds.IsEmpty && !live.Bounds.Contains(point))
                    return PcResult.Fail(PcErrorCode.StaleObs, "the intended element is no longer under the point; look again before acting");
                if (Difference(intended!, live, "the target changed while Tactus waited") is { } difference)
                    return PcResult.Fail(PcErrorCode.ExpectFailed, difference);
                var refreshedRequest = request with { Tree = fresh, Element = request.Element is null && !focusedKey ? null : live };
                var refreshed = parts.Ladder.Plan(refreshedRequest);
                if (refreshed.Refused) return PcResult.Fail(refreshed.Refusal ?? PcErrorCode.StaleElement, refreshed.Reason ?? "the target changed");
                var liveWay = refreshed.Plans.FirstOrDefault(p => p.Rung == way.Rung && p.Method == way.Method
                    && (p.Target ?? refreshedRequest.Element)?.RuntimeId == live.RuntimeId);
                if (liveWay is null) return PcResult.Fail(PcErrorCode.StaleElement, "the intended action is no longer available on the target");
                way = liveWay with { Target = live, Writes = way.Writes };
                before = fresh;
                request = refreshedRequest;
                call.NeedsFreshTarget = false;
            }
            if (call.GrantLive?.Invoke() == false)
                return PcResult.Fail(PcErrorCode.NotApproved, "the app was removed from Tactus's apps while the step waited");
            tried++;
            call.Acted = true;
            // An action in flight: the pill reads "Pausing" until it and its check end.
            using var unit = parts.Pause.Acting(info.App.Key);
            var structureSeen = parts.Feed.StructureVersion(handle);
            var windowsBefore = AppWindows(info);
            var pre = parts.Foreground.Read();
            var started = parts.Clock.NowMs;
            var pressesAt = parts.Input.Presses(handle);
            AimPointer(info, request, way);
            Func<long, CancellationToken, Task<PcUiaOutcome>>? validateBorrow = null;
            Func<PcBorrowRequest, PcBorrowRequest>? refreshBorrow = null;
            if (way.Rung == PcRung.R4 && (way.Target ?? request.Element) is { } borrowTarget
                && before.IndexOf(borrowTarget.RuntimeId) is { } borrowIndex)
            {
                var borrowIdentity = PcElementMatch.Identity(before, borrowIndex);
                PcUiNode? resolvedBorrow = null;
                PcPoint? resolvedPoint = null;
                validateBorrow = async (waitedMs, token) =>
                {
                    if (parts.Catalog.Window(handle) is not { } window || !info.SameWindow(window))
                        return PcUiaOutcome.Fail(PcErrorCode.WindowGone, "the window was replaced while the borrow waited");
                    // ED-NEW-pcctl-cx-10 (C3c V1/V9): our previous piece changes value and may grow the field.
                    // TextBoundary still guards intervening pauses; later pieces require the same window and focus.
                    if (textSent > 0)
                    {
                        var focused = await parts.Reader.Focused(handle, token).WaitAsync(token).ConfigureAwait(false);
                        return focused?.RuntimeId == (resolvedBorrow?.RuntimeId ?? borrowTarget.RuntimeId) ? PcUiaOutcome.Ok
                            : PcUiaOutcome.Fail(PcErrorCode.StaleElement, "the typed field no longer holds focus; look again before typing the rest");
                    }
                    // Long queue waits need one cold read. Short waits use the cheap element check; a changed
                    // identity then resolves on a cold tree even when a provider missed its change event.
                    var checkedLive = waitedMs >= 0 && waitedMs <= WarmTreeMaxAgeMs
                        ? await parts.Reader.Element(handle, borrowIdentity.RuntimeId, token).WaitAsync(token).ConfigureAwait(false) : null;
                    var cold = waitedMs > WarmTreeMaxAgeMs || (waitedMs >= 0 && (checkedLive is null || !PcElementMatch.Matches(checkedLive, borrowIdentity)));
                    var fresh = await Tree(handle, fresh: cold, token).ConfigureAwait(false);
                    if (fresh is null) return PcUiaOutcome.Fail(PcErrorCode.WindowGone, "the window is gone");
                    var match = PcElementMatch.Resolve(fresh, borrowIdentity);
                    if (!match.Found) return PcUiaOutcome.Fail(match.Kind == PcMatchKind.Ambiguous ? PcErrorCode.AmbiguousTarget : PcErrorCode.StaleElement,
                        match.Detail ?? "the intended element changed while the borrow waited");
                    var live = waitedMs < 0 ? borrowTarget
                        : checkedLive is not null && PcElementMatch.Matches(checkedLive, borrowIdentity) ? checkedLive : fresh.Elements[match.Index];
                    if (request.Point is { } oldPoint)
                    {
                        if (CoordinateMapper.Compare(request.Window.Frame, window.Frame) == PcFrameChange.Resized)
                            return PcUiaOutcome.Fail(PcErrorCode.StaleObs, "the window changed size while the borrow waited");
                        resolvedPoint = new PcPoint(oldPoint.X + window.Frame.X - request.Window.Frame.X,
                            oldPoint.Y + window.Frame.Y - request.Window.Frame.Y);
                        if (PointRefusal(resolvedPoint.Value) is { } pointFailure)
                            return PcUiaOutcome.Fail(pointFailure.Error!.Value, pointFailure.Reason!);
                        if (!live.Bounds.IsEmpty && !live.Bounds.Contains(resolvedPoint.Value))
                            return PcUiaOutcome.Fail(PcErrorCode.StaleObs, "the intended element is no longer under the point; look again before acting");
                    }
                    else if (!live.Bounds.IsEmpty)
                        resolvedPoint = new PcPoint(live.Bounds.X + live.Bounds.Width / 2, live.Bounds.Y + live.Bounds.Height / 2);
                    if (Difference(borrowTarget, live, "the target changed while the borrow waited") is { } difference)
                        return PcUiaOutcome.Fail(PcErrorCode.ExpectFailed, difference);
                    resolvedBorrow = live;
                    way = way with { Target = live };
                    if (request.Kind == PcInputKind.Type) typedField = live;
                    return new PcUiaOutcome(null, null);
                };
                refreshBorrow = pending =>
                {
                    if (resolvedBorrow is not { } live) return pending;
                    if (pending.Kind == PcBorrowKind.Focus)
                        return pending with { FocusControl = live.NativeWindow,
                            Focus = async token => (await parts.Reader.Perform(handle, live.RuntimeId,
                                new PcElementCommand(PcElementAction.Focus), token).ConfigureAwait(false)).Done };
                    return resolvedPoint is { } point ? RepointBorrow(pending, point, parts.Lease!.Host.Desktop()) : pending;
                };
            }
            else if (way.Rung == PcRung.R4 && request.Point is { } canvasPoint)
            {
                var resolvedPoint = canvasPoint;
                validateBorrow = (_, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    if (parts.Catalog.Window(handle) is not { } window || !info.SameWindow(window))
                        return Task.FromResult(PcUiaOutcome.Fail(PcErrorCode.WindowGone, "the window was replaced while the borrow waited"));
                    if (CoordinateMapper.Compare(request.Window.Frame, window.Frame) == PcFrameChange.Resized)
                        return Task.FromResult(PcUiaOutcome.Fail(PcErrorCode.StaleObs, "the window changed size while the borrow waited"));
                    resolvedPoint = new PcPoint(canvasPoint.X + window.Frame.X - request.Window.Frame.X,
                        canvasPoint.Y + window.Frame.Y - request.Window.Frame.Y);
                    return Task.FromResult(PointRefusal(resolvedPoint) is { } pointFailure
                        ? PcUiaOutcome.Fail(pointFailure.Error!.Value, pointFailure.Reason!) : PcUiaOutcome.Ok);
                };
                refreshBorrow = pending => RepointBorrow(pending, resolvedPoint, parts.Lease!.Host.Desktop());
            }
            var sent = false;
            var outcome = parts.Lease is { } lease
                ? await lease.RunWithPermission(call.GrantLive, () => way.Run(call.Token), validateBorrow, refreshBorrow, count => { sent = true; textSent += count; }).WaitAsync(call.Token).ConfigureAwait(false)
                : await way.Run(call.Token).WaitAsync(call.Token).ConfigureAwait(false);
            var grantRemoved = call.GrantLive?.Invoke() == false;
            // An action already dispatched keeps its actual outcome. A refused borrow sent nothing.
            if (grantRemoved && !outcome.Done && !sent && textSent == 0 && way.Rung == PcRung.R4 && outcome.Error == PcErrorCode.NeedsRealInput)
                return PcResult.Fail(PcErrorCode.NotApproved, "the app was removed from Tactus's apps while the step waited");
            PcResult GrantNote(PcResult result) => grantRemoved || call.GrantLive?.Invoke() == false
                ? result with { Body = (result.Body is null ? "" : result.Body + "\n") + "the grant was then removed; no further action was sent" } : result;
            call.Timings.Mark("act");
            if (request.Kind == PcInputKind.Drag && request.Path.Count > 1) AimPointer(info, request, way, request.Path[^1]);
            call.Audit.Rung = way.Rung + " " + way.Method;
            var post = parts.Foreground.Read();
            var learned = Learn(info, way, pre, post, windowsBefore, started);
            grantRemoved |= call.GrantLive?.Invoke() == false;
            if (grantRemoved && !outcome.Done && request.Kind == PcInputKind.Type && textSent > 0)
                return GrantNote(PcResult.Fail(PcErrorCode.ExpectFailed, GrantCut(textSent, typed?.Length ?? request.Text!.Length)));
            if (!outcome.Done)
            {
                last = PcResult.Fail(outcome.Error ?? PcErrorCode.NotSupported, outcome.Reason ?? "the app refused the action", Line(info, Latest(handle)));
                // A text stopped by a change while paused is partly typed: no other way types it again.
                if (grantRemoved || outcome.Error is PcErrorCode.Disabled or PcErrorCode.PasswordField or PcErrorCode.StaleElement or PcErrorCode.ExpectFailed) return GrantNote(last);
                if (way.Probe && way.Key is { } failedKey) parts.Ladder.Capabilities.Refuse(failedKey);
                continue;
            }
            var target = way.Target ?? request.Element;
            PcCheck? check = null;
            PcVerdict verdict = PcVerdict.Unverified("not checked");
            var checks = 0;
            async Task<bool> Holds()
            {
                // In a batch the first check reads the window afresh (one cached UIA fetch), so a step whose value already
                // reads back right goes on without waiting for its change event (critique C21).
                var after = await Tree(handle, fresh: call.InBatch && checks++ == 0, call.Token).ConfigureAwait(false);
                var now = parts.Catalog.Window(handle);
                // The user pressed in the window since the action: where their focus is now is their doing (critique C26).
                var seen = noise;
                if (parts.Input.Presses(handle) != pressesAt && after?.Focused is { } userFocus)
                    seen = new HashSet<string>(noise, StringComparer.Ordinal) { after.Elements[userFocus].RuntimeId };
                check = new PcCheck
                {
                    Command = way.Command,
                    Kind = request.Kind,
                    Before = before,
                    After = now is null ? null : after,
                    Target = target,
                    TargetAfter = target is not null && after?.IndexOf(target.RuntimeId) is { } at ? after.Elements[at] : null,
                    Typed = typed,
                    Insert = request.Insert,
                    TitleBefore = info.Title,
                    TitleAfter = now?.Title,
                    WindowClosed = now is null,
                    Minimized = now?.Minimized ?? false,
                    NewWindows = NewWindows(info, windowsBefore),
                    Noise = seen,
                    StillRunning = outcome.StillRunning,
                };
                verdict = PcVerify.Implicit(check);
                if (verdict.Passed && expect is not null) verdict = PcVerify.Expected(expect, check);
                return verdict.Passed;
            }
            PcSettleResult settled;
            using (var cut = CancellationTokenSource.CreateLinkedTokenSource(call.Token, parts.Pause.PausedToken(info.App.Key)))
            {
                try
                {
                    settled = await settle.Until(handle, structureSeen, Busy(before), call.InBatch, Holds, cut.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!call.Token.IsCancellationRequested)
                {
                    // Paused while the action settled: one read of the window is its post-check (DESIGN.md 10.1), then the step ends.
                    var started2 = parts.Clock.NowMs;
                    var holds = await Holds().ConfigureAwait(false);
                    settled = new PcSettleResult(holds, !holds, parts.Clock.NowMs - started2);
                }
            }
            call.Timings.Mark("settle");
            if (way.Key is { } key && learned)
            {
                if (verdict.Passed && way.Probe) parts.Ladder.Capabilities.Passed(key);
                else if (!verdict.Passed && way.Probe) parts.Ladder.Capabilities.Refuse(key);
            }
            last = await Conclude(info, request, way, check!, verdict, settled, post, call).ConfigureAwait(false);
            if (grantRemoved || call.GrantLive?.Invoke() == false || verdict.Passed || tried == 2 || check!.WindowClosed) return GrantNote(last);
            // A failed check tries the next way once (DESIGN.md 2.12); the window is read again first.
            before = await Tree(handle, fresh: false, call.Token).ConfigureAwait(false) ?? before;
        }
        return last ?? PcResult.Fail(PcErrorCode.NeedsRealInput, plan.Reason ?? PcRealInputReason.Off, Line(info, Latest(handle)));
    }

    private PcResult? PointRefusal(PcPoint point) => parts.Own.Rects().Any(r => r.Contains(point))
        ? PcResult.Fail(PcErrorCode.OwnWindow, "the point is on nth's own window")
        : !parts.Catalog.Monitors().Any(m => m.Bounds.Contains(point)) ? PcResult.Fail(PcErrorCode.OffScreen, "the point is on no screen") : null;

    private static PcBorrowRequest RepointBorrow(PcBorrowRequest pending, PcPoint point, PcRect desktop)
    {
        if (pending.Point is not { } oldPoint) return pending;
        return pending with { Point = point, Chunks = pending.Chunks.Select(chunk =>
            (IReadOnlyList<Native.PcInputEvent>)chunk.Select(e =>
            {
                if (e.Kind != Native.PcInputKind.Mouse || (e.Flags & Native.PcInputPlan.MOVE) == 0) return e;
                var old = CoordinateMapper.FromAbsolute(e.Dx, e.Dy, desktop);
                var mapped = Native.PcInputPlan.MoveTo(new PcPoint(old.X + point.X - oldPoint.X, old.Y + point.Y - oldPoint.Y), desktop);
                return e with { Dx = mapped.Dx, Dy = mapped.Dy };
            }).ToList()).ToList() };
    }

    /// <summary>
    /// The Tactus pointer shows where this way acts (DESIGN.md 4.5): the target's centre, else the given point, or a drag's
    /// start (and, after it ran, its end). Whether the user can see it: the window is not minimized and is the root window
    /// under the point. Raised before the action, never waited for.
    /// </summary>
    private void AimPointer(PcWindowInfo info, PcInputRequest request, PcRungPlan way, PcPoint? dropped = null)
    {
        if (parts.Pointer is not { } pointer || !parts.Settings().ShowPointer) return;
        var node = way.Target ?? request.Element;
        var at = dropped
            ?? (request.Kind == PcInputKind.Drag && request.Path.Count > 0 ? request.Path[0]
            : node is { Bounds: { Width: > 0, Height: > 0 } bounds } ? bounds.Center : request.Point);
        if (at is not { } point) return;
        var act = dropped is not null ? PcPointerAct.Move : request.Kind switch
        {
            PcInputKind.Click or PcInputKind.Command or PcInputKind.Drag => PcPointerAct.Press,
            PcInputKind.Type or PcInputKind.Key => PcPointerAct.Type,
            _ => PcPointerAct.Move,
        };
        var live = parts.Catalog.Window(info.Handle);
        var under = parts.Catalog.RootAt(point);
        var visible = live is { Minimized: false } && (under == info.Handle || (under != 0 && under == RootOwner(info)));
        pointer.Aim(new PcPointerAim(info.App.Key, AppName(info), info.ExePath, info.Handle, point, act, visible));
    }

    // The top of a window's owner chain (an owned dialog's main window), as the catalog reports owners.
    private nint RootOwner(PcWindowInfo info)
    {
        var root = info;
        for (var i = 0; i < 8 && root.Owner != 0 && parts.Catalog.Window(root.Owner) is { } owner; i++) root = owner;
        return root.Handle;
    }

    // Copy, cut and paste use the user's clipboard (DESIGN.md 2.8, critique C6): refused, whichever way they would run.
    private static PcResult? Clipboard(PcInputRequest request, PcRungPlan way)
    {
        if (request.Kind is not (PcInputKind.Click or PcInputKind.Key or PcInputKind.Command)) return null;
        var node = way.Target ?? (request.Kind == PcInputKind.Click ? request.Element : null);
        return PcClipboard.IsClipboardCommand(way.MenuText) || PcClipboard.IsClipboardCommand(node?.Name)
            ? PcResult.Fail(PcErrorCode.NotSupported, PcClipboard.Refusal)
            : null;
    }

    /// <summary>
    /// The guarded-action card (DESIGN.md 4.3, critique C13), checked at run time on what this way will press: the name in
    /// English, Hindi or Urdu, the AutomationId, the MSAA default action, the class, a menu item's text; in a mail or chat
    /// app also a button with no name. Asked once per step.
    /// </summary>
    private async Task<PcResult?> Guard(PcWindowInfo info, PcInputRequest request, PcRungPlan way, PcTree before, HashSet<string> confirmed, PcCall call)
    {
        if (!parts.Settings().ConfirmGuardedActions) return null;
        var presses = request.Kind is PcInputKind.Click or PcInputKind.Key
            || way.Command?.Action is PcElementAction.Invoke or PcElementAction.Close or PcElementAction.Toggle or PcElementAction.Select;
        if (!presses) return null;
        var node = way.Target ?? request.Element;
        // Enter or Space with no target presses the app's focused control.
        if (node is null && request.Kind == PcInputKind.Key && request.Chord is { Modifiers.Count: 0 } chord && chord.Key is KeyChordParser.Return or 0x20
            && before.Focused is { } focused)
            node = before.Elements[focused];
        var word = PcPolicy.GuardedWord(node?.Name, node?.AutomationId) ?? PcPolicy.GuardedWord(node?.DefaultAction)
            ?? PcPolicy.GuardedWord(null, node?.ClassName) ?? PcPolicy.GuardedWord(way.MenuText);
        var name = node?.Name is { Length: > 0 } named ? named : way.MenuText ?? "";
        if (word is null && node is not null && request.Kind != PcInputKind.Key && PcPolicy.UnnamedButtonNeedsConfirmation(info.App, node))
        {
            word = "press";
            name = "a button with no name (it may be Send)";
        }
        if (word is null) return null;
        var key = word + "\u0001" + name;
        if (confirmed.Contains(key)) return null;
        bool allowed;
        call.NeedsFreshTarget = true;
        using (call.Wait.Waiting(PcWaitReason.ConfirmCard))
            allowed = await parts.Confirmer.Confirm(Source(info), word, name, AppName(info), call.Token).ConfigureAwait(false);
        call.Timings.Mark("confirm");
        if (!allowed) return PcResult.Fail(PcErrorCode.NeedsConfirmation, "the user did not confirm " + PcWindowLine.Quote(name));
        confirmed.Add(key);
        return null;
    }

    /// <summary>
    /// The yield rule (DESIGN.md 2.9): a write into the user's control waits (UserControlTracker: it has the focus and they
    /// typed into it within 2 s, or their pointer is pressed on it). It runs as soon as the control is no longer theirs;
    /// after 10 s it answers user_typing_here and nothing is written. Writes into other controls run at once.
    /// </summary>
    private async Task<PcResult?> Yield(PcWindowInfo info, PcUiNode? target, PcCall call)
    {
        var start = parts.Clock.NowMs;
        var focused = target is null || target.Has(PcStates.Focused);
        IDisposable? waiting = null;
        try
        {
            while (true)
            {
                var now = parts.Clock.NowMs;
                var user = parts.Foreground.Read();
                var check = UserControlTracker.Check(parts.Input.Snapshot(), now, new PcWriteTarget(info.Handle, target, user.Foreground == info.Handle, user.Focus, focused));
                if (!check.Users) return null;
                // Paused while waiting for the user's typing: the step holds and starts again, its 10 s afresh.
                if (parts.Pause.PausedSince(info.App.Key, call.PauseEpoch)) throw new PcHeld();
                var waited = now - start;
                if (waited >= YieldMs) return PcResult.Fail(PcErrorCode.UserTypingHere, UsersControl(check.Hold, target, info));
                call.NeedsFreshTarget = true;
                waiting ??= call.Wait.Waiting(PcWaitReason.TypingHere);
                // A timeout tied to the condition: the moment the user's last key there is 2 s old, or the 10 s limit.
                var until = (int)Math.Max(1, Math.Min(check.AgainMs - now, YieldMs - waited));
                using (var cut = CancellationTokenSource.CreateLinkedTokenSource(call.Token, parts.Pause.PausedToken(info.App.Key)))
                {
                    try { await parts.Clock.Delay(until, cut.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (!call.Token.IsCancellationRequested) { throw new PcHeld(); }
                }
                call.Timings.Mark("yield");
                // The user may have moved on to another field: an element with no window of its own is asked again.
                if (target is { NativeWindow: 0 } element)
                {
                    var live = await parts.Reader.Element(info.Handle, element.RuntimeId, call.Token).WaitAsync(call.Token).ConfigureAwait(false);
                    focused = live?.Has(PcStates.Focused) ?? false;
                }
            }
        }
        finally
        {
            waiting?.Dispose();
        }
    }

    private string UsersControl(PcUserHold hold, PcUiNode? target, PcWindowInfo info)
    {
        var what = (target is null ? "this window's focused field" : PcVerify.Describe(target)) + " (" + AppName(info) + ")";
        return hold switch
        {
            PcUserHold.Pressed => "the user is holding the mouse button on " + what,
            PcUserHold.Unseen => "the user may be typing in " + what + ": Tactus cannot see the keyboard right now",
            _ => "the user is typing in " + what,
        };
    }

    /// <summary>
    /// Shared app state (DESIGN.md 2.9, AppStateGuard): before a cursor-free way runs, an app-global way waits for a still
    /// moment while the user is active in the app (the borrow's gate, words and answers), a focus-moving way while they are
    /// active in this window, and a way that can shift the layout while their pointer moved inside the window within
    /// 500 ms. A borrow (R4) waits at the lease's own gate. Returns deferred_by_user for "Not now", and whether it waited.
    /// </summary>
    private async Task<(PcResult? Deferred, bool Waited)> Share(PcWindowInfo info, PcInputRequest request, PcRungPlan way, PcCall call)
    {
        if (way.Rung == PcRung.R4) return (null, false);
        var effect = AppStateGuard.Classify(request, way);
        if (effect == PcStepEffect.None) return (null, false);
        var handle = info.Handle;
        var waited = false;
        using var cut = CancellationTokenSource.CreateLinkedTokenSource(call.Token, parts.Pause.PausedToken(info.App.Key));
        try
        {
            var now = parts.Clock.NowMs;
            var still = (effect.HasFlag(PcStepEffect.AppGlobal) && AppStateGuard.ActiveIn(parts.Input, AppWindows(info), now))
                || (effect.HasFlag(PcStepEffect.MovesFocus) && AppStateGuard.ActiveIn(parts.Input, [handle], now));
            if (still)
            {
                waited = true;
                var status = await parts.Lease.Still(info.App.Key, "a step in " + AppName(info), call.Wait.Waiting, cut.Token).ConfigureAwait(false);
                call.Timings.Mark("still");
                if (status == PcBorrowStatus.Deferred)
                {
                    return (PcResult.Fail(PcErrorCode.DeferredByUser, "the user chose Not now while working in " + AppName(info)
                        + "; do another step or try this one later", Line(info, Latest(handle))), true);
                }
            }
            if (effect.HasFlag(PcStepEffect.LayoutShift))
            {
                IDisposable? mark = null;
                try
                {
                    while (AppStateGuard.PointerNear(parts.Input.Snapshot(), handle, parts.Clock.NowMs) is { } quiet)
                    {
                        waited = true;
                        mark ??= call.Wait.Waiting(PcWaitReason.HandsOff);
                        await parts.Clock.Delay((int)Math.Max(1, quiet - parts.Clock.NowMs), cut.Token).ConfigureAwait(false);
                    }
                }
                finally
                {
                    mark?.Dispose();
                }
                call.Timings.Mark("pointer");
            }
        }
        catch (OperationCanceledException) when (!call.Token.IsCancellationRequested)
        {
            // Paused while waiting: the step holds and starts again from its own top.
            throw new PcHeld();
        }
        return (null, waited);
    }

    // After a wait in which the user pressed in the window: the target read live; gone, or changed from what the step resolved, stops it.
    private async Task<PcResult?> ChangedWhileWaiting(PcWindowInfo info, PcUiNode element, PcCall call)
    {
        var live = await parts.Reader.Element(info.Handle, element.RuntimeId, call.Token).WaitAsync(call.Token).ConfigureAwait(false);
        if (live is null) return PcResult.Fail(PcErrorCode.StaleElement, PcVerify.Describe(element) + " is gone; the user worked in the window while Tactus waited");
        return Difference(element, live, "the user changed " + PcVerify.Describe(live) + " while Tactus waited") is { } changed
            ? PcResult.Fail(PcErrorCode.ExpectFailed, changed)
            : null;
    }

    /// <summary>
    /// The delta check (DESIGN.md 2.6, critique C16): the user's foreground, pointer and focused control before and after a
    /// way. A way that moved the foreground (other than to a window the app opened) or the pointer is refused for 7 days in
    /// this app; one that moved the focus is marked focus-moving. When the user gave physical input during the way, or the
    /// hooks could not see it, nothing is learned (the move may be theirs). Returns whether the way was clean.
    /// </summary>
    private bool Learn(PcWindowInfo info, PcRungPlan way, PcUserState pre, PcUserState post, IReadOnlySet<nint> windowsBefore, long started)
    {
        var input = parts.Input.Snapshot();
        if (!input.Healthy || input.LastUserMs >= started) return false;
        var foregroundMoved = post.Foreground != pre.Foreground
            && !(post.Foreground != info.Handle && NewWindows(info, windowsBefore).Any(w => w.Handle == post.Foreground));
        var pointerMoved = post.Cursor != pre.Cursor;
        if (foregroundMoved || pointerMoved)
        {
            if (way.Key is { } key) parts.Ladder.Capabilities.Refuse(key);
            return false;
        }
        if (post.Focus != pre.Focus && post.Foreground == pre.Foreground)
        {
            if (way.Key is { } key) parts.Ladder.Capabilities.FocusMoving(key);
            return false;
        }
        return true;
    }

    private static bool Busy(PcTree tree) =>
        tree.Elements.Values.Any(n => n.Role.Contains("progress", StringComparison.OrdinalIgnoreCase) || n.Role.Contains("busy", StringComparison.OrdinalIgnoreCase));

    // After a way: record the new look and answer with the check, what changed, and the rung used.
    private async Task<PcResult> Conclude(PcWindowInfo info, PcInputRequest request, PcRungPlan way, PcCheck check, PcVerdict verdict, PcSettleResult settled,
        PcUserState post, PcCall call)
    {
        var handle = info.Handle;
        var extra = new StringBuilder();
        foreach (var window in check.NewWindows)
        {
            lock (gate) opened.Add(window.Handle);
            Remember(window);
            extra.Append("\nnew window: ").Append(PcWindowRef.Format(window.Handle)).Append(' ').Append(PcWindowLine.Quote(TreeFormatter.Clean(window.Title, 120)));
            if (post.Foreground == window.Handle)
            {
                extra.Append(" (it took the foreground)");
                // The user sees why their focus moved: "Excel opened Save As" on the pill for 2 s (DESIGN.md 2.9).
                parts.Control.Opened(new PcOpenedNotice(info.App.Key, AppName(info), TreeFormatter.Clean(window.Title, 60)));
            }
        }
        call.Output.Opened = check.NewWindows;
        if (settled.TimedOut) extra.Append("\nthe window was still changing after ").Append(Count(settled.ElapsedMs)).Append(" ms");
        if (check.StillRunning) extra.Append("\nthe app is still running the action (it may be showing a dialog)");
        if (check.Typed is not null && check.Target is null) extra.Append("\nnot read back: the window's focused control does not show its text");
        var head = "done: " + (way.Detail.Length > 0 ? way.Detail : request.Kind.ToString().ToLowerInvariant()) + "\nrung: " + way.Rung + " (" + way.Method + ")";
        call.Timings.Mark("verify");
        var now = parts.Catalog.Window(handle);
        if (now is null || check.After is null)
        {
            var closed = head + "\nthe window closed" + extra;
            return verdict.Passed ? PcResult.Success(Line(info, null), closed) : Fail(verdict, Line(info, null), closed);
        }
        var previous = Latest(handle);
        var observation = Record(now, check.After, CachedVersion(handle), null);
        var diff = previous is null ? check.After.Render(null, 120) : TreeDiff.Compute(previous.Tree, check.After, previous.Id) ?? "many changes: call pc_observe with full";
        var body = call.InBatch && verdict.Passed ? head + extra : head + "\n" + diff + extra;
        call.Timings.Mark("observe");
        var line = Line(now, observation);
        if (!verdict.Passed) return Fail(verdict, line, body);
        var result = PcResult.Success(line, body);
        // A point click shows the model exactly what it hit: a 256 x 256 crop at full resolution (critique C27).
        if (request.Point is { } point && NoPixels(now) is null)
        {
            var grab = await parts.Capture!.Grab(handle, now.Frame, call.Token).ConfigureAwait(false);
            if (grab is { Frame: var frame })
            {
                var crop = parts.Capture.AroundPoint(frame, new PcPoint(point.X - now.Frame.X, point.Y - now.Frame.Y), Grid());
                result = result with { Body = body + "\nthe crop around the click follows at full resolution", Details = [new PcImage(crop.Jpeg, crop.Width, crop.Height, observation.Id)] };
            }
            call.Timings.Mark("capture");
        }
        return result;
    }

    private static PcResult Fail(PcVerdict verdict, PcWindowLine line, string body) => verdict.Kind == PcVerdictKind.Unverified
        ? PcResult.Unverified(verdict.Saw ?? "no change tied to the target was seen", line, body)
        : PcResult.Fail(PcErrorCode.ExpectFailed, verdict.Saw ?? "the check did not hold", line) with { Body = body };
}
