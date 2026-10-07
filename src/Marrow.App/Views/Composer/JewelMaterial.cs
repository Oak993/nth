using SkiaSharp;

namespace Marrow.App.Views.Composer;

// DEVIATION OD-0090 (owner ruling, 2026-10-03, "Slider done, ship it!"): the effort slider's knob is the Jewel orb. Its
// colours are the locked page's (E:\Droppy\work\design\jewel-knob-locked\): knob-variants.js, Marrow's own code (no
// ported code, NOTICES.md), computes each maker's Jewel material from the maker's liquid in liquid-palettes.json; this file
// is that computation, function for function:
//   hexLab / labLin / hexOf (OKLCH to sRGB, chroma lowered at constant lightness and hue until inside the gamut), lch,
//   wrap, mixHex (in OKLab) -> JewelColor;
//   liquidOf (the liquid's stops in OKLCH, its hue along the tube and its chroma, near-neutral and many-hued liquids) ->
//   JewelLiquid (its chromaAt is left out: only the tone and pearl recipes read it, knob-variants.js:128-136 and :194,
//   and the port builds neither);
//   the jewel() recipe (V2 Jewel: a darker, richer core, a vivid body and a bright inner fire; the folds calmed at knob
//   sizes), material() and mixMaterial() -> JewelMaterial;
//   strikeTint() / mixTint() (the lightning's tint: the locked board's three, every other maker's from its stops) ->
//   StrikeTint.Of / StrikeTint.Mix.
// The palettes do not differ between the dark and the light appearance (checked over all 30 makers, every stop), so one
// table serves both; the knob reads the maker's 'high' palette, as the page does (liquidOf's default effort).

/// <summary>A maker's liquid stops (deep, mid, light, highlight) at its high and low intensity (liquid-palettes.json).</summary>
public sealed record JewelPalette(string Maker, IReadOnlyList<string> High, IReadOnlyList<string> Low);

/// <summary>
/// The palettes of the app's brands, verbatim from liquid-palettes.json (sha256 a600baef...5c441f15), dark appearance
/// (identical to light). Purple (OpenRouter and no provider) has no maker in the palettes: its own locked liquid stops
/// stand in for both intensities, its light stop as the highlight (UNRESOLVED: OD-0050's automatic colour is pending).
/// </summary>
public static class JewelPalettes
{
    private static readonly Dictionary<EffortBrand, JewelPalette> Table = new()
    {
        [EffortBrand.Claude] = new("Claude", ["#A14827", "#B95D3A", "#D47E58", "#E7B47E"], ["#A4583D", "#B6684B", "#C88060", "#DCAF83"]),
        [EffortBrand.Deepseek] = new("DeepSeek", ["#1F30C6", "#324CE4", "#6279F1", "#8CADF7"], ["#2B46C0", "#3C5AD6", "#6378DC", "#88A4E9"]),
        [EffortBrand.Meta] = new("Meta", ["#0044A3", "#0062D1", "#238BF1", "#32BDF7"], ["#1752AB", "#1F69CC", "#3888DC", "#4DB4E9"]),
        [EffortBrand.Antigravity] = new("Gemini", ["#3070DB", "#8C65BA", "#CE5F69", "#E38896"], ["#4379D2", "#8E6EB5", "#C66970", "#DB9099"]),
        [EffortBrand.Silver] = new("OpenAI", ["#FFFFFF", "#FAFCFF", "#E7E9F1", "#F1F2F7"], ["#FFFFFF", "#F9FBFF", "#EDEEF5", "#F4F4F9"]),
        [EffortBrand.Cursor] = new("Cursor", ["#BA3500", "#DC4500", "#F2783A", "#F8AC53"], ["#C04723", "#D95529", "#E37C4B", "#EDAA67"]),
        [EffortBrand.Grok] = new("Grok", ["#000001", "#06060B", "#16161D", "#1F1F2A"], ["#020203", "#08080C", "#121218", "#191921"]),
        [EffortBrand.Opencode] = new("OpenCode", ["#00772D", "#0EA242", "#3DD064", "#57E48F"], ["#308242", "#3DA354", "#54C66C", "#73DB94"]),
        [EffortBrand.OpencodeGo] = new("OpenCodeGo", ["#006B8B", "#0093B9", "#34C0DF", "#4ADBED"], ["#297896", "#3197B8", "#4EB8D2", "#6BD2E3"]),
        [EffortBrand.Zai] = new("Zai", ["#01071E", "#07215B", "#245DEB", "#3485F4"], ["#071025", "#0F2556", "#2352C6", "#3070DB"]),
        [EffortBrand.Devin] = new("Devin", ["#058066", "#2CAB8C", "#67D1B5", "#93E5D5"], ["#3B8B74", "#4EAD92", "#73C8B1", "#9EDCCE"]),
        [EffortBrand.Copilot] = new("Copilot", ["#731BDA", "#BF4CA3", "#E69E6D", "#F1C89C"], ["#773ED1", "#BC5CA3", "#DC9F77", "#E9C2A3"]),
        [EffortBrand.Commandcode] = new("CommandCode", ["#170060", "#240087", "#5739CB", "#7D52E1"], ["#1A1061", "#24187C", "#4D39AF", "#694ECD"]),
        [EffortBrand.Pi] = new("Pi", ["#86001A", "#B10025", "#DD4C57", "#EC6D7D"], ["#8E1F28", "#AD2631", "#CB5359", "#DE727A"]),
        [EffortBrand.Purple] = new("Purple", ["#7A4BE0", "#8C57F7", "#B48CFF", "#B48CFF"], ["#7A4BE0", "#8C57F7", "#B48CFF", "#B48CFF"]),
    };

