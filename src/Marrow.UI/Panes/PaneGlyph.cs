// PORT-ONLY: web surfaces W3a, the pane workspace's line glyphs (E:/Droppy/work/design/web-surfaces/index.html, the
// <symbol id="i-…"> sprite the approved canvas draws its pane headers, rails and capsule with; DESIGN.md 3.12 "Header
// controls are plain glyphs"; ED-NEW-web-w3a-1). No Swift counterpart. The paths are the canvas's own, drawn as the canvas
// draws them (.ic: no fill, stroke 1.6 in the 24-unit box, round caps and joins), so a 14 DIP glyph has the canvas's
// weight.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Marrow.Core.Models;

namespace Marrow.UI.Panes;

/// <summary>The canvas glyphs the workspace draws.</summary>
public enum PaneGlyphKind
{
    SideChat,
    Artifacts,
    Browser,
    Tasks,
    Changes,
    Terminal,
    App,
    Transcript,
    Expand,
    Restore,
    Close,
    More,
    Chevron,

    /// <summary>The canvas's i-plus (New terminal; later units' New tab).</summary>
    Plus,

    /// <summary>A pane with its foot band marked: the chat's drawer (W4's "Move to the drawer").</summary>
    DockBottom,

    /// <summary>W8a, Visus's toolbar and chips: the canvas's i-back.</summary>
    Back,

    /// <summary>The canvas's i-fwd.</summary>
    Forward,

    /// <summary>The canvas's i-reload.</summary>
    Reload,

    /// <summary>The canvas's i-lock (the site chip of an https page).</summary>
    Lock,

    /// <summary>The canvas's i-mask (private tabs).</summary>
    Mask,

    /// <summary>The canvas's i-search.</summary>
    Search,

    /// <summary>The canvas's i-copy.</summary>
    Copy,

    /// <summary>The canvas's i-ext (open elsewhere).</summary>
    External,

    /// <summary>The canvas's i-warn.</summary>
    Warn,

    /// <summary>The canvas's i-shield (prompts).</summary>
    Shield,
}

/// <summary>A line glyph in the inherited text colour.</summary>
public sealed class PaneGlyph : Control
{
    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<PaneGlyph>();

    public static readonly StyledProperty<PaneGlyphKind> KindProperty =
        AvaloniaProperty.Register<PaneGlyph, PaneGlyphKind>(nameof(Kind));

    /// <summary>The canvas's stroke, in the 24-unit box.</summary>
    public const double Stroke = 1.6;

    private const double Box = 24;

