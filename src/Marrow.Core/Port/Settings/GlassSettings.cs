// PORT-ONLY, owner-requested addition (owner 2026-10-11, relayed by the lead: "add glass UI settings inside Customization, so the user can set it as he
// wishes"; OD-0107) and owner-approved design (OD-0147, the owner 2026-10-11: "Glass UI Approved, ship it with option for user to be able to set all
// those settings ... but ship with its current default values"): the Glass settings of Settings > Customization (ED-NEW-glasslook-2), as the approved
// design canvas (E:/Droppy/work/design/glass-ui/index.html, its Glass settings panel) has them: the Look (Clear, Regular, Tinted, Apple's Liquid Glass
// stops in iOS 27) and the Transparency slider between them, the eight fine-tuning values with the canvas's ranges and steps, and the behaviour switches
// (tint colour, motion, Increase contrast, Reduce transparency, Translucent sidebar). Every number is the canvas's own (its LOOKS and FINE tables).
using System.Globalization;
using System.Text.Json;

namespace Marrow.Core.Port.Settings;

/// <summary>The Look of the glass, as Apple's Liquid Glass setting offers it: the three stops of the Transparency slider.</summary>
public enum GlassVariant
{
    /// <summary>The default, the iOS 27 midpoint (the slider at 50).</summary>
    Regular,
    /// <summary>More clear: the most see-through (the slider at 0).</summary>
    Clear,
    /// <summary>More tinted: frostier, reads best (the slider at 100).</summary>
    Tinted,
}

/// <summary>The canvas's tint colours: a small colour cast, capped so it stays glass.</summary>
public enum GlassTintColour
{
    /// <summary>No tint.</summary>
    None,
    /// <summary>Silver.</summary>
    Silver,
    /// <summary>Blue.</summary>
    Blue,
    /// <summary>Purple.</summary>
    Purple,
    /// <summary>Pink.</summary>
    Pink,
    /// <summary>Orange.</summary>
    Orange,
    /// <summary>Green.</summary>
    Green,
}

/// <summary>
/// The <c>glass</c> setting: compact JSON of what differs from <see cref="Default"/>. Lenient read, as <see cref="WorkingDotsSettings"/>: a missing,
/// mistyped or out-of-range value keeps its default or is held to its range, and unreadable data as a whole is the default. The three accessibility
/// switches (motion, Increase contrast, Reduce transparency) are null until the user sets them: then they follow Windows' own switch (one source of
/// truth; GlassLooks reads Windows), and the user's choice, once made, is the glass's.
/// </summary>
public sealed record GlassSettings
{
    /// <summary>The settings key (the canvas's schema; the earlier <c>glassLook</c> key held shares of another look and is not read).</summary>
    public const string StoreKey = "glass";

    /// <summary>The Transparency slider's default: Regular.</summary>
    public const double RegularAt = 50;

    /// <summary>The Transparency slider, 0 (Clear) to 100 (Tinted), whole steps; 50 is Regular. Moving it sets every fine-tuning value to the look
    /// at that point (the canvas's setG).</summary>
    public double Transparency { get; init; } = RegularAt;

    /// <summary>Refraction: the bend at the edge, times the bezel; also sets the bezel magnification.</summary>
    public double Refraction { get; init; } = 1.30;

    /// <summary>Bezel width (px): how far in from the edge the lens reaches.</summary>
    public double Bezel { get; init; } = 12;

    /// <summary>Frost (px): the blur radius of the body, never below 20.</summary>
    public double Frost { get; init; } = 32;

    /// <summary>Colour fringe (px): the red-to-blue split in the sharp rim, at most 1 px.</summary>
    public double Fringe { get; init; } = 0.4;

    /// <summary>Rim light: the one-pixel line on the lit side.</summary>
    public double Rim { get; init; } = 0.5;

    /// <summary>Tint: takes colour out (not light) and calms bright content.</summary>
    public double Tint { get; init; } = 0.12;

    /// <summary>Saturation: the colour of what shows through.</summary>
    public double Saturation { get; init; } = 1.22;

    /// <summary>Shadow depth: the soft shadow and the contact shadow.</summary>
    public double Shadow { get; init; } = 1;

    /// <summary>The tint colour: a small colour cast, capped so it stays glass.</summary>
    public GlassTintColour TintColour { get; init; } = GlassTintColour.None;

    /// <summary>Motion (the bulge on press): true Reduced, false Full, null following Windows' Animation effects.</summary>
    public bool? ReducedMotion { get; init; }

    /// <summary>Increase contrast (sharper edges and a stronger line of light); null follows Windows' contrast themes.</summary>
    public bool? IncreaseContrast { get; init; }

