using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// F.LoadingMark on Avalonia 12.1.2 headless (R-TEST-7): the LoadingMark control and the BootStage control run from the
// frame clock seam (ED-083, ED-097 (2), ED-102 (7); R-VIEW-10), follow reduce motion from the environment (R-VIEW-6,
// ED-097 (3)), take no input (ED-097 (6), ED-103 (1)) and draw the icon of src/Marrow.App/Assets/icon.svg.
// PORT-ONLY surface these tests fix, in src/Marrow.UI/Port/LoadingMark.cs, namespace Marrow.UI.Port:
//   LoadingMark : Control { double Elapsed; double CurrentOpacity; CometState State; }   Elapsed is SwiftClock time
//       since the control was attached, as of its last frame
//   BootStage(BootSequence sequence) : Control { double Elapsed; BootFrame Frame; void Ready();
//       event EventHandler MainWindowShown; event EventHandler Closed; }
[Collection("Headless")]
public sealed class LoadingMarkHeadlessTests(HeadlessSession headless)
{
    private static Window Show(Control content, IBrush? background = null)
    {
        var window = new Window { Width = 256, Height = 256, Content = content, Background = background };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static void Frame(ManualTimeProvider time, ManualFrameSource frames, double seconds)
    {
        time.Advance(TimeSpan.FromSeconds(seconds));
        frames.Advance(TimeSpan.FromSeconds(seconds));
        Dispatcher.UIThread.RunJobs();
    }

    private static Color Pixel(WriteableBitmap bitmap, int x, int y)
    {
        using var buffer = bitmap.Lock();
        var bytes = new byte[4];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address + y * buffer.RowBytes + x * 4, bytes, 0, 4);
        return buffer.Format == Avalonia.Platform.PixelFormat.Rgba8888
            ? Color.FromArgb(bytes[3], bytes[0], bytes[1], bytes[2])
            : Color.FromArgb(bytes[3], bytes[2], bytes[1], bytes[0]);
    }

    private static Color Capture(Window window, int x, int y)
    {
        using var frame = window.CaptureRenderedFrame()!;
        // CopyPixels writes the frame's own pixel format, so the bitmap takes that format and Pixel reads the channels
        // in that order (review point 2: a Bgra8888 bitmap here read R and B swapped).
        using var bitmap = new WriteableBitmap(frame.PixelSize, frame.Dpi,
            frame.Format ?? Avalonia.Platform.PixelFormat.Rgba8888, Avalonia.Platform.AlphaFormat.Premul);
        using (var buffer = bitmap.Lock()) frame.CopyPixels(new PixelRect(frame.PixelSize), buffer.Address, buffer.RowBytes * buffer.Size.Height, buffer.RowBytes);
        return Pixel(bitmap, x, y);
    }

    [Fact]
    [Trait("Decision", "ED-097")]
    public void The_mark_is_a_picture_not_a_control_with_no_hit_testing_and_no_focus() => headless.OnUiThread(() =>
    {
        var mark = new LoadingMark();
        Assert.False(mark.IsHitTestVisible);
        Assert.False(mark.Focusable);
        var stage = new BootStage(new BootSequence(new BootWindowLayout(new Rect(0, 0, 780, 480), 14, [new Rect(20, 8, 740, 28)]), reduceMotion: false));
        Assert.False(stage.IsHitTestVisible); // ED-103 (1): nothing on the stage takes input
        Assert.False(stage.Focusable);
    });

