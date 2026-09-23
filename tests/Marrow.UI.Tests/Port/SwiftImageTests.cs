using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// F.SymbolImage (ED-179; OD-0010, PA-0012, porting/icons/README.md): SwiftUI Image(systemName:) drawn with the Lucide
// icon the mapping names, and Image(_ name:) for the three asset-catalogue images.
// PORT-ONLY surface these tests fix, namespace Marrow.UI.Port:
//   src/Marrow.UI/Port/SymbolCatalogue.cs:
//     sealed record SymbolVariant(bool Fill, string? Container, bool Slash, string? Badge)
//     sealed record SymbolMapping(string Sf, string Lucide, string Confidence, string? Note, IReadOnlyList<string> UsedIn,
//         SymbolVariant Variant, bool ComposedRendering, string? FillRendering)
//     sealed record SymbolCategory(string Title, string Site, IReadOnlyList<string> Symbols)
//     static class SymbolCatalogue { IReadOnlyCollection<string> SymbolNames; SymbolMapping? Mapping(string sf);
//         SvgIcon? Icon(string lucideName); SymbolCategory Popular; IReadOnlyList<SymbolCategory> Categories; }
//   src/Marrow.UI/Port/SwiftImage.cs:
//     enum ImageRenderingMode { Original, Template }   enum ContentMode { Fit, Fill }
//     sealed class SwiftImage : Control {
//         static SwiftImage SystemName(string name); static SwiftImage Named(string name);
//         string? SymbolName; string? AssetName; SymbolMapping? Mapping; SvgIcon? Icon; Bitmap? Bitmap; Bitmap? Bitmap2x;
//         bool IsEmpty; bool IsResizable; ContentMode? AspectContentMode; ImageRenderingMode EffectiveRenderingMode;
//         IBrush? Foreground (TextElement.Foreground, inherited as SwiftUI's foreground style is);
//         SwiftImage Resizable(); SwiftImage AspectRatio(ContentMode mode); SwiftImage RenderingMode(ImageRenderingMode mode);
//         SwiftImage Interpolation(BitmapInterpolationMode mode); SwiftImage ForegroundStyle(SwiftColor color); }
[Collection("Headless")]
public sealed class SwiftImageTests(HeadlessSession headless)
{
    private static readonly SwiftColor Red = SwiftColor.Rgb(0xFF0000);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Marrow.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Marrow.sln not found above the test output");
    }

    private static byte[] Asset(string path)
    {
        using var stream = AssetLoader.Open(new Uri($"avares://Marrow.UI/Assets/{path}"));
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static Window Show(Control content, double width, double height)
    {
        var host = new Border { Background = Brushes.White, Child = content };
        var window = new Window { Width = width, Height = height, Content = host, Background = Brushes.White };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static WriteableBitmap Capture(Window window)
    {
        using var frame = window.CaptureRenderedFrame()!;
        var bitmap = new WriteableBitmap(frame.PixelSize, frame.Dpi,
            frame.Format ?? PixelFormat.Rgba8888, AlphaFormat.Premul);
        using (var buffer = bitmap.Lock()) frame.CopyPixels(new PixelRect(frame.PixelSize), buffer.Address, buffer.RowBytes * buffer.Size.Height, buffer.RowBytes);
        return bitmap;
    }

    private static Color Pixel(WriteableBitmap bitmap, int x, int y)
    {
        using var buffer = bitmap.Lock();
        var bytes = new byte[4];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address + y * buffer.RowBytes + x * 4, bytes, 0, 4);
        return buffer.Format == PixelFormat.Rgba8888
            ? Color.FromArgb(bytes[3], bytes[0], bytes[1], bytes[2])
            : Color.FromArgb(bytes[3], bytes[2], bytes[1], bytes[0]);
    }

    private static bool IsRed(Color c) => c.R > 200 && c.G < 70 && c.B < 70;

    private static bool IsWhite(Color c) => c.R > 245 && c.G > 245 && c.B > 245;

    // ---- the mapping and the embedded icons ----

    [Fact]
    [Trait("Decision", "ED-179")]
    public void The_icons_and_the_mapping_are_embedded_unmodified_from_porting_icons() => headless.OnUiThread(() =>
    {
        var source = Path.Combine(RepoRoot(), "porting", "icons");
        var files = Directory.GetFiles(Path.Combine(source, "lucide")).Select(f => "lucide/" + Path.GetFileName(f))
            .Append("sf-symbols-to-lucide.json").ToList();
        Assert.Equal(715, files.Count); // 713 SVGs, the Lucide LICENSE and the mapping
        foreach (var file in files)
            Assert.True(File.ReadAllBytes(Path.Combine(source, file)).AsSpan().SequenceEqual(Asset("Icons/" + file)), file);
    });

    [Fact]
    [Trait("Decision", "ED-179")]
    public void Every_SF_name_in_the_mapping_resolves_to_an_embedded_Lucide_icon() => headless.OnUiThread(() =>
    {
        // porting/icons/README.md "Counts": 2753 distinct SF names, 146 drawn by app code, unmapped 0.
        Assert.Equal(2753, SymbolCatalogue.SymbolNames.Count);
        var app = 0;
        foreach (var sf in SymbolCatalogue.SymbolNames)
        {
            var mapping = SymbolCatalogue.Mapping(sf);
            Assert.NotNull(mapping);
            Assert.Equal(sf, mapping.Sf);
            Assert.NotNull(SymbolCatalogue.Icon(mapping.Lucide));
            var image = SwiftImage.SystemName(sf);
            Assert.False(image.IsEmpty, sf);
            Assert.Same(SymbolCatalogue.Icon(mapping.Lucide), image.Icon);
            if (mapping.UsedIn.Contains("app")) app++;
        }
        Assert.Equal(146, app);
    });

    [Fact]
    [Trait("Decision", "ED-179")]
    public void A_mapping_entry_carries_the_fields_the_README_defines() => headless.OnUiThread(() =>
    {
        var xmark = SymbolCatalogue.Mapping("xmark")!;
        Assert.Equal("x", xmark.Lucide);
        Assert.Equal("exact", xmark.Confidence);
        Assert.Equal(new SymbolVariant(false, null, false, null), xmark.Variant);
        Assert.False(xmark.ComposedRendering);
        Assert.Null(xmark.FillRendering);

        var check = SymbolCatalogue.Mapping("checkmark.circle.fill")!;
        Assert.Equal("circle-check", check.Lucide);
        Assert.Equal(new SymbolVariant(true, "circle", false, null), check.Variant);
        Assert.NotNull(check.FillRendering);
        Assert.Contains("app", check.UsedIn);

        var zero = SymbolCatalogue.Mapping("0.circle.fill")!;
        Assert.True(zero.ComposedRendering);
        Assert.Equal("approximate", zero.Confidence);
        Assert.Contains("draw \"0\" as text", zero.Note, StringComparison.Ordinal);
    });

    [Fact]
    [Trait("Decision", "ED-179")]
    public void The_picker_catalogue_keeps_the_Popular_shortlist_and_the_27_categories_in_source_order() => headless.OnUiThread(() =>
    {
        Assert.Equal("Popular", SymbolCatalogue.Popular.Title);
        Assert.Equal(56, SymbolCatalogue.Popular.Symbols.Count);
        Assert.Equal("hammer.fill", SymbolCatalogue.Popular.Symbols[0]);
        Assert.Equal(27, SymbolCatalogue.Categories.Count);
        Assert.Equal("Communication", SymbolCatalogue.Categories[0].Title);
        Assert.Equal("arrow.down.left.video.fill", SymbolCatalogue.Categories[0].Symbols[0]);
        Assert.Equal(2630, SymbolCatalogue.Categories.Sum(c => c.Symbols.Count));
        Assert.All(SymbolCatalogue.Categories.SelectMany(c => c.Symbols), sf => Assert.NotNull(SymbolCatalogue.Mapping(sf)));
    });

    // ---- unknown names render nothing ----

    [Fact]
    [Trait("Decision", "ED-179")]
    public void An_unknown_symbol_or_asset_name_is_an_empty_image_that_draws_nothing() => headless.OnUiThread(() =>
    {
        var symbol = SwiftImage.SystemName("no.such.symbol").ForegroundStyle(Red);
        Assert.True(symbol.IsEmpty);
        Assert.Null(symbol.Mapping);
        Assert.Null(symbol.Icon);
        var asset = SwiftImage.Named("no-such-asset");
        Assert.True(asset.IsEmpty);
        Assert.Null(asset.Icon);
        Assert.Null(asset.Bitmap);

        var panel = new StackPanel { Children = { symbol, asset } };
        var window = Show(panel, 64, 64);
        Assert.Equal(new Size(0, 0), symbol.DesiredSize);
        Assert.Equal(new Size(0, 0), asset.DesiredSize);

        var sized = SwiftImage.SystemName("no.such.symbol").Resizable().ForegroundStyle(Red);
        sized.Width = 48;
        sized.Height = 48;
        panel.Children.Add(sized);
        Dispatcher.UIThread.RunJobs();
        using var bitmap = Capture(window);
        for (var y = 0; y < 64; y += 2)
            for (var x = 0; x < 64; x += 2)
                Assert.True(IsWhite(Pixel(bitmap, x, y)), $"pixel {x},{y}");
        window.Close();
    });

    // ---- sizing: a resizable image fills its frame; the stroke is Lucide's 2 units of 24 ----

    [Fact]
    [Trait("Decision", "ED-179")]
    public void A_resizable_symbol_draws_Lucide_strokes_in_the_foreground_colour_scaled_from_24_units() => headless.OnUiThread(() =>
    {
        var image = SwiftImage.SystemName("xmark").Resizable().ForegroundStyle(Red);
        Assert.True(image.IsResizable);
        Assert.Equal(ImageRenderingMode.Template, image.EffectiveRenderingMode);
        image.Width = 48;
        image.Height = 48;
        image.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        image.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        var window = Show(image, 64, 64);
        using var bitmap = Capture(window);
        // x.svg in 48 px: the diagonals run 12,12 -> 36,36 and 36,12 -> 12,36, 4 px wide (2 units at 2 px a unit).
        Assert.True(IsRed(Pixel(bitmap, 24, 24)));
        Assert.True(IsRed(Pixel(bitmap, 16, 16)));
        Assert.True(IsRed(Pixel(bitmap, 32, 16)));
        Assert.True(IsRed(Pixel(bitmap, 24, 23))); // 0.7 px off the diagonal: inside the 2 px half width
        Assert.True(IsWhite(Pixel(bitmap, 24, 16))); // 5.7 and 4.9 px off the two diagonals
        Assert.True(IsWhite(Pixel(bitmap, 24, 12)));
        Assert.True(IsWhite(Pixel(bitmap, 4, 4)));
        // round caps: the cap reaches one unit (2 px) past the end point along the line's direction
        Assert.True(IsRed(Pixel(bitmap, 11, 11)));
        Assert.True(IsWhite(Pixel(bitmap, 8, 8)));
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-179")]
    public void A_resizable_image_stretches_to_its_frame_and_aspectRatio_fit_keeps_it_square_and_centred() => headless.OnUiThread(() =>
    {
        SwiftImage Framed(SwiftImage image)
        {
            image.Width = 96;
            image.Height = 48;
            image.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            image.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
            return image;
        }

        var stretched = Framed(SwiftImage.SystemName("xmark").Resizable().ForegroundStyle(Red));
        var window = Show(stretched, 112, 64);
        using (var bitmap = Capture(window))
        {
            // stretched 4 x 2: the diagonal 6,6 -> 18,18 runs 24,12 -> 72,36 and passes 30,15
            Assert.True(IsRed(Pixel(bitmap, 30, 15)));
            Assert.True(IsRed(Pixel(bitmap, 48, 24)));
        }
        window.Close();

        var fitted = Framed(SwiftImage.SystemName("xmark").Resizable().AspectRatio(ContentMode.Fit).ForegroundStyle(Red));
        Assert.Equal(ContentMode.Fit, fitted.AspectContentMode);
        window = Show(fitted, 112, 64);
        using (var bitmap = Capture(window))
        {
            // fitted 2 x 2 in the middle 48 px: the diagonal runs 36,12 -> 60,36
            Assert.True(IsRed(Pixel(bitmap, 48, 24)));
            Assert.True(IsRed(Pixel(bitmap, 40, 16)));
            Assert.True(IsWhite(Pixel(bitmap, 30, 15)));
            Assert.True(IsWhite(Pixel(bitmap, 20, 24)));
        }
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-179")]
    public void The_foreground_style_is_inherited_from_an_enclosing_view_as_SwiftUI_passes_it_down() => headless.OnUiThread(() =>
    {
        var image = SwiftImage.SystemName("minus").Resizable();
        image.Width = 48;
        image.Height = 48;
        image.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        image.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        var parent = new Border { Child = image };
        parent.SetValue(TextElement.ForegroundProperty, Brushes.Red);
        var window = Show(parent, 64, 64);
        using var bitmap = Capture(window);
        // minus.svg: "M5 12h14", in 48 px 10,24 -> 38,24
        Assert.True(IsRed(Pixel(bitmap, 24, 24)));
        Assert.True(IsWhite(Pixel(bitmap, 24, 30)));
        window.Close();
    });

    // ---- the three asset-catalogue images ----

    [Theory]
    [Trait("Decision", "ED-179")]
    [InlineData("Images/droppy-logo.png", "0f5d9663a0d23c458ab8e2783237596e5618d482f4ac23f00ead91eccef04388")]
    [InlineData("Images/droppy-logo@2x.png", "8007b4c58a34c96525245976b4d715930a24b4a8fac1614811c058720d98943d")]
    [InlineData("Images/hydra-mark.svg", "26c998a5e343e9bc0e8110be443fcb5b716dec0c705f4a52a2b5066d5d3d8f72")]
    [InlineData("Images/badge-edit.svg", "91ce10a554cb96c12c59a2f778b9541014483c9e70f9b84f5a3f4776efd20e3e")]
    public void The_asset_images_are_the_pinned_catalogue_files_unchanged(string path, string sha256) => headless.OnUiThread(() =>
    {
        // SHA-256 of DroppyCode/Resources/Assets.xcassets/<name>.imageset/<file> at the pinned commit.
        Assert.Equal(sha256, Convert.ToHexStringLower(SHA256.HashData(Asset(path))));
    });

    [Fact]
    [Trait("Decision", "ED-179")]
    public void The_three_asset_images_load_in_the_form_the_catalogue_holds() => headless.OnUiThread(() =>
    {
        // droppy-logo: two PNG representations, 1x and 2x, rendering intent original.
        var logo = SwiftImage.Named("droppy-logo");
        Assert.False(logo.IsEmpty);
        Assert.Null(logo.Icon);
        Assert.Equal(new PixelSize(256, 256), logo.Bitmap!.PixelSize);
        Assert.Equal(new PixelSize(512, 512), logo.Bitmap2x!.PixelSize);
        Assert.Equal(ImageRenderingMode.Original, logo.EffectiveRenderingMode);

        // hydra-mark: a vector template, one filled path in a 100-unit view box.
        var hydra = SwiftImage.Named("hydra-mark");
        Assert.Null(hydra.Bitmap);
        Assert.Equal(new Rect(0, 0, 100, 100), hydra.Icon!.ViewBox);
        var head = Assert.Single(hydra.Icon.Shapes);
        Assert.Equal(SvgPaintKind.Color, head.Fill.Kind);
        Assert.Equal(SvgPaintKind.None, head.Stroke.Kind);
        Assert.Equal(FillRule.NonZero, head.FillRule);
        Assert.Equal(ImageRenderingMode.Template, hydra.EffectiveRenderingMode);

        // badge-edit: a vector template, a rounded square and a plus stroked 1.5 wide in an 18-unit view box.
        var badge = SwiftImage.Named("badge-edit");
        Assert.Equal(new Rect(0, 0, 18, 18), badge.Icon!.ViewBox);
        Assert.Equal(["rect", "path"], badge.Icon.Shapes.Select(s => s.Element));
        Assert.All(badge.Icon.Shapes, s => Assert.Equal(1.5, s.StrokeWidth));
        Assert.All(badge.Icon.Shapes, s => Assert.Equal(SvgPaintKind.Color, s.Stroke.Kind));
        Assert.Equal(ImageRenderingMode.Template, badge.EffectiveRenderingMode);
    });

    [Fact]
    [Trait("Decision", "ED-179")]
    public void Named_images_that_are_not_resizable_take_their_catalogue_size_in_points() => headless.OnUiThread(() =>
    {
        var logo = SwiftImage.Named("droppy-logo");
        var hydra = SwiftImage.Named("hydra-mark");
        var badge = SwiftImage.Named("badge-edit");
        var window = Show(new StackPanel { Children = { logo, hydra, badge } }, 400, 500);
        Assert.Equal(new Size(256, 256), logo.DesiredSize);
        Assert.Equal(new Size(100, 100), hydra.DesiredSize);
        Assert.Equal(new Size(18, 18), badge.DesiredSize);
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-179")]
    public void Template_asset_images_draw_in_the_foreground_colour_with_their_own_fill_rule() => headless.OnUiThread(() =>
    {
        // TimelineRows.swift:2744-2748: Image("badge-edit").renderingMode(.template).resizable().frame(18, 18).
        var badge = SwiftImage.Named("badge-edit").RenderingMode(ImageRenderingMode.Template).Resizable().ForegroundStyle(Red);
        badge.Width = 36;
        badge.Height = 36;
        badge.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        badge.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        var window = Show(badge, 64, 64);
        using (var bitmap = Capture(window))
        {
            Assert.True(IsRed(Pixel(bitmap, 4, 18)));   // the square's left edge, x = 2 units
            Assert.True(IsRed(Pixel(bitmap, 18, 18)));  // the plus
            Assert.True(IsWhite(Pixel(bitmap, 18, 8))); // between the top edge and the plus
            Assert.True(IsWhite(Pixel(bitmap, 50, 50)));
        }
        window.Close();

        // HydraGlyph.swift:217-223 HydraMarkImage: resizable, template, aspectRatio fit.
        var hydra = SwiftImage.Named("hydra-mark").Resizable().RenderingMode(ImageRenderingMode.Template).AspectRatio(ContentMode.Fit).ForegroundStyle(Red);
        hydra.Width = 100;
        hydra.Height = 100;
        hydra.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        hydra.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        window = Show(hydra, 120, 120);
        using (var bitmap = Capture(window))
        {
            Assert.True(IsRed(Pixel(bitmap, 40, 60)));   // the head
            Assert.True(IsWhite(Pixel(bitmap, 63, 43))); // the eye: its subpath winds the other way, a hole under nonzero
            Assert.True(IsWhite(Pixel(bitmap, 5, 60)));  // outside the head
        }
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-179")]
    public void The_original_bitmap_draws_its_own_pixels_whatever_the_foreground() => headless.OnUiThread(() =>
    {
        // SettingsView.swift:1090-1093: Image("droppy-logo").resizable().interpolation(.high).frame(40, 40).
        var logo = SwiftImage.Named("droppy-logo").Resizable().Interpolation(BitmapInterpolationMode.HighQuality).ForegroundStyle(Red);
        logo.Width = 40;
        logo.Height = 40;
        logo.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        logo.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        var window = Show(logo, 64, 64);
        using var bitmap = Capture(window);
        var center = Pixel(bitmap, 20, 20);
        using var reference = new Bitmap(new MemoryStream(Asset("Images/droppy-logo.png")));
        using var scaled = reference.CreateScaledBitmap(new PixelSize(40, 40), BitmapInterpolationMode.HighQuality);
        var expected = ReadPixel(scaled, 20, 20);
        var alpha = expected.A / 255.0;
        // composited over the white page
        Assert.InRange(center.R, expected.R * alpha + 255 * (1 - alpha) - 24, expected.R * alpha + 255 * (1 - alpha) + 24);
        Assert.InRange(center.G, expected.G * alpha + 255 * (1 - alpha) - 24, expected.G * alpha + 255 * (1 - alpha) + 24);
        Assert.InRange(center.B, expected.B * alpha + 255 * (1 - alpha) - 24, expected.B * alpha + 255 * (1 - alpha) + 24);
        Assert.True(IsWhite(Pixel(bitmap, 50, 50)));
        window.Close();
    });

    private static Color ReadPixel(Bitmap source, int x, int y)
    {
        var bytes = new byte[4];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            source.CopyPixels(new PixelRect(x, y, 1, 1), handle.AddrOfPinnedObject(), 4, 4);
        }
        finally
        {
            handle.Free();
        }
        // Premultiplied Bgra8888 is the Skia default for decoded bitmaps; unpremultiply for comparison.
        var a = bytes[3];
        if (a == 0) return Color.FromArgb(0, 0, 0, 0);
        var format = source.Format ?? PixelFormat.Bgra8888;
        var (r, g, b) = format == PixelFormat.Rgba8888 ? (bytes[0], bytes[1], bytes[2]) : (bytes[2], bytes[1], bytes[0]);
        return source.AlphaFormat == AlphaFormat.Premul
            ? Color.FromArgb(a, (byte)Math.Min(255, r * 255 / a), (byte)Math.Min(255, g * 255 / a), (byte)Math.Min(255, b * 255 / a))
            : Color.FromArgb(a, r, g, b);
    }
}
