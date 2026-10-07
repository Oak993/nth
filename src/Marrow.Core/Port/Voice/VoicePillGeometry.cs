// PORT-ONLY, owner-ruled addition (OD-0091 fourth to sixth amendments, OD-0097; first-release voice typing, VT-12, voice-SPEC V-12.2, V-12.5, V-12.6): the numbers of the pill, the
// cross beside the orb and the text field's right edge. Pure: the views read them, and the tests read them without a view.
namespace Marrow.Core.Port.Voice;

/// <summary>
/// The geometry of the listening pill (DESIGN voice-composer.js:39, 811-834; voice-composer.html .vpill). Inline: height 40, font 13.5, opening to the left of the orb; float: height 44,
/// font 15, opening to the right, 52 above the composer's top, the orb lifted into it.
/// </summary>
public static class VoicePillGeometry
{
    /// <summary>The cross's own glass disc (24), its inset from the pill's near end (8) and the pill's padding at each end (8).</summary>
    public const double CrossSize = 24;

    public const double Pad = 8;

    /// <summary>The float pill's top, from the composer's top (V-12.2).</summary>
    public const double FloatTop = -52;

    /// <summary>The composer's own height in the design (52): where the orb stands, centred, before it lifts.</summary>
    public const double DesignComposerHeight = 52;

    /// <summary>The pill's height: inline 40, float 44. The Cross style draws no pill and has none.</summary>
    public static double Height(VoiceListeningStyle style) => style == VoiceListeningStyle.Float ? 44 : 40;

    /// <summary>The label's font size: inline 13.5, float 15.</summary>
    public static double FontSize(VoiceListeningStyle style) => style == VoiceListeningStyle.Float ? 15 : 13.5;

    /// <summary>Where the label starts, from the pill's left: inline 40, float 52 (the cross is at the right end there).</summary>
    public static double LabelLeft(VoiceListeningStyle style) => style == VoiceListeningStyle.Float ? 52 : 40;

    /// <summary>
    /// The open width, <c>H + 8 + labelWidth + nameWidth + 8 + 24 + 8</c> (V-12.2, V-12.10): the orb's cap, the pad, the label, the language name, the pad, the cross and the pad.
    /// <paramref name="labelWidth"/> and <paramref name="nameWidth"/> are measured, rounded up.
    /// </summary>
    public static double OpenWidth(VoiceListeningStyle style, double labelWidth, double nameWidth = 0) =>
        Height(style) + Pad + labelWidth + nameWidth + Pad + CrossSize + Pad;

    /// <summary>The closed width: a disc round the orb.</summary>
    public static double ClosedWidth(VoiceListeningStyle style) => Height(style);

    /// <summary>
    /// The pill's left edge from the orb's centre <paramref name="orbCentreX"/> at width <paramref name="width"/>: inline, the right end stays at <c>cx + H/2</c> and the pill opens to the left;
    /// float, the left end stays at <c>cx - H/2</c> and it opens to the right.
    /// </summary>
    public static double Left(VoiceListeningStyle style, double orbCentreX, double width) =>
        style == VoiceListeningStyle.Float ? orbCentreX - Height(style) / 2 : orbCentreX + Height(style) / 2 - width;

    /// <summary>How far the orb lifts when the float pill is fully open: <c>FloatTop + H/2 - 26</c>, -56 (V-12.2).</summary>
    public static double FloatLift => FloatTop + Height(VoiceListeningStyle.Float) / 2 - DesignComposerHeight / 2;

    /// <summary>The cross beside the orb, fully out: <c>-(D * 1.16 / 2 + 7)</c> from where it is tucked behind the orb (V-12.5).</summary>
    public static double CrossBesideOffset(double orbDiameter) => -(orbDiameter * ListenScale / 2 + 7);

    /// <summary>The orb's scale while listening (K.listenScale, V-11.4): the cross clears the grown ball.</summary>
    public const double ListenScale = 1.16;

    /// <summary>
    /// The text field's right edge (V-12.6) as the space the text column keeps free on its right, measured from the edge it has at rest (8 before the controls row, the row's own margin):
    /// the live inline pill stops the text 10 before the pill's left end (<c>W - D/2 - H/2 + 2</c> with the pill at <paramref name="openWidth"/>); the live cross, 10 before the cross
    /// (<c>26 - D/2 - xo</c>); the float pill, which stands above the composer, and rest leave it. <paramref name="orbDiameter"/> is the orb's slot.
    /// </summary>
    public static double TextRightInset(VoiceListeningStyle style, bool live, double orbDiameter, double openWidth)
    {
        if (!live) return 0;
        const double TextGap = 10;
        const double RowMargin = 8;
        return style switch
        {
            VoiceListeningStyle.Pill => Math.Max(0, openWidth - orbDiameter / 2 - Height(style) / 2 + TextGap - RowMargin),
            VoiceListeningStyle.Cross => Math.Max(0, TextGap + CrossSize - RowMargin - orbDiameter / 2 - CrossBesideOffset(orbDiameter)),
            _ => 0,
        };
    }
}
