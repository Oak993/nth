// PORT-ONLY: Esc three times (OD-0145, ED-NEW-pcctl-12 item 2, ED-NEW-pcctl-15; DESIGN.md 10.2 path 5, 10.11): three
// within 1 s stop; auto-repeat, injected Escs and Escs an IME consumes are excluded; another key resets; the first press
// must be in a Tactus window or the overlay.
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.Core.Tests.Port.PcControl.Pill;

[Trait("Decision", "ED-NEW-pcctl-15")]
public sealed class EscTripleCounterTests
{
    private static readonly PcKeyDown Esc = new(Escape: true, Repeat: false, Injected: false, ImeConsumed: false, InTactusWindow: true);

    [Fact]
    public void Three_Escs_within_one_second_stop_and_the_count_starts_over()
    {
        var counter = new EscTripleCounter();

        Assert.False(counter.Key(0, Esc));
        Assert.False(counter.Key(400, Esc));
        Assert.True(counter.Key(1_000, Esc));
        Assert.Equal(0, counter.Count);
        Assert.False(counter.Key(1_100, Esc));
    }

    [Fact]
    public void Escs_spread_over_more_than_a_second_do_not_stop()
    {
        var counter = new EscTripleCounter();

        counter.Key(0, Esc);
        counter.Key(600, Esc);
        Assert.False(counter.Key(1_001, Esc));
        // The late one began a new count.
        Assert.Equal(1, counter.Count);
    }

    [Fact]
    public void Repeats_and_injected_Escs_neither_count_nor_reset()
    {
        var counter = new EscTripleCounter();
        counter.Key(0, Esc);
        counter.Key(100, Esc);

        Assert.False(counter.Key(150, Esc with { Repeat = true }));
        Assert.False(counter.Key(200, Esc with { Injected = true }));
        Assert.False(counter.Key(220, new PcKeyDown(false, false, true, false, true)));
        Assert.Equal(2, counter.Count);
        Assert.True(counter.Key(300, Esc));
    }

    [Fact]
    public void Another_key_or_an_IME_Esc_resets_the_count()
    {
        var counter = new EscTripleCounter();
        counter.Key(0, Esc);
        counter.Key(100, Esc);
        counter.Key(150, new PcKeyDown(false, false, false, false, true));
        Assert.False(counter.Key(200, Esc));

        counter.Key(300, Esc);
        counter.Key(350, Esc with { ImeConsumed = true });
        Assert.Equal(0, counter.Count);
    }

    [Fact]
    public void The_first_Esc_must_land_in_a_Tactus_window_and_the_later_ones_need_not()
    {
        var counter = new EscTripleCounter();
        var elsewhere = Esc with { InTactusWindow = false };

        Assert.False(counter.Key(0, elsewhere));
        Assert.Equal(0, counter.Count);
        counter.Key(100, Esc);
        counter.Key(200, elsewhere);
        Assert.True(counter.Key(300, elsewhere));
    }
}
