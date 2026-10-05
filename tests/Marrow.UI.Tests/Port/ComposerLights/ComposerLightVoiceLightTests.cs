using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.Core.Models.ComposerLights;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Port.ComposerLights;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port.ComposerLights;

// VT-9, V-T61 (voice-SPEC V-13.4, OD-0097 B1 to B3): the composer light while you speak. ComposerLightView.VoiceLight is the level the host asks for (0, 0.35 or 1);
// on the render thread the light follows it with tau 0.17 s down and 0.2 s up on the frame clock, snaps below 0.003 and above 0.997, its whole output times the level;
// at 0 it asks for no frames and its motion clock stands still, resuming where it stopped; a change mid-session retargets; reduce motion jumps; the default 1 changes
// nothing. Both light styles. (The mapping from the setting and the session state to the level is Core's VoiceLightTarget.)
[Collection("Headless")]
public sealed class ComposerLightVoiceLightTests(HeadlessSession headless)
{
    private const int Width = 900;
    private const int Height = 132;

    private static (Window Window, ComposerLightView View) Show(ComposerLightSettings settings, bool reduce = false, double? voiceLight = null)
    {
        var view = new ComposerLightView { Settings = settings, ComposerRect = new Rect(40, 40, 820, 52) };
        if (voiceLight is { } v) view.VoiceLight = v;
        if (reduce) EnvironmentValues.SetAccessibilityReduceMotion(view, true);
        var window = new Window
        {
            Width = Width, Height = Height, RequestedThemeVariant = ThemeVariant.Dark, Background = new SolidColorBrush(Color.FromRgb(0x17, 0x17, 0x17)),
            Content = new Panel { Children = { view } },
        };
        window.Show();
        Tick();
        return (window, view);
    }

