// PORT-ONLY: SwiftUI Image(systemName:) and Image(_ name:) have no Avalonia twin; this control stands in for both
// (ED-179 (4)). Symbols draw the Lucide icon OD-0010's mapping names; the three asset-catalogue images are the pinned
// files in Assets/Images (ED-179 (3)).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Marrow.UI.Port;

// Decisions: ED-179, OD-0010, PA-0012, porting/icons/README.md. Visual parity with SF Symbols stays UNVERIFIED under
// OD-0001 and the README's "Limits of this table".
//
// INCOMPLETE (ED-179 (2) stop, awaiting the orchestrator's ruling; see the unit report):
//   - the size a symbol takes from .font(...) and .imageScale(...) when it is not resizable, and the stroke width a
//     font weight gives; a non-resizable symbol throws on measure until then, so no call site draws a guessed size;
//   - fill_rendering (README: "render with fill=currentColor on closed shapes or accept the outline") and
//     composed_rendering (container, slash, badge, text in a container): the Lucide outline is drawn as is;
//   - .symbolRenderingMode, .symbolEffect and .contentTransition on symbols are not provided.

/// <summary>SwiftUI <c>Image.TemplateRenderingMode</c>.</summary>
public enum ImageRenderingMode
{
    Original,
    Template,
}

/// <summary>SwiftUI <c>ContentMode</c>, for <c>.aspectRatio(contentMode:)</c>.</summary>
public enum ContentMode
{
    Fit,
    Fill,
}

/// <summary>
/// A SwiftUI <c>Image</c>: an SF Symbol (drawn as its Lucide icon) or a named asset-catalogue image. An unknown name
/// is an empty image that takes no space and draws nothing, as SwiftUI's is. The modifiers set this control and
/// return it, so call sites read like the Swift chain.
/// </summary>
public sealed class SwiftImage : Control
{
    /// <summary>The foreground style, inherited from enclosing views as SwiftUI's environment passes it down.</summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<SwiftImage>();

    private sealed record Asset(SvgIcon? Icon, Bitmap? Bitmap, Bitmap? Bitmap2x, ImageRenderingMode Intent);

    // DroppyCode/Resources/Assets.xcassets: the three images app code names (ED-179 (3)), with each imageset's
    // "template-rendering-intent" (droppy-logo "original"; hydra-mark and badge-edit "template").
    private static readonly Lazy<IReadOnlyDictionary<string, Asset>> Assets = new(() => new Dictionary<string, Asset>(StringComparer.Ordinal)
    {
        ["droppy-logo"] = new(null, LoadBitmap("droppy-logo.png"), LoadBitmap("droppy-logo@2x.png"), ImageRenderingMode.Original),
        ["hydra-mark"] = new(LoadSvg("hydra-mark.svg"), null, null, ImageRenderingMode.Template),
        ["badge-edit"] = new(LoadSvg("badge-edit.svg"), null, null, ImageRenderingMode.Template),
    });

    private readonly ImageRenderingMode intent;
    private ImageRenderingMode? renderingMode;

    static SwiftImage()
    {
        AffectsRender<SwiftImage>(ForegroundProperty);
    }

    private SwiftImage(string? symbolName, string? assetName, SymbolMapping? mapping, SvgIcon? icon, Bitmap? bitmap,
        Bitmap? bitmap2x, ImageRenderingMode intent)
    {
        SymbolName = symbolName;
        AssetName = assetName;
        Mapping = mapping;
        Icon = icon;
        Bitmap = bitmap;
        Bitmap2x = bitmap2x;
        this.intent = intent;
    }

    /// <summary><c>Image(systemName:)</c>.</summary>
    public static SwiftImage SystemName(string name)
    {
        var mapping = SymbolCatalogue.Mapping(name);
        var icon = mapping is null ? null : SymbolCatalogue.Icon(mapping.Lucide);
        return new SwiftImage(name, null, mapping, icon, null, null, ImageRenderingMode.Template);
    }

