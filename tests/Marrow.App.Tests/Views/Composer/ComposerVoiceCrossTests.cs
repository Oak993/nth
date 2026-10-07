using Avalonia.Automation;
using Avalonia.Controls;
using Marrow.App.Port.Voice;
using Marrow.App.Views.Composer;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Session;
using Xunit;
using static Marrow.App.Tests.Fixtures.VoicePillProbe;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;

namespace Marrow.App.Tests.Views.Composer;

// VT-12, V-T74 and V-T75 (voice-SPEC V-12.3, V-12.5, V-12.6, V-12.8): the cross in its two places and the text field's right edge. The cross is named Cancel voice input with the tooltip Cancel (Esc) and is a tab
// stop only while live; in the pill it is a 24 px disc at the near end (inline: left 8; float: right 8); beside the orb it slides to -(D * 1.16 / 2 + 7) on 0.42 s ez with its opacity 0.24 s after 0.04 s; the text's
// right edge stops 10 px before the pill (inline) or the cross, eased over 0.42 s ez. Every session is scripted; a quiet room keeps the label on Listening.
[Collection("Headless")]
public sealed class ComposerVoiceCrossTests(HeadlessSession headless)
{
    private static ComposerTextColumn Column(ComposerView view) => One<ComposerTextColumn>(view);

    [Fact] // V-T74: the cross in the inline pill
    [Trait("Decision", "ED-292")]
    public void In_the_inline_pill_the_cross_is_a_24_disc_8_from_the_near_end_named_and_a_tab_stop_only_while_live()
    {
        headless.OnUiThread(() =>
        {
            using var rig = ComposerVoicePillGeometryTests.Quiet();
            var (view, window) = rig.Open(900);
            rig.Main.OnMain(() =>
            {
                var cross = Pill(view).Cross;
                Assert.Equal("Cancel voice input", AutomationProperties.GetName(cross));
                Assert.Equal("Cancel (Esc)", ToolTip.GetTip(cross));
                Assert.False(cross.IsTabStop); // not live
                Assert.False(cross.IsHitTestVisible);
                Assert.Equal(VoiceCrossLook.Disc, cross.Look);
                Assert.False(Slot(view).Cross.IsTabStop);
            });
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            Run(rig, window, 1);
            rig.Main.OnMain(() =>
            {
                var pill = Pill(view);
                var rect = Frame(pill.Cross, pill);
                Assert.Equal(8, rect.X, 0.6);
                Assert.Equal(8, rect.Y, 0.6);
                Assert.Equal(24, rect.Width, 0.6);
                Assert.Equal(24, rect.Height, 0.6);
                Assert.True(pill.Cross.IsTabStop);
                Assert.True(pill.Cross.IsHitTestVisible);
                Assert.False(Slot(view).Cross.IsTabStop); // the cross beside the orb is not in this style
            });
            rig.Main.OnMain(() => Click(Pill(view).Cross)); // the cross cancels
            Run(rig, window, 0.3);
            rig.Main.OnMain(() =>
            {
                Assert.Equal(VoiceSessionState.Rest, view.Voice!.Session.State);
                Assert.False(Pill(view).Cross.IsTabStop);
            });
            rig.Close(window);
        });
    }

    [Fact] // V-T74: the cross in the floating pill is at its right end
    [Trait("Decision", "ED-292")]
    public void In_the_floating_pill_the_cross_is_at_the_right_end()
    {
        headless.OnUiThread(() =>
        {
            using var rig = ComposerVoicePillGeometryTests.Quiet(VoiceOrbSettings.Default with { ListeningStyle = VoiceListeningStyle.Float });
            var (view, window) = rig.Open(900);
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            Run(rig, window, 1.2);
            rig.Main.OnMain(() =>
            {
                var pill = Pill(view);
                var rect = Frame(pill.Cross, pill);
                Assert.Equal(pill.Bounds.Width - 8 - 24, rect.X, 0.6);
                Assert.Equal(10, rect.Y, 0.6); // centred in the 44 px pill
            });
            rig.Close(window);
        });
    }

