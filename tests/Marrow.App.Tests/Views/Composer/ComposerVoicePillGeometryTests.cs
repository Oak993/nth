using Avalonia;
using Marrow.App.Port.Voice;
using Marrow.App.Views.Composer;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Voice;
using Marrow.UI.Port;
using Xunit;
using static Marrow.App.Tests.Fixtures.VoicePillProbe;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;

namespace Marrow.App.Tests.Views.Composer;

// VT-12, V-T72 (voice-SPEC V-12.2, V-12.4, V-15): the pill's geometry in a real composer. Inline it is a disc of H = 40 round the orb, opens to W = H + 8 + label + 8 + 24 + 8 with its right end fixed at the orb's
// centre + H/2 and its centre on the orb's; floating it is H = 44, 52 above the composer's top, opening to the right from the orb's centre - H/2, the orb lifted by -56 on a spring. The material is the shared glass in a capsule,
// tinted pgBg. The label is measured here independently of the production code. A quiet room keeps the label on Listening (no microphone, engine or model: R-TEST-10).
[Collection("Headless")]
public sealed class ComposerVoicePillGeometryTests(HeadlessSession headless)
{
    private const string Listening = "Listening…";

    internal static VoiceComposerRig Quiet(VoiceOrbSettings? orb = null)
    {
        var rig = new VoiceComposerRig(orb);
        rig.Main.OnMain(() => rig.App.Settings.VoiceNotes = new VoiceNotesSettings(Local: true, Cloud: true));
        rig.QuietRoom();
        return rig;
    }

    [Fact] // V-T72
    [Trait("Decision", "ED-292")]
    public void The_inline_pill_is_a_disc_of_H_closed_and_W_open_with_its_right_end_fixed_at_the_orb_centre_plus_half_H_and_centred_on_the_orb()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Quiet();
            var (view, window) = rig.Open(900);
            var closed = rig.Main.OnMain(() => PillRect(view));
            Assert.Equal(40, closed.Width, 0.6);
            Assert.Equal(40, closed.Height, 0.6);
            Assert.Equal(0, rig.Main.OnMain(() => Pill(view).ShownOpacity));
            var centre = rig.Main.OnMain(() => OrbCentre(view));
            Assert.Equal(centre.X + 20, closed.Right, 1.0);
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            var rects = Frames(rig, window, 70, () => PillRect(view));
            var width = OpenWidth(40, Listening, 13.5);
            var last = rects[^1];
            Assert.Equal(width, last.Width, 1.0);
            Assert.Equal(40, last.Height, 0.6);
            Assert.Equal(centre.X + 20, last.Right, 1.0);
            Assert.Equal(centre.Y, last.Top + 20, 1.0); // vertically centred on the orb's centre
            foreach (var rect in rects.Skip(1)) Assert.Equal(centre.X + 20, rect.Right, 1.0); // the right end never moves while it opens
            Assert.Equal(40, rects.Select(r => r.Width).Min(), 1.0);
            Assert.True(rects.Select(r => r.Width).Zip(rects.Select(r => r.Width).Skip(1), (a, b) => b >= a - 0.01).All(up => up), "the width only grows while it opens");
            rig.Close(window);
        });
    }

    [Fact] // V-T72
    [Trait("Decision", "ED-292")]
    public void The_floating_pill_is_44_high_opens_to_the_right_from_the_orb_centre_minus_half_H_52_above_the_composer_and_lifts_the_orb_56()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Quiet(VoiceOrbSettings.Default with { ListeningStyle = VoiceListeningStyle.Float });
            var (view, window) = rig.Open(900);
            var centre = rig.Main.OnMain(() => OrbCentre(view));
            var restTarget = rig.Main.OnMain(() => Frame(Slot(view).HitTarget, view).Center);
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            var lifts = Frames(rig, window, 120, () => Slot(view).Lift);
            var rect = rig.Main.OnMain(() => PillRect(view));
            Assert.Equal(44, rect.Height, 0.6);
            Assert.Equal(OpenWidth(44, Listening, 15), rect.Width, 1.0);
            Assert.Equal(centre.X - 22, rect.Left, 1.0);
            Assert.Equal(-52, rect.Top, 1.0);
            Assert.Equal(1, lifts[^1], 0.01);
            var lifted = rig.Main.OnMain(() => Frame(Slot(view).HitTarget, view).Center);
            Assert.Equal(restTarget.Y - 56, lifted.Y, 1.0); // the orb and its hit target stand in the pill's near cap
            Assert.Equal(restTarget.X, lifted.X, 1.0);
            // the lift is the spring of V-12.2 (response 0.36, damping 0.8): checked against the closed form, a start up to three frames late
            double Spring(double t, double response, double damping)
            {
                var w = 2 * Math.PI / response;
                var wd = w * Math.Sqrt(1 - damping * damping);
                return 1 - Math.Exp(-damping * w * t) * (Math.Cos(wd * t) + damping * w / wd * Math.Sin(wd * t));
            }
            var fits = Enumerable.Range(0, 4).Where(slip => lifts.Select((value, i) => Math.Abs(value - Spring(Math.Max(0, (i - slip) * FrameTime), 0.36, 0.8))).All(error => error < 0.06)).ToList();
            Assert.True(fits.Count > 0, "the lift follows the spring: " + string.Join(", ", lifts.Take(30).Select(v => v.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))));
            rig.Close(window);
        });
    }

    [Fact] // V-T72, V-12.4
    [Trait("Decision", "ED-292")]
    public void The_pill_body_is_the_shared_glass_material_in_a_capsule_tinted_with_pgBg()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Quiet();
            var (view, window) = rig.Open(900);
            rig.Main.OnMain(() =>
            {
                var body = Pill(view).Body;
                var effect = SwiftGlass.GetEffect(body);
                Assert.NotNull(effect);
                Assert.Equal(GlassShapeKind.Capsule, effect!.Shape.Kind);
                Assert.NotNull(effect.TintColor);
                var dark = Avalonia.Styling.ThemeVariant.Dark.Equals(body.ActualThemeVariant);
                Assert.Equal(dark ? 0.50 : 0.56, effect.TintOpacity!.Value, 0.005); // pgBg rgba(18,18,22,.50) dark, rgba(250,249,251,.56) light
                var rgb = effect.TintColor!.Resolve(body.ActualThemeVariant);
                Assert.Equal(dark ? (18, 18, 22) : (250, 249, 251), ((int)rgb.R, (int)rgb.G, (int)rgb.B));
            });
            rig.Close(window);
        });
    }

    [Fact] // V-T72, V-15: the style picked during a session takes effect with the next one
    [Trait("Decision", "ED-292")]
    public void A_style_picked_while_listening_takes_effect_with_the_next_session()
    {
        headless.OnUiThread(() =>
        {
            using var rig = Quiet();
            var (view, window) = rig.Open(900);
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            Run(rig, window, 0.8);
            rig.Main.OnMain(() => rig.App.Settings.VoiceOrb = rig.App.Settings.VoiceOrb with { ListeningStyle = VoiceListeningStyle.Float });
            Run(rig, window, 0.5);
            Assert.Equal(40, rig.Main.OnMain(() => PillRect(view)).Height, 0.6); // still the inline pill
            rig.Main.OnMain(() => Click(Slot(view).HitTarget)); // stop
            Run(rig, window, 3);
            rig.Main.OnMain(() => Click(Slot(view).HitTarget)); // the next session
            Run(rig, window, 1.2);
            Assert.Equal(44, rig.Main.OnMain(() => PillRect(view)).Height, 0.6);
            rig.Close(window);
        });
    }
}
