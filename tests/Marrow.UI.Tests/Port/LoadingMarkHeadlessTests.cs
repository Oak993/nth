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

    // ---- review point 1: trail and glow blur as a Gaussian standard deviation (ED-102 (2), (7); ED-103 (5)) ----

    private static WriteableBitmap Copy(RenderTargetBitmap bitmap)
    {
        var copy = new WriteableBitmap(bitmap.PixelSize, bitmap.Dpi,
            bitmap.Format ?? Avalonia.Platform.PixelFormat.Rgba8888, Avalonia.Platform.AlphaFormat.Premul);
        using (var buffer = copy.Lock())
            bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), buffer.Address, buffer.RowBytes * buffer.Size.Height, buffer.RowBytes);
        return copy;
    }

    private static byte[,] Alpha(WriteableBitmap bitmap)
    {
        var size = bitmap.PixelSize;
        var alpha = new byte[size.Width, size.Height];
        using var buffer = bitmap.Lock();
        var row = new byte[buffer.RowBytes];
        for (var y = 0; y < size.Height; y++)
        {
            System.Runtime.InteropServices.Marshal.Copy(buffer.Address + y * buffer.RowBytes, row, 0, buffer.RowBytes);
            for (var x = 0; x < size.Width; x++) alpha[x, y] = row[x * 4 + 3];
        }
        return alpha;
    }

    // The standard deviation (device px) of a 2 px white bar drawn under the effect at the given scale, less the bar's
    // own spread (a 2 px box has variance 1/3).
    private static double MeasuredSigma(double sigma, double scale)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(600, 20));
        using (var context = bitmap.CreateDrawingContext())
        {
            context.FillRectangle(Brushes.Black, new Rect(0, 0, 600, 20));
            using (context.PushTransform(Matrix.CreateScale(scale, scale)))
            using (context.PushEffect(CometPainter.GaussianBlur(sigma), new Rect(0, 0, 600 / scale, 20 / scale)))
                context.FillRectangle(Brushes.White, new Rect(299 / scale, 0, 2 / scale, 20 / scale));
        }
        using var copy = Copy(bitmap);
        double sum = 0, mean = 0, square = 0;
        for (var x = 0; x < 600; x++)
        {
            var w = (double)Pixel(copy, x, 10).R;
            sum += w;
            mean += w * (x + .5);
        }
        mean /= sum;
        for (var x = 0; x < 600; x++) square += Pixel(copy, x, 10).R * Math.Pow(x + .5 - mean, 2);
        return Math.Sqrt(square / sum - 1.0 / 3);
    }

    [Fact]
    [Trait("Decision", "ED-102")]
    [Trait("Decision", "ED-103")]
    public void The_blur_is_a_gaussian_whose_standard_deviation_is_the_blur_value_in_drawing_units_at_every_scale() => headless.OnUiThread(() =>
    {
        // ED-102 (2): the trails are "blurred by 10 and 20 units"; the reference draws them with ctx.filter =
        // blur(blurUnits * S px) (boot_sequence.html:188), and a CSS blur() length is the Gaussian's standard deviation,
        // so the drawn sigma is 10 and 20 icon units, scaled with the mark (S = 210/1024 on the boot stage). The window
        // glow is a canvas shadowBlur of 14 DIP (:284), whose standard deviation is half of it, 7 DIP.
        foreach (var (sigma, scale) in new[] { (10.0, 1.0), (20.0, 1.0), (7.0, 1.0), (20.0, .5), (20.0, 210 / 1024.0), (10.0, 210 / 1024.0) })
        {
            var expected = sigma * scale;
            var measured = MeasuredSigma(sigma, scale);
            Assert.True(Math.Abs(measured - expected) <= Math.Max(.1 * expected, .3),
                $"sigma {sigma} at scale {scale}: measured {measured:F2} px, expected {expected:F2} px");
        }
    });

    // Chamfer (3-4) distance, in px, from each pixel to the nearest pixel set in the mask.
    private static double[,] Distance(bool[,] inside)
    {
        int w = inside.GetLength(0), h = inside.GetLength(1);
        var d = new double[w, h];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++) d[x, y] = inside[x, y] ? 0 : 1e9;
        void Relax(int x, int y, int nx, int ny, double step)
        {
            if (nx >= 0 && ny >= 0 && nx < w && ny < h && d[nx, ny] + step < d[x, y]) d[x, y] = d[nx, ny] + step;
        }
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                Relax(x, y, x - 1, y, 3); Relax(x, y, x, y - 1, 3); Relax(x, y, x - 1, y - 1, 4); Relax(x, y, x + 1, y - 1, 4);
            }
        for (var y = h - 1; y >= 0; y--)
            for (var x = w - 1; x >= 0; x--)
            {
                Relax(x, y, x + 1, y, 3); Relax(x, y, x, y + 1, 3); Relax(x, y, x + 1, y + 1, 4); Relax(x, y, x - 1, y + 1, 4);
            }
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++) d[x, y] /= 3;
        return d;
    }

    [Fact]
    [Trait("Decision", "ED-102")]
    public void The_comet_trails_are_soft_smears_that_reach_about_one_standard_deviation_past_the_arms() => headless.OnUiThread(() =>
    {
        // ED-102 (2): trail copies rotated back 14 and 30 degrees at opacity .42 and .2 (times a trail factor of 1 here),
        // blurred with sigma 10 and 20 units. Drawn at half scale (1 px = 2 units) on a transparent bitmap.
        const double scale = .5;
        var reveals = new double[] { 1, 1, 1 };
        byte[,] Render(CometState state, bool main)
        {
            using var bitmap = new RenderTargetBitmap(new PixelSize(512, 512));
            using (var context = bitmap.CreateDrawingContext())
            using (context.PushTransform(Matrix.CreateScale(scale, scale)))
                CometPainter.DrawArms(context, reveals, state, main);
            using var copy = Copy(bitmap);
            return Alpha(copy);
        }
        // The unblurred copies: the main pass alone (trail factor 0 draws no trails) turned back 14 and 30 degrees.
        var back14 = Render(new CometState(-14 * Math.PI / 180, 0, 0, 1, .55, 1, null, null), main: true);
        var back30 = Render(new CometState(-30 * Math.PI / 180, 0, 0, 1, .55, 1, null, null), main: true);
        var shapes = new bool[512, 512];
        for (var y = 0; y < 512; y++)
            for (var x = 0; x < 512; x++) shapes[x, y] = back14[x, y] >= 128 || back30[x, y] >= 128;
        var distance = Distance(shapes);
        var trails = Render(new CometState(0, 2, 1, 1, .55, 1, null, null), main: false);
        double Band(double from, double to)
        {
            double sum = 0;
            var count = 0;
            for (var y = 0; y < 512; y++)
                for (var x = 0; x < 512; x++)
                {
                    var units = distance[x, y] / scale;
                    if (units < from || units > to) continue;
                    sum += trails[x, y];
                    count++;
                }
            Assert.True(count > 500, $"band {from}-{to}: {count} px");
            return sum / count;
        }
        // One sigma (20 units) past the copies a straight edge keeps .2 * .5 * erfc(1/sqrt 2) = .032 of the 30 degree
        // copy and .42 * .5 * erfc(sqrt 2) = .0096 of the 14 degree copy: between 2.4 and 10.7 of 255. A blur three
        // times too narrow leaves under .01 of 255 there. Three sigma out (60 units) under .1 of 255 remains.
        var near = Band(18, 22);
        var far = Band(56, 64);
        Assert.True(near >= 1.5 && near <= 12, $"alpha 18-22 units past the arms: {near:F2}");
        Assert.True(far < 1, $"alpha 56-64 units past the arms: {far:F2}");
    });

    [Fact]
    [Trait("Decision", "ED-103")]
    public void The_window_outline_glow_falls_off_as_a_gaussian_of_seven_dip() => headless.OnUiThread(() =>
    {
        // ED-103 (5) "a 14 DIP glow": boot_sequence.html:284 draws it as ctx.shadowBlur = px(14), and a canvas
        // shadowBlur of 14 is a Gaussian of standard deviation 7. The 1.5 DIP line blurred that way falls from 3 to 10 DIP
        // off the edge by exp(-(10^2 - 3^2) / (2 * (49 + 1.5^2 / 12))) = .40 (a sigma of 4.5 would give .11).
        var layout = new BootWindowLayout(new Rect(50.5, 100.5, 300, 200), 14, [new Rect(60, 110, 280, 180)]);
        using var bitmap = new RenderTargetBitmap(new PixelSize(400, 400));
        using (var context = bitmap.CreateDrawingContext()) BootStage.DrawOutline(context, layout, 1);
        using var copy = Copy(bitmap);
        var alpha = Alpha(copy);
        double at3 = 0, at10 = 0;
        for (var x = 150; x <= 250; x++)
        {
            at3 += alpha[x, 97];
            at10 += alpha[x, 90];
        }
        Assert.True(at3 > 500, $"glow 3 DIP off the edge: {at3}");
        Assert.InRange(at10 / at3, .33, .47);
    });

    // ---- final findings 1-2: the rendered core glow follows GlowOpacity; every particle dot is rendered ----

    // Final finding 1 (ED-102 (3), boot_sequence.html:218-219): the glow is a radial gradient of radius 230 * glowS whose
    // stops are rgba(255,255,255, clamp(.55 glowA)) at 0 and rgba(185,200,255, clamp(.28 glowA)) at .35, falling to
    // rgba(147,172,255,0) at 1. Drawn alone (core scale 0) on a transparent bitmap at a fixed glow scale, the alpha at
    // the centre and at .35 of the radius is therefore decided by the glow opacity alone.
    [Theory]
    [Trait("Decision", "ED-102")]
    [InlineData(.3025, 1.0)]
    [InlineData(.55, 1.0)]
    [InlineData(1.2, 1.0)]
    [InlineData(1.65, 1.0)]
    [InlineData(.55, 1.5)]
    [InlineData(1.65, 1.5)]
    public void The_rendered_core_glow_alpha_follows_the_glow_opacity_at_a_fixed_glow_scale(double glowOpacity, double glowScale) => headless.OnUiThread(() =>
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(1024, 1024));
        using (var context = bitmap.CreateDrawingContext())
            CometPainter.DrawCore(context, new CometState(0, 0, 0, 0, glowOpacity, glowScale, null, null), 1);
        using var copy = Copy(bitmap);
        var centre = Pixel(copy, 512, 512);
        var centreAlpha = 255 * Math.Min(1, .55 * glowOpacity);
        Assert.True(Math.Abs(centre.A - centreAlpha) <= 3, $"glow {glowOpacity}: centre alpha {centre.A}, expected {centreAlpha:F1}");
        // .35 of the radius: row 512 + d with the pixel centre at d + .5 = .35 * 230 * glowScale.
        var d = (int)Math.Round(.35 * 230 * glowScale - .5);
        var stop = Pixel(copy, 512, 512 + d);
        var stopAlpha = 255 * Math.Min(1, .28 * glowOpacity);
        Assert.True(Math.Abs(stop.A - stopAlpha) <= 4, $"glow {glowOpacity}: alpha at .35 radius {stop.A}, expected {stopAlpha:F1}");
        Assert.True(Math.Abs(stop.R * 255.0 / stop.A - 185) <= 8 && Math.Abs(stop.G * 255.0 / stop.A - 200) <= 8 &&
            Math.Abs(stop.B * 255.0 / stop.A - 255) <= 8, $"glow colour at .35 radius {stop}");
        // Outside 230 * glowScale nothing is drawn.
        var outside = Pixel(copy, 512, 512 + (int)Math.Ceiling(230 * glowScale) + 1);
        Assert.Equal(0, outside.A);
    });

    // A dot of boot_sequence.html:234-238 is a radial gradient of radius r, white at alpha a at 0, the particle colour at
    // alpha a at .25 and transparent at 1. Its alpha integrates to a * pi * r^2 * .4375 (.0625 over the plateau and .375
    // over the linear fall), so the radius follows from the summed alpha and the plateau's alpha. The dot is drawn in DIP,
    // magnified k times about its centre.
    private static (double Alpha, double Radius, Color Colour) MeasureDot(Action<DrawingContext> draw, Point centre, double k)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(400, 400));
        using (var context = bitmap.CreateDrawingContext())
        using (context.PushTransform(Matrix.CreateTranslation(-centre.X, -centre.Y) * Matrix.CreateScale(k, k) *
                   Matrix.CreateTranslation(200, 200)))
            draw(context);
        using var copy = Copy(bitmap);
        var alpha = Alpha(copy);
        double sum = 0;
        for (var y = 0; y < 400; y++)
            for (var x = 0; x < 400; x++) sum += alpha[x, y] / 255.0;
        var peak = alpha[200, 200] / 255.0;
        var radius = peak <= 0 ? 0 : Math.Sqrt(sum / (.4375 * Math.PI * peak)) / k;
        var ring = Pixel(copy, 200 + (int)Math.Round(.6 * radius * k), 200);
        var colour = ring.A == 0 ? ring : Color.FromRgb((byte)Math.Min(255, ring.R * 255 / ring.A),
            (byte)Math.Min(255, ring.G * 255 / ring.A), (byte)Math.Min(255, ring.B * 255 / ring.A));
        return (peak, radius, colour);
    }

    private static void AssertDot((double Alpha, double Radius, Color Colour) dot, double alpha, double radius, Color colour, string what)
    {
        Assert.True(Math.Abs(dot.Alpha - alpha) <= 4 / 255.0, $"{what}: alpha {dot.Alpha:F3}, expected {alpha:F3}");
        Assert.True(Math.Abs(dot.Radius - radius) <= .05 * radius, $"{what}: radius {dot.Radius:F3} DIP, expected {radius:F3}");
        Assert.True(Math.Abs(dot.Colour.R - colour.R) <= 8 && Math.Abs(dot.Colour.G - colour.G) <= 8 &&
            Math.Abs(dot.Colour.B - colour.B) <= 8, $"{what}: colour {dot.Colour}, expected {colour}");
    }

    private static BootSequence ParticleSequence() => new(new BootWindowLayout(new Rect(-262, -112, 780, 480), 14,
        [new Rect(-248, -60, 204, 150), new Rect(10, -42, 468, 220), new Rect(-242, -104, 740, 28)]), reduceMotion: false);

    // Final finding 2, ED-102 (4) "a particle is drawn as a soft dot of radius 9 to 20 shrinking to 55 % as it lands,
    // then melts out over .35 of its flight time" and "where this text and the preview differ, this text rules": the
    // drawn radius is the particle's size (9 to 20 icon units, 210/1024 DIP each) times (1 - .45 q), not the preview's
    // 2.2 times it (boot_sequence.html:344). The alpha is fade * (.55 + .45 smooth(0, .25, q)) with fade =
    // 1 - smooth(1, 1.35, raw) after landing (:342-344); the colour is the arm's colour at the target (:120).
    [Theory]
    [Trait("Decision", "ED-102")]
    [InlineData(.1)]
    [InlineData(.5)]
    [InlineData(1.0)]
    [InlineData(1.175)]
    public void A_gather_particle_is_drawn_as_a_soft_dot_of_its_size_shrinking_to_55_percent_and_melting_after_landing(double raw) => headless.OnUiThread(() =>
    {
        var sequence = ParticleSequence();
        var particle = sequence.GatherParticles[0];
        var time = particle.Delay + raw * particle.Duration;
        var q = Math.Clamp(raw, 0, 1);
        var fade = raw < 1 ? 1 : 1 - CometEasing.Smooth(1, 1.35, raw);
        var alpha = fade * (.55 + .45 * CometEasing.Smooth(0, .25, q));
        var radius = particle.Size * (1 - .45 * q) * 210 / 1024;
        var centre = sequence.IconToStage(particle.Position(time));
        var dot = MeasureDot(context => BootStage.DrawGatherDot(context, sequence, particle, time), centre, 16);
        AssertDot(dot, alpha, radius, particle.Color, $"gather dot at raw {raw}");
    });

    [Fact]
    [Trait("Decision", "ED-102")]
    public void A_gather_particle_has_melted_out_at_35_percent_of_its_flight_past_landing() => headless.OnUiThread(() =>
    {
        var sequence = ParticleSequence();
        var particle = sequence.GatherParticles[0];
        var time = particle.Delay + 1.36 * particle.Duration;
        var dot = MeasureDot(context => BootStage.DrawGatherDot(context, sequence, particle, time),
            sequence.IconToStage(particle.Position(time)), 16);
        Assert.Equal(0, dot.Alpha);
    });

    // boot_sequence.html:340 (ED-102 (4) says nothing of a particle before its start, so the preview governs): a particle
    // that has not started twinkles on its start ring as a dot of 1.8 times its size at alpha
    // smooth(0, .18, t) * .35 * (.6 + .4 sin(tw + 9 t)).
    [Fact]
    [Trait("Decision", "ED-102")]
    public void A_particle_waiting_to_start_twinkles_on_its_start_ring() => headless.OnUiThread(() =>
    {
        var sequence = ParticleSequence();
        foreach (var time in new[] { .09, .2 })
        {
            var particle = sequence.GatherParticles.First(p => p.Delay > .3 &&
                .6 + .4 * Math.Sin(p.Twinkle + time * 9) > .8);
            var alpha = CometEasing.Smooth(0, .18, time) * .35 * (.6 + .4 * Math.Sin(particle.Twinkle + time * 9));
            var radius = particle.Size * 1.8 * 210 / 1024;
            var start = new Point(512 + Math.Cos(particle.StartAngle) * particle.StartRadius,
                512 + Math.Sin(particle.StartAngle) * particle.StartRadius);
            var centre = sequence.IconToStage(start);
            var dot = MeasureDot(context => BootStage.DrawGatherDot(context, sequence, particle, time), centre, 16);
            Assert.True(Math.Abs(dot.Alpha - alpha) <= 4 / 255.0, $"waiting dot at {time}: alpha {dot.Alpha:F3}, expected {alpha:F3}");
            Assert.True(Math.Abs(dot.Radius - radius) <= .08 * radius, $"waiting dot at {time}: radius {dot.Radius:F3}, expected {radius:F3}");
        }
    });

    // boot_sequence.html:355-362 (ED-103 (4) gives no dot size, so the preview governs): a window particle is a dot of
    // 2.2 times its size times (1 - .3 fly) at alpha appear * melt * (.6 + .4 (1 - fly)), appear = smooth(0, .2, td),
    // melt = 1 - smooth(landing, landing + .35, td).
    [Theory]
    [Trait("Decision", "ED-103")]
    [InlineData(-1.0)]
    [InlineData(.5)]
    [InlineData(1.175)]
    public void A_window_particle_is_drawn_with_the_references_radius_and_alpha_ramp(double at) => headless.OnUiThread(() =>
    {
        var sequence = ParticleSequence();
        foreach (var particle in new[] { sequence.WindowParticles[0], sequence.WindowParticles[^1] })
        {
            // at -1: td .1 (before its flight); .5: halfway through its flight; 1.175: .175 s after landing.
            var td = at < 0 ? .1 : at <= 1 ? particle.Delay + at * particle.Duration : particle.LandsAt + .175;
            var fly = CometEasing.InOutCubic((td - particle.Delay) / particle.Duration);
            var alpha = CometEasing.Smooth(0, .2, td) *
                (1 - CometEasing.Smooth(particle.LandsAt, particle.LandsAt + .35, td)) * (.6 + .4 * (1 - fly));
            var radius = particle.Size * 210 / 1024 * 2.2 * (1 - .3 * fly);
            var dot = MeasureDot(context => BootStage.DrawWindowDot(context, particle, td), particle.Position(td), 16);
            AssertDot(dot, alpha, radius, particle.Color, $"window dot at td {td:F3}");
        }
    });
}
