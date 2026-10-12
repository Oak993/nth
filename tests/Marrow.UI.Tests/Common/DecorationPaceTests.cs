using Marrow.Core.Port;
using Marrow.UI.Common;
using Marrow.UI.Tests.Port;

namespace Marrow.UI.Tests.Common;

// ED-NEW-lean-8 (review): the 60 Hz decorative cap of the main-thread decorations (the working spinner, the breathing ring,
// the Hydra charge) at the refresh rates a PC has, with frame timing jitter: exactly one repaint per 60 Hz slot, evenly
// spaced, none dropped at 60 Hz or on a 60.1 Hz panel; a pause and Reset start the count again. Pure: the frames' times and
// the display's period are given (no window, no wall clock).
[Trait("Decision", "ED-NEW-lean-8")]
public sealed class DecorationPaceTests
{
    // a main-thread frame comes after its vblank by 0.2-1.4 ms, every 37th by up to 1.9 ms (under half a refresh at 240 Hz)
    private static double Latency(long k)
    {
        var noise = Math.Sin(k * 12.9898) * 43758.5453;
        return .0002 + .0012 * (noise - Math.Floor(noise)) + (k % 37 == 0 ? .0005 : 0);
    }

    private static ContinuousInstant At(double seconds) => new((long)Math.Round(seconds * TimeSpan.TicksPerSecond));

    /// <summary>The frames (by refresh number) that repaint, of <paramref name="seconds"/> of a display at <paramref name="rate"/>.</summary>
    private static List<long> Run(double rate, double seconds, Func<long, double>? latency = null, double? period = null)
    {
        using var reported = DecorationPace.Period.Override(() => TimeSpan.FromSeconds(period ?? 1 / rate));
        var pace = new DecorationPace();
        var due = new List<long>();
        for (long k = 0; k < (long)Math.Round(rate * seconds); k++)
            if (pace.Due(At(100 + k / rate + (latency ?? Latency)(k)))) due.Add(k);
        return due;
    }

    [Theory]
    [InlineData(60.0)]
    [InlineData(120.0)]
    [InlineData(144.0)]
    [InlineData(165.0)]
    [InlineData(240.0)]
    public void Every_rate_repaints_once_per_sixty_hertz_slot_evenly_spaced(double rate)
    {
        var due = Run(rate, 3);
        Assert.Equal(180, due.Count);
        var perSlot = rate / 60;
        var gaps = due.Zip(due.Skip(1), (a, b) => b - a).ToList();
        Assert.All(gaps, gap => Assert.InRange(gap, (long)Math.Floor(perSlot), (long)Math.Ceiling(perSlot)));
        AmbientRefreshPacerTests.AssertEven(gaps);
    }

    [Fact]
    public void At_60_Hz_every_frame_repaints_though_the_first_comes_late_and_the_rest_jitter()
    {
        // the first frame of the run 5 ms late, the rest anywhere from on time to 6 ms late: under half a refresh
        var due = Run(60, 60, k => k == 0 ? .005 : .006 * Math.Abs(Math.Sin(k * 7.31)));
        Assert.Equal(3600, due.Count);
    }

    [Fact]
    public void A_60_point_1_Hz_panel_repaints_every_frame_for_a_minute()
    {
        var due = Run(60.1, 60);
        Assert.Equal((int)Math.Round(60.1 * 60), due.Count);
    }

    [Fact]
    public void A_pause_starts_the_count_again_at_the_frame_after_it()
    {
        using var reported = DecorationPace.Period.Override(() => TimeSpan.FromSeconds(1 / 120.0));
        var pace = new DecorationPace();
        var due = new List<long>();
        for (long k = 0; k < 120; k++) if (pace.Due(At(100 + k / 120.0 + Latency(k)))) due.Add(k);
        // a 0.5 s pause (the decoration was scrolled away or the window hidden), then frames again
        for (long k = 180; k < 300; k++) if (pace.Due(At(100 + k / 120.0 + Latency(k)))) due.Add(k);
        Assert.Contains(180L, due); // the first frame after the pause repaints
        var after = due.Where(k => k >= 180).ToList();
        Assert.All(after.Zip(after.Skip(1), (a, b) => b - a), gap => Assert.Equal(2, gap));
        Assert.Equal(60, after.Count);
    }

    [Fact]
    public void Reset_makes_the_next_frame_repaint()
    {
        using var reported = DecorationPace.Period.Override(() => TimeSpan.FromSeconds(1 / 240.0));
        var pace = new DecorationPace();
        Assert.True(pace.Due(At(100)));
        Assert.False(pace.Due(At(100 + 1 / 240.0)));
        pace.Reset();
        Assert.True(pace.Due(At(100 + 2 / 240.0))); // a new run: its first frame
        Assert.False(pace.Due(At(100 + 3 / 240.0)));
        Assert.False(pace.Due(At(100 + 4 / 240.0)));
        Assert.False(pace.Due(At(100 + 5 / 240.0)));
        Assert.True(pace.Due(At(100 + 6 / 240.0))); // four refreshes on
    }

    [Fact]
    public void Without_a_known_refresh_period_a_240_Hz_display_repaints_sixty_times_a_second()
    {
        using var reported = DecorationPace.Period.Override(() => null);
        var pace = new DecorationPace();
        var due = 0;
        for (long k = 0; k < 240 * 3; k++) if (pace.Due(At(100 + k / 240.0 + Latency(k)))) due++;
        Assert.InRange(due, 179, 181);
    }
}
