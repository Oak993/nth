// PORT-ONLY: Tactus, PC control (OD-0140, OD-0141, OD-0144, ED-NEW-pcctl-6, ED-NEW-pcctl-7, ED-NEW-pcctl-8; DESIGN.md 2.2,
// 2.8, 2.12). The tools that act and wait in one window: pc_click, pc_type, pc_set_value, pc_key, pc_scroll, pc_drag,
// pc_action, pc_focus_window (asks on the pill, then shows the window without activating it), pc_read and pc_wait. Each
// builds one input request for the action ladder; the checks, the rung choice and the report are shared (Act).
using System.Text.Json.Nodes;
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl;

public sealed partial class PcControlEngine
{
    private async Task<PcResult> Step(string tool, PcWindowInfo info, JsonObject args, PcCall call)
    {
        lock (gate)
        {
            if (stoppedForTurn || stoppedApps.Contains(info.App.Key)) return PcResult.Fail(PcErrorCode.StoppedByUser, "the user stopped Tactus");
        }
        if (!parts.Settings().Enabled) return PcResult.Fail(PcErrorCode.PcOff, "Tactus was switched off");
        return tool switch
        {
            PcToolSchemas.Observe => await Observe(info, args, call).ConfigureAwait(false),
            PcToolSchemas.Click => await Click(info, args, call).ConfigureAwait(false),
            PcToolSchemas.Type => await Type(info, args, call).ConfigureAwait(false),
            PcToolSchemas.SetValue => await SetValue(info, args, call).ConfigureAwait(false),
            PcToolSchemas.Key => await Key(info, args, call).ConfigureAwait(false),
            PcToolSchemas.Scroll => await Scroll(info, args, call).ConfigureAwait(false),
            PcToolSchemas.Drag => await Drag(info, args, call).ConfigureAwait(false),
            PcToolSchemas.Action => await ElementAction(info, args, call).ConfigureAwait(false),
            PcToolSchemas.FocusWindow => await Show(info, call).ConfigureAwait(false),
            PcToolSchemas.Read => await Read(info, args, call).ConfigureAwait(false),
            PcToolSchemas.Wait => await Wait(info, args, call).ConfigureAwait(false),
            _ => PcResult.Fail(PcErrorCode.BadArgs, "there is no tool " + tool),
        };
    }

    private async Task<PcResult> Click(PcWindowInfo info, JsonObject args, PcCall call)
    {
        var button = (PcArgs.Str(args, "button") ?? "left") switch
        {
            "left" => PcMouseButton.Left,
            "right" => PcMouseButton.Right,
            "middle" => PcMouseButton.Middle,
            _ => throw new PcArgsException("button must be left, right or middle"),
        };
        var count = PcArgs.Int(args, "count") ?? 1;
        if (count is < 1 or > 3) throw new PcArgsException("count must be 1, 2 or 3");
        var modifiers = Modifiers(PcArgs.Str(args, "modifiers"));
        var expect = PcArgs.Expect(args);
        var (target, failure) = await Resolve(info, args, pointAllowed: true, required: true, call).ConfigureAwait(false);
        if (failure is not null) return failure;
        var request = new PcInputRequest(PcInputKind.Click, info) { Element = target!.Node, Point = target.Point, Button = button, Count = count, Modifiers = modifiers };
        return await Act(info, request, expect, call).ConfigureAwait(false);
    }

