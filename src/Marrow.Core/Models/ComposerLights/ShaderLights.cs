// PORT-ONLY, owner-ruled addition (OD-0139 Composer Light v2; ED-NEW-lightv2-1, ED-NEW-lightv2-2): the shader lights. The
// specification is the approved page "Composer Light v2" (https://claude.ai/artifact/8afgSmTQEFL6hngZpQ6DrZ, source
// E:/Droppy/work/design/composer-light-v2/index.html): six single-pass lights (A Lumen, B Fluxa, C Lacus, D Caustica,
// E Borea, F Quies) that live between the composer's glass and the window's glass and spread outward onto the window, and
// (the owner, 2026-10-11: "lock and SHIP every light") v3's seven (H Nebula, I Serica, J Favilla, K Unda, L Argenta, M Vesper,
// N Neonis; E:/Droppy/work/design/composer-light-v3/index.html) and v4's eight (O Crystallum, P Radiantia, Q Nacrea, R Aurum,
// S Stellaris, T Velum, U Lamina, V Fulgor; E:/Droppy/work/design/composer-light-v4/index.html). Each
// one's controls and defaults are declared here, as data (the owner, 2026-10-11: every light's spread, intensity, speed and
// colours are the user's, and more lights follow from a list); the Customization page renders its rows from the declaration,
// and the renderer finds its shader by the light's id. The colour packs are the page's four (Spectrum, Ember, Ice, Accent),
// the owner's ten more ("Add more color options though"; their colours are v3's and v4's PACKS) and Custom (four colours of
// the user's own, per light).
using System.Collections.Immutable;
using System.Globalization;

namespace Marrow.Core.Models.ComposerLights;

/// <summary>One control a shader light offers in Settings › Customization.</summary>
public enum ShaderLightControl { Pack, Spread, Intensity, Speed }

/// <summary>A colour pack: four colours (sRGB hex), or none for the two that are worked out (Accent, Custom).</summary>
public sealed record ShaderLightPack(string Id, string Name, IReadOnlyList<string>? Colours);

/// <summary>Four colours of the user's own (Custom), #RRGGBB each.</summary>
public sealed record ShaderLightColours(string A, string B, string C, string D)
{
    public IReadOnlyList<string> All => [A, B, C, D];

    public string this[int i] => i switch { 0 => A, 1 => B, 2 => C, _ => D };

    public ShaderLightColours With(int i, string hex) => i switch { 0 => this with { A = hex }, 1 => this with { B = hex }, 2 => this with { C = hex }, _ => this with { D = hex } };

    /// <summary>Each colour as opaque upper-case #RRGGBB; an unreadable one is <paramref name="fallback"/>'s.</summary>
    public ShaderLightColours Clamped(ShaderLightColours fallback)
    {
        static string One(string? hex, string back) => UiverseAuraOptions.NormalizedHex(hex) is { } n ? UiverseAuraOptions.OpaqueHex(n) : back;
        return new(One(A, fallback.A), One(B, fallback.B), One(C, fallback.C), One(D, fallback.D));
    }
}

/// <summary>
/// One shader light as the page declares it: its id (the settings' and the shader's key), name, kind and description, its
/// default pack and spread, the loop phase its Reduce Motion still shows (<c>still</c>), how far its light reaches as a
/// multiple of the spread or <paramref name="ReachPx"/> px, the larger, plus <paramref name="ReachHalfWidth"/> of the composer's half width
/// (where its falloff ends: the band it shades), and the controls it offers.
/// </summary>
public sealed record ShaderLightDef(string Id, string Name, string Kind, string Description, string Pack, double Spread, double Still, double Reach,
    IReadOnlyList<ShaderLightControl> Controls, double ReachPx = 0, double ReachHalfWidth = 0)
{
    /// <summary>The page's values: its pack and spread, intensity 1, speed 1; Custom starts from the default pack's colours.</summary>
    public ShaderLightOptions Defaults => new(Pack, Spread, 1, 1, ShaderLightCatalog.CustomSeed(Pack));
}

/// <summary>One shader light's options: its pack, spread (px), intensity, speed and its Custom colours.</summary>
public sealed record ShaderLightOptions(string Pack, double Spread, double Intensity, double Speed, ShaderLightColours Custom)
{
    /// <summary>Every value inside its slider's range and on its step; an unknown pack is the light's default.</summary>
    public ShaderLightOptions Clamped(ShaderLightDef def) => new(
        ShaderLightCatalog.Pack(Pack) is not null ? Pack : def.Pack,
        ShaderLightCatalog.SpreadSlider.Snap(Spread),
        ShaderLightCatalog.IntensitySlider.Snap(Intensity),
        ShaderLightCatalog.SpeedSlider.Snap(Speed),
        (Custom ?? def.Defaults.Custom).Clamped(def.Defaults.Custom));
}

