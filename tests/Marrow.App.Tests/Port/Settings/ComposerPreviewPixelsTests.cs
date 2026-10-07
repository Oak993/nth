using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.App.Port.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Models.ComposerLights;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;
using HostWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Port.Settings;

// customization-SPEC CZ-T45 (review of CZ-3): the parts the preview restates because the real ones need a thread (the ring, the chip's chevron end, the send glyph's ink) are held to the real
// controls' pixels, not only to their layout. Each is compared against its background (the plate's glass differs between the two windows), the ring by its track alone (the real arc is the
// context window's share), the chip by what follows the name (the real chip also has a model mark and an effort title).
[Collection("Headless")]
public sealed class ComposerPreviewPixelsTests(HeadlessSession headless)
{
    private static string Report(string what, byte[] real, byte[] preview) =>
        $"{what}: max {PreviewPixels.MaxDifference(PreviewPixels.AgainstItsBackground(real), PreviewPixels.AgainstItsBackground(preview))}, mean {PreviewPixels.MeanDifference(PreviewPixels.AgainstItsBackground(real), PreviewPixels.AgainstItsBackground(preview)):F2}";

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    [Trait("Decision", "ED-251")]
    [Trait("Spec", "CZ-T45")]
    public void The_restated_ring_chip_end_and_send_ink_draw_as_the_real_controls_do() => headless.OnUiThread(() =>
    {
        var real = RealComposerRig.Capture();
        var session = new PreviewSession(ProviderKind.Claude);
        var preview = new ComposerPreview(session) { Settings = ComposerLightSettings.Default with { Style = ComposerLightStyle.Off } };
        preview.Ring.ArcVisible = false;
        var stage = new Border { Width = 496, Height = 212, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(-12, 0, 0, -12), Child = preview };
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource(); using var bound = FrameClock.Source.Override(frames);
        var window = new HostWindow { Width = 1400, Height = 900, Content = stage };
        window.Show();
        try
        {
            window.UpdateLayout();
            Rect Frame(Visual v) => new(v.TranslatePoint(default, window)!.Value, v.Bounds.Size);
            var ringRect = Frame(preview.Ring);
            var ring = PreviewPixels.Region(window, ringRect);
            var chevron = RealComposerRig.Chevron(preview.Chip);
            var chip = Frame(preview.Chip);
            var chevronRect = Frame(chevron);
            var end = new Rect(chevronRect.X - 4, chip.Y, chip.Right - chevronRect.X + 4, chip.Height);
            var chevronEnd = PreviewPixels.Region(window, end);
            var disc = Frame(preview.Send);
            var rest = PreviewPixels.Region(window, disc);
            session.Pick(PreviewState.Typing);
            runtime.AdvanceMilliseconds(300); frames.Advance(TimeSpan.FromMilliseconds(300));
            window.UpdateLayout();
            var typing = PreviewPixels.Region(window, disc);

            Assert.Equal(real.RingSize, ((int)Math.Round(ringRect.Width), (int)Math.Round(ringRect.Height)));
            Assert.Equal(real.ChevronEndSize, ((int)Math.Round(end.Width), (int)Math.Round(end.Height)));
            Assert.Equal(real.DiscSize, ((int)Math.Round(disc.Width), (int)Math.Round(disc.Height)));
            Assert.Equal(real.ChevronGap, RealComposerRig.GapBeforeChevron(chevron), 2);
            var failures = new List<string>();
            void Check(string what, byte[] r, byte[] p, int max, double mean)
            {
                var a = PreviewPixels.AgainstItsBackground(r); var b = PreviewPixels.AgainstItsBackground(p);
                if (PreviewPixels.MaxDifference(a, b) > max || PreviewPixels.MeanDifference(a, b) > mean) failures.Add(Report(what, r, p));
            }
            Check("ring track", real.Ring, ring, 4, .2);
            Check("chip chevron end", real.ChevronEnd, chevronEnd, 4, .2);
            Check("send at rest", real.DiscRest, rest, 4, .2);
            Check("send with a draft", real.DiscTyping, typing, 4, .2);
            Assert.True(failures.Count == 0, string.Join("; ", failures));
        }
        finally { window.Close(); }
    });
}