    private async Task<PcResult> Type(PcWindowInfo info, JsonObject args, PcCall call)
    {
        var text = PcArgs.Str(args, "text") ?? throw new PcArgsException("text is required");
        if (text.Length > 20_000) throw new PcArgsException("text is longer than 20000 characters");
        var insert = (PcArgs.Str(args, "mode") ?? "insert") switch
        {
            "insert" => true,
            "replace" => false,
            _ => throw new PcArgsException("mode must be insert or replace"),
        };
        var submit = PcArgs.Bool(args, "submit", false);
        var expect = PcArgs.Expect(args);
        var (target, failure) = await Resolve(info, args, pointAllowed: false, required: false, call).ConfigureAwait(false);
        if (failure is not null) return failure;
        // Without a target, the text goes to the app's own focused control.
        var element = target?.Node ?? await parts.Reader.Focused(info.Handle, call.Token).WaitAsync(call.Token).ConfigureAwait(false);
        if (PcPolicy.RefusesText(element)) return PcResult.Fail(PcErrorCode.PasswordField, "this is a password field");
        call.Audit.Text = text;
        var request = new PcInputRequest(PcInputKind.Type, info) { Element = element, Text = text, Insert = insert };
        var typed = await Act(info, request, submit ? null : expect, call, text).ConfigureAwait(false);
        if (!typed.Ok || !submit) return typed;
        // submit: the field's default button (Enter on a dialog), or Enter to the field, by the same ladder and checked.
        var enter = new PcInputRequest(PcInputKind.Key, info) { Element = element, Chord = new KeyChord([], KeyChordParser.Return), KeysText = "enter" };
        var submitted = await Act(info, enter, expect, call).ConfigureAwait(false);
        return submitted with { Body = typed.Body?.Split('\n')[0] + "\nthen " + submitted.Body };
    }

    private async Task<PcResult> SetValue(PcWindowInfo info, JsonObject args, PcCall call)
    {
        var value = PcArgs.Str(args, "value") ?? throw new PcArgsException("value is required");
        var expect = PcArgs.Expect(args);
        var (target, failure) = await Resolve(info, args, pointAllowed: false, required: true, call).ConfigureAwait(false);
        if (failure is not null) return failure;
        call.Audit.Text = value;
        var request = new PcInputRequest(PcInputKind.Command, info) { Element = target!.Node, Command = new PcElementCommand(PcElementAction.SetValue) { Value = value } };
        return await Act(info, request, expect, call).ConfigureAwait(false);
    }

    private async Task<PcResult> Key(PcWindowInfo info, JsonObject args, PcCall call)
    {
        var keys = PcArgs.Str(args, "keys") ?? throw new PcArgsException("keys is required");
        var parsed = KeyChordParser.Parse(keys);
        if (!parsed.Ok) return PcResult.Fail(parsed.Error ?? PcErrorCode.BadArgs, parsed.Reason ?? "unknown key");
        if (PcClipboard.IsClipboardChord(parsed.Chord!)) return PcResult.Fail(PcErrorCode.BadArgs, PcClipboard.Refusal);
        var repeat = PcArgs.Int(args, "repeat") ?? 1;
        if (repeat is < 1 or > 20) throw new PcArgsException("repeat must be 1 to 20");
        var expect = PcArgs.Expect(args);
        var (target, failure) = await Resolve(info, args, pointAllowed: false, required: false, call).ConfigureAwait(false);
        if (failure is not null) return failure;
        var request = new PcInputRequest(PcInputKind.Key, info) { Element = target?.Node, Chord = parsed.Chord, KeysText = keys, Repeat = repeat };
        return await Act(info, request, expect, call).ConfigureAwait(false);
    }

