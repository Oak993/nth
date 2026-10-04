// PORT-ONLY, owner-ruled addition (OD-0092, OD-0098 / OD-0099 Q-V-1 C; first-release voice typing, VT-1): the first-run self-test's
// thresholds and ladders (voice-SPEC V-10.3). The thresholds are BENCH proposals, not measurements; they are named constants.
namespace Marrow.Core.Port.Voice;

/// <summary>How fast the timed transcription was (V-10.3).</summary>
public enum SelfTestBand { Fast, Slow, TooSlow }

/// <summary>What the self-test does next: keep the model, or step down one rung and time again.</summary>
public enum SelfTestAction { Keep, StepDown }

/// <summary>
/// The outcome of one timing. <see cref="ModelId"/> is the model to keep, or to install and time next. <see cref="LiveWords"/> is true
/// (RTF at most 0.15), false (too slow and no further step), or null when the answer to the slow-PC question decides (Q-V-1 C);
/// <see cref="AskUser"/> is true then.
/// </summary>
public sealed record SelfTestDecision(SelfTestAction Action, string ModelId, bool? LiveWords, bool AskUser);

/// <summary>The self-test rules (V-10.3).</summary>
public static class SelfTestPlan
{
    /// <summary>RTF at most this: keep, live words on.</summary>
    public const double LiveWordsMaxRtf = 0.15;

    /// <summary>RTF at most this (and above <see cref="LiveWordsMaxRtf"/>): keep, live words per the user's answer (Q-V-1 C). Above it: step down.</summary>
    public const double KeepMaxRtf = 0.5;

    /// <summary>The bundled clip's length in seconds (V-3.5): RTF = seconds / 10.</summary>
    public const double ClipSeconds = 10.0;

    private static readonly string[] Multilingual =
        ["large-v3-turbo-q8_0", "large-v3-turbo-q5_0", "medium-q8_0", "small-q8_0", "small-q5_1"];

    private static readonly string[] English = ["small.en-q8_0", "base.en", "tiny.en"];

    /// <summary>The real-time factor of a timed run: seconds over the clip's length.</summary>
    public static double Rtf(double seconds) => seconds / ClipSeconds;

    /// <summary>The band an RTF falls in (the edges 0.15 and 0.5 belong to the faster band).</summary>
    public static SelfTestBand BandOf(double rtf) =>
        rtf <= LiveWordsMaxRtf ? SelfTestBand.Fast : rtf <= KeepMaxRtf ? SelfTestBand.Slow : SelfTestBand.TooSlow;

    /// <summary>The ladder, best rung first (V-10.3).</summary>
    public static IReadOnlyList<string> Ladder(bool english) => english ? English : Multilingual;

    /// <summary>The next rung down the ladder the model is on; null at the bottom or for a model on no ladder.</summary>
    public static string? StepDown(string? modelId) => Neighbour(modelId, +1);

    /// <summary>The rung above (bigger); null at the top or for a model on no ladder.</summary>
    public static string? RungAbove(string? modelId) => Neighbour(modelId, -1);

    private static string? Neighbour(string? modelId, int offset)
    {
        if (modelId is null) return null;
        foreach (var ladder in new[] { Multilingual, English })
        {
            var i = Array.IndexOf(ladder, modelId);
            if (i < 0) continue;
            var j = i + offset;
            return j >= 0 && j < ladder.Length ? ladder[j] : null;
        }
        return null;
    }

    /// <summary>
    /// V-10.3 for one timing of <paramref name="modelId"/>. RTF at most 0.15: keep, live words on. Up to 0.5: keep and ask the user once
    /// (OD-0099 Q-V-1 C). Above 0.5: step down one rung to be timed again, once only: after a step (<paramref name="steppedDown"/>), or with
    /// no rung below, the model is kept with live words off and nothing to ask (ED: the question offers a smaller model for live words,
    /// which "once only" no longer allows).
    /// </summary>
    public static SelfTestDecision Decide(string modelId, double rtf, bool steppedDown)
    {
        switch (BandOf(rtf))
        {
            case SelfTestBand.Fast:
                return new SelfTestDecision(SelfTestAction.Keep, modelId, true, false);
            case SelfTestBand.Slow:
                return new SelfTestDecision(SelfTestAction.Keep, modelId, null, true);
            default:
                if (!steppedDown && StepDown(modelId) is { } next)
                    return new SelfTestDecision(SelfTestAction.StepDown, next, null, false);
                return new SelfTestDecision(SelfTestAction.Keep, modelId, false, false);
        }
    }
}
