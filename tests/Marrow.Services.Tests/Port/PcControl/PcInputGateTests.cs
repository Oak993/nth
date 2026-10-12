using Marrow.Services.Port.PcControl;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-17; DESIGN.md 2.7 "The gate"). The still moment a borrow waits for:
// the thresholds to the millisecond, the IME's longer keyboard wait, unseen input, the order of the blocks, what
// "Do it now" passes, and the words the pill shows while it waits.
[Trait("Decision", "ED-NEW-pcctl-17")]
public sealed class PcInputGateTests
{
    private const long Now = 100_000;

    [Theory]
    [InlineData(349, PcGateBlock.Pointer)]
    [InlineData(350, PcGateBlock.None)]
    public void The_pointer_must_be_still_350_ms(int ago, PcGateBlock block)
    {
        var state = Still() with { LastPointerMs = Now - ago };

        Assert.Equal(block, PcInputGate.Check(state, Now, PcForegroundProtection.None).Block);
    }

    [Theory]
    [InlineData(false, 699, PcGateBlock.Keys)]
    [InlineData(false, 700, PcGateBlock.None)]
    [InlineData(true, 1_999, PcGateBlock.Keys)]
    [InlineData(true, 2_000, PcGateBlock.None)]
    public void The_keyboard_must_be_still_700_ms_or_2_s_with_an_IME_language(bool ime, int ago, PcGateBlock block)
    {
        var state = Still() with { LastKeyMs = Now - ago, ImeLanguage = ime };

        Assert.Equal(block, PcInputGate.Check(state, Now, PcForegroundProtection.None).Block);
    }

    [Theory]
    [InlineData(699, PcGateBlock.Unseen)]
    [InlineData(700, PcGateBlock.None)]
    public void Input_the_hooks_did_not_see_counts_as_typing(int ago, PcGateBlock block)
    {
        var state = Still() with { LastUnseenMs = Now - ago };

        Assert.Equal(block, PcInputGate.Check(state, Now, PcForegroundProtection.None).Block);
    }

    [Fact]
    public void A_quiet_healthy_desktop_opens_the_gate()
    {
        Assert.True(PcInputGate.Check(Still(), Now, PcForegroundProtection.None).Open);
        Assert.True(PcInputGate.Check(new PcPhysicalSnapshot { Healthy = true }, Now, PcForegroundProtection.None).Open);
    }

    [Fact]
    public void Blocks_come_in_order_protected_unhealthy_held_composition_then_stillness()
    {
        var busy = Still() with { Healthy = false, HeldKeys = 1, Composition = 0x10, LastPointerMs = Now, LastKeyMs = Now };

        Assert.Equal(PcGateBlock.Protected, PcInputGate.Check(busy, Now, PcForegroundProtection.Elevated).Block);
        Assert.Equal(PcGateBlock.Unhealthy, PcInputGate.Check(busy, Now, PcForegroundProtection.None).Block);
        Assert.Equal(PcGateBlock.Held, PcInputGate.Check(busy with { Healthy = true }, Now, PcForegroundProtection.None).Block);
        Assert.Equal(PcGateBlock.Held, PcInputGate.Check(Still() with { HeldButtons = 1 }, Now, PcForegroundProtection.None).Block);
        Assert.Equal(PcGateBlock.Composition, PcInputGate.Check(busy with { Healthy = true, HeldKeys = 0 }, Now, PcForegroundProtection.None).Block);
        Assert.Equal(PcGateBlock.Pointer, PcInputGate.Check(busy with { Healthy = true, HeldKeys = 0, Composition = 0 }, Now, PcForegroundProtection.None).Block);
    }

    [Fact]
    public void Do_it_now_passes_only_the_stillness()
    {
        var moving = Still() with { LastPointerMs = Now, LastKeyMs = Now, LastUnseenMs = Now };

        Assert.True(PcInputGate.Check(moving, Now, PcForegroundProtection.None, doItNow: true).Open);
        Assert.Equal(PcGateBlock.Held, PcInputGate.Check(moving with { HeldKeys = 1 }, Now, PcForegroundProtection.None, doItNow: true).Block);
        Assert.Equal(PcGateBlock.Composition, PcInputGate.Check(moving with { Composition = 0x10 }, Now, PcForegroundProtection.None, doItNow: true).Block);
        Assert.Equal(PcGateBlock.Unhealthy, PcInputGate.Check(moving with { Healthy = false }, Now, PcForegroundProtection.None, doItNow: true).Block);
        Assert.Equal(PcGateBlock.Protected, PcInputGate.Check(moving, Now, PcForegroundProtection.Fullscreen, doItNow: true).Block);
    }

    [Theory]
    [InlineData(PcForegroundProtection.Elevated, "Waiting until you leave the admin window")]
    [InlineData(PcForegroundProtection.UiAccess, "Waiting until you leave the admin window")]
    [InlineData(PcForegroundProtection.SecureDesktop, "Waiting until Windows' prompt closes")]
    [InlineData(PcForegroundProtection.Locked, "Waiting until Windows' prompt closes")]
    [InlineData(PcForegroundProtection.Fullscreen, "Waiting until you leave the full-screen app")]
    [InlineData(PcForegroundProtection.Presentation, "Waiting until you leave the full-screen app")]
    [InlineData(PcForegroundProtection.Busy, "Waiting until you leave the full-screen app")]
    public void A_protected_foreground_says_what_it_waits_for(PcForegroundProtection protection, string words)
    {
        Assert.Equal(words, PcInputGate.Check(Still(), Now, protection).Waiting);
    }

    [Fact]
    public void Stillness_and_health_waits_say_their_own_words()
    {
        Assert.Equal("Hands off for a moment", PcInputGate.Check(Still() with { LastKeyMs = Now }, Now, PcForegroundProtection.None).Waiting);
        Assert.Equal("Hands off for a moment", PcInputGate.Check(Still() with { HeldButtons = 1 }, Now, PcForegroundProtection.None).Waiting);
        Assert.Equal(
            "Real mouse and keyboard off for a moment: restarting the input check",
            PcInputGate.Check(Still() with { Healthy = false }, Now, PcForegroundProtection.None).Waiting);
    }

    private static PcPhysicalSnapshot Still() => new()
    {
        Healthy = true,
        LastPointerMs = Now - 5_000,
        LastKeyMs = Now - 5_000,
    };
}
