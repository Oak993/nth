// PORT-ONLY: OD-0136 amendment 2, step 2 option A's "light through text" (ED-NEW-nth-motion), kept by the owner for the
// diff header ("Reading the diff", step 5), the running tool row's label (step 8) and the connecting labels (step 11). The
// word is drawn as the site draws it (its own colour, --text2 on the page); on the render thread the same glyphs are drawn
// again in --strong through a 60 px window with a linear ramp to its centre, which travels -w to +w across the word in 1.8 s
// (.45,0,.2,1) and rests 0.6 s. Calm: no light. The glyphs are the label's own shaped runs, so the light sits exactly on them.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;
using Avalonia.Skia;
using SkiaSharp;

namespace Marrow.UI.Port.Nth;

/// <summary>The label's glyphs (each with where TextLayout.Draw puts it), the word's box, and whether the light runs.</summary>
public sealed record NthShineState(IReadOnlyList<(IImmutableGlyphRunReference Run, Matrix Place)> Runs, Rect Box, bool Shining, double Period);

/// <summary>A TextBlock with the light passing through its text while <see cref="IsShining"/>.</summary>
public class NthShineText : TextBlock
{
    private readonly NthVisualHost host;
    private TextLayout? captured;
    private Point capturedAt;
    private IReadOnlyList<(IImmutableGlyphRunReference, Matrix)> runs = [];
    private Rect box;
    private bool shining = true;
    private double period = 2.4;

    public NthShineText() => host = new NthVisualHost(this, () => new NthShinePainter(), () => new NthShineState(runs, box, shining, period));

    /// <summary>Whether the light passes (a running row: true; done: false, and the label steps back).</summary>
    public bool IsShining { get => shining; set { if (shining == value) return; shining = value; if (value) host.Restart(); else host.Send(); } }

    /// <summary>The light's cycle (2.4 s; travel 75 %, rest 25 %).</summary>
    public double Period { get => period; set { period = value; host.Send(); } }

    /// <summary>The frame last sent (tests).</summary>
    public NthFrame? Frame => host.Frame;

    /// <summary>Whether the light is on screen and moving now.</summary>
    public bool IsMoving => host.IsMoving;

    public NthFrame Snapshot() => host.Snapshot();

    protected override Size ArrangeOverride(Size finalSize)
    {
        var arranged = base.ArrangeOverride(finalSize);
        Capture(arranged);
        return arranged;
    }

    // The glyphs as TextBlock.Render places them: the padding, and a too-short box centres or bottoms the text by its alignment.
    private void Capture(Size size)
    {
        var padding = Padding;
        var layout = TextLayout;
        var top = padding.Top;
        if (size.Height < layout.Height)
        {
            if (VerticalAlignment == Avalonia.Layout.VerticalAlignment.Center) top += (size.Height - layout.Height) / 2;
            else if (VerticalAlignment == Avalonia.Layout.VerticalAlignment.Bottom) top += size.Height - layout.Height;
        }
        var origin = new Point(padding.Left, top);
        if (ReferenceEquals(layout, captured) && origin == capturedAt) return;
        captured = layout; capturedAt = origin;
        var list = new List<(IImmutableGlyphRunReference, Matrix)>();
        double y = 0, left = double.PositiveInfinity, right = double.NegativeInfinity;
        foreach (var line in layout.TextLines)
        {
            var x = line.Start;
            foreach (var run in line.TextRuns)
            {
                if (run is ShapedTextRun shaped && shaped.GlyphRun.TryCreateImmutableGlyphRunReference() is { } reference)
                {
                    list.Add((reference, Matrix.CreateTranslation(origin.X + x, origin.Y + y + line.Baseline - shaped.GlyphRun.BaselineOrigin.Y)));
                    left = Math.Min(left, origin.X + x);
                    right = Math.Max(right, origin.X + x + shaped.Size.Width);
                }
                if (run is DrawableTextRun drawable) x += drawable.Size.Width;
            }
            y += line.Height;
        }
        runs = list;
        box = list.Count == 0 ? default : new Rect(left, origin.Y, right - left, y);
        host.Send();
    }
}

/// <summary>Draws the light through the label's glyphs (render thread).</summary>
public sealed class NthShinePainter : NthPainter
{
    private readonly SKPaint mask = new() { IsAntialias = false, BlendMode = SKBlendMode.DstIn };
    private SKShader? ramp;
    private object? owned;
    private IImmutableBrush? strong;
    private bool strongDark;

    public override bool Moving(NthFrame frame, double now) => frame.State is NthShineState { Shining: true } && !frame.Calm;

    public override Rect Bounds(NthFrame frame) => new Rect(frame.Size).Inflate(2);

    internal override void Render(ImmediateDrawingContext context, NthFrame frame, double now)
    {
        if (frame.State is not NthShineState s) return;
        Own(s.Runs);
        if (!s.Shining || s.Runs.Count == 0) return;
        if (NthMotion.Shine(now - frame.Started, s.Box.Width, frame.Calm, s.Period) is not { } offset) return;
        var centre = s.Box.X + offset;
        if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;
        if (strong is null || strongDark != frame.Dark) { strong = new ImmutableSolidColorBrush(NthPalette.ToColor(frame.Palette.Strong)); strongDark = frame.Dark; }
        ramp ??= SKShader.CreateLinearGradient(new SKPoint((float)-NthMotion.ShineHalf, 0), new SKPoint((float)NthMotion.ShineHalf, 0),
            [SKColors.Transparent, SKColors.White, SKColors.Transparent], [0, .5f, 1], SKShaderTileMode.Clamp);
        int saved;
        using (var lease = feature.Lease())
        {
            var canvas = lease.SkCanvas;
            saved = canvas.SaveCount;
            var window = new SKRect((float)Math.Max(s.Box.Left, centre - NthMotion.ShineHalf), (float)s.Box.Top - 2,
                (float)Math.Min(s.Box.Right, centre + NthMotion.ShineHalf), (float)s.Box.Bottom + 2);
            if (window.Width <= 0) return;
            canvas.SaveLayer(window, null);
        }
        try
        {
            // the label's own glyphs, in --strong, then kept only through the window's ramp
            foreach (var (run, place) in s.Runs)
            {
                using var placed = context.PushPreTransform(place);
                context.DrawGlyphRun(strong, run);
            }
            using var lease = feature.Lease();
            lease.SkCanvas.Translate((float)centre, 0);
            mask.Shader = ramp;
            lease.SkCanvas.DrawPaint(mask);
        }
        finally
        {
            using var lease = feature.Lease();
            lease.SkCanvas.RestoreToCount(saved);
        }
    }

    // The painter owns the glyph references it was last given: a new list releases the old one on the render thread.
    private void Own(IReadOnlyList<(IImmutableGlyphRunReference Run, Matrix Place)> runs)
    {
        if (ReferenceEquals(runs, owned)) return;
        Release();
        owned = runs;
    }

    private void Release()
    {
        if (owned is IReadOnlyList<(IImmutableGlyphRunReference Run, Matrix Place)> old)
            foreach (var (run, _) in old) run.Dispose();
        owned = null;
    }

    public override void Dispose()
    {
        Release(); mask.Dispose(); ramp?.Dispose();
        base.Dispose();
    }
}
