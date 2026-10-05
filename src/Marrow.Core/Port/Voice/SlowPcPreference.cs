// PORT-ONLY, owner-ruled addition (OD-0099 Q-V-1 C; first-release voice typing, VT-14): the slow-PC preference row of the Voice typing page, in the words of the welcome-voice set V2
// (E:\Droppy\work\design\decisions-2026-10-04\welcome-voice\src\wv_lib.py WORDS['V2']['slow']; BUILD-PLAN VT-14: "ED place: under the recommendation box, V2's words").
namespace Marrow.Core.Port.Voice;

/// <summary>The row's words, when it is offered, and what an answer does (voice-SPEC V-7.7, V-10.3; SelfTestPlan R1 to R8).</summary>
public static class SlowPcPreference
{
    public const string Title = "How should voice typing work here?";
    public const string Question = "Your PC can’t write your words live with the accurate model. Which matters more to you?";
    public const string LiveTitle = "See my words as I speak";
    public const string AccurateTitle = "Get every word right";
    public const string AccurateDetail = "Keeps the accurate model; your text appears when you stop speaking.";

    /// <summary>`Uses a smaller model ({small} more). ...`; <paramref name="smallerModelId"/> is the model the live answer takes, its size is {small}.</summary>
    public static string LiveDetail(string smallerModelId) =>
        "Uses a smaller model (" + ModelCatalog.SizeText(ModelCatalog.Find(smallerModelId)?.Bytes ?? 0) + " more). It hears languages other than English less well, Hindi and Urdu most.";

    /// <summary>
    /// The accurate model of the stored result: its <c>model</c>, or, when live words took the rung below (R6), the rung above it. Null with no stored model, or when the
    /// model has no rung above it to have come from.
    /// </summary>
    public static string? AccurateModelId(VoiceHardwareRecord? record)
    {
        if (record is null || record.Model.Length == 0) return null;
        return record.LiveWords ? SelfTestPlan.RungAbove(record.Model) : record.Model;
    }

    /// <summary>
    /// ENGINEERING DECISION (the spec places the row, not its conditions): the row is offered on a processor-only result that has a smaller rung to offer (R2), when the self-test
    /// measured it too slow for live words (a time was measured and live words are off: R1, nothing is asked of a GPU) or once an answer is stored, so it can be changed back.
    /// </summary>
    public static bool IsOffered(VoiceHardwareRecord? record, VoiceTypingSettings typing)
    {
        if (record is null || record.Backend != VoiceBackend.Cpu) return false;
        if (AccurateModelId(record) is not { } accurate || SelfTestPlan.StepDown(accurate) is null) return false;
        return typing.SlowChoice is not null || (record.Rtf > 0 && !record.LiveWords);
    }

    /// <summary>What an answer does for the accurate model (R6): Live takes the next rung down with live words on; Accurate keeps the model, text after the stop.</summary>
    public static SelfTestDecision Apply(string accurateModelId, LiveWordsChoice answer) => SelfTestPlan.ApplyAnswer(accurateModelId, answer);

    /// <summary>The option shown as chosen: the stored answer, else the accurate model, which is what R8 keeps until the user answers.</summary>
    public static LiveWordsChoice Shown(VoiceTypingSettings typing) => typing.SlowChoice ?? LiveWordsChoice.Accurate;
}
