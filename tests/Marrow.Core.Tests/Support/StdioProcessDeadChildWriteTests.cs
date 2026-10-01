using System.Reflection;
using System.Text;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Core.Tests.Support;

// DroppyCode/App/DroppyCodeApp.swift:11-12: "Provider processes can exit while we write to them; report EPIPE instead of
// crashing." (signal(SIGPIPE, SIG_IGN)). PR-LAUNCH-003 windows_path: Windows has no SIGPIPE; a write to a closed pipe fails
// on the write call (ERROR_NO_DATA / ERROR_BROKEN_PIPE, platform_adaptations PA-0004) and must reach the caller's error path,
// never an unhandled fault. The Swift callers of that write are Core/Support/StdioProcess.swift:101 (send), :110 (write)
// and :118 (closeInput): each is `try?` on a serial queue, so a failed write is dropped and the app goes on; the child's
// ending is seen on its message stream, which finishes.
public sealed class StdioProcessDeadChildWriteTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    [Fact]
    [Trait("Req", "PR-LAUNCH-003")] // App/DroppyCodeApp.swift:11-12; Core/Support/StdioProcess.swift:101, :110, :118
    public async Task Writing_to_a_child_that_has_exited_is_absorbed_and_the_child_ending_reaches_the_reader()
    {
        using var runtime = new TestRuntime();
        var child = new StdioProcess(SystemTools.PowerShell, SystemTools.PowerShellScript("exit 3"),
            Environment.CurrentDirectory,
            Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
                .ToDictionary(e => (string)e.Key, e => (string)e.Value!, StringComparer.OrdinalIgnoreCase),
            StdioFramer.Framing.Lines);
        child.Start();
        Assert.Equal(3, await child.WaitForExit().WaitAsync(Bound)); // the provider process is gone
        Assert.False(child.IsRunning);

        for (var round = 0; round < 5; round++) // each is a write to a closed pipe: ERROR_NO_DATA / ERROR_BROKEN_PIPE
        {
            child.Send(new JSONValue.String("prompt " + round));
            child.Write(Encoding.UTF8.GetBytes("raw " + round + "\n"));
        }
        child.CloseInput();
        // Nothing public says the queued writes have run: wait on the write queue itself (ED-119).
        var queue = (SerialQueue)typeof(StdioProcess).GetField("writeQueue", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(child)!;
        queue.Sync(() => { });

        // The process is still here (an unhandled fault on the queue's thread would have ended it), and the reader sees
        // the stream finish rather than hang or throw.
        var messages = new List<JSONValue>();
        await foreach (var message in child.Messages.WithCancellation(new CancellationTokenSource(Bound).Token)) messages.Add(message);
        Assert.Empty(messages);
    }
}
