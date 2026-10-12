using Marrow.Services.Port.PcControl.Workspace;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus's separate workspace, what the user does to set it up (unit P18, ED-NEW-pcws-2, ED-NEW-pcws-6,
// ED-NEW-pcws-9; DESIGN.md 2.13). nth never changes the child-session setting by itself: only the user's own enable and
// disable commands write it, and only from a terminal they opened as administrator. Also the pipe and token shapes the
// agent accepts, and the browser profile of the workspace.
[Trait("Decision", "ED-NEW-pcws-2")]
public sealed class WorkspaceSetupTests
{
    [Fact]
    public void The_setting_commands_change_nothing_without_an_administrator_terminal()
    {
        var sessions = new FakeChildSessions();
        var output = new StringWriter();
        Assert.Equal(WorkspaceSetup.ExitNotElevated, WorkspaceSetup.Run([WorkspaceSetup.EnableCommand], sessions, () => false, output));
        Assert.Equal(WorkspaceSetup.ExitNotElevated, WorkspaceSetup.Run([WorkspaceSetup.DisableCommand], sessions, () => false, output));
        Assert.Empty(sessions.Writes);
        Assert.Contains("Nothing was changed.", output.ToString(), StringComparison.Ordinal);
        Assert.True(WorkspaceSetup.NeedsAdmin);
    }

    [Fact]
    public void Elevated_the_user_turns_child_sessions_on_and_off_and_a_refusal_says_the_error()
    {
        var sessions = new FakeChildSessions();
        var output = new StringWriter();
        Assert.Equal(WorkspaceSetup.ExitDone, WorkspaceSetup.Run([WorkspaceSetup.EnableCommand], sessions, () => true, output));
        Assert.Equal(WorkspaceSetup.ExitDone, WorkspaceSetup.Run([WorkspaceSetup.DisableCommand], sessions, () => true, output));
        Assert.Equal(new[] { true, false }, sessions.Writes);
        Assert.Contains("Child sessions are on.", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("Child sessions are off.", output.ToString(), StringComparison.Ordinal);
        sessions.FailWith = 5;
        Assert.Equal(WorkspaceSetup.ExitFailed, WorkspaceSetup.Run([WorkspaceSetup.EnableCommand], sessions, () => true, output));
        Assert.Contains("(error 5)", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Anything_else_is_a_usage_error_that_writes_nothing()
    {
        var sessions = new FakeChildSessions();
        var output = new StringWriter();
        Assert.Equal(WorkspaceSetup.ExitUsage, WorkspaceSetup.Run([WorkspaceSetup.EnableCommand, "--force"], sessions, () => true, output));
        Assert.Equal(WorkspaceSetup.ExitUsage, WorkspaceSetup.Run([WorkspaceSetup.AgentCommand], sessions, () => true, output));
        Assert.Equal(WorkspaceSetup.ExitUsage, WorkspaceSetup.Run([], sessions, () => true, output));
        Assert.Empty(sessions.Writes);
        Assert.False(WorkspaceSetup.IsSettingCommand([WorkspaceSetup.AgentCommand]));
    }

    [Fact]
    public void The_panel_shows_the_exact_command_the_undo_and_the_four_limits()
    {
        var exe = "C:\\Users\\Me\\AppData\\Local\\nth\\current\\nth.exe";
        Assert.Equal("& \"" + exe + "\" --tactus-workspace-enable | Out-Host", WorkspaceSetup.Command(exe, WorkspaceSetup.EnableCommand));
        Assert.Equal("& \"" + exe + "\" --tactus-workspace-disable | Out-Host", WorkspaceSetup.Command(exe + "\"", WorkspaceSetup.DisableCommand));
        Assert.Equal(4, WorkspaceSetup.Limits.Count);
        Assert.Contains("administrator", WorkspaceSetup.OpenTerminal, StringComparison.Ordinal);
        Assert.Contains("3389", WorkspaceSetup.RemoteDesktopNote, StringComparison.Ordinal);
        Assert.Equal("ms-settings:remotedesktop", WorkspaceSetup.RemoteDesktopSettings);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-pcws-5")]
    public void The_agent_takes_only_a_fresh_pipe_name_and_token_of_the_exact_shape()
    {
        var name = WorkspacePipe.NewName();
        var token = WorkspacePipe.NewToken();
        Assert.True(WorkspacePipe.IsName(name));
        Assert.True(WorkspacePipe.IsToken(token));
        Assert.NotEqual(token, WorkspacePipe.NewToken());
        Assert.False(WorkspacePipe.IsName("nth-workspace-ABC"));
        Assert.False(WorkspacePipe.IsName("other-" + name));
        Assert.False(WorkspacePipe.IsToken(token.ToUpperInvariant() + "0"));
        Assert.False(WorkspacePipe.IsToken(null));
        Assert.Equal("\"C:\\nth\\nth.exe\" --tactus-workspace-agent " + name + " " + token, WorkspacePipe.StartProgram("C:\\nth\\nth.exe", name, token));
        Assert.Throws<ArgumentException>(() => WorkspacePipe.StartProgram("nth.exe", name, token));
        Assert.Throws<ArgumentException>(() => WorkspacePipe.StartProgram("C:\\nth\\nth.exe", name, "short"));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-pcws-9")]
    public void Browsers_in_the_workspace_start_with_tactuss_own_profile_and_other_apps_with_no_arguments()
    {
        Assert.Equal("--user-data-dir=\"C:\\Data\\Edge\"", WorkspaceBrowserProfile.Arguments("C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe", "C:\\Data"));
        Assert.Equal("--user-data-dir=\"C:\\Data\\Edge\"", WorkspaceBrowserProfile.Arguments("MSEdge", "C:\\Data"));
        Assert.Equal("--user-data-dir=\"C:\\Data\\Chrome\"", WorkspaceBrowserProfile.Arguments("C:\\Apps\\CHROME.EXE", "C:\\Data"));
        Assert.Null(WorkspaceBrowserProfile.Arguments("C:\\Apps\\notepad.exe", "C:\\Data"));
        Assert.Null(WorkspaceBrowserProfile.Arguments("C:\\Apps\\chrome.exe", "C:\\Da\"ta"));
        Assert.Null(WorkspaceBrowserProfile.Arguments("C:\\Apps\\chrome.exe", "relative"));
        var inner = new RecordingLauncher();
        var launcher = new WorkspaceLauncher(inner, "C:\\Data");
        launcher.Launch("C:\\Apps\\chrome.exe");
        launcher.Launch("C:\\Apps\\notepad.exe");
        Assert.Equal(new[] { ("C:\\Apps\\chrome.exe", (string?)"--user-data-dir=\"C:\\Data\\Chrome\""), ("C:\\Apps\\notepad.exe", null) }, inner.Started);
    }
}
