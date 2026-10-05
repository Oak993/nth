// PORT-ONLY, owner-ruled addition (OD-0106 Countdown C, its amendments and its OKLCH glide; OD-0099 Q-V-6 B; first-release voice typing, VT-17): the three colours of a theme.
namespace Marrow.App.Port.Voice.Countdown;

/// <summary>
/// The line's colours in one theme. Neutral is the pill's own label tone (the locked voice design's <c>--pgLab</c>, drawn at 55 % as the page draws the draining line:
/// <c>color-mix(in srgb, var(--pgLab) 55%, transparent)</c>). Warning is the voice design's warning colour (<c>--vwarn</c>, the colour of the blocked note's mark: the app's
/// other warning mark in the composer). Danger is the app's error red (AppTheme's fixed systemRed, #FF3B30: the voice design has no red of its own).
/// ENGINEERING DECISION (ED-NEW-cloud-vt-17): these are the tokens OD-0106 asked the lead to record.
/// </summary>
public sealed record CountdownPalette(CountdownColour Neutral, CountdownColour Warning, CountdownColour Danger)
{
    public static CountdownPalette Dark { get; } = new(Hex(0x8D8E94, 0.55), Hex(0xE0A84A), Hex(0xFF3B30));

    public static CountdownPalette Light { get; } = new(Hex(0x55565B, 0.55), Hex(0xB7791F), Hex(0xFF3B30));

    private static CountdownColour Hex(int rgb, double alpha = 1.0) => new(((rgb >> 16) & 0xFF) / 255.0, ((rgb >> 8) & 0xFF) / 255.0, (rgb & 0xFF) / 255.0, alpha);
}
