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
// ED-182 additions (the F.SymbolImage stop, questions Q1 to Q6; every value it parks is oracle O-63):
//   src/Marrow.UI/Port/SvgIcon.cs: SvgShape gains a trailing bool IsClosed (circle, ellipse, rect, polygon, and a path
//     whose every subpath is closed by Z or ends on its start point).
//   src/Marrow.UI/Port/SymbolCatalogue.cs:
//     sealed record SymbolComposition(string? Text, string? Container, bool Slash, bool Badge, IReadOnlyList<string> Undrawn)
//     static SymbolComposition SymbolCatalogue.Composition(SymbolMapping mapping); SvgIcon? SymbolCatalogue.ContainerIcon(string container)
//   src/Marrow.UI/Port/SwiftImage.cs:
//     enum SymbolRenderingMode { Monochrome, Hierarchical, Palette, Multicolor }   enum SymbolEffectKind { Bounce, Replace }
//     enum SwiftImage.Scale { Small, Medium, Large } (SwiftUI Image.Scale)
//     AttachedProperty<SwiftFont?> SwiftImage.FontProperty (inherited, as SwiftUI's .font environment); GetFont; SetFont;
//     SwiftImage Font(SwiftFont font); SwiftImage FontWeight(SwiftFont.Weight weight); SwiftImage ImageScale(Scale scale);
//     SwiftImage SymbolRenderingMode(SymbolRenderingMode mode); SwiftImage ForegroundStyle(SwiftColor primary, SwiftColor secondary);
//     SwiftImage SymbolEffect(SymbolEffectKind effect, object? value); SwiftImage ContentTransitionSymbolEffect(SymbolEffectKind effect);
//     double SymbolSide; SwiftFont.Weight? Weight; Scale SymbolImageScale; SymbolRenderingMode SymbolRendering;
//     IBrush? SecondaryForeground; SymbolEffectKind? DeferredSymbolEffect; SymbolEffectKind? DeferredContentTransition;
//     SymbolComposition? Composition
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
    [InlineData("Images/hydra-mark.svg", "26c998a5e343e9bc0e8110be443fcb5b716dec0c705f4a52a2b5066d5d3d8f72")]
    [InlineData("Images/badge-edit.svg", "91ce10a554cb96c12c59a2f778b9541014483c9e70f9b84f5a3f4776efd20e3e")]
    public void The_asset_images_are_the_pinned_catalogue_files_unchanged(string path, string sha256) => headless.OnUiThread(() =>
    {
        // SHA-256 of DroppyCode/Resources/Assets.xcassets/<name>.imageset/<file> at the pinned commit (hydra-mark.svg
        // and badge-edit.svg are untouched by B3). The droppy-logo PNGs are no longer here: OD-0015 ships the Marrow icon
        // under that lookup key (see the next test and MarrowLogoArtworkTests).
        Assert.Equal(sha256, Convert.ToHexStringLower(SHA256.HashData(Asset(path))));
    });

    [Theory]
    [Trait("Decision", "ED-179")] // OD-0015: the owner-ruled Marrow icon replaces the Droppy logo
    [InlineData("Images/marrow-icon.png", "9596225755adc853eedebf423bc8b815f474bcaf278ecff9a1f7877e3f4011e9")]
    [InlineData("Images/marrow-icon@2x.png", "3aae230159f6c67ecdf4a1d17396dd013810f2dbca0ccf6ad7f1833a3eca3ccf")]
    public void The_logo_images_are_the_Marrow_icon_renders_not_the_upstream_catalogue_files(string path, string sha256) => headless.OnUiThread(() =>
    {
        // OD-0112: locked Aurora generator outputs, IconKit.box(size) filling 256/512 px; hashes recorded in ED-402. The upstream
        // droppy-logo PNG hashes (307aca65..., 467897a0...) are asserted absent in MarrowLogoArtworkTests.
        Assert.Equal(sha256, Convert.ToHexStringLower(SHA256.HashData(Asset(path))));
    });

    [Fact]
    [Trait("Decision", "ED-179")]
    public void The_three_asset_images_load_in_the_form_the_catalogue_holds() => headless.OnUiThread(() =>
    {
        // droppy-logo (the Swift key; the artwork is the Marrow icon, OD-0015): two PNG representations, 1x and 2x, rendering intent original.
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
        // SettingsView.swift:1090-1093: Image("droppy-logo").resizable().interpolation(.high).frame(40, 40); the artwork is the Marrow icon (OD-0015).
        var logo = SwiftImage.Named("droppy-logo").Resizable().Interpolation(BitmapInterpolationMode.HighQuality).ForegroundStyle(Red);
        logo.Width = 40;
        logo.Height = 40;
        logo.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        logo.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        var window = Show(logo, 64, 64);
        using var bitmap = Capture(window);
        var center = Pixel(bitmap, 20, 20);
        using var reference = new Bitmap(new MemoryStream(Asset("Images/marrow-icon.png")));
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

    // ---- ED-182: size, weight, fill, composition and effects of a symbol (every value parked as oracle O-63) ----

    private static SwiftImage TopLeft(SwiftImage image)
    {
        image.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        image.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        return image;
    }

    private static SwiftImage Framed(SwiftImage image, double side)
    {
        image.Width = side;
        image.Height = side;
        return TopLeft(image);
    }

    private static byte[] Bytes(WriteableBitmap bitmap)
    {
        using var buffer = bitmap.Lock();
        var bytes = new byte[buffer.RowBytes * buffer.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
        return bytes;
    }

    private static byte[] Drawn(SwiftImage image, int windowSide)
    {
        var window = Show(image, windowSide, windowSide);
        using var bitmap = Capture(window);
        var bytes = Bytes(bitmap);
        window.Close();
        return bytes;
    }

    [Fact]
    [Trait("Decision", "ED-182")]
    [Trait("Oracle", "O-63")]
    public void A_symbol_with_a_font_of_point_size_S_measures_a_square_of_1_2_S_and_the_body_size_without_a_font() => headless.OnUiThread(() =>
    {
        var sized = SwiftImage.SystemName("xmark").Font(SwiftFont.System(20));
        var noFont = SwiftImage.SystemName("xmark");
        var caption = SwiftImage.SystemName("xmark").Font(SwiftFont.Caption);
        var window = Show(new StackPanel { Children = { TopLeft(sized), TopLeft(noFont), TopLeft(caption) } }, 200, 200);
        Assert.Equal(24, sized.SymbolSide, 9);
        Assert.Equal(new Size(24, 24), sized.DesiredSize);
        // Without a font the SwiftUI default body size applies (SwiftFont.Body, ED-059: 13 points).
        Assert.Equal(1.2 * SwiftFont.Body.PointSize, noFont.SymbolSide, 9);
        Assert.Equal(15.6, noFont.SymbolSide, 9);
        Assert.InRange(noFont.DesiredSize.Width, 15.6, 16); // layout rounding may round the square up to whole pixels
        Assert.Equal(1.2 * SwiftFont.Caption.PointSize, caption.SymbolSide, 9);
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-182")]
    [Trait("Oracle", "O-63")]
    public void The_font_is_inherited_from_an_enclosing_view_and_a_nearer_font_wins() => headless.OnUiThread(() =>
    {
        var image = TopLeft(SwiftImage.SystemName("xmark"));
        var panel = new StackPanel { Children = { image } };
        SwiftImage.SetFont(panel, SwiftFont.System(10));
        var window = Show(panel, 100, 100);
        Assert.Equal(new Size(12, 12), image.DesiredSize);
        image.Font(SwiftFont.System(30));
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Assert.Equal(new Size(36, 36), image.DesiredSize);
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-182")]
    [Trait("Oracle", "O-63")]
    public void A_symbol_that_is_not_resizable_never_throws_on_measure_or_render() => headless.OnUiThread(() =>
    {
        var panel = new WrapPanel();
        var names = SymbolCatalogue.SymbolNames.Where(n => SymbolCatalogue.Mapping(n)!.UsedIn.Contains("app")).ToList();
        Assert.Equal(146, names.Count);
        foreach (var name in names) panel.Children.Add(SwiftImage.SystemName(name).ForegroundStyle(Red));
        var window = Show(panel, 400, 400);
        Assert.All(panel.Children.Cast<SwiftImage>(), image =>
        {
            Assert.Equal(1.2 * SwiftFont.Body.PointSize, image.SymbolSide, 9);
            Assert.InRange(image.DesiredSize.Width, 15.6, 16);
        });
        using var bitmap = Capture(window);
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-182")]
    [Trait("Oracle", "O-63")]
    public void The_glyph_fills_its_square_at_side_over_24_a_unit_and_the_square_is_centred_in_its_frame() => headless.OnUiThread(() =>
    {
        var image = Framed(SwiftImage.SystemName("xmark").Font(SwiftFont.System(20)).ForegroundStyle(Red), 64);
        var window = Show(image, 80, 80);
        using var bitmap = Capture(window);
        // the 24 px square sits at 20,20 in the 64 px frame; x.svg's diagonal 6,6 -> 18,18 runs 26,26 -> 38,38 at 1 px a unit
        Assert.True(IsRed(Pixel(bitmap, 32, 32)));
        Assert.True(IsRed(Pixel(bitmap, 27, 27)));
        Assert.True(IsRed(Pixel(bitmap, 36, 27)));
        Assert.True(IsWhite(Pixel(bitmap, 32, 26)));
        Assert.True(IsWhite(Pixel(bitmap, 16, 16))); // where a glyph stretched over the frame would draw
        Assert.True(IsWhite(Pixel(bitmap, 22, 22)));
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-182")]
    [Trait("Oracle", "O-63")]
    public void A_symbol_in_a_line_of_text_is_centred_on_the_texts_cap_height_centre() => headless.OnUiThread(() =>
    {
        var font = SwiftFont.System(40);
        var image = SwiftImage.SystemName("circle.fill").Font(font).ForegroundStyle(Red);
        var text = new TextBlock
        {
            Foreground = Brushes.Black,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Inlines = [new InlineUIContainer(image), new Run("H")],
        };
        font.Apply(text);
        var window = Show(text, 200, 140);
        using var bitmap = Capture(window);
        int redTop = -1, redBottom = -1, redRight = -1;
        for (var y = 0; y < 140; y++)
            for (var x = 0; x < 200; x++)
                if (IsRed(Pixel(bitmap, x, y)))
                {
                    if (redTop < 0) redTop = y;
                    redBottom = y;
                    redRight = Math.Max(redRight, x);
                }
        Assert.True(redTop >= 0, "the symbol drew");
        int inkTop = -1, inkBottom = -1;
        for (var y = 0; y < 140; y++)
            for (var x = redRight + 2; x < 200; x++)
            {
                var c = Pixel(bitmap, x, y);
                if (c.R < 80 && c.G < 80 && c.B < 80)
                {
                    if (inkTop < 0) inkTop = y;
                    inkBottom = y;
                }
            }
        Assert.True(inkTop >= 0, "the H drew");
        Assert.Equal(48, image.SymbolSide, 9);
        // the cap height is the ink height of Inter's "H" at 40 points (Inter's cap height is about 0.73 em)
        var cap = SwiftImage.CapHeight(font);
        Assert.InRange(cap, 27, 31);
        Assert.Equal(24 + cap / 2, TextBlock.GetBaselineOffset(image), 9);
        Assert.InRange(redBottom - redTop + 1, 43, 46); // the filled circle: radius 10 plus half the 2-unit stroke, 2 px a unit
        var symbolCentre = (redTop + redBottom + 1) / 2.0;
        var capCentre = (inkTop + inkBottom + 1) / 2.0;
        Assert.InRange(symbolCentre - capCentre, -1.5, 1.5);
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-182")]
    [Trait("Oracle", "O-63")]
    public void Weight_does_not_change_the_stroke_Lucides_2_of_24_units_at_every_weight() => headless.OnUiThread(() =>
    {
        foreach (var weight in Enum.GetValues<SwiftFont.Weight>())
        {
            var image = TopLeft(SwiftImage.SystemName("minus").Font(SwiftFont.System(40, weight)).FontWeight(weight).ForegroundStyle(Red));
            Assert.Equal(weight, image.Weight);
            var window = Show(image, 64, 64);
            using var bitmap = Capture(window);
            // the 48 px square at 2 px a unit: "M5 12h14" runs 10,24 -> 38,24, 4 px thick over rows 22 to 25
            for (var y = 22; y <= 25; y++) Assert.True(IsRed(Pixel(bitmap, 24, y)), $"{weight} row {y}");
            Assert.True(IsWhite(Pixel(bitmap, 24, 20)), weight.ToString());
            Assert.True(IsWhite(Pixel(bitmap, 24, 27)), weight.ToString());
            window.Close();
        }
    });

    [Fact]
    [Trait("Decision", "ED-182")]
    [Trait("Oracle", "O-63")]
    public void imageScale_multiplies_the_square_by_0_8_1_0_and_1_25() => headless.OnUiThread(() =>
    {
        var font = SwiftFont.System(20);
        var plain = TopLeft(SwiftImage.SystemName("xmark").Font(font));
        var small = TopLeft(SwiftImage.SystemName("xmark").Font(font).ImageScale(SwiftImage.Scale.Small));
        var medium = TopLeft(SwiftImage.SystemName("xmark").Font(font).ImageScale(SwiftImage.Scale.Medium));
        var large = TopLeft(SwiftImage.SystemName("xmark").Font(font).ImageScale(SwiftImage.Scale.Large));
        Assert.Equal(SwiftImage.Scale.Medium, plain.SymbolImageScale);
        Assert.Equal(SwiftImage.Scale.Small, small.SymbolImageScale);
        var window = Show(new StackPanel { Children = { plain, small, medium, large } }, 100, 200);
        Assert.Equal(24, plain.SymbolSide, 9);
        Assert.Equal(19.2, small.SymbolSide, 9);
        Assert.Equal(24, medium.SymbolSide, 9);
        Assert.Equal(30, large.SymbolSide, 9);
        Assert.Equal(new Size(24, 24), medium.DesiredSize);
        Assert.Equal(new Size(30, 30), large.DesiredSize);
        window.Close();

        // MarkdownView.swift:509-511: checkmark.circle.fill with .imageScale(.small) and no font of its own.
        var task = SwiftImage.SystemName("checkmark.circle.fill").ImageScale(SwiftImage.Scale.Small);
        Assert.Equal(0.8 * 1.2 * SwiftFont.Body.PointSize, task.SymbolSide, 9);
    });

    [Fact]
    [Trait("Decision", "ED-182")]
    public void An_SVG_shape_knows_whether_it_is_closed() => headless.OnUiThread(() =>
    {
        Assert.Equal([true, false], SymbolCatalogue.Icon("circle-check")!.Shapes.Select(s => s.IsClosed));
        Assert.True(Assert.Single(SymbolCatalogue.Icon("square")!.Shapes).IsClosed);
        Assert.True(Assert.Single(SymbolCatalogue.Icon("diamond")!.Shapes).IsClosed);  // ends in Z
        Assert.True(Assert.Single(SymbolCatalogue.Icon("squircle")!.Shapes).IsClosed); // ends on its start point, no Z
        Assert.All(SymbolCatalogue.Icon("x")!.Shapes, s => Assert.False(s.IsClosed));
        Assert.Equal([false, true], SymbolCatalogue.Icon("user")!.Shapes.Select(s => s.IsClosed));
    });

    [Fact]
    [Trait("Decision", "ED-182")]
    [Trait("Oracle", "O-63")]
    public void A_fill_name_fills_its_container_and_knocks_every_other_element_out() => headless.OnUiThread(() =>
    {
        // checkmark.circle.fill -> circle-check: <circle cx=12 cy=12 r=10/> and the check "m16 9-5.5 5.5L8 12".
        Assert.Equal("circle-check", SymbolCatalogue.Mapping("checkmark.circle.fill")!.Lucide);
        var window = Show(Framed(SwiftImage.SystemName("checkmark.circle.fill").Resizable().ForegroundStyle(Red), 48), 64, 64);
        using (var bitmap = Capture(window))
        {
            Assert.True(IsRed(Pixel(bitmap, 24, 12)));   // inside the circle, off the check: filled
            Assert.True(IsRed(Pixel(bitmap, 24, 4)));    // the circle's stroke
            Assert.True(IsWhite(Pixel(bitmap, 21, 29))); // the check's corner at 10.5,14.5: knocked out
            Assert.True(IsWhite(Pixel(bitmap, 2, 2)));
        }
        window.Close();

        // the outline name draws the outline
        Assert.Equal("circle-check", SymbolCatalogue.Mapping("checkmark.circle")!.Lucide);
        window = Show(Framed(SwiftImage.SystemName("checkmark.circle").Resizable().ForegroundStyle(Red), 48), 64, 64);
        using (var bitmap = Capture(window))
        {
            Assert.True(IsWhite(Pixel(bitmap, 24, 12)));
            Assert.True(IsRed(Pixel(bitmap, 24, 4)));
            Assert.True(IsRed(Pixel(bitmap, 21, 29)));
        }
        window.Close();

        // stop.fill -> square: one closed shape, filled whole
        window = Show(Framed(SwiftImage.SystemName("stop.fill").Resizable().ForegroundStyle(Red), 48), 64, 64);
        using (var bitmap = Capture(window))
        {
            Assert.True(IsRed(Pixel(bitmap, 24, 24)));
            Assert.True(IsRed(Pixel(bitmap, 24, 5)));
            Assert.True(IsWhite(Pixel(bitmap, 2, 24)));
        }
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-182")]
    [Trait("Oracle", "O-63")]
    public void A_fill_name_whose_icon_has_no_enclosing_closed_shape_draws_its_outline() => headless.OnUiThread(() =>
    {
        // person.fill -> user: the head circle does not enclose the open body path.
        Assert.Equal("user", SymbolCatalogue.Mapping("person.fill")!.Lucide);
        var window = Show(Framed(SwiftImage.SystemName("person.fill").Resizable().ForegroundStyle(Red), 48), 64, 64);
        using (var bitmap = Capture(window))
        {
            Assert.True(IsWhite(Pixel(bitmap, 24, 14))); // inside the head: not filled
            Assert.True(IsRed(Pixel(bitmap, 24, 6)));    // the head's stroke
            Assert.True(IsRed(Pixel(bitmap, 24, 30)));   // the body's shoulder line, y 15
            Assert.True(IsWhite(Pixel(bitmap, 24, 36))); // under the shoulders: the open body is not filled
        }
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-182")]
    [Trait("Oracle", "O-63")]
    public void Every_symbol_in_the_mapping_measures_and_draws_with_its_composition() => headless.OnUiThread(() =>
    {
        var panel = new WrapPanel();
        foreach (var sf in SymbolCatalogue.SymbolNames)
        {
            var composition = SymbolCatalogue.Composition(SymbolCatalogue.Mapping(sf)!);
            if (composition.Container is { } container) Assert.True(SymbolCatalogue.ContainerIcon(container) is not null, sf);
            panel.Children.Add(SwiftImage.SystemName(sf).Font(SwiftFont.System(10)).ForegroundStyle(Red));
        }
        var window = Show(panel, 800, 800);
        using var bitmap = Capture(window);
        Assert.All(panel.Children.Cast<SwiftImage>(), image => Assert.Equal(new Size(12, 12), image.DesiredSize));
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-182")]
    [Trait("Oracle", "O-63")]
    public void The_composition_is_read_from_the_mappings_composed_note() => headless.OnUiThread(() =>
    {
        SymbolComposition Of(string sf) => SymbolCatalogue.Composition(SymbolCatalogue.Mapping(sf)!);

        var plain = Of("xmark");
        Assert.Equal((null, null, false, false), (plain.Text, plain.Container, plain.Slash, plain.Badge));
        Assert.Empty(plain.Undrawn);

        var pencil = Of("pencil.circle.fill"); // "composed: enclose in a circle"
        Assert.Equal((null, "circle", false, false), (pencil.Text, pencil.Container, pencil.Slash, pencil.Badge));
        Assert.Empty(pencil.Undrawn);

        var zero = Of("0.circle.fill"); // draw "0" as text inside the Lucide circle
        Assert.Equal(("0", "circle"), (zero.Text, zero.Container));
        var sos = Of("sos.circle.fill"); // text plus "enclose the glyph in a circle": the text is the enclosed glyph
        Assert.Equal(("SOS", "circle"), (sos.Text, sos.Container));
        Assert.Empty(sos.Undrawn);

        Assert.True(Of("hand.raised.slash").Slash);
        Assert.True(Of("app.badge.fill").Badge);
        var macro = Of("camera.macro.slash.circle.fill");
        Assert.Equal(("circle", true), (macro.Container, macro.Slash));

        Assert.Equal(["composed: write 4K on the screen"], Of("4k.tv.fill").Undrawn);

        Assert.Same(SymbolCatalogue.Icon("circle"), SymbolCatalogue.ContainerIcon("circle"));
        Assert.Same(SymbolCatalogue.Icon("square"), SymbolCatalogue.ContainerIcon("square"));
        Assert.Same(SymbolCatalogue.Icon("squircle"), SymbolCatalogue.ContainerIcon("app"));
        Assert.Same(SymbolCatalogue.Icon("rectangle-horizontal"), SymbolCatalogue.ContainerIcon("rectangle"));
        Assert.Null(SymbolCatalogue.ContainerIcon("no-such-container"));
        Assert.Equal(pencil, SwiftImage.SystemName("pencil.circle.fill").Composition);
    });

    [Fact]
    [Trait("Decision", "ED-182")]
    [Trait("Oracle", "O-63")]
    public void An_enclosed_glyph_is_scaled_to_half_the_box_and_centred_in_its_container() => headless.OnUiThread(() =>
    {
        // arrow.uturn.backward.circle -> undo-2 enclosed in a circle: "M4 9h10.5" becomes y 10.5 from x 8 to 13.25.
        var window = Show(Framed(SwiftImage.SystemName("arrow.uturn.backward.circle").Resizable().ForegroundStyle(Red), 48), 64, 64);
        using (var bitmap = Capture(window))
        {
            Assert.True(IsRed(Pixel(bitmap, 24, 4)));    // the container circle's stroke
            Assert.True(IsRed(Pixel(bitmap, 20, 21)));   // the half-size bar at 10.25,10.75
            Assert.True(IsWhite(Pixel(bitmap, 10, 18))); // where the full-size bar would run (5.25,9.25)
            Assert.True(IsWhite(Pixel(bitmap, 24, 12)));
        }
        window.Close();

        // pencil.circle.fill: the container filled, the half-size pencil knocked out of it.
        window = Show(Framed(SwiftImage.SystemName("pencil.circle.fill").Resizable().ForegroundStyle(Red), 48), 64, 64);
        using (var bitmap = Capture(window))
        {
            Assert.True(IsRed(Pixel(bitmap, 24, 6)));    // circle interior, away from the pencil
            Assert.True(IsWhite(Pixel(bitmap, 22, 22))); // on the half-size pencil's lower edge (x + y = 22)
            Assert.True(IsRed(Pixel(bitmap, 32, 8)));    // on the full-size pencil's edge, beyond the half-size one: still filled
        }
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-182")]
    [Trait("Oracle", "O-63")]
    public void A_composed_slash_is_the_2_2_to_22_22_stroke() => headless.OnUiThread(() =>
    {
        Assert.Equal("hand", SymbolCatalogue.Mapping("hand.raised.slash")!.Lucide);
        Assert.Equal("hand", SymbolCatalogue.Mapping("hand.raised")!.Lucide);
        var window = Show(Framed(SwiftImage.SystemName("hand.raised.slash").Resizable().ForegroundStyle(Red), 48), 64, 64);
        using (var bitmap = Capture(window))
        {
            Assert.True(IsRed(Pixel(bitmap, 5, 5)));
            Assert.True(IsRed(Pixel(bitmap, 42, 42)));
            Assert.True(IsWhite(Pixel(bitmap, 1, 1)));
        }
        window.Close();
        window = Show(Framed(SwiftImage.SystemName("hand.raised").Resizable().ForegroundStyle(Red), 48), 64, 64);
        using (var bitmap = Capture(window))
        {
            Assert.True(IsWhite(Pixel(bitmap, 5, 5)));
            Assert.True(IsWhite(Pixel(bitmap, 42, 42)));
        }
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-182")]
    [Trait("Oracle", "O-63")]
    public void A_corner_badge_is_a_radius_4_dot_at_18_6_with_the_drawing_knocked_out_1_5_around_it() => headless.OnUiThread(() =>
    {
        // app.badge.fill -> squircle (filled) with a corner badge.
        var window = Show(Framed(SwiftImage.SystemName("app.badge.fill").Resizable().ForegroundStyle(Red), 48), 64, 64);
        using var bitmap = Capture(window);
        Assert.True(IsRed(Pixel(bitmap, 36, 12)));   // the badge centre 18,6
        Assert.True(IsRed(Pixel(bitmap, 36, 5)));    // inside the dot (18.25,2.75: 3.3 from its centre)
        Assert.True(IsWhite(Pixel(bitmap, 29, 18))); // 14.75,9.25: 4.6 from the badge centre, in the 1.5 gap
        Assert.True(IsRed(Pixel(bitmap, 24, 30)));   // the filled squircle away from the badge
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-182")]
    [Trait("Oracle", "O-63")]
    public void Text_in_a_container_is_centred_and_knocked_out_of_a_filled_container() => headless.OnUiThread(() =>
    {
        // 0.circle.fill: the Lucide circle filled, "0" in Inter Semibold at 11 of 24 units knocked out, centred.
        var window = Show(Framed(SwiftImage.SystemName("0.circle.fill").Resizable().ForegroundStyle(Red), 96), 112, 112);
        using var bitmap = Capture(window);
        Assert.True(IsRed(Pixel(bitmap, 48, 48))); // the counter of the 0 keeps the fill

        (int Start, int End) WhiteRun(Func<int, Color> at, int from, int step)
        {
            var i = from;
            while (i > 0 && i < 111 && !IsWhite(at(i))) i += step;
            var start = i;
            while (i > 0 && i < 111 && IsWhite(at(i))) i += step;
            return (start, i - step);
        }

        var left = WhiteRun(x => Pixel(bitmap, x, 48), 48, -1);
        var right = WhiteRun(x => Pixel(bitmap, x, 48), 48, 1);
        Assert.True(left.Start - left.End >= 2, "the 0's left stroke is knocked out");
        Assert.True(right.End - right.Start >= 2, "the 0's right stroke is knocked out");
        Assert.True(IsRed(Pixel(bitmap, left.End - 3, 48)), "the fill resumes left of the 0");
        Assert.True(IsRed(Pixel(bitmap, right.End + 3, 48)), "the fill resumes right of the 0");
        Assert.InRange((left.Start + left.End + right.Start + right.End) / 4.0, 46.5, 49.5);
        var top = WhiteRun(y => Pixel(bitmap, 48, y), 48, -1);
        var bottom = WhiteRun(y => Pixel(bitmap, 48, y), 48, 1);
        Assert.InRange((top.Start + top.End + bottom.Start + bottom.End) / 4.0, 46.5, 49.5);
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-182")]
    [Trait("Oracle", "O-63")]
    public void symbolRenderingMode_draws_monochrome_in_the_foreground_colour() => headless.OnUiThread(() =>
    {
        SwiftImage Base() => Framed(SwiftImage.SystemName("xmark.circle.fill").Resizable(), 48);
        var monochrome = Drawn(Base().ForegroundStyle(Red), 64);
        // ComposerView.swift:1008-1010: .symbolRenderingMode(.palette).foregroundStyle(.white, .black.opacity(0.6))
        var palette = Base().SymbolRenderingMode(SymbolRenderingMode.Palette).ForegroundStyle(Red, SwiftColor.Rgb(0x0000FF));
        Assert.Equal(SymbolRenderingMode.Palette, palette.SymbolRendering);
        Assert.NotNull(palette.SecondaryForeground);
        // ED-282 palette: two styles in palette mode on a .fill symbol with a container draw its two layers
        // (the next test); every mode with one style, and every other mode, still draws monochrome.
        Assert.NotEqual(monochrome, Drawn(palette, 64));
        foreach (var mode in Enum.GetValues<SymbolRenderingMode>())
            Assert.Equal(monochrome, Drawn(Base().SymbolRenderingMode(mode).ForegroundStyle(Red), 64));
        foreach (var mode in Enum.GetValues<SymbolRenderingMode>().Where(mode => mode != SymbolRenderingMode.Palette))
            Assert.Equal(monochrome, Drawn(Base().SymbolRenderingMode(mode).ForegroundStyle(Red, SwiftColor.Rgb(0x0000FF)), 64));
        Assert.Equal(SymbolRenderingMode.Monochrome, Base().SymbolRendering);
    });

    [Fact]
    [Trait("Decision", "ED-182")]
    [Trait("Oracle", "O-63")]
    public void A_palette_fill_symbol_draws_the_container_in_the_second_style_and_the_elements_over_it_in_the_first() => headless.OnUiThread(() =>
    {
        // ComposerView.swift:1008-1010, EditorAttachments.swift:30: xmark.circle.fill, .symbolRenderingMode(.palette),
        // .foregroundStyle(.white, .black.opacity(0.6)): a white x on a dark disc, not the monochrome disc with an x cut out of it.
        var blue = SwiftColor.Rgb(0x0000FF);
        var window = Show(Framed(SwiftImage.SystemName("xmark.circle.fill").Resizable().SymbolRenderingMode(SymbolRenderingMode.Palette).ForegroundStyle(Red, blue), 48), 64, 64);
        using var bitmap = Capture(window);
        Assert.True(IsRed(Pixel(bitmap, 24, 24)), "the x's crossing is the first style");
        Assert.True(IsRed(Pixel(bitmap, 18, 18)), "so is the x's arm");
        var disc = Pixel(bitmap, 40, 24);
        Assert.True(disc.B > 200 && disc.R < 70 && disc.G < 70, "the disc is the second style");
        var between = Pixel(bitmap, 24, 36); // below the crossing, off both arms
        Assert.True(between.B > 200 && between.R < 70 && between.G < 70, "the disc runs under the x");
        Assert.True(IsWhite(Pixel(bitmap, 1, 1)), "and stops at its edge");
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-182")]
    public void symbolEffect_and_contentTransition_are_accepted_and_draw_statically_as_tracked_deferrals() => headless.OnUiThread(() =>
    {
        // EffortSlider.swift:667-670 .symbolEffect(.bounce, value: isOn); ModelsSettingsPage.swift:414-417
        // .contentTransition(.symbolEffect(.replace)).
        var font = SwiftFont.System(SwiftFont.TextStyle.Body, weight: SwiftFont.Weight.Semibold);
        var still = Drawn(TopLeft(SwiftImage.SystemName("bolt.fill").Font(font).ForegroundStyle(Red)), 32);
        var bounce = TopLeft(SwiftImage.SystemName("bolt.fill").Font(font).ForegroundStyle(Red).SymbolEffect(SymbolEffectKind.Bounce, true));
        Assert.Equal(SymbolEffectKind.Bounce, bounce.DeferredSymbolEffect);
        Assert.Equal(still, Drawn(bounce, 32));

        var plain = Drawn(TopLeft(SwiftImage.SystemName("checkmark.circle.fill").Font(SwiftFont.System(17)).ForegroundStyle(Red)), 32);
        var replace = TopLeft(SwiftImage.SystemName("checkmark.circle.fill").Font(SwiftFont.System(17)).ForegroundStyle(Red)
            .ContentTransitionSymbolEffect(SymbolEffectKind.Replace));
        Assert.Equal(SymbolEffectKind.Replace, replace.DeferredContentTransition);
        Assert.Null(replace.DeferredSymbolEffect);
        Assert.Equal(plain, Drawn(replace, 32));

        var header = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Marrow.UI", "Port", "SwiftImage.cs"));
        Assert.Contains("MARROW_DEFERRED ED-182", header, StringComparison.Ordinal);
        Assert.Contains(".symbolEffect", header, StringComparison.Ordinal);
        Assert.Contains(".contentTransition(.symbolEffect", header, StringComparison.Ordinal);
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
