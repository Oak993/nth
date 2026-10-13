using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Panes;
using Marrow.App.Views.Terminal;
using Marrow.App.Views.Terminal.Port;
using Marrow.Core.Models;
using Marrow.UI.Panes;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Terminal;

// nth web surfaces, unit W4 (work/design/web-surfaces/DESIGN.md 3.1 "Terminal | drawer | under the chat column | stays the chat's drawer
// by default; can be dragged into the grid", 3.7, the canvas's terminal cell: the kind glyph, the shell's title, Close; 14 W4
// "terminal adapter in TerminalPanel.cs"). The terminal pane hosts the thread's shells in a workspace cell: the same sessions the
// drawer has, one long-lived view each, handed from the drawer to the cell and back without a restart. Real shells (ConPTY, no
// window), as the drawer's own tests use.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w4-1")]
public sealed class TerminalPaneTests(HeadlessSession headless)
{
    private static TerminalTab[] Tabs(Control root) => LogicalQuery.SelfAndDescendants<TerminalTab>(root).ToArray();

    private static TerminalHost? HostIn(Control root) => LogicalQuery.SelfAndDescendants<TerminalHost>(root).FirstOrDefault();

    [Fact]
    public void The_pane_has_the_terminal_glyph_the_new_terminal_and_move_to_the_drawer_actions_and_hosts_the_threads_shell() => TerminalPanelRig.Run(headless, rig =>
    {
        var directory = TerminalPanelRig.Folder("pane shell");
        var (host, content, _) = rig.ShowPane();
        var only = Assert.Single(rig.App.Terminals.Sessions(rig.Thread.Id));
        Assert.Equal("Terminal 1", only.Title);
        Assert.Equal("Terminal 1", content.Title);
        Assert.Equal(["New terminal", "Move to the drawer"], content.Actions.Select(action => action.Name).ToArray());
        Assert.Equal([PaneGlyphKind.Plus, PaneGlyphKind.DockBottom], content.Actions.Select(action => action.Glyph!.Value).ToArray());
        var shown = Assert.IsType<TerminalHost>(HostIn(host));
        Assert.Same(only.View, LogicalQuery.SelfAndDescendants<TerminalHostView>(shown).Single());
        Assert.False(Assert.IsType<TerminalPaneBody>(content.Body).Strip.IsVisible, "one shell needs no tab row");
        Assert.True(only.IsRunning);
    });

    [Fact]
    public void The_header_title_follows_the_selected_shell_and_its_own_title_changes() => TerminalPanelRig.Run(headless, rig =>
    {
        var directory = TerminalPanelRig.Folder("pane title");
        var (_, content, _) = rig.ShowPane();
        var first = Assert.Single(rig.App.Terminals.Sessions(rig.Thread.Id));
        rig.Step(() => first.Title = "npm run dev");
        Assert.Equal("npm run dev", content.Title);
        TerminalSession second = null!;
        rig.Step(() => second = rig.App.Terminals.Open(rig.Thread.Id, directory, title: "git log"));
        Assert.Equal("git log", content.Title);
        rig.Step(() => rig.App.Terminals.Selection = rig.App.Terminals.Selection.Mutating(map => map[rig.Thread.Id] = first.Id));
        Assert.Equal("npm run dev", content.Title);
        rig.Step(() => second.Title = "");
        Assert.Equal("npm run dev", content.Title);
    });

