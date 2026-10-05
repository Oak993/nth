using Avalonia.Media;
using Marrow.App.Port.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models.ComposerLights;
using static Marrow.App.Tests.Fixtures.FrameCurves;

namespace Marrow.App.Tests.Port.Settings;

// customization-SPEC CZ-14.1 (every transition of the page, with its timing, on the frame clock: CZ-T40), CZ-T41 (reduce motion lands every transition at once and stills the glyphs), OD-0076 (nothing cuts).
// Each transition is sampled at the 60 Hz frames after its action and held to the named CSS timing function (solved in the fixture, not by the port): monotonic, within 0.02 of the travel, no step larger than the curve's own.
[Collection("Headless")]
public sealed class CustomizationMotionTests(HeadlessSession headless)
{
    private static (double Min, double Max) Channel(Color from, Color to)
    {
        var travel = new[] { to.R - from.R, to.G - from.G, to.B - from.B }.Select((delta, i) => (delta: Math.Abs(delta), i)).MaxBy(x => x.delta);
        return travel.i switch { 0 => (from.R, to.R), 1 => (from.G, to.G), _ => (from.B, to.B) };
    }

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T40, CZ-14.1: the page's rows: dimmed row 0.3 s ease; section fold 0.34 s ease with the fade 0.24 s after 0.08 s (open) and 0.14 s (shut); detail crossfade; Advanced chevron 0.3 s ez and nest fold; Reset idle 0.25 s
    [Trait("Spec", "CZ-T40")]
    public void The_pages_row_transitions_follow_their_curves_on_the_frame_clock() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig();
        rig.Advance(1500);
        var beam = rig.Page.Beam!;
        var aura = rig.Page.Aura!;

        // a row dims: its text to one half over 0.3 s ease (CZ-12.2.7)
        rig.Do(() => rig.App.Settings.ComposerLight = ComposerLightSettings.Default with { Beam = BorderBeamOptions.Default.WithType(BeamType.PulseInner) });
        Check("dimmed row", Sample(rig, () => beam.ColourDrift.Row.TextOpacity, 24), 1, .5, 0, .3, Ease);
        rig.Do(() => rig.App.Settings.ComposerLight = ComposerLightSettings.Default);
        Check("undimmed row", Sample(rig, () => beam.ColourDrift.Row.TextOpacity, 24), .5, 1, 0, .3, Ease);
        rig.Advance(500);

        // a detail changes: the old sentence out in 0.1 s, the new in over 0.2 s after 0.08 s (CZ-12.9)
        rig.Do(() => rig.Page.Beam!.Motion.Control.Select("pulse"));
        var outgoing = Sample(rig, () => beam.Motion.Row.Detail.OutgoingOpacity, 12);
        Check("detail out", outgoing, 1, 0, 0, .1, Ease);
        rig.Do(() => rig.Page.Beam!.Motion.Control.Select("rotate"));
        Check("detail in", Sample(rig, () => beam.Motion.Row.Detail.IncomingOpacity, 24), 0, 1, .08, .2, Ease);
        rig.Advance(500);

        // the style changes: the old style's sections fold shut (0.34 s, faded out in 0.14 s), the new one's open (0.34 s, faded in over 0.24 s after 0.08 s)
        rig.Do(() => rig.Page.StyleCards!.Select("aura"));
        var shutting = Sample(rig, () => beam.Look.Fraction, 30);
        Check("section folds shut", shutting, 1, 0, 0, .34, Ease);
        rig.Do(() => rig.Page.StyleCards!.Select("beam"));
        Check("section folds open", Sample(rig, () => beam.Look.Fraction, 30), 0, 1, 0, .34, Ease);
        rig.Do(() => rig.Page.StyleCards!.Select("aura"));
        Check("section fades out", Sample(rig, () => beam.Look.Fade, 30), 1, 0, 0, .14, Ease);
        rig.Do(() => rig.Page.StyleCards!.Select("beam"));
        Check("section fades in", Sample(rig, () => beam.Look.Fade, 30), 0, 1, .08, .24, Ease);
        rig.Advance(500);

