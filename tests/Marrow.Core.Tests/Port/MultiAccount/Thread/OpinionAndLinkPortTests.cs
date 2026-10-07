using Marrow.Testing.MultiAccountSettings;

namespace Marrow.Core.Tests.Port.MultiAccount.Thread;

// MA-10 (step 3b, job 0; OD-0115 link=A, opinions=B): the seams for "link up" and "Ask others", and their fakes. Nothing here asks a real account.
[Trait("Feature", "MA-10")]
[Trait("Rule", "R-TEST-4")]
public sealed class OpinionAndLinkPortTests
{
    private static readonly Guid Chat = Guid.NewGuid();

    [Fact]
    public void Linking_keeps_the_order_it_was_done_in_and_a_second_link_changes_nothing()
    {
        var port = new FakeLinkPort();
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        var changed = 0;
        port.Changed += _ => changed++;
        Assert.Empty(port.Linked(Chat));
        port.Link(Chat, b);
        port.Link(Chat, a);
        port.Link(Chat, b);
        Assert.Equal([b, a], port.Linked(Chat));
        Assert.Equal(2, changed);
        Assert.Empty(port.Linked(Guid.NewGuid()));
    }

    [Fact]
    public void Unlinking_removes_only_that_account_and_an_account_that_was_not_linked_says_nothing()
    {
        var port = new FakeLinkPort();
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        port.Link(Chat, a);
        port.Link(Chat, b);
        var changed = 0;
        port.Changed += _ => changed++;
        port.Unlink(Chat, a);
        port.Unlink(Chat, a);
        Assert.Equal([b], port.Linked(Chat));
        Assert.Equal(1, changed);
        Assert.Equal([("link", Chat, a), ("link", Chat, b), ("unlink", Chat, a), ("unlink", Chat, a)], port.Calls);
    }

    [Fact]
    public async Task Asking_others_hands_the_picked_accounts_over_for_this_message_and_gives_a_round()
    {
        var port = new FakeOpinionsPort();
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        var round = await port.AskAsync(Chat, "Is this safe to merge?", [a, b]);
        var asked = Assert.Single(port.Asked);
        Assert.Equal((Chat, "Is this safe to merge?"), (asked.Thread, asked.Message));
        Assert.Equal([a, b], asked.Accounts);
        var shown = Assert.Single(port.Rounds(Chat));
        Assert.Equal(round, shown.Id);
        Assert.Equal([a, b], shown.Answers.Select(x => x.AccountId));
        Assert.False(shown.Combined);
        Assert.Empty(port.Rounds(Guid.NewGuid()));
    }

    [Fact]
    public async Task Asking_nobody_is_refused()
    {
        var port = new FakeOpinionsPort();
        await Assert.ThrowsAsync<ArgumentException>(() => port.AskAsync(Chat, "x", []));
        Assert.Empty(port.Asked);
    }

    [Fact]
    public async Task Combine_is_done_once_for_a_round_and_the_second_is_refused()
    {
        var port = new FakeOpinionsPort();
        var round = await port.AskAsync(Chat, "x", [Guid.NewGuid()]);
        var changed = 0;
        port.Changed += _ => changed++;
        await port.CombineAsync(Chat, round);
        Assert.True(Assert.Single(port.Rounds(Chat)).Combined);
        Assert.Equal(1, changed);
        await Assert.ThrowsAsync<InvalidOperationException>(() => port.CombineAsync(Chat, round));
        await Assert.ThrowsAsync<InvalidOperationException>(() => port.CombineAsync(Chat, Guid.NewGuid()));
        Assert.Equal([round], port.Combined);
    }

    [Fact]
    public async Task A_fake_answer_arrives_as_text_then_done_and_the_port_says_so_each_time()
    {
        var port = new FakeOpinionsPort();
        var a = Guid.NewGuid();
        var round = await port.AskAsync(Chat, "x", [a]);
        var changed = 0;
        port.Changed += _ => changed++;
        port.Stream(round, a, "Looks fine, ");
        port.Stream(round, a, "except the lock.");
        Assert.False(Assert.Single(Assert.Single(port.Rounds(Chat)).Answers).Done);
        port.Finish(round, a);
        var answer = Assert.Single(Assert.Single(port.Rounds(Chat)).Answers);
        Assert.Equal(("Looks fine, except the lock.", true), (answer.Text, answer.Done));
        Assert.Equal(3, changed);
    }
}