    [Fact]
    public void A_slim_tab_row_shows_with_several_shells_and_selecting_one_swaps_the_hosted_view() => TerminalPanelRig.Run(headless, rig =>
    {
        var directory = TerminalPanelRig.Folder("pane tabs");
        var (host, content, _) = rig.ShowPane();
        var body = Assert.IsType<TerminalPaneBody>(content.Body);
        var first = Assert.Single(rig.App.Terminals.Sessions(rig.Thread.Id));
        TerminalSession second = null!;
        rig.Step(() => second = rig.App.Terminals.Open(rig.Thread.Id, directory));
        Assert.True(body.Strip.IsVisible);
        Assert.Equal(TerminalPaneBody.StripHeight, body.Strip.Bounds.Height, 0.5);
        Assert.Equal(2, Tabs(host).Length);
        Assert.Same(second.View, LogicalQuery.SelfAndDescendants<TerminalHostView>(host).Single());
        rig.Step(() => rig.App.Terminals.Selection = rig.App.Terminals.Selection.Mutating(map => map[rig.Thread.Id] = first.Id));
        Assert.Same(first.View, LogicalQuery.SelfAndDescendants<TerminalHostView>(host).Single());
        Assert.True(first.IsRunning && second.IsRunning, "switching tabs ends nothing");
        rig.Step(() => rig.App.Terminals.Close(second));
        Assert.False(body.Strip.IsVisible);
    });

    [Fact]
    public void The_new_terminal_action_opens_another_shell_in_the_threads_directory_and_selects_it() => TerminalPanelRig.Run(headless, rig =>
    {
        var directory = TerminalPanelRig.Folder("pane new");
        var (_, content, _) = rig.ShowPane();
        rig.Step(() => content.Actions[0].Invoke());
        var sessions = rig.App.Terminals.Sessions(rig.Thread.Id);
        Assert.Equal(2, sessions.Count);
        Assert.Equal(sessions[1].Id, rig.App.Terminals.Selected(rig.Thread.Id)?.Id);
        Assert.Equal(sessions[0].Directory, sessions[1].Directory);
        Assert.All(sessions, session => Assert.True(session.IsRunning));
    });

    [Fact]
    public void The_move_to_the_drawer_action_writes_the_workspace() => TerminalPanelRig.Run(headless, rig =>
    {
        var (_, content, _) = rig.ShowPane();
        rig.Step(() => rig.Runtime.Workspace = rig.Runtime.Workspace.MoveTerminalToGrid());
        Assert.True(rig.Runtime.IsTerminalInGrid);
        rig.Step(() => content.Actions[1].Invoke());
        Assert.True(rig.Runtime.IsTerminalInDrawer);
        Assert.False(rig.Runtime.IsTerminalInGrid);
        Assert.False(rig.Runtime.Workspace.TerminalPrefersGrid);
    });

    [Fact]
    public void With_no_shell_left_the_pane_shows_the_empty_state_with_its_new_shell_button() => TerminalPanelRig.Run(headless, rig =>
    {
        var (host, content, _) = rig.ShowPane();
        var only = Assert.Single(rig.App.Terminals.Sessions(rig.Thread.Id));
        rig.Step(() => rig.App.Terminals.Close(only));
        Assert.Contains(LogicalQuery.SelfAndDescendants<TextBlock>(host), block => block.Text == "No shell open in this thread");
        Assert.Contains(LogicalQuery.SelfAndDescendants<TextBlock>(host), block => block.Text == "New shell");
        Assert.Null(HostIn(host));
        Assert.Equal("Terminal", content.Title);
    });

    [Fact]
    public void The_shell_is_handed_between_the_drawer_and_the_pane_without_a_restart() => TerminalPanelRig.Run(headless, rig =>
    {
        var directory = TerminalPanelRig.Folder("hand over");
        var (_, drawer, _) = rig.Show(directory);
        var session = Assert.Single(rig.App.Terminals.Sessions(rig.Thread.Id));
        Assert.Contains(session.View, LogicalQuery.SelfAndDescendants<TerminalHostView>(drawer));
        var process = session.View.ProcessId;
        Assert.True(session.IsRunning);

        var (paneHost, _, _) = rig.ShowPane();
        Assert.Same(session.View, LogicalQuery.SelfAndDescendants<TerminalHostView>(paneHost).Single());
        Assert.DoesNotContain(session.View, LogicalQuery.SelfAndDescendants<TerminalHostView>(drawer));
        Assert.Equal(process, session.View.ProcessId);
        Assert.True(session.IsRunning);
        Assert.Single(rig.App.Terminals.Sessions(rig.Thread.Id));

        var (_, backDrawer, _) = rig.Show(directory);
        Assert.Same(session.View, LogicalQuery.SelfAndDescendants<TerminalHostView>(backDrawer).Single());
        Assert.Equal(process, session.View.ProcessId);
        Assert.True(session.IsRunning);
        Assert.Single(rig.App.Terminals.Sessions(rig.Thread.Id));
    });

