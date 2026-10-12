// PORT-ONLY, owner-ruled addition (OD-0083, OD-0085, OD-0089; ED-251): the composer light's settings. The specification is
// the approved playground, E:/Droppy/work/design/composer-lights/playground/src/logic.js (AURA_DEFAULTS, AURA_ORIGINAL,
// BEAM_DEFAULTS, BEAM_CALM, the slider rows of auraRows() and beamRows(), and slider()'s rounding), not the pinned Swift
// source, which has no composer light. Border beam is border-beam 1.4.1 (MIT, Jakub Antalik); the Uiverse light is
// SelfMadeSystem's Uiverse.io button (MIT). Both are credited in THIRD_PARTY_NOTICES.md.
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Marrow.Core.Models.ComposerLights;

/// <summary>Which light the composer wears (OD-0085): Border beam (the default, OD-0089), the Uiverse light, none, or a shader light (OD-0150: the one <see cref="ComposerLightSettings.ShaderId"/> names).</summary>
public enum ComposerLightStyle { Beam, Aura, Off, Shader }

/// <summary>
/// OD-0150 (ED-NEW-lightv2-5): how the chat text above the composer meets a shader light (the v4 page's "Text and light", TL.mode):
/// the text on top of the light with a soft dark halo, the light washing over the text, or the text inverting against it.
/// </summary>
public enum ComposerLightTextMode { TextAbove, LightAbove, Difference }

/// <summary>OD-0150 (ED-NEW-lightv2-6): the wallpaper's dimming near the composer while a light is on: worked out from the wallpaper, or none.</summary>
public enum WallpaperDimMode { Auto, Off }

/// <summary>What the composer is doing, as the playground's preview states (logic.js STATES).</summary>
public enum ComposerLightPhase { Rest, Typing, Running }

/// <summary>Border beam's family (the playground's Family row).</summary>
public enum BeamFamily { Rotate, Pulse }

/// <summary>Border beam's type, the package's <c>size</c> prop: md (Large), sm (Small), line, pulse-inner, pulse-outside.</summary>
public enum BeamType { Large, Small, Line, PulseInner, PulseOutside }

/// <summary>Border beam's <c>colorVariant</c> (logic.js PAL_NAMES).</summary>
public enum BeamPalette { Colorful, Mono, Ocean, Sunset, Forest, Candy, Ice, Gold }

/// <summary>Border beam's <c>theme</c> prop; Auto follows the app's appearance.</summary>
public enum BeamTheme { Dark, Light, Auto }

/// <summary>The Uiverse light's colour scheme (the Colours row): the reference's pair, the chip's maker, the accent, or the user's pair.</summary>
public enum AuraScheme { Original, Maker, Accent, Custom }

/// <summary>One slider of the playground: its range and step (slider(group, key, t, sub, min, max, step, ...)).</summary>
public readonly record struct LightSlider(double Min, double Max, double Step)
{
    /// <summary>slider()'s set: <c>Math.round(clamp(v, min, max) / step) * step</c> (JavaScript rounds a half up), then to 4 places as the page's slider does (cz.js:456 <c>+(...).toFixed(4)</c>), so 0.01 * 1395 is 13.95 and not 13.950000000000001 (CZ-13.2).</summary>
    public double Snap(double value)
    {
        if (double.IsNaN(value)) value = Min;
        var clamped = Math.Clamp(value, Min, Max);
        return Math.Round(Math.Floor(clamped / Step + 0.5) * Step, 4, MidpointRounding.AwayFromZero);
    }
}

