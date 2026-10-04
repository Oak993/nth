using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.UI.Tests.Markdown;

// Source: DroppyCode/UI/Markdown/StreamVeil.swift (B3 52bc792), :174-181.
// UI-THEME-047's notes: "The shared text clock is pinned to a preferred 30 frames a second (minimum 24, maximum 60)". The reference videos hold no
// frame that shows a veil's cadence, so under OD-0098 the Swift is the proof: `preferredFrameRateRange = CAFrameRateRange(minimum: 24, maximum: 60,
// preferred: 30)`. ENGINEERING DECISION (the framework's documented behaviour, labelled): a display link asked for that range ticks at its preferred
// rate when the display allows it, never above the maximum and never below the minimum. The port's clock ticks at the preferred 30 whatever the
// display's rate, which lies inside [24, 60]; this measures that over a second at three display rates.
public sealed class StreamVeilClockRangeTests
{
    [Theory]
    [InlineData(60)]
    [InlineData(120)]
    [InlineData(240)]
    [Trait("Req", "UI-THEME-047")] // StreamVeil.swift:174-181 preferredFrameRateRange minimum 24, maximum 60, preferred 30
    public void The_veil_clock_ticks_at_its_preferred_30_a_second_inside_the_24_to_60_range_at_any_display_rate(int displayHz)
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var scrollScope = ScrollActivity.Shared.Override(new ScrollActivity());
        VeilClock? clock = null;
        runtime.OnMain(() => { clock = new VeilClock(); clock.Run(clock.Now.AddSeconds(30)); });
        var ticks = 0;
        var last = runtime.OnMain(() => clock!.Now);
        var step = 1000.0 / displayHz;
        var elapsed = 0.0;
        for (var i = 0; i < displayHz * 2; i++) // two seconds of display frames
        {
            elapsed += step;
            runtime.AdvanceMilliseconds((int)Math.Round(elapsed) - (int)Math.Round(elapsed - step));
            runtime.OnMain(() => frames.Advance(TimeSpan.FromMilliseconds(step)));
            var now = runtime.OnMain(() => clock!.Now);
            if (now != last) { ticks++; last = now; }
        }
        var perSecond = ticks / 2.0;
        Assert.InRange(perSecond, 24, 60); // inside the range the Swift asks for
        Assert.InRange(perSecond, 29, 31); // and at its preferred 30
    }
}
