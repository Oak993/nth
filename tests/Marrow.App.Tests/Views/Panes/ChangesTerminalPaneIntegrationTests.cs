using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views;
using Marrow.App.Views.Diff;
using Marrow.App.Views.Panes;
using Marrow.App.Views.Terminal;
using Marrow.App.Views.Terminal.Port;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Panes;

// nth web surfaces, unit W4 in the real detail view (work/design/web-surfaces/DESIGN.md 3.1, 3.7, 14 W4 "Ctrl+D opens the pane;
// terminal drags between drawer and grid; terminal keeps Ctrl+\ and Ctrl+D"): a real thread, its real chat column with the
// terminal drawer and the toolbar, the pane factories the app registers. Ctrl+D (the runtime's ToggleChanges, as AppCommands runs it)
// opens the Changes pane with the thread's diff in it; the toolbar button does the same and Alt+click on it opens the popover;
// the terminal's shell is handed from the chat's drawer to a grid cell and back without a restart.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w4-1")]
public sealed class ChangesTerminalPaneIntegrationTests(HeadlessSession headless)
{
    [Fact]
    public void Ctrl_D_opens_the_Changes_pane_with_the_threads_diff_in_it_and_again_closes_it() => headless.OnUiThread(() =>
    {
        using var scene = new ChangesTerminalScene();
        scene.Open();
        Assert.Empty(scene.Workspace.Cards);

        scene.H.Main.OnMain(() => scene.Runtime.ToggleChanges()); // AppCommands' Toggle changes
        scene.PumpUntil(() => scene.Workspace.CardOf(PaneKind.Changes) is not null);
        var card = Assert.IsType<PaneCard>(scene.Workspace.CardOf(PaneKind.Changes));
        var inspector = Assert.IsType<DiffInspector>(card.Content.Body);
        Assert.Equal("Changes", card.Content.Title);
        scene.PumpUntil(() => !scene.Workspace.IsMoving);
        Assert.False(scene.Runtime.IsDiffVisible);
        Assert.True(inspector.Bounds.Width > 100, "the pane has a cell of its own");
        Assert.Contains(PaneKind.Changes, scene.Workspace.State.GridPanes);

        scene.H.Main.OnMain(() => scene.Runtime.ToggleChanges());
        scene.PumpUntil(() => scene.Workspace.CardOf(PaneKind.Changes) is null);
        Assert.Null(scene.Workspace.CardOf(PaneKind.Changes));
        Assert.False(scene.Runtime.IsChangesPaneOpen);
    });

    [Fact]
    public void The_toolbar_button_opens_the_pane_and_Alt_click_on_it_opens_the_popover_instead() => headless.OnUiThread(() =>
    {
        using var scene = new ChangesTerminalScene();
        scene.Open();
        var button = scene.ToolbarButton("Changes" + ShortcutStore.Hint(AppShortcut.ToggleChanges));
        var centre = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), scene.Window)!.Value;

        scene.H.Main.OnMain(() =>
        {
            scene.Window.MouseDown(centre, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            scene.Window.MouseUp(centre, MouseButton.Left, RawInputModifiers.None);
        });
        scene.PumpUntil(() => scene.Runtime.IsChangesPaneOpen);
        Assert.True(scene.Runtime.IsChangesPaneOpen);
        Assert.False(scene.Runtime.IsDiffVisible);
        scene.PumpUntil(() => scene.Workspace.CardOf(PaneKind.Changes) is not null);
        Assert.NotNull(scene.Workspace.CardOf(PaneKind.Changes));

        scene.H.Main.OnMain(() => scene.Runtime.ToggleChanges());
        scene.PumpUntil(() => !scene.Runtime.IsChangesPaneOpen);

        button = scene.ToolbarButton("Changes" + ShortcutStore.Hint(AppShortcut.ToggleChanges));
        centre = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), scene.Window)!.Value;
        scene.H.Main.OnMain(() =>
        {
            scene.Window.MouseDown(centre, MouseButton.Left, RawInputModifiers.LeftMouseButton | RawInputModifiers.Alt);
            scene.Window.MouseUp(centre, MouseButton.Left, RawInputModifiers.Alt);
        });
        scene.Pump(3);
        Assert.True(scene.Runtime.IsDiffVisible, "Alt+click is the Mac's popover");
        Assert.False(scene.Runtime.IsChangesPaneOpen);
        Assert.Contains(scene.Recorder.Calls, call => call.Presented); // and the popover was asked to show
    });

    [Fact]
    public void The_terminal_shell_goes_from_the_drawer_to_a_grid_cell_and_back_without_a_restart() => headless.OnUiThread(() =>
    {
        using var scene = new ChangesTerminalScene();
        scene.Open();
        scene.H.Main.OnMain(() => scene.Runtime.IsTerminalVisible = true);
        scene.PumpUntil(() => scene.All<TerminalPanel>().Any());
        var drawer = Assert.Single(scene.All<TerminalPanel>());
        var session = Assert.Single(scene.Model.Terminals.Sessions(scene.ThreadID));
        Assert.Null(scene.Workspace.CardOf(PaneKind.Terminal));
        Assert.Contains(session.View, scene.All<TerminalHostView>());
        var process = session.View.ProcessId;

        // The drawer's button: to the grid.
        var moveToGrid = drawer.GetVisualDescendants().OfType<Button>().Single(button => Avalonia.Automation.AutomationProperties.GetName(button) == "Move to the panes");
        Assert.True(moveToGrid.IsVisible && moveToGrid.IsEnabled);
        scene.H.Main.OnMain(() => moveToGrid.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)));
        Assert.True(scene.PumpUntil(() => scene.Workspace.CardOf(PaneKind.Terminal) is not null && !scene.All<TerminalPanel>().Any(), 400));
        var card = Assert.IsType<PaneCard>(scene.Workspace.CardOf(PaneKind.Terminal));
        Assert.IsType<TerminalPaneBody>(card.Content.Body);
        Assert.Contains(session.View, card.GetVisualDescendants().OfType<TerminalHostView>());
        Assert.True(session.IsRunning);
        Assert.Equal(process, session.View.ProcessId);
        Assert.Single(scene.Model.Terminals.Sessions(scene.ThreadID));
        Assert.True(scene.Runtime.IsTerminalVisible && scene.Runtime.IsTerminalInGrid && !scene.Runtime.IsTerminalInDrawer);

        // The pane's action: back to the drawer.
        scene.PumpUntil(() => !scene.Workspace.IsMoving);
        scene.H.Main.OnMain(() => card.Content.Actions[1].Invoke());
        Assert.True(scene.PumpUntil(() => scene.Workspace.CardOf(PaneKind.Terminal) is null && scene.All<TerminalPanel>().Any(), 400));
        var back = Assert.Single(scene.All<TerminalPanel>());
        Assert.Contains(session.View, back.GetVisualDescendants().OfType<TerminalHostView>());
        Assert.True(session.IsRunning);
        Assert.Equal(process, session.View.ProcessId);
        Assert.Single(scene.Model.Terminals.Sessions(scene.ThreadID));
    });
}
