using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Core.Tests.Port;

// ED-207: the per-flow observer of Shell.Run calls that lets a test assert a call site's literal argv and timeout budget
// while the real process runs. Each test starts a TestRuntime, so the deadline Shell.Run arms moves only on the manual
// clock (R-TEST-5); the processes are system binaries (R-TEST-10).
public sealed class ShellObserverTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    [Fact]
    [Trait("Decision", "ED-207")]
    public async Task A_call_is_reported_on_entry_with_every_argument_its_site_passed_and_the_process_still_runs()
    {
        using var runtime = new TestRuntime();
        using var shell = ShellRecording.Start();
        var arguments = new List<string> { "/L", "C" };
        var environment = ShellScripts.HostEnvironment();
        var input = ShellScripts.Utf8("b\r\na\r\n");
        var directory = Environment.SystemDirectory;
        var run = Shell.Run(SystemTools.Sort, arguments, directory, environment, input, timeout: 45, outputLimit: 4096);
        // Reported before Run returned, so before the process could have ended.
        var call = Assert.Single(shell.Calls);
        Assert.Equal(SystemTools.Sort, call.Executable);
        Assert.Equal(new[] { "/L", "C" }, call.Arguments);
        Assert.NotSame(arguments, call.Arguments);
        Assert.Equal(directory, call.Directory);
        Assert.Same(environment, call.Environment);
        Assert.Same(input, call.Input);
        Assert.Equal(45.0, call.Timeout);
        Assert.Equal(4096, call.OutputLimit);
        var result = await run.WaitAsync(Bound);
        Assert.True(result.Succeeded, result.FailureMessage);
        Assert.True(result.Output.IndexOf('a', StringComparison.Ordinal) < result.Output.IndexOf('b', StringComparison.Ordinal));
        Assert.Single(shell.Calls);
    }

    [Fact]
    [Trait("Decision", "ED-207")]
    public async Task Only_the_observing_flow_and_the_flows_it_starts_are_reported()
    {
        using var runtime = new TestRuntime();
        using var shell = ShellRecording.Start();
        Task<ShellResult> elsewhere;
        using (ExecutionContext.SuppressFlow())
            elsewhere = Task.Run(() => Shell.Run(SystemTools.Where, ["/Q", "where.exe"], null, ShellScripts.HostEnvironment()));
        var started = Task.Run(() => Shell.Run(SystemTools.Where, ["/Q", "sort.exe"], null, ShellScripts.HostEnvironment()));
        Assert.True((await elsewhere.WaitAsync(Bound)).Succeeded);
        Assert.True((await started.WaitAsync(Bound)).Succeeded);
        var call = Assert.Single(shell.Calls);
        Assert.Equal(new[] { "/Q", "sort.exe" }, call.Arguments);
    }

    [Fact]
    [Trait("Decision", "ED-207")]
    public async Task Disposing_an_observer_restores_the_one_that_was_there_before()
    {
        using var runtime = new TestRuntime();
        var outer = ShellRecording.Start();
        var inner = ShellRecording.Start();
        await Shell.Run(SystemTools.Where, ["/Q", "sort.exe"], null, ShellScripts.HostEnvironment()).WaitAsync(Bound);
        inner.Dispose();
        await Shell.Run(SystemTools.Where, ["/Q", "where.exe"], null, ShellScripts.HostEnvironment()).WaitAsync(Bound);
        outer.Dispose();
        await Shell.Run(SystemTools.Where, ["/Q", "sort.exe"], null, ShellScripts.HostEnvironment()).WaitAsync(Bound);
        Assert.Equal(new[] { "sort.exe" }, inner.Calls.Select(call => call.Arguments[1]));
        Assert.Equal(new[] { "where.exe" }, outer.Calls.Select(call => call.Arguments[1]));
    }
}
