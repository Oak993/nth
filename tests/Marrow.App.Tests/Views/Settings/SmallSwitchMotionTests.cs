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
// widens the knob to 30 (25 to 29-30 in 80-100 ms); the commit slides the knob its 14 points in about 0.21 s, ease-out (fractions 0.13 at 22 ms,
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

    // The alpha of the pixel at the knob's centre, the switch drawn alone on a transparent bitmap.
    private static int KnobAlpha(SmallSwitch toggle)
    {
        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)toggle.Bounds.Width, (int)toggle.Bounds.Height));
        using (var context = bitmap.CreateDrawingContext()) toggle.Render(context);
        using var stream = new MemoryStream(); bitmap.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        using var pixels = SkiaSharp.SKBitmap.Decode(stream.ToArray()); var knob = toggle.KnobNow();
        return pixels.GetPixel((int)(knob.X + knob.Width / 2), (int)(knob.Y + knob.Height / 2)).Alpha;
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // UI-SETTINGS-012: the pressed look; the commit on release inside and no commit on release outside are proven in SettingsViewFinishGeneralTests and PR-SETTINGS-026's test
    public void A_press_widens_the_knob_to_30_on_the_side_it_leaves_and_commits_only_on_release()
    {
        With(() => new GeneralSettingsPage(), (view, model, h, w) =>
        {
            var s = model.Settings; s.ShowReasoning = false; Drain(w); Tick(h, 300);
            var toggle = Switch(view, "Show thinking"); var at = At(toggle, w);
            w.MouseMove(at); Drain(w); w.MouseDown(at, MouseButton.Left); Drain(w);
            Assert.False(s.ShowReasoning); // the value commits on release, not on the press
            Tick(h, 45); Assert.InRange(toggle.KnobNow().Width, 26.5, 29.5); Assert.Equal(26 + 4 * SmallSwitch.Press.Progress(0.045), toggle.KnobNow().Width, 6);
            Tick(h, 60); Assert.Equal(30, toggle.KnobNow().Width, 6); Assert.Equal(2, toggle.KnobNow().X, 6); // off: the left edge holds, the knob grows to the right (80-100 ms)
            Assert.False(s.ShowReasoning);
            // The pressed knob is the glass lens: translucent white (alpha 0x30) in a lighter rim, where at rest it is opaque white.
            Assert.InRange(KnobAlpha(toggle), 60, 80); // 0x30 of white over the off track's 0x1A: 0.188 + 0.102 x 0.812 = 0.27 of 255 = 69
            w.MouseUp(at, MouseButton.Left); Drain(w); Assert.True(s.ShowReasoning);
            Tick(h, 400); Assert.Equal(new Rect(16, 2, 26, 16), toggle.KnobNow()); Assert.Equal(255, KnobAlpha(toggle)); // released: opaque white again

            // On: a press holds the right edge and grows to the left.
            w.MouseDown(at, MouseButton.Left); Drain(w); Tick(h, 100); var pressed = toggle.KnobNow(); Assert.Equal(30, pressed.Width, 6); Assert.Equal(42, pressed.Right, 6);
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