/// <summary>
/// Border beam's options, every one the playground offers (BEAM_DEFAULTS), with its defaults: the package's own (theme on
/// Auto, the playground's opening value) and the playground's geometry switch Fitted (true, "Fitted to the composer").
/// A null value is the slider's Auto. The playground's "Extra CSS" text row is not carried: the port draws no CSS.
/// </summary>
public sealed record BorderBeamOptions
{
    public BeamFamily Family { get; init; } = BeamFamily.Rotate;
    public BeamType Type { get; init; } = BeamType.Large;
    public BeamPalette Palette { get; init; } = BeamPalette.Colorful;
    public BeamTheme Theme { get; init; } = BeamTheme.Auto;
    public bool Active { get; init; } = true;
    public double? Duration { get; init; }
    public double? Radius { get; init; }
    public double? Brightness { get; init; }
    public double HueShift { get; init; }
    public double Strength { get; init; } = 1;
    public double? Saturation { get; init; }
    public double GlowSize { get; init; } = 1;
    public double HueRange { get; init; } = 30;
    public bool StaticColors { get; init; }
    public double StrokeX { get; init; } = 1;
    public double InnerX { get; init; } = 1;
    public double BloomX { get; init; } = 1;
    public double? CoreBlur { get; init; }
    public double? BloomBlur { get; init; }
    public double? GlowX { get; init; }
    public double? GlowY { get; init; }
    public double Boost { get; init; } = 1;
    public double Spike { get; init; } = 1;
    public bool Faithful { get; init; }
    public bool Respond { get; init; }
    public bool Fitted { get; init; } = true;

    // beamRows(): each slider's range and step
    // CZ-13.2: the Customization page's Speed steps 0.05 (cz.js:530); 0.05 is not a multiple of 0.02, so 0.55 would have moved to 0.56. Step 0.01 keeps every 0.02 and every 0.05 value where it is.
    public static readonly LightSlider DurationSlider = new(0.5, 14, 0.01);
    public static readonly LightSlider RadiusSlider = new(0, 32, 1);
    public static readonly LightSlider BrightnessSlider = new(0.2, 3, 0.05);
    public static readonly LightSlider HueShiftSlider = new(-180, 180, 1);
    public static readonly LightSlider StrengthSlider = new(0, 1, 0.01);
    public static readonly LightSlider SaturationSlider = new(0, 2.5, 0.05);
    public static readonly LightSlider GlowSizeSlider = new(0.25, 3, 0.05);
    public static readonly LightSlider HueRangeSlider = new(0, 180, 1);
    public static readonly LightSlider LayerSlider = new(0, 2, 0.05);
    public static readonly LightSlider CoreBlurSlider = new(0, 20, 0.5);
    public static readonly LightSlider BloomBlurSlider = new(0, 60, 0.5);
    public static readonly LightSlider GlowAxisSlider = new(0.35, 4, 0.01);
    public static readonly LightSlider BoostSlider = new(0.5, 2, 0.01);
    public static readonly LightSlider SpikeSlider = new(0, 3, 0.05);

    /// <summary>The playground's opening values (BEAM_DEFAULTS), the shipped default (OD-0089).</summary>
    public static BorderBeamOptions Default { get; } = new();

    /// <summary>The family a type belongs to: Large, Small and Line rotate; the two pulses pulse.</summary>
    public static BeamFamily FamilyOf(BeamType type) => type is BeamType.PulseInner or BeamType.PulseOutside ? BeamFamily.Pulse : BeamFamily.Rotate;

    /// <summary>The Family row's pick: a new family starts on its first type (Large, or Pulse Inner).</summary>
    public BorderBeamOptions WithFamily(BeamFamily family) =>
        family == Family ? this : this with { Family = family, Type = family == BeamFamily.Rotate ? BeamType.Large : BeamType.PulseInner };

    /// <summary>The Type row's pick; the family follows the type.</summary>
    public BorderBeamOptions WithType(BeamType type) => this with { Type = type, Family = FamilyOf(type) };

    /// <summary>"Library defaults": BEAM_DEFAULTS, keeping the family, type, palette and geometry.</summary>
    public BorderBeamOptions LibraryDefaults() => Default with { Family = Family, Type = Type, Palette = Palette, Fitted = Fitted };

