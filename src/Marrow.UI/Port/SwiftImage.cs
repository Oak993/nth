// PORT-ONLY: SwiftUI Image(systemName:) and Image(_ name:) have no Avalonia twin; this control stands in for both
// (ED-179 (4)). Symbols draw the Lucide icon OD-0010's mapping names; the three asset-catalogue images are the pinned
// files in Assets/Images (ED-179 (3)).
using System.Collections.Concurrent;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Marrow.UI.Port;

// Decisions: ED-179, ED-182, OD-0010, PA-0012, porting/icons/README.md. Visual parity with SF Symbols stays UNVERIFIED
// under OD-0001 and the README's "Limits of this table"; every ED-182 value is oracle O-63 (to be measured against the
// eight tour screenshots of OD-0004 in the verification wave).
//
// ED-182, how a Lucide icon stands in for an SF Symbol:
//   (Q1) a symbol that is not resizable takes a square of side 1.2 S for its font's point size S (the inherited
//        FontProperty, SwiftUI's .font environment; SwiftFont.Body without one), centred in its frame; in a line of
//        text (an InlineUIContainer) its centre sits on the text's cap-height centre (TextBlock.BaselineOffset);
//   (Q2) the stroke is Lucide's 2 of 24 units at every weight (named visual compromise); FontWeight is recorded only;
//   (Q3) .imageScale multiplies the square by 0.8, 1.0 and 1.25;
//   (Q4) a .fill name fills its container (the outermost closed shape whose bounds enclose every other element) and
//        knocks every other element out; with no such shape it draws the outline;
//   (Q5) composed renderings (SymbolCatalogue.Composition): the glyph at 50 % in the centre of its container, the
//        (2,2)-(22,22) slash, the radius 4 badge at (18,6) knocked out 1.5 around, text in Inter Semibold 11 of 24 units;
//   (Q6) .symbolRenderingMode draws monochrome in the foreground colour (named visual compromise).
//
// MARROW_DEFERRED ED-182 (tracked incomplete work, drawn statically until the verification wave):
//   - .symbolEffect(.bounce, value:) (EffortSlider.swift:670, UsageViews.swift:267): SymbolEffect records it;
//   - .contentTransition(.symbolEffect(.replace)) (ComposerView.swift:834, ModelsSettingsPage.swift:417,
//     SidebarView.swift:2130, MarkdownView.swift:1490): ContentTransitionSymbolEffect records it.
//   Call sites that use them carry `// MARROW_DEFERRED ED-182`.
//
// UNRESOLVED (not covered by ED-182; reported to the orchestrator): composed-note clauses other than text in a
// circle or square, enclose, slash and badge (SymbolComposition.Undrawn: "add a down arrow", "write 4K on the screen",
// currency signs and the like; catalogue symbols only, none of the 146 names app code draws except
// macwindow.on.rectangle's "window floating over a rectangle") draw the Lucide icon without that clause.

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

/// <summary>SwiftUI <c>SymbolRenderingMode</c>; every mode draws monochrome in the foreground colour (ED-182 (Q6)).</summary>
public enum SymbolRenderingMode
{
    Monochrome,
    Hierarchical,
    Palette,
    Multicolor,
}

/// <summary>The SF symbol effects the pinned source names: <c>.bounce</c> (.symbolEffect) and <c>.replace</c>
/// (.contentTransition(.symbolEffect)). Recorded, not animated (MARROW_DEFERRED ED-182).</summary>
public enum SymbolEffectKind
{
    Bounce,
    Replace,
}

/// <summary>
/// A SwiftUI <c>Image</c>: an SF Symbol (drawn as its Lucide icon) or a named asset-catalogue image. An unknown name
/// is an empty image that takes no space and draws nothing, as SwiftUI's is. The modifiers set this control and
/// return it, so call sites read like the Swift chain.
/// </summary>
public sealed class SwiftImage : Control
{
    /// <summary>SwiftUI <c>Image.Scale</c>, for <c>.imageScale(_:)</c>.</summary>
    public enum Scale
    {
        Small,
        Medium,
        Large,
    }

    /// <summary>The foreground style, inherited from enclosing views as SwiftUI's environment passes it down.</summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<SwiftImage>();

