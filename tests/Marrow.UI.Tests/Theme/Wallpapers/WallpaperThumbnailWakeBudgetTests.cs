using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Marrow.UI.Theme.Wallpapers;

namespace Marrow.UI.Tests.Theme.Wallpapers;

// ED-NEW-smooth2-2: retain the thumbnail's own step, admission and stall rules, park the frame request between draws.
public sealed class WallpaperThumbnailWakeBudgetTests
{
    [Fact]
    [Trait("Decision", "ED-NEW-lean-7")]
    public void A_240_Hz_display_delivers_at_most_two_callbacks_per_thumbnail_step_and_none_while_unseen()
    {
        using var runtime = new TestRuntime();
        var period = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 240);
        var frames = new ManualFrameSource { WakePeriod = period };
        using var source = FrameClock.Source.Override(frames);
        var ticker = new WallpaperThumbnailTicker();
        var client = new FakeWallpaperThumbnailClient(0) { Live = true };
        runtime.OnMain(() => ticker.Add(client));
        void Run(int count)
        {
            for (var i = 0; i < count; i++)
            {
                runtime.Advance(period);
                runtime.OnMain(() => frames.Advance(period));
            }
        }
        Run(240);
        var before = frames.Delivered;
        var moved = client.Moved;
        Run(2400);
        var delivered = frames.Delivered - before;
        var seconds = client.Moved - moved;
        Assert.InRange(seconds, 9.95, 10.05);
        Assert.True(delivered <= 1400, $"The thumbnail delivered {delivered / 10.0} callbacks/s for its 60 Hz step (budget 140).");
        frames.Unseen = true;
        before = frames.Delivered; moved = client.Moved;
        Run(2400);
        Assert.Equal(before, frames.Delivered);
        Assert.Equal(moved, client.Moved);
        frames.Unseen = false;
        Run(8);
        Assert.InRange(client.Moved - moved, 0.0, WallpaperThumbnailTicker.MaxStep + .04);
        Assert.True(client.Moved > moved);
        runtime.OnMain(() => ticker.Remove(client));
        Assert.Equal(0, frames.Pending);
    }
}
