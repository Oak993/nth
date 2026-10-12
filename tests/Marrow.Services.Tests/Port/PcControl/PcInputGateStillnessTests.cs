// PORT-ONLY: Tactus, how far the user is into the stillness the gate waits for (ED-NEW-pcctl-38; DESIGN.md 4.5
// "Hands-off behaviour": the ring's fill speed shows the threshold, 350 ms pointer, 700 ms keyboard). The least of the
// pointer's, the keys' and the unseen input's parts; 2 s for keys with an IME language; nothing while a key or button is
// held, a composition is open or the hooks are unhealthy; full when no input was ever seen.
using Marrow.Services.Port.PcControl;

namespace Marrow.Services.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-38")]
public sealed class PcInputGateStillnessTests
{
    private const long Now = 100_000;

    private static PcPhysicalSnapshot Still(long? pointer = null, long? key = null, long? unseen = null) =>
        new() { Healthy = true, LastPointerMs = pointer, LastKeyMs = key, LastUnseenMs = unseen };

    [Fact]
    public void The_fill_is_the_least_part_of_each_threshold()
    {
        Assert.Equal(1, PcInputGate.Stillness(Still(), Now));
        Assert.Equal(0.5, PcInputGate.Stillness(Still(pointer: Now - 175), Now), 9);
        Assert.Equal(0.5, PcInputGate.Stillness(Still(pointer: Now - 1_000, key: Now - 350), Now), 9);
        Assert.Equal(0.25, PcInputGate.Stillness(Still(pointer: Now - 175, key: Now - 175), Now), 9);
        Assert.Equal(0.1, PcInputGate.Stillness(Still(unseen: Now - 70), Now), 9);
        Assert.Equal(0.175, PcInputGate.Stillness(Still(key: Now - 350) with { ImeLanguage = true }, Now), 9);
        Assert.Equal(1, PcInputGate.Stillness(Still(pointer: Now - 5_000, key: Now - 5_000), Now));
        Assert.Equal(0, PcInputGate.Stillness(Still(pointer: Now + 50), Now));
    }

    [Fact]
    public void Nothing_fills_while_a_key_or_button_is_held_a_composition_is_open_or_the_hooks_are_unhealthy()
    {
        Assert.Equal(0, PcInputGate.Stillness(Still() with { HeldKeys = 1 }, Now));
        Assert.Equal(0, PcInputGate.Stillness(Still() with { HeldButtons = 1 }, Now));
        Assert.Equal(0, PcInputGate.Stillness(Still() with { Composition = 7 }, Now));
        Assert.Equal(0, PcInputGate.Stillness(Still() with { Healthy = false }, Now));
    }
}
