// PORT-ONLY: Tactus, PC control (OD-0140, OD-0144, ED-NEW-pcctl-7, ED-NEW-pcctl-8; DESIGN.md 2.12, critique C26). Every
// action is checked locally before its call returns. Each action has an implicit post-condition: a value that reads back
// equal (NFC and line endings normalised, for Urdu and Hindi too), a toggle that flipped, an item that is selected, a
// branch expanded or a popup opened, a scroll that moved, a window that closed. An Invoke must cause a change Tactus can
// tie to its target: in the target or its subtree, in its parent's state, a new window of the same process, or a focus or
// title change. Elements that changed on their own since Tactus last looked (a clock, a caret, an animation) and the
// control the user is typing in do not count. Only unrelated changes give "unverified"; no change at all gives
// "expect_failed: nothing changed". The model's own expect is then checked on the same fresh tree.
using System.Globalization;
using System.Text;
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl;

public enum PcVerdictKind
{
    Pass,
    Unverified,
    Mismatch,
}

/// <summary>A check's verdict: pass, unverified (only unrelated changes) or a mismatch, with what was seen.</summary>
public sealed record PcVerdict(PcVerdictKind Kind, string? Saw)
{
    public bool Passed => Kind == PcVerdictKind.Pass;
    public static PcVerdict Pass { get; } = new(PcVerdictKind.Pass, null);
    public static PcVerdict Mismatch(string saw) => new(PcVerdictKind.Mismatch, saw);
    public static PcVerdict Unverified(string saw) => new(PcVerdictKind.Unverified, saw);
}

/// <summary>What the check of one action looks at: the trees before and after, the target in both, and what else changed.</summary>
public sealed record PcCheck
{
    /// <summary>The command the rung ran (null for posted keys, posted text and point clicks).</summary>
    public PcElementCommand? Command { get; init; }

    /// <summary>The kind of input (click, type, key, scroll, drag, element action).</summary>
    public PcInputKind Kind { get; init; }

    public PcTree? Before { get; init; }
    public PcTree? After { get; init; }
    public PcUiNode? Target { get; init; }

    /// <summary>The target found again in the fresh tree (by identity); null when it is gone.</summary>
    public PcUiNode? TargetAfter { get; init; }

    /// <summary>Typed text, and whether it was written in insert mode (else it replaced the field).</summary>
    public string? Typed { get; init; }
    public bool Insert { get; init; }

    public string? TitleBefore { get; init; }
    public string? TitleAfter { get; init; }
    public bool WindowClosed { get; init; }
    public bool Minimized { get; init; }
    public IReadOnlyList<PcWindowInfo> NewWindows { get; init; } = [];

