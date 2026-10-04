// PORT-ONLY, owner-ruled addition (OD-0092, OD-0098 / OD-0099 Q-V-1 C; first-release voice typing, VT-1): the first-run self-test's
// thresholds and ladders (voice-SPEC V-10.3). The thresholds are BENCH proposals, not measurements; they are named constants.
namespace Marrow.Core.Port.Voice;

/// <summary>How fast the timed transcription was (V-10.3).</summary>
public enum SelfTestBand { Fast, Slow, TooSlow }

/// <summary>What the self-test does next: keep the model, or step down one rung and time again.</summary>
public enum SelfTestAction { Keep, StepDown }

/// <summary>
/// The outcome of one timing. <see cref="ModelId"/> is the model to keep, or to install and time next. <see cref="LiveWords"/> is true
/// (live words on), false (text after the user stops), or null when the answer to the slow-PC question decides (Q-V-1 C; also for a
/// step down, which is timed again). <see cref="AskUser"/> is true when that question is to be asked now; null live words with no
/// question means the answer already stored for this hardware key decides.
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
    /// V-10.3 for one timing of <paramref name="modelId"/>, with the lead's rulings R1 to R3 (2026-10-04, from the OD-0099 Q-V-1 C amendment:
    /// "when the speed test finds the PC too slow to run the accurate model live, the app asks the user once ... a fast PC (every GPU) is
    /// never asked and stays live with the accurate model").
    /// <list type="bullet">
    /// <item>R1: a timing on a working GPU backend (<paramref name="basis"/> Gpu) keeps the model with live words on and asks nothing, at any RTF.</item>
    /// <item>The processor: RTF at most 0.15 keeps the model with live words on. Above 0.5, before any step (<paramref name="steppedDown"/>
    /// false) and with a rung below, steps down one rung to be timed again (once only).</item>
    /// <item>Otherwise the processor is too slow for live words. R3: with a smaller rung on the model's ladder the model is kept and the
    /// slow-PC question is asked, also after the step, unless it was already answered for this hardware key
    /// (<paramref name="answeredForThisKey"/>, see <see cref="AnsweredFor"/>): then the stored answer decides and nothing is asked.
    /// R2: with no smaller rung (the bottom of a ladder, or a model on none) the model is kept with text after the user stops (live words
    /// off) and nothing is asked.</item>
    /// </list>
    /// </summary>
    public static SelfTestDecision Decide(string modelId, double rtf, bool steppedDown, RecommendationBasis basis, bool answeredForThisKey)
    {
        if (basis == RecommendationBasis.Gpu)
            return new SelfTestDecision(SelfTestAction.Keep, modelId, true, false);
        var band = BandOf(rtf);
        if (band == SelfTestBand.Fast)
            return new SelfTestDecision(SelfTestAction.Keep, modelId, true, false);
        var smaller = StepDown(modelId);
        if (band == SelfTestBand.TooSlow && !steppedDown && smaller is not null)
            return new SelfTestDecision(SelfTestAction.StepDown, smaller, null, false);
        if (smaller is null)
            return new SelfTestDecision(SelfTestAction.Keep, modelId, false, false);
        return new SelfTestDecision(SelfTestAction.Keep, modelId, null, !answeredForThisKey);
    }

    /// <summary>
    /// R3's "the same hardware key": the slow-PC question counts as answered for this PC when an answer is stored
    /// (<c>voiceTyping.liveWordsChoice</c>) and the stored check (<c>voiceHardware</c>) has today's key (V-10.5). A new GPU, driver or app
    /// build (a stale record) asks again.
    /// </summary>
    public static bool AnsweredFor(VoiceHardwareRecord? stored, string currentKey, VoiceTypingSettings typing) =>
        typing.SlowChoice is not null && !HardwareKey.IsStale(stored, currentKey);
}
