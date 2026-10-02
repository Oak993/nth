#if DEBUG
using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Providers;

// Expectations come from DroppyCode/Services/Providers/CommandCodeSession.swift (B3 52bc7926): start() :98-113 and the process
// launch in send() :181-193. The session runs against the compiled Command Code stand-in (tests/fixtures/command-code-cli,
// R-TEST-8, R-TEST-10: offline, never a real `cmd` CLI) with the data root redirected into MARROW_PORT_DATA_DIR through
// DevDataRoot (ED-212, Debug builds only: hence the #if DEBUG), so the approval mod, the approvals directories and the MCP
// bridge are written under the throwaway root and never under the real %LOCALAPPDATA%\Marrow Code (R-TEST-9).
// [Collection]: the redirect is the process-wide MARROW_DEV_DATA_ROOT variable, so the class shares the one non-parallel
// collection that serializes every user of it (R-TEST-5).
[Collection("Dev data root env")]
public sealed class CommandCodeSessionLaunchTests
{
    private static string Utf8String(byte[] bytes) => Encoding.UTF8.GetString(bytes);

    [Fact]
    [Trait("Req", "PR-PROVCLI-207")] // CommandCodeSession.swift:103-113; CommandCodeAPI.swift:439-444 (support directory, deviation OD-0008 C7)
    public async Task Start_writes_the_approval_mod_creates_a_fresh_approvals_directory_and_returns_an_empty_id()
    {
        using var rig = new CommandCodeSessionRig("start-fresh");
        Assert.False(Directory.Exists(rig.SupportDirectory));
        Assert.False(rig.Session.IsRunning);

        var id = await rig.Session.Start();

        Assert.Equal("", id); // no session id yet: the only provider whose successful start returns an empty one
        Assert.True(rig.Session.IsRunning);
        Assert.Equal(Encoding.UTF8.GetBytes(CommandCodeAPI.ApprovalMod), File.ReadAllBytes(rig.ModPath));
        // <data root>/Marrow Code/CommandCode/approvals/<a fresh UUID>, intermediate directories included.
        var directory = Assert.Single(Directory.GetDirectories(rig.ApprovalsRoot));
        var name = Path.GetFileName(directory);
        Assert.True(Guid.TryParseExact(name, "D", out _), name);
        Assert.Equal(name.ToUpperInvariant(), name); // Swift UUID().uuidString is upper case
        Assert.Empty(Directory.GetFileSystemEntries(directory));
        Assert.Equal(new[] { "approvals", "marrow-approvals.ts" }, Directory.GetFileSystemEntries(rig.SupportDirectory).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-207")] // CommandCodeSession.swift:104-106 (a fresh UUID for every start)
    public async Task Every_start_creates_its_own_approvals_directory()
    {
        using var rig = new CommandCodeSessionRig("start-twice");
        var second = new CommandCodeSession(rig.Configuration);
        try
        {
            await rig.Session.Start();
            await second.Start();

            var directories = Directory.GetDirectories(rig.ApprovalsRoot);
            Assert.Equal(2, directories.Length);
            Assert.NotEqual(Path.GetFileName(directories[0]), Path.GetFileName(directories[1]));
        }
        finally
        {
            second.Stop();
        }
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-207")] // CommandCodeSession.swift:110-111,255-263 (started true and stopped false; stop removes the directory but not the mod)
    public async Task Start_after_a_stop_runs_again_and_stop_removes_the_approvals_directory()
    {
        using var rig = new CommandCodeSessionRig("start-stop-start");
        await rig.Session.Start();
        var first = Assert.Single(Directory.GetDirectories(rig.ApprovalsRoot));

        rig.Session.Stop();

        Assert.False(rig.Session.IsRunning);
        Assert.False(Directory.Exists(first));
        Assert.True(File.Exists(rig.ModPath));

        await rig.Session.Start();

        Assert.True(rig.Session.IsRunning); // stopped is false again
        var second = Assert.Single(Directory.GetDirectories(rig.ApprovalsRoot));
        Assert.NotEqual(first, second);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-207")] // CommandCodeSession.swift:100-102,112 (a resumed session's id is what start returns)
    public async Task Start_returns_the_resumed_session_id()
    {
        var home = CommandCodeScratch.NewFolder("resume-home");
        var projects = Path.Combine(home, ".commandcode", "projects", "some-project");
        Directory.CreateDirectory(projects);
        File.WriteAllBytes(Path.Combine(projects, "sess-42.jsonl"), []);
        using var rig = new CommandCodeSessionRig("start-resume", home: home, resumeID: "sess-42");

        var id = await rig.Session.Start();

        Assert.Equal("sess-42", id);
        Assert.True(rig.Session.IsRunning);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-207")] // CommandCodeSession.swift:103 (a failed mod install propagates out of start(), before anything else is created)
    public async Task A_failed_mod_install_propagates_out_of_start()
    {
        using var rig = new CommandCodeSessionRig("start-mod-fails");
        // A file where the mod's folder must be: installMod's createDirectory fails (a failing replace of the mod file itself
        // would only retry on the manual clock this rig binds, so the failure is placed at the folder step).
        Directory.CreateDirectory(Path.GetDirectoryName(rig.SupportDirectory)!);
        File.WriteAllText(rig.SupportDirectory, "not a directory");

        await Assert.ThrowsAnyAsync<Exception>(async () => await rig.Session.Start());

        Assert.False(rig.Session.IsRunning);
        Assert.True(File.Exists(rig.SupportDirectory)); // untouched: nothing was created beside it
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-207")] // CommandCodeSession.swift:103 (a failed write of the mod file propagates out of start(); no approvals directory is made)
    public async Task A_failed_mod_write_propagates_out_of_start()
    {
        using var rig = new CommandCodeSessionRig("start-mod-write-fails");
        Directory.CreateDirectory(rig.ModPath); // a directory where the mod file must go: it cannot be replaced

        // FileOps.WriteAtomic retries a failed replace, waiting on the (manual) clock this rig binds: run start() off the test
        // thread and let the test thread advance that clock until the write gives up.
        var start = Task.Run(async () => await rig.Session.Start());
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (!start.IsCompleted && DateTime.UtcNow < deadline)
        {
            rig.Runtime.Advance(TimeSpan.FromSeconds(1));
            Thread.Sleep(5);
        }
        Assert.True(start.IsCompleted, "start() never gave up on the mod write");

        await Assert.ThrowsAnyAsync<Exception>(() => start);
        Assert.False(rig.Session.IsRunning);
        Assert.False(Directory.Exists(rig.ApprovalsRoot));
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-207")] // CommandCodeSession.swift:107 (a failed directory creation propagates; the session never counts as started)
    public async Task A_failed_approvals_directory_creation_propagates_out_of_start()
    {
        using var rig = new CommandCodeSessionRig("start-dir-fails");
        Directory.CreateDirectory(rig.SupportDirectory);
        File.WriteAllText(rig.ApprovalsRoot, "not a directory"); // a file where the approvals folder must be

        await Assert.ThrowsAnyAsync<Exception>(async () => await rig.Session.Start());

        Assert.False(rig.Session.IsRunning);
        Assert.True(File.Exists(rig.ModPath)); // the mod was installed before the directory step failed
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-207")] // CommandCodeSession.swift:109,650-661 (the filesystem watcher is started on the approvals directory)
    public async Task Start_watches_the_approvals_directory_for_the_mods_requests()
    {
        using var rig = new CommandCodeSessionRig("start-watcher");
        await rig.Session.Start();
        rig.SetField("turnActive", true); // requests are only picked up while a turn runs (:670)
        var directory = Assert.Single(Directory.GetDirectories(rig.ApprovalsRoot));

        File.WriteAllText(
            Path.Combine(directory, "r1.request.json"),
            "{\"toolCallId\":\"tc-1\",\"toolName\":\"shell_command\",\"input\":{\"command\":\"dir\"}}",
            new UTF8Encoding(false));
        rig.WaitFor(() => rig.Events.OfType<ProviderEvent.Approval>().Any(), "the watcher to pick the request up");

        var approval = Assert.Single(rig.Events.OfType<ProviderEvent.Approval>()).Value;
        Assert.Equal("tc-1", approval.Id);
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-216")] // CommandCodeSession.swift:181-193 (a failed process start resets turnActive first, then throws the wrapped message)
    public async Task A_launch_that_cannot_start_clears_the_turn_and_reports_the_wrapped_message()
    {
        using var rig = new CommandCodeSessionRig("launch-fails", launchable: false);
        await rig.Session.Start();

        var error = await Assert.ThrowsAsync<ProviderError>(() => rig.Session.Send(CommandCodeSessionRig.Turn()));

        var failed = Assert.IsType<ProviderError.Case.Failed>(error.ErrorCase);
        Assert.StartsWith("Command Code could not start: ", failed.Message, StringComparison.Ordinal);
        // The underlying error's own localized description follows the prefix: ask the same launch directly what it says.
        var launcher = new StdioProcess(rig.Configuration.Executable!.Path, [], rig.WorkingDirectory, new Dictionary<string, string>());
        var underlying = Assert.ThrowsAny<Exception>(launcher.Start).Message;
        Assert.NotEqual("", underlying);
        Assert.Equal("Command Code could not start: " + underlying, failed.Message);
        Assert.Equal(failed.Message, error.ErrorDescription);
        Assert.False((bool)rig.Field("turnActive")!);
        Assert.Empty(rig.Events); // nothing was announced: no turnStarted, no completion

        // The flag was cleared, so the next send is not blocked by "still on the last turn" (PR-PROVCLI-208): it fails
        // at the launch again, with the same wrapped message.
        var again = await Assert.ThrowsAsync<ProviderError>(() => rig.Session.Send(CommandCodeSessionRig.Turn()));
        Assert.StartsWith("Command Code could not start: ", Assert.IsType<ProviderError.Case.Failed>(again.ErrorCase).Message, StringComparison.Ordinal);
        Assert.Equal(2, ((int)rig.Field("turnCounter")!)); // both attempts reached the launch
    }

    [Fact]
    [Trait("Req", "PR-PROVCLI-216")] // CommandCodeSession.swift:181-186 (the process gets the executable, the argv, the working directory and the built environment)
    public async Task The_launch_runs_the_executable_in_the_working_directory_with_the_built_argv_and_environment()
    {
        using var rig = new CommandCodeSessionRig("launch-ok", model: "gpt-5.5");
        await rig.Session.Start();

        await rig.Session.Send(CommandCodeSessionRig.Turn());

        // The stand-in found its control folder through the environment the session built, and recorded who it was run as.
        var arguments = rig.LaunchArguments(0);
        Assert.Equal("-p", arguments[0]);
        Assert.Equal("gpt-5.5", arguments[arguments.ToList().IndexOf("--model") + 1]);
        Assert.Equal(
            Path.GetFullPath(rig.WorkingDirectory).TrimEnd('\\').ToUpperInvariant(),
            Path.GetFullPath(rig.LaunchDirectory(0)).TrimEnd('\\').ToUpperInvariant());
        rig.WaitForTurns(1);
        Assert.IsType<ProviderEvent.TurnCompleted>(rig.Events[^1]);
    }
}
#endif
