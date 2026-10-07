using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.App.Port.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Models.ComposerLights;
using Marrow.UI.Port;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;

namespace Marrow.App.Tests.Port.Settings;

// GAP-2, CZ-T45: compare the plate itself as well as the existing tests' text/control ink, with identical window and app backdrop.
[Collection("Headless")]
public sealed class GapComposerPreviewRenderTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-251")]
    [Trait("Spec", "CZ-T45")]
    public void GAP_The_preview_plate_at_472_has_the_real_composers_pixels_in_the_same_window() => headless.OnUiThread(() =>
    {
        using var h = RuntimeHarness();
        h.Main.OnMain(() => h.App.Settings.ShowsUsagePanel = false);
        var real = Pill(h, h.Runtime, null);
        var window = h.Main.OnMain(() => Show(real, h.App, 472));
        try
        {
            Pump(h, window);
            byte[] expected = [];
            Size size = default;
            h.Main.OnMain(() =>
            {
                var plate = Assert.IsType<Border>(real.Child);
                // Retain layout; hide content ink to isolate fill, rim, clipping and glass. Do not subtract the plate's background.
                plate.Child!.Opacity = 0;
                window.UpdateLayout();
                size = plate.Bounds.Size;
                expected = PreviewPixels.Region(window, new Rect(plate.TranslatePoint(default, window)!.Value, size));
            });
            var preview = new ComposerPreview(new PreviewSession(ProviderKind.Claude))
            {
                Settings = ComposerLightSettings.Default with { Style = ComposerLightStyle.Off },
            };
            preview.Pill.Child!.Opacity = 0;
            var stage = new Border
            {
                Width = 496, Height = 212, HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(-12, 0, 0, -12), Child = preview,
            };
            h.Main.OnMain(() => Host(window).Child = stage);
            Pump(h, window);
            h.Main.OnMain(() =>
            {
                Assert.Equal(size, preview.Pill.Bounds.Size);
                var actual = PreviewPixels.Region(window, new Rect(preview.Pill.TranslatePoint(default, window)!.Value, size));
                // The established real-control pixel comparison uses max 4/255 and mean .2/255.
                Assert.InRange(PreviewPixels.MaxDifference(expected, actual), 0, 4);
                Assert.InRange(PreviewPixels.MeanDifference(expected, actual), 0, .2);
            });
        }
        finally { Close(h, window); }
    });
}