    /// <summary>Reduce transparency (solid surfaces instead of glass); null follows Windows' Transparency effects (off: reduced).</summary>
    public bool? ReduceTransparency { get; init; }

    /// <summary>Translucent sidebar: the window behind tints the sidebar; off, the sidebar is all but solid.</summary>
    public bool TranslucentSidebar { get; init; } = true;

    /// <summary>The shipped glass: Regular, every value the canvas's default, the switches following Windows.</summary>
    public static GlassSettings Default { get; } = new();

    /// <summary>The eight fine-tuning values, in the canvas's order, with its names, descriptions, ranges, steps and units (its FINE table). Every
    /// range is an anti-plastic clamp (ED-NEW-glasslook-3): a fringe of at most 1 px, a rim line of at most 1, a tint of at most 0.5, a frost of at
    /// least 20 px.</summary>
    public static IReadOnlyList<GlassKnob> Knobs { get; } =
    [
        new("refraction", "Refraction", "Bend at the edge, times the bezel; also sets the bezel magnification.", 0.4, 2.4, 0.05, GlassUnit.Times),
        new("bezel", "Bezel width", "How far in from the edge the lens reaches.", 6, 24, 0.5, GlassUnit.Pixels1),
        new("frost", "Frost", "Blur radius of the body; never below 20, the text scrim grows as it drops.", 20, 64, 1, GlassUnit.Pixels0),
        new("fringe", "Colour fringe", "Red-to-blue split in the sharp rim, at most 1 px.", 0, 1, 0.05, GlassUnit.Pixels2),
        new("rim", "Rim light", "The one-pixel line on the lit side.", 0.1, 1, 0.01, GlassUnit.Plain),
        new("tint", "Tint", "Takes colour out (not light) and calms bright content.", 0, 0.5, 0.01, GlassUnit.Plain),
        new("saturation", "Saturation", "Colour of what shows through.", 0.8, 1.8, 0.01, GlassUnit.Times),
        new("shadow", "Shadow depth", "Soft shadow and contact shadow.", 0, 2, 0.05, GlassUnit.Times),
    ];

    /// <summary>The Transparency slider's range and step.</summary>
    public static GlassKnob TransparencyKnob { get; } = new("transparency", "Transparency", "From more clear to more tinted.", 0, 100, 1, GlassUnit.Whole);

    /// <summary>The Looks in the page's order (Apple's: Clear, then the default, then Tinted), with their ids, names and slider positions.</summary>
    public static IReadOnlyList<(string Id, string Name, GlassVariant Value, double At)> Looks { get; } =
        [("clear", "Clear", GlassVariant.Clear, 0), ("regular", "Regular", GlassVariant.Regular, RegularAt), ("tinted", "Tinted", GlassVariant.Tinted, 100)];

    // The canvas's LOOKS: refraction, bezel, frost, fringe, rim, tint, saturation, shadow.
    private static readonly double[] ClearStop = [1.40, 12, 24, 0.5, 0.55, 0.04, 1.25, 1];
    private static readonly double[] RegularStop = [1.30, 12, 32, 0.4, 0.50, 0.12, 1.22, 1];
    private static readonly double[] TintedStop = [1.10, 11, 44, 0.3, 0.50, 0.24, 1.15, 1];

    /// <summary>The canvas's tint colours (sRGB, 0 to 1), with their ids and names; None has none.</summary>
    public static IReadOnlyList<(string Id, string Name, GlassTintColour Value, double R, double G, double B)> TintColours { get; } =
    [
        ("none", "No tint", GlassTintColour.None, 0, 0, 0), ("silver", "Silver", GlassTintColour.Silver, 0.86, 0.87, 0.9),
        ("blue", "Blue", GlassTintColour.Blue, 0, 0.48, 1), ("purple", "Purple", GlassTintColour.Purple, 0.69, 0.32, 0.87),
        ("pink", "Pink", GlassTintColour.Pink, 1, 0.18, 0.33), ("orange", "Orange", GlassTintColour.Orange, 1, 0.58, 0),
        ("green", "Green", GlassTintColour.Green, 0.2, 0.78, 0.35),
    ];

    /// <summary>A Look's id.</summary>
    public static string IdOf(GlassVariant look) => Looks.First(l => l.Value == look).Id;

    /// <summary>The Look the slider sits on, or null between two (the canvas's stopName).</summary>
    public GlassVariant? Look
    {
        get
        {
            foreach (var l in Looks)
                if (Math.Abs(Transparency - l.At) < 0.5) return l.Value;
            return null;
        }
    }

