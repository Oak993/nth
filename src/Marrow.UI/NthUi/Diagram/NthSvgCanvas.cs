// PORT-ONLY: web surfaces W16c (DESIGN.md 7.2 "drawn natively by the svg component ... native vector"; 7.4 motion; 10; ED-NEW-web-w16c-1).
// No Swift counterpart. The control that draws a sanitised SVG: it sizes itself from the picture's own box (never wider than the
// room it is given, never stretched), draws with NthSvgPainter on every render, and follows the thread's theme (currentColor is
// the primary text colour; a model's line art can be adapted for contrast). Nothing here parses markup.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.UI.Port;

namespace Marrow.UI.NthUi.Diagram;

public sealed class NthSvgCanvas : Control
{
    /// <summary>The tallest a picture grows (a very tall picture is scaled down to fit).</summary>
    public const double TallestPicture = 1600;

    private NthSvgDocument? document;
    private NthSvgPainter? painter;
    private double? requestedWidth;
    private bool adapt;
    private FontFamily? body;
    private FontFamily? mono;

    public NthSvgCanvas()
    {
        IsHitTestVisible = false;
        Focusable = false;
        AutomationProperties.SetControlTypeOverride(this, AutomationControlType.Image);
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public NthSvgDocument? Document
    {
        get => document;
        set
        {
            if (ReferenceEquals(document, value)) return;
            document = value;
            painter = value is null ? null : new NthSvgPainter(value);
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>The width the picture asks for (the line's <c>width</c>); null takes the picture's own.</summary>
    public double? RequestedWidth
    {
        get => requestedWidth;
        set
        {
            if (requestedWidth == value) return;
            requestedWidth = value;
            InvalidateMeasure();
        }
    }

    /// <summary>Whether strokes and text are adapted to the surface (a model's line art, not a themed diagram).</summary>
    public bool Adapt
    {
        get => adapt;
        set
        {
            if (adapt == value) return;
            adapt = value;
            InvalidateVisual();
        }
    }

    /// <summary>The scale the picture is drawn at, relative to its own box (tests and the diagram's scroll rule).</summary>
    public double Scale => document is { } doc && doc.ViewBox.Width > 0 ? Bounds.Width / doc.ViewBox.Width : 1;

    /// <summary>Shapes and texts drawn by the last render (tests).</summary>
    public int Drawn => painter?.Drawn ?? 0;

    /// <summary>The size a picture of this document takes in <paramref name="width"/> DIPs.</summary>
    public static Size Fit(NthSvgDocument doc, double natural, double width)
    {
        var box = doc.ViewBox;
        if (box.Width <= 0 || box.Height <= 0) return default;
        var aspect = box.Height / box.Width;
        var w = Math.Max(1, Math.Min(natural, width));
        if (w * aspect > TallestPicture) w = TallestPicture / aspect;
        return new Size(w, w * aspect);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (document is not { } doc) return default;
        var natural = requestedWidth ?? doc.DeclaredSize?.Width ?? doc.ViewBox.Width;
        var width = double.IsInfinity(availableSize.Width) ? natural : availableSize.Width;
        return Fit(doc, natural, width);
    }

    public override void Render(DrawingContext context)
    {
        if (painter is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var theme = ActualThemeVariant;
        body ??= NthUiTokens.Body.Resolve().FontFamily;
        mono ??= NthUiTokens.Mono.Resolve().FontFamily;
        var paint = new NthSvgPaintContext
        {
            CurrentColor = NthUiTokens.Text1.Resolve(theme),
            Backdrop = NthUiTokens.Backdrop.Resolve(theme),
            Adapt = adapt,
            Fallback = body,
            Mono = mono,
        };
        painter.Draw(context, new Rect(Bounds.Size), paint);
    }
}

/// <summary>A fade-in of one control on the live view's frame loop (a picture that has just become drawable).</summary>
internal sealed class NthSvgFade : INthUiAnimated
{
    private readonly Control target;
    private NthUiTween fade;

    public NthSvgFade(Control target, double now, double seconds, double from = 0)
    {
        this.target = target;
        fade = NthUiTween.At(from).Retarget(1, now, seconds, Marrow.UI.Port.Nth.NthEase.Out);
        target.Opacity = from;
    }

    public bool Step(double now)
    {
        target.Opacity = Math.Clamp(fade.Value(now), 0, 1);
        if (!fade.Settled(now)) return true;
        target.Opacity = 1;
        return false;
    }
}
