// PORT-ONLY: OD-0136, the locked nth icon B "Exponent": a bold "n" stroke with a small raised silver bead on the silver tile.
// Source of truth: work/design/nth-brand/nth-brand.html (assets-src/brand/nth-brand-icon.svg keeps its <defs>): the gradients
// gTile, gPool, gHi, gGlyph, gBead, the filter fG and the symbols tile, mkB and mkBs; the boot stage's .metal/.bs tile (its CSS
// gradients, rim and box-shadow); the hero/loop .mk::after sweep. This file draws only SkiaSharp, so the icon generator
// (scripts/brand/NthIcon) compiles the very same drawing the app shows. OD-0142: the boot has no sweep (the stage's .sw is
// gone); its motion turns the tile's own gradient, lifts its shadow and leads the n's trim with a round head.
using SkiaSharp;

namespace Marrow.UI.Port;

/// <summary>Which tile body: the SVG symbol <c>#tile</c> (app icon, logos, the empty chat) or the boot stage's CSS
/// <c>.bs.metal</c> tile (which fills its box: the stage svg's viewBox is <c>8 8 240 240</c>).</summary>
public enum NthTileStyle { Icon, Stage }

/// <summary>The n and its bead in symbol units: the stems' centre lines (<see cref="Left"/>, <see cref="Right"/>; the arc spans
/// them), the arc's centre height <see cref="Top"/> (where the stems end), the stems' foot <see cref="Bottom"/>, the stroke width,
/// the bead, and whether the group takes fG's drop shadow.</summary>
public readonly record struct NthGlyph(float Left, float Right, float Top, float Bottom, float Width, float BeadX, float BeadY, float BeadRadius, bool Shadow)
{
    /// <summary><c>mkB</c> (32 px and up): M70 196V124a44 44 0 0 1 88 0v72, width 26; bead 186,74 r14; fG.</summary>
    public static NthGlyph Regular => new(70, 158, 124, 196, 26, 186, 74, 14, true);
    /// <summary><c>mkBs</c>, the thicker cut for 16-24 px: M66 200V126a48 48 0 0 1 96 0v74, width 36; bead 194,68 r20; no filter.</summary>
    public static NthGlyph Small => new(66, 162, 126, 200, 36, 194, 68, 20, false);
}

/// <summary>One pose of the mark. <see cref="Draw"/> is the drawn share of the n stroke (stroke-dashoffset 100 to 0);
/// <see cref="Sweep"/> is the hero's soft sweep (the loop/hero <c>.mk::after</c>: white .42, clear at 40% and 60%) as its eased
/// progress (background-position 100% to 0), drawn only strictly between 0 and 1. <see cref="Light"/> turns the stage tile's
/// gradient by that many degrees (clockwise) about the tile's centre (OD-0142). <see cref="TileScale"/>, <see cref="TileOpacity"/>
/// and <see cref="Lift"/> (the shadow's lift: 0 close under the tile, 1 the design's box-shadow) are applied by whoever places
/// the tile (the boot painter); <see cref="NthMark.Draw"/> draws the tile at rest scale.</summary>
public readonly record struct NthPose(double Draw, double BeadOpacity, double BeadScale, double Sweep,
    double TileScale, double TileOpacity, double Lift, double Light)
{
    /// <summary>The finished mark: fully drawn, the bead at rest, no sweep, the tile at rest with its gradient at its own angle.</summary>
    public static NthPose Still => new(1, 1, 1, 0, 1, 1, 1, 0);
}

/// <summary>
/// Draws the nth tile in the design's 256-unit symbol space mapped onto a box. Own one per render thread; every shader and filter
/// is made once, in symbol units, and placed by the canvas matrix, so a frame allocates nothing managed.
/// </summary>
public sealed class NthMark : IDisposable
{
    // Brand metal (fixed in both themes): --g800, --g600, --s400, --s300, --s200, --s100, --white.
    private static readonly SKColor G800 = new(0x14, 0x16, 0x1c), G600 = new(0x3b, 0x41, 0x4f), S400 = new(0x9a, 0xa3, 0xb5),
        S300 = new(0xb9, 0xc0, 0xcd), S200 = new(0xd6, 0xda, 0xe2), S100 = new(0xe8, 0xeb, 0xf0), White = new(0xff, 0xff, 0xff);
    // --pool rgba(214,218,226,.55); its clear end keeps the pool's colour so the fade matches the browser's premultiplied interpolation.
    private static readonly SKColor Pool = new(214, 218, 226, 140), PoolClear = new(214, 218, 226, 0);
    private static readonly SKColor Hi55 = new(255, 255, 255, 140), Hi08 = new(255, 255, 255, 20), Clear = new(255, 255, 255, 0);
    private static readonly SKColor Rim = new(255, 255, 255, 97); // --rim rgba(255,255,255,.38)
    /// <summary>--gshadow rgba(6,8,12,.38): the glyph's drop shadow and the stage tile's box-shadow.</summary>
    public static readonly SKColor GShadow = new(6, 8, 12, 97);