    [Fact] // V-T74: beside the orb the cross slides out and in
    [Trait("Decision", "ED-292")]
    public void Beside_the_orb_the_cross_tucks_behind_it_slides_out_by_xo_on_ez_and_is_a_tab_stop_only_while_live()
    {
        headless.OnUiThread(() =>
        {
            using var rig = ComposerVoicePillGeometryTests.Quiet(VoiceOrbSettings.Default with { ListeningStyle = VoiceListeningStyle.Cross });
            var (view, window) = rig.Open(900);
            var centre = rig.Main.OnMain(() => OrbCentre(view));
            rig.Main.OnMain(() =>
            {
                var cross = Slot(view).Cross;
                Assert.Equal(VoiceCrossLook.Beside, cross.Look);
                Assert.False(cross.IsTabStop);
                Assert.False(cross.IsHitTestVisible);
                Assert.Equal(0, cross.Opacity);
                Assert.Equal(Slot(view).Diameter / 2, Canvas.GetLeft(cross) + cross.Width, 0.6); // tucked: its box's right edge at the slot's centre (drawn at scale 0.55)
            });
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            var seen = Frames(rig, window, 40, () => (Slide: Slot(view).CrossSlide, Opacity: Slot(view).CrossOpacity, Right: Frame(Slot(view).Cross, view).Right));
            var xo = -(32 * 1.16 / 2 + 7);
            for (var k = 0; k < seen.Count; k++)
            {
                var t = k * FrameTime;
                Assert.Equal(CssCurves.At(CssCurves.Ez, t, 0.42), seen[k].Slide, 1e-4); // transform .42s ez
                Assert.Equal(CssCurves.At(CssCurves.Ease, t - 0.04, 0.24), seen[k].Opacity, 1e-4); // opacity .24s ease after .04s
            }
            Assert.Equal(centre.X + xo, seen[^1].Right, 0.7);
            rig.Main.OnMain(() =>
            {
                Assert.True(Slot(view).Cross.IsTabStop);
                Assert.True(Slot(view).Cross.IsHitTestVisible);
                Assert.False(Pill(view).Open); // no pill in this style
            });
            rig.Main.OnMain(() => Click(Slot(view).Cross));
            var back = Frames(rig, window, 30, () => (Slot(view).CrossSlide, Slot(view).CrossOpacity));
            Assert.Equal(VoiceSessionState.Rest, rig.Main.OnMain(() => view.Voice!.Session.State));
            for (var k = 1; k < back.Count; k++) Assert.Equal(1 - CssCurves.At(CssCurves.Ease, k * FrameTime, 0.2), back[k].Item2, 1e-4); // out: opacity .2s ease
            Assert.Equal(0, back[^1].Item1, 1e-6);
            rig.Main.OnMain(() => Assert.False(Slot(view).Cross.IsTabStop));
            rig.Close(window);
        });
    }

    [Fact] // V-T75, V-12.6: the text field's right edge, per style, eased over 0.42 s ez
    [Trait("Decision", "ED-292")]
    public void The_text_edge_stops_10_before_the_inline_pill_and_before_the_cross_eased_and_the_float_pill_leaves_it()
    {
        headless.OnUiThread(() =>
        {
            foreach (var style in new[] { VoiceListeningStyle.Pill, VoiceListeningStyle.Cross, VoiceListeningStyle.Float })
            {
                using var rig = ComposerVoicePillGeometryTests.Quiet(VoiceOrbSettings.Default with { ListeningStyle = style });
                var (view, window) = rig.Open(900);
                var rest = rig.Main.OnMain(() => Frame(Column(view), view).Right);
                rig.Main.OnMain(() => Click(Slot(view).HitTarget));
                var edges = Frames(rig, window, 60, () => Frame(Column(view), view).Right);
                var last = edges[^1];
                if (style == VoiceListeningStyle.Float)
                {
                    Assert.All(edges, edge => Assert.Equal(rest, edge, 0.6)); // the pill stands above the composer
                    rig.Close(window);
                    continue;
                }
                var stop = rig.Main.OnMain(() => style == VoiceListeningStyle.Pill ? PillRect(view).Left : Frame(Slot(view).Cross, view).Left);
                Assert.Equal(stop - 10, last, 1.0); // 10 px before the pill's left end, or before the cross
                // eased on ez over 0.42 s (the start up to three frames late: the session's turn, the track's turn)
                var travel = rest - last;
                Assert.True(travel > 20, "the edge moved in: " + travel);
                var fits = Enumerable.Range(0, 4).Where(slip => edges.Select((edge, i) => Math.Abs(edge - (rest - travel * CssCurves.At(CssCurves.Ez, (i - slip) * FrameTime, 0.42)))).All(error => error < 1.2)).ToList();
                Assert.True(fits.Count > 0, "the edge follows ez: " + string.Join(", ", edges.Take(20).Select(e => e.ToString("F1", System.Globalization.CultureInfo.InvariantCulture))));
                rig.Main.OnMain(() => Click(Slot(view).HitTarget)); // stop, and let the session land
                Run(rig, window, 4);
                Assert.Equal(rest, rig.Main.OnMain(() => Frame(Column(view), view).Right), 0.6); // back at rest
                rig.Close(window);
            }
        });
    }
}