    /// <summary>RuntimeIds that changed on their own since Tactus last looked, and the user's own control while they type.</summary>
    public IReadOnlySet<string> Noise { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>An Invoke that was still running after 1.5 s (a modal dialog from inside the call).</summary>
    public bool StillRunning { get; init; }
}

public static class PcVerify
{
    /// <summary>The action's implicit post-condition.</summary>
    public static PcVerdict Implicit(PcCheck check)
    {
        var command = check.Command;
        var target = check.Target;
        if (command?.Action == PcElementAction.Close || (check.Kind == PcInputKind.Key && check.WindowClosed))
            return check.WindowClosed ? PcVerdict.Pass : PcVerdict.Mismatch("the window is still open");
        if (check.WindowClosed)
        {
            // A button that closes its dialog: the closing is the change. Anything else should have kept its window.
            return command?.Action is PcElementAction.Invoke || command is null && check.Kind is PcInputKind.Click or PcInputKind.Key
                ? PcVerdict.Pass
                : PcVerdict.Mismatch("the window closed");
        }
        if (check.Typed is { } typed && target is not null)
            return Typed(target, check.TargetAfter, typed, check.Insert);
        if (command is null || target is null) return Attributed(check);
        var after = check.TargetAfter;
        var what = Describe(target);
        switch (command.Action)
        {
            case PcElementAction.Invoke:
                return Attributed(check);
            case PcElementAction.Toggle:
                if (after is null) return PcVerdict.Mismatch(what + " is gone");
                return ToggleState(after) != ToggleState(target) ? PcVerdict.Pass : PcVerdict.Mismatch(what + " is still " + ToggleState(after));
            case PcElementAction.Select:
                if (after is null) return PcVerdict.Mismatch(what + " is gone");
                return after.Has(PcStates.Selected) ? PcVerdict.Pass : PcVerdict.Mismatch(what + " is not selected");
            case PcElementAction.Expand:
                if (check.NewWindows.Count > 0) return PcVerdict.Pass;
                if (after is null) return PcVerdict.Mismatch(what + " is gone");
                return after.Has(PcStates.Expanded) || (!after.Has(PcStates.Collapsed) && SubtreeChanged(check)) ? PcVerdict.Pass : PcVerdict.Mismatch(what + " is still collapsed");
            case PcElementAction.Collapse:
                if (after is null) return PcVerdict.Mismatch(what + " is gone");
                return after.Has(PcStates.Collapsed) || !after.Has(PcStates.Expanded) ? PcVerdict.Pass : PcVerdict.Mismatch(what + " is still expanded");
            case PcElementAction.SetValue:
                if (after is null) return PcVerdict.Mismatch(what + " is gone");
                return SameValue(command.Value ?? "", after.Value, target.Can(PcActions.RangeValue) && !target.Can(PcActions.Value))
                    ? PcVerdict.Pass
                    : PcVerdict.Mismatch(what + " shows " + Shown(after.Value) + ", not " + Shown(command.Value));
            case PcElementAction.Scroll:
                if (after is null) return PcVerdict.Mismatch(what + " is gone");
                if (target.ScrollPercent is { } was && after.ScrollPercent is { } now)
                {
                    if (Math.Abs(now - was) > 0.001) return PcVerdict.Pass;
                    var atEnd = command.Direction is PcScrollDirection.Down or PcScrollDirection.Right ? was >= 99.999 : was <= 0.001;
                    return atEnd ? PcVerdict.Pass : PcVerdict.Mismatch(what + " did not move (still at " + was.ToString("0.#", CultureInfo.InvariantCulture) + "%)");
                }
                return SubtreeChanged(check) ? PcVerdict.Pass : PcVerdict.Unverified(what + " shows no scroll position and no new items appeared");
            case PcElementAction.ScrollIntoView:
                if (after is null) return PcVerdict.Mismatch(what + " is gone");
                return !after.Has(PcStates.Offscreen) ? PcVerdict.Pass : PcVerdict.Mismatch(what + " is still off screen");
            case PcElementAction.Focus:
                if (after is null) return PcVerdict.Mismatch(what + " is gone");
                return after.Has(PcStates.Focused) ? PcVerdict.Pass : PcVerdict.Mismatch(what + " does not have the app's focus");
            case PcElementAction.Minimize:
                return check.Minimized ? PcVerdict.Pass : PcVerdict.Mismatch("the window is not minimized");
            case PcElementAction.Restore:
                return !check.Minimized ? PcVerdict.Pass : PcVerdict.Mismatch("the window is still minimized");
            default:
                return Attributed(check);
        }
    }

    /// <summary>The model's own expect, on the fresh tree (DESIGN.md 2.12).</summary>
    public static PcVerdict Expected(PcExpect expect, PcCheck check)
    {
        var tree = check.After;
        var after = check.TargetAfter;
        var what = check.Target is { } t ? Describe(t) : "the element";
        if (expect.WindowOpens is { } opens)
        {
            if (!check.NewWindows.Any(w => Fits(w, opens)))
                return PcVerdict.Mismatch(check.NewWindows.Count == 0 ? "no window opened" : "the window that opened is " + string.Join(", ", check.NewWindows.Select(w => Quote(w.Title))));
        }
        if (tree is null)
        {
            if (expect.Gone is not null && expect.Value is null && expect.Name is null && expect.Appears is null && expect.TextInWindow is null
                && expect.Selected is null && expect.Checked is null && expect.FocusedInApp is null)
                return PcVerdict.Pass;
            if (expect.WindowOpens is not null && expect.Value is null && expect.Name is null && expect.Appears is null && expect.TextInWindow is null
                && expect.Selected is null && expect.Checked is null && expect.FocusedInApp is null && expect.Gone is null)
                return PcVerdict.Pass;
            return PcVerdict.Mismatch("the window closed");
        }
        if (expect.Value is { } value && !SameValue(value, after?.Value, false))
            return PcVerdict.Mismatch(after is null ? what + " is gone" : what + " shows " + Shown(after.Value) + ", not " + Shown(value));
        if (expect.Name is { } name && !string.Equals(Norm(after?.Name), Norm(name), StringComparison.OrdinalIgnoreCase))
            return PcVerdict.Mismatch(after is null ? what + " is gone" : what + " is named " + Shown(after.Name) + ", not " + Shown(name));
        if (expect.Selected is { } selected && (after?.Has(PcStates.Selected) ?? false) != selected)
            return PcVerdict.Mismatch(what + (selected ? " is not selected" : " is still selected"));
        if (expect.Checked is { } isChecked && (after?.Has(PcStates.Checked) ?? false) != isChecked)
            return PcVerdict.Mismatch(what + (isChecked ? " is not checked" : " is still checked"));
        if (expect.FocusedInApp is { } focused && (after?.Has(PcStates.Focused) ?? false) != focused)
            return PcVerdict.Mismatch(what + (focused ? " does not have the app's focus" : " still has the app's focus"));
        if (expect.Appears is { } appears && !tree.Elements.Values.Any(n => Holds(n.Name, appears)))
            return PcVerdict.Mismatch("nothing named " + Shown(appears) + " is in the window");
        if (expect.Gone is { } gone && Contains(tree, gone))
            return PcVerdict.Mismatch(Shown(gone) + " is still in the window");
        if (expect.TextInWindow is { } text && !Contains(tree, text))
            return PcVerdict.Mismatch("no " + Shown(text) + " in the window");
        return PcVerdict.Pass;
    }