    /// <summary>Symbol units: the box is 256, the tile is the 240-unit square at 8 with corner 52 (the stage's 21.7%: 52.08).</summary>
    public const float Box = 256, TileInset = 8, TileSide = 240, TileCorner = 52, StageCorner = .217f * TileSide;
    /// <summary>The boot stage's CSS px in symbol units: its 26% tile is 243.9 px on the page's 938 px stage (its 1000 px column).</summary>
    public const float StagePixel = TileSide / 243.9f;
    /// <summary>The stage .bs box-shadow <c>0 18px 40px -14px</c>, in symbol units.</summary>
    public const float ShadowOffset = 18 * StagePixel, ShadowSigma = 20 * StagePixel, ShadowSpread = -14 * StagePixel;
    /// <summary>OD-0142: the shadow at lift 0, as shares of the box-shadow's offset and blur (a tight shadow close under the tile).</summary>
    public const float ShadowRestingOffset = .2f, ShadowRestingBlur = .3f;

    private readonly SKPaint paint = new() { IsAntialias = true };
    private readonly SKPaint layer = new();
    private readonly Parts regular = new(NthGlyph.Regular), small = new(NthGlyph.Small);
    private readonly SKPath segment = new();
    private readonly SKRoundRect round = new();
    private readonly SKShader tileIcon, poolIcon, hiIcon, tileStage, poolStage, metalStage, hiStage, sweepSoft;
    private readonly SKImageFilter glyphShadow;
    private readonly SKMaskFilter stageShadow;
    private bool disposed;