    /// <summary>The Transparency slider's words (the canvas's lookName): More clear under 20, Default to 80, More tinted past it.</summary>
    public static string LookName(double transparency) => transparency < 20 ? "More clear" : transparency < 80 ? "Default" : "More tinted";

    /// <summary>The glass at slider position <paramref name="transparency"/>: every fine-tuning value the look there (between two stops, each value
    /// in between, to four places: the canvas's lookAt); the behaviour switches kept.</summary>
    public GlassSettings At(double transparency)
    {
        var g = TransparencyKnob.Hold(transparency);
        var values = LookAt(g);
        return this with
        {
            Transparency = g, Refraction = values[0], Bezel = values[1], Frost = values[2], Fringe = values[3], Rim = values[4], Tint = values[5],
            Saturation = values[6], Shadow = values[7],
        };
    }

    /// <summary>The same settings at <paramref name="look"/>'s stop (every fine-tuning value its own).</summary>
    public GlassSettings At(GlassVariant look) => At(Looks.First(l => l.Value == look).At);

    private static double[] LookAt(double transparency)
    {
        var g = Math.Clamp(transparency, 0, 100) / 100;
        var (a, b, t) = g <= 0.5 ? (ClearStop, RegularStop, g / 0.5) : (RegularStop, TintedStop, (g - 0.5) / 0.5);
        var o = new double[a.Length];
        for (var i = 0; i < a.Length; i++) o[i] = Math.Round(a[i] + (b[i] - a[i]) * t, 4);
        return o;
    }

    /// <summary>Whether a fine-tuning value differs from the look at the slider's position (the canvas's Custom badge).</summary>
    public bool IsCustom
    {
        get
        {
            var look = LookAt(Transparency);
            for (var i = 0; i < Knobs.Count; i++)
                if (Math.Abs(look[i] - Get(Knobs[i].Id)) > 1e-3) return true;
            return false;
        }
    }

    /// <summary>The fine-tuning back to the look at the slider's position (the canvas's Reset); the slider and the switches kept.</summary>
    public GlassSettings ResetFineTuning() => At(Transparency);

    /// <summary>A fine-tuning value by its id.</summary>
    public double Get(string id) => id switch
    {
        "transparency" => Transparency, "refraction" => Refraction, "bezel" => Bezel, "frost" => Frost, "fringe" => Fringe, "rim" => Rim,
        "tint" => Tint, "saturation" => Saturation, "shadow" => Shadow,
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown glass knob."),
    };

    /// <summary>The same settings with value <paramref name="id"/> at <paramref name="value"/>, held to its range; the Transparency slider sets every
    /// fine-tuning value to the look there.</summary>
    public GlassSettings With(string id, double value)
    {
        if (id == TransparencyKnob.Id) return At(value);
        var knob = Knobs.FirstOrDefault(k => k.Id == id) ?? throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown glass knob.");
        var held = knob.Hold(value);
        return id switch
        {
            "refraction" => this with { Refraction = held }, "bezel" => this with { Bezel = held }, "frost" => this with { Frost = held },
            "fringe" => this with { Fringe = held }, "rim" => this with { Rim = held }, "tint" => this with { Tint = held },
            "saturation" => this with { Saturation = held }, _ => this with { Shadow = held },
        };
    }

    /// <summary>Every value held to its range, the tint colour one of the canvas's (what the store and the material take).</summary>
    public GlassSettings Held()
    {
        var held = this with
        {
            Transparency = TransparencyKnob.Hold(Transparency),
            TintColour = Enum.IsDefined(TintColour) ? TintColour : GlassTintColour.None,
        };
        foreach (var knob in Knobs) held = held.With(knob.Id, held.Get(knob.Id));
        return held;
    }

    /// <summary>The values <paramref name="t"/> of the way from these to <paramref name="to"/>'s (what a glide shows on the page); the switches are
    /// <paramref name="to"/>'s. At 0 these, at 1 exactly <paramref name="to"/>.</summary>
    public GlassSettings Toward(GlassSettings to, double t)
    {
        ArgumentNullException.ThrowIfNull(to);
        if (t >= 1) return to;
        if (t <= 0) return this;
        double M(double a, double b) => a + (b - a) * t;
        return to with
        {
            Transparency = M(Transparency, to.Transparency), Refraction = M(Refraction, to.Refraction), Bezel = M(Bezel, to.Bezel), Frost = M(Frost, to.Frost),
            Fringe = M(Fringe, to.Fringe), Rim = M(Rim, to.Rim), Tint = M(Tint, to.Tint), Saturation = M(Saturation, to.Saturation), Shadow = M(Shadow, to.Shadow),
        };
    }

