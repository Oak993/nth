using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Core.Tests.Support;

// PORT-ONLY (ED-NEW-perf-ui-thread-6): JSONRPCConnection.StartAsync creates the child on the pool (CreateProcessW is not the UI thread's) and then reads as Start does; a Close that comes
// while the child is being created ends the child once it exists. The children are powershell.exe scripts (system binary, R-TEST-10) that log every stdin frame to stderr.
public sealed class JSONRPCStartAsyncTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(90);

    private const string LogUntilBye =
        "$i=[Console]::In; $e=[Console]::Error; while(($l=$i.ReadLine()) -ne $null){ $e.WriteLine($l); $e.Flush(); if($l.Contains('\"method\":\"bye\"')){ break } }";

    private static StdioProcess Child() =>
        new(SystemTools.PowerShell, SystemTools.PowerShellEncoded(LogUntilBye), Environment.CurrentDirectory,
            Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
                .ToDictionary(e => (string)e.Key, e => (string)e.Value!, StringComparer.OrdinalIgnoreCase));

    private static List<string> Lines(string text) => text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();

    [Fact]
    [Trait("Rule", "R-TEST-4")] // ED-NEW-perf-ui-thread-6
    public void A_connection_started_asynchronously_has_a_running_child_and_writes_what_a_started_one_writes()
    {
        using var runtime = new TestRuntime();
        var process = Child();
        var connection = new JSONRPCConnection(process, sendsVersion: true);

        runtime.Loop.Run(() => connection.StartAsync(), Bound);
        Assert.True(process.IsRunning);
        runtime.OnMain(() =>
        {
            connection.Notify("tick");
            connection.Notify("bye", JSONValue.FromDictionaryLiteral());
        });
        runtime.Loop.RunUntil(() => Lines(process.ErrorTail).Count >= 2, Bound);

        var frames = Lines(process.ErrorTail).Select(line => JSONValue.Parse(line)!).ToList();
        Assert.Equal(new JSONValue.String("tick"), frames[0]["method"]);
        Assert.Equal(new JSONValue.String("2.0"), frames[0]["jsonrpc"]);
        Assert.Equal(new JSONValue.String("bye"), frames[1]["method"]);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // ED-NEW-perf-ui-thread-6: whichever of the pool's creation and the Close came first, no child is left running
    public void A_close_that_comes_with_the_start_leaves_no_child_running()
    {
        using var runtime = new TestRuntime();
        var process = Child();
        var connection = new JSONRPCConnection(process, sendsVersion: true);
        Task? start = null;

        runtime.OnMain(() =>
        {
            start = connection.StartAsync();
            connection.Close(); // the child may not exist yet: this Close can end nothing
        });
        runtime.Loop.Run(() => start!, Bound);
        runtime.Loop.RunUntil(() => !process.IsRunning, Bound);

        Assert.False(process.IsRunning);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // ED-NEW-perf-ui-thread-7: the order ED-NEW-perf-ui-thread-6 could not hold, now held by the launch seam: the Close comes while the child is still being created
    public void A_close_that_comes_while_the_child_is_being_created_ends_it_once_it_exists()
    {
        using var runtime = new TestRuntime();
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var launches = 0;
        using var scope = StdioProcess.LaunchRunner.Override((_, launch) => Task.Run(async () =>
        {
            await released.Task;
            launch();
            Interlocked.Increment(ref launches);
        }));
        // A child that ignores its stdin: the Close below shuts stdin before the child exists, so only a Terminate can end it before its 60 s.
        var process = new StdioProcess(SystemTools.PowerShell, SystemTools.PowerShellEncoded("Start-Sleep -Seconds 60"), Environment.CurrentDirectory,
            Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!, StringComparer.OrdinalIgnoreCase));
        var connection = new JSONRPCConnection(process, sendsVersion: true);
        Task? start = null;

        runtime.OnMain(() =>
        {
            start = connection.StartAsync();
            connection.Close(); // the child does not exist yet: this Close ends nothing
        });
        Assert.False(process.IsRunning);
        Assert.Equal(0, Volatile.Read(ref launches));

        released.SetResult();
        runtime.Loop.Run(() => start!, Bound);
        Assert.Equal(1, Volatile.Read(ref launches));
        Assert.True(process.ProcessIdentifier > 0);
        var due = runtime.Time.Elapsed + TimeSpan.FromSeconds(3); // a Terminate of a running child arms the forced kill 3 s later on the test clock; a Close before the launch arms nothing
        runtime.Loop.RunUntil(() => runtime.Time.TimerDeadlines.Contains(due), Bound);
        runtime.Advance(TimeSpan.FromSeconds(3));
        runtime.Loop.RunUntil(() => !process.IsRunning, TimeSpan.FromSeconds(15));
        Assert.False(process.IsRunning);
    }
}
