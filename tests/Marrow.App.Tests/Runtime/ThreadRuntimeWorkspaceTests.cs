using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Runtime;

// PORT-ONLY: web surfaces W4 (DESIGN.md 3.1, 3.7, 3.11 "Pane state moves off ThreadRuntime flags (IsTerminalVisible, IsDiffVisible)
// onto PaneWorkspaceState"; ED-NEW-web-w4-1). The terminal's open state is the workspace's: IsTerminalVisible is a view of it
// (the drawer or a grid cell), it reopens where the terminal was last, and a move between the two asks for the keyboard. Changes
// is a pane (ToggleChanges); the old IsDiffVisible flag stays the Alt-click popover's.
[Trait("Decision", "ED-NEW-web-w4-1")]
public sealed class ThreadRuntimeWorkspaceTests
{
    private static void WithRuntime(Action<ThreadRuntime> body)
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Workspace", h.NewFolder("workspace"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        AppModelHarness.WriteDocument(thread.Id);
        var app = h.MakeModel();
        h.Main.OnMain(() => body(new ThreadRuntime(thread.Id, app)));
    }

    [Fact]
    public void The_terminal_flag_opens_and_closes_the_terminal_in_the_workspace_and_asks_for_the_keyboard_on_opening()
    {
        WithRuntime(runtime =>
        {
            Assert.False(runtime.IsTerminalVisible);
            Assert.False(runtime.TerminalWantsFocus);
            runtime.IsTerminalVisible = true;
            Assert.True(runtime.IsTerminalVisible);
            Assert.True(runtime.IsTerminalInDrawer);
            Assert.False(runtime.IsTerminalInGrid);
            Assert.True(runtime.Workspace.IsOpen(PaneKind.Terminal));
            Assert.True(runtime.TerminalWantsFocus); // ThreadRuntime.swift:241-243

            runtime.TerminalWantsFocus = false;
            runtime.IsTerminalVisible = true; // already visible: nothing asked again
            Assert.False(runtime.TerminalWantsFocus);

            runtime.IsTerminalVisible = false;
            Assert.False(runtime.IsTerminalVisible);
            Assert.False(runtime.IsTerminalInDrawer);
            Assert.False(runtime.Workspace.IsOpen(PaneKind.Terminal));
        });
    }

    [Fact]
    public void A_terminal_moved_to_the_grid_is_still_visible_but_no_longer_the_drawer_and_reopens_where_it_was_left()
    {
        WithRuntime(runtime =>
        {
            runtime.IsTerminalVisible = true;
            runtime.TerminalWantsFocus = false;
            runtime.Workspace = runtime.Workspace.MoveTerminalToGrid();
            Assert.True(runtime.IsTerminalVisible);
            Assert.False(runtime.IsTerminalInDrawer);
            Assert.True(runtime.IsTerminalInGrid);
            Assert.True(runtime.TerminalWantsFocus, "the moved terminal's new host takes the keyboard the old one had");

            runtime.IsTerminalVisible = false;
            Assert.False(runtime.IsTerminalVisible);
            Assert.False(runtime.IsTerminalInGrid);
            runtime.TerminalWantsFocus = false;
            runtime.IsTerminalVisible = true;
            Assert.True(runtime.IsTerminalInGrid, "the terminal was last in the grid, so it opens there");
            Assert.False(runtime.IsTerminalInDrawer);
            Assert.True(runtime.TerminalWantsFocus);

            runtime.Workspace = runtime.Workspace.MoveTerminalToDrawer();
            Assert.True(runtime.IsTerminalInDrawer);
            Assert.False(runtime.IsTerminalInGrid);
        });
    }

    [Fact]
    public void Moving_a_closed_terminal_or_changing_other_panes_asks_for_no_keyboard()
    {
        WithRuntime(runtime =>
        {
            runtime.Workspace = runtime.Workspace.Open(PaneKind.Browser);
            runtime.Workspace = runtime.Workspace.Open(PaneKind.BackgroundTasks);
            Assert.False(runtime.TerminalWantsFocus);
            runtime.IsTerminalVisible = true;
            runtime.TerminalWantsFocus = false;
            runtime.Workspace = runtime.Workspace.Close(PaneKind.Browser);
            Assert.False(runtime.TerminalWantsFocus);
        });
    }

    [Fact]
    public void An_observer_of_one_of_the_flags_is_told_only_when_it_flips_not_for_every_write_of_the_workspace()
    {
        WithRuntime(runtime =>
        {
            var visible = 0;
            var drawer = 0;
            var grid = 0;
            var pane = 0;
            ObservationTracking.Track(() => runtime.IsTerminalVisible, () => visible++);
            ObservationTracking.Track(() => runtime.IsTerminalInDrawer, () => drawer++);
            ObservationTracking.Track(() => runtime.IsTerminalInGrid, () => grid++);
            ObservationTracking.Track(() => runtime.IsChangesPaneOpen, () => pane++);
            runtime.Workspace = runtime.Workspace.Open(PaneKind.Browser); // not about the terminal or Changes
            Assert.Equal((0, 0, 0, 0), (visible, drawer, grid, pane));

            runtime.IsTerminalVisible = true; // the drawer opens
            Assert.Equal((1, 1, 0, 0), (visible, drawer, grid, pane));

            // Each tracker fires once. Fresh ones for the flags that fired; the grid's is still armed. Moved to the grid, the terminal is
            // still visible: only its two halves flip.
            ObservationTracking.Track(() => runtime.IsTerminalVisible, () => visible++);
            ObservationTracking.Track(() => runtime.IsTerminalInDrawer, () => drawer++);
            runtime.Workspace = runtime.Workspace.MoveTerminalToGrid();
            Assert.Equal((1, 2, 1, 0), (visible, drawer, grid, pane));

            runtime.ToggleChanges();
            Assert.Equal((1, 2, 1, 1), (visible, drawer, grid, pane));
        });
    }

    [Fact]
    public void Toggle_changes_opens_and_closes_the_pane_and_clears_the_focus_and_a_popover_that_is_open_closes_first()
    {
        WithRuntime(runtime =>
        {
            var anchor = new Avalonia.Controls.Control();
            runtime.ShowDiff(anchor, Guid.NewGuid(), focusEdits: [new FileEdit("a.txt", "old", "new")]);
            Assert.True(runtime.IsDiffVisible);
            Assert.NotEmpty(runtime.DiffFocusEdits);

            runtime.ToggleChanges(); // the popover closes first, as the second press always has
            Assert.False(runtime.IsDiffVisible);
            Assert.False(runtime.IsChangesPaneOpen);
            Assert.Empty(runtime.DiffFocusEdits);

            runtime.ToggleChanges();
            Assert.True(runtime.IsChangesPaneOpen);
            Assert.False(runtime.IsDiffVisible, "Ctrl+D opens the pane, never the popover");
            Assert.True(runtime.Workspace.IsOpen(PaneKind.Changes));
            runtime.ToggleChanges();
            Assert.False(runtime.IsChangesPaneOpen);

            runtime.ToggleDiff(); // the Alt+click path is the popover's, and leaves the pane alone
            Assert.True(runtime.IsDiffVisible);
            Assert.False(runtime.IsChangesPaneOpen);
        });
    }
}