    /// <summary>SwiftUI's <c>.font</c> environment as a symbol reads it (ED-182 (Q1)): set on the image or on any
    /// enclosing control, the nearest one winning.</summary>
    public static readonly AttachedProperty<SwiftFont?> FontProperty =
        AvaloniaProperty.RegisterAttached<SwiftImage, Control, SwiftFont?>("Font", inherits: true);

    private sealed record Asset(SvgIcon? Icon, Bitmap? Bitmap, Bitmap? Bitmap2x, ImageRenderingMode Intent);

    // DroppyCode/Resources/Assets.xcassets: the images app code names (ED-179 (3)), with each imageset's
    // "template-rendering-intent" (droppy-logo "original"; every other asset here "template"). The 25
    // hydra-head-NN marks (UI/Common/HydraGlyph.swift, HydraPersona.asset, Core/Models/Hydra.swift:231-257) are
    // first-party app artwork, not SF Symbols (PA-0012): reusable, per platform_adaptations.md PA-0012.
    private static readonly Lazy<IReadOnlyDictionary<string, Asset>> Assets = new(() => new Dictionary<string, Asset>(StringComparer.Ordinal)
    {
        ["droppy-logo"] = new(null, LoadBitmap("droppy-logo.png"), LoadBitmap("droppy-logo@2x.png"), ImageRenderingMode.Original),
        ["hydra-mark"] = new(LoadSvg("hydra-mark.svg"), null, null, ImageRenderingMode.Template),
        ["badge-edit"] = new(LoadSvg("badge-edit.svg"), null, null, ImageRenderingMode.Template),
    }.Concat(Enumerable.Range(1, 25).Select(i => new KeyValuePair<string, Asset>(
        $"hydra-head-{i:D2}", new(LoadSvg($"hydra-head-{i:D2}.svg"), null, null, ImageRenderingMode.Template))))
        .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal));

    // ED-182 (Q4, Q5): the drawing of each SF name, built on its first render and shared.
    private static readonly ConcurrentDictionary<string, SymbolDrawing> Drawings = new(StringComparer.Ordinal);

    // ED-182 (Q1): the cap height of each resolved font, measured once from its "H".
    private static readonly ConcurrentDictionary<(string Family, double Size, FontWeight Weight, FontStyle Style), double> CapHeights = new();

    private readonly ImageRenderingMode intent;
    private ImageRenderingMode? renderingMode;

    static SwiftImage()
    {
        AffectsRender<SwiftImage>(ForegroundProperty, FontProperty);
        AffectsMeasure<SwiftImage>(FontProperty);
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

    /// <summary>The <c>.font</c> a control passes down to the symbols inside it.</summary>
    public static SwiftFont? GetFont(Control control) => control.GetValue(FontProperty);

    /// <summary><c>.font(_:)</c> on an enclosing view: every symbol inside it without a nearer font takes it.</summary>
    public static void SetFont(Control control, SwiftFont? font) => control.SetValue(FontProperty, font);

    public string? SymbolName { get; }

    public string? AssetName { get; }

    public SymbolMapping? Mapping { get; }

    /// <summary>What the mapping's composed note draws over the Lucide icon (ED-182 (Q5)); null for an asset or an
    /// unknown name.</summary>
    public SymbolComposition? Composition => Mapping is null ? null : SymbolCatalogue.Composition(Mapping);

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

    /// <summary>The second style of <c>.foregroundStyle(_:_:)</c>; recorded, not drawn (ED-182 (Q6): monochrome).</summary>
    public IBrush? SecondaryForeground { get; private set; }

    /// <summary><c>.fontWeight(_:)</c>; recorded only, the stroke stays Lucide's 2 of 24 units (ED-182 (Q2)).</summary>
    public SwiftFont.Weight? Weight { get; private set; }

    /// <summary><c>.imageScale(_:)</c>; medium when none was set.</summary>
    public Scale SymbolImageScale { get; private set; } = Scale.Medium;

    /// <summary><c>.symbolRenderingMode(_:)</c>; monochrome when none was set.</summary>
    public SymbolRenderingMode SymbolRendering { get; private set; } = Port.SymbolRenderingMode.Monochrome;

    /// <summary><c>.symbolEffect(_:value:)</c>, recorded and not animated (MARROW_DEFERRED ED-182).</summary>
    public SymbolEffectKind? DeferredSymbolEffect { get; private set; }

    /// <summary><c>.contentTransition(.symbolEffect(_:))</c>, recorded and not animated (MARROW_DEFERRED ED-182).</summary>
    public SymbolEffectKind? DeferredContentTransition { get; private set; }

    /// <summary>ED-182 (Q1, Q3): the side of the square a symbol takes when it is not resizable: 1.2 times its font's
    /// point size (SwiftFont.Body's without a font), times 0.8, 1.0 or 1.25 for the image scale.</summary>
    public double SymbolSide => 1.2 * (GetValue(FontProperty) ?? SwiftFont.Body).PointSize * SymbolImageScale switch
    {
        Scale.Small => 0.8,
        Scale.Large => 1.25,
        _ => 1.0,
    };

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

    /// <summary><c>.foregroundStyle(_:_:)</c>: the primary style draws the symbol; the secondary is recorded only
    /// (ED-182 (Q6): no hierarchical, palette or multicolour layers).</summary>
    public SwiftImage ForegroundStyle(SwiftColor primary, SwiftColor secondary)
    {
        Foreground = primary.Brush(this);
        SecondaryForeground = secondary.Brush(this);
        return this;
    }

    /// <summary><c>.font(_:)</c> on the image itself (ED-182 (Q1)).</summary>
    public SwiftImage Font(SwiftFont font)
    {
        SetValue(FontProperty, font);
        return this;
    }

    /// <summary><c>.fontWeight(_:)</c>: recorded; the stroke does not change (ED-182 (Q2)).</summary>
    public SwiftImage FontWeight(SwiftFont.Weight weight)
    {
        Weight = weight;
        return this;
    }

    /// <summary><c>.imageScale(_:)</c> (ED-182 (Q3)).</summary>
    public SwiftImage ImageScale(Scale scale)
    {
        SymbolImageScale = scale;
        InvalidateMeasure();
        InvalidateVisual();
        return this;
    }

    /// <summary><c>.symbolRenderingMode(_:)</c>: recorded; every mode draws monochrome (ED-182 (Q6)).</summary>
    public SwiftImage SymbolRenderingMode(SymbolRenderingMode mode)
    {
        SymbolRendering = mode;
        return this;
    }

    /// <summary><c>.symbolEffect(_:value:)</c>: recorded; the image draws statically (MARROW_DEFERRED ED-182).</summary>
    public SwiftImage SymbolEffect(SymbolEffectKind effect, object? value)
    {
        _ = value;
        DeferredSymbolEffect = effect;
        return this;
    }

    /// <summary><c>.contentTransition(.symbolEffect(_:))</c>: recorded; a new symbol replaces the old one at once
    /// (MARROW_DEFERRED ED-182).</summary>
    public SwiftImage ContentTransitionSymbolEffect(SymbolEffectKind effect)
    {
        DeferredContentTransition = effect;
        return this;
    }

    /// <summary>The image's own size in points: a symbol's ED-182 square, the vector's view box (each asset SVG
    /// declares the same width and height) or the 1x bitmap's pixel size.</summary>
    private Size NaturalSize()
    {
        if (Bitmap is { } bitmap) return new Size(bitmap.PixelSize.Width, bitmap.PixelSize.Height);
        if (Icon is not { } icon) return default;
        if (SymbolName is not null) return new Size(SymbolSide, SymbolSide);
        return icon.ViewBox.Size;
    }

    private double AspectRatioValue()
    {
        if (Bitmap is { } bitmap) return (double)bitmap.PixelSize.Width / bitmap.PixelSize.Height;
        return Icon is { } icon ? icon.ViewBox.Width / icon.ViewBox.Height : 1;
    }

    /// <summary>ED-182 (Q1): a font's cap height, measured as the ink height of its "H" (Avalonia's FontMetrics carries
    /// no cap height; FormattedText builds no geometry without a brush, so it is given one); 0 when the face draws no
    /// "H", which puts the square's centre on the baseline.</summary>
    internal static double CapHeight(SwiftFont font)
    {
        var resolved = font.Resolve();
        return CapHeights.GetOrAdd((resolved.FontFamily.ToString(), resolved.FontSize, resolved.FontWeight, resolved.FontStyle), _ =>
        {
            var typeface = new Typeface(resolved.FontFamily, resolved.FontStyle, resolved.FontWeight);
            var text = new FormattedText("H", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, resolved.FontSize, Brushes.Black);
            return text.BuildGeometry(default)?.Bounds.Height ?? 0;
        });
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (IsEmpty) return default;
        if (!IsResizable)
        {
            if (SymbolName is not null)
            {
                // ED-182 (Q1): in a line of text the square's centre sits on the cap-height centre, so the text's
                // baseline lies half the square plus half the cap height below the square's top.
                SetCurrentValue(TextBlock.BaselineOffsetProperty,
                    SymbolSide / 2 + CapHeight(GetValue(FontProperty) ?? SwiftFont.Body) / 2);
            }
            return NaturalSize();
        }
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
        if (Icon is { } icon)
        {
            if (Mapping is { } mapping)
                DrawSymbol(context, Drawings.GetOrAdd(mapping.Sf, _ => SymbolDrawing.Build(mapping, icon)), destination, template);
            else
                DrawVector(context, icon.ViewBox, icon.Shapes, destination, template);
        }
        else DrawBitmap(context, destination, template);
    }

    private static Matrix ViewBoxTransform(Rect box, Rect destination) =>
        Matrix.CreateTranslation(-box.X, -box.Y)
        * Matrix.CreateScale(destination.Width / box.Width, destination.Height / box.Height)
        * Matrix.CreateTranslation(destination.X, destination.Y);

    private void DrawSymbol(DrawingContext context, SymbolDrawing drawing, Rect destination, bool template)
    {
        if (drawing.Area() is not { } area)
        {
            DrawVector(context, drawing.ViewBox, drawing.Shapes, destination, template);
            return;
        }
        if (Foreground is not { } foreground) return;
        using (context.PushTransform(ViewBoxTransform(drawing.ViewBox, destination)))
            context.DrawGeometry(foreground, null, area);
    }

    private void DrawVector(DrawingContext context, Rect box, IReadOnlyList<SvgShape> shapes, Rect destination, bool template)
    {
        using (context.PushTransform(ViewBoxTransform(box, destination)))
        {
            foreach (var shape in shapes)
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

    /// <summary>
    /// A symbol as drawn (ED-182 (Q4, Q5)): its Lucide shapes with the composition applied, in the icon's 24-unit view
    /// box, and, for a .fill name with a container or for a badge, the one area painted in the foreground colour.
    /// </summary>
    private sealed class SymbolDrawing
    {
        // ED-182 (Q5) geometry, in Lucide's 24-unit box.
        private static readonly Point BoxCentre = new(12, 12);
        private const double InnerGlyphScale = 0.5;
        private static readonly Point SlashFrom = new(2, 2);
        private static readonly Point SlashTo = new(22, 22);
        private const double SlashWidth = 2;
        private static readonly Point BadgeCentre = new(18, 6);
        private const double BadgeRadius = 4;
        private const double BadgeGap = 1.5;
        private const double TextSize = 11;

        private readonly bool fill;
        private readonly bool badge;
        private bool areaBuilt;
        private Geometry? area;

        private SymbolDrawing(Rect viewBox, IReadOnlyList<SvgShape> shapes, bool fill, bool badge)
        {
            ViewBox = viewBox;
            Shapes = shapes;
            this.fill = fill;
            this.badge = badge;
        }

        public Rect ViewBox { get; }

        public IReadOnlyList<SvgShape> Shapes { get; }

        public static SymbolDrawing Build(SymbolMapping mapping, SvgIcon icon)
        {
            var composition = SymbolCatalogue.Composition(mapping);
            var shapes = new List<SvgShape>();
            if (composition.Text is { } text)
            {
                // the Lucide icon is itself the circle or square the text sits in
                shapes.AddRange(icon.Shapes);
                shapes.Add(TextShape(text));
            }
            else if (composition.Container is { } container && SymbolCatalogue.ContainerIcon(container) is { } box)
            {
                shapes.AddRange(box.Shapes);
                shapes.AddRange(icon.Shapes.Select(Inner));
            }
            else shapes.AddRange(icon.Shapes);
            if (composition.Slash)
                shapes.Add(new SvgShape("line", new LineGeometry(SlashFrom, SlashTo), SvgPaint.None, SvgPaint.CurrentColor, SlashWidth,
                    PenLineCap.Round, PenLineJoin.Round, FillRule.NonZero, IsClosed: false));
            return new SymbolDrawing(icon.ViewBox, shapes.AsReadOnly(), mapping.Variant.Fill, composition.Badge);
        }

        /// <summary>The glyph scaled to half the box about its centre; the pen stays Lucide's 2 units (ED-182 (Q2)).</summary>
        private static SvgShape Inner(SvgShape shape)
        {
            var geometry = shape.Geometry.Clone();
            geometry.Transform = new MatrixTransform(Matrix.CreateTranslation(-BoxCentre.X, -BoxCentre.Y)
                * Matrix.CreateScale(InnerGlyphScale, InnerGlyphScale)
                * Matrix.CreateTranslation(BoxCentre.X, BoxCentre.Y));
            return shape with { Geometry = geometry };
        }

        /// <summary>Text in Inter Semibold at 11 of 24 units, filled, its ink centred on the box centre.</summary>
        private static SvgShape TextShape(string text)
        {
            var font = SwiftFont.System(TextSize, SwiftFont.Weight.Semibold).Resolve();
            var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(font.FontFamily, font.FontStyle, font.FontWeight), font.FontSize, Brushes.Black);
            var geometry = formatted.BuildGeometry(default) ?? new StreamGeometry();
            var ink = geometry.Bounds;
            geometry.Transform = new TranslateTransform(BoxCentre.X - ink.Center.X, BoxCentre.Y - ink.Center.Y);
            return new SvgShape("text", geometry, SvgPaint.CurrentColor, SvgPaint.None, 0, PenLineCap.Flat, PenLineJoin.Miter,
                FillRule.NonZero, IsClosed: false);
        }

        /// <summary>The one area to paint for a .fill name with a container or for a badge; null when the shapes
        /// draw as they are (no fill, or a .fill name whose icon has no enclosing closed shape: ED-182 (Q4)).</summary>
        public Geometry? Area()
        {
            if (areaBuilt) return area;
            areaBuilt = true;
            var container = fill ? Container() : null;
            if (container is null && !badge) return area = null;
            Geometry? result = null;
            if (container is not null)
            {
                // ED-182 (Q4): the container filled (its inside and its own stroke), every other element knocked out.
                result = Union(container.Geometry, Painted(container));
                foreach (var shape in Shapes)
                    if (!ReferenceEquals(shape, container) && Painted(shape) is { } knocked)
                        result = new CombinedGeometry(GeometryCombineMode.Exclude, result, knocked);
            }
            else
            {
                foreach (var shape in Shapes) result = Union(result, Painted(shape));
            }
            // ED-182 (Q5): the badge dot, with the drawing under it knocked out 1.5 units around it.
            var gapRadius = BadgeRadius + BadgeGap;
            var gap = new EllipseGeometry(new Rect(BadgeCentre.X - gapRadius, BadgeCentre.Y - gapRadius, 2 * gapRadius, 2 * gapRadius));
            var dot = new EllipseGeometry(new Rect(BadgeCentre.X - BadgeRadius, BadgeCentre.Y - BadgeRadius, 2 * BadgeRadius, 2 * BadgeRadius));
            if (badge)
                result = result is null ? dot : Union(new CombinedGeometry(GeometryCombineMode.Exclude, result, gap), dot);
            return area = result;
        }

        /// <summary>ED-182 (Q4): the outermost closed shape whose bounds enclose every other element's bounds.</summary>
        private SvgShape? Container()
        {
            SvgShape? best = null;
            foreach (var candidate in Shapes)
            {
                if (!candidate.IsClosed) continue;
                var bounds = candidate.Geometry.Bounds.Inflate(1e-3);
                if (!Shapes.All(other => ReferenceEquals(other, candidate) || bounds.Contains(other.Geometry.Bounds))) continue;
                if (best is null || Extent(candidate.Geometry.Bounds) > Extent(best.Geometry.Bounds)) best = candidate;
            }
            return best;
        }

        private static double Extent(Rect rect) => rect.Width * rect.Height;

        /// <summary>Where a shape puts paint: its fill, and its stroke widened by its pen.</summary>
        private static Geometry? Painted(SvgShape shape)
        {
            Geometry? painted = shape.Fill.Kind != SvgPaintKind.None ? shape.Geometry : null;
            if (shape.Stroke.Kind != SvgPaintKind.None && shape.StrokeWidth > 0)
                painted = Union(painted, shape.Geometry.GetWidenedGeometry(
                    new Pen(Brushes.Black, shape.StrokeWidth, lineCap: shape.LineCap, lineJoin: shape.LineJoin)));
            return painted;
        }

        private static Geometry? Union(Geometry? a, Geometry? b) =>
            a is null ? b : b is null ? a : new CombinedGeometry(GeometryCombineMode.Union, a, b);
    }
}
