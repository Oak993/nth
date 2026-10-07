using Marrow.Testing;
using Marrow.UI.Tests.Fixtures;
using Marrow.UI.Theme.Wallpapers;

namespace Marrow.UI.Tests.Theme.Wallpapers;

public sealed class WallpaperWakeSourceTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 5, 12, 0, 30, TimeSpan.Zero);

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void With_Dynamic_off_nothing_is_armed_and_nothing_wakes()
    {
        var clock = new ManualTimeProvider(Start); var wakes = 0;
        using var source = new WallpaperWakeSource(new(clock, new FixedWallpaperAccent("#005fb8"), new FixedWallpaperPosition()), () => wakes++);
        Assert.Equal(0, clock.ActiveTimers); source.Update(false); Assert.Equal(0, clock.ActiveTimers);
        clock.Advance(TimeSpan.FromHours(3)); Assert.Equal(0, wakes);
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void Dynamic_wakes_exactly_at_each_minute_boundary_of_the_local_clock()
    {
        var clock = new ManualTimeProvider(Start); var wakes = 0;
        using var source = new WallpaperWakeSource(new(clock, new FixedWallpaperAccent("#005fb8"), new FixedWallpaperPosition()), () => wakes++);
        source.Update(true); Assert.Equal(1, clock.ActiveTimers);
        clock.Advance(TimeSpan.FromSeconds(29)); Assert.Equal(0, wakes);
        clock.Advance(TimeSpan.FromSeconds(1)); Assert.Equal(1, wakes);
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 12, 1, 0, TimeSpan.Zero), clock.GetLocalNow());
        clock.Advance(TimeSpan.FromSeconds(59)); Assert.Equal(1, wakes);
        clock.Advance(TimeSpan.FromSeconds(1)); Assert.Equal(2, wakes);
        clock.Advance(TimeSpan.FromMinutes(10)); Assert.Equal(12, wakes); Assert.Equal(1, clock.ActiveTimers);
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void Turning_Dynamic_off_or_disposing_stops_the_wakes_and_leaves_no_timer()
    {
        var clock = new ManualTimeProvider(Start); var wakes = 0;
        var source = new WallpaperWakeSource(new(clock, new FixedWallpaperAccent("#005fb8"), new FixedWallpaperPosition()), () => wakes++);
        source.Update(true); source.Update(true); Assert.Equal(1, clock.ActiveTimers);
        clock.Advance(TimeSpan.FromSeconds(31)); Assert.Equal(1, wakes);
        source.Update(false); Assert.Equal(0, clock.ActiveTimers); clock.Advance(TimeSpan.FromHours(1)); Assert.Equal(1, wakes);
        source.Update(true); Assert.Equal(1, clock.ActiveTimers); source.Dispose(); Assert.Equal(0, clock.ActiveTimers);
        clock.Advance(TimeSpan.FromHours(1)); Assert.Equal(1, wakes);
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void A_change_of_the_accent_wakes_at_once_with_no_timer_and_stops_after_dispose()
    {
        var clock = new ManualTimeProvider(Start); var accent = new FixedWallpaperAccent("#005fb8"); var wakes = 0;
        var source = new WallpaperWakeSource(new(clock, accent, new FixedWallpaperPosition()), () => wakes++);
        accent.Hex = "#f7630c"; Assert.Equal(1, wakes); accent.Hex = "#f7630c"; Assert.Equal(1, wakes); Assert.Equal(0, clock.ActiveTimers);
        source.Dispose(); accent.Hex = "#107c10"; Assert.Equal(1, wakes);
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")]
    public void The_system_accent_raises_Changed_only_when_the_colour_differs()
    {
        var accent = new SystemWallpaperAccent(); var changes = 0; accent.Changed += () => changes++;
        accent.Update(0x00, 0x5f, 0xb8); Assert.Equal(0, changes); Assert.Equal("#005fb8", accent.Hex);
        accent.Update(0xf7, 0x63, 0x0c); Assert.Equal(1, changes); Assert.Equal("#f7630c", accent.Hex);
        accent.Update(0xf7, 0x63, 0x0c); Assert.Equal(1, changes);
        accent.Update(0x00, 0x5f, 0xb8); Assert.Equal(2, changes);
    }
}
