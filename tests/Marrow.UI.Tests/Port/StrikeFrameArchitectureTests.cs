using System.Reflection;
using Marrow.UI.Port;

namespace Marrow.UI.Tests.Port;

// ST-0: ED-248 review 2 / strike-port RISKS R-01 and R-04.
public sealed class StrikeFrameArchitectureTests
{
    [Fact]
    [Trait("Decision", "ED-248")]
    public void A_cached_Dwm_vblank_cannot_stop_a_running_timeline()
    {
        var type = typeof(FrameClock).Assembly.GetType("Marrow.UI.Port.DisplayRefreshCounter")!;
        var method = type.GetMethod("ObserveVblank", [typeof(long), typeof(long), typeof(long)]);
        Assert.NotNull(method);
        var counter = Activator.CreateInstance(type)!;
        const long start = 656_940_147_206, period = 41_672;
        for (var i = 0; i < 14_400; i++)
            Assert.Equal((long)i, (long)method.Invoke(counter, [start, period, start + i * period])!);
    }
    [Fact]
    [Trait("Decision", "ED-248")]
    public void Display_period_estimate_changes_do_not_rescale_the_absolute_uptime()
    {
        var type = typeof(FrameClock).Assembly.GetType("Marrow.UI.Port.DisplayRefreshCounter")!;
        var method = type.GetMethod("ObserveVblank", [typeof(long), typeof(long)]);
        Assert.NotNull(method);
        var counter = Activator.CreateInstance(type)!;
        const long start = 656_940_147_206;
        long[] periods = [41_672, 41_673, 41_671, 41_672, 41_673];
        for (var i = 0; i < periods.Length; i++)
            Assert.Equal((long)i, (long)method.Invoke(counter, [start + i * 41_672L, periods[i]])!);
    }
    [Fact]
    [Trait("Decision", "ED-248")]
    public void Vblank_identity_uses_the_display_timestamp_rather_than_the_query_counter()
    {
        var type = typeof(FrameClock).Assembly.GetType("Marrow.UI.Port.DisplayRefreshCounter")!;
        var method = type.GetMethod("VblankNumber", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        // Two readers observe the same physical vblank. The following delayed callback
        // still advances by its actual display periods, independent of query count.
        long[] stamps = [1_000_000, 1_000_000, 1_041_667, 1_083_334, 1_083_334, 1_166_668];
        var numbers = stamps.Select(t => (long)method.Invoke(null, [t, 41_667L])!).ToArray();
        Assert.Equal([24L, 24L, 25L, 26L, 26L, 28L], numbers);
    }

    [Fact]
    [Trait("Decision", "ED-248")]
    public void The_production_source_provides_a_retained_render_thread_source()
    {
        var assembly = typeof(FrameClock).Assembly;
        var source = assembly.GetType("Marrow.UI.Port.RenderFrameSource");
        Assert.NotNull(source);
        Assert.True(typeof(IFrameSource).IsAssignableFrom(source));
        Assert.NotNull(typeof(AvaloniaFrameSource).GetMethod("RenderFrames"));
    }

    [Fact]
    [Trait("Decision", "ED-248")]
    public void Display_refresh_numbers_ignore_late_tick_gap_rounding()
    {
        var type = typeof(FrameClock).Assembly.GetType("Marrow.UI.Port.DisplayRefreshCounter");
        Assert.NotNull(type);
        var counter = Activator.CreateInstance(type)!;
        var observe = type.GetMethod("Observe", BindingFlags.Public | BindingFlags.Instance)!;
        // Actual vblank numbers: a tick 0.6 refresh late and the following early tick belong to
        // consecutive refreshes. Rounded gaps would add two then one, drifting by a refresh.
        long[] vblank = [100, 101, 102, 103, 110, 110, 111];
        long[] expected = [0, 1, 2, 3, 10, 10, 11];
        for (var i = 0; i < vblank.Length; i++)
            Assert.Equal(expected[i], (long)observe.Invoke(counter, [vblank[i]])!);
    }
}
