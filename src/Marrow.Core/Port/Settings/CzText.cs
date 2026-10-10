// PORT-ONLY (OD-0103): locked Customization text and numeric helpers, CZ-5..8 and CZ-12.10/11.
using System.Globalization;

namespace Marrow.Core.Port.Settings;

public static class CzText
{
    // CZ-9, OD-0091/OD-0097: the locked voice rows (voicecmp.js:207-222, voiceopts.js:8-40, 237-250).
    public const string VoiceHeading = "Voice orb";
    public const string VoiceNote = "Click the orb in the preview to try it: it listens until you click it again (the cross or Esc cancels), then the words arrive.";
    public const string WordsDimDetail = "Cross beside the orb has no pill, so no words are shown";
    public const string LightDimDetail = "No effect while the composer light is Off";
    public static readonly IReadOnlyDictionary<string, (string Id, string Text, string Detail)[]> VoiceOptions = new Dictionary<string, (string, string, string)[]>
    {
        ["Orb"] = [("siri", "Siri Wave", "Siri Wave, the default"), ("membrane", "Voice Membrane", "Voice Membrane")],
        ["Listening style"] = [("pill", "Pill in the composer", "The orb opens into a glass pill that says what it hears; the cross at its far end cancels"), ("cross", "Cross beside the orb", "A cross slides out beside the orb to cancel; the text field shows the words"), ("float", "Floating pill", "The orb lifts into a glass pill above the composer, the text keeps its full width")],
        ["Pill words"] = [("L1", "Listening… · Transcribing… · Finishing…", "What the app is doing, plainly"), ("L2", "Listening… · Writing… · Finishing…", "Writing says what you see: the words appearing in the composer"), ("L3", "Hearing you… · Writing… · Tidying up…", "Warmer and personal"), ("L4", "Speak now · Transcribing… · Almost done…", "An invitation in silence, then what the app is doing"), ("L5", "Listening… · Typing… · Wrapping up…", "Names the composer’s own state: it is typing"), ("L6", "Listening · Transcribing · Finishing", "Without the ellipses: quieter, and the pill stays narrower")],
        ["Orb size"] = [("32", "32 px", "32 px, the default"), ("26", "26 px", "26 px, a quieter orb")],
        ["Attachment button"] = [("in", "Inside the composer", "At the composer’s left, inside it"), ("out", "Outside", "In the margin beside the composer")],
        ["Composer light while you speak"] = [("away", "Steps aside", "The light fades out as you start speaking and returns when the text is done: one light on the composer at a time"), ("dim", "Dims to 35 %", "The light keeps running at 35 % under the voice glow"), ("stay", "Stays on", "The light keeps running at full strength under the voice glow")],
        ["Spoken language"] = [("auto", "Auto-detect, silent", "Whisper detects the language each time; nothing is shown"), ("show", "Auto-detect, named in the pill", "Whisper detects the language, and the pill names it once it is heard, so a wrong guess shows at once"), ("windows", "Windows display language", "Windows’ display language is not in the list, so Whisper detects the language"), ("ask", "Asked on first use", "The first click on the orb asks which language you will speak; the answer is kept on the Voice typing page")],
        ["After voice input"] = [("leave", "Leave the text in the composer", "The words wait in the composer until you press send"), ("send", "Send it as the prompt", "The words go out as your prompt as soon as they arrive")],
    };

    public static string SpokenLanguageDetail(Marrow.Core.Port.Voice.VoiceLangMode mode, string? picked, string? windowsUiLanguage)
    {
        var language = Marrow.Core.Port.Voice.VoiceLanguage.Find(picked);
        if (language is not null) return language.Name + " is set on the Voice typing page and comes first; picking here clears it";
        if (mode != Marrow.Core.Port.Voice.VoiceLangMode.Windows) return VoiceOptions["Spoken language"][(int)mode].Detail;
        var code = Marrow.Core.Port.Voice.VoiceLanguage.WindowsLanguageCode(windowsUiLanguage);
        return code is null ? "Windows’ display language is not in the list, so Whisper detects the language"
            : Marrow.Core.Port.Voice.VoiceLanguage.Find(code)!.Name + ", Windows’ display language, from the start; Auto-detect stays one click away on the Voice typing page";
    }

