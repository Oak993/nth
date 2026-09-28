using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Chat;

/// <summary>
/// The provider's mark in the stage capsule and in a head row is drawn in Chrome.secondaryText
/// (HydraPanel.swift:144 and :323-324 <c>.foregroundStyle(Chrome.secondaryText)</c>), not in the ambient
/// default (black). Both tests read the brush the artwork really renders with and draw the artwork.
/// </summary>
[Collection("Headless")]
public sealed class HydraPanelProviderMarkTests(HeadlessSession headless)
{
    private static readonly Color Secondary = Chrome.SecondaryText.Resolve(ThemeVariant.Light);

    private static void AssertRendersInSecondaryText(ProviderIcon icon)
    {
        var image = Assert.IsType<SwiftImage>(icon.Child);
        // The brush the artwork paints with (SwiftImage.Foreground is the inherited TextElement.Foreground).
        var brush = Assert.IsAssignableFrom<ISolidColorBrush>(image.Foreground);
        Assert.Equal(Secondary, brush.Color);
        // And the pixels it really draws: template artwork takes the foreground colour, so every drawn
        // pixel carries the foreground's alpha (0x98) and never the opaque default.
        var size = (int)icon.Size;
        using var bitmap = new RenderTargetBitmap(new PixelSize(size, size));
        using (var context = bitmap.CreateDrawingContext()) image.Render(context);
        using var stream = new MemoryStream();
        bitmap.Save(stream, new PngBitmapEncoderOptions());
        using var pixels = SKBitmap.Decode(stream.ToArray());
        var maxAlpha = pixels.Pixels.Max(p => p.Alpha);
        Assert.InRange(maxAlpha, 1, Secondary.A);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-171")] // App/Views/Chat/HydraPanel.swift:141-145 ProviderIcon(...).foregroundStyle(Chrome.secondaryText)
    [Trait("Req", "UI-HYDRA-019")] // App/Views/Chat/HydraPanel.swift:144
    public void The_stage_capsule_provider_mark_renders_in_secondary_text()
    {
        headless.OnUiThread(() =>
        {
            using var context = new HydraPanelContext();
            context.Panel.IsVisible = true; context.Drain(); // the context hides the panel; the pixels need a laid-out mark
            var icon = Assert.Single(context.Find<ProviderIcon>(), i => i.Size == 12);
            AssertRendersInSecondaryText(icon);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-175")] // App/Views/Chat/HydraPanel.swift:322-324 ProviderIcon(...).foregroundStyle(Chrome.secondaryText)
    [Trait("Req", "UI-HYDRA-015")] // App/Views/Chat/HydraPanel.swift:323-324
    public void A_head_row_provider_mark_renders_in_secondary_text()
    {
        headless.OnUiThread(() =>
        {
            using var context = new HydraPanelContext();
            var row = new HydraHeadRow(context.First, false, () => { });
            var host = new Border { Child = row };
            var window = new Avalonia.Controls.Window { Content = host, Width = 420, Height = 120 };
            try
            {
                window.Show();
                var icon = Assert.Single(LogicalQuery.SelfAndDescendants<ProviderIcon>(row));
                AssertRendersInSecondaryText(icon);
            }
            finally { window.Close(); }
        });
    }
}
