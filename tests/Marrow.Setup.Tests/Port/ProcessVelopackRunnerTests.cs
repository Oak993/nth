using Marrow.Setup.Port;
using Marrow.Setup.Support;

namespace Marrow.Setup.Tests.Port;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 6 and 10, unit U0 (ED-NEW-inst-u0-1). The production runner starts a
// child with no window, passes arguments one by one, runs it in the command's folder with the command's variables added,
// and reports the child's own exit code when it ends. The child here is Windows' cmd.exe running one built-in command in
// a temporary folder; no Velopack program and no installer is ever started by these tests.
public sealed class ProcessVelopackRunnerTests : IDisposable
{
    private readonly string folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "nth-tests", "runner-" + Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public async Task The_childs_own_exit_code_comes_back_when_it_ends()
    {
        var run = new ProcessVelopackRunner().Start(Command(["/d", "/c", "exit 7"]));

        Assert.True(run.ProcessId > 0);
        Assert.Equal(7, await run.Exited.WaitAsync(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public async Task The_child_runs_in_the_commands_folder_with_the_options_variable_added()
    {
        var options = Path.Combine(folder, "nth Setup", "1.0.0", "install-options.json");
        var command = Command(["/d", "/c", "set NTH_SETUP_OPTIONS> seen.txt"]) with
        {
            Environment = new Dictionary<string, string> { ["NTH_SETUP_OPTIONS"] = options },
        };

        Assert.Equal(0, await new ProcessVelopackRunner().Start(command).Exited.WaitAsync(TimeSpan.FromSeconds(30)));

        Assert.Equal("NTH_SETUP_OPTIONS=" + options, File.ReadAllText(Path.Combine(folder, "seen.txt")).TrimEnd());
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public void A_program_that_does_not_exist_fails_to_start_rather_than_reporting_an_exit_code() =>
        Assert.ThrowsAny<Exception>(() => new ProcessVelopackRunner().Start(Command([]) with { Executable = Path.Combine(folder, "missing.exe") }));

    private VelopackCommand Command(string[] arguments) => new(
        VelopackCommandKind.Install,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
        arguments,
        folder,
        new Dictionary<string, string>(),
        folder,
        LogPath: null);
}
