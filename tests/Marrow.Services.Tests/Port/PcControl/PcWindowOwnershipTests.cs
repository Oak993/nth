using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0141, OD-0144, ED-NEW-pcctl-8; DESIGN.md 2.10). One window, one chat: the chat whose
// action used a window first holds it until its turn ends or it has left the window alone for a minute; another chat
// learns who holds it (busy_window). Several windows are held by several chats at once.
[Trait("Decision", "ED-NEW-pcctl-8")]
public sealed class PcWindowOwnershipTests
{
    [Fact]
    public void The_first_chat_holds_the_window_and_another_chat_is_told_who()
    {
        var clock = new PcFakeClock();
        var holds = new PcWindowOwnership(clock);
        Assert.Null(holds.Holder(0x1a, "chat-1"));
        Assert.Null(holds.Claim(0x1a, "chat-1", "the main chat"));
        Assert.Null(holds.Claim(0x1a, "chat-1", "the main chat"));
        Assert.Null(holds.Holder(0x1a, "chat-1"));
        Assert.Equal("the main chat", holds.Holder(0x1a, "head-2"));
        Assert.Equal("the main chat", holds.Claim(0x1a, "head-2", "head Research"));
        // Other windows stay free for other chats: several apps at once.
        Assert.Null(holds.Claim(0x2b, "head-2", "head Research"));
        Assert.Equal("head Research", holds.Holder(0x2b, "chat-1"));
    }

    [Fact]
    public void A_hold_lapses_after_a_minute_unused_and_each_use_renews_it()
    {
        var clock = new PcFakeClock();
        var holds = new PcWindowOwnership(clock);
        holds.Claim(0x1a, "chat-1", "the main chat");
        clock.NowMs = 50_000;
        holds.Claim(0x1a, "chat-1", "the main chat");
        clock.NowMs = 100_000;
        Assert.Equal("the main chat", holds.Holder(0x1a, "head-2"));
        clock.NowMs = 50_000 + PcWindowOwnership.IdleMs;
        Assert.Null(holds.Holder(0x1a, "head-2"));
        Assert.Null(holds.Claim(0x1a, "head-2", "head Research"));
        Assert.Equal("head Research", holds.Holder(0x1a, "chat-1"));
    }

    [Fact]
    public void The_end_of_a_chats_turn_frees_every_window_it_held_and_only_those()
    {
        var clock = new PcFakeClock();
        var holds = new PcWindowOwnership(clock);
        holds.Claim(0x1a, "chat-1", "the main chat");
        holds.Claim(0x2b, "chat-1", "the main chat");
        holds.Claim(0x3c, "head-2", "head Research");
        holds.Release("chat-1");
        Assert.Null(holds.Holder(0x1a, "head-2"));
        Assert.Null(holds.Holder(0x2b, "head-2"));
        Assert.Equal("head Research", holds.Holder(0x3c, "chat-1"));
    }
}
