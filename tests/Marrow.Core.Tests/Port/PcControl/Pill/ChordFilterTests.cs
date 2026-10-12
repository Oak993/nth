// PORT-ONLY: the pause chord's filter (OD-0145, ED-NEW-pcctl-15; DESIGN.md 10.1, 10.11): auto-repeat, injected presses and
// presses under 400 ms apart toggle nothing; it resumes only when every live app is paused.
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.Core.Tests.Port.PcControl.Pill;

[Trait("Decision", "ED-NEW-pcctl-15")]
public sealed class ChordFilterTests
{
    [Fact]
    public void Auto_repeat_and_injected_presses_count_for_nothing()
    {
        var filter = new ChordFilter();

        Assert.False(filter.Accept(0, repeat: true, injected: false));
        Assert.False(filter.Accept(1_000, repeat: false, injected: true));
        Assert.True(filter.Accept(2_000, repeat: false, injected: false));
    }

    [Fact]
    public void Presses_under_400_ms_apart_count_once()
    {
        var filter = new ChordFilter();

        Assert.True(filter.Accept(1_000, false, false));
        Assert.False(filter.Accept(1_399, false, false));
        // Measured from the press before, so a burst counts once.
        Assert.False(filter.Accept(1_700, false, false));
        Assert.True(filter.Accept(2_100, false, false));
    }

    [Theory]
    [InlineData(2, 2, false, true)]
    [InlineData(2, 1, false, false)]
    [InlineData(2, 0, true, false)]
    [InlineData(0, 0, true, true)]
    [InlineData(0, 0, false, false)]
    public void The_chord_resumes_only_when_every_live_app_is_paused(int live, int paused, bool latched, bool resumes)
    {
        Assert.Equal(resumes, ChordFilter.Resumes(live, paused, latched));
    }
}
