using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Settings;

// Source: DroppyCode/App/Views/Settings/AboutUpdateViews.swift (B3 52bc792), :19-59,214-217,261-265; ED-215.
[Collection("Headless")]
public sealed class AboutUpdateViewsArtworkTests(HeadlessSession headless)
{
    private static byte[] Draw(SwiftImage image, SwiftColor foreground)
    {
        image.ForegroundStyle(foreground);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)image.Width, (int)image.Height));
        using (var context = bitmap.CreateDrawingContext()) image.Render(context);
        using var stream = new MemoryStream(); bitmap.Save(stream, new PngBitmapEncoderOptions());
        using var pixels = SKBitmap.Decode(stream.ToArray());
        Assert.Contains(pixels.Pixels, pixel => pixel.Alpha > 0);
        Assert.True(pixels.Pixels.Where(pixel => pixel.Alpha > 0).Distinct().Count() > 1);
        return pixels.Bytes;
    }

    [Theory]
    [Trait("Req", "PR-UPDATE-078")] // App/Views/Settings/AboutUpdateViews.swift:214-217
    [Trait("Req", "PR-UPDATE-079")] // App/Views/Settings/AboutUpdateViews.swift:261-265
    [Trait("Req", "UI-SETTINGS-057")] // App/Views/Settings/AboutUpdateViews.swift:190-202
    [Trait("Req", "UI-UPDATE-012")] // App/Views/Settings/AboutUpdateViews.swift:214-217
    [Trait("Req", "UI-UPDATE-013")] // App/Views/Settings/AboutUpdateViews.swift:214-217
    [Trait("Req", "UI-UPDATE-014")] // App/Views/Settings/AboutUpdateViews.swift:214-217
    [Trait("Req", "UI-UPDATE-015")] // App/Views/Settings/AboutUpdateViews.swift:261-265
    [InlineData(ReleaseNoteCategory.NewFeatures, "UpdateNewFeaturesIcon")]
    [InlineData(ReleaseNoteCategory.BugFixes, "UpdateBugFixesIcon")]
    [InlineData(ReleaseNoteCategory.Refinements, "UpdateRefinementsIcon")]
    public void The_actual_card_and_popover_images_draw_nonempty_original_artwork(ReleaseNoteCategory category, string assetName) => headless.OnUiThread(() =>
    {
        var section = new UpdateReleaseNotesDigest.Section { Category = category, Bullets = new ValueList<string>(["Change"]), RollupCount = 0 };
        var cards = new UpdateReleaseSectionCards(new UpdateReleaseNotesDigest { Sections = new ValueList<UpdateReleaseNotesDigest.Section>([section]) }, "1.7.0");
        var popover = new UpdateReleaseSectionPopover(section, "1.7.0");
        var window = new Avalonia.Controls.Window { Content = new StackPanel { Children = { cards, popover } }, Width = 330, Height = 400 };
        window.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            foreach (var (view, size) in new (Control, double)[] { (cards, 26), (popover, 24) })
            {
                var image = Assert.Single(view.GetVisualDescendants().OfType<SwiftImage>(), image => image.AssetName == assetName);
                Assert.False(image.IsEmpty); Assert.NotNull(image.Bitmap);
                // The pinned imagesets have no template-rendering-intent: ED-215 preserves original color.
                Assert.Equal(ImageRenderingMode.Original, image.EffectiveRenderingMode);
                Assert.Equal(size, image.Width); Assert.Equal(size, image.Height);
                Assert.True(image.IsResizable); Assert.Equal(ContentMode.Fit, image.AspectContentMode);
                Assert.Equal(Draw(image, new SwiftColor(1, 0, 0)), Draw(image, new SwiftColor(0, 0, 1)));
            }
        }
        finally { window.Close(); }
    });
}