    /// <summary>"Calm for the composer": BEAM_CALM over the current values (9 s, strength .55, hue range 18, glow 1.3, follow the state).</summary>
    public BorderBeamOptions Calm() => this with { Duration = 9, Strength = 0.55, HueRange = 18, GlowSize = 1.3, Respond = true };

    /// <summary>Every value inside its slider's range and on its step; the family matching the type.</summary>
    public BorderBeamOptions Clamped() => this with
    {
        Family = FamilyOf(Type),
        Duration = Duration is { } d ? DurationSlider.Snap(d) : null,
        Radius = Radius is { } r ? RadiusSlider.Snap(r) : null,
        Brightness = Brightness is { } b ? BrightnessSlider.Snap(b) : null,
        HueShift = HueShiftSlider.Snap(HueShift),
        Strength = StrengthSlider.Snap(Strength),
        Saturation = Saturation is { } s ? SaturationSlider.Snap(s) : null,
        GlowSize = GlowSizeSlider.Snap(GlowSize),
        HueRange = HueRangeSlider.Snap(HueRange),
        StrokeX = LayerSlider.Snap(StrokeX),
        InnerX = LayerSlider.Snap(InnerX),
        BloomX = LayerSlider.Snap(BloomX),
        CoreBlur = CoreBlur is { } c ? CoreBlurSlider.Snap(c) : null,
        BloomBlur = BloomBlur is { } bb ? BloomBlurSlider.Snap(bb) : null,
        GlowX = GlowX is { } gx ? GlowAxisSlider.Snap(gx) : null,
        GlowY = GlowY is { } gy ? GlowAxisSlider.Snap(gy) : null,
        Boost = BoostSlider.Snap(Boost),
        Spike = SpikeSlider.Snap(Spike),
    };
}

/// <summary>
/// The Uiverse light's options (AURA_DEFAULTS, the playground's calm defaults; "Original values" restores the reference's
/// own, AURA_ORIGINAL). Colours are #RRGGBBAA as the glass picker stores them (OD-0099 Look Picks, Q-CZ-3 B); a stored #RRGGBB
/// reads as opaque.
/// </summary>
public sealed record UiverseAuraOptions
{
    public AuraScheme Scheme { get; init; } = AuraScheme.Original;
    public string Color1 { get; init; } = "#8E7CFF";
    public string Color2 { get; init; } = "#38C9B4";
    public double Speed { get; init; } = 12;
    public bool Breathing { get; init; } = true;
    public double Rest { get; init; } = 40;
    public double Typing { get; init; } = 55;
    public double Running { get; init; } = 75;
    public double HaloSize { get; init; } = 2;
    public double HaloBlur { get; init; } = 30;
    public bool Rim { get; init; } = true;
    public double RimWidth { get; init; } = 1.5;
    public bool Dots { get; init; } = true;
    public double DotCell { get; init; } = 3;
    public double DotStrength { get; init; } = 67;

    // auraRows(): each slider's range and step
    public static readonly LightSlider SpeedSlider = new(3, 40, 0.5);
    public static readonly LightSlider IntensitySlider = new(0, 100, 1);
    public static readonly LightSlider HaloSizeSlider = new(0, 40, 1);
    public static readonly LightSlider HaloBlurSlider = new(4, 64, 1);
    public static readonly LightSlider RimWidthSlider = new(0.5, 5, 0.25);
    public static readonly LightSlider DotCellSlider = new(2, 8, 0.5);
    public static readonly LightSlider DotStrengthSlider = new(0, 100, 1);

    public static UiverseAuraOptions Default { get; } = new();

    /// <summary>The intensity (0..100) for a phase: At rest, Typing, Running a turn.</summary>
    public double IntensityFor(ComposerLightPhase phase) => phase switch
    {
        ComposerLightPhase.Typing => Typing,
        ComposerLightPhase.Running => Running,
        _ => Rest,
    };

