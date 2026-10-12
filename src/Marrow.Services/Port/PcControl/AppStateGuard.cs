// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-19; DESIGN.md 2.9 "Shared app state", "Focus inside the app";
// critique C5, C6). Protecting the control the user types in is not enough: in an app the user works in, Tactus could
// still switch the tab, change the selection, scroll the page they read, open a dialog that takes their next Enter, or
// shift the layout under their pointer. Each way is classed before it runs. App-global ways (selection, the active tab
// or pane, scrolling, anything that may open or close a window: an Invoke, a menu command, a shortcut) wait for a still
// moment while the user is active in the app, exactly like a borrow; a way that moves focus waits while the user is active
// in that window; a way that can shift the layout waits while the user's pointer moved inside the window within 500 ms.
// Writes into controls that are not the user's (a check box, a field's value) run at once.
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl;

/// <summary>What a way can do to the app's shared state.</summary>
[Flags]
public enum PcStepEffect
{
    None = 0,

    /// <summary>Selection, the active tab, sheet, document or pane, scrolling, or a window that may open or close.</summary>
    AppGlobal = 1,

    /// <summary>Expand, collapse, reorder, insert or delete rows, scroll: what is under the user's pointer may move.</summary>
    LayoutShift = 2,

    /// <summary>Moves the keyboard focus inside the app.</summary>
    MovesFocus = 4,
}

public static class AppStateGuard
{
    /// <summary>"Active in an app": physical input into one of its windows within the last 2 s.</summary>
    public const int ActiveMs = 2000;

    /// <summary>A pointer that moved inside the window within 500 ms holds back steps that can shift the layout.</summary>
    public const int PointerNearMs = 500;

    private const ushort Tab = 0x09;
    private const ushort Apps = 0x5D;
    private const ushort F1 = 0x70;
    private const ushort F24 = 0x87;

    /// <summary>What the way can do to the app's shared state.</summary>
    public static PcStepEffect Classify(PcInputRequest request, PcRungPlan way)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(way);
        var effect = way.WaitsForStill ? PcStepEffect.MovesFocus : PcStepEffect.None;
        var node = way.Target ?? request.Element;
        if (way.MenuText is not null) return effect | PcStepEffect.AppGlobal;
        if (way.Command is { } command)
        {
            return effect | command.Action switch
            {
                PcElementAction.Invoke or PcElementAction.Select => FormValue(node) ? PcStepEffect.None : PcStepEffect.AppGlobal,
                PcElementAction.Toggle or PcElementAction.SetValue => PcStepEffect.None,
                PcElementAction.Expand or PcElementAction.Collapse => PcStepEffect.LayoutShift,
                PcElementAction.Scroll or PcElementAction.ScrollIntoView => PcStepEffect.AppGlobal | PcStepEffect.LayoutShift,
                PcElementAction.Focus => PcStepEffect.MovesFocus,
                _ => PcStepEffect.AppGlobal,
            };
        }
        return effect | request.Kind switch
        {
            PcInputKind.Scroll or PcInputKind.Drag => PcStepEffect.AppGlobal | PcStepEffect.LayoutShift,
            PcInputKind.Click when FormValue(node) => PcStepEffect.None,
            PcInputKind.Click when Field(node) => PcStepEffect.MovesFocus,
            PcInputKind.Click => PcStepEffect.AppGlobal,
            PcInputKind.Key when Shortcut(request.Chord) => PcStepEffect.AppGlobal,
            PcInputKind.Key when request.Chord?.Key == Tab => PcStepEffect.MovesFocus,
            PcInputKind.Command => PcStepEffect.AppGlobal,
            _ => PcStepEffect.None,
        };
    }

    /// <summary>Whether the user gave physical input into any of these windows within the last 2 s (DESIGN.md 2.9).</summary>
    public static bool ActiveIn(IPhysicalInput input, IEnumerable<nint> windows, long nowMs)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(windows);
        return windows.Any(window => input.LastIn(window) is { } at && nowMs - at < ActiveMs);
    }

    /// <summary>
    /// When the user's pointer near the window counts as still: 500 ms after it last moved inside the window. Null when it
    /// is already still there, or is over another window.
    /// </summary>
    public static long? PointerNear(PcPhysicalSnapshot state, nint window, long nowMs)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.PointerWindow == window && state.LastPointerMs is { } at && nowMs - at < PointerNearMs ? at + PointerNearMs : null;
    }

    // A chord that may run a command: Ctrl, Alt or Windows held, or Enter, Esc, the menu key or a function key.
    private static bool Shortcut(KeyChord? chord) => chord is { } c
        && (c.HasModifier(KeyChordParser.Control) || c.HasModifier(KeyChordParser.Alt) || c.HasModifier(KeyChordParser.LeftWin)
            || c.HasModifier(KeyChordParser.RightWin) || c.Key is KeyChordParser.Return or KeyChordParser.Escape or Apps || c.Key is >= F1 and <= F24);

    // A form control's own value: ticking a box or picking a radio button changes that control, not the app's view.
    private static bool FormValue(PcUiNode? node) => node?.Role is { } role
        && (role.Contains("check box", StringComparison.OrdinalIgnoreCase) || role.Contains("radio", StringComparison.OrdinalIgnoreCase));

    // A text field: a click into it moves the caret and the focus.
    private static bool Field(PcUiNode? node) => node?.Role is { } role
        && (role.Contains("edit", StringComparison.OrdinalIgnoreCase) || role.Contains("document", StringComparison.OrdinalIgnoreCase));
}
