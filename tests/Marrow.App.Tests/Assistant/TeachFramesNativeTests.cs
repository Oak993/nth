using Marrow.App.Assistant.Teach;
using Marrow.Core.Port.Assistant;
using SkiaSharp;

namespace Marrow.App.Tests.Assistant;

// Owed native check (never run by the unit agent; the lead runs it alone, with focus): DESIGN.md rev 5, 16.1 "Native gate", AS-19. A frame of the real
// screen is a JPEG of the monitor the window in front is on, no wider than 1280 pixels, in about half a second at most, and two frames of an
// unchanged screen are alike. The frame of the screen is the plain GDI copy (the design names Windows.Graphics.Capture, which Tactus brings);
// the owner looks at one frame to see it is the screen and nothing is missing.
[Trait("Feature", "AS-19")]
[Trait("Decision", "ED-NEW-dextra-as19-9")]
[Trait("Platform", "WINDOWS-ONLY")]
public sealed class TeachFramesNativeTests
{
    [Fact]
    public void A_frame_of_the_real_screen_is_a_jpeg_no_wider_than_the_frame_width()
    {
        var frames = new ScreenFrames();
        var started = System.Diagnostics.Stopwatch.StartNew();
        var jpeg = frames.Capture();
        started.Stop();
        Assert.NotNull(jpeg);
        Assert.Equal(new byte[] { 0xFF, 0xD8, 0xFF }, jpeg![..3]);
        using var decoded = SKBitmap.Decode(jpeg);
        Assert.InRange(decoded.Width, 200, TeachLimits.FrameWidth);
        Assert.InRange(decoded.Height, 100, 4000);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(2), $"the frame took {started.Elapsed.TotalMilliseconds:0} ms");
    }

    [Fact]
    public void Two_frames_of_a_screen_that_did_not_change_are_the_same_size_within_a_few_percent()
    {
        var frames = new ScreenFrames();
        var first = frames.Capture()!;
        Thread.Sleep(300);
        var second = frames.Capture()!;
        Assert.InRange(Math.Abs(first.Length - second.Length), 0, first.Length / 10);
    }
}