    /// <summary><c>Image(_ name:)</c> for an asset-catalogue image.</summary>
    public static SwiftImage Named(string name) => Assets.Value.TryGetValue(name, out var asset)
        ? new SwiftImage(null, name, null, asset.Icon, asset.Bitmap, asset.Bitmap2x, asset.Intent)
        : new SwiftImage(null, name, null, null, null, null, ImageRenderingMode.Original);

    public string? SymbolName { get; }

    public string? AssetName { get; }

    public SymbolMapping? Mapping { get; }

    /// <summary>The vector content: the symbol's Lucide icon or a vector asset; null for a bitmap or an unknown name.</summary>
    public SvgIcon? Icon { get; }

    /// <summary>The 1x representation of a bitmap asset.</summary>
    public Bitmap? Bitmap { get; }

    /// <summary>The 2x representation of a bitmap asset, used when the screen scale is 1.5 or more.</summary>
    public Bitmap? Bitmap2x { get; }

    /// <summary>True when the name matched nothing: the image takes no space and draws nothing.</summary>
    public bool IsEmpty => Icon is null && Bitmap is null;

    public bool IsResizable { get; private set; }

    public ContentMode? AspectContentMode { get; private set; }

    /// <summary><c>.renderingMode(_:)</c> when set, otherwise the image's own intent (symbols are templates).</summary>
    public ImageRenderingMode EffectiveRenderingMode => renderingMode ?? intent;

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary><c>.resizable()</c>: the image fills the space it is given.</summary>
    public SwiftImage Resizable()
    {
        IsResizable = true;
        InvalidateMeasure();
        InvalidateVisual();
        return this;
    }

    /// <summary><c>.aspectRatio(contentMode:)</c> with the image's own aspect ratio.</summary>
    public SwiftImage AspectRatio(ContentMode mode)
    {
        AspectContentMode = mode;
        InvalidateMeasure();
        InvalidateVisual();
        return this;
    }

    /// <summary><c>.renderingMode(_:)</c>.</summary>
    public SwiftImage RenderingMode(ImageRenderingMode mode)
    {
        renderingMode = mode;
        InvalidateVisual();
        return this;
    }

    /// <summary><c>.interpolation(_:)</c>, as the Avalonia bitmap interpolation mode.</summary>
    public SwiftImage Interpolation(BitmapInterpolationMode mode)
    {
        RenderOptions.SetBitmapInterpolationMode(this, mode);
        InvalidateVisual();
        return this;
    }

    /// <summary><c>.foregroundStyle(_:)</c> with a colour.</summary>
    public SwiftImage ForegroundStyle(SwiftColor color)
    {
        Foreground = color.Brush(this);
        return this;
    }

    /// <summary>The image's own size in points: the vector's view box (each asset SVG declares the same width and
    /// height) or the 1x bitmap's pixel size.</summary>
    private Size NaturalSize()
    {
        if (Bitmap is { } bitmap) return new Size(bitmap.PixelSize.Width, bitmap.PixelSize.Height);
        if (Icon is not { } icon) return default;
        if (SymbolName is not null) throw SizingPending();
        return icon.ViewBox.Size;
    }

    private InvalidOperationException SizingPending() => new(
        $"SwiftImage.SystemName(\"{SymbolName}\"): the size of a symbol that is not resizable into a finite frame (from .font and .imageScale) " +
        "awaits the orchestrator's ruling (ED-179 (2)); it is not guessed.");

    private double AspectRatioValue()
    {
        if (Bitmap is { } bitmap) return (double)bitmap.PixelSize.Width / bitmap.PixelSize.Height;
        return Icon is { } icon ? icon.ViewBox.Width / icon.ViewBox.Height : 1;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (IsEmpty) return default;
        if (!IsResizable) return NaturalSize();
        var infiniteWidth = double.IsInfinity(availableSize.Width);
        var infiniteHeight = double.IsInfinity(availableSize.Height);
        if (infiniteWidth && infiniteHeight) return NaturalSize();
        if (AspectContentMode is null)
        {
            if (infiniteWidth || infiniteHeight)
            {
                var natural = NaturalSize();
                return new Size(infiniteWidth ? natural.Width : availableSize.Width, infiniteHeight ? natural.Height : availableSize.Height);
            }
            return availableSize;
        }
        var ratio = AspectRatioValue();
        if (infiniteWidth) return new Size(availableSize.Height * ratio, availableSize.Height);
        if (infiniteHeight) return new Size(availableSize.Width, availableSize.Width / ratio);
        return Aspect(new Rect(availableSize), ratio, AspectContentMode.Value).Size;
    }

