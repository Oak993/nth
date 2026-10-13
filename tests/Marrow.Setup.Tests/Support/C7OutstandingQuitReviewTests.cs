using Marrow.Core.Support;
using Marrow.Setup.Support;
using Marrow.Setup.Tests.Fakes;
using Marrow.Testing;

namespace Marrow.Setup.Tests.Support;

public sealed class C7OutstandingQuitReviewTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public Task Cancel_still_withdraws_an_earlier_accepted_quit_after_a_fresh_busy_answer() => VerifyAsync(false);

    [Fact]
    [Trait("Decision", "ED-247")]
    public Task Cancel_still_withdraws_an_earlier_uncertain_quit_after_a_fresh_busy_answer() => VerifyAsync(true);

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_lost_withdraw_answer_retains_the_exact_id_for_a_second_cancel()
    {
        var time = new ManualTimeProvider();
        var nth = new FakeNth(time, HandshakeRig.Root);
        var channel = new C7QuitReentryChannel(nth, false) { WithdrawFailuresRemaining = 1 };
        var handshake = new RunningNthHandshake(HandshakeRig.Root, channel, nth, time);
        Assert.Equal(RunningNthState.NotResponding, (await time.RunAsync(handshake.AskAsync())).State);
        await handshake.CancelAsync();
        Assert.True(channel.EarlierQuitPending);
        Assert.Equal(RunningNthState.Cancelled, handshake.Status.State);
        await handshake.CancelAsync();
        Assert.False(channel.EarlierQuitPending);
        var quit = Assert.Single(channel.Requests, request => request.Command == NthControlCommand.Quit);
        var withdrawals = channel.Requests.Where(request => request.Command == NthControlCommand.Withdraw).ToArray();
        Assert.Equal(2, withdrawals.Length);
        Assert.All(withdrawals, withdrawal => Assert.Equal(quit.Id, withdrawal.Id));
        Assert.True(nth.Running);
        Assert.Empty(nth.Ended);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task Cancelling_an_active_request_uses_one_shared_withdrawal_deadline()
    {
        var time = new ManualTimeProvider();
        var nth = new FakeNth(time, HandshakeRig.Root);
        var channel = new C7QuitReentryChannel(nth, false) { BlockWithdraw = true };
        var handshake = new RunningNthHandshake(HandshakeRig.Root, channel, nth, time);
        var asking = handshake.AskAsync();
        try
        {
            await time.AdvanceUntilAsync(() => channel.EarlierQuitPending, TimeSpan.FromSeconds(1));
            Assert.True(channel.EarlierQuitPending);
            Assert.False(asking.IsCompleted);
            await time.RunAsync(handshake.CancelAsync());
        }
        finally
        {
            if (!asking.IsCompleted) await time.RunAsync(handshake.CancelAsync());
        }
        Assert.Equal(RunningNthState.Cancelled, (await asking).State);
        var withdrawal = Assert.Single(channel.Requests, request => request.Command == NthControlCommand.Withdraw);
        var quit = Assert.Single(channel.Requests, request => request.Command == NthControlCommand.Quit);
        Assert.Equal(quit.Id, withdrawal.Id);
        Assert.True(channel.EarlierQuitPending); // no answer is not withdrawal proof
        Assert.True(nth.Running);
        Assert.Empty(nth.Ended);
    }

    private static async Task VerifyAsync(bool lostFirstAnswer)
    {
        var time = new ManualTimeProvider();
        var nth = new FakeNth(time, HandshakeRig.Root);
        var channel = new C7QuitReentryChannel(nth, lostFirstAnswer);
        var handshake = new RunningNthHandshake(HandshakeRig.Root, channel, nth, time);
        Assert.Equal(RunningNthState.NotResponding, (await time.RunAsync(handshake.AskAsync())).State);
        Assert.True(channel.EarlierQuitPending);
        Assert.Equal(RunningNthState.Busy, (await time.RunAsync(handshake.AskAsync())).State);
        Assert.Equal(2, channel.Requests.Count(request => request.Command == NthControlCommand.Quit));

        await handshake.CancelAsync();

        var withdrawn = Assert.Single(channel.Requests, request => request.Command == NthControlCommand.Withdraw);
        var quits = channel.Requests.Where(request => request.Command == NthControlCommand.Quit).ToArray();
        Assert.True(Guid.TryParseExact(quits[0].Id, "N", out _));
        Assert.Equal(quits[0].Id, withdrawn.Id);
        Assert.NotEqual(quits[1].Id, withdrawn.Id); // a definitive Busy belongs only to the later attempt
        Assert.False(channel.EarlierQuitPending);
        Assert.Equal(RunningNthState.Cancelled, handshake.Status.State);
        Assert.True(nth.Running);
        Assert.Empty(nth.Ended);
    }
}