    [Fact]
    [Trait("Decision", "ED-097")]
    [Trait("Decision", "ED-102")]
    [Trait("Decision", "ED-083")]
    [Trait("Rule", "R-VIEW-10")]
    public void The_mark_runs_from_the_frame_clock_after_the_400_ms_rule_and_asks_for_no_frames_once_detached() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        var mark = new LoadingMark { Width = 64, Height = 64 };
        var window = Show(mark);
        Assert.Equal(0, mark.Elapsed);
        Assert.Equal(0, mark.CurrentOpacity);
        Assert.Equal(1, frames.Pending);
        Frame(time, frames, .3);
        Assert.Equal(.3, mark.Elapsed, 9);
        Assert.Equal(0, mark.CurrentOpacity);
        Frame(time, frames, .2);
        Assert.Equal(.5, mark.CurrentOpacity, 9);
        Frame(time, frames, 1.1); // 1.6 s: 1.2 s into the first turn
        Assert.Equal(1, mark.CurrentOpacity, 9);
        Assert.Equal(LoadingMark.Motion(mark.Elapsed, reduceMotion: false), mark.State);
        Assert.Equal(Math.PI, mark.State.Angle, 7);
        // time alone moves nothing: only a frame does
        time.Advance(TimeSpan.FromSeconds(1));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1.6, mark.Elapsed, 9);
        window.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, frames.Pending);
    });

    [Fact]
    [Trait("Decision", "ED-097")]
    [Trait("Rule", "R-VIEW-6")]
    public void Under_reduce_motion_from_the_environment_the_mark_is_drawn_still() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        var mark = new LoadingMark { Width = 64, Height = 64 };
        var host = new Border { Child = mark };
        EnvironmentValues.SetAccessibilityReduceMotion(host, true);
        var window = Show(host);
        Frame(time, frames, 1.6);
        Assert.Equal(CometMotion.Still, mark.State);
        Assert.Equal(1, mark.CurrentOpacity, 9);
        EnvironmentValues.SetAccessibilityReduceMotion(host, false);
        Frame(time, frames, .1);
        Assert.Equal(LoadingMark.Motion(1.7, reduceMotion: false), mark.State); // moving again, on the same clock
        Assert.Equal(1.7, mark.Elapsed, 9);
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-097")]
    [Trait("Decision", "ED-102")]
    public void The_mark_draws_nothing_before_400_ms_then_the_icon_with_its_white_core_on_the_dark_tile() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        var gray = Color.FromRgb(0x80, 0x80, 0x80);
        // 128 DIP mark centred in 256: icon unit u maps to 64 + u / 8
        var mark = new LoadingMark { Width = 128, Height = 128 };
        var window = Show(mark, new SolidColorBrush(gray));
        Frame(time, frames, .2);
        Assert.Equal(gray, Capture(window, 128, 128));
        Assert.Equal(gray, Capture(window, 128, 76));
        Frame(time, frames, .8); // 1.0 s: fully shown, .6 s after the first pulse
        var core = Capture(window, 128, 128); // icon (512, 512): the core, #FFFFFF to #F2F5FF near its focus
        Assert.True(core.R >= 235 && core.G >= 235 && core.B >= 240, $"core pixel {core}");
        var tile = Capture(window, 128, 76); // icon (512, 96): the tile under its glow, no arm or ring there
        Assert.True(tile.R < 100 && tile.G < 100 && tile.B < 140, $"tile pixel {tile}");
        var outside = Capture(window, 66, 66); // icon (16, 16): outside the rounded tile
        Assert.True(Math.Abs(outside.R - gray.R) < 24 && Math.Abs(outside.B - gray.B) < 24, $"outside pixel {outside}");
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-103")]
    [Trait("Decision", "ED-102")]
    [Trait("Rule", "R-VIEW-10")]
    public void The_boot_stage_runs_from_the_frame_clock_shows_the_main_window_when_the_outline_lands_and_closes_after_the_last_particle() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        var layout = new BootWindowLayout(new Rect(-262, -112, 780, 480), 14,
            [new Rect(-248, -60, 204, 150), new Rect(10, -42, 468, 220), new Rect(-242, -104, 740, 28)]);
        var sequence = new BootSequence(layout, reduceMotion: false);
        var stage = new BootStage(sequence);
        var shown = new List<double>();
        var closed = new List<double>();
        stage.MainWindowShown += (_, _) => shown.Add(stage.Elapsed);
        stage.Closed += (_, _) => closed.Add(stage.Elapsed);
        var window = Show(stage);
        for (var i = 0; i < 50; i++) Frame(time, frames, .05); // 2.5 s
        Assert.Equal(BootPhase.Turning, stage.Frame.Phase);
        stage.Ready();
        for (var i = 0; i < 80; i++) Frame(time, frames, .05); // to 6.5 s
        var showAt = sequence.MainWindowShownAt(2.5);
        var closeAt = sequence.StageClosesAt(2.5);
        Assert.Single(shown);
        Assert.InRange(shown[0], showAt, showAt + .05 + 1e-9);
        Assert.Single(closed);
        Assert.InRange(closed[0], closeAt, closeAt + .05 + 1e-9);
        Assert.Equal(BootPhase.Closed, stage.Frame.Phase);
        Assert.Equal(0, frames.Pending); // closed: no more frames
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-103")]
    [Trait("Decision", "ED-102")]
    public void The_boot_arms_share_the_tile_centre_even_when_the_stage_origin_is_not_the_mark_origin() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        var layout = new BootWindowLayout(new Rect(110, 72.5, 780, 480), 14, [new Rect(124, 124.5, 204, 150)]);
        var stage = new BootStage(new BootSequence(layout, reduceMotion: true)) { Width = 1000, Height = 625 };
        var window = new Window { Width = 1000, Height = 625, Content = stage };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Frame(time, frames, .5);
        // icon.svg arm 0 begins at (392.88,217.16). The 210 DIP mark is centred on (500,312.5).
        var arm = Capture(window, 476, 252);
        // icon.svg arm 0 gradient starts at #9CBBFF (156,187,255): blue leads red. d0d4d27's assertion, restored.
        Assert.True(arm.B > 140 && arm.B > arm.R, $"arm pixel {arm}");
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-097")]
    public void A_nonsquare_mark_centres_its_icon_without_moving_the_tile_into_the_left_gutter() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        var gray = Color.FromRgb(0x80, 0x80, 0x80);
        var mark = new LoadingMark { Width = 180, Height = 128 };
        var host = new Canvas { Children = { mark } };
        var window = Show(host, new SolidColorBrush(gray));
        Frame(time, frames, 1);
        Assert.Equal(gray, Capture(window, 15, 64));
        var core = Capture(window, 90, 64);
        Assert.True(core.R > 225 && core.G > 225 && core.B > 225, $"core pixel {core}");
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-097")]
    [Trait("Decision", "ED-102")]
    public void The_tile_keyline_is_bright_at_the_top_and_fades_to_zero_at_the_bottom() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        var mark = new LoadingMark { Width = 1024, Height = 1024, ReduceMotion = true };
        var window = new Window { Width = 1024, Height = 1024, Content = mark };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Frame(time, frames, 1);
        var topLine = Capture(window, 512, 74);
        var topInside = Capture(window, 512, 80);
        var bottomLine = Capture(window, 512, 950);
        var bottomInside = Capture(window, 512, 944);
        Assert.True(topLine.R >= topInside.R + 12, $"top line {topLine}, inside {topInside}");
        Assert.True(Math.Abs(bottomLine.R - bottomInside.R) <= 6, $"bottom line {bottomLine}, inside {bottomInside}");
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-102")]
    public void The_core_glow_fades_to_transparent_at_its_outer_radius_without_a_hard_disk_edge() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        var mark = new LoadingMark { Width = 1024, Height = 1024, ReduceMotion = true };
        var window = new Window { Width = 1024, Height = 1024, Content = mark };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Frame(time, frames, 1);
        // The radial glow ends at radius 230; adjacent pixels at r=228 and r=233 remain on the same tile.
        var inside = Capture(window, 512, 740);
        var outside = Capture(window, 512, 745);
        Assert.True(Math.Abs(inside.R - outside.R) <= 6 && Math.Abs(inside.B - outside.B) <= 6,
            $"glow edge inside {inside}, outside {outside}");
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-097")]
    public void The_tile_has_a_radial_glow_over_its_linear_base_gradient() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        var mark = new LoadingMark { Width = 1024, Height = 1024, ReduceMotion = true };
        var window = new Window { Width = 1024, Height = 1024, Content = mark };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Frame(time, frames, 1);
        // These points have equal projection on the tile gradient (.8,1) but different distance to the glow centre.
        var near = Capture(window, 336, 213);
        var far = Capture(window, 477, 100);
        var nearLight = near.R + near.G + near.B;
        var farLight = far.R + far.G + far.B;
        Assert.True(nearLight > farLight + 6, $"glow near {near}, far {far}");
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-102")]
    public void The_core_glow_brightens_in_the_frame_after_the_turn_pulse() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        var mark = new LoadingMark { Width = 1024, Height = 1024 };
        var window = new Window { Width = 1024, Height = 1024, Content = mark };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Frame(time, frames, 2.8); // .4 s appearance delay + one 2.4 s turn
        var resting = Capture(window, 512, 620);
        Frame(time, frames, .06); // the rise is complete and the glow flashes
        var flashed = Capture(window, 512, 620);
        Assert.True(flashed.R + flashed.G + flashed.B > resting.R + resting.G + resting.B + 10,
            $"resting glow {resting}, flashed glow {flashed}");
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-TEST-7")]
    public void The_capture_helper_reads_red_as_red_and_blue_as_blue() => headless.OnUiThread(() =>
    {
        // Review point 2: the helper once read R and B swapped. A pure red and a pure blue window must read as such.
        var red = Show(new Border(), new SolidColorBrush(Color.FromRgb(255, 0, 0)));
        Assert.Equal(Color.FromRgb(255, 0, 0), Capture(red, 10, 10));
        red.Close();
        var blue = Show(new Border(), new SolidColorBrush(Color.FromRgb(0, 0, 255)));
        Assert.Equal(Color.FromRgb(0, 0, 255), Capture(blue, 10, 10));
        blue.Close();
    });

    [Fact]
    [Trait("Decision", "ED-103")]
    [Trait("Decision", "ED-102")]
    public void Readiness_between_frames_takes_the_moment_of_the_call_not_the_last_frame() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        var layout = new BootWindowLayout(new Rect(-262, -112, 780, 480), 14,
            [new Rect(-248, -60, 204, 150), new Rect(10, -42, 468, 220), new Rect(-242, -104, 740, 28)]);
        var stage = new BootStage(new BootSequence(layout, reduceMotion: false));
        var window = Show(stage);
        for (var i = 0; i < 77; i++) Frame(time, frames, .05); // the frame at 3.85 s, the end of the first turn
        time.Advance(TimeSpan.FromSeconds(.01));
        stage.Ready(); // the app is ready at 3.86 s, between frames
        Frame(time, frames, .006); // 3.866 s
        // ED-103 (3): the final rest is the end of the first turn that ends at or after 3.86 s: 1.45 + 2 * 2.4 = 6.25 s.
        // The turn that began with the pulse at 3.85 s keeps running, its ring still out.
        Assert.Equal(BootPhase.Turning, stage.Frame.Phase);
        Assert.Equal(3.85, stage.Frame.Comet.LastPulse!.Value, 9);
        Assert.NotNull(stage.Frame.Comet.RingAge);
        for (var i = 0; i < 47; i++) Frame(time, frames, .05); // 6.216 s
        Assert.Equal(BootPhase.Turning, stage.Frame.Phase);
        Frame(time, frames, .05); // 6.266 s
        Assert.Equal(BootPhase.AtRest, stage.Frame.Phase);
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-103")]
    public void Readiness_before_the_stage_starts_counts_as_ready_at_zero_and_rests_after_the_first_turn() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        var layout = new BootWindowLayout(new Rect(-262, -112, 780, 480), 14,
            [new Rect(-248, -60, 204, 150), new Rect(10, -42, 468, 220), new Rect(-242, -104, 740, 28)]);
        var stage = new BootStage(new BootSequence(layout, reduceMotion: false));
        time.Advance(TimeSpan.FromSeconds(5)); // time passes before the stage is attached
        stage.Ready();
        var window = Show(stage);
        for (var i = 0; i < 76; i++) Frame(time, frames, .05); // 3.8 s
        Assert.Equal(BootPhase.Turning, stage.Frame.Phase);
        Frame(time, frames, .06); // 3.86 s: ED-103 (3), the first turn always runs and ends at 3.85 s
        Assert.Equal(BootPhase.AtRest, stage.Frame.Phase);
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-103")]
    public void The_window_outline_strokes_BED2FF_at_85_percent_over_a_9CBBFF_glow() => headless.OnUiThread(() =>
    {
        // ED-103 (5): 1.5 DIP, #BED2FF at .85 with a 14 DIP glow; boot_sequence.html:285-287 gives the glow colour
        // rgba(156,187,255,.9). The top edge sits at y 50.5, so pixel row 50 lies wholly inside the 1.5 DIP stroke.
        var layout = new BootWindowLayout(new Rect(50.5, 50.5, 100, 100), 14, [new Rect(60, 60, 80, 80)]);
        using var bitmap = new RenderTargetBitmap(new PixelSize(200, 200));
        using (var context = bitmap.CreateDrawingContext())
        {
            context.FillRectangle(Brushes.Black, new Rect(0, 0, 200, 200));
            BootStage.DrawOutline(context, layout, 1);
        }
        using var copy = new WriteableBitmap(bitmap.PixelSize, bitmap.Dpi,
            bitmap.Format ?? Avalonia.Platform.PixelFormat.Rgba8888, Avalonia.Platform.AlphaFormat.Premul);
        using (var buffer = copy.Lock())
            bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), buffer.Address, buffer.RowBytes * buffer.Size.Height, buffer.RowBytes);
        // The stroke: .85 of (190,210,255) over black plus a faint glow, so well short of the full-alpha colour.
        var line = Pixel(copy, 100, 50);
        Assert.InRange(line.R, 155, 180);
        Assert.InRange(line.G, 172, 195);
        Assert.InRange(line.B, 208, 235);
        // The glow above the line: its hue is #9CBBFF's (R/B = 156/255 = .61), not the stroke's (190/255 = .75).
        double sumR = 0, sumB = 0;
        for (var y = 40; y <= 48; y++)
            for (var x = 75; x <= 125; x++)
            {
                var glow = Pixel(copy, x, y);
                sumR += glow.R;
                sumB += glow.B;
            }
        Assert.True(sumB > 200, $"glow blue sum {sumB}");
        Assert.InRange(sumR / sumB, .55, .68);
    });
}