    private async Task<PcResult> Scroll(PcWindowInfo info, JsonObject args, PcCall call)
    {
        var direction = (PcArgs.Str(args, "direction") ?? "") switch
        {
            "up" => PcScrollDirection.Up,
            "down" => PcScrollDirection.Down,
            "left" => PcScrollDirection.Left,
            "right" => PcScrollDirection.Right,
            _ => throw new PcArgsException("direction must be up, down, left or right"),
        };
        var unit = (PcArgs.Str(args, "unit") ?? "page") switch
        {
            "page" => PcScrollUnit.Page,
            "line" => PcScrollUnit.Line,
            _ => throw new PcArgsException("unit must be page or line"),
        };
        var amount = PcArgs.Num(args, "amount") ?? 1;
        if (amount is < 0.1 or > 50) throw new PcArgsException("amount must be 0.1 to 50");
        var expect = PcArgs.Expect(args);
        var (target, failure) = await Resolve(info, args, pointAllowed: true, required: false, call).ConfigureAwait(false);
        if (failure is not null) return failure;
        var tree = target?.Tree ?? await Tree(info.Handle, fresh: false, call.Token).ConfigureAwait(false);
        if (tree is null) return PcResult.Fail(PcErrorCode.WindowGone, "the window is gone");
        // What scrolls: the target or its nearest scrollable ancestor; with a bare point, the scrollable element under it;
        // without a target, the largest scrollable area.
        PcUiNode? scrollable = null;
        if (target?.Node is { } node && node.Can(PcActions.Scroll)) scrollable = node;
        else if (target is { Number: > 0 } && PcElementMatch.Ancestor(tree, target.Number, PcActions.Scroll) is > 0 and var ancestor) scrollable = tree.Elements[ancestor];
        else if (target?.Point is { } point && PcElementMatch.AtPoint(tree, point) is > 0 and var under && PcElementMatch.Ancestor(tree, under, PcActions.Scroll) is > 0 and var holder)
            scrollable = tree.Elements[holder];
        else if (target is null)
            scrollable = tree.Elements.Values.Where(n => n.Can(PcActions.Scroll) && !n.Bounds.IsEmpty).MaxBy(n => (long)n.Bounds.Width * n.Bounds.Height);
        var request = new PcInputRequest(PcInputKind.Scroll, info) { Element = scrollable, Point = target?.Point, Direction = direction, Unit = unit, Amount = amount };
        return await Act(info, request, expect, call).ConfigureAwait(false);
    }

    private async Task<PcResult> Drag(PcWindowInfo info, JsonObject args, PcCall call)
    {
        var observation = Observation(PcArgs.Str(args, "obs"));
        if (observation is null || observation.Window.Handle != info.Handle || observation.Shot is null || LatestShot(info.Handle)?.Number != observation.Number)
            return PcResult.Fail(PcErrorCode.StaleObs, "a drag needs the obs of this window's latest screenshot");
        if (args["path"] is not JsonArray { Count: >= 2 and <= 50 } path) throw new PcArgsException("path must hold 2 to 50 points");
        var hold = PcArgs.Int(args, "hold_ms") ?? 0;
        if (hold is < 0 or > 2000) throw new PcArgsException("hold_ms must be 0 to 2000");
        var modifiers = Modifiers(PcArgs.Str(args, "modifiers"));
        var expect = PcArgs.Expect(args);
        if (info.Minimized) return PcResult.Fail(PcErrorCode.OffScreen, "the window is minimized");
        if (CoordinateMapper.Compare(observation.Window.Frame, info.Frame) == PcFrameChange.Resized)
            return PcResult.Fail(PcErrorCode.StaleObs, "the window changed size since " + observation.Id);
        var points = new List<PcPoint>();
        foreach (var item in path)
        {
            var (x, y) = PcArgs.Pair(item, "path")!.Value;
            if (!CoordinateMapper.OnGrid(observation.Shot.Grid, x, y)) return PcResult.Fail(PcErrorCode.OffScreen, "a point of the path is outside the screenshot");
            var screen = CoordinateMapper.ToScreen(info.Frame, observation.Shot.Grid.Scale, x, y);
            if (parts.Own.Rects().Any(r => r.Contains(screen))) return PcResult.Fail(PcErrorCode.OwnWindow, "the path crosses nth's own window");
            if (!parts.Catalog.Monitors().Any(m => m.Bounds.Contains(screen))) return PcResult.Fail(PcErrorCode.OffScreen, "a point of the path is on no screen");
            points.Add(screen);
        }
        if (StaleView(info, observation) is { } stale) return stale;
        var request = new PcInputRequest(PcInputKind.Drag, info) { Path = points, Modifiers = modifiers, HoldMs = hold };
        return await Act(info, request, expect, call).ConfigureAwait(false);
    }

