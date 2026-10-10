using Avalonia.Controls;
using Marrow.Testing;
using Marrow.Core.Port;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// ED-NEW-frame-pacing: decorative motion advances exactly once per 60 Hz slot of the display's refreshes, evenly, on
// the same refresh for every ambient source, at any refresh rate; never on a timer.
[Trait("Decision", "ED-NEW-frame-pacing")]
public sealed class AmbientRefreshPacerTests
{
    // a tick comes after its vblank by 0.2-1.4 ms, every 37th by up to 1.9 ms (under half a refresh at 240 Hz)
    private static double Latency(long k)
    {
        var noise = Math.Sin(k * 12.9898) * 43758.5453;
        return .0002 + .0012 * (noise - Math.Floor(noise)) + (k % 37 == 0 ? .0005 : 0);
    }

    private static List<long> Run(double rate, double seconds, Func<long, bool>? present = null, double start = 100)
    {
        var pacer = new AmbientRefreshPacer(() => TimeSpan.FromSeconds(1 / rate));
        var beats = new List<long>();
        for (long k = 0; k < (long)Math.Round(rate * seconds); k++)
        {
            if (present is not null && !present(k)) continue;
            var now = start + k / rate + Latency(k);
            var key = (long)(now * 1e7);
            var track = pacer.Beat(key, now);
            var knob = pacer.Beat(key, now);
            Assert.Equal(track, knob); // one decision per refresh: the track and the knob share it
            if (track) beats.Add(k);
        }
        return beats;
    }

    [Theory]
    [InlineData(60.0)]
    [InlineData(120.0)]
    [InlineData(144.0)]
    [InlineData(165.0)]
    [InlineData(240.0)]
    public void Every_rate_advances_once_per_sixty_hertz_slot_evenly_spaced(double rate)
    {
        var beats = Run(rate, 3);
        Assert.Equal(180, beats.Count);
        var perSlot = rate / 60;
        var gaps = beats.Zip(beats.Skip(1), (a, b) => b - a).ToList();
        Assert.All(gaps, gap => Assert.InRange(gap, (long)Math.Floor(perSlot), (long)Math.Ceiling(perSlot)));
        AssertEven(gaps);
        // never more than 60 in any second of refreshes
        var window = (int)Math.Round(rate);
        for (var i = 0; i < beats.Count; i++) Assert.True(beats.Count(b => b >= beats[i] && b < beats[i] + window) <= 60);
    }

    /// <summary>Evenly spaced: any run of L consecutive slots spans the same number of refreshes as any other, to within one (never 2-1-2, never 3-3-2-2).</summary>
    internal static void AssertEven(IReadOnlyList<long> gaps)
    {
        for (var length = 1; length <= 12 && length <= gaps.Count; length++)
        {
            var spans = Enumerable.Range(0, gaps.Count - length + 1).Select(i => gaps.Skip(i).Take(length).Sum()).ToList();
            Assert.True(spans.Max() - spans.Min() <= 1, $"runs of {length} slots span {spans.Min()} to {spans.Max()} refreshes");
        }
    }

    [Fact]
    public void A_240_Hz_display_reporting_59_99_slots_keeps_four_refreshes_for_a_minute()
    {
        var rate = 239.96;
        var beats = Run(rate, 60);
        Assert.All(beats.Zip(beats.Skip(1), (a, b) => b - a), gap => Assert.Equal(4, gap));
        Assert.InRange(beats.Count, 3598, 3600);
    }

    [Fact]
    public void A_source_that_joins_later_lands_on_the_same_refreshes()
    {
        var rate = 240.0;
        var pacer = new AmbientRefreshPacer(() => TimeSpan.FromSeconds(1 / rate));
        var first = new List<long>(); var second = new List<long>();
        for (long k = 0; k < 720; k++)
        {
            var now = 5 + k / rate + Latency(k); var key = (long)(now * 1e7);
            if (pacer.Beat(key, now)) first.Add(k);
            if (k >= 7 && pacer.Beat(key, now)) second.Add(k); // the knob registers three refreshes into a slot
        }
        Assert.Equal(first.Where(k => k >= 7), second);
        Assert.All(second, k => Assert.Equal(0, k % 4));
    }