    /// <summary>"Calm defaults": AURA_DEFAULTS, keeping the scheme and the custom pair.</summary>
    public UiverseAuraOptions CalmDefaults() => Default with { Scheme = Scheme, Color1 = Color1, Color2 = Color2 };

    /// <summary>"Original values": AURA_ORIGINAL over the current values (the reference's 8 s turn, 50 / 50 / 100, no halo growth, blur 32, rim 2).</summary>
    public UiverseAuraOptions Original() => this with
    {
        Scheme = AuraScheme.Original, Speed = 8, Breathing = true, Rest = 50, Typing = 50, Running = 100, HaloSize = 0, HaloBlur = 32,
        Rim = true, RimWidth = 2, Dots = true, DotCell = 3, DotStrength = 67,
    };

    /// <summary>Every value inside its slider's range and on its step; colours as #RRGGBB or #RRGGBBAA (an unreadable one falls back to the default).</summary>
    public UiverseAuraOptions Clamped() => this with
    {
        Color1 = NormalizedHex(Color1) ?? Default.Color1,
        Color2 = NormalizedHex(Color2) ?? Default.Color2,
        Speed = SpeedSlider.Snap(Speed),
        Rest = IntensitySlider.Snap(Rest),
        Typing = IntensitySlider.Snap(Typing),
        Running = IntensitySlider.Snap(Running),
        HaloSize = HaloSizeSlider.Snap(HaloSize),
        HaloBlur = HaloBlurSlider.Snap(HaloBlur),
        RimWidth = RimWidthSlider.Snap(RimWidth),
        DotCell = DotCellSlider.Snap(DotCell),
        DotStrength = DotStrengthSlider.Snap(DotStrength),
    };

    /// <summary>"#RRGGBB" or "#RRGGBBAA" in upper case, or null when the text is neither a six- nor an eight-digit hex colour.</summary>
    public static string? NormalizedHex(string? text)
    {
        if (text is not { Length: 7 or 9 } || text[0] != '#') return null;
        for (var i = 1; i < text.Length; i++) if (!Uri.IsHexDigit(text[i])) return null;
        return text.ToUpperInvariant();
    }

    /// <summary>The colour without its alpha: the first six digits of a normalised colour ("#RRGGBB").</summary>
    public static string OpaqueHex(string hex) => hex.Length > 7 ? hex[..7] : hex;

    /// <summary>The alpha (0..1) of a normalised colour: the last pair of "#RRGGBBAA" as a byte / 255 (the design's own hex field reads it so); 1 for "#RRGGBB".</summary>
    public static double AlphaOf(string hex) =>
        hex.Length == 9 ? int.Parse(hex.AsSpan(7, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture) / 255.0 : 1;
}

/// <summary>
/// The composer light (OD-0085, OD-0089): the style and both styles' options, persisted as one JSON object under the
/// settings key <see cref="StoreKey"/>. Border beam is the default.
/// </summary>
public sealed record ComposerLightSettings
{
    /// <summary>The settings key.</summary>
    public const string StoreKey = "composerLight";

    public ComposerLightStyle Style { get; init; } = ComposerLightStyle.Beam;
    public BorderBeamOptions Beam { get; init; } = BorderBeamOptions.Default;
    public UiverseAuraOptions Aura { get; init; } = UiverseAuraOptions.Default;

    /// <summary>OD-0150: the shader light worn when <see cref="Style"/> is <see cref="ComposerLightStyle.Shader"/> (kept while another style shows).</summary>
    public string ShaderId { get; init; } = ShaderLightCatalog.All[0].Id;

    /// <summary>OD-0150: every shader light's options (each light keeps its own).</summary>
    public ShaderLightOptionsSet Shaders { get; init; } = ShaderLightOptionsSet.Empty;

    /// <summary>OD-0150: how the chat text meets a shader light (the v4 page's TL.mode; Text above light by default).</summary>
    public ComposerLightTextMode TextMode { get; init; } = ComposerLightTextMode.TextAbove;