        // Advanced opens: the chevron turns over 0.3 s ez, the nest folds open over 0.34 s ease (the divider above it fades in 0.3 s)
        rig.Do(() => beam.AdvancedButton.Open = true);
        var chevron = Sample(rig, () => beam.AdvancedButton.Chevron.Angle, 24);
        Check("advanced chevron", chevron, 0, 180, 0, .3, Ez);
        rig.Do(() => beam.AdvancedButton.Open = false);
        Check("advanced nest folds shut", Sample(rig, () => beam.AdvancedNest.Fraction, 30), 1, 0, 0, .34, Ease);
        rig.Do(() => beam.AdvancedButton.Open = true);
        Check("advanced nest folds open", Sample(rig, () => beam.AdvancedNest.Fraction, 30), 0, 1, 0, .34, Ease);
        rig.Advance(500);

        // Reset wakes from idle: 0.42 to 1 over 0.25 s ease
        rig.Do(() => rig.App.Settings.ComposerLight = ComposerLightSettings.Default with { Beam = BorderBeamOptions.Default with { Duration = 5 } });
        Check("reset wakes", Sample(rig, () => beam.ResetButton.Opacity, 18), .42, 1, 0, .25, Ease);
        _ = aura;
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T40, CZ-14.1 "Preview maker": the chip's name crossfades and its width eases 0.3 s ez; the dot's colour eases 0.35 s
    [Trait("Spec", "CZ-T40")]
    public void The_preview_makers_transitions_follow_their_curves_on_the_frame_clock() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig();
        rig.Advance(1500);
        var chip = rig.Page.Stage!.Switch.Chip;
        var from = (Width: chip.ValueLabel.Width, Dot: chip.Dot.Value);
        rig.Do(() => rig.Page.Session!.Pick("Claude"));
        var to = (Width: chip.ValueLabel.Width, Dot: chip.Dot.Target);
        var widths = Sample(rig, () => chip.ValueLabel.Width, 24);
        Check("chip width", widths, from.Width, widths[^1], 0, .3, Ez);
        Assert.NotEqual(from.Width, widths[^1]);
        rig.Do(() => rig.Page.Session!.Pick("OpenAI"));
        var label = Sample(rig, () => chip.ValueLabel.IncomingOpacity, 24);
        Check("chip name in", label, 0, 1, .08, .2, Ease);
        rig.Advance(500);
        rig.Do(() => rig.Page.Session!.Pick("Claude"));
        var dot = Channel(from.Dot, to.Dot);
        var read = new Func<double>(() =>
        {
            var c = chip.Dot.Value;
            var travel = new[] { to.Dot.R - from.Dot.R, to.Dot.G - from.Dot.G, to.Dot.B - from.Dot.B }.Select((d, i) => (Math.Abs(d), i)).MaxBy(x => x.Item1).i;
            return travel switch { 0 => c.R, 1 => c.G, _ => c.B };
        });
        Check("chip dot", Sample(rig, read, 24), dot.Min, dot.Max, 0, .35, Ease);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T40, CZ-14.1 "Preview state": the typing placeholder crossfade 0.15 s / 0.12 s and back over 0.25 s; the dots fade 0.4 s ease-in-out; the ring's arc 0.6 s and its spin eased in with a 0.35 s time constant
    [Trait("Spec", "CZ-T40")]
    public void The_preview_states_transitions_follow_their_curves_on_the_frame_clock() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig();
        rig.Advance(1500);
        var preview = rig.Page.Stage!.Preview;
        rig.Do(() => rig.Page.PreviewStatePick.Select("typing"));
        Check("placeholder out", Sample(rig, () => preview.HintOpacity, 15), 1, 0, 0, .15, Ease);
        rig.Do(() => rig.Page.PreviewStatePick.Select("rest"));
        Check("placeholder back", Sample(rig, () => preview.HintOpacity, 24), 0, 1, 0, .25, Ease);
        rig.Advance(500);
        rig.Do(() => rig.Page.PreviewStatePick.Select("typing"));
        Check("typed text in", Sample(rig, () => preview.Typed.Opacity, 12), 0, 1, 0, .12, Ease);
        rig.Do(() => rig.Page.PreviewStatePick.Select("rest"));
        Check("typed text out", Sample(rig, () => preview.Typed.Opacity, 24), 1, 0, 0, .25, Ease);
        rig.Advance(500);

        rig.Do(() => rig.Page.PreviewStatePick.Select("running"));
        var arc = Sample(rig, () => preview.Ring.Fraction, 40);
        Check("ring arc", arc, PreviewRing.RestFraction, PreviewRing.RunningFraction, 0, .6, Ease);
        rig.Do(() => rig.Page.PreviewStatePick.Select("rest"));
        rig.Advance(2500);
        rig.Do(() => rig.Page.PreviewStatePick.Select("running"));
        var dots = Sample(rig, () => preview.DotsOpacity, 30);
        Check("dots in", dots, 0, 1, 0, .4, EaseInOut);

