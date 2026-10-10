// PORT-ONLY: Ignis U4 (OD-0134, ED-NEW-ignis-views). The popover's two tracks (".ktrack"): 6 pt high, 3 pt corners, the track
// fill with a soft inner shade and a gloss on the fill's upper half. Quick memory: the fill is the share of the cache's life
// left, in the warming gradient at 45 % + 55 % of the heat, with a 14 pt ember head at its end that breathes while warm (the
// paced ambient clock). Chat size: the accent fill and the ember notch (2 pt, 4 pt above and below) at the tidy-up point.
using Avalonia;
using SkiaSharp;

namespace Marrow.App.Port.Ignis.Views;

internal sealed class KindleTrack : AmbientCanvas
{
    public enum Kind { QuickMemory, ChatSize }

    private readonly Kind kind;
    private readonly Marrow.UI.Port.AnimatedScalar fill, heat;
    private double? notch;
    private bool warm;

    public KindleTrack(Kind kind)
    {
        this.kind = kind;
        Height = 6;
        ClipToBounds = false;
        fill = new Marrow.UI.Port.AnimatedScalar(this, 0, _ => InvalidateVisual());
        heat = new Marrow.UI.Port.AnimatedScalar(this, 0, _ => InvalidateVisual());
    }

    public double Fill => fill.Value;
    public double Heat => heat.Value;
    public double? Notch => notch;

    protected override bool Moving => kind == Kind.QuickMemory && warm;

    public void Show(double toFill, double toHeat, bool isWarm, double? notchAt, bool animate)
    {
        var curve = animate ? KindleMotion.Ease(this, KindleMotion.Smooth(.5)) : null;
        fill.Set(Math.Clamp(toFill, 0, 1), curve);
        heat.Set(Math.Clamp(toHeat, 0, 1), animate ? KindleMotion.Ease(this, Marrow.UI.Port.Animation.EaseOut(.6)) : null);
        notch = notchAt;
        if (isWarm != warm) { warm = isWarm; MovingChanged(); }
        else InvalidateVisual();
    }

    private sealed record State(KindlePalette Palette, double Fill, double Heat, double? Notch, bool Warm);

    protected override object Snapshot() => new State(KindlePalette.For(this), fill.Value, heat.Value, notch, warm);

    protected override void Draw(SKCanvas canvas, Size size, double seconds, bool still, object snapshot)
    {
        var s = (State)snapshot;
        var w = (float)size.Width;
        var track = new SKRoundRect(new SKRect(0, 0, w, 6), 3, 3);
        using (var ground = new SKPaint { IsAntialias = true, Color = KindlePalette.Sk(s.Palette.Track) })
            canvas.DrawRoundRect(track, ground);
        var fw = (float)(w * s.Fill);
        if (kind == Kind.QuickMemory)
        {
            if (fw > .5f)
            {
                var (a, b) = s.Palette.CacheStops(s.Heat);
                var opacity = .45 + .55 * s.Heat;
                using var shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(Math.Max(fw, 1), 0), [KindlePalette.Sk(a, opacity), KindlePalette.Sk(b, opacity)], SKShaderTileMode.Clamp);
                using var paint = new SKPaint { IsAntialias = true, Shader = shader };
                canvas.DrawRoundRect(new SKRoundRect(new SKRect(0, 0, Math.Max(fw, 6), 6), 3, 3), paint);
                Gloss(canvas, Math.Max(fw, 6));
            }
            if (s.Heat > .01 && s.Fill > 0)
            {
                var breath = s.Warm && !still ? KindleMotion.Breath(seconds) : 0;
                var r = (float)(7 * (1 + .09 * breath));
                var center = new SKPoint(fw, 3);
                using var glow = new SKPaint { IsAntialias = true, Color = KindlePalette.Sk(s.Palette.EmberGlow, s.Heat), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, (float)(6 * s.Heat)) };
                canvas.DrawCircle(center, r * .6f, glow);
                using var shader = SKShader.CreateRadialGradient(center, r,
                    [KindlePalette.Sk(s.Palette.EmberCore, s.Heat), KindlePalette.Sk(s.Palette.EmberCore, s.Heat), KindlePalette.Sk(s.Palette.EmberOuter, s.Heat), KindlePalette.Sk(s.Palette.EmberOuter, 0)],
                    [0, .3f, .6f, .72f], SKShaderTileMode.Clamp);
                using var head = new SKPaint { IsAntialias = true, Shader = shader };
                canvas.DrawCircle(center, r, head);
            }
        }
        else
        {
            if (fw > .5f)
            {
                var accent = s.Palette.Accent;
                var start = KindlePalette.Mix(s.Palette.CoolA, accent, .55);
                using var glow = new SKPaint { IsAntialias = true, Color = KindlePalette.Sk(accent, .45), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3) };
                canvas.DrawRoundRect(new SKRoundRect(new SKRect(2, 1, Math.Max(fw, 6) + 2, 6), 3, 3), glow);
                using var shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(Math.Max(fw, 1), 0), [KindlePalette.Sk(start), KindlePalette.Sk(accent)], SKShaderTileMode.Clamp);
                using var paint = new SKPaint { IsAntialias = true, Shader = shader };
                canvas.DrawRoundRect(new SKRoundRect(new SKRect(0, 0, Math.Max(fw, 6), 6), 3, 3), paint);
                Gloss(canvas, Math.Max(fw, 6));
            }
            if (s.Notch is { } at)
            {
                var x = (float)(w * Math.Clamp(at, 0, 1));
                using var mark = new SKPaint { IsAntialias = true, Color = KindlePalette.Sk(s.Palette.EmberOuter) };
                canvas.DrawRoundRect(new SKRoundRect(new SKRect(x - 1, -4, x + 1, 10), 1, 1), mark);
            }
        }
    }

    private static void Gloss(SKCanvas canvas, float width)
    {
        using var shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, 3.3f), [new SKColor(255, 255, 255, 110), new SKColor(255, 255, 255, 0)], SKShaderTileMode.Clamp);
        using var gloss = new SKPaint { IsAntialias = true, Shader = shader };
        canvas.DrawRoundRect(new SKRoundRect(new SKRect(0, 0, width, 3.3f), 3, 1), gloss);
    }
}