    /// <summary>Whether any element's name or value holds the text (case-insensitive, NFC).</summary>
    public static bool Contains(PcTree tree, string text) => tree.Elements.Values.Any(n => Holds(n.Name, text) || Holds(n.Value, text));

    /// <summary>Whether a window matches a window_opens expectation: title contains (case-insensitive) and class equals.</summary>
    public static bool Fits(PcWindowInfo window, PcWindowMatch match) =>
        (match.Title is null || Holds(window.Title, match.Title)) && (match.Class is null || string.Equals(window.ClassName, match.Class, StringComparison.OrdinalIgnoreCase));

    /// <summary>Typed text, read back (DESIGN.md 2.8, critique C31): replace must equal; insert must now be in the value.</summary>
    public static PcVerdict Typed(PcUiNode before, PcUiNode? after, string text, bool insert)
    {
        if (after is null) return PcVerdict.Mismatch(Describe(before) + " is gone");
        if (before.Has(PcStates.Password)) return PcVerdict.Pass;
        if (!before.Can(PcActions.Value) && !before.Can(PcActions.Text) && after.Value is null)
            return PcVerdict.Unverified(Describe(before) + " does not show its text, so the typing could not be read back");
        var value = Text(after.Value);
        var wanted = Text(text);
        var ok = insert ? value.Contains(wanted, StringComparison.Ordinal) : string.Equals(value, wanted, StringComparison.Ordinal);
        return ok ? PcVerdict.Pass : PcVerdict.Mismatch(Describe(before) + " shows " + Shown(after.Value) + (insert ? " and does not hold " : ", not ") + Shown(text));
    }

    /// <summary>
    /// The Invoke check (critique C26): a change tied to the target. Pass for a change in the target, its subtree or its
    /// parent's state, a new window of the process, a title change or a focus change; unverified when only unrelated parts
    /// changed; a mismatch when nothing changed.
    /// </summary>
    public static PcVerdict Attributed(PcCheck check)
    {
        if (check.NewWindows.Count > 0) return PcVerdict.Pass;
        if (!string.Equals(check.TitleBefore, check.TitleAfter, StringComparison.Ordinal) && check.TitleBefore is not null && check.TitleAfter is not null) return PcVerdict.Pass;
        if (check.Before is not { } before || check.After is not { } after)
            return check.StillRunning ? PcVerdict.Unverified("the app has not finished the action") : PcVerdict.Unverified("Tactus had no earlier look at the window to compare with");
        var changed = Changed(before, after);
        var noise = check.Noise;
        var real = changed.Where(id => !noise.Contains(id)).ToList();
        var focusBefore = before.Focused is { } fb ? before.Elements[fb].RuntimeId : null;
        var focusAfter = after.Focused is { } fa ? after.Elements[fa].RuntimeId : null;
        // A focus move counts unless the user's own focus is on either end of it.
        if (!string.Equals(focusBefore, focusAfter, StringComparison.Ordinal) && !(focusBefore is not null && noise.Contains(focusBefore))
            && !(focusAfter is not null && noise.Contains(focusAfter)))
            return PcVerdict.Pass;
        if (check.Target is { } target)
        {
            var related = Related(before, target.RuntimeId);
            related.UnionWith(Related(after, target.RuntimeId));
            if (real.Any(related.Contains)) return PcVerdict.Pass;
            if (Parent(before, target.RuntimeId) is { } parent && StateChanged(before, after, parent)) return PcVerdict.Pass;
        }
        else if (real.Count > 0)
        {
            // A point click or a posted key has no element to tie the change to: any change that is not noise counts.
            return PcVerdict.Pass;
        }
        if (check.StillRunning) return PcVerdict.Unverified("the app has not finished the action and nothing tied to it changed yet");
        if (changed.Count > 0) return PcVerdict.Unverified("only unrelated parts of the window changed");
        return PcVerdict.Mismatch("nothing changed");
    }