    public static JewelPalette For(EffortBrand brand) => Table[brand];
}

/// <summary>knob-variants.js's colour functions (OKLab / OKLCH, sRGB gamut fit), bit for bit in doubles.</summary>
public static class JewelColor
{
    private static double S2L(double v) => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    private static double L2S(double v) => v <= 0.0031308 ? v * 12.92 : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;

    /// <summary>"#RRGGBB" as a colour.</summary>
    public static SKColor Parse(string hex) =>
        new(Convert.ToByte(hex.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16));

    /// <summary>A colour as "#RRGGBB" (upper case, as linHex writes it).</summary>
    public static string Hex(SKColor c) => $"#{c.Red:X2}{c.Green:X2}{c.Blue:X2}";

    /// <summary>hexLab: sRGB to OKLab.</summary>
    public static (double L, double A, double B) Lab(SKColor c)
    {
        double r = S2L(c.Red / 255.0), g = S2L(c.Green / 255.0), b = S2L(c.Blue / 255.0);
        var l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        var m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        var s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
        return (0.2104542553 * l + 0.793617785 * m - 0.0040720468 * s, 1.9779984951 * l - 2.428592205 * m + 0.4505937099 * s,
            0.0259040371 * l + 0.7827717662 * m - 0.808675766 * s);
    }

    // labLin: OKLab to linear sRGB (out of gamut values kept)
    private static (double R, double G, double B) Linear(double L, double a, double b)
    {
        var l = Math.Pow(L + 0.3963377774 * a + 0.2158037573 * b, 3);
        var m = Math.Pow(L - 0.1055613458 * a - 0.0638541728 * b, 3);
        var s = Math.Pow(L - 0.0894841775 * a - 1.291485548 * b, 3);
        return (4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s, -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
            -0.0041960863 * l - 0.7034186147 * m + 1.707614701 * s);
    }

    private static bool InGamut((double R, double G, double B) c) =>
        c.R >= -1e-4 && c.R <= 1.0001 && c.G >= -1e-4 && c.G <= 1.0001 && c.B >= -1e-4 && c.B <= 1.0001;

    // linHex: linear sRGB to an 8-bit colour, each channel clamped, encoded and rounded (Math.round: halves up)
    private static SKColor Encode((double R, double G, double B) c)
    {
        static byte Channel(double v) => (byte)Math.Floor(Math.Clamp(L2S(Math.Clamp(v, 0, 1)), 0, 1) * 255 + 0.5);
        return new SKColor(Channel(c.R), Channel(c.G), Channel(c.B));
    }

