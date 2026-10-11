// PORT-ONLY: Tactus (ED-NEW-pcctl-9; DESIGN.md 3.1 item 5, critique C23). When a capture pool waits for a frame and when
// it closes, on a plain millisecond clock: no Windows.Graphics.Capture call is made.
using Marrow.App.Port.PcControl;

namespace Marrow.App.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-9")]
public sealed class WgcPoolPolicyTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_new_pool_waits_for_its_first_frame(bool throttled)
    {
        var policy = new WgcPoolPolicy(throttled);

        Assert.Equal(WgcPoolPolicy.FirstFrameMs, policy.WaitMs(1_000));
    }

    [Fact]
    public void An_unthrottled_pool_never_waits_once_it_has_a_frame()
    {
        var policy = new WgcPoolPolicy(throttled: false);
        policy.Frame(1_000);

        Assert.Equal(0, policy.WaitMs(1_001));
        Assert.Equal(0, policy.WaitMs(60_000));
    }

    [Fact]
    public void A_throttled_pool_waits_out_the_rest_of_the_slot_a_change_may_be_held_in()
    {
        var policy = new WgcPoolPolicy(throttled: true);
        policy.Frame(1_000);

        Assert.Equal(200 + 34 - 50, policy.WaitMs(1_050));
        Assert.Equal(1, policy.WaitMs(1_233));
        Assert.Equal(0, policy.WaitMs(1_234));
        Assert.Equal(0, policy.WaitMs(5_000));
    }

    [Fact]
    public void An_unthrottled_pool_above_five_frames_a_second_closes_after_the_grab()
    {
        var policy = new WgcPoolPolicy(throttled: false);
        for (var i = 0; i < 10; i++) policy.Frame(1_000 + (i * 150));

        Assert.False(policy.CloseAfterGrab(2_400));

        policy.Frame(2_500);
        Assert.True(policy.CloseAfterGrab(2_500));
    }

    [Fact]
    public void Frames_older_than_two_seconds_stop_counting()
    {
        var policy = new WgcPoolPolicy(throttled: false);
        for (var i = 0; i < 11; i++) policy.Frame(1_000 + (i * 10));

        Assert.True(policy.CloseAfterGrab(1_200));
        Assert.Equal(11, policy.RecentFrames(3_000));
        Assert.False(policy.CloseAfterGrab(3_101));
        Assert.Equal(0, policy.RecentFrames(3_101));
    }

    [Fact]
    public void A_throttled_pool_stays_open_however_busy_the_window()
    {
        var policy = new WgcPoolPolicy(throttled: true);
        for (var i = 0; i < 60; i++) policy.Frame(1_000 + (i * 16));

        Assert.False(policy.CloseAfterGrab(2_000));
        Assert.True(policy.Throttled);
    }

    [Fact]
    public void A_pool_unused_for_thirty_seconds_is_idle_and_use_renews_it()
    {
        var policy = new WgcPoolPolicy(throttled: true);
        policy.Use(10_000);

        Assert.False(policy.Idle(39_999));
        Assert.True(policy.Idle(40_000));

        policy.Use(39_000);
        Assert.False(policy.Idle(40_000));
        Assert.True(policy.Idle(69_000));
    }

    [Fact]
    public void Frames_do_not_count_as_use()
    {
        var policy = new WgcPoolPolicy(throttled: false);
        policy.Use(0);
        for (var t = 1_000; t < 31_000; t += 1_000) policy.Frame(t);

        Assert.True(policy.Idle(30_000));
        Assert.Equal((long?)30_000, policy.LastFrameAt);
    }
}