        // the spin: 330 degrees a second, eased in with a time constant of 0.35 s (the angle is the integral of the eased speed)
        rig.Do(() => rig.Page.PreviewStatePick.Select("rest"));
        rig.Advance(3000);
        double before = 0;
        rig.Do(() => before = preview.Ring.Angle);
        rig.Do(() => rig.Page.PreviewStatePick.Select("running"));
        var spin = Sample(rig, () => preview.Ring.Angle, 60);
        for (var k = 1; k < spin.Count; k++)
        {
            var t = k * FrameMs / 1000;
            var expected = before + 330 * (t - PreviewRing.TimeConstant * (1 - Math.Exp(-t / PreviewRing.TimeConstant)));
            Assert.True(Math.Abs(spin[k] - expected) <= 3.2, $"ring spin frame {k}: {spin[k]:F1}, the eased spin {expected:F1}");
            Assert.True(spin[k] >= spin[k - 1], $"ring spin frame {k}: not monotonic");
        }
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T41, CZ-14.1: under reduce motion every one of these lands at once
    [Trait("Spec", "CZ-T41")]
    public void Under_reduce_motion_every_transition_lands_at_once() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(reduceMotion: true);
        var beam = rig.Page.Beam!;
        var preview = rig.Page.Stage!.Preview;
        rig.Do(() => rig.App.Settings.ComposerLight = ComposerLightSettings.Default with { Beam = BorderBeamOptions.Default.WithType(BeamType.PulseInner) });
        Assert.Equal(.5, beam.ColourDrift.Row.TextOpacity);
        rig.Do(() => beam.Motion.Control.Select("rotate"));
        Assert.Equal(1, beam.Motion.Row.Detail.IncomingOpacity);
        Assert.Equal(0, beam.Motion.Row.Detail.OutgoingOpacity);
        rig.Do(() => rig.Page.StyleCards!.Select("aura"));
        Assert.Equal((0, 0), (beam.Look.Fraction, beam.Look.Fade));
        Assert.Equal((1, 1), (rig.Page.Aura!.Look.Fraction, rig.Page.Aura.Look.Fade));
        rig.Do(() => rig.Page.Aura.AdvancedButton.Open = true);
        Assert.Equal(180, rig.Page.Aura.AdvancedButton.Chevron.Angle);
        Assert.Equal(1, rig.Page.Aura.AdvancedNest.Fraction);
        rig.Do(() => rig.Page.Session!.Pick("Claude"));
        Assert.Equal(1, rig.Page.Stage.Switch.Chip.ValueLabel.IncomingOpacity);
        Assert.Equal(rig.Page.Stage.Switch.Chip.Dot.Target, rig.Page.Stage.Switch.Chip.Dot.Value);
        rig.Do(() => rig.Page.PreviewStatePick.Select("running"));
        Assert.Equal(PreviewRing.RunningFraction, preview.Ring.Fraction);
        // (the working dots' 0.4 s fade is the Mac's own un-gated transition, ComposerView.swift:672: Reduce Motion leaves it, OD-0099 Q-RM A)
        Assert.True(preview.DotsShown);
        rig.Do(() => rig.Page.PreviewStatePick.Select("typing"));
        rig.Advance(20); // the preview's next frame
        Assert.Equal((0, 1), (preview.HintOpacity, preview.Typed.Opacity));
        Assert.Equal(ComposerPreview.TypedString, preview.TypedText);
        // the style cards' glyphs are still frames: with nothing moving, nothing waits for a frame
        rig.Do(() => rig.Page.PreviewStatePick.Select("rest"));
        rig.Advance(500);
        Assert.Equal(0, rig.Frames.Pending);
    });

    [Fact]
    [Trait("Decision", "ED-251")] // CZ-T41, CZ-5.3: the glyphs of the style cards turn only while they are seen, and stop when the user asks for less motion
    [Trait("Spec", "CZ-T41")]
    public void The_style_glyphs_turn_while_seen_and_stop_under_reduce_motion() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig();
        rig.Advance(200);
        Assert.True(rig.Frames.Pending > 0, "the glyphs wait for a frame");
        rig.Do(() => Marrow.UI.Port.EnvironmentValues.SetAccessibilityReduceMotion(rig.Host, true));
        rig.Advance(100);
        rig.Advance(100);
        Assert.Equal(0, rig.Frames.Pending);
    });
}