    /// <summary>OD-0150: "Light behind text", 0..1 (TL.behind, 35 %).</summary>
    public double TextBehind { get; init; } = DefaultTextBehind;

    /// <summary>OD-0150: "Text halo", 0..1 (TL.halo, 60 %).</summary>
    public double TextHalo { get; init; } = DefaultTextHalo;

    /// <summary>OD-0150: "Dim wallpaper behind light": Auto (the default) or Off.</summary>
    public WallpaperDimMode WallpaperDim { get; init; } = WallpaperDimMode.Auto;

    /// <summary>OD-0150: the dimming's strength, 0..1 (50 %), scaling the Auto amount.</summary>
    public double WallpaperDimStrength { get; init; } = DefaultWallpaperDimStrength;

    public const double DefaultTextBehind = .35, DefaultTextHalo = .6, DefaultWallpaperDimStrength = .5;

    /// <summary>The three 0..100 % sliders (Light behind text, Text halo, the dimming's strength).</summary>
    public static readonly LightSlider PercentSlider = new(0, 1, 0.01);

    /// <summary>True when the text and dimming settings are as they ship.</summary>
    public bool TextAndDimAtDefaults => TextMode == ComposerLightTextMode.TextAbove && TextBehind == DefaultTextBehind && TextHalo == DefaultTextHalo
        && WallpaperDim == WallpaperDimMode.Auto && WallpaperDimStrength == DefaultWallpaperDimStrength;

    /// <summary>These settings with the text and dimming settings as they ship.</summary>
    public ComposerLightSettings WithTextAndDimDefaults() => this with
    {
        TextMode = ComposerLightTextMode.TextAbove, TextBehind = DefaultTextBehind, TextHalo = DefaultTextHalo,
        WallpaperDim = WallpaperDimMode.Auto, WallpaperDimStrength = DefaultWallpaperDimStrength,
    };

    /// <summary>The shader light the composer wears now, or null when its style is not a shader light.</summary>
    public ShaderLightDef? ActiveShader => Style == ComposerLightStyle.Shader ? ShaderLightCatalog.Find(ShaderId) : null;

    /// <summary>The options of shader light <paramref name="id"/>.</summary>
    public ShaderLightOptions ShaderOptions(string id) => Shaders.For(id);

    /// <summary>OD-0089: Border beam with the playground's opening values; the Uiverse light at its calm defaults.</summary>
    public static ComposerLightSettings Default { get; } = new();

    public ComposerLightSettings Clamped() => this with
    {
        Beam = Beam.Clamped(), Aura = Aura.Clamped(),
        ShaderId = ShaderLightCatalog.Find(ShaderId) is not null ? ShaderId : ShaderLightCatalog.All[0].Id,
        Shaders = (Shaders ?? ShaderLightOptionsSet.Empty).Clamped(),
        TextMode = Enum.IsDefined(TextMode) ? TextMode : ComposerLightTextMode.TextAbove,
        TextBehind = PercentSlider.Snap(TextBehind),
        TextHalo = PercentSlider.Snap(TextHalo),
        WallpaperDim = Enum.IsDefined(WallpaperDim) ? WallpaperDim : WallpaperDimMode.Auto,
        WallpaperDimStrength = PercentSlider.Snap(WallpaperDimStrength),
    };

    // ---------- JSON: the playground's own keys ----------

    private static readonly string[] StyleNames = ["beam", "aura", "off"]; // a shader light is stored by its id (OD-0150)
    private static readonly string[] FamilyNames = ["rotate", "pulse"];
    private static readonly string[] TypeNames = ["md", "sm", "line", "pulse-inner", "pulse-outside"];
    private static readonly string[] PaletteNames = ["colorful", "mono", "ocean", "sunset", "forest", "candy", "ice", "gold"];
    private static readonly string[] ThemeNames = ["dark", "light", "auto"];
    private static readonly string[] SchemeNames = ["original", "maker", "accent", "custom"];
    private static readonly string[] TextModeNames = ["above", "light", "diff"]; // the v4 page's TL.mode
    private static readonly string[] DimNames = ["auto", "off"];

