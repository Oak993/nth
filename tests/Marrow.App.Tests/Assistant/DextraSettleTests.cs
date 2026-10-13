using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-7 (DESIGN.md rev 5, 12.4 "Taint"; ED-NEW-dextra-as7-18): an nth-dextra call arrives on a pool thread while
// the chat's provider events (a page it read just before) may still wait on the main thread. Before the call is decided
// the controller lets the main thread run what waits there, so the policy reads the chat with that taint; when the main
// thread does not get to it in time, the call is refused. The main thread's queue is held still and the clock is manual.
[Trait("Feature", "AS-7")]
[Trait("Decision", "ED-NEW-dextra-as7-18")]
public sealed class DextraSettleTests
{
    [Fact]
    public void A_call_settles_only_after_the_events_already_waiting_on_the_main_thread_have_tainted_the_chat()
    {
        using var rig = new DextraTaintRig();
        var home = rig.Home();
        var chat = DextraTaintRig.AsChat(home);
        rig.Agents.Defer = true;
        // The read is still waiting on the main thread when the call comes in.
        rig.Agents.Post(() => rig.Controller.OnProviderEvent(home.Id,
            new ProviderEvent.ToolStarted("w1", new ToolCall { ToolCallKind = ToolCall.Kind.Web, Title = "https://hostile.example/page" })));

        var settled = rig.Controller.Settle(chat, CancellationToken.None);
        Assert.False(settled.IsCompleted);
        Assert.False(rig.Controller.PolicyContext(chat).Tainted);

        rig.Agents.RunPosted();
        Assert.True(MissionRig.Within(settled));
        Assert.True(rig.Controller.PolicyContext(chat).Tainted);
    }

    [Fact]
    public void A_call_whose_main_thread_does_not_get_to_it_in_time_fails_to_settle()
    {
        var clock = new ManualTimeProvider();
        using var rig = new DextraTaintRig(time: clock);
        var home = rig.Home();
        rig.Agents.Defer = true;
        var settled = rig.Controller.Settle(DextraTaintRig.AsChat(home), CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(11));
        Assert.True(MissionRig.Within(settled.ContinueWith(_ => { }, TaskScheduler.Default)));
        Assert.IsType<TimeoutException>(settled.Exception?.InnerException);
    }

    [Fact]
    public void On_the_main_thread_itself_a_call_settles_at_once()
    {
        using var rig = new DextraTaintRig();
        var home = rig.Home();
        Assert.True(rig.Controller.Settle(DextraTaintRig.AsChat(home), CancellationToken.None).IsCompletedSuccessfully);
    }
}