    private static Rect Aspect(Rect frame, double ratio, ContentMode mode)
    {
        var byWidth = new Size(frame.Width, frame.Width / ratio);
        var byHeight = new Size(frame.Height * ratio, frame.Height);
        var fitsByWidth = byWidth.Height <= frame.Height;
        var size = mode == ContentMode.Fit ? (fitsByWidth ? byWidth : byHeight) : (fitsByWidth ? byHeight : byWidth);
        return new Rect(frame.X + (frame.Width - size.Width) / 2, frame.Y + (frame.Height - size.Height) / 2, size.Width, size.Height);
    }

    private Rect Destination()
    {
        var bounds = new Rect(Bounds.Size);
        if (IsResizable)
            return AspectContentMode is { } mode ? Aspect(bounds, AspectRatioValue(), mode) : bounds;
        var natural = NaturalSize();
        return new Rect((bounds.Width - natural.Width) / 2, (bounds.Height - natural.Height) / 2, natural.Width, natural.Height);
    }

    public override void Render(DrawingContext context)
    {
        if (IsEmpty) return;
        var destination = Destination();
        if (destination.Width <= 0 || destination.Height <= 0) return;
        var template = EffectiveRenderingMode == ImageRenderingMode.Template;
        if (Icon is { } icon) DrawVector(context, icon, destination, template);
        else DrawBitmap(context, destination, template);
    }

    private void DrawVector(DrawingContext context, SvgIcon icon, Rect destination, bool template)
    {
        var box = icon.ViewBox;
        var transform = Matrix.CreateTranslation(-box.X, -box.Y)
            * Matrix.CreateScale(destination.Width / box.Width, destination.Height / box.Height)
            * Matrix.CreateTranslation(destination.X, destination.Y);
        using (context.PushTransform(transform))
        {
            foreach (var shape in icon.Shapes)
            {
                var fill = Brush(shape.Fill, template);
                var strokeBrush = Brush(shape.Stroke, template);
                var pen = strokeBrush is null ? null : new Pen(strokeBrush, shape.StrokeWidth, lineCap: shape.LineCap, lineJoin: shape.LineJoin);
                if (fill is null && pen is null) continue;
                context.DrawGeometry(fill, pen, shape.Geometry);
            }
        }
    }

    private IBrush? Brush(SvgPaint paint, bool template) => paint.Kind switch
    {
        SvgPaintKind.None => null,
        SvgPaintKind.CurrentColor => Foreground,
        _ => template ? Foreground : new Avalonia.Media.Immutable.ImmutableSolidColorBrush(paint.Color),
    };

    private void DrawBitmap(DrawingContext context, Rect destination, bool template)
    {
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var bitmap = scaling >= 1.5 && Bitmap2x is not null ? Bitmap2x : Bitmap!;
        var source = new Rect(bitmap.Size);
        if (!template)
        {
            context.DrawImage(bitmap, source, destination);
            return;
        }
        if (Foreground is not { } foreground) return;
        // A template image draws its alpha in the foreground style.
        var mask = new ImageBrush(bitmap) { Stretch = Stretch.Fill, DestinationRect = new RelativeRect(destination, RelativeUnit.Absolute) };
        using (context.PushOpacityMask(mask, destination))
            context.FillRectangle(foreground, destination);
    }

    private static Uri ImageUri(string file) => new($"avares://Marrow.UI/Assets/Images/{file}");

    private static Bitmap LoadBitmap(string file)
    {
        using var stream = AssetLoader.Open(ImageUri(file));
        return new Bitmap(stream);
    }

    private static SvgIcon LoadSvg(string file)
    {
        using var stream = AssetLoader.Open(ImageUri(file));
        using var reader = new StreamReader(stream);
        return SvgIcon.Parse(reader.ReadToEnd());
    }
}
