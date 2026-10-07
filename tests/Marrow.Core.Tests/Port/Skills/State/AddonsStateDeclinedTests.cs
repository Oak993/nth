using Marrow.Core.Port.Skills.State;

namespace Marrow.Core.Tests.Port.Skills.State;

// Unit SK-7: "the per-chat declined list". ONDEMAND-PLAN.md 3.1: state.json holds "declined offers (per chat)"; 5.3 Guards: no offer when the item is
// "declined in this chat ('Don't ask again in this chat' is the decline's meaning)"; 5.4 `asked -> (no) declined`. A decline in one chat says nothing
// about another chat. Forgetting a chat's list when the chat goes is an ENGINEERING DECISION (ED-295), so the file cannot grow without end.
[Trait("Spec", "DL-7")]
public sealed class AddonsStateDeclinedTests
{
    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void A_decline_holds_in_its_chat_only_and_only_for_its_item()
    {
        var state = AddonsState.Empty.Decline("chat-1", "T3");
        Assert.True(state.IsDeclined("chat-1", "T3"));
        Assert.False(state.IsDeclined("chat-2", "T3"));
        Assert.False(state.IsDeclined("chat-1", "T1"));
        Assert.False(AddonsState.Empty.IsDeclined("chat-1", "T3"));
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void Declining_twice_is_the_same_as_once_and_each_chat_keeps_its_own_sorted_list()
    {
        var state = AddonsState.Empty.Decline("chat-2", "T3").Decline("chat-1", "T10").Decline("chat-2", "T1").Decline("chat-2", "T3");
        Assert.Equal(["chat-1", "chat-2"], state.Declined.Select(d => d.ChatId));
        Assert.Equal(["T1", "T3"], state.Declined.Single(d => d.ChatId == "chat-2").ItemIds);
        Assert.Equal(["T10"], state.Declined.Single(d => d.ChatId == "chat-1").ItemIds);
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void A_blank_chat_or_item_is_refused_and_leaves_the_state_alone()
    {
        var state = AddonsState.Empty.Decline("chat-1", "T3");
        var before = StateFixtures.Text(state);
        Assert.Throws<ArgumentException>(() => state.Decline("", "T3"));
        Assert.Throws<ArgumentException>(() => state.Decline(" ", "T3"));
        Assert.Throws<ArgumentException>(() => state.Decline("chat-1", ""));
        Assert.Throws<ArgumentException>(() => state.Decline("chat-1", "T 3"));
        Assert.Throws<ArgumentException>(() => state.IsDeclined("", "T3"));
        Assert.Equal(before, StateFixtures.Text(state));
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void Forgetting_a_chat_removes_its_list_and_only_its_list()
    {
        var state = AddonsState.Empty.Decline("chat-1", "T3").Decline("chat-2", "T3");
        var forgotten = state.ForgetChat("chat-1");
        Assert.False(forgotten.IsDeclined("chat-1", "T3"));
        Assert.True(forgotten.IsDeclined("chat-2", "T3"));
        Assert.Equal(StateFixtures.Text(state), StateFixtures.Text(state.ForgetChat("chat-9")));
        Assert.Throws<ArgumentException>(() => state.ForgetChat(""));
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void A_decline_does_not_touch_the_item_s_state_and_survives_a_round_trip()
    {
        var state = AddonsState.Empty.Transition("T1", ItemState.Downloading, "3.14.8").Decline("chat-1", "T1");
        Assert.Equal(ItemState.Downloading, state.Get("T1").State);
        var back = AddonsStateJson.Parse(StateFixtures.Text(state));
        Assert.True(back.IsDeclined("chat-1", "T1"));
    }
}
