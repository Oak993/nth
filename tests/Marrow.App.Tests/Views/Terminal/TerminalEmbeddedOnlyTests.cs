using System.Diagnostics;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Terminal;
using Marrow.Core.Models;
using Xunit;

namespace Marrow.App.Tests.Views.Terminal;

// Source: DroppyCode/App/AppModel.swift:1094-1098 runScript (scripts go through terminals.run, never through the
// workspace) and the whole terminal surface (TerminalPanel.swift:1-318, TerminalStore.swift:1-176), which is in-window.
// Workspace.Platform fails closed in tests, so any attempt to hand work to an external terminal is recorded here.
[Collection("Headless")]
public sealed class TerminalEmbeddedOnlyTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-TERM-116")] // App/AppModel.swift:1094-1098; App/Views/Terminal/TerminalPanel.swift:35-43,71-75
    public void Running_a_script_and_opening_terminals_happen_in_the_drawer_and_never_launch_an_external_terminal_Native() => TerminalPanelRig.RunWithRealShell(headless, rig =>
    {
        using var workspace = new ChatWorkspaceFixture();
        var project = rig.App.Project(rig.Thread.ProjectID)!;
        TerminalSession script = null!;

        rig.Step(() => rig.App.RunScript(new ProjectScript(name: "Dev server", command: "Write-Output ('IN' + 'DRAWER'); Start-Sleep -Seconds 60"), rig.Thread.Id));

        // The script is a tab of the thread's own drawer, in the working directory, running in the embedded view.
        script = Assert.Single(rig.App.Terminals.Sessions(rig.Thread.Id));
        Assert.Equal("Dev server", script.Title);
        Assert.Equal(project.Path, script.Directory);
        Assert.Equal(script.Id, rig.App.Terminals.Selected(rig.Thread.Id)?.Id);
        Assert.True(rig.App.Runtime(rig.Thread.Id).IsTerminalVisible); // the drawer is shown for it
        var (host, _, _) = rig.Show(project.Path);
        Assert.Same(script, Assert.Single(rig.App.Terminals.Sessions(rig.Thread.Id))); // showing it opens no second shell
        Assert.Contains(LogicalQuery.SelfAndDescendants<TerminalTab>(host), tab => LogicalQuery.SelfAndDescendants<TextBlock>(tab).Any(text => text.Text == "Dev server"));
        Assert.True(UiPump.Until(() => script.View.ScrollbackText.Contains("INDRAWER", StringComparison.Ordinal), TimeSpan.FromSeconds(30)),
            "the script's output did not reach the drawer's own terminal view");

        // The shell is a pseudoconsole child of this process: it owns no window of its own (a Windows Terminal,
        // conhost or PowerShell window would).
        using var process = Process.GetProcessById(script.View.ProcessId);
        Assert.Equal(IntPtr.Zero, process.MainWindowHandle);

        // The plus button and the empty state's button do the same thing: another tab, in the drawer.
        var plus = LogicalQuery.SelfAndDescendants<Button>(host).Single(button => Avalonia.Automation.AutomationProperties.GetName(button) == "New terminal");
        rig.Step(() => plus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        Assert.Equal(2, rig.App.Terminals.Sessions(rig.Thread.Id).Count);

        // Nothing was handed to the workspace: no Windows Terminal, no console window, no Explorer.
        Assert.Empty(workspace.Launches);
        Assert.Empty(workspace.Reveals);
    });
}
