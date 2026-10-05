using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Session;
using Xunit;
using static Marrow.App.Tests.Fixtures.VoicePillProbe;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;

namespace Marrow.App.Tests.Views.Composer;

// V-T74 / V-12.8: actual routed pointer input must reach the cancel control in every style.
// V-T88 / OD-0076: the disc's press follows the design's .vpill.open .px transform .38s ez.
[Collection("Headless")]
public sealed class ComposerVoicePointerTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(VoiceListeningStyle.Pill)]
    [InlineData(VoiceListeningStyle.Float)]
    [InlineData(VoiceListeningStyle.Cross)]
    [Trait("Decision", "ED-292")]
    public void The_visible_cross_cancels_through_real_window_pointer_input(VoiceListeningStyle style)
    {
        headless.OnUiThread(() =>
        {
            using var rig = ComposerVoicePillGeometryTests.Quiet(VoiceOrbSettings.Default with { ListeningStyle = style });
            var area = rig.Main.OnMain(() =>
            {
                var made = new ComposerArea(rig.Runtime, workingDirectory: null);
                AppEnvironment.SetAppModel(made, rig.App);
                return made;
            });
            var window = rig.Main.OnMain(() => Show(area, rig.App, 1600));
            rig.PumpWindow(window);
            var view = One<ComposerView>(area);
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            Run(rig, window, 1);
            var cross = style == VoiceListeningStyle.Cross ? Slot(view).Cross : Pill(view).Cross;
            var at = rig.Main.OnMain(() => cross.TranslatePoint(new Point(12, 12), window)!.Value);
            Assert.True(new Rect(window.ClientSize).Contains(at), "the cross is inside the window");
            rig.Main.OnMain(() =>
            {
                window.MouseDown(at, MouseButton.Left);
                window.MouseUp(at, MouseButton.Left);
            });
            rig.PumpWindow(window);
            Assert.Equal(VoiceSessionState.Rest, rig.Main.OnMain(() => view.Voice!.State));
            rig.Close(window);
        });
    }

    [Fact]
    [Trait("Decision", "ED-292")]
    public void Pressing_the_disc_eases_its_drawn_scale_on_the_design_curve()
    {
        headless.OnUiThread(() =>
        {
            using var rig = ComposerVoicePillGeometryTests.Quiet();
            var (view, window) = rig.Open(900);
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            Run(rig, window, 1);
            var cross = Pill(view).Cross;
            var at = rig.Main.OnMain(() => cross.TranslatePoint(new Point(12, 12), window)!.Value);
            rig.Main.OnMain(() => window.MouseDown(at, MouseButton.Left));
            var samples = Frames(rig, window, 30, () => (cross.PressScale,
                Width: cross.TranslatePoint(new Point(24, 0), window)!.Value.X - cross.TranslatePoint(default, window)!.Value.X));
            for (var k = 0; k < samples.Count; k++)
            {
                var expected = 1 - 0.08 * CssCurves.At(CssCurves.Ez, k * FrameTime, 0.38);
                Assert.Equal(expected, samples[k].PressScale, 1e-4);
                Assert.Equal(24 * expected, samples[k].Width, 0.05);
            }
            rig.Main.OnMain(() => window.MouseUp(at + new Vector(100, 0), MouseButton.Left));
            var released = Frames(rig, window, 30, () => cross.PressScale);
            for (var k = 0; k < released.Count; k++)
                Assert.Equal(0.92 + 0.08 * CssCurves.At(CssCurves.Ez, k * FrameTime, 0.38), released[k], 1e-4);
            rig.Close(window);
        });
    }
}