    private static void Tick()
    {
        for (var i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static void Run(ManualTimeProvider clock, double seconds)
    {
        // the frame clock: 1/60 s a frame, one rendered frame each
        var frames = (int)Math.Round(seconds * 60);
        for (var i = 0; i < frames; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1.0 / 60));
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        Tick();
    }

    private static byte[] Pixels(Window window)
    {
        using var bitmap = window.CaptureRenderedFrame()!;
        var size = bitmap.PixelSize;
        var bytes = new byte[size.Width * size.Height * 4];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), bytes.Length, size.Width * 4); }
        finally { handle.Free(); }
        return bytes;
    }

    // how far the picture is from the flat #171717 field: the sum over every pixel of the largest channel difference
    private static long Light(byte[] pixels)
    {
        long sum = 0;
        for (var o = 0; o < pixels.Length; o += 4)
            sum += Math.Max(Math.Abs(pixels[o] - 0x17), Math.Max(Math.Abs(pixels[o + 1] - 0x17), Math.Abs(pixels[o + 2] - 0x17)));
        return sum;
    }

    private static ComposerLightSettings Aura => ComposerLightSettings.Default with { Style = ComposerLightStyle.Aura };

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Spec", "V-T61")]
    [Trait("Decision", "ED-251")]
    public void The_default_level_is_one_and_changes_nothing() => headless.OnUiThread(() =>
    {
        using var motion = new ComposerLightMotion();
        byte[] first, second;
        {
            var clock = new ManualTimeProvider(); using var bind = SwiftClock.Provider.Override(clock);
            var (window, view) = Show(ComposerLightSettings.Default);
            try
            {
                Assert.Equal(1, view.VoiceLight);
                Assert.Equal(1, view.Handler!.VoiceLight);
                Run(clock, 0.9);
                first = Pixels(window);
                Assert.Equal(1, view.Handler.VoiceLight);
            }
            finally { window.Close(); }
        }
        {
            var clock = new ManualTimeProvider(); using var bind = SwiftClock.Provider.Override(clock);
            var (window, _) = Show(ComposerLightSettings.Default, voiceLight: 1);
            try
            {
                Run(clock, 0.9);
                second = Pixels(window);
            }
            finally { window.Close(); }
        }
        Assert.True(Light(first) > 20000, "the beam lights the composer");
        Assert.Equal(first, second);
    });

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Spec", "V-T61")]
    [Trait("Decision", "ED-251")]
    public void Going_down_it_follows_with_tau_0_17_and_going_up_with_tau_0_2_on_the_frame_clock() => headless.OnUiThread(() =>
    {
        using var motion = new ComposerLightMotion();
        var clock = new ManualTimeProvider(); using var bind = SwiftClock.Provider.Override(clock);
        var (window, view) = Show(ComposerLightSettings.Default);
        try
        {
            var handler = view.Handler!;
            view.VoiceLight = 0;
            Tick();
            Run(clock, 0.51);
            Assert.Equal(Math.Exp(-0.51 / 0.17), handler.VoiceLight, 0.01);       // 95 % of the way down in 0.51 s
            Run(clock, 2);
            Assert.Equal(0, handler.VoiceLight);
            view.VoiceLight = 1;
            Tick();
            Run(clock, 0.60);
            Assert.Equal(1 - Math.Exp(-0.60 / 0.2), handler.VoiceLight, 0.01);    // 95 % of the way up in 0.6 s
            Run(clock, 2);
            Assert.Equal(1, handler.VoiceLight);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Spec", "V-T61")]
    [Trait("Decision", "ED-251")]
    public void At_level_zero_the_light_draws_nothing_and_asks_for_no_frames() => headless.OnUiThread(() =>
    {
        using var motion = new ComposerLightMotion();
        var clock = new ManualTimeProvider(); using var bind = SwiftClock.Provider.Override(clock);
        var (window, view) = Show(ComposerLightSettings.Default);
        try
        {
            var handler = view.Handler!;
            Assert.True(handler.IsAnimating);
            Run(clock, 0.5);
            Assert.True(Light(Pixels(window)) > 20000);
            view.VoiceLight = 0;
            Tick();
            Run(clock, 2);
            Assert.Equal(0, handler.VoiceLight);
            Assert.False(handler.IsAnimating, "no frames are asked for at 0");
            Assert.Equal(0, Light(Pixels(window)));
            var frames = handler.Frames;
            Run(clock, 1);
            Assert.Equal(frames, handler.Frames);                                 // not a frame drawn either
            Assert.Equal(0, Light(Pixels(window)));
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Spec", "V-T61")]
    [Trait("Decision", "ED-251")]
    public void The_lights_motion_clock_stands_still_at_zero_and_resumes_where_it_stopped() => headless.OnUiThread(() =>
    {
        using var motion = new ComposerLightMotion();
        var clock = new ManualTimeProvider(); using var bind = SwiftClock.Provider.Override(clock);
        var (window, view) = Show(ComposerLightSettings.Default);
        try
        {
            var handler = view.Handler!;
            Run(clock, 0.5);
            view.VoiceLight = 0;
            Tick();
            Run(clock, 1.5);
            var stopped = handler.Light!.Clock.Time;
            Assert.Equal(0, handler.VoiceLight);
            Run(clock, 5);                                                        // five seconds with the light away
            Assert.Equal(stopped, handler.Light!.Clock.Time);
            view.VoiceLight = 1;
            Tick();
            Assert.InRange(handler.Light!.Clock.Time - stopped, 0, 0.06);         // it starts again from where it stopped (at most the clock's 50 ms step), not five seconds on
            Run(clock, 0.5);
            Assert.InRange(handler.Light!.Clock.Time - stopped, 0.4, 0.7);
            Assert.True(handler.IsAnimating);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Spec", "V-T61")]
    [Trait("Decision", "ED-251")]
    public void At_a_level_of_0_35_the_light_keeps_running_at_35_percent() => headless.OnUiThread(() =>
    {
        using var motion = new ComposerLightMotion();
        var clock = new ManualTimeProvider(); using var bind = SwiftClock.Provider.Override(clock);
        var (window, view) = Show(ComposerLightSettings.Default with { Beam = BorderBeamOptions.Default with { StaticColors = true } });
        try
        {
            Run(clock, 0.5);
            var full = Light(Pixels(window));
            view.VoiceLight = 0.35;
            Tick();
            Run(clock, 3);
            var dim = Light(Pixels(window));
            Assert.Equal(0.35, view.Handler!.VoiceLight, 0.004);
            Assert.True(view.Handler.IsAnimating, "it keeps running");
            Assert.True(dim > 0);
            Assert.InRange(dim / (double)full, 0.2, 0.5);                          // the beam moved on, but it is a third of the light
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Spec", "V-T61")]
    [Trait("Decision", "ED-251")]
    public void The_uiverse_light_follows_it_too() => headless.OnUiThread(() =>
    {
        using var motion = new ComposerLightMotion();
        var clock = new ManualTimeProvider(); using var bind = SwiftClock.Provider.Override(clock);
        var (window, view) = Show(Aura);
        try
        {
            var handler = view.Handler!;
            Run(clock, 0.5);
            Assert.True(Light(Pixels(window)) > 20000, "the halo is lit");
            view.VoiceLight = 0;
            Tick();
            Run(clock, 2);
            Assert.Equal(0, Light(Pixels(window)));                                // nothing anywhere, halo and rim included
            Assert.False(handler.IsAnimating);
            view.VoiceLight = 1;
            Tick();
            Run(clock, 1.5);
            Assert.True(Light(Pixels(window)) > 20000);
            Assert.True(handler.IsAnimating);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Spec", "V-T61")]
    [Trait("Decision", "ED-251")]
    public void A_change_of_level_in_the_middle_of_a_move_retargets_from_where_it_is() => headless.OnUiThread(() =>
    {
        using var motion = new ComposerLightMotion();
        var clock = new ManualTimeProvider(); using var bind = SwiftClock.Provider.Override(clock);
        var (window, view) = Show(ComposerLightSettings.Default);
        try
        {
            var handler = view.Handler!;
            view.VoiceLight = 0;
            Tick();
            Run(clock, 0.3);
            var mid = handler.VoiceLight;
            Assert.InRange(mid, 0.05, 0.5);
            view.VoiceLight = 0.35;                                                // the setting changed to "dims" during the session
            Tick();
            Run(clock, 1.0 / 60);
            Assert.InRange(handler.VoiceLight, mid, 0.35);                         // no jump: from the middle value
            Run(clock, 2);
            Assert.Equal(0.35, handler.VoiceLight, 0.004);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Spec", "V-T61")]
    [Trait("Decision", "ED-251")]
    public void Under_reduce_motion_the_level_jumps_to_the_target() => headless.OnUiThread(() =>
    {
        using var motion = new ComposerLightMotion();
        var clock = new ManualTimeProvider(); using var bind = SwiftClock.Provider.Override(clock);
        var (window, view) = Show(ComposerLightSettings.Default, reduce: true);
        try
        {
            var handler = view.Handler!;
            Assert.Equal(1, handler.VoiceLight);
            Assert.True(Light(Pixels(window)) > 20000, "the still is drawn");
            view.VoiceLight = 0;
            Tick();
            Assert.Equal(0, handler.VoiceLight);                                   // no clock advanced: it jumped
            Assert.Equal(0, Light(Pixels(window)));
            view.VoiceLight = 0.35;
            Tick();
            Assert.Equal(0.35, handler.VoiceLight);
            view.VoiceLight = 1;
            Tick();
            Assert.Equal(1, handler.VoiceLight);
            Assert.True(Light(Pixels(window)) > 20000);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Spec", "V-T61")]
    [Trait("Decision", "ED-251")]
    public void Without_a_gpu_the_light_is_a_still_and_its_level_jumps_as_well() => headless.OnUiThread(() =>
    {
        // ED-251: the headless (software) renderer draws the light as one still; a fade of the level would be thirty slow frames, so the level takes the target at once
        var clock = new ManualTimeProvider(); using var bind = SwiftClock.Provider.Override(clock);
        var (window, view) = Show(ComposerLightSettings.Default);
        try
        {
            var handler = view.Handler!;
            Assert.False(handler.IsAnimating);
            Assert.True(Light(Pixels(window)) > 20000, "the still is drawn");
            view.VoiceLight = 0.35;
            Tick();
            Assert.Equal(0.35, handler.VoiceLight);
            view.VoiceLight = 0;
            Tick();
            Assert.Equal(0, handler.VoiceLight);
            Assert.Equal(0, Light(Pixels(window)));
            Assert.False(handler.IsAnimating);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Spec", "V-T61")]
    [Trait("Decision", "ED-251")]
    public void A_view_made_with_a_level_starts_at_it_and_the_level_is_kept_in_0_to_1() => headless.OnUiThread(() =>
    {
        using var motion = new ComposerLightMotion();
        var clock = new ManualTimeProvider(); using var bind = SwiftClock.Provider.Override(clock);
        var (window, view) = Show(ComposerLightSettings.Default, voiceLight: 0);
        try
        {
            Assert.Equal(0, view.Handler!.VoiceLight);                              // a first frame never starts from full
            Assert.False(view.Handler.IsAnimating);
            view.VoiceLight = 7;
            Assert.Equal(1, view.VoiceLight);
            view.VoiceLight = -2;
            Assert.Equal(0, view.VoiceLight);
            view.VoiceLight = double.NaN;
            Assert.Equal(1, view.VoiceLight);
        }
        finally { window.Close(); }
    });
}
