using Avalonia;
using Avalonia.Controls;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port.Voice;
using Xunit;
using static Marrow.App.Tests.Fixtures.VoicePillProbe;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;

namespace Marrow.App.Tests.Views.Composer;

// VT-12, V-T86 (voice-SPEC V-12.7): the floating pill fades any line it crosses above the composer (here the changes tab, "N files +x -y") out over 0.16 s ease and back over 0.25 s ease after 0.05 s; a line it does
// not cross stays. The window is narrow, so the centred tab and the pill (which opens to the right of the orb) meet.
[Collection("Headless")]
public sealed class ComposerVoiceFloatLineTests(HeadlessSession headless)
{
    private static string Patch(string path) =>
        "diff --git a/" + path + " b/" + path + "\nnew file mode 100644\n--- /dev/null\n+++ b/" + path + "\n@@ -0,0 +1 @@\n+new\n";

    private static (ComposerArea Area, ComposerView View, TabSlot Tabs, Avalonia.Controls.Window Window) Open(VoiceComposerRig rig, double width)
    {
        rig.Main.OnMain(() => rig.Runtime.RehearseTurn("edit", providerDiff: Patch("src/new.txt")));
        rig.Main.OnMain(rig.Runtime.NoteDiffChanged);
        rig.Main.Loop.Run(() => rig.Runtime.RefreshChangeStats(), AppModelHarness.Bound);
        Assert.NotNull(rig.Runtime.ChangeStats);
        var area = rig.Main.OnMain(() =>
        {
            var made = new ComposerArea(rig.Runtime, workingDirectory: null);
            AppEnvironment.SetAppModel(made, rig.App);
            return made;
        });
        var window = rig.Main.OnMain(() => Show(area, rig.App, width));
        rig.PumpWindow(window);
        return (area, One<ComposerView>(area), One<TabSlot>(area), window);
    }

    [Fact] // V-T86
    [Trait("Decision", "ED-292")]
    public void The_floating_pill_fades_the_line_it_crosses_out_over_0_16_and_back_over_0_25_after_0_05()
    {
        headless.OnUiThread(() =>
        {
            using var rig = ComposerVoicePillGeometryTests.Quiet(VoiceOrbSettings.Default with { ListeningStyle = VoiceListeningStyle.Float });
            var (area, view, tabs, window) = Open(rig, 380);
            var tab = rig.Main.OnMain(() => Frame(tabs, area));
            Assert.True(tab.Width > 0, "the changes tab is showing");
            Assert.Equal(1, rig.Main.OnMain(() => tabs.Opacity));
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            var out_ = Frames(rig, window, 30, () => tabs.Opacity);
            var pillRect = rig.Main.OnMain(() => view.VoiceFloatRect(area));
            Assert.NotNull(pillRect);
            Assert.True(pillRect!.Value.Intersects(tab), "the pill's rectangle crosses the line");
            var fits = Enumerable.Range(0, 4).Where(slip => out_.Select((v, i) => Math.Abs(v - (1 - CssCurves.At(CssCurves.Ease, (i - slip) * FrameTime, 0.16)))).All(e => e < 0.01)).ToList();
            Assert.True(fits.Count > 0, "out over 0.16 s ease: " + string.Join(", ", out_.Take(14).Select(v => v.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))));
            Assert.Equal(0, out_[^1], 1e-6);
            rig.Main.OnMain(() => Click(Slot(view).HitTarget)); // stop; the pill folds when the final pass is done
            Run(rig, window, 3);
            var back = Frames(rig, window, 40, () => tabs.Opacity);
            Assert.Equal(1, back[^1], 1e-6);
            rig.Close(window);
        });
    }

    [Fact] // V-T86: coming back is 0.25 s ease after 0.05 s, measured from the frame the pill stops crossing
    [Trait("Decision", "ED-292")]
    public void The_line_comes_back_on_0_25_ease_after_0_05_when_the_pill_is_gone()
    {
        headless.OnUiThread(() =>
        {
            using var rig = ComposerVoicePillGeometryTests.Quiet(VoiceOrbSettings.Default with { ListeningStyle = VoiceListeningStyle.Float });
            var (area, view, tabs, window) = Open(rig, 380);
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            Run(rig, window, 1);
            Assert.Equal(0, rig.Main.OnMain(() => tabs.Opacity), 1e-6);
            rig.Main.OnMain(() => Click(Pill(view).Cross)); // the pill's own cross cancels
            var seen = Frames(rig, window, 40, () => tabs.Opacity);
            var fits = Enumerable.Range(0, 4).Where(slip => seen.Select((v, i) => Math.Abs(v - CssCurves.At(CssCurves.Ease, (i - slip) * FrameTime - 0.05, 0.25))).All(e => e < 0.01)).ToList();
            Assert.True(fits.Count > 0, "back over 0.25 s ease after 0.05 s: " + string.Join(", ", seen.Take(24).Select(v => v.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))));
            Assert.Equal(1, seen[^1], 1e-6);
            rig.Close(window);
        });
    }

    [Fact] // V-T86: a line the pill does not cross stays; the inline pill crosses nothing
    [Trait("Decision", "ED-292")]
    public void A_line_that_is_not_crossed_is_not_touched()
    {
        headless.OnUiThread(() =>
        {
            using var rig = ComposerVoicePillGeometryTests.Quiet(VoiceOrbSettings.Default with { ListeningStyle = VoiceListeningStyle.Pill });
            var (_, view, tabs, window) = Open(rig, 380);
            rig.Main.OnMain(() => Click(Slot(view).HitTarget));
            var seen = Frames(rig, window, 60, () => tabs.Opacity);
            Assert.All(seen, v => Assert.Equal(1, v, 1e-9));
            rig.Close(window);
        });
    }
}