/// <summary>The catalogue of shader lights and colour packs (the page's VARIANTS and PACKS, and the owner's packs).</summary>
public static class ShaderLightCatalog
{
    /// <summary>Seconds of one seamless loop at speed 1 and rate 1 (the page's LOOP): every animated term is a whole harmonic of it.</summary>
    public const double LoopSeconds = 24;

    /// <summary>The warm white core every light shares (the page's CORE).</summary>
    public const string Core = "#FFF1E2";

    public static readonly LightSlider SpreadSlider = new(30, 220, 1);
    public static readonly LightSlider IntensitySlider = new(0.2, 2, 0.05);
    public static readonly LightSlider SpeedSlider = new(0.25, 3, 0.05);

    private static readonly ShaderLightControl[] Every = [ShaderLightControl.Pack, ShaderLightControl.Spread, ShaderLightControl.Intensity, ShaderLightControl.Speed];

    // Reach: where each light's outward falloff ends, as a multiple of the spread. haloP() is zero past 2.9 x of its scale, and
    // its blue reaches 1.18 x further (dispersion 1): 3.42 for a halo at the spread; Lacus's below-side halo is 1.35 x the spread
    // (4.62); Quies has no dispersion (2.9). Borea's and Lumen's exponential tails fade out over the band's last quarter.
    public static IReadOnlyList<ShaderLightDef> All { get; } =
    [
        new("lumen", "Lumen", "Interlayer bloom", "Colour leaks out between the glass as a wide halo", "spectrum", 130, .13, 3.42, Every),
        new("fluxa", "Fluxa", "Edge flow", "A ribbon of colour flows along the rim", "spectrum", 95, .31, 3.42, Every),
        new("lacus", "Lacus", "Light leak", "A lamp under frosted glass, pooling below", "ember", 115, .07, 4.62, Every),
        new("caustica", "Caustica", "Caustic glass", "A slow caustic net drifts around it", "ice", 135, .41, 3.42, Every),
        new("borea", "Borea", "Aurora rim", "Aurora curtains spill outward as rays", "spectrum", 110, .22, 3.42, Every),
        new("quies", "Quies", "Calm accent", "One calm colour and a slow highlight", "accent", 75, .18, 2.9, Every),
        // v3 (H to N; G was retired). Reach: haloP as above; Nebula's back cloud is cut at mB .006 (1.04 x) and its core glow's
        // exp(-d / 22) tail lasts 102 px; Serica's third ribbon is full to 1.1 x the half width, then 3.4 x the spread; Unda's
        // rings stop at 2 x the spread (2.1 x with their width); Vesper's sky tail (5 x) and its sun's exp(-r / 90) (430 px)
        new("nebula", "Nebula", "Nebula", "Silky colour clouds drift past each other in two layers", "aurora", 140, .37, 1.04, Every, ReachPx: 102),
        new("serica", "Serica", "Silk", "Three silk ribbons of light trail behind it in the wind", "rosegold", 120, .52, 3.4, Every, ReachHalfWidth: .1),
        new("favilla", "Favilla", "Ember drift", "Warm sparks lift off the top edge and fade as they rise", "ember", 110, .29, 3.42, Every),
        new("unda", "Unda", "Pulse rings", "Soft rings ripple outward like light on water", "ocean", 130, .44, 2.1, Every),
        new("argenta", "Argenta", "Liquid metal", "A chrome lip with highlights sliding round the rim", "silver", 90, .16, 3.42, Every),
        new("vesper", "Vesper", "Dusk horizon", "A dusk horizon glows behind it, a soft sun drifting by", "sunset", 130, .33, 5.0, Every, ReachPx: 430),
        new("neonis", "Neonis", "Neon tube", "A neon tube with a white-hot core and a deep glow", "cyber", 100, .12, 2.9, Every),
        // v4 (O to V). Reach: haloP with each light's dispersion (2.9 x (1 + .18 disp)); Radiantia's rays (exp(-d / .75 s) at
        // 1.06: 4.72 x); Velum's curtain (4.91 x above a hem up to 22 px over the top: 5.06 x); Stellaris's dust stops at 1.3 x
        // and its halo at 2.05 x; Fulgor's ghosts reach 230 px and its streak is held to the band (the page's canvas never
        // showed its 12.8 x tail either)
        new("crystallum", "Crystallum", "Sapphire edge", "A sapphire bevel with prismatic reflections and fire", "ice", 120, .27, 3.74, Every),
        new("radiantia", "Radiantia", "God rays", "A hidden sun behind the glass casts drifting rays", "sunset", 150, .4, 4.72, Every),
        new("nacrea", "Nacrea", "Thin-film pearl", "A thin pearl film shimmers in drifting colours", "lavender", 100, .58, 3.16, Every),
        new("aurum", "Aurum", "Molten pour", "Molten metal pours slowly along the edge", "gold", 100, .48, 3.42, Every),
        new("stellaris", "Stellaris", "Starlight dust", "Fine dust motes twinkle in the light around the edge", "silver", 110, .08, 2.05, Every),
        new("velum", "Velum", "Aurora curtain", "An aurora curtain waves up from the composer", "aurora", 150, .62, 5.06, Every),
        new("lamina", "Lamina", "Holographic foil", "Holographic foil swept by a slow rainbow sheen", "spectrum", 100, .71, 3.32, Every),
        new("fulgor", "Fulgor", "Lens flare halo", "A breathing halo with a travelling lens flare", "accent", 110, .15, 3.11, Every, ReachPx: 230),
    ];

