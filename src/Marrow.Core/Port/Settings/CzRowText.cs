// PORT-ONLY CZ-4 (customization-SPEC CZ-6, CZ-7; DESIGN cz.js:506-575): the Customization page's detail lines, value formats and "is it still the default" tests for the Border beam and
// Uiverse light sections, as pure functions so the tests hold them to the locked cz.js's own rows (tests/fixtures/customization/page.generate.cjs). The labels and the fixed details are
// CzText's; what is here is every text that depends on a setting.
using Marrow.Core.Models.ComposerLights;

namespace Marrow.Core.Port.Settings;

public static class CzRowText
{
    /// <summary>CZ-8: the note under Style Off (cz.js:673, a single line).</summary>
    public const string OffNote = "The composer has no light. Your Border beam and Uiverse light settings are kept for when you turn one back on.";

    // ---------- the value formats (cz.js:509, 549) ----------

    /// <summary>JavaScript's <c>Math.round(v) + ''</c>: the half rounds up and negative zero prints 0.</summary>
    private static string Whole(double v)
    {
        var r = CzText.Round(v);
        return r == 0 ? "0" : CzText.Fmt(r, 0);
    }

    /// <summary><c>x2(v) = fmt(v, 2) + '×'</c>.</summary>
    public static string X2(double v) => CzText.Fmt(v, 2) + "×";

    /// <summary><c>deg(v) = round(v) + '°'</c>.</summary>
    public static string Deg(double v) => Whole(v) + "°";

    /// <summary><c>s(v) = fmt(v, 2) + ' s'</c> (the Border beam's Speed).</summary>
    public static string Seconds(double v) => CzText.Fmt(v, 2) + " s";

    /// <summary>The Uiverse light's Speed: <c>fmt(v, 1) + ' s'</c> (cz.js:560).</summary>
    public static string Seconds1(double v) => CzText.Fmt(v, 1) + " s";

    /// <summary><c>px(v) = fmt(v, 1) + ' px'</c>.</summary>
    public static string Px(double v) => CzText.Fmt(v, 1) + " px";

    /// <summary>The Border beam's <c>pc(v) = round(v * 100) + '%'</c> (a fraction).</summary>
    public static string Percent01(double v) => Whole(v * 100) + "%";

    /// <summary>The Uiverse light's <c>pc(v) = round(v) + '%'</c> (already a percentage).</summary>
    public static string Percent(double v) => Whole(v) + "%";

    /// <summary>Colour drift: <c>'±' + round(line ? min(13, v) : v) + '°'</c>.</summary>
    public static string Drift(double v, bool line) => "±" + Whole(line ? Math.Min(13, v) : v) + "°";

    /// <summary>Corner: <c>v + ' px'</c>.</summary>
    public static string Corner(double v) => CzText.Fmt(v, 4) + " px";

    // ---------- Border beam details (cz.js:511-543) ----------

    public static string MotionDetail(BeamFamily family) => family == BeamFamily.Rotate ? "Travels around the edge" : "Breathes in place";

    /// <summary>Speed: Auto, else the unit of the beam's turn (beamDetail: Line sweeps, Pulse breathes, the rest lap).</summary>
    public static string SpeedDetail(bool auto, BeamType type) =>
        auto ? "Auto: the designed pace" : "Seconds for " + (type == BeamType.Line ? "one sweep" : BorderBeamOptions.FamilyOf(type) == BeamFamily.Pulse ? "one breath" : "one lap");

    public static string BrightnessDetail(bool auto) => auto ? "Auto: set for the theme" : "Lighter or deeper colours";

    public static string SaturationDetail(bool auto) => auto ? "Auto: set for the theme" : "Vivid or muted colours";

    public static string RadiusDetail(bool auto) => auto ? "Auto: follows the composer" : "Rounder or squarer than the composer";

    /// <summary>Colour drift does nothing under Pulse (it runs through every hue), Mono or Hold colours still: dimmed and inert.</summary>
    public static bool DriftDimmed(BorderBeamOptions b) => b.Family == BeamFamily.Pulse || b.Palette == BeamPalette.Mono || b.StaticColors;

    public static string DriftDetail(BorderBeamOptions b) =>
        b.Family == BeamFamily.Pulse ? "Pulse always runs through every hue"
        : b.Palette == BeamPalette.Mono || b.StaticColors ? "Off while the colours hold still"
        : b.Type == BeamType.Line ? "How far the colours wander (Line keeps it under 13°)"
        : "How far the colours wander";

    public static string HoldDetail(bool mono) => mono ? "Mono always holds still" : "The palette stops drifting";

    public static string TunedDetail(BeamTheme theme) => theme switch
    {
        BeamTheme.Auto => "Matches the app’s theme",
        BeamTheme.Dark => "Always the dark recipe",
        _ => "Always the light recipe",
    };

    // ---------- Reset (cz.js:571-575) ----------

    /// <summary>Every key of the beam but <c>fit</c>, <c>css</c>, <c>faithful</c> and <c>active</c> equals the default (CZ-6.5).</summary>
    public static bool BeamIsDefault(BorderBeamOptions b) =>
        b with { Fitted = BorderBeamOptions.Default.Fitted, Faithful = BorderBeamOptions.Default.Faithful, Active = BorderBeamOptions.Default.Active } == BorderBeamOptions.Default;

    public static bool AuraIsDefault(UiverseAuraOptions a) => a == UiverseAuraOptions.Default;

    public static string BeamResetDetail(bool isDefault) => isDefault ? "Border beam as nth ships it" : "Return every Border beam setting to how it ships";

    public static string AuraResetDetail(bool isDefault) => isDefault ? "The Uiverse light as nth ships it" : "Return every Uiverse light setting to how it ships";

    // ---------- Uiverse details (cz.js:560-568) ----------

    public static string RimWidthDetail(bool rim) => rim ? "Thickness of the rim light" : "Turn on Rim light to use it";

    public static string DotCellDetail(bool dots) => dots ? "Finer or coarser dots" : "Turn on Halftone to use it";

    public static string DotStrengthDetail(bool dots) => dots ? "How visible the dots are" : "Turn on Halftone to use it";
}
