using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;
using Marrow.Services.Store;

namespace Marrow.App.Tests.Port.Accounts;

// ED-NEW-perf-ui-thread-5: the runtime hands its journal lines to the writer's queue at every save and every turn's end, and the disk work (the append, the rotation, the compression)
// is on that queue's thread. The writer's sink is held here, so a flush that wrote on the calling thread would never come back.
[Trait("Rule", "R-TEST-4")]
public sealed class HandoffJournalOffThreadTests
{
    [Fact]
    public void A_turn_that_ends_returns_while_the_disk_is_still_held_and_the_lines_reach_the_disk_thread_whole()
    {
        using var rig = new ThreadRuntimeTurnRig(settings: app => app.Settings.Accounts.Add(ProviderKind.Claude));
        rig.Send("Keep the command output.");
        var folder = Path.Combine(Storage.ThreadsDirectory, SwiftUUID.UuidString(rig.InitialThread.Id));
        var release = new ManualResetEventSlim();
        var entered = new ManualResetEventSlim();
        var written = new List<string>();
        var diskThread = 0;
        using var sink = HandoffJournalWriter.Sink.Override((where, lines) =>
        {
            if (where != folder) return;
            diskThread = Environment.CurrentManagedThreadId;
            lock (written) written.AddRange(lines);
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
        });
        var uiThread = rig.H.Main.OnMain(() => Environment.CurrentManagedThreadId);
        var output = new string('x', 150_000) + "LAST-BYTE";

        rig.H.Main.OnMain(() =>
        {
            rig.Session.Emit(new ProviderEvent.ToolStarted("tool", new() { ToolCallKind = ToolCall.Kind.Command, Title = "offline" }));
            rig.Session.Emit(new ProviderEvent.ToolOutput("tool", output));
            rig.Session.Emit(new ProviderEvent.ToolUpdated("tool", new() { Status = ToolCall.Status.Completed }));
        });
        rig.Complete(); // the turn's end flushes the journal: with the sink held this returns only if the flush does not write on this thread

        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)), "the writer never took the lines");
        Assert.NotEqual(uiThread, diskThread);
        Assert.NotEqual(Environment.CurrentManagedThreadId, diskThread);
        release.Set();
        HandoffJournalWriter.WaitForQueuedWrites();
        lock (written) Assert.Contains(written, line => line.Contains("LAST-BYTE", StringComparison.Ordinal));
    }
}
