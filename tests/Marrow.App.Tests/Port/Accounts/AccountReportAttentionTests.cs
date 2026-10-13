using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Port.Accounts;

// ED-NEW-perf-idle-5: the account-limits poll (every 3 min per account, and for some providers a CLI process each time) ran for as
// long as a thread runtime was loaded, with the app minimised and nobody there. Now an account is in use only while a window of
// the app can be seen (or a turn runs, or a handshake cost waits), and the poll picks up at once when one can be seen again.
[Trait("Rule", "R-TEST-4")]
public sealed class AccountReportAttentionTests
{
    [Fact]
    public void Reports_are_not_polled_while_no_window_can_be_seen_and_read_at_once_when_one_can()
    {
        var attention = new FakeAppAttention();
        using var scope = AppAttention.Instance.Override(attention);
        using var rig = new AccountReportDemandRig();
        var view = rig.Observe();
        rig.Drain();
        var polled = rig.Reads.Count;
        for (var i = 0; i < 3; i++) rig.Advance(); // seen: it polls through twelve minutes
        Assert.True(rig.Reads.Count > polled, "a seen window polls");

        rig.H.Main.OnMain(() => { attention.IsSeen = false; attention.Raise(); });
        rig.Drain();
        var asleep = rig.Reads.Count;
        for (var i = 0; i < 20; i++) rig.Advance(); // an hour and twenty minutes, minimised
        Assert.Equal(asleep, rig.Reads.Count);

        rig.Percent = 91;
        rig.H.Main.OnMain(() => { attention.IsSeen = true; attention.Raise(); });
        rig.H.Main.OnMain(() => rig.H.Main.Time.Advance(TimeSpan.FromSeconds(1))); // the poll's timer was armed for now
        rig.Drain();
        Assert.True(rig.Reads.Count > asleep, "seen again: read at once");
        Assert.Equal(91, rig.H.Main.OnMain(() => rig.App.AccountRouting.Snapshot(rig.Next)!.Limits!.Windows[0].Percent));
        rig.H.Main.OnMain(view.Dispose);
        rig.Drain();
    }

    [Fact]
    public void With_no_window_tracked_the_attention_reads_as_seen()
    {
        var attention = new AppAttention();
        Assert.True(attention.Seen);
    }
}