    /// <summary>The light with this id, or null.</summary>
    public static ShaderLightDef? Find(string? id) => id is null ? null : All.FirstOrDefault(d => d.Id == id);

    /// <summary>The packs in the order the page shows them: the design page's four, the owner's ten, Custom last.</summary>
    public static IReadOnlyList<ShaderLightPack> Packs { get; } =
    [
        new("spectrum", "Spectrum", ["#FF8A3D", "#FF4F9A", "#9B5CFF", "#3E8BFF"]),
        new("ember", "Ember", ["#FF5E2E", "#FFA542", "#FFD9A0", "#E8405C"]),
        new("ice", "Ice", ["#62D2FF", "#C9F1FF", "#7E8CFF", "#3FB0E6"]),
        new("accent", "Accent", null),
        new("aurora", "Aurora", ["#3DF5A6", "#1FC8D0", "#7C6BFF", "#A6FF9E"]),
        new("sunset", "Sunset", ["#FFB238", "#FF6B5A", "#E83E8C", "#7A3C9E"]),
        new("ocean", "Ocean", ["#14B8A6", "#1E4FD8", "#3EE0F0", "#8EF0C8"]),
        new("rosegold", "Rose Gold", ["#E8838F", "#F7C1C4", "#F3DDB8", "#C8784E"]),
        new("lavender", "Lavender", ["#C6A4F5", "#8E9BFF", "#F5B8D9", "#9B6BE8"]),
        new("citrus", "Citrus", ["#A6E83A", "#FFE45C", "#FF9A2E", "#6EF0B0"]),
        new("cyber", "Cyber", ["#FF2BD6", "#22F0FF", "#8A3DFF", "#FF3D8B"]),
        new("silver", "Silver", ["#F4F6F8", "#E6E1D8", "#A9B1BC", "#CFE6F5"]),
        new("gold", "Gold", ["#F0B03C", "#FF9F1C", "#F5DC8E", "#B07A3A"]),
        new("forest", "Forest", ["#7A9A3A", "#22B573", "#A9C6A0", "#1A9C95"]),
        new("custom", "Custom", null),
    ];

    public const string AccentPack = "accent", CustomPack = "custom";

    /// <summary>The pack with this id, or null.</summary>
    public static ShaderLightPack? Pack(string? id) => id is null ? null : Packs.FirstOrDefault(p => p.Id == id);

    /// <summary>
    /// The four colours of the accent pack, worked out from the app's accent as the page's own Accent pack is from its accent
    /// (#489CF9 -> #6FB2FF, #A4CEFF, #3D8DF0): the accent, lifted 22 % and 50 % toward white, and 10 % deeper.
    /// </summary>
    public static IReadOnlyList<string> AccentColours(string accent)
    {
        var a = UiverseAuraOptions.NormalizedHex(accent) is { } n ? UiverseAuraOptions.OpaqueHex(n) : "#489CF9";
        return [a, BorderBeamMath.MixHex(a, "#ffffff", 0.22).ToUpperInvariant(), BorderBeamMath.MixHex(a, "#ffffff", 0.5).ToUpperInvariant(), BorderBeamMath.MixHex(a, "#000000", 0.1).ToUpperInvariant()];
    }

