using Marrow.Core.Models.ComposerLights;
using Marrow.Core.Port.Voice.Glow;
using Marrow.UI.Port.Voice.Glow;

namespace Marrow.UI.Tests.Port.Voice;

// VT-9, V-T60/V-T62 (voice-SPEC V-13.2, V-13.3): the glow's per-frame state. The glow fades in over 0.6 s and out over 0.5 s on CSS `ease` (voice-glow.js's vb-fade-in /
// vb-fade-out), the driver (VT-4) is stepped only while the glow is active or fading, with the session's level, the processing flag, the theme and the composer's size.
public sealed class VoiceGlowSceneTests
{
    private const double Frame = 1.0 / 60;
    private const double W = 720, H = 96;

    private static VoiceGlowInput Input(bool active, bool processing = false, double level = 0, bool dark = true, bool reduce = false, double w = W, double h = H) =>
        new(active, processing, new VoiceGlowLevel { Value = level }, dark, reduce, w, h);

    // runs the scene `seconds` of 1/60 s frames from `from` and returns the time reached
    private static double Run(VoiceGlowScene scene, double from, double seconds, VoiceGlowInput input)
    {
        var n = (int)Math.Round(seconds / Frame);
        var t = from;
        for (var i = 0; i < n; i++)
        {
            t += Frame;
            scene.Advance(t, input);
        }
        return t;
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Spec", "V-T62")]
    [Trait("Decision", "ED-302")]
    public void Nothing_is_stepped_or_drawn_until_the_glow_is_first_active()
    {
        var scene = new VoiceGlowScene();
        Run(scene, 0, 2, Input(false));
        Assert.Equal(0, scene.Opacity);
        Assert.False(scene.Visible);
        Assert.False(scene.NeedsFrames);
        Assert.Equal(0, scene.DriverSteps);
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Spec", "V-T62")]
    [Trait("Decision", "ED-302")]
    public void Activating_fades_in_over_0_6_seconds_on_the_css_ease_curve()
    {
        var scene = new VoiceGlowScene();
        var on = Input(true);
        scene.Advance(10, on);
        Assert.Equal(0, scene.Opacity, 9);
        Assert.True(scene.NeedsFrames);
        scene.Advance(10.3, on);
        Assert.Equal(CssTiming.Ease(0.5), scene.Opacity, 9);
        Assert.True(scene.Visible);
        scene.Advance(10.45, on);
        Assert.Equal(CssTiming.Ease(0.75), scene.Opacity, 9);
        scene.Advance(10.6, on);
        Assert.Equal(1, scene.Opacity, 9);
        Run(scene, 10.6, 5, on);
        Assert.Equal(1, scene.Opacity);
        Assert.True(scene.NeedsFrames, "an active glow breathes: frames while active");
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Spec", "V-T62")]
    [Trait("Decision", "ED-302")]
    public void Deactivating_fades_out_over_0_5_seconds_and_then_nothing_runs()
    {
        var scene = new VoiceGlowScene();
        var t = Run(scene, 0, 1, Input(true));
        Assert.Equal(1, scene.Opacity, 9);
        var off = Input(false);
        scene.Advance(t, off);
        scene.Advance(t + 0.25, off);
        Assert.Equal(1 - CssTiming.Ease(0.5), scene.Opacity, 9);
        Assert.True(scene.NeedsFrames);
        scene.Advance(t + 0.5, off);
        Assert.Equal(0, scene.Opacity, 9);
        scene.Advance(t + 0.52, off);
        Assert.False(scene.Visible);
        Assert.False(scene.NeedsFrames);
        var steps = scene.DriverSteps;
        Run(scene, t + 0.52, 3, off);
        Assert.Equal(steps, scene.DriverSteps);                 // the driver is not stepped while the glow is gone
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Spec", "V-T62")]
    [Trait("Decision", "ED-336")]
    public void A_fade_out_that_starts_in_the_middle_of_the_fade_in_goes_down_from_where_it_is()
    {
        // ENGINEERING DECISION (OD-0076: nothing cuts): the design's vb-fade-out animation starts from 1 and would snap a half-risen glow up; the port fades from the current opacity
        var scene = new VoiceGlowScene();
        scene.Advance(0, Input(true));
        scene.Advance(0.2, Input(true));
        var risen = scene.Opacity;
        Assert.InRange(risen, 0.1, 0.9);
        var off = Input(false);
        scene.Advance(0.2, off);
        var previous = scene.Opacity;
        Assert.True(previous <= risen + 1e-9, "no jump up at the turn");
        for (var i = 1; i <= 40; i++)
        {
            scene.Advance(0.2 + i * 0.0125, off);
            Assert.True(scene.Opacity <= previous + 1e-12, "it only goes down");
            previous = scene.Opacity;
        }
        Assert.Equal(0, scene.Opacity, 6);
        Assert.False(scene.NeedsFrames);
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Spec", "V-T62")]
    [Trait("Decision", "ED-336")]
    public void Under_reduce_motion_the_glow_comes_and_goes_at_once()
    {
        var scene = new VoiceGlowScene();
        scene.Advance(0, Input(true, reduce: true));
        Assert.Equal(1, scene.Opacity);
        scene.Advance(0.016, Input(false, reduce: true));
        Assert.Equal(0, scene.Opacity);
        Assert.False(scene.NeedsFrames);
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Spec", "V-T60")]
    [Trait("Decision", "ED-302")]
    public void The_driver_gets_the_level_the_theme_and_the_size_and_its_frames_are_the_drivers_own()
    {
        var level = new VoiceGlowLevel();
        var scene = new VoiceGlowScene();
        var resolved = VoiceGlowResolver.Resolve(VoiceGlowComposerProps.For(dark: true, processing: false, reduceMotion: false));
        var driver = new VoiceGlowDriver(resolved.Config);
        var t = 0.0;
        for (var i = 0; i < 240; i++)
        {
            level.Value = i < 120 ? 0 : 0.6;
            t += Frame;
            scene.Advance(t, new VoiceGlowInput(true, false, level, true, false, W, H));
            var expected = driver.Step(Frame, level.Value, W, H);
            Assert.Equal(expected.Level, scene.Frame!.Level, 10);          // the same sequence of steps (the clock's last digit apart)
            Assert.Equal(expected.Glow, scene.Frame.Glow, 10);
            Assert.Equal(expected.W, scene.Frame.W, 10);
            Assert.Equal(expected.BandY[28], scene.Frame.BandY[28], 8);
        }
        Assert.Equal(240, scene.DriverSteps);
        Assert.True(scene.Frame!.Level > 0.3, "the smoothed level has risen toward 0.6");
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Spec", "V-T60")]
    [Trait("Decision", "ED-302")]
    public void The_driver_steps_are_capped_at_0_05_whatever_the_gap_between_frames()
    {
        var scene = new VoiceGlowScene();
        var on = Input(true);
        var t = Run(scene, 0, 0.5, on);
        var before = scene.Frame!.Time;
        scene.Advance(t + 5, on);                                 // five seconds with no frame (a stall)
        Assert.Equal(0.05, scene.Frame.Time - before, 9);
        Assert.Equal(1, scene.Opacity);
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Spec", "V-T60")]
    [Trait("Decision", "ED-302")]
    public void Coming_back_after_it_was_gone_restarts_the_driver_with_a_one_sixtieth_step()
    {
        var scene = new VoiceGlowScene();
        var t = Run(scene, 0, 1, Input(true));
        t = Run(scene, t, 1.2, Input(false));
        Assert.False(scene.Visible);
        var kept = scene.Frame!.Time;
        scene.Advance(t + 30, Input(true));                       // half a minute later
        Assert.Equal(Frame, scene.Frame.Time - kept, 9);          // a registration's first step is 1/60 s, whatever the gap
        scene.Advance(t + 30.02, Input(true));
        Assert.Equal(0.02, scene.Frame.Time - kept - Frame, 9);
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Spec", "V-T60")]
    [Trait("Decision", "ED-302")]
    public void Processing_sweeps_the_glow_and_a_new_theme_reaches_the_look()
    {
        var scene = new VoiceGlowScene();
        var t = Run(scene, 0, 1, Input(true, level: 0.6));
        Assert.Equal(0, scene.Frame!.ScanA);
        Assert.False(scene.Look!.Config.Processing);
        t = Run(scene, t, 1.5, Input(true, processing: true));
        Assert.True(scene.Look!.Config.Processing);
        Assert.True(scene.Frame.ScanA > 0.5);
        Assert.True(scene.Frame.Warp < 0.2);

        Assert.True(scene.Look.Recipe.Dark);
        Assert.Equal(1.15, scene.Look.Recipe.Brightness);
        Run(scene, t, 0.1, Input(true, processing: true, dark: false));
        Assert.False(scene.Look!.Recipe.Dark);
        Assert.Equal(0.95, scene.Look.Recipe.Brightness);         // the light preset's brightness
        Assert.NotNull(scene.Look.Recipe.Core);                   // and its epicentre
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Spec", "V-T60")]
    [Trait("Decision", "ED-302")]
    public void Reduce_motion_stills_the_drivers_decoration_but_not_its_response_to_the_voice()
    {
        var scene = new VoiceGlowScene();
        Run(scene, 0, 2, Input(true, level: 0.8, reduce: true));
        Assert.True(scene.Look!.Config.ReducedMotion);
        Assert.Equal(0, scene.Frame!.Hue);                        // no hue drift
        Assert.Equal(0, scene.Frame.Phase);                       // no flow
        Assert.True(scene.Frame.Level > 0.5);                     // but the level still lifts the glow
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Spec", "V-T62")]
    [Trait("Decision", "ED-302")]
    public void A_frame_of_the_scene_allocates_nothing()
    {
        var scene = new VoiceGlowScene();
        var on = Input(true, level: 0.5);
        var t = Run(scene, 0, 3, on);                             // warmed up: the look and the driver exist
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 300; i++)
        {
            t += Frame;
            scene.Advance(t, on);
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    [Trait("Feature", "VT-9")]
    [Trait("Spec", "V-T62")]
    [Trait("Decision", "ED-302")]
    public void A_new_size_reaches_the_drivers_band()
    {
        var scene = new VoiceGlowScene();
        var t = Run(scene, 0, 1, Input(true, level: 0.6));
        Assert.True(scene.Frame!.HasBand);
        var wide = scene.Frame.BandX[^1] - scene.Frame.BandX[0];
        Run(scene, t, 0.1, Input(true, level: 0.6, w: 480, h: 150));
        Assert.True(scene.Frame.BandX[^1] - scene.Frame.BandX[0] < wide, "the band spans the narrower shape");
        Run(scene, t + 0.1, 0.1, Input(true, level: 0.6, w: 0, h: 0));
        Assert.False(scene.Frame.HasBand);
    }
}
