using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Port.MultiAccount;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Review;

// Independent review of codex/ma-4 (finding MA4-1): ThreadRuntime.Handle journals every provider event of every thread, synchronously, to a second file beside the thread
// (every delta, every tool output, unbounded), for every user, including one with a single account who will never hand over. "One account looks exactly like today."
public sealed class ReviewMa4JournalScopeTests
{
    [Fact]
    [Trait("Feature", "MA-4")]
    [Trait("Review", "MA-4")]
    [Trait("Rule", "R-TEST-4")]
    public void REVIEW_a_thread_of_a_single_account_user_writes_no_handoff_journal()
    {
        using var rig = new ThreadRuntimeTurnRig();
        rig.Send("hello");
        rig.H.Main.OnMain(() => rig.Session.Emit(new ProviderEvent.MessageDelta("assistant-1", "some streamed text")));
        rig.Complete();
        var folder = Path.Combine(Storage.ThreadsDirectory, SwiftUUID.UuidString(rig.InitialThread.Id));
        Assert.False(File.Exists(HandoffJournal.PathIn(folder)), "handoff-events.jsonl was written for a user with one account per service");
    }
}
