using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

// UI-SETTINGS-012, SettingsView.swift:296-305 (the system switch, controlSize .small, accent tint). Its look and motion are the Mac's, measured in
// the reference runtime (ED-NEW-settingsview; E:\Droppy\work\reference-video, CATALOGUE settings.switch-toggle, 1 px = 1 pt): the on switch is a
// 44 x 20 accent track holding a white 26 x 16 knob 2 in from every edge (S 178.0); the off track is the label colour at 0.10 (S 42.5); a press
// widens the knob to 30 and 2 taller about its own centre, into a lens 0.48 white over the accent and about 0.6 white over the off track (25 to 29-30 in 80-100 ms; frames/w-1-settings-tour-24.0/win row y 346); the commit slides the knob its 14 points in about 0.21 s, ease-out (fractions 0.13 at 22 ms,
// 0.38 at 54, 0.62 at 94, 0.83 at 144, 0.98 at 197, 1 at 210), while the track crossfades over 0.25 s.
[Collection("Headless")]
public sealed class SmallSwitchMotionTests(HeadlessSession headless) : SettingsFinishFixture(headless)
{
    private static SmallSwitch Switch(Control view, string row) => Assert.Single(Nodes<SmallSwitch>(Row(view, row)));

    [Fact]
    [Trait("Rule", "R-TEST-4")] // UI-SETTINGS-012: the measured geometry and motion; clicking, Space, the pointer rules, the accent and the disabled row are proven in SettingsViewFinishGeneralTests
    public void Switch_is_44_by_20_with_a_26_by_16_knob_that_slides_14_points_in_about_0_21_seconds_while_the_track_crossfades_over_0_25()
    {
        With(() => new GeneralSettingsPage(), (view, model, h, w) =>
        {
            var s = model.Settings; s.ShowReasoning = false; Drain(w); Tick(h, 300);
            var toggle = Switch(view, "Show thinking");
            Assert.Equal(44, toggle.Bounds.Width, 3); Assert.Equal(20, toggle.Bounds.Height, 3);
            Assert.Equal(new Rect(2, 2, 26, 16), toggle.KnobNow()); // off: the knob at the left, 2 in from the track's edges
            Assert.Equal(Chrome.Overlay(0.10).Resolve(toggle.ActualThemeVariant), toggle.TrackNow()); // off: the label colour at 0.10
            var accent = Chrome.AccentNSColor.Resolve(toggle.ActualThemeVariant);

            // Space commits with no press, so the slide is read alone: the knob's x over the 14 points of travel.
            Assert.True(toggle.Focus()); Press(w, Key.Space); Assert.True(s.ShowReasoning);
            Assert.Equal(2, toggle.KnobNow().X, 6); Assert.Equal(26, toggle.KnobNow().Width, 6); // nothing has moved before the first frame
            double Fraction() => (toggle.KnobNow().X - 2) / 14;
            double[] times = [22, 54, 94, 125, 144, 197, 210]; double[] table = [0.13, 0.38, 0.62, double.NaN, 0.83, 0.98, 1.0]; var last = 0.0; var elapsed = 0.0; var previous = -1.0;
            for (var i = 0; i < times.Length; i++)
            {
                Tick(h, times[i] - elapsed); elapsed = times[i]; var fraction = Fraction();
                Assert.True(fraction > previous || fraction == 1, $"the knob moves on at {times[i]} ms ({previous} to {fraction})"); previous = fraction;
                Assert.Equal(26, toggle.KnobNow().Width, 6); Assert.Equal(2, toggle.KnobNow().Y, 6); Assert.Equal(Slide(times[i] / 1000), fraction, 6); // the port's curve ...
                if (!double.IsNaN(table[i])) Assert.InRange(fraction, table[i] - 0.12, table[i] + 0.12); // ... and the reference runtime's measured fractions (ease-out within 0.12)
                if (times[i] == 125) { var track = toggle.TrackNow(); Assert.InRange(track.A, 27, 254); Assert.NotEqual(accent, track); } // the track is between grey and accent at 125 ms
                last = fraction;
            }
            Assert.Equal(1, last, 6); Assert.Equal(new Rect(16, 2, 26, 16), toggle.KnobNow()); // the right edge is 2 in from the track's: 44 - 2 = 16 + 26
            Tick(h, 60); Assert.Equal(accent, toggle.TrackNow()); // the crossfade has run its 0.25 s

            // And back: the knob returns and the track fades to grey.
            Press(w, Key.Space); Assert.False(s.ShowReasoning); Tick(h, 100); Assert.InRange(Fraction(), 0.01, 0.99); Tick(h, 200);
            Assert.Equal(new Rect(2, 2, 26, 16), toggle.KnobNow()); Assert.Equal(Chrome.Overlay(0.10).Resolve(toggle.ActualThemeVariant), toggle.TrackNow());
        });
    }

    private static double Slide(double seconds) => SmallSwitch.Slide.Progress(seconds);