    /// <summary>The four colours a light wears: its pack's, the accent's, or its Custom four.</summary>
    public static IReadOnlyList<string> Colours(ShaderLightOptions options, string accent) => options.Pack switch
    {
        AccentPack => AccentColours(accent),
        CustomPack => options.Custom.All,
        _ => Pack(options.Pack)?.Colours ?? Packs[0].Colours!,
    };

    /// <summary>The page's own Accent pack (its accent #489CF9): what Custom starts from for a light whose pack is Accent.</summary>
    public static IReadOnlyList<string> PageAccent { get; } = ["#489CF9", "#6FB2FF", "#A4CEFF", "#3D8DF0"];

    /// <summary>Custom's first four colours: the light's default pack's, as the page seeds them (PACKS[def.pack].c).</summary>
    public static ShaderLightColours CustomSeed(string pack)
    {
        var c = pack == AccentPack ? PageAccent : Pack(pack)?.Colours ?? Packs[0].Colours!;
        return new(c[0], c[1], c[2], c[3]);
    }

    /// <summary>sRGB to linear as the page does it (<c>pow(x / 255, 2.2)</c>), r g b.</summary>
    public static (double R, double G, double B) Linear(string hex)
    {
        var n = int.Parse(hex.AsSpan(1, 6), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return (Math.Pow(((n >> 16) & 255) / 255.0, 2.2), Math.Pow(((n >> 8) & 255) / 255.0, 2.2), Math.Pow((n & 255) / 255.0, 2.2));
    }
}

/// <summary>
/// Every shader light's options, by id, compared by value (so a settings value with the same options is equal). A light with
/// no entry wears its defaults; Reset removes its entry.
/// </summary>
public sealed class ShaderLightOptionsSet : IEquatable<ShaderLightOptionsSet>
{
    private readonly ImmutableSortedDictionary<string, ShaderLightOptions> map;

    private ShaderLightOptionsSet(ImmutableSortedDictionary<string, ShaderLightOptions> map) => this.map = map;

    public static ShaderLightOptionsSet Empty { get; } = new(ImmutableSortedDictionary.Create<string, ShaderLightOptions>(StringComparer.Ordinal));

    /// <summary>The lights that have options of their own.</summary>
    public IEnumerable<KeyValuePair<string, ShaderLightOptions>> Entries => map;

    /// <summary>The options of light <paramref name="id"/>: its own, or its defaults.</summary>
    public ShaderLightOptions For(string id) =>
        map.TryGetValue(id, out var o) ? o : ShaderLightCatalog.Find(id)?.Defaults ?? ShaderLightCatalog.All[0].Defaults;

    public ShaderLightOptionsSet With(string id, ShaderLightOptions options) => new(map.SetItem(id, options));

    public ShaderLightOptionsSet Without(string id) => map.ContainsKey(id) ? new(map.Remove(id)) : this;

    /// <summary>Only known lights, each clamped; an entry equal to its defaults is dropped.</summary>
    public ShaderLightOptionsSet Clamped()
    {
        var b = ImmutableSortedDictionary.CreateBuilder<string, ShaderLightOptions>(StringComparer.Ordinal);
        foreach (var (id, o) in map)
        {
            if (ShaderLightCatalog.Find(id) is not { } def) continue;
            var c = o.Clamped(def);
            if (c != def.Defaults) b[id] = c;
        }
        return b.Count == map.Count && b.All(kv => map.TryGetValue(kv.Key, out var o) && o == kv.Value) ? this : new(b.ToImmutable());
    }

    public bool Equals(ShaderLightOptionsSet? other) =>
        other is not null && (ReferenceEquals(this, other) || map.Count == other.map.Count && map.All(kv => other.map.TryGetValue(kv.Key, out var o) && o == kv.Value));

    public override bool Equals(object? obj) => Equals(obj as ShaderLightOptionsSet);

    public override int GetHashCode()
    {
        var h = new HashCode();
        foreach (var (id, o) in map) { h.Add(id); h.Add(o); }
        return h.ToHashCode();
    }

    public static bool operator ==(ShaderLightOptionsSet? a, ShaderLightOptionsSet? b) => a is null ? b is null : a.Equals(b);
    public static bool operator !=(ShaderLightOptionsSet? a, ShaderLightOptionsSet? b) => !(a == b);
}