    public NthMark()
    {
        // #tile: gTile (objectBoundingBox 0,0 to 1,1), gPool (cx .82 cy .95 r .6), gHi (.25,0 to .6,1).
        tileIcon = Linear(new(8, 8), new(248, 248), [G800, G600, S400], [0, .55f, 1]);
        poolIcon = SKShader.CreateRadialGradient(new SKPoint(8 + .82f * 240, 8 + .95f * 240), .6f * 240, [Pool, PoolClear], [0, 1], SKShaderTileMode.Clamp);
        hiIcon = Linear(new(8 + .25f * 240, 8), new(8 + .6f * 240, 248), [Hi55, Hi08, Clear], [0, .42f, .6f]);
        // .metal: linear-gradient(145deg, g800, g600 55%, s400) under radial-gradient(60% 60% at 82% 95%, pool, clear 70%);
        // ::before linear-gradient(160deg, hi55 0%, hi08 42%, clear 60%).
        tileStage = Css(145, [G800, G600, S400], [0, .55f, 1]);
        poolStage = SKShader.CreateRadialGradient(new SKPoint(8 + .82f * 240, 8 + .95f * 240), .6f * 240, [Pool, PoolClear], [0, .7f], SKShaderTileMode.Clamp);
        hiStage = Css(160, [Hi55, Hi08, Clear], [0, .42f, .6f]);
        // .metal's two background layers paint as one: the pool over the linear gradient, edged by one antialiased pass.
        metalStage = SKShader.CreateCompose(tileStage, poolStage, SKBlendMode.SrcOver);
        // The hero's sweep: linear-gradient(105deg, ...) on a 300% x 100% background, centred on the band's own origin.
        sweepSoft = Band([Clear, new SKColor(255, 255, 255, 107), Clear], [.4f, .5f, .6f]);
        // fG: feDropShadow dx 0, dy 3, stdDeviation 5, --gshadow.
        glyphShadow = SKImageFilter.CreateDropShadow(0, 3, 5, 5, GShadow);
        stageShadow = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, ShadowSigma);
        layer.ImageFilter = glyphShadow;
    }

    /// <summary>Draws the tile, the n and the bead (and the sweep) with the 256-unit symbol box mapped onto <paramref name="box"/>.
    /// For <see cref="NthTileStyle.Stage"/> the tile fills the box's inner 240 units, as the stage svg's viewBox does, and
    /// <see cref="NthPose.Light"/> turns its gradient (at 0 the drawing is exactly the resting tile's).</summary>
    public void Draw(SKCanvas canvas, SKRect box, NthTileStyle style, NthGlyph glyph, in NthPose pose)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(canvas);
        var saved = canvas.Save();
        try
        {
            canvas.Translate(box.Left, box.Top);
            canvas.Scale(box.Width / Box, box.Height / Box);
            paint.Style = SKPaintStyle.Fill; paint.Color = SKColors.White; paint.MaskFilter = null;
            var corner = style == NthTileStyle.Icon ? TileCorner : StageCorner;
            var tile = new SKRect(TileInset, TileInset, TileInset + TileSide, TileInset + TileSide);
            if (style == NthTileStyle.Icon) { Fill(canvas, tile, corner, tileIcon); Fill(canvas, tile, corner, poolIcon); } // three SVG rects
            else if (pose.Light == 0)
            {
                Fill(canvas, tile, corner, metalStage); // .metal (one element's backgrounds); its children paint inside .bs's overflow clip
                round.SetRect(tile, corner, corner);
                canvas.ClipRoundRect(round, SKClipOperation.Intersect, true);
            }
            else
            {
                // OD-0142: the moving light. The tile's whole material (gradient, pool, highlight) turns about the tile's centre
                // inside the fixed rounded tile: the same shaders under a rotation, dithered so no band steps show while it moves.
                round.SetRect(tile, corner, corner);
                canvas.ClipRoundRect(round, SKClipOperation.Intersect, true);
                var turned = canvas.Save();
                const float centre = TileInset + TileSide / 2, cover = TileSide * .75f; // the turned square still covers the tile
                canvas.RotateDegrees((float)pose.Light, centre, centre);
                var area = new SKRect(centre - cover, centre - cover, centre + cover, centre + cover);
                paint.IsDither = true;
                paint.Shader = metalStage; canvas.DrawRect(area, paint);
                paint.Shader = hiStage; canvas.DrawRect(area, paint);
                paint.Shader = null; paint.IsDither = false;
                canvas.RestoreToCount(turned);
            }
            if (style == NthTileStyle.Icon || pose.Light == 0) Fill(canvas, tile, corner, style == NthTileStyle.Icon ? hiIcon : hiStage);
            if (style == NthTileStyle.Icon) RimStroke(canvas, 2, TileCorner); // the symbol's rim lies under the mark
            if (glyph == regular.Glyph) Glyph(canvas, regular, pose);
            else if (glyph == small.Glyph) Glyph(canvas, small, pose);
            else { using var parts = new Parts(glyph); Glyph(canvas, parts, pose); } // a fitted cut (the icon generator's 16-24 px)
            if (style == NthTileStyle.Stage) RimStroke(canvas, StagePixel, StageCorner); // .bs::after inset 0 0 0 1px --rim, above the mark
            else SweepBand(canvas, corner, pose); // .mk::after sits over the whole symbol (the hero's soft sweep)
        }
        finally { canvas.RestoreToCount(saved); }
    }

    /// <summary>The stage tile's outer box-shadow (<c>0 18px 40px -14px --gshadow</c>) for a tile drawn with
    /// <see cref="NthTileStyle.Stage"/> on <paramref name="tile"/> (the 240-unit tile itself, not its box). OD-0142:
    /// <paramref name="lift"/> lifts the tile off the desktop: at 0 the shadow is tight and close (<see cref="ShadowRestingOffset"/>,
    /// <see cref="ShadowRestingBlur"/>), at 1 it is the design's, a little beyond 1 a little further and softer. The blur follows
    /// the canvas matrix, so one mask filter serves every lift: the shadow is drawn in a frame scaled by the blur's share.</summary>
    public void DrawStageShadow(SKCanvas canvas, SKRect tile, double alpha, double lift = 1)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!(alpha > .002)) return;
        var saved = canvas.Save();
        try
        {
            canvas.Translate(tile.Left - TileInset * tile.Width / TileSide, tile.Top - TileInset * tile.Height / TileSide);
            canvas.Scale(tile.Width / TileSide, tile.Height / TileSide);
            paint.Shader = null; paint.Style = SKPaintStyle.Fill; paint.MaskFilter = stageShadow;
            paint.Color = GShadow.WithAlpha((byte)Math.Round(GShadow.Alpha * Math.Clamp(alpha, 0, 1)));
            var l = Math.Max(0, lift);
            var offset = (float)(ShadowOffset * (ShadowRestingOffset + (1 - ShadowRestingOffset) * l));
            var inner = new SKRect(TileInset - ShadowSpread, TileInset - ShadowSpread + offset, TileInset + TileSide + ShadowSpread, TileInset + TileSide + ShadowSpread + offset);
            var r = Math.Max(0, StageCorner + ShadowSpread);
            var soft = (float)(ShadowRestingBlur + (1 - ShadowRestingBlur) * l);
            if (soft != 1)
            {
                // The same rounded rectangle in a frame scaled by `soft` about its centre: the blur scales, the shape does not.
                canvas.Scale(soft, soft, inner.MidX, inner.MidY);
                float cx = inner.MidX, cy = inner.MidY, hw = inner.Width / 2 / soft, hh = inner.Height / 2 / soft;
                inner = new SKRect(cx - hw, cy - hh, cx + hw, cy + hh);
                r /= soft;
            }
            round.SetRect(inner, r, r);
            canvas.DrawRoundRect(round, paint);
        }
        finally { paint.MaskFilter = null; canvas.RestoreToCount(saved); }
    }

    private void Fill(SKCanvas canvas, SKRect tile, float corner, SKShader shader)
    {
        paint.Shader = shader; round.SetRect(tile, corner, corner);
        canvas.DrawRoundRect(round, paint); paint.Shader = null;
    }

    private void RimStroke(SKCanvas canvas, float width, float corner)
    {
        paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = width; paint.Color = Rim;
        var inset = TileInset + width / 2; var r = corner - width / 2;
        round.SetRect(new SKRect(inset, inset, TileInset + TileSide - width / 2, TileInset + TileSide - width / 2), r, r);
        canvas.DrawRoundRect(round, paint);
        paint.Style = SKPaintStyle.Fill; paint.Color = SKColors.White;
    }

    private void Glyph(SKCanvas canvas, Parts parts, in NthPose pose)
    {
        var draw = Math.Clamp(pose.Draw, 0, 1);
        var beadAlpha = Math.Clamp(pose.BeadOpacity, 0, 1);
        if (draw <= 0 && beadAlpha <= 0) return;
        var g = parts.Glyph;
        if (g.Shadow) canvas.SaveLayer(layer); // fG over the whole group: the stroke and the bead share one shadow
        if (draw > 0)
        {
            paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = g.Width; paint.StrokeCap = SKStrokeCap.Butt;
            paint.Shader = parts.Stroke; paint.Color = SKColors.White;
            if (draw >= 1) canvas.DrawPath(parts.Path, paint);
            else
            {
                segment.Reset();
                var length = (float)(parts.Measure.Length * draw);
                if (parts.Measure.GetSegment(0, length, segment, true)) canvas.DrawPath(segment, paint);
                // OD-0142: a round head leads the trim. It is clipped at the feet's line, so on the right stem it lands flush on the
                // finished glyph's butt foot, and the draw's last frame is exactly the finished n.
                if (parts.Measure.GetPosition(length, out var head))
                {
                    var clipped = canvas.Save();
                    canvas.ClipRect(new SKRect(0, 0, Box, g.Bottom), SKClipOperation.Intersect, true);
                    paint.Style = SKPaintStyle.Fill;
                    canvas.DrawCircle(head, g.Width / 2, paint);
                    canvas.RestoreToCount(clipped);
                }
            }
            paint.Shader = null; paint.Style = SKPaintStyle.Fill;
        }
        if (beadAlpha > 0 && pose.BeadScale > 0)
        {
            var saved = canvas.Save();
            // scale(s) about the bead's own centre (transform-box: fill-box)
            canvas.Scale((float)pose.BeadScale, (float)pose.BeadScale, g.BeadX, g.BeadY);
            paint.Shader = parts.Bead;
            paint.Color = SKColors.White.WithAlpha((byte)Math.Round(255 * beadAlpha));
            canvas.DrawCircle(g.BeadX, g.BeadY, g.BeadRadius, paint);
            paint.Shader = null; paint.Color = SKColors.White;
            canvas.RestoreToCount(saved);
        }
        if (g.Shadow) canvas.Restore();
    }

    private void SweepBand(SKCanvas canvas, float corner, in NthPose pose)
    {
        var w = pose.Sweep;
        if (!(w > 0 && w < 1)) return; // before and after its pass the band lies wholly outside the rounded tile
        var saved = canvas.Save();
        var tile = new SKRect(TileInset, TileInset, TileInset + TileSide, TileInset + TileSide);
        round.SetRect(tile, corner, corner);
        canvas.ClipRoundRect(round, SKClipOperation.Intersect, true);
        // background-position p = 1 - w on a 3W-wide image: the band's centre runs from -W/2 to 3W/2 across the tile.
        var x = TileInset + (float)(TileSide * (2 * w - .5));
        canvas.Translate(x, TileInset + TileSide / 2);
        paint.Shader = sweepSoft; paint.Color = SKColors.White;
        canvas.DrawRect(new SKRect(tile.Left - x, -TileSide / 2, tile.Right - x, TileSide / 2), paint);
        paint.Shader = null;
        canvas.RestoreToCount(saved);
    }

    // One cut's path (M left bottom V top a r r 0 0 1 2r 0 V bottom), its measure, gGlyph over the path's own bounding box (from
    // the arc's top to the foot) and gBead over the bead's box (cx .36, cy .3, r .8).
    private sealed class Parts : IDisposable
    {
        public readonly NthGlyph Glyph;
        public readonly SKPath Path = new();
        public readonly SKPathMeasure Measure;
        public readonly SKShader Stroke, Bead;
        public Parts(NthGlyph g)
        {
            Glyph = g;
            var r = (g.Right - g.Left) / 2;
            Path.MoveTo(g.Left, g.Bottom); Path.LineTo(g.Left, g.Top);
            Path.ArcTo(new SKPoint(r, r), 0, SKPathArcSize.Small, SKPathDirection.Clockwise, new SKPoint(g.Right, g.Top));
            Path.LineTo(g.Right, g.Bottom);
            Measure = new SKPathMeasure(Path);
            Stroke = Linear(new(0, g.Top - r), new(0, g.Bottom), [White, S100, S300], [0, .55f, 1]);
            var d = 2 * g.BeadRadius;
            Bead = SKShader.CreateRadialGradient(new SKPoint(g.BeadX - g.BeadRadius + .36f * d, g.BeadY - g.BeadRadius + .3f * d), .8f * d,
                [White, S200, S400], [0, .5f, 1], SKShaderTileMode.Clamp);
        }
        public void Dispose() { Stroke.Dispose(); Bead.Dispose(); Measure.Dispose(); Path.Dispose(); }
    }

    private static SKShader Linear(SKPoint from, SKPoint to, SKColor[] colors, float[] stops) =>
        SKShader.CreateLinearGradient(from, to, colors, stops, SKShaderTileMode.Clamp);

    // A CSS linear-gradient(angle) over the 240-unit tile: its line passes through the centre, as long as the box's projection.
    private static SKShader Css(double degrees, SKColor[] colors, float[] stops)
    {
        var a = degrees * Math.PI / 180;
        var (dx, dy) = (Math.Sin(a), -Math.Cos(a));
        var half = (TileSide * Math.Abs(dx) + TileSide * Math.Abs(dy)) / 2;
        var c = TileInset + TileSide / 2;
        return Linear(new((float)(c - dx * half), (float)(c - dy * half)), new((float)(c + dx * half), (float)(c + dy * half)), colors, stops);
    }

    // linear-gradient(105deg) on a 3W x W background, centred on the origin (the caller places the band).
    private static SKShader Band(SKColor[] colors, float[] stops)
    {
        var a = 105 * Math.PI / 180;
        var (dx, dy) = (Math.Sin(a), -Math.Cos(a));
        var half = (3 * TileSide * Math.Abs(dx) + TileSide * Math.Abs(dy)) / 2;
        return Linear(new((float)(-dx * half), (float)(-dy * half)), new((float)(dx * half), (float)(dy * half)), colors, stops);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var shader in new[] { metalStage, tileIcon, poolIcon, hiIcon, tileStage, poolStage, hiStage, sweepSoft }) shader.Dispose();
        layer.ImageFilter = null; glyphShadow.Dispose(); stageShadow.Dispose();
        regular.Dispose(); small.Dispose(); segment.Dispose();
        round.Dispose(); paint.Dispose(); layer.Dispose();
    }
}
