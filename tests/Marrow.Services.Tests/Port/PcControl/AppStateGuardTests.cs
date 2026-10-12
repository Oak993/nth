using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-19; DESIGN.md 2.9 "Shared app state", "Focus inside the app";
// critique C5, C6). Each way is classed before it runs: presses, selection, tabs, scrolling, menu commands and shortcuts
// are app-global; ticking a box, picking a radio button, setting a value and plain keys are writes; expanding shifts the
// layout; focus moves and clicks into a field move focus. "Active in an app" is its windows' physical input within 2 s,
// and a pointer that moved inside the window holds layout shifts for 500 ms.
[Trait("Decision", "ED-NEW-pcctl-19")]
public sealed class AppStateGuardTests
{
    private static readonly PcWindowInfo Excel = new(0x1a, "Book1 - Excel", "XLMAIN", 7, "c:\\office\\excel.exe");

    [Fact]
    public void Presses_selection_tabs_scrolling_menus_and_shortcuts_are_app_global()
    {
        Assert.Equal(PcStepEffect.AppGlobal, Of(PcInputKind.Click, Node("button", "Save"), PcElementAction.Invoke));
        Assert.Equal(PcStepEffect.AppGlobal, Of(PcInputKind.Click, Node("tab item", "Sheet2"), PcElementAction.Select));
        Assert.Equal(PcStepEffect.AppGlobal, Of(PcInputKind.Command, Node("list item", "Draft"), PcElementAction.Select));
        Assert.Equal(PcStepEffect.AppGlobal | PcStepEffect.LayoutShift, Of(PcInputKind.Scroll, Node("pane", "Grid"), PcElementAction.Scroll));
        Assert.Equal(PcStepEffect.AppGlobal | PcStepEffect.LayoutShift, Of(PcInputKind.Command, Node("data item", "B7"), PcElementAction.ScrollIntoView));
        Assert.Equal(PcStepEffect.AppGlobal, Of(PcInputKind.Command, Node("window", "Book1"), PcElementAction.Close));
        Assert.Equal(PcStepEffect.AppGlobal, Of(PcInputKind.Click, null, menu: "Save As..."));
        Assert.Equal(PcStepEffect.AppGlobal, Of(PcInputKind.Click, null));
        Assert.Equal(PcStepEffect.AppGlobal, Of(PcInputKind.Key, null, chord: new KeyChord([KeyChordParser.Control], 'S')));
        Assert.Equal(PcStepEffect.AppGlobal, Of(PcInputKind.Key, null, chord: new KeyChord([], 0x74)));
        Assert.Equal(PcStepEffect.AppGlobal, Of(PcInputKind.Key, null, chord: new KeyChord([], KeyChordParser.Return)));
        Assert.Equal(PcStepEffect.AppGlobal | PcStepEffect.LayoutShift, Of(PcInputKind.Drag, null));
    }

    [Fact]
    public void Writes_into_controls_are_left_to_the_yield_rule()
    {
        Assert.Equal(PcStepEffect.None, Of(PcInputKind.Click, Node("check box", "Word wrap"), PcElementAction.Toggle));
        Assert.Equal(PcStepEffect.None, Of(PcInputKind.Click, Node("radio button", "Landscape"), PcElementAction.Select));
        Assert.Equal(PcStepEffect.None, Of(PcInputKind.Command, Node("edit", "Subject"), PcElementAction.SetValue));
        Assert.Equal(PcStepEffect.None, Of(PcInputKind.Type, Node("edit", "Subject")));
        Assert.Equal(PcStepEffect.None, Of(PcInputKind.Key, null, chord: new KeyChord([], 'A')));
        Assert.Equal(PcStepEffect.None, Of(PcInputKind.Key, null, chord: new KeyChord([KeyChordParser.Shift], 'A')));
    }

    [Fact]
    public void Expanding_shifts_the_layout_and_focus_moves_are_their_own_class()
    {
        Assert.Equal(PcStepEffect.LayoutShift, Of(PcInputKind.Click, Node("tree item", "Folder"), PcElementAction.Expand));
        Assert.Equal(PcStepEffect.LayoutShift, Of(PcInputKind.Command, Node("tree item", "Folder"), PcElementAction.Collapse));
        Assert.Equal(PcStepEffect.MovesFocus, Of(PcInputKind.Command, Node("edit", "Subject"), PcElementAction.Focus));
        Assert.Equal(PcStepEffect.MovesFocus, Of(PcInputKind.Click, Node("edit", "Subject")));
        Assert.Equal(PcStepEffect.MovesFocus, Of(PcInputKind.Key, null, chord: new KeyChord([], 0x09)));
        var kept = Plan(PcElementAction.Invoke) with { WaitsForStill = true };
        Assert.Equal(PcStepEffect.AppGlobal | PcStepEffect.MovesFocus,
            AppStateGuard.Classify(new PcInputRequest(PcInputKind.Click, Excel) { Element = Node("button", "Bold") }, kept));
    }

    [Fact]
    public void Active_in_an_app_is_physical_input_into_its_windows_within_two_seconds()
    {
        var clock = new PcManualClock();
        var monitor = new PhysicalInputMonitor(clock);
        monitor.Installed(true);
        monitor.Record(new PcRawInput(1000, PcRawKind.Key, true, 0x41, false, 0) { Window = 0x1a });
        var app = new nint[] { 0x1a, 0x1b };

        Assert.True(AppStateGuard.ActiveIn(monitor, app, 1000 + AppStateGuard.ActiveMs - 1));
        Assert.False(AppStateGuard.ActiveIn(monitor, app, 1000 + AppStateGuard.ActiveMs));
        Assert.False(AppStateGuard.ActiveIn(monitor, new nint[] { 0x2c }, 1500));

        // A pointer passing over one of its windows counts too.
        monitor.Record(new PcRawInput(5000, PcRawKind.Move, false, 0, false, 0) { Window = 0x1b, Moved = new PcPoint(1, 0) });
        Assert.True(AppStateGuard.ActiveIn(monitor, app, 6000));
    }

    [Fact]
    public void A_pointer_that_moved_inside_the_window_holds_layout_shifts_for_500_ms()
    {
        var state = new PcPhysicalSnapshot { Healthy = true, PointerWindow = 0x1a, LastPointerMs = 1000 };

        Assert.Equal(1000 + AppStateGuard.PointerNearMs, AppStateGuard.PointerNear(state, 0x1a, 1200));
        Assert.Null(AppStateGuard.PointerNear(state, 0x1a, 1000 + AppStateGuard.PointerNearMs));
        Assert.Null(AppStateGuard.PointerNear(state with { PointerWindow = 0x2c }, 0x1a, 1200));
    }

    private static PcUiNode Node(string role, string name) => new("1", role, name) { Bounds = new PcRect(10, 10, 80, 20) };

    private static PcRungPlan Plan(PcElementAction? action, string? menu = null) =>
        new(PcRung.R1, "way", _ => Task.FromResult(PcUiaOutcome.Ok)) { Command = action is { } a ? new PcElementCommand(a) : null, MenuText = menu };

    private static PcStepEffect Of(PcInputKind kind, PcUiNode? node, PcElementAction? action = null, string? menu = null, KeyChord? chord = null) =>
        AppStateGuard.Classify(new PcInputRequest(kind, Excel) { Element = node, Chord = chord }, Plan(action, menu));
}
