// PORT-ONLY, owner-requested addition (owner 2026-10-11, relayed by the lead: "add glass UI settings inside Customization, so the user can set it as he
// wishes"; OD-0107): the Glass settings of Settings > Customization (ED-NEW-glasslook-2). The owner's target is Apple's Liquid Glass as shipped in
// iOS 27 and macOS 27 (the lead, 2026-10-11): the group is a Look choice, Clear, Regular or Tinted, as Apple's own Liquid Glass setting, with the
// fine-tuning under it. The two MIT repos' live demos are the technique references for the knobs (rizzytoday/liquid-glass: Scale, Edge Blur, Border,
// Lightness, Alpha, Frost, Saturation, Aberration R/G/B; dashersw/liquid-glass-js: Edge, Rim and Base Intensity and Distance, Corner Boost, Ripple,
// Blur Radius, Tint Opacity); each knob is a share of the look its surface is given for the chosen Look (Marrow.UI's GlassLooks table), so 1 (100 %)
// is that look.
using System.Globalization;
using System.Text.Json;

namespace Marrow.Core.Port.Settings;

/// <summary>The Look of the glass, as Apple's Liquid Glass setting offers it.</summary>
public enum GlassVariant
{
    /// <summary>The glass as the app draws it by default.</summary>
    Regular,
    /// <summary>A clearer glass: more of what lies behind it shows.</summary>
    Clear,
    /// <summary>A more opaque, tinted glass: more contrast for what lies on it.</summary>
    Tinted,
}

/// <summary>
/// The <c>glassLook</c> setting: compact JSON of the Look and the knobs that differ from <see cref="Default"/>. Lenient read, as
/// <see cref="WorkingDotsSettings"/>: a missing, mistyped or out-of-range value keeps its default or is held to its range, and unreadable data as a
/// whole is the default.
/// </summary>
public sealed record GlassSettings
{
    /// <summary>The settings key.</summary>
    public const string StoreKey = "glassLook";

    /// <summary>The Look (Clear, Regular, Tinted): which of each surface's looks the knobs below tune.</summary>
    public GlassVariant Look { get; init; } = GlassVariant.Regular;

    /// <summary>How far the bezel bends what lies behind it (rizzytoday's Scale; dashersw's Edge and Rim Intensity): a share of the surface's own bend.</summary>
    public double Refraction { get; init; } = 1;

    /// <summary>How wide the bending bezel is (rizzytoday's Edge Blur and Border; dashersw's Edge and Rim Distance).</summary>
    public double Bezel { get; init; } = 1;

    /// <summary>How clear the bezel is, 0 to 1 (rizzytoday bends the backdrop itself, unblurred); null is the surface's own.</summary>
    public double? Clarity { get; init; }

    /// <summary>The frost: the blur behind the glass (dashersw's Blur Radius).</summary>
    public double Frost { get; init; } = 1;

    /// <summary>The colour fringe at the bezel (rizzytoday's Aberration).</summary>
    public double Fringe { get; init; } = 1;

    /// <summary>The rim light: the specular line and the light inner edge.</summary>
    public double Rim { get; init; } = 1;

    /// <summary>The tint: the film's opacity (dashersw's Tint Opacity; rizzytoday's Frost, Lightness and Alpha).</summary>
    public double Tint { get; init; } = 1;

    /// <summary>The saturation of what the glass shows (rizzytoday's Saturation).</summary>
    public double Saturation { get; init; } = 1;

    /// <summary>The shadow depth: the contact line just outside the edge and the soft shadow under the glass.</summary>
    public double Shadow { get; init; } = 1;

    /// <summary>The Regular look, every knob at its surface's own.</summary>
    public static GlassSettings Default { get; } = new();

    /// <summary>The knobs: their names in the store and on the page, and their ranges. ED-NEW-glasslook-3 (the owner's premium principles, 2026-10-11:
    /// "no setting can make the glass look plastic"): no knob reaches past what keeps the glass glass: never more fill (Tint) than the surface's own,
    /// a rim at most a quarter brighter (never a thick glossy border), a fringe never wider than the surface's own (a hint, not a rainbow), a bend at
    /// most twice the surface's, a bezel that stays a bezel, saturation that never turns garish, and the frost between a little blur and a soft
    /// frost. The text-on-glass rule (4.5:1) holds at every end of every Look (GlassTuningTests).</summary>
    public static IReadOnlyList<GlassKnob> Knobs { get; } =
    [
        new("refraction", 0, 2),
        new("bezel", 0.75, 1.25),
        new("clarity", 0, 1),
        new("frost", 0.5, 1.5),
        new("fringe", 0, 1),
        new("rim", 0, 1.25),
        new("tint", 0, 1),
        new("saturation", 0.5, 1.5),
        new("shadow", 0, 1.5),
    ];

    /// <summary>The Looks in the page's order (Apple's: Clear, then the default, then Tinted), with their ids and names.</summary>
    public static IReadOnlyList<(string Id, string Name, GlassVariant Value)> Looks { get; } =
        [("clear", "Clear", GlassVariant.Clear), ("regular", "Regular", GlassVariant.Regular), ("tinted", "Tinted", GlassVariant.Tinted)];

