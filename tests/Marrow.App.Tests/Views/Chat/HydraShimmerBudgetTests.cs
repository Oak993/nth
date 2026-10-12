using Avalonia.Controls;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Chat;

// ED-NEW-lean-11 (lean pass L11): the bytes the Hydra shimmer (TimelineRows.swift:480-521) allocates per tick of its 30 Hz timeline. Each tick used to
// make a new gradient brush, its stops, three colours and a translation; now the band keeps its brush and its translation and changes their values.
// Manual frame source, 30 warm-up ticks, then 30 measured; Debug build.
[Collection("Headless")]
public sealed class HydraShimmerBudgetTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-NEW-lean-11")]
    public void A_shimmer_tick_allocates_only_what_the_timeline_does() => headless.OnUiThread(() =>
    {
        using var h = TimelineRowsPrSupport.Harness(); // manual time: the timeline draws when its 1/30 s is due on the clock
        var frames = new ManualFrameSource();
        using var seam = FrameClock.Source.Override(frames);
        var shimmer = new HydraShimmer().Body(new TextBlock { Text = "Cooking the plan", Width = 240 });
        var window = TimelineRowsPrSupport.Show(shimmer);
        var band = (Border)((Panel)((Panel)shimmer).Children[1]).Children[0];
        double Offset() => band.RenderTransform is Avalonia.Media.TranslateTransform move ? move.X : double.NaN;
        var moves = 0;
        long bytes = 0;
        for (var tick = 0; tick < 60; tick++)
        {
            h.Main.AdvanceMilliseconds(34);
            var offset = Offset();
            var before = GC.GetAllocatedBytesForCurrentThread();
            frames.Advance(TimeSpan.FromMilliseconds(34));
            var after = GC.GetAllocatedBytesForCurrentThread();
            if (Offset() != offset) moves++;
            if (tick >= 30) bytes += after - before;
        }
        bytes /= 30;
        TimelineRowsPrSupport.Close(window);
        Assert.True(moves >= 50, $"the band moves at every tick ({moves} of 60)");
        // Measured on the unchanged code (2026-10-12, Debug): 9,048 B per tick (limit was 13,600). After: 2,672 B, the timeline's and the moving band's
        // change notifications. Limit about 1.3x.
        Assert.True(bytes <= 3_480, $"HydraShimmer: {bytes} B per tick");
    });
}
