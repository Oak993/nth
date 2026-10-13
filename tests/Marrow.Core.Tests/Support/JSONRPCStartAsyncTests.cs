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
}