    /// <summary>OKLCH to the nearest sRGB colour: chroma lowered at constant lightness and hue until it is inside sRGB.</summary>
    public static SKColor HexOf(double L, double C, double h)
    {
        L = Math.Clamp(L, 0, 1); C = Math.Max(0, C);
        double lo = 0, hi = C;
        var result = Linear(L, 0, 0);
        var full = Linear(L, C * Math.Cos(h), C * Math.Sin(h));
        if (InGamut(full)) return Encode(full);
        for (var i = 0; i < 16; i++)
        {
            var c = (lo + hi) / 2;
            var rgb = Linear(L, c * Math.Cos(h), c * Math.Sin(h));
            if (InGamut(rgb)) { lo = c; result = rgb; } else hi = c;
        }
        return Encode(result);
    }

    /// <summary>lch: a colour's OKLCH (lightness, chroma, hue in radians).</summary>
    public static (double L, double C, double H) Lch(SKColor c)
    {
        var (L, a, b) = Lab(c);
        return (L, Math.Sqrt(a * a + b * b), Math.Atan2(b, a));
    }

    /// <summary>An angle wrapped into (-pi, pi].</summary>
    public static double Wrap(double a) => Math.Atan2(Math.Sin(a), Math.Cos(a));

    /// <summary>mixHex: two colours mixed in OKLab.</summary>
    public static SKColor Mix(SKColor a, SKColor b, double u)
    {
        var (La, Aa, Ba) = Lab(a);
        var (Lb, Ab, Bb) = Lab(b);
        return Encode(Linear(La + (Lb - La) * u, Aa + (Ab - Aa) * u, Ba + (Bb - Ba) * u));
    }
}

/// <summary>liquidOf: a maker's liquid as the materials read it (its stops in OKLCH, its hue along the tube, its chroma).</summary>
public sealed class JewelLiquid
{
    // OD-0087/0063: the selected IR-3 liquid; the three-stop adapter repeats its light highlight.
    public static JewelLiquid FromStops(string maker,IReadOnlyList<string> stops) => new(maker,[stops[0],stops[1],stops[2],stops.Count>3?stops[3]:stops[2]]);
    private readonly (double L, double C, double H) deep, mid, light, highlight;

    private JewelLiquid(string maker, IReadOnlyList<string> stops)
    {
        Maker = maker;
        Deep = JewelColor.Parse(stops[0]); Mid = JewelColor.Parse(stops[1]); Light = JewelColor.Parse(stops[2]); Highlight = JewelColor.Parse(stops[3]);
        deep = JewelColor.Lch(Deep); mid = JewelColor.Lch(Mid); light = JewelColor.Lch(Light); highlight = JewelColor.Lch(Highlight);
        // a near-neutral liquid (OpenAI silver, Grok obsidian) has no hue of its own: its stops' faint cast is used
        var c = Math.Max(deep.C, Math.Max(mid.C, light.C));
        Achro = c < 0.035;
        hueNeutral = mid.C > 0.002 ? mid.H : 265 * Math.PI / 180;
        C = Math.Max(c, 0.012);
        var span = Achro ? 0 : Math.Abs(JewelColor.Wrap(mid.H - deep.H)) + Math.Abs(JewelColor.Wrap(light.H - mid.H));
        Multi = span > 35 * Math.PI / 180;
        var hm = (mid.H * 180 / Math.PI + 360) % 360;
        Blue = !Achro && hm > 235 && hm < 300;
    }

    private readonly double hueNeutral;

    /// <summary>The liquid of a brand at its high (or low) intensity.</summary>
    public static JewelLiquid Of(EffortBrand brand, bool high = true)
    {
        var palette = JewelPalettes.For(brand);
        return new JewelLiquid(palette.Maker, high ? palette.High : palette.Low);
    }

    public string Maker { get; }
    public SKColor Deep { get; }
    public SKColor Mid { get; }
    public SKColor Light { get; }
    public SKColor Highlight { get; }

    /// <summary>The liquid's chroma (its stops' highest, at least 0.012).</summary>
    public double C { get; }
    public bool Achro { get; }
    public bool Multi { get; }
    public bool Blue { get; }
    public double Ld => deep.L;
    public double Lm => mid.L;
    public double Ll => light.L;
    public double Lh => highlight.L;