    /// <summary>The RuntimeIds whose element appeared, went or changed (role, name, value, states other than focus).</summary>
    public static HashSet<string> Changed(PcTree before, PcTree after)
    {
        var old = Signatures(before);
        var now = Signatures(after);
        var changed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (id, signature) in old)
        {
            if (!now.TryGetValue(id, out var current) || current != signature) changed.Add(id);
        }
        foreach (var id in now.Keys)
        {
            if (!old.ContainsKey(id)) changed.Add(id);
        }
        return changed;
    }

    private static bool SubtreeChanged(PcCheck check) =>
        check.Before is { } before && check.After is { } after && check.Target is { } target
        && Changed(before, after).Where(id => !check.Noise.Contains(id)).Any(Related(before, target.RuntimeId).Union(Related(after, target.RuntimeId)).Contains);

    // The target and every element under it.
    private static HashSet<string> Related(PcTree tree, string runtimeId)
    {
        var related = new HashSet<string>(StringComparer.Ordinal) { runtimeId };
        if (tree.IndexOf(runtimeId) is not { } index) return related;
        var under = new HashSet<int> { index };
        foreach (var line in tree.Lines)
        {
            if (under.Contains(line.ParentIndex))
            {
                under.Add(line.Index);
                related.Add(line.RuntimeId);
            }
        }
        return related;
    }

    private static string? Parent(PcTree tree, string runtimeId)
    {
        if (tree.IndexOf(runtimeId) is not { } index) return null;
        var line = tree.Lines.First(l => l.Index == index);
        return line.ParentIndex > 1 ? tree.Elements[line.ParentIndex].RuntimeId : null;
    }

    private static bool StateChanged(PcTree before, PcTree after, string runtimeId)
    {
        if (before.IndexOf(runtimeId) is not { } a || after.IndexOf(runtimeId) is not { } b) return false;
        var x = before.Elements[a];
        var y = after.Elements[b];
        const PcStates watched = PcStates.Expanded | PcStates.Collapsed | PcStates.Selected | PcStates.Checked;
        return (x.States & watched) != (y.States & watched) || !string.Equals(x.Value, y.Value, StringComparison.Ordinal);
    }

    private static Dictionary<string, string> Signatures(PcTree tree)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in tree.Lines)
        {
            if (line.RuntimeId.Length == 0 || line.RuntimeId.Contains('#', StringComparison.Ordinal)) continue;
            var node = tree.Elements[line.Index];
            map[line.RuntimeId] = node.Role + "\u0001" + node.Name + "\u0001" + node.Value + "\u0001" + ((int)(node.States & ~PcStates.Focused)).ToString(CultureInfo.InvariantCulture);
        }
        return map;
    }

    private static bool SameValue(string wanted, string? shown, bool numeric)
    {
        if (shown is null) return wanted.Length == 0;
        if (string.Equals(Text(shown), Text(wanted), StringComparison.Ordinal)) return true;
        return (numeric || double.TryParse(wanted, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            && double.TryParse(wanted, NumberStyles.Float, CultureInfo.InvariantCulture, out var a)
            && double.TryParse(shown, NumberStyles.Float, CultureInfo.InvariantCulture, out var b)
            && Math.Abs(a - b) < 1e-9;
    }

    private static bool Holds(string? text, string part) =>
        text is not null && Norm(text).Contains(Norm(part), StringComparison.OrdinalIgnoreCase);

    private static string Norm(string? text) => (text ?? "").Normalize(NormalizationForm.FormC).Trim();

    /// <summary>Text compared after NFC and line-ending normalisation (\r\n, \r and \n are the same).</summary>
    public static string Text(string? text) =>
        (text ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Normalize(NormalizationForm.FormC);

    private static string ToggleState(PcUiNode node) => node.Has(PcStates.Mixed) ? "mixed" : node.Has(PcStates.Checked) ? "on" : "off";

    private static string Shown(string? text) => text is null ? "nothing" : PcWindowLine.Quote(TreeFormatter.Clean(text, 80));

    private static string Quote(string text) => PcWindowLine.Quote(TreeFormatter.Clean(text, 80));

    /// <summary>"button "Save"" for messages.</summary>
    public static string Describe(PcUiNode node) =>
        (string.IsNullOrWhiteSpace(node.Role) ? "element" : node.Role.Trim()) + (string.IsNullOrWhiteSpace(node.Name) ? "" : " " + PcWindowLine.Quote(TreeFormatter.Clean(node.Name, 60)));
}
