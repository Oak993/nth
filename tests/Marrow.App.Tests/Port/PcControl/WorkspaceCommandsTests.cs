using Marrow.App.Port.PcControl.Workspace;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Port.PcControl.Workspace;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Port.PcControl;

// PORT-ONLY: Tactus, the separate workspace's command lines (unit P18, ED-NEW-pcws-2, ED-NEW-pcws-5, ED-NEW-pcws-6;
// DESIGN.md 2.13, section 8 P18). Program.Main hands the three commands over after the relays and before anything of the
// desktop app; the enable and disable commands write Windows' child-session switch only from an administrator terminal;
// the agent takes only a pipe name and token of the exact shape and serves nth over the pipe with its own engine; and in
// the source only the user's own commands can reach the call that writes the switch, and nothing starts them.
[Trait("Decision", "ED-NEW-pcws-6")]
public sealed class WorkspaceCommandsTests
{
    private const string Agent = WorkspaceSetup.AgentCommand;
    private const string Name = "nth-workspace-0123456789abcdef0123456789abcdef";
    private const string Token = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private static WorkspaceCommandSeams Seams(FakeChildSessions sessions, bool elevated, TextWriter output, Func<string, CancellationToken, Task<Stream?>>? connect = null, Func<WorkspaceAgentSettings, PcAssembly>? assembly = null, List<string>? log = null) => new()
    {
        Switch = sessions,
        Elevated = () => elevated,
        Output = output,
        Connect = connect ?? ((_, _) => throw new InvalidOperationException("a setting command never connects")),
        Assembly = assembly ?? (_ => throw new InvalidOperationException("a setting command never makes an engine")),
        Log = line =>
        {
            if (log is null) return;
            lock (log) log.Add(line);
        },
    };