    /// <summary>The hue at u (0 deep, 0.5 mid, 1 light), around the colour wheel the short way.</summary>
    public double HueAt(double u)
    {
        if (Achro) return hueNeutral;
        double a = u < 0.5 ? deep.H : mid.H, b = u < 0.5 ? mid.H : light.H, k = u < 0.5 ? u * 2 : (u - 0.5) * 2;
        return a + JewelColor.Wrap(b - a) * k;
    }
}

/// <summary>
/// One state of the orb's material (Thinking: charged; Idle: at rest): the twelve colours of Violet Ember's roles and the
/// numbers the recipe sets (the fold numbers only at knob sizes: null takes the preset's own).
/// </summary>
public sealed record JewelState(SKColor ColorA, SKColor ColorB, SKColor ColorC, SKColor ColorD, SKColor Highlight, SKColor Glow,
    SKColor ShellInner, SKColor ShellMid, SKColor ShellEdge, SKColor Sheen, SKColor Spec, SKColor Canvas,
    double Exposure, double Speed, double? Zoom, double? Warp, double? RidgeAmt, double? Sharp)
{
    /// <summary>The state between two (colours in OKLab, numbers linearly), as mixObj.</summary>
    public static JewelState Mix(JewelState a, JewelState b, double u)
    {
        static double? N(double? x, double? y, double u) => y is not { } to ? null : (x ?? to) + (to - (x ?? to)) * u;
        return new(JewelColor.Mix(a.ColorA, b.ColorA, u), JewelColor.Mix(a.ColorB, b.ColorB, u), JewelColor.Mix(a.ColorC, b.ColorC, u),
            JewelColor.Mix(a.ColorD, b.ColorD, u), JewelColor.Mix(a.Highlight, b.Highlight, u), JewelColor.Mix(a.Glow, b.Glow, u),
            JewelColor.Mix(a.ShellInner, b.ShellInner, u), JewelColor.Mix(a.ShellMid, b.ShellMid, u), JewelColor.Mix(a.ShellEdge, b.ShellEdge, u),
            JewelColor.Mix(a.Sheen, b.Sheen, u), JewelColor.Mix(a.Spec, b.Spec, u), JewelColor.Mix(a.Canvas, b.Canvas, u),
            a.Exposure + (b.Exposure - a.Exposure) * u, a.Speed + (b.Speed - a.Speed) * u,
            N(a.Zoom, b.Zoom, u), N(a.Warp, b.Warp, u), N(a.RidgeAmt, b.RidgeAmt, u), N(a.Sharp, b.Sharp, u));
    }
}

/// <summary>
/// material(jewel, liquid, px): the knob's Thinking and Idle colours and numbers, the flare's tint (surge) and the share of
/// the lightning's charge and flare the material takes (fx).
/// </summary>
public sealed record JewelMaterial(SKColor Liquid, JewelState Think, JewelState Idle, SKColor Surge, double FxCharge, double FxFlare)
{
    /// <summary>The material for a brand's liquid at the app's knob size (OD-0090: 48 px).</summary>
    public static JewelMaterial For(EffortBrand brand) => materials[(int)brand] ??= For(JewelLiquid.Of(brand), EffortSlider.ThumbSize);

    private static readonly JewelMaterial?[] materials = new JewelMaterial?[Enum.GetValues<EffortBrand>().Length];