    private static readonly Dictionary<PaneGlyphKind, Geometry> Shapes = new()
    {
        [PaneGlyphKind.SideChat] = Parse(
            "M4 6.5h10a1.5 1.5 0 0 1 1.5 1.5v6a1.5 1.5 0 0 1-1.5 1.5H8.5L5 18.5v-3H4A1.5 1.5 0 0 1 2.5 14V8A1.5 1.5 0 0 1 4 6.5z" +
            "M18 9.5h1.5A1.5 1.5 0 0 1 21 11v5.5a1.5 1.5 0 0 1-1.5 1.5H19v2.5L16 18h-3"),
        [PaneGlyphKind.Artifacts] = Parse("M7 4h10a3 3 0 0 1 3 3v10a3 3 0 0 1-3 3H7a3 3 0 0 1-3-3V7a3 3 0 0 1 3-3z M4 9.5h16M9.5 9.5V20"),
        [PaneGlyphKind.Browser] = Parse(Circle(12, 12, 8) + "M4 12h16M12 4c2.6 2.6 2.6 13.4 0 16M12 4c-2.6 2.6-2.6 13.4 0 16"),
        [PaneGlyphKind.Tasks] = Parse("M9 6.5h11M9 12h11M9 17.5h11" + Circle(4.5, 6.5, 1) + Circle(4.5, 12, 1) + Circle(4.5, 17.5, 1)),
        [PaneGlyphKind.Changes] = Parse("M8 4v11M5 7l3-3 3 3M16 20V9M13 17l3 3 3-3"),
        [PaneGlyphKind.Terminal] = Parse("M5 7l5 5-5 5M12.5 17H19"),
        [PaneGlyphKind.App] = Parse("M12 3l8 4.5v9L12 21l-8-4.5v-9z M12 12l8-4.5M12 12v9M12 12L4 7.5"),
        [PaneGlyphKind.Transcript] = Parse("M5 5.5h14a1.5 1.5 0 0 1 1.5 1.5v8a1.5 1.5 0 0 1-1.5 1.5H10l-4.5 3.5V16.5H5A1.5 1.5 0 0 1 3.5 15V7A1.5 1.5 0 0 1 5 5.5z"),
        [PaneGlyphKind.Expand] = Parse("M14 4h6v6M20 4l-6.5 6.5M10 20H4v-6M4 20l6.5-6.5"),
        [PaneGlyphKind.Restore] = Parse("M20 4l-6 6M14 5v5h5M4 20l6-6M10 19v-5H5"),
        [PaneGlyphKind.Close] = Parse("M6 6l12 12M18 6L6 18"),
        [PaneGlyphKind.More] = Parse(Circle(12, 5.5, 1.2) + Circle(12, 12, 1.2) + Circle(12, 18.5, 1.2)),
        [PaneGlyphKind.Chevron] = Parse("M7 10l5 5 5-5"),
        [PaneGlyphKind.Plus] = Parse("M12 5v14M5 12h14"),
        [PaneGlyphKind.Back] = Parse("M15 18l-6-6 6-6"),
        [PaneGlyphKind.Forward] = Parse("M9 18l6-6-6-6"),
        [PaneGlyphKind.Reload] = Parse("M19.5 12a7.5 7.5 0 1 1-2.2-5.3M19.5 4.5v4.5H15"),
        [PaneGlyphKind.Lock] = Parse("M7.5 11h9a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-9a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2z M8.5 11V8a3.5 3.5 0 0 1 7 0v3"),
        [PaneGlyphKind.Mask] = Parse(
            "M3 9.5c3-2.5 15-2.5 18 0-.6 4.5-3.2 7-5.6 7-1.5 0-2-1.6-3.4-1.6S10.1 16.5 8.6 16.5C6.2 16.5 3.6 14 3 9.5z M7.5 11.5l2 .5M14.5 12l2-.5"),
        [PaneGlyphKind.Search] = Parse(Circle(11, 11, 6) + "M20 20l-4.5-4.5"),
        [PaneGlyphKind.Copy] = Parse(
            "M10.5 8.5h7a2 2 0 0 1 2 2v7a2 2 0 0 1-2 2h-7a2 2 0 0 1-2-2v-7a2 2 0 0 1 2-2z M15.5 8.5V6.5a2 2 0 0 0-2-2h-7a2 2 0 0 0-2 2v7a2 2 0 0 0 2 2h2"),
        [PaneGlyphKind.External] = Parse("M14 4h6v6M20 4l-8.5 8.5M18 14v4a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h4"),
        [PaneGlyphKind.Warn] = Parse("M12 4l9 16H3z M12 10v4M12 17h.01"),
        [PaneGlyphKind.Shield] = Parse("M12 3.5l7 2.5v5.5c0 4.4-3 7.6-7 9-4-1.4-7-4.6-7-9V6z"),
        [PaneGlyphKind.DockBottom] = Parse("M7 4h10a3 3 0 0 1 3 3v10a3 3 0 0 1-3 3H7a3 3 0 0 1-3-3V7a3 3 0 0 1 3-3z M4 14.5h16"),
    };

    static PaneGlyph()
    {
        AffectsRender<PaneGlyph>(ForegroundProperty, KindProperty);
    }

    public PaneGlyph()
    {
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        IsHitTestVisible = false;
    }

    public PaneGlyph(PaneGlyphKind kind, double size = 14) : this()
    {
        Kind = kind;
        Width = size;
        Height = size;
    }

    public PaneGlyphKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>The kind glyph of a pane (the canvas headers and capsule).</summary>
    public static PaneGlyphKind For(PaneKind kind) => kind switch
    {
        PaneKind.SideChat => PaneGlyphKind.SideChat,
        PaneKind.Artifacts => PaneGlyphKind.Artifacts,
        PaneKind.Browser => PaneGlyphKind.Browser,
        PaneKind.BackgroundTasks => PaneGlyphKind.Tasks,
        PaneKind.Changes => PaneGlyphKind.Changes,
        PaneKind.Terminal => PaneGlyphKind.Terminal,
        PaneKind.App => PaneGlyphKind.App,
        PaneKind.Transcript or PaneKind.Chat => PaneGlyphKind.Transcript,
    };

    /// <summary>The geometry of a glyph in its 24-unit box (tests read it).</summary>
    public static Geometry Geometry(PaneGlyphKind kind) => Shapes[kind];

    protected override Size MeasureOverride(Size availableSize) => new(double.IsNaN(Width) ? 14 : Width, double.IsNaN(Height) ? 14 : Height);

    public override void Render(DrawingContext context)
    {
        if (Foreground is not { } brush || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var scale = Math.Min(Bounds.Width, Bounds.Height) / Box;
        var pen = new ImmutablePen(brush.ToImmutable(), Stroke, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation((Bounds.Width - Box * scale) / 2, (Bounds.Height - Box * scale) / 2)))
            context.DrawGeometry(null, pen, Shapes[Kind]);
    }

    private static Geometry Parse(string data) => StreamGeometry.Parse(data);

    // An SVG <circle> as two arcs.
    private static string Circle(double cx, double cy, double r) => FormattableString.Invariant(
        $"M{cx - r} {cy}a{r} {r} 0 1 0 {2 * r} 0a{r} {r} 0 1 0 {-2 * r} 0z");
}
