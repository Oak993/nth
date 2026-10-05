using SkiaSharp;

namespace Marrow.UI.Port;

// PORT-ONLY: OD-0113. Exact drawing/timing from locked loading-aurora.html:765-837.
public static class AuroraLoaderMotion
{
    public const int CurtainColumns = 24;
    public static double Smooth(double u) { u = Math.Clamp(u, 0, 1); return u * u * (3 - 2 * u); }
    public static double Visibility(double seconds) => Smooth((seconds - .4) / .2);
    public static double Breath(double seconds) => .55 + .45 * (.5 - .5 * Math.Cos(2 * Math.PI * seconds / 2.4));
    public static double Phase(double seconds, double period) => ((seconds / period % 1) + 1) % 1;
    public static double SheenShift(double seconds) => -1.3 + 2.6 * Phase(seconds, 1.8);
    public static double ArcAngle(double seconds)
    {
        var ph = Phase(seconds, 1.5);
        return 2 * Math.PI * ph + .5 * Math.Sin(2 * Math.PI * ph) - Math.PI / 2;
    }

    public static AuroraCurtainRay CurtainRay(double size, double seconds, int column)
    {
        var u = column / 23.0;
        var ph = Phase(seconds, 2.4);
        var fold = .5 + .5 * Math.Sin(2 * Math.PI * (u * 1.1 - ph));
        var foot = size * .66 + size * .07 * Math.Sin(2 * Math.PI * (u * 1.4 + ph));
        var env = Math.Pow(Math.Sin(Math.PI * u), .7);
        return new(size * .08 + size * .84 * u, foot,
            size * (.12 + .36 * fold) * (.4 + .6 * env), (.35 + .65 * fold) * env);
    }

    public static SKColor Palette(bool dark, double phase)
    {
        var u = ((phase % 1) + 1) % 1 * 3;
        var i = (int)u;
        var a = Stop(dark, i % 3); var b = Stop(dark, (i + 1) % 3);
        var f = u - i;
        // The page's rgba truncates RGB with |0; opacity is rounded to three decimals.
        return new((byte)(a.Red + (b.Red - a.Red) * f), (byte)(a.Green + (b.Green - a.Green) * f), (byte)(a.Blue + (b.Blue - a.Blue) * f));
    }

    public static SKColor Stop(bool dark, int index) => (dark, index) switch
    {
        (true, 0) => new(60, 240, 170), (true, 1) => new(30, 190, 160), (true, 2) => new(170, 90, 255),
        (false, 0) => new(8, 158, 108), (false, 1) => new(10, 128, 150), (false, 2) => new(112, 72, 224),
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };
}
