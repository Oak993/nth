// PORT-ONLY, owner-ruled addition (OD-0089; ED-251): border-beam 1.4.1's tables (Copyright (c) 2026 Jakub Antalik, MIT),
// exactly as E:/Droppy/work/design/composer-lights/border-beam-tables.json holds them (read out of the published
// dist/index.es.js by beam-fix/ref/dump.mjs), embedded byte for byte. The package's names are kept: pe (size and theme
// presets), G (the Large type's blobs and Line's spike colours), Se (Small), Ve, Ue, Qe (Line), Fe, Je, eo, Ye, oo (the
// pulse types).
using System.Globalization;
using System.Text.Json;

namespace Marrow.Core.Models.ComposerLights;

/// <summary>
/// A CSS colour of the tables: rgb()/rgba() (channels 0..255, alpha 0..1), "transparent", "white" or #rrggbb. Bad is true for
/// the package's double-comma colours (<c>rgba(r, g, b,, a)</c>), which Chrome rejects (the "As 1.4.1 ships" defect).
/// </summary>
public readonly record struct CssColor(double R, double G, double B, double A, bool Bad = false)
{
    public static readonly CssColor Transparent = new(0, 0, 0, 0);
    public static readonly CssColor White = new(255, 255, 255, 1);

    public CssColor WithAlpha(double a) => this with { A = a };

    public static CssColor Parse(string text)
    {
        var s = text.Trim();
        if (s == "transparent") return Transparent;
        if (s == "white") return White;
        if (s.StartsWith('#') && s.Length == 7)
            return new CssColor(int.Parse(s.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture), int.Parse(s.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                int.Parse(s.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture), 1);
        var open = s.IndexOf('(', StringComparison.Ordinal); var close = s.LastIndexOf(')');
        if (open < 0 || close < open) throw new FormatException("Not a CSS colour: " + text);
        var bad = s.Contains(",,", StringComparison.Ordinal);
        var parts = s[(open + 1)..close].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        double P(int i) => double.Parse(parts[i], CultureInfo.InvariantCulture);
        return new CssColor(P(0), P(1), P(2), parts.Length > 3 ? P(3) : 1, bad);
    }
}

/// <summary>One blob of a Large or Small type: its colour, centre (percent of the box) and ellipse radii (px).</summary>
public sealed record BeamBlob(CssColor Color, double X, double Y, double W, double H);

/// <summary>One of Line's ellipses: colour, radii and offset from the travelling point (px on the site's search bar).</summary>
public sealed record LineBlob(CssColor Color, double SizeW, double SizeH, double OffsetX, double OffsetY);

/// <summary>A pulse blob's oscillator region (1..3) and the corner whose opacity it breathes with (tl, tr, bl, br).</summary>
public sealed record PulseSlot(int Region, string Quad);

/// <summary>A pulse type's extra blob (eo, Ye, oo): the colour index into G, its region and corner, radii, and an optional position (percent).</summary>
public sealed record PulseBlob(int ColorIndex, int Region, string Quad, double W, double H, double? X, double? Y);

/// <summary>A type's preset for one theme (pe): layer opacities, the inner shadow, saturation and (pulses) brightness.</summary>
public sealed record BeamPreset(double StrokeOpacity, double InnerOpacity, double BloomOpacity, CssColor InnerShadow, double Saturation, double? Brightness);

/// <summary>The package's tables.</summary>
public sealed class BorderBeamTables
{
    public required IReadOnlyDictionary<string, IReadOnlyDictionary<string, BeamPreset>> Presets { get; init; }   // pe
    public required IReadOnlyDictionary<string, IReadOnlyList<BeamBlob>> LargeBlobs { get; init; }                 // G[p].border
    public required IReadOnlyDictionary<string, (CssColor Primary, CssColor Secondary)> Spike { get; init; }        // G[p].spike
    public required IReadOnlyDictionary<string, (CssColor Primary, CssColor Secondary)> SpikeLight { get; init; }   // G[p].spikeLt
    public required IReadOnlyDictionary<string, IReadOnlyList<BeamBlob>> SmallBorder { get; init; }                // Se[p].border
    public required IReadOnlyDictionary<string, IReadOnlyList<BeamBlob>> SmallInner { get; init; }                 // Se[p].inner
    public required IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<LineBlob>>> LineStroke { get; init; } // Ve[p][theme]
    public required IReadOnlyDictionary<string, IReadOnlyList<LineBlob>> LineInner { get; init; }                  // Ue[p]
    public required IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<(CssColor C1, CssColor C2)>>> LineSpikes { get; init; } // Qe[p][theme]
    public required IReadOnlyList<PulseSlot> PulseSlots { get; init; }        // Fe
    public required IReadOnlyList<(double W, double H)> PulseInnerSizes { get; init; } // Je
    public required IReadOnlyList<PulseBlob> PulseInnerBloom { get; init; }   // eo
    public required IReadOnlyList<PulseBlob> PulseOutsideCore { get; init; }  // Ye
    public required IReadOnlyList<PulseBlob> PulseOutsideBloom { get; init; } // oo

    private static readonly Lazy<BorderBeamTables> shared = new(Load);

    /// <summary>The tables, read once from the embedded copy.</summary>
    public static BorderBeamTables Shared => shared.Value;

    private static BorderBeamTables Load()
    {
        using var stream = typeof(BorderBeamTables).Assembly.GetManifestResourceStream("Marrow.Core.BorderBeamTables.json")
            ?? throw new InvalidOperationException("The embedded border-beam tables are missing.");
        using var document = JsonDocument.Parse(stream);
        var t = document.RootElement;
        static CssColor C(JsonElement e) => CssColor.Parse(e.GetString()!);
        static (double, double) Pair(string text)
        {
            var p = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return (double.Parse(p[0].TrimEnd('%', 'p', 'x'), CultureInfo.InvariantCulture), double.Parse(p[1].TrimEnd('%', 'p', 'x'), CultureInfo.InvariantCulture));
        }
        static IReadOnlyList<BeamBlob> Blobs(JsonElement list) => list.EnumerateArray().Select(b =>
        {
            var (x, y) = Pair(b.GetProperty("pos").GetString()!);
            var (w, h) = Pair(b.GetProperty("size").GetString()!);
            return new BeamBlob(C(b.GetProperty("color")), x, y, w, h);
        }).ToArray();
        static IReadOnlyList<LineBlob> Line(JsonElement list) => list.EnumerateArray().Select(b => new LineBlob(C(b.GetProperty("color")),
            b.GetProperty("sizeW").GetDouble(), b.GetProperty("sizeH").GetDouble(), b.GetProperty("offsetX").GetDouble(), b.GetProperty("offsetY").GetDouble())).ToArray();
        static double? Percent(JsonElement o, string key) =>
            o.TryGetProperty(key, out var v) ? double.Parse(v.GetString()!.TrimEnd('%'), CultureInfo.InvariantCulture) : null;
        static IReadOnlyList<PulseBlob> Pulse(JsonElement list) => list.EnumerateArray().Select(b => new PulseBlob(b.GetProperty("ci").GetInt32(),
            b.GetProperty("region").GetInt32(), b.GetProperty("quad").GetString()!, b.GetProperty("w").GetDouble(), b.GetProperty("h").GetDouble(),
            Percent(b, "x"), Percent(b, "y"))).ToArray();

        var presets = new Dictionary<string, IReadOnlyDictionary<string, BeamPreset>>();
        foreach (var size in t.GetProperty("pe").EnumerateObject())
        {
            var themes = new Dictionary<string, BeamPreset>();
            foreach (var theme in size.Value.EnumerateObject())
            {
                var p = theme.Value;
                themes[theme.Name] = new BeamPreset(p.GetProperty("strokeOpacity").GetDouble(), p.GetProperty("innerOpacity").GetDouble(),
                    p.GetProperty("bloomOpacity").GetDouble(), C(p.GetProperty("innerShadow")), p.GetProperty("saturation").GetDouble(),
                    p.TryGetProperty("brightness", out var br) ? br.GetDouble() : null);
            }
            presets[size.Name] = themes;
        }
        var large = new Dictionary<string, IReadOnlyList<BeamBlob>>();
        var spike = new Dictionary<string, (CssColor, CssColor)>();
        var spikeLight = new Dictionary<string, (CssColor, CssColor)>();
        foreach (var p in t.GetProperty("G").EnumerateObject())
        {
            large[p.Name] = Blobs(p.Value.GetProperty("border"));
            var s = p.Value.GetProperty("spike"); spike[p.Name] = (C(s.GetProperty("primary")), C(s.GetProperty("secondary")));
            var l = p.Value.GetProperty("spikeLt"); spikeLight[p.Name] = (C(l.GetProperty("primary")), C(l.GetProperty("secondary")));
        }
        var smallBorder = new Dictionary<string, IReadOnlyList<BeamBlob>>();
        var smallInner = new Dictionary<string, IReadOnlyList<BeamBlob>>();
        foreach (var p in t.GetProperty("Se").EnumerateObject())
        {
            smallBorder[p.Name] = Blobs(p.Value.GetProperty("border"));
            smallInner[p.Name] = Blobs(p.Value.GetProperty("inner"));
        }
        var lineStroke = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<LineBlob>>>();
        foreach (var p in t.GetProperty("Ve").EnumerateObject())
            lineStroke[p.Name] = new Dictionary<string, IReadOnlyList<LineBlob>> { ["dark"] = Line(p.Value.GetProperty("dark")), ["light"] = Line(p.Value.GetProperty("light")) };
        var lineInner = new Dictionary<string, IReadOnlyList<LineBlob>>();
        foreach (var p in t.GetProperty("Ue").EnumerateObject()) lineInner[p.Name] = Line(p.Value);
        var lineSpikes = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<(CssColor, CssColor)>>>();
        foreach (var p in t.GetProperty("Qe").EnumerateObject())
        {
            IReadOnlyList<(CssColor, CssColor)> Spikes(string theme) => p.Value.GetProperty(theme).GetProperty("spikes").EnumerateArray()
                .Select(k => (C(k.GetProperty("color1")), C(k.GetProperty("color2")))).ToArray();
            lineSpikes[p.Name] = new Dictionary<string, IReadOnlyList<(CssColor, CssColor)>> { ["dark"] = Spikes("dark"), ["light"] = Spikes("light") };
        }
        return new BorderBeamTables
        {
            Presets = presets,
            LargeBlobs = large,
            Spike = spike,
            SpikeLight = spikeLight,
            SmallBorder = smallBorder,
            SmallInner = smallInner,
            LineStroke = lineStroke,
            LineInner = lineInner,
            LineSpikes = lineSpikes,
            PulseSlots = t.GetProperty("Fe").EnumerateArray().Select(f => new PulseSlot(f.GetProperty("region").GetInt32(), f.GetProperty("quad").GetString()!)).ToArray(),
            PulseInnerSizes = t.GetProperty("Je").EnumerateArray().Select(j => (j[0].GetDouble(), j[1].GetDouble())).ToArray(),
            PulseInnerBloom = Pulse(t.GetProperty("eo")),
            PulseOutsideCore = Pulse(t.GetProperty("Ye")),
            PulseOutsideBloom = Pulse(t.GetProperty("oo")),
        };
    }
}