    // The switch drawn alone on a transparent bitmap: the alpha of the pixel at the knob's centre, and of the track beside the knob (clear of it).
    private static (double Knob, double Track) CentreAndTrackAlpha(SmallSwitch toggle)
    {
        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)toggle.Bounds.Width, (int)toggle.Bounds.Height));
        using (var context = bitmap.CreateDrawingContext()) toggle.Render(context);
        using var stream = new MemoryStream(); bitmap.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        using var pixels = SkiaSharp.SKBitmap.Decode(stream.ToArray()); var knob = toggle.KnobNow();
        return (pixels.GetPixel((int)(knob.X + knob.Width / 2), (int)(knob.Y + knob.Height / 2)).Alpha / 255.0,
            pixels.GetPixel((int)toggle.Bounds.Width - 6, (int)toggle.Bounds.Height / 2).Alpha / 255.0);
    }

    private static int KnobAlpha(SmallSwitch toggle) => (int)Math.Round(CentreAndTrackAlpha(toggle).Knob * 255);

    // The white of the lens over its own track: the knob's coverage with the track's taken out (the lens is drawn over the track).
    private static double LensWhite(SmallSwitch toggle) { var (knob, track) = CentreAndTrackAlpha(toggle); return (knob - track) / (1 - track); }

    // The same over the opaque accent track (an on switch): how far the centre pixel's green has gone from the accent's to white.
    private static double LensWhiteOverAccent(SmallSwitch toggle)
    {
        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)toggle.Bounds.Width, (int)toggle.Bounds.Height));
        using (var context = bitmap.CreateDrawingContext()) toggle.Render(context);
        using var stream = new MemoryStream(); bitmap.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        using var pixels = SkiaSharp.SKBitmap.Decode(stream.ToArray()); var knob = toggle.KnobNow();
        var accent = Chrome.AccentNSColor.Resolve(toggle.ActualThemeVariant);
        return (pixels.GetPixel((int)(knob.X + knob.Width / 2), (int)(knob.Y + knob.Height / 2)).Green - accent.G) / (255.0 - accent.G);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // UI-SETTINGS-012: the pressed look; the commit on release inside and no commit on release outside are proven in SettingsViewFinishGeneralTests and PR-SETTINGS-026's test
    public void A_press_widens_the_knob_to_30_by_18_about_its_centre_and_commits_only_on_release()
    {
        With(() => new GeneralSettingsPage(), (view, model, h, w) =>
        {
            var s = model.Settings; s.ShowReasoning = false; Drain(w); Tick(h, 300);
            var toggle = Switch(view, "Show thinking"); var at = At(toggle, w);
            w.MouseMove(at); Drain(w); w.MouseDown(at, MouseButton.Left); Drain(w);
            Assert.False(s.ShowReasoning); // the value commits on release, not on the press
            Tick(h, 45); Assert.InRange(toggle.KnobNow().Width, 26.5, 29.5); Assert.Equal(26 + 4 * SmallSwitch.Press.Progress(0.045), toggle.KnobNow().Width, 6);
            Tick(h, 60); Assert.Equal(30, toggle.KnobNow().Width, 6); Assert.Equal(18, toggle.KnobNow().Height, 6); Assert.Equal(15, toggle.KnobNow().X + toggle.KnobNow().Width / 2, 6); // the centre stays (the reference's 740.5 held 740.5-741.0), the knob grows both ways (80-100 ms)
            Assert.False(s.ShowReasoning);
            // The pressed knob is the glass lens, where at rest it is opaque white. Over the off track it is 0.58-0.61 white: SOURCE FACT, the reference runtime
            // (frames/w-1-settings-tour-24.0/win row y 346, the off knob's centre x 726, held at 000043-000044 and 000109-000110: (171-178,178-186,180-187)
            // over the off track's (46-47,78-80,78-80)). The track beside the knob stays the label colour at 0.10.
            Assert.InRange(CentreAndTrackAlpha(toggle).Track, 0.05, 0.15);
            Assert.InRange(LensWhite(toggle), 0.57, 0.62);
            w.MouseUp(at, MouseButton.Left); Drain(w); Assert.True(s.ShowReasoning);
            Tick(h, 400); Assert.Equal(new Rect(16, 2, 26, 16), toggle.KnobNow()); Assert.Equal(255, KnobAlpha(toggle)); // released: opaque white again

            // On: a press grows about the centre too (29 at rest).
            w.MouseDown(at, MouseButton.Left); Drain(w); Tick(h, 100); var pressed = toggle.KnobNow(); Assert.Equal(30, pressed.Width, 6); Assert.Equal(29, pressed.X + pressed.Width / 2, 6);
            Assert.InRange(LensWhiteOverAccent(toggle), 0.46, 0.50); // over the accent track the lens is 0.48 white (the reference's centre pixel (242,141,173) over (246,37,103))
            w.MouseMove(new Point(at.X + 200, at.Y)); w.MouseUp(new Point(at.X + 200, at.Y), MouseButton.Left); Drain(w); Assert.True(s.ShowReasoning); // released away from the switch: nothing changes (PR-SETTINGS-026)
            Tick(h, 400); Assert.Equal(new Rect(16, 2, 26, 16), toggle.KnobNow());
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // UI-SETTINGS-012: reduce motion; OD-0076 stills every transition under the system's reduce-motion setting
    public void Under_reduce_motion_the_switch_jumps_to_its_state()
    {
        With(() => new GeneralSettingsPage(), (view, model, h, w) =>
        {
            using var motion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });
            var s = model.Settings; s.ShowReasoning = false; Drain(w); Tick(h, 300);
            var toggle = Switch(view, "Show thinking"); Assert.True(toggle.Focus()); Press(w, Key.Space); Assert.True(s.ShowReasoning);
            Assert.Equal(new Rect(16, 2, 26, 16), toggle.KnobNow()); Assert.Equal(Chrome.AccentNSColor.Resolve(toggle.ActualThemeVariant), toggle.TrackNow()); // no frame has run
        });
    }
}