    // Exact literal tokens of CZ-5..8 (including choices, units and format templates); generated from the locked tables.
    public static IReadOnlyList<string> TableText { get; } = Array.AsReadOnly(new string[]
    {
        "Style",
        "Light",
        "beam",
        "Border beam",
        "A beam of colour travels the edge",
        "aura",
        "Uiverse light",
        "A soft two-tone glow turns around it",
        "off",
        "Off",
        "No light around the composer",
        "cardfill",
        "tile",
        "tilesel",
        "sel",
        "composerLight.style",
        "x2(v) = fmt(v, 2) + '×'",
        "deg(v) = round(v) + '°'",
        "s(v) = fmt(v, 2) + ' s'",
        "px(v) = fmt(v, 1) + ' px'",
        "pc(v) = round(v * 100) + '%'",
        "round",
        "Math.round",
        "BorderBeamResolved.AutoValues(beam, appDark, w, 46, 22)",
        "w",
        "h",
        "Look",
        "Motion",
        "Rotate",
        "Pulse",
        "family",
        "size",
        "md",
        "pulse-inner",
        "BorderBeamOptions.WithFamily",
        "Travels around the edge",
        "Breathes in place",
        "Shape",
        "Large",
        "Line",
        "Small",
        "Inside",
        "Outside",
        "A wide beam with an inner glow",
        "A short streak glides along the edge",
        "A slimmer beam, a tighter glow",
        "Colour pools just inside the edge",
        "A glow spills out past the edge",
        "Colours",
        "color",
        "colorful",
        "Colourful",
        "mono",
        "Mono",
        "ocean",
        "Ocean",
        "sunset",
        "Sunset",
        "forest",
        "Forest",
        "candy",
        "Candy",
        "ice",
        "Ice",
        "gold",
        "Gold",
        "BorderBeamTables",
        "border",
        "Pace and strength",
        "Speed",
        "duration",
        "s",
        "Auto: the designed pace",
        "Seconds for one lap",
        "Seconds for one sweep",
        "Seconds for one breath",
        "Brightness",
        "brightness",
        "x2",
        "Auto: set for the theme",
        "Lighter or deeper colours",
        "Intensity",
        "strength",
        "pc",
        "How strongly the light shows",
        "Follow the composer",
        "respond",
        "Softer at rest, fuller as you type and while a turn runs",
        "Advanced",
        "Colour recipe, glow layers and corner",
        "Show",
        "Hide",
        "Colour shift",
        "hueShift",
        "deg",
        "Turns every colour around the wheel",
        "Saturation",
        "saturation",
        "Vivid or muted colours",
        "Colour drift",
        "hueRange",
        "'±' + round(line ? min(13, v) : v) + '°'",
        "Pulse always runs through every hue",
        "Off while the colours hold still",
        "How far the colours wander (Line keeps it under 13°)",
        "How far the colours wander",
        "Hold colours still",
        "staticColors",
        "Mono always holds still",
        "The palette stops drifting",
        "Glow size",
        "glowSize",
        "Softness of every glow",
        "Edge line",
        "strokeX",
        "The thin line on the edge",
        "Inner glow",
        "innerX",
        "The wash inside the edge",
        "Outer glow",
        "bloomX",
        "The bloom beyond the edge",
        "Corner",
        "radius",
        "v + ' px'",
        "Auto: follows the composer",
        "Rounder or squarer than the composer",
        "Tuned for",
        "theme",
        "Auto",
        "Dark",
        "Matches the app’s theme",
        "Always the dark recipe",
        "Always the light recipe",
        "Outside glow",
        "Core softness",
        "coreBlur",
        "px",
        "The bright glow near the edge",
        "Bloom softness",
        "bloomBlur",
        "The wide glow around it",
        "Glow width",
        "glowX",
        "clamp(w / 350, 0.35, 4)",
        "How far it spreads sideways",
        "Glow height",
        "glowY",
        "clamp(46 / 140, 0.35, 4)",
        "How far it spreads up and down",
        "Glow boost",
        "boost",
        "Extra brightness in the glow",
        "Flare",
        "spike",
        "How much the streak flares as it goes",
        "Defaults",
        "Border beam as Nth ships it",
        "BorderBeamOptions.Default",
        "fit",
        "faithful",
        "active",
        "Return every Border beam setting to how it ships",
        "Reset to defaults",
        "Beam = BorderBeamOptions.Default",
        "Library defaults",
        "Calm for the composer",
        "pc(v) = round(v) + '%'",
        "Original",
        "Model",
        "Accent",
        "Custom",
        "scheme",
        "Orange and blue",
        "The model in use: {maker name}",
        "Your accent: {accent name}",
        "Two colours you pick",
        "Your colours",
        "One side",
        "The other",
        "First colour",
        "Second colour",
        "c1",
        "c2",
        "One for each side of the glow",
        "Rim light",
        "rim",
        "A bright line hugging the edge",
        "Halftone",
        "dots",
        "The glow seen through fine dots",
        "{maker name}",
        "{accent name}",
        "AccentPalette",
        "speed",
        "fmt(v, 1) + ' s'",
        "Seconds for one turn",
        "Breathing",
        "breathing",
        "The glow slowly swells and settles",
        "Brightness by moment",
        "At rest",
        "rest",
        "While the composer waits",
        "While typing",
        "typing",
        "As you write a message",
        "While a turn runs",
        "running",
        "As the agent works",
        "Halo, rim and dot sizes",
        "Halo size",
        "haloSize",
        "How far the glow reaches",
        "Halo softness",
        "haloBlur",
        "How soft its outer edge is",
        "Rim width",
        "rimW",
        "Thickness of the rim light",
        "Turn on Rim light to use it",
        "Dot spacing",
        "dotCell",
        "Finer or coarser dots",
        "Turn on Halftone to use it",
        "Dot strength",
        "dotStr",
        "How visible the dots are",
        "The Uiverse light as Nth ships it",
        "UiverseAuraOptions",
        "Default",
        "Return every Uiverse light setting to how it ships",
        "Aura = UiverseAuraOptions.Default",
        "The composer has no light. Your Border beam and Uiverse light settings are kept for when you turn one back on.",
    });

