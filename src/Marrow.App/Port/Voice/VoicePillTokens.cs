// PORT-ONLY, owner-ruled addition (OD-0091 fifth amendment, OD-0097; first-release voice typing, VT-12, voice-SPEC V-12.4): the pill's colours, by appearance.
using Avalonia.Media;
using Avalonia.Styling;

namespace Marrow.App.Port.Voice;

/// <summary>
/// The glass tokens of the pill, the cross and the language question (V-12.4's table; DESIGN voice-composer.html:22, 35), and the composer-window tokens the cross beside the orb wears
/// (<c>hoverfill</c>, <c>pressfill</c>, <c>winedge</c>, <c>wink</c>, <c>wink3</c>). One set for each appearance.
/// </summary>
internal sealed record VoicePillTokens(
    Color PgBg, Color PgRim, Color PgEdge, Color PgHi, Color PgShade, Color PgLab, Color PgLabHi, Color PgX, Color PgXHover, Color PgXBg, Color PgXRim,
    Color HoverFill, Color PressFill, Color WinEdge, Color Wink, Color Wink3)
{
    public static VoicePillTokens Light { get; } = new(
        PgBg: Color.FromArgb(0x8F, 250, 249, 251), PgRim: Color.FromArgb(0xBF, 255, 255, 255), PgEdge: Color.FromArgb(0x24, 40, 30, 60), PgHi: Color.FromArgb(0xD9, 255, 255, 255),
        PgShade: Color.FromArgb(0x1A, 60, 40, 30), PgLab: Color.FromRgb(0x55, 0x56, 0x5B), PgLabHi: Color.FromRgb(0x16, 0x16, 0x1A), PgX: Color.FromRgb(0x4A, 0x4B, 0x50),
        PgXHover: Color.FromRgb(0x16, 0x16, 0x1A), PgXBg: Color.FromArgb(0x8C, 255, 255, 255), PgXRim: Color.FromArgb(0x1F, 40, 30, 60),
        HoverFill: Color.FromArgb(0x12, 28, 28, 30), PressFill: Color.FromArgb(0x1F, 28, 28, 30), WinEdge: Color.FromArgb(0x1F, 0, 0, 0), Wink: Color.FromRgb(0x1C, 0x1C, 0x1E),
        Wink3: Color.FromArgb(0x99, 28, 28, 30));

    public static VoicePillTokens Dark { get; } = new(
        PgBg: Color.FromArgb(0x80, 18, 18, 22), PgRim: Color.FromArgb(0x29, 255, 255, 255), PgEdge: Color.FromArgb(0x59, 0, 0, 0), PgHi: Color.FromArgb(0x38, 255, 255, 255),
        PgShade: Color.FromArgb(0x38, 0, 0, 0), PgLab: Color.FromRgb(0x8D, 0x8E, 0x94), PgLabHi: Color.FromRgb(0xF7, 0xF7, 0xF8), PgX: Color.FromRgb(0xB4, 0xB5, 0xBA),
        PgXHover: Color.FromRgb(0xF7, 0xF7, 0xF8), PgXBg: Color.FromArgb(0x17, 255, 255, 255), PgXRim: Color.FromArgb(0x29, 255, 255, 255),
        HoverFill: Color.FromArgb(0x12, 236, 236, 236), PressFill: Color.FromArgb(0x1F, 236, 236, 236), WinEdge: Color.FromArgb(0x1A, 255, 255, 255), Wink: Color.FromRgb(0xEC, 0xEC, 0xEC),
        Wink3: Color.FromArgb(0x9E, 236, 236, 236));

    /// <summary>The set for an appearance.</summary>
    public static VoicePillTokens For(ThemeVariant? variant) => LookDraw.IsDark(variant) ? Dark : Light;
}
