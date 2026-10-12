// PORT-ONLY, owner-ruled addition (OD-0092; first-release voice typing, VT-3): which language an Auto-detect session decodes in (ED-NEW-voicefix-4).
using Marrow.Core.Port.Voice.Seams;

namespace Marrow.Core.Port.Voice.Transcript;

/// <summary>
/// The language a pass decodes in, and whether its words may be shown and may settle the session (<see cref="Trusted"/>). Whisper told a language writes in it, so a
/// decision for English on Hindustani speech would write a translation: an untrusted decision is never shown while the session listens.
/// </summary>
public readonly record struct LanguageDecision(string Language, bool Trusted);

/// <summary>
/// ED-NEW-voicefix-4: Auto-detect never takes one Whisper guess on trust. Whisper's detection gives the top language with its probability and the more likely of Hindi and
/// Urdu (one spoken language, Hindustani, in two scripts) with its probability; this decides from them and from the Hindustani code the user has spoken before (in this
/// session, in an earlier one, or as the Windows display language).
/// <list type="number">
/// <item>The top is Hindi or Urdu: Hindustani, in the user's own script when there is one (Whisper often swaps the two), else the top's. Trusted.</item>
/// <item>The user has spoken Hindustani before: any other language must be decisive (<see cref="Decisive"/> or more with Hindi and Urdu under
/// <see cref="DecisiveRunnerUp"/>); anything less is Hindustani in the user's script. Trusted either way.</item>
/// <item>No Hindustani before: an unsure top (under <see cref="Sure"/>) with Hindi or Urdu a strong runner-up (<see cref="RunnerUp"/> or more) is Hindustani, trusted.
/// Otherwise the top, trusted only from <see cref="Sure"/>.</item>
/// </list>
/// </summary>
public static class AutoLanguagePolicy
{
    /// <summary>The two codes of Hindustani, the runners-up every detection is asked about.</summary>
    public static IReadOnlyList<string> Hindustani { get; } = ["hi", "ur"];

    /// <summary>Without Hindustani before, a top language this likely is trusted.</summary>
    public const float Sure = 0.80f;

    /// <summary>Without Hindustani before, Hindi or Urdu this likely behind an unsure top is Hindustani misheard.</summary>
    public const float RunnerUp = 0.10f;

    /// <summary>After Hindustani, another language needs this much...</summary>
    public const float Decisive = 0.90f;

    /// <summary>...with Hindi and Urdu under this.</summary>
    public const float DecisiveRunnerUp = 0.05f;

    public static bool IsHindustani(string? code) => code is "hi" or "ur";

    /// <summary>The decision for <paramref name="odds"/>. <paramref name="spokenBefore"/> is the Hindustani code the user has spoken before, or null.</summary>
    public static LanguageDecision Decide(LanguageOdds odds, string? spokenBefore)
    {
        ArgumentNullException.ThrowIfNull(odds);
        var before = IsHindustani(spokenBefore) ? spokenBefore : null;
        if (IsHindustani(odds.Top)) return new(before ?? odds.Top, true);
        var runnerUp = IsHindustani(odds.RunnerUp) ? odds.RunnerUp : null;
        var runnerUpProbability = runnerUp is null ? 0f : Probability(odds.RunnerUpProbability);
        var top = Probability(odds.TopProbability);
        if (before is not null)
            return top >= Decisive && runnerUpProbability < DecisiveRunnerUp ? new(odds.Top, true) : new(before, true);
        if (runnerUp is not null && top < Sure && runnerUpProbability >= RunnerUp) return new(runnerUp, true);
        return new(odds.Top, top >= Sure);
    }

    // a probability outside 0..1 (or NaN) from a native call is no evidence
    private static float Probability(float value) => float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 0f;
}
