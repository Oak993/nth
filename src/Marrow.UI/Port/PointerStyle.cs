// PORT-ONLY: SwiftUI's `.pointerStyle(_:)` modifier (App/Views/Composer/FollowUpQueueTab.swift:384,
// App/Views/Settings/ModelsSettingsPage.swift:184, App/Views/Sidebar/SidebarView.swift:124,
// App/Views/Chat/TimelineMinimap.swift:211), over Avalonia's Cursor. PA-0019, ED-205.
using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Marrow.UI.Port;

/// <summary>
/// SwiftUI's <c>PointerStyle</c>, restricted to the three raw values the pinned B3 tree passes to
/// <c>.pointerStyle(_:)</c> (a repo-wide grep of DroppyCode for "pointerStyle" at 52bc792 returns exactly four
/// call sites: FollowUpQueueTab.swift:384 and ModelsSettingsPage.swift:184, both
/// <c>isDragged ? .grabActive : .grabIdle</c>; SidebarView.swift:124, <c>drag.id != nil ? .grabActive : nil</c>;
/// TimelineMinimap.swift:211, <c>.link</c>). The <c>nil</c> arm at SidebarView.swift:124 is not a member of this
/// type - it is the absence of an override, carried by the nullable argument to
/// <see cref="PointerStyleControl.PointerStyle"/>.
/// </summary>
public enum PointerStyle
{
    /// <summary><c>.grabIdle</c>: AppKit's NSCursor.openHand, shown over a grip before a drag starts.</summary>
    GrabIdle,

    /// <summary><c>.grabActive</c>: AppKit's NSCursor.closedHand, shown while a grip is being dragged.</summary>
    GrabActive,

    /// <summary><c>.link</c>: AppKit's NSCursor.pointingHand. Avalonia already has this cursor
    /// (<see cref="StandardCursorType.Hand"/>), so PA-0019 ships no custom art for it.</summary>
    Link,
}

/// <summary>The attached modifier itself: SwiftUI's stateless <c>.pointerStyle(_:)</c>. Callers re-apply it
/// whenever the state it reads changes (a drag's start and end), exactly as SwiftUI re-evaluates the modifier's
/// argument on every body pass; this type keeps no per-control state of its own.</summary>
public static class PointerStyleControl
{
    /// <summary><c>.pointerStyle(style)</c>: sets the control's cursor for <paramref name="style"/>, or clears
    /// the override back to the control's normal cursor (Avalonia's own inherited/default one) when
    /// <paramref name="style"/> is null. The rendered grab cursors are sized for the control's own render
    /// scaling (<see cref="TopLevel.RenderScaling"/>; 1 with no attached <see cref="TopLevel"/>, as in a test
    /// that never shows a window).</summary>
    public static T PointerStyle<T>(this T control, PointerStyle? style) where T : Control
    {
        control.Cursor = style is null ? null : PointerStyleCursors.Get(style.Value, TopLevel.GetTopLevel(control)?.RenderScaling ?? 1);
        return control;
    }
}

/// <summary>
/// PA-0019's two custom cursors: the Lucide icons <c>hand.svg</c> (grab-idle) and <c>hand-fist.svg</c>
/// (grab-active; both already shipped at <c>Assets/Icons/lucide/</c>, ISC licence), rasterized through
/// <see cref="SvgIcon"/> as a black glyph over a wider white halo so it reads on any background, at the system
/// cursor's size (<see cref="BaseSize"/> px at 100%, ED-205) scaled for the control's render scaling. The pixel
/// art is UNVERIFIED (PA-0019): no reference-runtime capture of AppKit's actual openHand/closedHand bitmaps
/// exists under OD-0001, so this is a best-effort visual match, not a source fact. The hotspot is the centre of
/// the rendered square, matching AppKit's openHand/closedHand hotspot at the centre of their 16x16 image (the
/// same relative position at any size). <see cref="PointerStyle.Link"/> needs none of this: it is Avalonia's own
/// <see cref="StandardCursorType.Hand"/>. Both the rendered bitmaps and the wrapping <see cref="Cursor"/>
/// instances are cached per (style, scale), so re-applying the modifier on every drag tick never re-rasterizes.
/// </summary>
public static class PointerStyleCursors
{
    /// <summary>The rendered cursor's side in pixels at 100% render scaling. ENGINEERING DECISION (ED-205):
    /// PA-0019 names no source pixel size (AppKit's own openHand/closedHand bitmaps are not captured, OD-0001),
    /// so this follows Windows' own documented default system cursor size (SM_CXCURSOR/SM_CYCURSOR) rather than
    /// AppKit's 16x16 cursor bitmap.</summary>
    public const double BaseSize = 32;

    // ED-205: Lucide's own stroke is 2 of 24 view-box units (SvgIconTests); the halo pen is 3 units wider than
    // whatever stroke width a shape carries, a 1.5-unit white margin on each side of the black line. The 24-unit
    // icon sits 2 units in from every edge of the square it is scaled into, so the halo's overshoot past the
    // icon's own view box never clips against the rendered bitmap's edge.
    private const double HaloExtraWidth = 3;
    private const double IconMargin = 2;