    /// <summary>The package's name of a type (its <c>size</c> prop).</summary>
    public static string TypeName(BeamType type) => TypeNames[(int)type];

    /// <summary>The package's name of a palette (its <c>colorVariant</c>).</summary>
    public static string PaletteName(BeamPalette palette) => PaletteNames[(int)palette];

    /// <summary>Compact JSON, keys as the playground names them.</summary>
    public byte[] ToJson()
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            // OD-0150: a shader light's style is its id; the id is kept on its own too, for when another style shows
            w.WriteString("style", Style == ComposerLightStyle.Shader ? ShaderId : StyleNames[(int)Style]);
            w.WriteString("shader", ShaderId);
            w.WriteStartObject("beam");
            var b = Beam;
            w.WriteString("family", FamilyNames[(int)b.Family]);
            w.WriteString("size", TypeNames[(int)b.Type]);
            w.WriteString("color", PaletteNames[(int)b.Palette]);
            w.WriteString("theme", ThemeNames[(int)b.Theme]);
            w.WriteBoolean("active", b.Active);
            Optional(w, "duration", b.Duration);
            Optional(w, "radius", b.Radius);
            Optional(w, "brightness", b.Brightness);
            w.WriteNumber("hueShift", b.HueShift);
            w.WriteNumber("strength", b.Strength);
            Optional(w, "saturation", b.Saturation);
            w.WriteNumber("glowSize", b.GlowSize);
            w.WriteNumber("hueRange", b.HueRange);
            w.WriteBoolean("staticColors", b.StaticColors);
            w.WriteNumber("strokeX", b.StrokeX);
            w.WriteNumber("innerX", b.InnerX);
            w.WriteNumber("bloomX", b.BloomX);
            Optional(w, "coreBlur", b.CoreBlur);
            Optional(w, "bloomBlur", b.BloomBlur);
            Optional(w, "glowX", b.GlowX);
            Optional(w, "glowY", b.GlowY);
            w.WriteNumber("boost", b.Boost);
            w.WriteNumber("spike", b.Spike);
            w.WriteBoolean("faithful", b.Faithful);
            w.WriteBoolean("respond", b.Respond);
            w.WriteBoolean("fit", b.Fitted);
            w.WriteEndObject();
            w.WriteStartObject("aura");
            var a = Aura;
            w.WriteString("scheme", SchemeNames[(int)a.Scheme]);
            w.WriteString("c1", a.Color1);
            w.WriteString("c2", a.Color2);
            w.WriteNumber("speed", a.Speed);
            w.WriteBoolean("breathing", a.Breathing);
            w.WriteNumber("rest", a.Rest);
            w.WriteNumber("typing", a.Typing);
            w.WriteNumber("running", a.Running);
            w.WriteNumber("haloSize", a.HaloSize);
            w.WriteNumber("haloBlur", a.HaloBlur);
            w.WriteBoolean("rim", a.Rim);
            w.WriteNumber("rimW", a.RimWidth);
            w.WriteBoolean("dots", a.Dots);
            w.WriteNumber("dotCell", a.DotCell);
            w.WriteNumber("dotStr", a.DotStrength);
            w.WriteEndObject();
            w.WriteStartObject("shaders");
            foreach (var (id, o) in Shaders.Entries)
            {
                w.WriteStartObject(id);
                w.WriteString("pack", o.Pack);
                w.WriteNumber("spread", o.Spread);
                w.WriteNumber("intensity", o.Intensity);
                w.WriteNumber("speed", o.Speed);
                w.WriteStartArray("custom");
                foreach (var c in o.Custom.All) w.WriteStringValue(c);
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndObject();
            w.WriteStartObject("text");
            w.WriteString("mode", TextModeNames[(int)TextMode]);
            w.WriteNumber("behind", TextBehind);
            w.WriteNumber("halo", TextHalo);
            w.WriteEndObject();
            w.WriteStartObject("wallDim");
            w.WriteString("mode", DimNames[(int)WallpaperDim]);
            w.WriteNumber("strength", WallpaperDimStrength);
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static void Optional(Utf8JsonWriter w, string key, double? value)
    {
        if (value is { } v) w.WriteNumber(key, v);
        else w.WriteNull(key);
    }

    /// <summary>
    /// Reads the stored JSON leniently: a missing or unreadable value keeps its default, a number outside its slider is
    /// clamped to it, an unknown name keeps the default. Unreadable data as a whole is the default.
    /// </summary>
    public static ComposerLightSettings FromJson(ReadOnlySpan<byte> json)
    {
        try
        {
            var reader = new Utf8JsonReader(json, new JsonReaderOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Default;
            var settings = Default with { Style = Name(root, "style", StyleNames, Default.Style) };
            // OD-0150: the shader lights (a style named by a light's id; the kept id; each light's options)
            if (Text(root, "shader") is { } kept && ShaderLightCatalog.Find(kept) is not null) settings = settings with { ShaderId = kept };
            if (Text(root, "style") is { } styleName && ShaderLightCatalog.Find(styleName) is not null)
                settings = settings with { Style = ComposerLightStyle.Shader, ShaderId = styleName };
            if (root.TryGetProperty("shaders", out var shaders) && shaders.ValueKind == JsonValueKind.Object)
            {
                var set = ShaderLightOptionsSet.Empty;
                foreach (var entry in shaders.EnumerateObject())
                {
                    if (ShaderLightCatalog.Find(entry.Name) is not { } def || entry.Value.ValueKind != JsonValueKind.Object) continue;
                    var o = entry.Value; var d = def.Defaults;
                    var custom = d.Custom;
                    if (o.TryGetProperty("custom", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        var i = 0;
                        foreach (var c in arr.EnumerateArray())
                        {
                            if (i > 3) break;
                            if (c.ValueKind == JsonValueKind.String && c.GetString() is { } hex) custom = custom.With(i, hex);
                            i++;
                        }
                    }
                    set = set.With(entry.Name, new ShaderLightOptions(Text(o, "pack") ?? d.Pack, Number(o, "spread", d.Spread), Number(o, "intensity", d.Intensity), Number(o, "speed", d.Speed), custom));
                }
                settings = settings with { Shaders = set };
            }
            if (root.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.Object)
                settings = settings with
                {
                    TextMode = Name(text, "mode", TextModeNames, Default.TextMode),
                    TextBehind = Number(text, "behind", Default.TextBehind),
                    TextHalo = Number(text, "halo", Default.TextHalo),
                };
            if (root.TryGetProperty("wallDim", out var dim) && dim.ValueKind == JsonValueKind.Object)
                settings = settings with
                {
                    WallpaperDim = Name(dim, "mode", DimNames, Default.WallpaperDim),
                    WallpaperDimStrength = Number(dim, "strength", Default.WallpaperDimStrength),
                };
            if (root.TryGetProperty("beam", out var beam) && beam.ValueKind == JsonValueKind.Object)
            {
                var d = BorderBeamOptions.Default;
                var type = Name(beam, "size", TypeNames, d.Type);
                settings = settings with
                {
                    Beam = new BorderBeamOptions
                    {
                        Type = type,
                        Family = BorderBeamOptions.FamilyOf(type),
                        Palette = Name(beam, "color", PaletteNames, d.Palette),
                        Theme = Name(beam, "theme", ThemeNames, d.Theme),
                        Active = Bool(beam, "active", d.Active),
                        Duration = Nullable(beam, "duration", d.Duration),
                        Radius = Nullable(beam, "radius", d.Radius),
                        Brightness = Nullable(beam, "brightness", d.Brightness),
                        HueShift = Number(beam, "hueShift", d.HueShift),
                        Strength = Number(beam, "strength", d.Strength),
                        Saturation = Nullable(beam, "saturation", d.Saturation),
                        GlowSize = Number(beam, "glowSize", d.GlowSize),
                        HueRange = Number(beam, "hueRange", d.HueRange),
                        StaticColors = Bool(beam, "staticColors", d.StaticColors),
                        StrokeX = Number(beam, "strokeX", d.StrokeX),
                        InnerX = Number(beam, "innerX", d.InnerX),
                        BloomX = Number(beam, "bloomX", d.BloomX),
                        CoreBlur = Nullable(beam, "coreBlur", d.CoreBlur),
                        BloomBlur = Nullable(beam, "bloomBlur", d.BloomBlur),
                        GlowX = Nullable(beam, "glowX", d.GlowX),
                        GlowY = Nullable(beam, "glowY", d.GlowY),
                        Boost = Number(beam, "boost", d.Boost),
                        Spike = Number(beam, "spike", d.Spike),
                        Faithful = Bool(beam, "faithful", d.Faithful),
                        Respond = Bool(beam, "respond", d.Respond),
                        Fitted = Bool(beam, "fit", d.Fitted),
                    },
                };
            }
            if (root.TryGetProperty("aura", out var aura) && aura.ValueKind == JsonValueKind.Object)
            {
                var d = UiverseAuraOptions.Default;
                settings = settings with
                {
                    Aura = new UiverseAuraOptions
                    {
                        Scheme = Name(aura, "scheme", SchemeNames, d.Scheme),
                        Color1 = Text(aura, "c1") ?? d.Color1,
                        Color2 = Text(aura, "c2") ?? d.Color2,
                        Speed = Number(aura, "speed", d.Speed),
                        Breathing = Bool(aura, "breathing", d.Breathing),
                        Rest = Number(aura, "rest", d.Rest),
                        Typing = Number(aura, "typing", d.Typing),
                        Running = Number(aura, "running", d.Running),
                        HaloSize = Number(aura, "haloSize", d.HaloSize),
                        HaloBlur = Number(aura, "haloBlur", d.HaloBlur),
                        Rim = Bool(aura, "rim", d.Rim),
                        RimWidth = Number(aura, "rimW", d.RimWidth),
                        Dots = Bool(aura, "dots", d.Dots),
                        DotCell = Number(aura, "dotCell", d.DotCell),
                        DotStrength = Number(aura, "dotStr", d.DotStrength),
                    },
                };
            }
            return settings.Clamped();
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException)
        {
            // malformed JSON, invalid UTF-8, a lone surrogate escape (GetString throws InvalidOperationException)
            return Default;
        }
    }

    private static T Name<T>(JsonElement o, string key, string[] names, T fallback) where T : struct, Enum
    {
        if (!o.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.String) return fallback;
        var i = Array.IndexOf(names, v.GetString());
        return i < 0 ? fallback : (T)(object)i;
    }

    private static bool Bool(JsonElement o, string key, bool fallback) =>
        o.TryGetProperty(key, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : fallback;

    private static double Number(JsonElement o, string key, double fallback) =>
        o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) && double.IsFinite(d) ? d : fallback;

    private static double? Nullable(JsonElement o, string key, double? fallback)
    {
        if (!o.TryGetProperty(key, out var v)) return fallback;
        if (v.ValueKind == JsonValueKind.Null) return null;
        return v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) && double.IsFinite(d) ? d : fallback;
    }

    private static string? Text(JsonElement o, string key) =>
        o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>A short readable form for diagnostics.</summary>
    public override string ToString() => Encoding.UTF8.GetString(ToJson());

    internal static string Invariant(double v) => v.ToString("R", CultureInfo.InvariantCulture);
}
