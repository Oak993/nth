// PORT-ONLY, owner-ruled addition (OD-0091 fourth amendment; first-release voice typing, VT-12, voice-SPEC V-12.3): the label's text sweep as a function of time.
using Marrow.UI.Port;

namespace Marrow.App.Port.Voice;

/// <summary>
/// <c>@keyframes pill-sweep{0%,12%{background-position:135% 0}70%,100%{background-position:-55% 0}}</c> with <c>animation: pill-sweep 2.35s ease-in-out infinite</c>: the position is held at 135 %
/// for the first 12 % of the cycle and at -55 % for the last 30 %, and moves between them on ease-in-out (the timing function applies to the interval between keyframes).
/// </summary>
internal static class VoicePillSweep
{
    public const double Cycle = 2.35;
    public const double From = 1.35;
    public const double To = -0.55;
    private const double HoldStart = 0.12;
    private const double HoldEnd = 0.70;

    private static readonly Animation EaseInOut = Animation.TimingCurve(0.42, 0, 0.58, 1, (HoldEnd - HoldStart) * Cycle);

    /// <summary>The position after <paramref name="seconds"/> of an open pill (the cycle repeats).</summary>
    public static double Position(double seconds)
    {
        var inCycle = seconds <= 0 ? 0 : seconds % Cycle;
        var fraction = inCycle / Cycle;
        if (fraction <= HoldStart) return From;
        if (fraction >= HoldEnd) return To;
        return From + (To - From) * EaseInOut.Progress(inCycle - HoldStart * Cycle);
    }
}