    private async Task<PcResult> ElementAction(PcWindowInfo info, JsonObject args, PcCall call)
    {
        var action = (PcArgs.Str(args, "action") ?? "") switch
        {
            "expand" => PcElementAction.Expand,
            "collapse" => PcElementAction.Collapse,
            "toggle" => PcElementAction.Toggle,
            "select" => PcElementAction.Select,
            "focus" => PcElementAction.Focus,
            "scroll_into_view" => PcElementAction.ScrollIntoView,
            "minimize" => PcElementAction.Minimize,
            "maximize" => PcElementAction.Maximize,
            "restore" => PcElementAction.Restore,
            "close" => PcElementAction.Close,
            _ => throw new PcArgsException("action must be one of " + string.Join(", ", PcToolSchemas.ElementActions)),
        };
        var expect = PcArgs.Expect(args);
        var (target, failure) = await Resolve(info, args, pointAllowed: false, required: true, call).ConfigureAwait(false);
        if (failure is not null) return failure;
        var node = target!.Node!;
        // The window actions act on the window itself, whichever of its elements was named.
        if (action is PcElementAction.Minimize or PcElementAction.Maximize or PcElementAction.Restore or PcElementAction.Close && !node.Can(PcActions.Window))
            node = target.Tree.Elements[1];
        var request = new PcInputRequest(PcInputKind.Command, info) { Element = node, Command = new PcElementCommand(action) };
        return await Act(info, request, expect, call).ConfigureAwait(false);
    }

    // pc_focus_window (critique C15): the pill asks "Show <app>?" first; the window is then restored and raised without
    // being activated, so the user's keyboard stays where it is.
    private async Task<PcResult> Show(PcWindowInfo info, PcCall call)
    {
        PcShowAnswer answer;
        using (call.Wait.Waiting(PcWaitReason.ConfirmCard))
            answer = await parts.Confirmer.Show(Source(info), AppName(info), info.Title, call.Token).ConfigureAwait(false);
        call.Timings.Mark("confirm");
        if (answer != PcShowAnswer.Show) return PcResult.Fail(PcErrorCode.DeclinedByUser, "the user chose Not now");
        var shown = parts.Catalog.Show(info.Handle);
        call.Timings.Mark("act");
        return shown
            ? PcResult.Success(Line(info, Latest(info.Handle)), "the window is shown to the user; their keyboard stays where it was")
            : PcResult.Fail(PcErrorCode.NotSupported, "Windows did not show the window");
    }

    private async Task<PcResult> Read(PcWindowInfo info, JsonObject args, PcCall call)
    {
        var offset = PcArgs.Int(args, "offset") ?? 0;
        if (offset < 0) throw new PcArgsException("offset must be 0 or more");
        var length = PcArgs.Int(args, "length") ?? 8000;
        if (length is < 1 or > 20_000) throw new PcArgsException("length must be 1 to 20000");
        PcUiNode? node;
        if (args["element"] is null && args["target"] is null)
        {
            node = await parts.Reader.Focused(info.Handle, call.Token).WaitAsync(call.Token).ConfigureAwait(false);
            if (node is null) throw new PcArgsException("name the element to read (the window has no focused element Tactus can see)");
        }
        else
        {
            var (target, failure) = await Resolve(info, args, pointAllowed: false, required: true, call).ConfigureAwait(false);
            if (failure is not null) return failure;
            node = target!.Node!;
        }
        if (node.Has(PcStates.Password)) return PcResult.Fail(PcErrorCode.PasswordField, "a password field is never read");
        var text = await parts.Reader.ReadText(info.Handle, node.RuntimeId, call.Token).WaitAsync(call.Token).ConfigureAwait(false) ?? "";
        call.Timings.Mark("read");
        var from = Math.Min(offset, text.Length);
        var to = Math.Min(text.Length, from + length);
        if (to < text.Length && to > from && char.IsHighSurrogate(text[to - 1])) to--;
        var page = text[from..to];
        call.Output.ReadText = page;
        var head = "text " + Count(from) + " to " + Count(to) + " of " + Count(text.Length) + " characters of " + PcVerify.Describe(node) + (to < text.Length ? " (more: offset " + Count(to) + ")" : "");
        return PcResult.Success(Line(info, Latest(info.Handle)), head + "\n" + page);
    }