    [Fact]
    public void A_missed_refresh_returns_to_the_grid_without_a_double_update()
    {
        var beats = Run(240, 1, present: k => k is not (41 or 42 or 43 or 44 or 45));
        Assert.DoesNotContain(44L, beats);
        Assert.Contains(46L, beats); // the slot whose refresh was missed is drawn at the next refresh
        Assert.Contains(48L, beats); // and the next slot is on the grid again
        Assert.All(beats.Where(k => k > 48), k => Assert.Equal(0, k % 4));
    }

    [Fact]
    public void After_a_pause_the_first_refresh_is_a_slot()
    {
        var pacer = new AmbientRefreshPacer(() => TimeSpan.FromSeconds(1 / 240.0));
        Assert.True(pacer.Beat(1, 10)); Assert.False(pacer.Beat(2, 10 + 1 / 240.0));
        Assert.True(pacer.Beat(3, 11)); Assert.False(pacer.Beat(4, 11 + 1 / 240.0));
    }

    [Fact]
    public void Without_a_known_period_slots_stay_one_sixtieth_apart()
    {
        var pacer = new AmbientRefreshPacer(() => null);
        var beats = 0;
        for (var k = 0; k < 720; k++) if (pacer.Beat(k + 1, 3 + k / 240.0 + Latency(k))) beats++;
        Assert.InRange(beats, 179, 181);
    }

    [Fact]
    public void A_period_that_is_not_the_displays_never_runs_faster_than_sixty()
    {
        // DWM says 60 Hz; the window's ticks come at 240 Hz (another monitor): a few early ticks, then the 1/60 s grid
        var pacer = new AmbientRefreshPacer(() => TimeSpan.FromSeconds(1 / 60.0));
        var beats = new List<double>();
        for (var k = 0; k < 720; k++) { var now = 7 + k / 240.0 + Latency(k); if (pacer.Beat(k + 1, now)) beats.Add(now); }
        Assert.InRange(beats.Count, 179, 184);
        Assert.All(beats.Skip(5).Zip(beats.Skip(6), (a, b) => b - a), gap => Assert.InRange(gap, .008, .026));
    }

    [Collection("Headless")]
    public sealed class Controller(HeadlessSession headless)
    {
        [Fact]
        public void Pacing_waits_on_refreshes_never_a_timer_runs_interactions_at_display_rate_and_stops_when_idle_or_unseen() => headless.OnUiThread(() =>
        {
            var clock = new ManualTimeProvider(); using var time = SwiftClock.Provider.Override(clock);
            var state = new AmbientWindowState(new Window(), true);
            state.Touch(0);
            using var ambient = new AmbientMotionController(state) { Pacer = new AmbientRefreshPacer(() => TimeSpan.FromSeconds(1 / 240.0)) };
            var paced = new List<AmbientMotionController.Paced>();
            for (var k = 0; k < 16; k++) paced.Add(ambient.Pace(k + 1, k / 240.0, false));
            Assert.Equal(4, paced.Count(p => p == AmbientMotionController.Paced.Go));
            Assert.Equal(12, paced.Count(p => p == AmbientMotionController.Paced.Wait));
            Assert.Equal(0, clock.ActiveTimers);
            Assert.True(ambient.Request(16 / 240.0, false));
            for (var k = 16; k < 24; k++) Assert.Equal(AmbientMotionController.Paced.Go, ambient.Pace(k + 1, k / 240.0, true));
            // after the interaction the slots go on from the count: refreshes 24 and 28 are slots, 25-27 are not
            Assert.Equal(AmbientMotionController.Paced.Go, ambient.Pace(25, 24 / 240.0, false));
            Assert.Equal(AmbientMotionController.Paced.Wait, ambient.Pace(26, 25 / 240.0, false));
            Assert.Equal(AmbientMotionController.Paced.Stop, ambient.Pace(27, 20.7, false));
            Assert.False(ambient.Request(20.7, false));
            state.Touch(21);
            Assert.True(ambient.Request(21, false));
            state.SetAvailable(false);
            Assert.Equal(AmbientMotionController.Paced.Stop, ambient.Pace(28, 21.1, true));
            Assert.Equal(0, clock.ActiveTimers);
        });
    }
}