    /// <summary>
    /// The jewel() recipe. Same hue, cut as a gem: a darker, more saturated core, a vivid body and a bright inner fire. On a
    /// liquid of several hues the boost is held to 1.2x and the hues pulled halfway to the mid stop; a blue liquid sits a
    /// little lighter and takes a smaller share of the strike; at knob sizes (up to 64 px) the folds are larger and softer.
    /// </summary>
    public static JewelMaterial For(JewelLiquid q, double px)
    {
        const double Deg = Math.PI / 180;
        var z = px <= 24 ? 's' : px <= 64 ? 'm' : 'l';
        double C = q.C, mh = q.Multi ? 0.5 : 1;
        double hM = q.HueAt(0.5), hD = hM + JewelColor.Wrap(q.HueAt(0) - hM) * mh, hL = hM + JewelColor.Wrap(q.HueAt(1) - hM) * mh;
        double kB = q.Multi ? 1.15 : 1.35, kC = q.Multi ? 1.2 : 1.5;
        var blue = q.Blue ? 1 : 0;
        (double L, double C, double H) A = (Math.Clamp(q.Ld * 0.3, 0.12, 0.18) + (z == 's' ? 0.11 : z == 'm' ? 0.06 : 0) + 0.05 * blue, Math.Min(C * 0.85, 0.1), hD);
        (double L, double C, double H) B = (Math.Clamp(q.Ld * 0.8, 0.27, 0.46) + 0.05 * blue, C * kB, hD);
        (double L, double C, double H) Cc = (Math.Clamp(q.Lm * 0.96, 0.46, 0.68), C * kC, hM);
        (double L, double C, double H) D = (z == 's' ? 0.74 : z == 'm' ? 0.78 : 0.8, C * 0.9, hL);
        (double L, double C, double H) Hi = (0.92, C * 0.38, hL);
        static SKColor Of((double L, double C, double H) v) => JewelColor.HexOf(v.L, v.C, v.H);
        static SKColor Dim((double L, double C, double H) v, double k, double kc) => JewelColor.HexOf(v.L * k, v.C * kc, v.H);
        // pattern frequency and contrast by size (Violet Ember: zoom sets the fold frequency, ridge and sharp the veins)
        (double Zoom, double Warp, double Ridge, double Sharp)? P = z == 's' ? (0.16, 3.4, 0.4, 1.9) : z == 'm' ? (0.36, 4.1, 0.58, 2.6) : null;
        var shellInner = JewelColor.HexOf(0.97, C * 0.18, hL);
        var shellMid = JewelColor.HexOf(Cc.L + 0.08, Cc.C, hM);
        var shellEdge = JewelColor.HexOf(B.L + 0.06, B.C, JewelColor.Wrap(hD - 14 * Deg));
        var sheen = JewelColor.HexOf(0.95, C * 0.25, hL);
        var spec = JewelColor.HexOf(0.86, C * 0.45, hM);
        var canvas = JewelColor.HexOf(0.06, Math.Min(C * 0.4, 0.04), hD);
        var think = new JewelState(Of(A), Of(B), Of(Cc), Of(D), Of(Hi), Of(Cc), shellInner, shellMid, shellEdge, sheen, spec, canvas,
            0.96, 4.2, P?.Zoom, P?.Warp, P?.Ridge, P?.Sharp);
        // the owner (2026-10-03): the gem must live at rest too; Jewel rests at 2.6 and the charge carries it to 4.2.
        // The preset's Idle profile (zoom 0.92, warp 0.46, ridge 0.42, sharp 0.78 of Thinking), on the calmed numbers.
        var idle = new JewelState(Of(A), Dim(B, 0.98, 1), Dim(Cc, 0.96, 0.97), Dim(D, 0.94, 0.95), Dim(Hi, 0.94, 0.9), Dim(Cc, 0.88, 0.9),
            shellInner, shellMid, shellEdge, sheen, spec, canvas, 0.98, 2.6,
            P?.Zoom * 0.92, P?.Warp * 0.46, P?.Ridge * 0.42, P?.Sharp * 0.78);
        return new JewelMaterial(q.Mid, think, idle, JewelColor.HexOf(0.8, C * 0.6, hM), blue == 1 ? 0.36 : 0.58, blue == 1 ? 0.15 : 0.24);
    }

    /// <summary>mixMaterial: the material between two, for the 0.6 s crossfade of a model change.</summary>
    public static JewelMaterial Mix(JewelMaterial a, JewelMaterial b, double u)
    {
        if (u >= 1) return b;
        if (u <= 0) return a;
        return new(JewelColor.Mix(a.Liquid, b.Liquid, u), JewelState.Mix(a.Think, b.Think, u), JewelState.Mix(a.Idle, b.Idle, u),
            JewelColor.Mix(a.Surge, b.Surge, u), a.FxCharge + (b.FxCharge - a.FxCharge) * u, a.FxFlare + (b.FxFlare - a.FxFlare) * u);
    }
}