    private static readonly Lazy<SvgIcon> GrabIdleIcon = new(() => LoadIcon("hand"));
    private static readonly Lazy<SvgIcon> GrabActiveIcon = new(() => LoadIcon("hand-fist"));
    private static readonly Lazy<Cursor> LinkCursor = new(() => new Cursor(StandardCursorType.Hand));

    private static readonly ConcurrentDictionary<(PointerStyle Style, double Scale), Bitmap> RenderCache = new();
    private static readonly ConcurrentDictionary<(PointerStyle Style, double Scale), Cursor> CursorCache = new();

    /// <summary>The rendered cursor's side in pixels at <paramref name="scale"/> (<see cref="BaseSize"/> scaled
    /// and rounded to the nearest whole pixel).</summary>
    public static int PixelSize(double scale) => (int)Math.Round(BaseSize * scale);

    /// <summary>The hotspot <see cref="Get"/> gives its <see cref="Cursor"/>: the centre of the square
    /// <see cref="PixelSize"/> renders, matching AppKit's openHand/closedHand hotspot at the centre of their own
    /// image.</summary>
    public static PixelPoint Hotspot(double scale)
    {
        var side = PixelSize(scale);
        return new PixelPoint(side / 2, side / 2);
    }

    /// <summary>The style's cursor at <paramref name="scale"/>: <see cref="PointerStyle.Link"/> is Avalonia's own
    /// <see cref="StandardCursorType.Hand"/> (PA-0019: no custom art needed, and no scale dependence either);
    /// the two grab styles are this class's rendered art, wrapped with <see cref="Hotspot"/>. The same
    /// <see cref="Cursor"/> instance comes back for a repeated (style, scale) pair.</summary>
    public static Cursor Get(PointerStyle style, double scale) => style == PointerStyle.Link
        ? LinkCursor.Value
        : CursorCache.GetOrAdd((style, scale), key => new Cursor(Render(key.Style, key.Scale), Hotspot(key.Scale)));

    /// <summary>Rasterizes <paramref name="style"/>'s glyph at <paramref name="scale"/>: a black stroke over a
    /// wider white halo, both at the Lucide icon's own line cap and join, into a square <see cref="PixelSize"/>
    /// on a side. The same <see cref="Bitmap"/> instance comes back for a repeated (style, scale) pair.
    /// <see cref="PointerStyle.Link"/> has no rendered art of its own (throws); use <see cref="Get"/> for it.</summary>
    public static Bitmap Render(PointerStyle style, double scale)
    {
        if (style == PointerStyle.Link)
            throw new ArgumentOutOfRangeException(nameof(style), style, "Link has no rendered art; it is StandardCursorType.Hand.");
        return RenderCache.GetOrAdd((style, scale),
            key => Rasterize(key.Style == PointerStyle.GrabIdle ? GrabIdleIcon.Value : GrabActiveIcon.Value, key.Scale));
    }

    private static Bitmap Rasterize(SvgIcon icon, double scale)
    {
        var pixels = PixelSize(scale);
        var size = new Size(pixels, pixels);
        var glyph = new HaloGlyph(icon);
        glyph.Measure(size);
        glyph.Arrange(new Rect(size));
        var bitmap = new RenderTargetBitmap(new PixelSize(pixels, pixels), new Vector(96 * scale, 96 * scale));
        bitmap.Render(glyph);
        return bitmap;
    }

    private static SvgIcon LoadIcon(string name)
    {
        using var stream = AssetLoader.Open(new Uri($"avares://Marrow.UI/Assets/Icons/lucide/{name}.svg"));
        using var reader = new StreamReader(stream);
        return SvgIcon.Parse(reader.ReadToEnd());
    }

    /// <summary>Draws <see cref="SvgIcon.Shapes"/> twice into its own square: a white halo pass, wider than each
    /// shape's own stroke, then the black glyph on top at the shape's own width - the macOS-style outline
    /// PA-0019 asks for. The icon's own view box sits <see cref="IconMargin"/> units in from every edge of the
    /// square it is scaled into, so the halo's overshoot never clips against the bitmap's own edge.</summary>
    private sealed class HaloGlyph(SvgIcon icon) : Control
    {
        protected override Size MeasureOverride(Size availableSize) => availableSize;

        public override void Render(DrawingContext context)
        {
            var box = icon.ViewBox;
            var padded = Math.Max(box.Width, box.Height) + 2 * IconMargin;
            var scale = Math.Min(Bounds.Width, Bounds.Height) / padded;
            var transform = Matrix.CreateTranslation(-box.X + IconMargin, -box.Y + IconMargin) * Matrix.CreateScale(scale, scale);
            using (context.PushTransform(transform))
            {
                foreach (var shape in icon.Shapes)
                    context.DrawGeometry(null, new Pen(Brushes.White, shape.StrokeWidth + HaloExtraWidth,
                        lineCap: shape.LineCap, lineJoin: shape.LineJoin), shape.Geometry);
                foreach (var shape in icon.Shapes)
                    context.DrawGeometry(null, new Pen(Brushes.Black, shape.StrokeWidth,
                        lineCap: shape.LineCap, lineJoin: shape.LineJoin), shape.Geometry);
            }
        }
    }
}