    public static IReadOnlyDictionary<string, string> StyleTitles { get; } = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string>
    {
        ["beam"] = "Border beam",
        ["aura"] = "Uiverse light",
        ["off"] = "Off",
    });
    public static IReadOnlyDictionary<string, string> StyleDescriptions { get; } = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string>
    {
        ["beam"] = "A beam of colour travels the edge",
        ["aura"] = "A soft two-tone glow turns around it",
        ["off"] = "No light around the composer",
    });
    public static IReadOnlyDictionary<string, string> PaletteNames { get; } = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string>
    {
        ["colorful"] = "Colourful",
        ["mono"] = "Mono",
        ["ocean"] = "Ocean",
        ["sunset"] = "Sunset",
        ["forest"] = "Forest",
        ["candy"] = "Candy",
        ["ice"] = "Ice",
        ["gold"] = "Gold",
    });
    public static IReadOnlyDictionary<string, string> ShapeDetails { get; } = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string>
    {
        ["md"] = "A wide beam with an inner glow",
        ["line"] = "A short streak glides along the edge",
        ["sm"] = "A slimmer beam, a tighter glow",
        ["pulse-inner"] = "Colour pools just inside the edge",
        ["pulse-outside"] = "A glow spills out past the edge",
    });

    // DESIGN cz.js:557: these details include the current maker/accent names.
    public static string AuraColours(string scheme, string makerName, string accentName) => scheme switch
    {
        "original" => "Orange and blue", "maker" => "The model in use: " + makerName,
        "accent" => "Your accent: " + accentName, "custom" => "Two colours you pick",
        _ => throw new ArgumentOutOfRangeException(nameof(scheme)),
    };

    // JavaScript Math.round rounds ties toward +infinity; preserve already integral large doubles.
    public static double Round(double value)
    {
        if (!double.IsFinite(value) || Math.Abs(value) >= 4503599627370496) return value;
        var floor = Math.Floor(value);
        return value - floor >= .5 ? floor + 1 : floor;
    }

    // CZ-12.10: JS Number.toString's decimal/exponent thresholds, shortest round trip, negative zero prints 0.
    public static string Fmt(double value, int digits)
    {
        var power = Math.Pow(10, digits);
        var v = Round(value * power) / power;
        if (v == 0) return "0";
        if (double.IsNaN(v)) return "NaN";
        if (double.IsPositiveInfinity(v)) return "Infinity";
        if (double.IsNegativeInfinity(v)) return "-Infinity";
        var raw = v.ToString("R", CultureInfo.InvariantCulture);
        var e = raw.IndexOf('E', StringComparison.Ordinal);
        if (e < 0) return raw;
        var exponent = int.Parse(raw.AsSpan(e + 1), CultureInfo.InvariantCulture);
        var mantissa = raw[..e];
        if (Math.Abs(v) >= 1e-6 && Math.Abs(v) < 1e21)
        {
            var negative = mantissa.StartsWith('-');
            if (negative) mantissa = mantissa[1..];
            var dot = mantissa.IndexOf('.', StringComparison.Ordinal);
            var decimalAt = (dot < 0 ? mantissa.Length : dot) + exponent;
            var numbers = mantissa.Replace(".", "", StringComparison.Ordinal);
            var expanded = decimalAt <= 0 ? "0." + new string('0', -decimalAt) + numbers
                : decimalAt >= numbers.Length ? numbers + new string('0', decimalAt - numbers.Length)
                : numbers.Insert(decimalAt, ".");
            return negative ? "-" + expanded : expanded;
        }
        return mantissa + "e" + (exponent >= 0 ? "+" : "-") + Math.Abs(exponent).ToString(CultureInfo.InvariantCulture);
    }

    // CZ-12.2.4: clamp before quantizing, not after; the design's toFixed(4) operates on the binary result.
    public static double Snap(double value, double min, double max, double step) =>
        double.Parse((Round(Math.Clamp(value, min, max) / step) * step).ToString("F4", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    // DESIGN cz.js:107-116; the t += .05 accumulation must remain binary floating point.
    public static string Tone(string hex, bool dark, string title)
    {
        bool Legible(string color) => dark ? Luminance(color) >= .12 : Luminance(color) <= .42;
        if (Legible(hex)) return hex;
        var target = Legible(title) ? title : dark ? "#FFFFFF" : "#1C1C1E";
        for (var t = .1; t < 1; t += .05)
        {
            var color = MixHex(hex, target, t);
            if (Legible(color)) return color;
        }
        return target;
    }

    // CZ-12.11: the page's mixHex is the playground's (composer-lights logic.js:44: Math.round(x + (b - x) * t)); the port of it exists.
    private static string MixHex(string a, string b, double t) => Marrow.Core.Models.ComposerLights.BorderBeamMath.MixHex(a, b, t);

    private static double Luminance(string hex)
    {
        static double Channel(ReadOnlySpan<char> text)
        {
            var v = int.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return v <= .03928 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4);
        }
        return .2126 * Channel(hex.AsSpan(1, 2)) + .7152 * Channel(hex.AsSpan(3, 2)) + .0722 * Channel(hex.AsSpan(5, 2));
    }
}