    /// <summary>Compact JSON of what differs from the default: the slider when not Regular, each fine-tuning value that differs from the look at the
    /// slider, the tint colour, and each switch the user has set; each held to its range (so what is stored always reads back).</summary>
    public byte[] ToJson()
    {
        var held = Held();
        var look = LookAt(held.Transparency);
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            if (held.Transparency != RegularAt) w.WriteNumber(TransparencyKnob.Id, held.Transparency);
            for (var i = 0; i < Knobs.Count; i++)
            {
                var value = held.Get(Knobs[i].Id);
                if (Math.Abs(value - look[i]) > 1e-9) w.WriteNumber(Knobs[i].Id, Math.Round(value, 4));
            }
            if (held.TintColour != GlassTintColour.None) w.WriteString("tintColour", TintColours.First(c => c.Value == held.TintColour).Id);
            if (held.ReducedMotion is { } reduced) w.WriteString("motion", reduced ? "reduced" : "full");
            if (held.IncreaseContrast is { } contrast) w.WriteBoolean("increaseContrast", contrast);
            if (held.ReduceTransparency is { } reduce) w.WriteBoolean("reduceTransparency", reduce);
            if (!held.TranslucentSidebar) w.WriteBoolean("translucentSidebar", false);
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
            static double? Number(JsonElement root, string id) =>
                root.TryGetProperty(id, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var number) && double.IsFinite(number) ? number : null;
            static bool? Flag(JsonElement root, string id) =>
                root.TryGetProperty(id, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
            var read = Default.At(Number(root, TransparencyKnob.Id) is { } g ? Math.Round(g) : RegularAt);
            foreach (var knob in Knobs)
                if (Number(root, knob.Id) is { } number) read = read.With(knob.Id, number);
            if (root.TryGetProperty("tintColour", out var tint) && tint.ValueKind == JsonValueKind.String && TintColours.FirstOrDefault(c => c.Id == tint.GetString()) is { Id: not null } colour)
                read = read with { TintColour = colour.Value };
            if (root.TryGetProperty("motion", out var motion) && motion.ValueKind == JsonValueKind.String)
                read = read with { ReducedMotion = motion.GetString() switch { "reduced" => true, "full" => false, _ => null } };
            return read with
            {
                IncreaseContrast = Flag(root, "increaseContrast"), ReduceTransparency = Flag(root, "reduceTransparency"),
                TranslucentSidebar = Flag(root, "translucentSidebar") ?? true,
            };
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException)
        {
            return Default;
        }
    }

    /// <inheritdoc />
    public override string ToString() => System.Text.Encoding.UTF8.GetString(ToJson());
}

/// <summary>How a <see cref="GlassKnob"/>'s value is written (the canvas's fmt).</summary>
public enum GlassUnit
{
    /// <summary>Two places: 0.50.</summary>
    Plain,
    /// <summary>Two places and a times sign: 1.30×.</summary>
    Times,
    /// <summary>Whole pixels: 32 px.</summary>
    Pixels0,
    /// <summary>One place of pixels: 12.0 px.</summary>
    Pixels1,
    /// <summary>Two places of pixels: 0.40 px.</summary>
    Pixels2,
    /// <summary>A whole number: 50.</summary>
    Whole,
}

/// <summary>One value of <see cref="GlassSettings"/>: its id, the canvas's name and description, its range and step, and its unit.</summary>
public sealed record GlassKnob(string Id, string Name, string Description, double Min, double Max, double Step, GlassUnit Unit)
{
    /// <summary>The value held to the range (a value that is not a number: the low end).</summary>
    public double Hold(double value) => Unit == GlassUnit.Whole
        ? Math.Clamp(double.IsFinite(value) ? Math.Round(value) : Min, Min, Max)
        : Math.Clamp(double.IsFinite(value) ? value : Min, Min, Max);

    /// <summary>The value as the page shows it (the canvas's format).</summary>
    public string Format(double value) => Unit switch
    {
        GlassUnit.Times => value.ToString("F2", CultureInfo.InvariantCulture) + "\u00d7",
        GlassUnit.Pixels0 => Math.Round(value).ToString(CultureInfo.InvariantCulture) + " px",
        GlassUnit.Pixels1 => value.ToString("F1", CultureInfo.InvariantCulture) + " px",
        GlassUnit.Pixels2 => value.ToString("F2", CultureInfo.InvariantCulture) + " px",
        GlassUnit.Whole => Math.Round(value).ToString(CultureInfo.InvariantCulture),
        _ => value.ToString("F2", CultureInfo.InvariantCulture),
    };
}
