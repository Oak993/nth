// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-11, voice-SPEC V-8.3): the caret after the dictated words.
using Marrow.UI.Port;

namespace Marrow.App.Port.Voice;

/// <summary>
/// The caret the dictation layer draws after the last word (V-8.3; DESIGN voice-composer.html:132-133): 1.5 px wide and 17 px high, in the text colour, blinking on the design's
/// <c>blink 1.06s ease-in-out infinite</c> keyframes (1 to 45 %, to 0 by 55 %, 0 to 95 %, back to 1 by 100 %). Under reduced motion the design's <c>animation:none</c> leaves it solid.
/// </summary>
internal static class DictationCaret
{
    public const double Width = 1.5;
    public const double Height = 17;
    public const double Period = 1.06;

    private static readonly Animation EaseInOut = Animation.TimingCurve(0.42, 0, 0.58, 1, 1);

    /// <summary>The caret's opacity <paramref name="seconds"/> into the blink (any origin: it repeats).</summary>
    public static double Opacity(double seconds, bool still)
    {
        if (still) return 1;
        var phase = seconds % Period / Period;
        if (phase < 0) phase += 1;
        if (phase <= 0.45) return 1;
        if (phase < 0.55) return 1 - EaseInOut.Progress((phase - 0.45) / 0.1);
        if (phase <= 0.95) return 0;
        return EaseInOut.Progress((phase - 0.95) / 0.05);
    }
}
