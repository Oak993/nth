// PORT-ONLY, owner-ruled addition (OD-0083, OD-0085, OD-0089; ED-251): the composer light's settings. The specification is
// the approved playground, E:/Droppy/work/design/composer-lights/playground/src/logic.js (AURA_DEFAULTS, AURA_ORIGINAL,
// BEAM_DEFAULTS, BEAM_CALM, the slider rows of auraRows() and beamRows(), and slider()'s rounding), not the pinned Swift
// source, which has no composer light. Border beam is border-beam 1.4.1 (MIT, Jakub Antalik); the Uiverse light is
// SelfMadeSystem's Uiverse.io button (MIT). Both are credited in THIRD_PARTY_NOTICES.md.
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Marrow.Core.Models.ComposerLights;

/// <summary>Which light the composer wears (OD-0085): Border beam (the default, OD-0089), the Uiverse light, or none.</summary>
public enum ComposerLightStyle { Beam, Aura, Off }

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
    /// <summary>slider()'s set: <c>Math.round(clamp(v, min, max) / step) * step</c> (JavaScript rounds a half up).</summary>
    public double Snap(double value)
    {
        if (double.IsNaN(value)) value = Min;
        var clamped = Math.Clamp(value, Min, Max);
        return Math.Floor(clamped / Step + 0.5) * Step;
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
    public static readonly LightSlider DurationSlider = new(0.5, 14, 0.02);
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
/// own, AURA_ORIGINAL). Colours are #RRGGBB.
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

    /// <summary>Every value inside its slider's range and on its step; colours as #RRGGBB (an unreadable one falls back to the default).</summary>
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

    /// <summary>"#rrggbb" in upper case, or null when the text is not a six-digit hex colour.</summary>
    public static string? NormalizedHex(string? text)
    {
        if (text is not { Length: 7 } || text[0] != '#') return null;
        for (var i = 1; i < 7; i++) if (!Uri.IsHexDigit(text[i])) return null;
        return text.ToUpperInvariant();
    }
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

    /// <summary>OD-0089: Border beam with the playground's opening values; the Uiverse light at its calm defaults.</summary>
    public static ComposerLightSettings Default { get; } = new();

    public ComposerLightSettings Clamped() => this with { Beam = Beam.Clamped(), Aura = Aura.Clamped() };

    // ---------- JSON: the playground's own keys ----------

    private static readonly string[] StyleNames = ["beam", "aura", "off"];
    private static readonly string[] FamilyNames = ["rotate", "pulse"];
    private static readonly string[] TypeNames = ["md", "sm", "line", "pulse-inner", "pulse-outside"];
    private static readonly string[] PaletteNames = ["colorful", "mono", "ocean", "sunset", "forest", "candy", "ice", "gold"];
    private static readonly string[] ThemeNames = ["dark", "light", "auto"];
    private static readonly string[] SchemeNames = ["original", "maker", "accent", "custom"];

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
            w.WriteString("style", StyleNames[(int)Style]);
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
        catch (JsonException)
        {
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