    private static string SourceRoot([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", "..", "..", "src"));

    // The code of a source file: its lines that are not comments.
    private static string Code(string file) =>
        string.Join('\n', File.ReadLines(file).Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    [Fact]
    public void Program_hands_the_three_commands_over_after_the_relays_and_before_the_desktop_app()
    {
        var program = File.ReadAllText(Path.Combine(SourceRoot(), "Marrow.App", "Program.cs"));
        var relay = program.IndexOf("PcControlBridge.RunClient(", StringComparison.Ordinal);
        var handles = program.IndexOf("WorkspaceCommands.Handles(args)", StringComparison.Ordinal);
        var runs = program.IndexOf("WorkspaceCommands.Run(args)", StringComparison.Ordinal);
        var desktop = program.IndexOf("RunVelopackHook(AppContext.BaseDirectory);", StringComparison.Ordinal);
        Assert.True(relay > 0 && relay < handles && handles < runs && runs < desktop, string.Join(", ", relay, handles, runs, desktop));
        Assert.True(WorkspaceCommands.Handles([Agent]));
        Assert.True(WorkspaceCommands.Handles([WorkspaceSetup.EnableCommand]));
        Assert.True(WorkspaceCommands.Handles([WorkspaceSetup.DisableCommand, "extra"]));
        Assert.False(WorkspaceCommands.Handles([]));
        Assert.False(WorkspaceCommands.Handles(["--tactus-workspace"]));
        Assert.False(WorkspaceCommands.Handles(["nth.exe", WorkspaceSetup.EnableCommand]));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-pcws-2")]
    public async Task The_setting_commands_write_the_switch_only_from_an_administrator_terminal()
    {
        var sessions = new FakeChildSessions();
        var output = new StringWriter();
        Assert.Equal(WorkspaceSetup.ExitNotElevated, await WorkspaceCommands.Run([WorkspaceSetup.EnableCommand], Seams(sessions, false, output)));
        Assert.Equal(WorkspaceSetup.ExitNotElevated, await WorkspaceCommands.Run([WorkspaceSetup.DisableCommand], Seams(sessions, false, output)));
        Assert.Empty(sessions.Writes);
        Assert.Equal(WorkspaceSetup.ExitDone, await WorkspaceCommands.Run([WorkspaceSetup.EnableCommand], Seams(sessions, true, output)));
        Assert.Equal(WorkspaceSetup.ExitDone, await WorkspaceCommands.Run([WorkspaceSetup.DisableCommand], Seams(sessions, true, output)));
        Assert.Equal(new[] { true, false }, sessions.Writes);
        Assert.Equal(WorkspaceSetup.ExitUsage, await WorkspaceCommands.Run([WorkspaceSetup.EnableCommand, "--force"], Seams(sessions, true, output)));
        Assert.Equal(WorkspaceSetup.ExitUsage, await WorkspaceCommands.Run(["--other"], Seams(sessions, true, output)));
        Assert.Equal(2, sessions.Writes.Count);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-pcws-5")]
    public async Task The_agent_takes_only_a_pipe_and_token_of_the_exact_shape_and_says_when_nth_is_not_there()
    {
        var log = new List<string>();
        var tried = new List<string>();
        var seams = Seams(new FakeChildSessions(), true, new StringWriter(), (name, _) =>
        {
            tried.Add(name);
            return Task.FromResult<Stream?>(null);
        }, log: log);
        Assert.Equal(WorkspaceSetup.ExitUsage, await WorkspaceCommands.Run([Agent], seams));
        Assert.Equal(WorkspaceSetup.ExitUsage, await WorkspaceCommands.Run([Agent, Name], seams));
        Assert.Equal(WorkspaceSetup.ExitUsage, await WorkspaceCommands.Run([Agent, "\\\\.\\pipe\\other", Token], seams));
        Assert.Equal(WorkspaceSetup.ExitUsage, await WorkspaceCommands.Run([Agent, Name, Token.ToUpperInvariant()], seams));
        Assert.Equal(WorkspaceSetup.ExitUsage, await WorkspaceCommands.Run([Agent, Name, Token, "extra"], seams));
        Assert.Empty(tried);
        Assert.Equal(WorkspaceAgent.ExitBroken, await WorkspaceCommands.Run([Agent, Name, Token], seams));
        Assert.Equal(new[] { Name }, tried);
        Assert.Contains("workspace.agent.unreachable", log);
        Assert.DoesNotContain(log, line => line.Contains(Token, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-pcws-5")]
    public async Task The_agent_serves_nth_over_the_pipe_with_its_own_engine_until_nth_says_bye()
    {
        using var rig = new PcAssemblyRig();
        rig.Desktop.OnLaunch = _ => rig.Desktop.AddApp(0x1a, "Untitled - Notepad", "notepad.exe", 101);
        var (nth, inside) = WorkspaceDuplex.Pair();
        var engines = 0;
        var log = new List<string>();
        var seams = Seams(new FakeChildSessions(), false, new StringWriter(), (_, _) => Task.FromResult<Stream?>(inside), settings =>
        {
            engines++;
            return WorkspaceAgent.Assembly(WorkspaceAgent.Backends(rig.Backends, "C:\\Profiles"), settings);
        }, log);
        var serving = WorkspaceCommands.Run([Agent, Name, Token], seams);
        var link = await WorkspaceLink.Accept(nth, Token, new PcManualClock(), _ => { }, CancellationToken.None);
        Assert.NotNull(link);
        var launched = await link.Launch("C:\\Apps\\notepad.exe", CancellationToken.None);
        Assert.True(launched.Started, launched.Problem);
        await link.DisposeAsync();
        Assert.Equal(WorkspaceAgent.ExitDone, await serving.WaitAsync(Patience));
        Assert.Equal(1, engines);
        Assert.Contains("workspace.agent.start", log);
        Assert.DoesNotContain(log, line => line.Contains(Token, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-pcws-2")]
    public void Only_the_users_own_commands_reach_the_call_that_writes_the_switch_and_nothing_starts_them()
    {
        var files = Directory.EnumerateFiles(SourceRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Select(file => (Name: Path.GetFileName(file), Code: Code(file)))
            .ToList();
        string[] Having(string text) => [.. files.Where(file => file.Code.Contains(text, StringComparison.Ordinal)).Select(file => file.Name).Order(StringComparer.Ordinal)];
        Assert.Equal(new[] { "ChildSessionNative.cs" }, Having("WTSEnableChildSessions"));
        Assert.Equal(new[] { "WorkspaceCommands.cs" }, Having("new ChildSessionSwitch("));
        Assert.Equal(new[] { "WorkspaceCommands.cs" }, Having("WorkspaceSetup.Run("));
        var naming = files.Where(file => file.Code.Contains("EnableCommand", StringComparison.Ordinal) || file.Code.Contains("DisableCommand", StringComparison.Ordinal)
            || file.Code.Contains("--tactus-workspace-enable", StringComparison.Ordinal) || file.Code.Contains("--tactus-workspace-disable", StringComparison.Ordinal)).ToList();
        Assert.Contains(naming, file => file.Name == "PcControlSettingsPage.cs");
        foreach (var file in naming)
        {
            foreach (var starts in new[] { "Process.Start", "ProcessStartInfo", "ShellExecute", "runas" })
                Assert.False(file.Code.Contains(starts, StringComparison.Ordinal), file.Name + " names a setting command and has " + starts);
        }
    }
}
