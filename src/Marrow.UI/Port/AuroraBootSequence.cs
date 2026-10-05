using Avalonia;

namespace Marrow.UI.Port;

// PORT-ONLY: OD-0113 Boot B. Locked loading-aurora.html:576-665 is the authority.
// Geometry is independent of the blocked Aurora C# painter; no substitute icon is drawn here.
public sealed class AuroraBootSequence(Rect window, bool reduceMotion)
{
    public const double MarkSize = 440;
    public static AuroraBootSequence FromPixels(PixelRect visible, PixelPoint stageOrigin, double scaling, bool reduceMotion)
    {
        if (!(scaling > 0) || !double.IsFinite(scaling)) throw new ArgumentOutOfRangeException(nameof(scaling));
        return new(new Rect((visible.X - stageOrigin.X) / scaling, (visible.Y - stageOrigin.Y) / scaling,
            visible.Width / scaling, visible.Height / scaling), reduceMotion);
    }

    public static double HandOver(double ready, bool reduceMotion) => Math.Max(ready, reduceMotion ? 1.2 : 1.8);

    public AuroraBootFrame At(double seconds, double? ready)
    {
        var hold = ready is { } r ? HandOver(r, reduceMotion) : double.PositiveInfinity;
        var closed = seconds >= hold + (reduceMotion ? .7 : 2.2);
        if (reduceMotion)
        {
            var side = .88 * MarkSize;
            var tile = new Rect(window.Center.X - side / 2, window.Center.Y - side / 2, side, side);
            var alpha = AuroraLoaderMotion.Smooth(seconds / .3) * (1 - AuroraLoaderMotion.Smooth((seconds - hold) / .3));
            return new(tile, .225 * side, new Rect(window.Size), new Rect(window.X, window.Y, window.Width, 38),
                0, 1, 0, alpha, AuroraLoaderMotion.Smooth((seconds - hold - .1) / .4), 0,
                AuroraLoaderMotion.Smooth(seconds / .3) * (1 - AuroraLoaderMotion.Smooth((seconds - hold - .3) / .4)),
                seconds >= hold + .1, closed);
        }

        var u = Math.Clamp((seconds - hold), 0, 1);
        var p = Progress(u);
        var scale = .93 + .07 * (1 - Math.Pow(1 - Math.Clamp(seconds / .9, 0, 1), 3));
        var initial = .88 * MarkSize * scale;
        var w = initial + (window.Width - initial) * p;
        var h = initial + (window.Height - initial) * p;
        var rect = p >= 1 ? window : new Rect(window.Center.X - w / 2, window.Center.Y - h / 2, w, h);
        var corner = .225 * initial + (8 - .225 * initial) * p;
        var glow = AuroraLoaderMotion.Smooth((p - .5) / .5);
        var fade = 1 - AuroraLoaderMotion.Smooth((seconds - hold - 1) / 1.2);
        return new(rect, corner, rect.Translate(-window.Position),
            new Rect(rect.X, rect.Y, rect.Width, 38 * AuroraLoaderMotion.Smooth((p - .45) / .55)),
            p, Math.Clamp((seconds - .12) / 1.2, 0, 1), Math.Max(0, seconds - 1.32), 1 - glow,
            AuroraLoaderMotion.Smooth((p - .35) / .65), .9 * glow * fade,
            AuroraLoaderMotion.Smooth(seconds / .26) * fade, seconds >= hold, closed);
    }

    // IconKit.bez in the locked page: preserve its eight Newton steps and 1e-5 x tolerance.
    private static double Progress(double x)
    {
        if (x <= 0) return 0; if (x >= 1) return 1;
        var t = x;
        for (var i = 0; i < 8; i++)
        {
            var cx = 3 * .32 * t * (1 - t) * (1 - t) + t * t * t - x;
            var dx = 3 * .32 * (1 - t) * (1 - t) + 6 * (-.32) * t * (1 - t) + 3 * t * t;
            if (Math.Abs(cx) < 1e-5) break;
            t = Math.Clamp(t - cx / (dx == 0 ? 1e-6 : dx), 0, 1);
        }
        return 3 * .72 * t * (1 - t) * (1 - t) + 3 * t * t * (1 - t) + t * t * t;
    }
}
