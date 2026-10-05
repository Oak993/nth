// PORT-ONLY, owner-ruled addition (OD-0097, second amendment; first-release voice typing, VT-9, voice-SPEC V-13.1): the package's colour palettes `Va`
// (voice-glow.js:62-226, copied value for value; generated from the design file by a script and held to it by VoiceGlowPaletteTests). voice-glow 0.2.1 (Jakub Antalik, MIT).
namespace Marrow.UI.Port.Voice.Glow;

/// <summary>The seven colours of each variant, per theme (<c>Va[variant][dark|light]</c>), one per lobe.</summary>
internal static class VoiceGlowPalettes
{
    /// <summary>The variant names, in the design's order.</summary>
    public static IReadOnlyList<string> Variants { get; } = ["colorful", "mono", "ocean", "sunset", "forest", "candy", "ice", "gold"];

    /// <summary>The seven colours of a variant for a theme; an unknown variant throws, as <c>Va[u][...]</c> does in the package.</summary>
    public static IReadOnlyList<GlowRgb> Of(string variant, bool dark) =>
        (dark ? Dark : Light).TryGetValue(variant, out var colors) ? colors : throw new ArgumentOutOfRangeException(nameof(variant), variant, "not a voice-glow colour variant");

    private static readonly Dictionary<string, GlowRgb[]> Dark = new(StringComparer.Ordinal)
    {
        ["colorful"] = [new(255, 70, 120), new(60, 190, 255), new(175, 70, 255), new(60, 220, 130), new(255, 150, 40), new(90, 100, 255), new(40, 200, 190)],
        ["mono"] = [new(215, 215, 215), new(180, 180, 180), new(190, 190, 190), new(160, 160, 160), new(170, 170, 170), new(150, 150, 150), new(155, 155, 155)],
        ["ocean"] = [new(80, 140, 255), new(40, 200, 230), new(120, 90, 255), new(30, 170, 210), new(160, 80, 240), new(60, 110, 255), new(40, 190, 180)],
        ["sunset"] = [new(255, 110, 60), new(255, 180, 40), new(255, 60, 90), new(255, 210, 80), new(240, 70, 140), new(255, 140, 50), new(230, 50, 110)],
        ["forest"] = [new(70, 220, 120), new(40, 200, 180), new(140, 230, 80), new(30, 170, 140), new(190, 235, 70), new(50, 190, 110), new(30, 150, 120)],
        ["candy"] = [new(255, 90, 170), new(255, 120, 220), new(210, 80, 255), new(255, 150, 190), new(180, 110, 255), new(255, 70, 140), new(230, 100, 240)],
        ["ice"] = [new(150, 230, 255), new(90, 200, 255), new(190, 240, 255), new(120, 190, 255), new(160, 220, 250), new(80, 170, 255), new(200, 235, 255)],
        ["gold"] = [new(255, 200, 70), new(255, 170, 40), new(255, 220, 110), new(240, 150, 30), new(255, 235, 140), new(230, 160, 40), new(250, 210, 90)],
    };

    private static readonly Dictionary<string, GlowRgb[]> Light = new(StringComparer.Ordinal)
    {
        ["colorful"] = [new(255, 201, 21), new(126, 196, 255), new(180, 40, 230), new(235, 100, 160), new(255, 176, 122), new(154, 160, 255), new(127, 217, 238)],
        ["mono"] = [new(60, 60, 60), new(90, 90, 90), new(85, 85, 85), new(110, 110, 110), new(105, 105, 105), new(125, 125, 125), new(120, 120, 120)],
        ["ocean"] = [new(40, 100, 240), new(20, 160, 200), new(90, 60, 230), new(20, 130, 180), new(130, 50, 220), new(40, 80, 230), new(20, 150, 150)],
        ["sunset"] = [new(235, 80, 30), new(230, 150, 10), new(230, 30, 70), new(225, 175, 30), new(215, 40, 110), new(235, 110, 20), new(205, 30, 90)],
        ["forest"] = [new(30, 170, 80), new(20, 150, 130), new(90, 180, 30), new(20, 130, 100), new(130, 180, 20), new(30, 150, 80), new(20, 120, 90)],
        ["candy"] = [new(235, 40, 140), new(230, 70, 190), new(180, 40, 230), new(235, 100, 160), new(150, 70, 230), new(230, 30, 110), new(200, 60, 210)],
        ["ice"] = [new(30, 160, 220), new(20, 130, 210), new(60, 180, 230), new(40, 120, 220), new(50, 160, 220), new(20, 110, 220), new(70, 170, 230)],
        ["gold"] = [new(200, 140, 10), new(190, 120, 0), new(210, 160, 30), new(180, 110, 0), new(205, 170, 40), new(175, 115, 5), new(195, 150, 20)],
    };

}