    [Fact]
    public void Escape_at_the_shell_prompt_reaches_the_shell_in_a_pane_where_the_drawer_would_close_Native() => TerminalPanelRig.RunWithRealShell(headless, rig =>
    {
        // A shell that waits for one key and prints its code: 27 is Escape. In the drawer a bare Escape at the prompt is the drawer's
        // (TerminalPanelHighRiskTests); in a pane there is no "close at the prompt", so the byte goes to the shell.
        var command = "Write-Output 'KEYWAIT'; $k = [Console]::ReadKey($true); Write-Output ('KEY=' + [int]$k.KeyChar); Start-Sleep -Seconds 5";
        TerminalSession session = null!;
        AvaloniaWindow window = null!;
        rig.Step(() =>
        {
            session = new TerminalSession(Guid.NewGuid(), TerminalPanelRig.Folder("pane escape"), "PowerShell", command);
            var root = new Border { Child = new TerminalHost(session, rig.Runtime, onEscapeAtPrompt: null) };
            AppEnvironment.SetAppModel(root, rig.App);
            window = new AvaloniaWindow { Content = root, Width = 640, Height = 240 };
            window.Show();
            TerminalPanelRig.Settle(window);
        });
        try
        {
            Assert.True(UiPump.Until(() => session.View.ScrollbackText.Contains("KEYWAIT", StringComparison.Ordinal), TimeSpan.FromSeconds(15)), "the shell did not wait for a key");
            Assert.True(UiPump.Until(() => TerminalSession.ForegroundJobIsTheShell(session.View.ProcessId), TimeSpan.FromSeconds(15)), "the shell did not own the foreground");
            Assert.True(session.View.Focus());
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.True(UiPump.Until(() => session.View.ScrollbackText.Contains("KEY=27", StringComparison.Ordinal), TimeSpan.FromSeconds(15)), "Escape did not reach the shell");
        }
        finally
        {
            rig.Step(() =>
            {
                window.Close();
                session.Terminate();
            });
        }
    });

    [Fact]
    public void The_drawer_has_a_move_to_the_panes_button_only_inside_a_workspace_and_it_moves_the_terminal() => TerminalPanelRig.Run(headless, rig =>
    {
        var directory = TerminalPanelRig.Folder("dock button");
        var (_, alone, _) = rig.Show(directory);
        var hidden = LogicalQuery.SelfAndDescendants<Button>(alone).Single(button => Avalonia.Automation.AutomationProperties.GetName(button) == "Move to the panes");
        Assert.False(hidden.IsVisible, "a drawer outside a pane workspace has nowhere to go");

        var (workspace, panel) = rig.ShowInWorkspace(directory);
        var button = LogicalQuery.SelfAndDescendants<Button>(panel).Single(button => Avalonia.Automation.AutomationProperties.GetName(button) == "Move to the panes");
        Assert.True(button.IsVisible);
        Assert.Equal("Move to the panes", Avalonia.Controls.ToolTip.GetTip(button));
        rig.Step(() => rig.Runtime.IsTerminalVisible = true);
        rig.Step(() => button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)));
        Assert.True(rig.Runtime.IsTerminalInGrid);
        Assert.False(rig.Runtime.IsTerminalInDrawer);
        Assert.NotNull(workspace);
    });
}