    private async Task<PcResult> Wait(PcWindowInfo info, JsonObject args, PcCall call)
    {
        var until = PcArgs.Str(args, "until") ?? throw new PcArgsException("until is required");
        var text = PcArgs.Str(args, "text");
        if (until is not ("idle" or "text" or "gone" or "expect")) throw new PcArgsException("until must be idle, text, gone, expect or step");
        if (until is "text" or "gone" && string.IsNullOrEmpty(text)) throw new PcArgsException("text is required for until " + until);
        var expect = PcArgs.Expect(args);
        if (until == "expect" && expect is null) throw new PcArgsException("expect is required for until expect");
        var timeout = PcArgs.Int(args, "timeout_ms") ?? 5000;
        if (timeout is < 100 or > 15_000) throw new PcArgsException("timeout_ms must be 100 to 15000");
        var start = parts.Clock.NowMs;
        if (until == "idle")
        {
            var quiet = await settle.Quiet(info.Handle, timeout, call.Token).ConfigureAwait(false);
            call.Timings.Mark("wait");
            return quiet.Held
                ? PcResult.Success(Line(info, Latest(info.Handle)), "idle after " + Count(quiet.ElapsedMs) + " ms")
                : PcResult.Fail(PcErrorCode.Timeout, "the window kept changing for " + Count(timeout) + " ms");
        }
        var locator = PcArgs.Locator(args);
        var element = PcArgs.Int(args, "element");
        var windowsBefore = AppWindows(info);
        PcElementIdentity? identity = element is { } number && Latest(info.Handle) is { } seen && seen.Tree.Elements.ContainsKey(number) ? PcElementMatch.Identity(seen.Tree, number) : null;
        while (true)
        {
            var version = parts.Feed.Version(info.Handle);
            var tree = await Tree(info.Handle, fresh: false, call.Token).ConfigureAwait(false);
            if (tree is null) return until == "gone" ? PcResult.Success(Line(info, null), "the window closed") : PcResult.Fail(PcErrorCode.WindowGone, "the window closed");
            string? held = null;
            if (until == "expect")
            {
                var target = locator is not null && PcElementMatch.Find(tree, locator) is { Found: true } found ? tree.Elements[found.Index]
                    : identity is not null && PcElementMatch.Resolve(tree, identity) is { Found: true } again ? tree.Elements[again.Index] : null;
                var check = new PcCheck { After = tree, Target = target, TargetAfter = target, NewWindows = NewWindows(info, windowsBefore) };
                if (PcVerify.Expected(expect!, check).Passed) held = "the expect holds";
            }
            else if (PcVerify.Contains(tree, text!) == (until == "text"))
            {
                held = (until == "text" ? "found " : "gone: ") + PcWindowLine.Quote(text!);
            }
            if (held is not null)
            {
                call.Timings.Mark("wait");
                var previous = Latest(info.Handle);
                var observation = Record(info, tree, CachedVersion(info.Handle), null);
                var diff = previous is null ? "" : "\n" + (TreeDiff.Compute(previous.Tree, tree, previous.Id) ?? "many changes: call pc_observe with full");
                return PcResult.Success(Line(info, observation), held + " after " + Count(parts.Clock.NowMs - start) + " ms" + diff);
            }
            var left = (int)(start + timeout - parts.Clock.NowMs);
            if (left <= 0)
            {
                var what = until switch
                {
                    "text" => "no " + PcWindowLine.Quote(text!),
                    "gone" => PcWindowLine.Quote(text!) + " is still there",
                    _ => "the expect does not hold",
                };
                return PcResult.Fail(PcErrorCode.Timeout, what + " after " + Count(timeout) + " ms");
            }
            await parts.Feed.Changed(info.Handle, version, left, call.Token).ConfigureAwait(false);
        }
    }

    private static IReadOnlyList<ushort> Modifiers(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var parsed = KeyChordParser.Parse(text.Trim() + "+a");
        if (!parsed.Ok) throw new PcArgsException("modifiers must be keys such as ctrl, shift or ctrl+shift");
        return parsed.Chord!.Modifiers;
    }
}
