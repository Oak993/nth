using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using SkiaSharp;

namespace Marrow.UI.Tests.Port;

// OD-0136: the empty chat's 44 px nth tile (nth-brand.html section 3, .hero .mk): the icon symbol drawn as vectors, one soft sweep
// as the prompt's .3 s fade-in ends (one .6 s pass, NthMotion.Hero), then still; frames only for that sweep.
[Collection("Headless")]
public sealed class NthTileTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "OD-0136")]
    public void The_tile_sweeps_once_after_it_appears_then_stops_asking_for_frames() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        using var motion = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform(reduceMotion: false));
        var tile = new NthTile();
        var window = new Window { Content = tile, Width = 120, Height = 120 };
        window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.Equal(new Size(NthTile.Side, NthTile.Side), tile.Bounds.Size);
            Assert.Equal(44, NthTile.Side);
            Assert.Equal(NthPose.Still, tile.Pose);
            var still = Draw(tile);
            Assert.Equal(1, frames.Pending);
            Step(frames, time, NthMotion.HeroSweepDelay + NthMotion.HeroSweepSeconds / 2);
            Assert.True(tile.Pose.Sweep is > 0 and < 1);
            Assert.Equal(NthSweep.Soft, tile.Pose.SweepKind);
            var sweeping = Draw(tile);
            Assert.True(sweeping.Zip(still).Any(pair => pair.First > pair.Second + 20), "the sweep did not brighten the tile");
            Step(frames, time, NthMotion.HeroDone + .1);
            Assert.Equal(NthPose.Still, tile.Pose);
            Assert.Equal(0, frames.Pending); // still: no more frames
            Assert.Equal(still, Draw(tile));
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void Reduced_motion_shows_the_still_tile_and_asks_for_no_frames() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        using var motion = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform(reduceMotion: true));
        var tile = new NthTile();
        var window = new Window { Content = tile, Width = 120, Height = 120 };
        window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.Equal(0, frames.Pending);
            Assert.Equal(NthPose.Still, tile.Pose);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void The_tile_draws_the_icon_symbol_at_44_points()
    {
        headless.OnUiThread(() =>
        {
            using var motion = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform(reduceMotion: true));
            var tile = new NthTile();
            tile.Measure(new Size(44, 44)); tile.Arrange(new Rect(0, 0, 44, 44));
            var drawn = Draw(tile);
            using var surface = SKSurface.Create(new SKImageInfo(44, 44, SKColorType.Bgra8888, SKAlphaType.Premul))!;
            surface.Canvas.Clear(SKColors.Transparent);
            using var mark = new NthMark();
            mark.Draw(surface.Canvas, new SKRect(0, 0, 44, 44), NthTileStyle.Icon, NthGlyph.Regular, NthPose.Still);
            using var expected = new SKBitmap(new SKImageInfo(44, 44, SKColorType.Bgra8888, SKAlphaType.Premul));
            surface.ReadPixels(expected.Info, expected.GetPixels(), expected.RowBytes, 0, 0);
            var reference = expected.GetPixelSpan().ToArray();
            Assert.Equal(reference.Length, drawn.Length);
            Assert.True(reference.Zip(drawn).All(pair => Math.Abs(pair.First - pair.Second) <= 2), "the tile is not the icon symbol");
            Assert.Equal(0, drawn[3]); // the corner is outside the rounded tile
            var stem = (int)(130 / 256.0 * 44) * 44 * 4 + (int)(70 / 256.0 * 44) * 4; // the n's left stem: light silver
            Assert.True(drawn[stem] > 180 && drawn[stem + 1] > 180 && drawn[stem + 2] > 180);
        });
    }

    private static void Step(ManualFrameSource frames, ManualTimeProvider time, double until)
    {
        while (frames.Now.TotalSeconds < until)
        {
            time.Advance(TimeSpan.FromSeconds(1.0 / 60)); frames.Advance(TimeSpan.FromSeconds(1.0 / 60));
            Dispatcher.UIThread.RunJobs();
        }
    }

    // The control's own drawing, premultiplied BGRA at 96 dpi.
    private static byte[] Draw(Control control)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(44, 44), new Vector(96, 96));
        bitmap.Render(control);
        var bytes = new byte[44 * 44 * 4];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(0, 0, 44, 44), handle.AddrOfPinnedObject(), bytes.Length, 44 * 4); }
        finally { handle.Free(); }
        return bytes;
    }
}