    /// <summary>A Look's id.</summary>
    public static string IdOf(GlassVariant look) => Looks.First(l => l.Value == look).Id;

    /// <summary>A knob's value by its id (clarity: null for the surface's own).</summary>
    public double? Get(string id) => id switch
    {
        "refraction" => Refraction, "bezel" => Bezel, "clarity" => Clarity, "frost" => Frost, "fringe" => Fringe, "rim" => Rim,
        "tint" => Tint, "saturation" => Saturation, "shadow" => Shadow,
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown glass knob."),
    };

    /// <summary>The same settings with knob <paramref name="id"/> at <paramref name="value"/>, held to its range (null: clarity only, the surface's own).</summary>
    public GlassSettings With(string id, double? value)
    {
        var knob = Knobs.FirstOrDefault(k => k.Id == id) ?? throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown glass knob.");
        if (value is null && id != "clarity") throw new ArgumentNullException(nameof(value), "Only clarity takes the surface's own.");
        var held = value is { } v ? knob.Hold(v) : (double?)null;
        return id switch
        {
            "refraction" => this with { Refraction = held!.Value }, "bezel" => this with { Bezel = held!.Value }, "clarity" => this with { Clarity = held },
            "frost" => this with { Frost = held!.Value }, "fringe" => this with { Fringe = held!.Value }, "rim" => this with { Rim = held!.Value },
            "tint" => this with { Tint = held!.Value }, "saturation" => this with { Saturation = held!.Value }, _ => this with { Shadow = held!.Value },
        };
    }

    /// <summary>Every knob held to its range, the Look one of the three (what the store and the material take).</summary>
    public GlassSettings Held()
    {
        var held = Enum.IsDefined(Look) ? this : this with { Look = GlassVariant.Regular };
        foreach (var knob in Knobs) held = held.With(knob.Id, Get(knob.Id) is { } v ? v : null);
        return held;
    }

    /// <summary>The knobs <paramref name="t"/> of the way from these to <paramref name="to"/>'s (a glide), at <paramref name="to"/>'s Look (the Look
    /// itself crossfades in the material: GlassLooks.Blend); clarity on Auto at one end is taken as <paramref name="ownClarity"/> there, and Auto at
    /// both ends stays Auto (each surface its own). At 0 these, at 1 exactly <paramref name="to"/>.</summary>
    public GlassSettings Toward(GlassSettings to, double t, double ownClarity)
    {
        ArgumentNullException.ThrowIfNull(to);
        if (t >= 1) return to;
        if (t <= 0) return this with { Look = to.Look };
        double M(double a, double b) => a + (b - a) * t;
        return new GlassSettings
        {
            Look = to.Look, Refraction = M(Refraction, to.Refraction), Bezel = M(Bezel, to.Bezel), Clarity = Clarity is null && to.Clarity is null ? null : M(Clarity ?? ownClarity, to.Clarity ?? ownClarity),
            Frost = M(Frost, to.Frost), Fringe = M(Fringe, to.Fringe), Rim = M(Rim, to.Rim), Tint = M(Tint, to.Tint), Saturation = M(Saturation, to.Saturation),
            Shadow = M(Shadow, to.Shadow),
        };
    }

    /// <summary>Compact JSON of the Look (when not Regular) and the knobs that differ from the default, each held to its range (so what is stored always reads back).</summary>
    public byte[] ToJson()
    {
        var held = Held();
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            if (held.Look != GlassVariant.Regular) w.WriteString("look", IdOf(held.Look));
            foreach (var knob in Knobs)
            {
                var value = held.Get(knob.Id);
                if (value == Default.Get(knob.Id)) continue;
                w.WriteNumber(knob.Id, Math.Round(value!.Value, 4));
            }
            w.WriteEndObject();
        }
        return stream.ToArray();
    }

    /// <summary>The lenient read.</summary>
    public static GlassSettings FromJson(ReadOnlySpan<byte> json)
    {
        try
        {
            var reader = new Utf8JsonReader(json, new JsonReaderOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Default;
            var read = Default;
            if (root.TryGetProperty("look", out var look) && look.ValueKind == JsonValueKind.String && Looks.FirstOrDefault(l => l.Id == look.GetString()) is { Id: not null } known)
                read = read with { Look = known.Value };
            foreach (var knob in Knobs)
                if (root.TryGetProperty(knob.Id, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var number) && double.IsFinite(number))
                    read = read.With(knob.Id, number);
            return read;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException)
        {
            return Default;
        }
    }

    /// <inheritdoc />
    public override string ToString() => System.Text.Encoding.UTF8.GetString(ToJson());
}

/// <summary>One knob of <see cref="GlassSettings"/>: its id and its range.</summary>
public sealed record GlassKnob(string Id, double Min, double Max)
{
    /// <summary>The value held to the range.</summary>
    public double Hold(double value) => Math.Clamp(double.IsFinite(value) ? value : 1, Min, Max);

    /// <summary>The value as the page shows it (a percentage).</summary>
    public string Format(double value) => Math.Round(value * 100).ToString(CultureInfo.InvariantCulture) + "%";
}
