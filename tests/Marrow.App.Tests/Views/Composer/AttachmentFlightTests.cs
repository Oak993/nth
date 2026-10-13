using Avalonia;
using Marrow.App.Port;
using Marrow.App.Views.Composer;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/AttachmentFlight.swift (B2 1fbf81e), :1-207.
[Collection("Headless")]
public sealed class AttachmentFlightTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-COMPOSER-084")] // App/Views/Composer/AttachmentFlight.swift:146-171 screen conversion and layout reporting
    public void Review_screen_frame_reader_reports_window_offset_and_control_size() => headless.OnUiThread(() =>
    {
        var reports = new List<Rect>();
        var reader = new ScreenFrameReader(reports.Add)
        {
            Width = 40, Height = 20, Margin = new Thickness(12, 18, 0, 0),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
        };
        var window = new Avalonia.Controls.Window { Width = 300, Height = 200, Position = new PixelPoint(100, 200), Content = reader };
        window.Show(); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var origin = window.PointToScreen(new Point(12, 18));
        Assert.Contains(new Rect(origin.X / window.RenderScaling, origin.Y / window.RenderScaling, 40, 20), reports);
        reader.Width = 50; window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(50, reports[^1].Width);
        window.Close();
    });
    [Fact]
    [Trait("Decision", "ED-155")]
    public void The_flight_window_extended_style_is_noactivate_layered_toolwindow_transparent()
    {
        Assert.Equal((nint)0x080800A0, AttachmentFlightWindow.WithFlightStyle(0));
        Assert.Equal((nint)0x080800A8, AttachmentFlightWindow.WithFlightStyle(0x8));
    }

    [Fact]
    [Trait("Req", "UI-COMPOSER-080")] // App/Views/Composer/AttachmentFlight.swift:126-141
    public void The_flight_surface_is_borderless_transparent_nonactivating_and_click_through()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var surface = new WindowsAttachmentFlightPlatform().Create(
                new Rect(10, 20, 100, 80), new Rect(0, 0, 1600, 900), new FlightImage());
            var window = Assert.IsAssignableFrom<Avalonia.Controls.Window>(surface);
            Assert.Equal(Avalonia.Controls.WindowDecorations.None, window.WindowDecorations);
            Assert.False(window.ShowActivated);
            Assert.False(window.Focusable);
            Assert.False(window.IsHitTestVisible);
            Assert.False(window.ShowInTaskbar);
            Assert.True(window.Topmost);
            Assert.Equal(100, window.Width);
            Assert.Equal(80, window.Height);
            surface.OrderOut();
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-084")] // App/Views/Composer/AttachmentFlight.swift:19-35,195-203
    public void An_armed_drop_is_taken_once_and_expires_after_two_seconds()
    {
        using var runtime = new TestRuntime();
        var drops = new AttachmentDrops();
        var image = new FlightImage();
        runtime.OnMain(() => drops.Arm(image, new Rect(10, 20, 30, 40)));
        runtime.Advance(TimeSpan.FromMilliseconds(1999));
        var taken = runtime.OnMain(drops.Take);
        Assert.NotNull(taken);
        Assert.Same(image, taken.Image);
        Assert.Equal(new Rect(10, 20, 30, 40), taken.FromScreen);
        Assert.Equal(6, taken.Radius);
        Assert.Null(runtime.OnMain(drops.Take));

        runtime.OnMain(() => drops.Arm(image, new Rect(50, 60, 70, 80)));
        runtime.Advance(TimeSpan.FromSeconds(2));
        Assert.Null(runtime.OnMain(drops.Take));
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-083")] // App/Views/Composer/AttachmentFlight.swift:63-123
    [Trait("Req", "UI-COMPOSER-080")] // App/Views/Composer/AttachmentFlight.swift:42-141
    public void One_critical_spring_moves_size_position_and_radius_together_until_landing()
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameSource = FrameClock.Source.Override(frames);
        var platform = new FlightPlatform { MainScreen = new Rect(100, 50, 1600, 900) };
        var flight = new AttachmentFlight(platform);
        var image = new FlightImage();
        var landings = 0;
        runtime.OnMain(() => flight.Fly(image, new Rect(110, 70, 20, 20), new Rect(190, 150, 40, 30), 6, 10, () => landings++));

        var made = Assert.Single(platform.Created);
        Assert.Same(image, made.Image);
        Assert.Equal(new Rect(108, 68, 124, 114), made.Frame);
        Assert.Equal(new Rect(100, 50, 1600, 900), made.Screen);
        Assert.Equal(1, made.Surface.Shows);
        Assert.Equal((new Rect(10, 20, 20, 20), 6.0), Assert.Single(made.Surface.States));

        var half = AttachmentFlight.SettlingDuration / 2;
        runtime.Advance(half);
        runtime.OnMain(() => frames.Advance(half));
        var middle = made.Surface.States[^1];
        var alpha = 26.0 / (2 * 1);
        var dampedFrequency = Math.Sqrt(170.0 / 1.0 - alpha * alpha);
        var progress = 1 - Math.Exp(-alpha * half.TotalSeconds) *
            (Math.Cos(dampedFrequency * half.TotalSeconds) + alpha / dampedFrequency * Math.Sin(dampedFrequency * half.TotalSeconds));
        Assert.InRange(Math.Abs(Math.Log(1000) / alpha - AttachmentFlight.SettlingDuration.TotalSeconds), 0, 0.0000001);
        Assert.Equal(10 + 80 * progress, middle.Frame.X, 8);
        Assert.Equal(20 + 80 * progress, middle.Frame.Y, 8);
        Assert.Equal(20 + 20 * progress, middle.Frame.Width, 8);
        Assert.Equal(20 + 10 * progress, middle.Frame.Height, 8);
        Assert.Equal(6 + 4 * progress, middle.Radius, 8);
        Assert.Equal(0, landings);

        runtime.Advance(AttachmentFlight.SettlingDuration - half);
        runtime.OnMain(() => frames.Advance(AttachmentFlight.SettlingDuration - half));
        Assert.Equal((new Rect(90, 100, 40, 30), 10.0), made.Surface.States[^1]);
        Assert.Equal(1, made.Surface.OrderOuts);
        Assert.Equal(1, landings);
        Assert.Equal(0, frames.Pending);
    }

    // ED-NEW-perf-motion-10 (the lead's ruling 2026-10-13): the ghost reaches the chip's frame with no step at the landing. The free spring was
    // 0.74 % short at the settling time, so the ghost's last drawn frame stood that far from the chip it is replaced by; now the last drawn
    // step is no larger than the one before it, at 60 and 144 Hz, and the landing state is the chip's frame.
    [Theory]
    [InlineData(60)]
    [InlineData(144)]
    [Trait("Rule", "R-VIEW-4")]
    [Trait("Decision", "ED-NEW-perfmotion-10")]
    public void The_ghost_lands_on_the_chip_with_no_step_larger_than_the_one_before(int hertz)
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameSource = FrameClock.Source.Override(frames);
        var platform = new FlightPlatform { MainScreen = new Rect(100, 50, 1600, 900) };
        var flight = new AttachmentFlight(platform);
        var landings = 0;
        runtime.OnMain(() => flight.Fly(new FlightImage(), new Rect(110, 70, 20, 20), new Rect(890, 650, 40, 30), 6, 10, () => landings++));
        var made = Assert.Single(platform.Created);
        var step = TimeSpan.FromSeconds(1.0 / hertz);
        for (var i = 0; i < 4 * hertz && landings == 0; i++)
        {
            runtime.Advance(step);
            runtime.OnMain(() => frames.Advance(step));
        }
        Assert.Equal(1, landings);
        var xs = made.Surface.States.Select(state => state.Frame.X).ToArray();
        Assert.Equal(790, xs[^1], 9); // the chip's frame
        var end = xs[^1] - xs[^2];
        var before = xs[^2] - xs[^3];
        Assert.True(Math.Abs(end) <= Math.Abs(before), $"the last step is {end:F4} px, the one before {before:F4} px");
        Assert.True(Math.Abs(end) < 0.5, $"the last step is {end:F4} px; the free spring left {780 * 0.0074:F1} px there");
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-084")] // App/Views/Composer/AttachmentFlight.swift:55-61
    public void Reduced_motion_returns_before_ordering_out_an_existing_flight()
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameSource = FrameClock.Source.Override(frames);
        var platform = new FlightPlatform();
        var flight = new AttachmentFlight(platform);
        var image = new FlightImage();
        var landed = 0;
        runtime.OnMain(() => flight.Fly(image, new Rect(0, 0, 20, 20), new Rect(50, 50, 20, 20), 6, 10, () => landed++));
        var first = Assert.Single(platform.Created).Surface;
        platform.ReduceMotion = true;
        runtime.OnMain(() => flight.Fly(image, new Rect(0, 0, 20, 20), new Rect(60, 60, 20, 20), 6, 10));
        Assert.Single(platform.Created);
        Assert.Equal(0, first.OrderOuts);
        runtime.Advance(AttachmentFlight.SettlingDuration);
        runtime.OnMain(() => frames.Advance(AttachmentFlight.SettlingDuration));
        Assert.Equal(1, first.OrderOuts);
        Assert.Equal(1, landed);
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-084")] // App/Views/Composer/AttachmentFlight.swift:56-61
    public void Missing_image_or_main_screen_returns_after_old_flight_is_ordered_out()
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameSource = FrameClock.Source.Override(frames);
        var platform = new FlightPlatform();
        var flight = new AttachmentFlight(platform);
        var image = new FlightImage();
        runtime.OnMain(() => flight.Fly(image, new Rect(0, 0, 20, 20), new Rect(50, 50, 20, 20), 6, 10));
        var first = Assert.Single(platform.Created).Surface;
        platform.ImageAvailable = false;
        runtime.OnMain(() => flight.Fly(image, new Rect(0, 0, 20, 20), new Rect(60, 60, 20, 20), 6, 10));
        Assert.Equal(1, first.OrderOuts);
        Assert.Single(platform.Created);
        platform.ImageAvailable = true;
        runtime.OnMain(() => flight.Fly(image, new Rect(0, 0, 20, 20), new Rect(50, 50, 20, 20), 6, 10));
        var second = platform.Created[1].Surface;
        platform.MainScreen = null;
        runtime.OnMain(() => flight.Fly(image, new Rect(0, 0, 20, 20), new Rect(60, 60, 20, 20), 6, 10));
        Assert.Equal(1, second.OrderOuts);
        Assert.Equal(2, platform.Created.Count);
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-083")] // App/Views/Composer/AttachmentFlight.swift:56-59,111-123
    public void A_second_flight_orders_out_the_first_before_it_starts()
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameSource = FrameClock.Source.Override(frames);
        var platform = new FlightPlatform();
        var flight = new AttachmentFlight(platform);
        var image = new FlightImage();
        var firstLanding = 0;
        runtime.OnMain(() => flight.Fly(image, new Rect(0, 0, 20, 20), new Rect(50, 50, 20, 20), 6, 10, () => firstLanding++));
        var first = Assert.Single(platform.Created).Surface;
        runtime.OnMain(() => flight.Fly(image, new Rect(20, 20, 20, 20), new Rect(100, 100, 20, 20), 6, 10));
        Assert.Equal(1, first.OrderOuts);
        Assert.Equal(2, platform.Created.Count);
        runtime.Advance(AttachmentFlight.SettlingDuration);
        runtime.OnMain(() => frames.Advance(AttachmentFlight.SettlingDuration));
        Assert.Equal(1, firstLanding); // Swift :111-116 captures the old flight and never cancels its completion.
        Assert.Equal(1, platform.Created[1].Surface.OrderOuts);
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-084")] // App/Views/Composer/AttachmentFlight.swift:189-204
    [Trait("Req", "UI-COMPOSER-080")] // App/Views/Composer/AttachmentFlight.swift:189-207
    public void Arrival_consumes_the_drop_before_the_reduced_motion_check_and_uses_chip_radius_ten()
    {
        using var runtime = new TestRuntime();
        var platform = new FlightPlatform { ReduceMotion = true };
        var flight = new AttachmentFlight(platform);
        var drops = new AttachmentDrops();
        var image = new FlightImage();
        runtime.OnMain(() => drops.Arm(image, new Rect(5, 6, 7, 8), radius: 4));
        var arrival = new AttachmentArrival(true, drops, flight);
        runtime.OnMain(() => arrival.Report(new Rect(30, 40, 50, 60)));
        Assert.Null(runtime.OnMain(drops.Take));
        Assert.Empty(platform.Created);

        platform.ReduceMotion = false;
        runtime.OnMain(() => arrival.Update(false));
        var frameClock = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(frameClock);
        runtime.OnMain(() => drops.Arm(image, new Rect(5, 6, 7, 8), radius: 4));
        runtime.OnMain(() => arrival.Report(new Rect(30, 40, 50, 60)));
        Assert.Empty(platform.Created);
        var fresh = new AttachmentArrival(true, drops, flight);
        runtime.OnMain(() => fresh.Report(new Rect(30, 40, 50, 60)));
        Assert.Single(platform.Created);
        Assert.Equal(4, platform.Created[0].Surface.States[0].Radius);
        runtime.Advance(AttachmentFlight.SettlingDuration);
        runtime.OnMain(() => frameClock.Advance(AttachmentFlight.SettlingDuration));
        Assert.Equal(10, platform.Created[0].Surface.States[^1].Radius);
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-084")] // App/Views/Composer/AttachmentFlight.swift:192-204 ScreenFrameReader is the background of the content: it reports the chip's own frame
    public void Arrival_reports_the_chips_frame_not_the_wrapper_frame() => headless.OnUiThread(() =>
    {
        var chip = new Avalonia.Controls.Border
        {
            Width = 72, Height = 32,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        var arrival = new AttachmentArrival(false, new AttachmentDrops(), new AttachmentFlight(new FlightPlatform()), chip);
        var window = new Avalonia.Controls.Window { Width = 240, Height = 80, Content = arrival };
        window.Show(); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var reports = new List<Rect>();
        var reader = arrival.Children.OfType<ScreenFrameReader>().Single();
        reader.OnChange = reports.Add;
        window.Width = 241; window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.NotEmpty(reports);
        Assert.Equal(72, reports[^1].Width);
        Assert.Equal(32, reports[^1].Height);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-084")] // App/Views/Composer/AttachmentFlight.swift:167-169 the reader's bounds convert through the view hierarchy, so a transform is part of the frame
    public void The_screen_frame_includes_a_visual_transform() => headless.OnUiThread(() =>
    {
        var reports = new List<Rect>();
        var reader = new ScreenFrameReader(reports.Add)
        {
            Width = 72, Height = 32, RenderTransform = new Avalonia.Media.ScaleTransform(2, 2),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
        };
        var window = new Avalonia.Controls.Window { Width = 300, Height = 200, Content = reader };
        window.Show(); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(144, reports[^1].Width);
        Assert.Equal(64, reports[^1].Height);
        window.Close();
    });
}
