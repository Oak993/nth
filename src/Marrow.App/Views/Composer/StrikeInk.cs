using SkiaSharp;

namespace Marrow.App.Views.Composer;

// P:791-841, SPEC K-107/K-111/K-113..K-126. Kept value colours: drifting stops and fades allocate nothing.
public readonly record struct StrikeInk(double Contrast, StrikeTint Tint, SKColor Deep, SKColor InkColor, StrikeInkPalette Palette)
{
    public double Amount => Tint.Ink;
    public double HalfWidth => 0.6 + 0.6 * Amount;

    public static StrikeInk Of(JewelLiquid q)
    {
        var lum = 0.2126 * LinearChannel(q.Light.Red) + 0.7152 * LinearChannel(q.Light.Green) + 0.0722 * LinearChannel(q.Light.Blue);
        var contrast = 1.05 / (lum + 0.05);
        var tint = StrikeTint.Of(q);
        var (_, a, b) = JewelColor.Lab(q.Deep); var cd = Math.Sqrt(a * a + b * b); var neutral = q.Achro || cd < 0.04;
        var ci = neutral ? 0.13 : Math.Min(0.11, cd * 1.1); var hi = neutral ? 262 * Math.PI / 180 : Math.Atan2(b, a); var li = neutral ? 0.36 : 0.3;
        var ink = Encode(Linear(li, ci * Math.Cos(hi), ci * Math.Sin(hi)));
        var h = neutral ? 255 : (Math.Atan2(b, a) * 180 / Math.PI + 360) % 360; var c = neutral ? 0.02 : cd;
        var palette = new StrikeInkPalette(Okc(0.15, Math.Min(0.025, c * 0.35) + 0.006, h), Okc(0.22, Math.Min(0.05, c * 0.6) + 0.01, h),
            Okc(0.26, Math.Min(0.07, c * 0.85) + 0.012, h + 14), Okc(0.3, 0.012, h), Okc(0.32, Math.Min(0.09, c) + 0.015, h - 10));
        var deep = q.Maker switch
        {
            "Claude" => new SKColor(184, 90, 56),
            "DeepSeek" => new SKColor(42, 69, 224),
            "Gemini" => new SKColor(120, 90, 200),
            _ => JewelColor.HexOf(Math.Clamp(q.Ld, 0.32, 0.6), Math.Max(q.C, 0.02), q.HueAt(0))
        };
        return new(contrast, tint, deep, ink, palette);
    }

    // P:824-830: 22 bisections (JewelColor.HexOf deliberately retains the older 16-step recipe).
    public static SKColor Okc(double l, double c, double hd)
    {
        var h = hd * Math.PI / 180; var cos = Math.Cos(h); var sin = Math.Sin(h);
        var chroma = c;
        if (!InGamut(Linear(l, c * cos, c * sin)))
        {
            double lo = 0, hi = c;
            for (var i = 0; i < 22; i++) { var m = (lo + hi) / 2; if (InGamut(Linear(l, m * cos, m * sin))) lo = m; else hi = m; }
            chroma = lo;
        }
        return Encode(Linear(l, chroma * cos, chroma * sin));
    }

    public SKColor Gradient(bool halo, double time, double phase, int i)
    {
        var u = 0.5 + 0.5 * Math.Sin(time * 0.35 + (halo ? phase : phase + 2.1) + i * 1.7);
        return MixColor(halo ? Palette.HaloA : Palette.MidA, halo ? Palette.HaloB : Palette.MidB, u);
    }

    public static StrikeInk Mix(StrikeInk a, StrikeInk b, double u) => new(a.Contrast + (b.Contrast - a.Contrast) * u, StrikeTint.Mix(a.Tint, b.Tint, u),
        MixColor(a.Deep, b.Deep, u), MixColor(a.InkColor, b.InkColor, u), new(MixColor(a.Palette.Core, b.Palette.Core, u), MixColor(a.Palette.MidA, b.Palette.MidA, u),
            MixColor(a.Palette.MidB, b.Palette.MidB, u), MixColor(a.Palette.HaloA, b.Palette.HaloA, u), MixColor(a.Palette.HaloB, b.Palette.HaloB, u)));

    private static SKColor MixColor(SKColor a, SKColor b, double u)
    {
        static byte Channel(byte a, byte b, double u) => (byte)Math.Floor(a + (b - a) * u + 0.5);
        return new(Channel(a.Red, b.Red, u), Channel(a.Green, b.Green, u), Channel(a.Blue, b.Blue, u));
    }

    private static double LinearChannel(byte value)
    {
        var v = value / 255d; return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }

    // Same locked OKLab inverse as JewelColor's private helper, without modifying Jewel's owned file/API.
    private static (double R, double G, double B) Linear(double l, double a, double b)
    {
        var x = Math.Pow(l + 0.3963377774 * a + 0.2158037573 * b, 3); var y = Math.Pow(l - 0.1055613458 * a - 0.0638541728 * b, 3); var z = Math.Pow(l - 0.0894841775 * a - 1.291485548 * b, 3);
        return (4.0767416621 * x - 3.3077115913 * y + 0.2309699292 * z, -1.2684380046 * x + 2.6097574011 * y - 0.3413193965 * z, -0.0041960863 * x - 0.7034186147 * y + 1.707614701 * z);
    }
    private static bool InGamut((double R, double G, double B) c) => c.R >= -1e-4 && c.R <= 1.0001 && c.G >= -1e-4 && c.G <= 1.0001 && c.B >= -1e-4 && c.B <= 1.0001;
    private static SKColor Encode((double R, double G, double B) c)
    {
        static byte Channel(double value)
        {
            var v = Math.Clamp(value, 0, 1); var encoded = v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;
            return (byte)Math.Floor(255 * encoded + 0.5);
        }
        return new(Channel(c.R), Channel(c.G), Channel(c.B));
    }
}
