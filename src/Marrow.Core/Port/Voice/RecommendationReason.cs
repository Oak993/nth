// PORT-ONLY, owner-ruled addition (OD-0099 Q-V-4 A; first-release voice typing, VT-1): the recommendation's reason line, the two
// templates of voice-SPEC V-10.4 filled with this PC's measured times.
using System.Globalization;

namespace Marrow.Core.Port.Voice;

/// <summary>The reason line under the recommendation (V-10.4).</summary>
public static class RecommendationReason
{
    /// <summary>
    /// <c>t</c> = RTF x 10 rounded to 0.5 s (half up), shown with no trailing zeros. ENGINEERING DECISION: never less than 0.5, so a
    /// measured time is not shown as "0 seconds".
    /// </summary>
    public static string SecondsText(double rtf)
    {
        var seconds = Math.Max(0.5, Math.Floor(rtf * SelfTestPlan.ClipSeconds * 2 + 0.5) / 2);
        return seconds.ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// <c>{t} seconds</c>, and <c>1 second</c> for exactly one: lead ruling R4 (2026-10-04, from the OD-0099 Q-V-1 C amendment review),
    /// grammar only; the Q-V-4 A templates are otherwise unchanged (0.5 and 1.5 stay plural).
    /// </summary>
    public static string Seconds(double rtf)
    {
        var t = SecondsText(rtf);
        return t + (t == "1" ? " second" : " seconds");
    }

    /// <summary>GPU: <c>Your {GPU name} runs it on the graphics card: text about {t} seconds after you stop speaking.</c></summary>
    public static string Gpu(string gpuName, double rtf) =>
        "Your " + gpuName + " runs it on the graphics card: text about " + Seconds(rtf) + " after you stop speaking.";

    /// <summary>
    /// CPU: <c>On the processor alone {Model} answers in about {t} seconds; {Bigger model} would take {t2} seconds or more.</c> The model
    /// names are the catalogue's (V-9.6 without version and size). With no measured time for the bigger model (<paramref name="biggerModelId"/>
    /// or <paramref name="biggerRtf"/> null) the second clause is left out (V-10.4: "t2 ... measured or omitted"), ending the sentence after "seconds".
    /// </summary>
    public static string Cpu(string modelId, double rtf, string? biggerModelId, double? biggerRtf)
    {
        var head = "On the processor alone " + (ModelCatalog.ShortName(modelId) ?? modelId) + " answers in about " + Seconds(rtf);
        if (biggerModelId is null || biggerRtf is not { } biggerTime) return head + ".";
        return head + "; " + (ModelCatalog.ShortName(biggerModelId) ?? biggerModelId) + " would take " + Seconds(biggerTime) + " or more.";
    }
}
