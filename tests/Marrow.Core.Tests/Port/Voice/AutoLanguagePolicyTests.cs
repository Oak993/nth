using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Port.Voice.Transcript;

namespace Marrow.Core.Tests.Port.Voice;

// ED-NEW-voicefix-4: Auto-detect never takes one Whisper guess on trust. The owner said "Haan bhai, kya chal raha hai aajkal" and got "Abi, what is going on?": Whisper
// told "en" for Hindustani speech writes a translation. The policy decides from Whisper's probabilities and from the Hindustani the user has spoken before.
public sealed class AutoLanguagePolicyTests
{
    private static LanguageOdds Odds(string top, float p, string? runnerUp = null, float q = 0f) => new(top, p, runnerUp, q);

    [Theory]
    [InlineData("hi", null, "hi")]
    [InlineData("ur", null, "ur")]
    [InlineData("ur", "hi", "hi")] // Whisper swaps the two scripts of one spoken language: the user's own script is kept
    [InlineData("hi", "ur", "ur")]
    [Trait("Feature", "VT-3")]
    [Trait("Decision", "ED-NEW-voicefix-4")]
    public void A_Hindi_or_Urdu_top_is_Hindustani_in_the_users_script_and_trusted(string top, string? before, string expected)
    {
        foreach (var p in new[] { 0.05f, 0.3f, 0.99f })
            Assert.Equal(new LanguageDecision(expected, true), AutoLanguagePolicy.Decide(Odds(top, p, top, p), before));
    }

    [Theory]
    [InlineData(0.55f, "hi", 0.30f, "hi")]
    [InlineData(0.79f, "ur", 0.10f, "ur")]
    [InlineData(0.40f, "hi", 0.12f, "hi")]
    [Trait("Feature", "VT-3")]
    [Trait("Decision", "ED-NEW-voicefix-4")]
    public void An_unsure_English_with_Hindi_or_Urdu_a_strong_runner_up_is_decoded_as_Hindustani(float english, string runnerUp, float runnerUpProbability, string expected)
    {
        var decision = AutoLanguagePolicy.Decide(Odds("en", english, runnerUp, runnerUpProbability), null);
        Assert.Equal(new LanguageDecision(expected, true), decision);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Decision", "ED-NEW-voicefix-4")]
    public void Without_Hindustani_before_English_is_trusted_only_when_Whisper_is_sure_of_it()
    {
        Assert.Equal(new LanguageDecision("en", true), AutoLanguagePolicy.Decide(Odds("en", 0.95f, "hi", 0.02f), null));
        Assert.Equal(new LanguageDecision("en", true), AutoLanguagePolicy.Decide(Odds("en", 0.80f, "hi", 0.15f), null)); // sure: the runner-up does not decide
        Assert.Equal(new LanguageDecision("en", false), AutoLanguagePolicy.Decide(Odds("en", 0.70f, "hi", 0.05f), null)); // unsure, and Hindustani is weak
        Assert.Equal(new LanguageDecision("en", false), AutoLanguagePolicy.Decide(Odds("en", 0.50f), null));
        Assert.Equal(new LanguageDecision("es", true), AutoLanguagePolicy.Decide(Odds("es", 0.92f, "hi", 0.01f), null));
        Assert.Equal(new LanguageDecision("pa", false), AutoLanguagePolicy.Decide(Odds("pa", 0.60f, "hi", 0.05f), null));
    }

    [Theory]
    [InlineData("hi")]
    [InlineData("ur")]
    [Trait("Feature", "VT-3")]
    [Trait("Decision", "ED-NEW-voicefix-4")]
    public void After_Hindustani_only_a_decisive_other_language_is_not_decoded_as_Hindustani(string before)
    {
        Assert.Equal(new LanguageDecision(before, true), AutoLanguagePolicy.Decide(Odds("en", 0.89f, "hi", 0.01f), before));
        Assert.Equal(new LanguageDecision(before, true), AutoLanguagePolicy.Decide(Odds("en", 0.95f, "ur", 0.05f), before));
        Assert.Equal(new LanguageDecision(before, true), AutoLanguagePolicy.Decide(Odds("en", 0.60f), before));
        Assert.Equal(new LanguageDecision(before, true), AutoLanguagePolicy.Decide(Odds("pa", 0.70f, "hi", 0.20f), before));
        Assert.Equal(new LanguageDecision("en", true), AutoLanguagePolicy.Decide(Odds("en", 0.97f, "hi", 0.01f), before));
        Assert.Equal(new LanguageDecision("en", true), AutoLanguagePolicy.Decide(Odds("en", 0.90f, "ur", 0.049f), before));
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Decision", "ED-NEW-voicefix-4")]
    public void Probabilities_that_are_not_probabilities_and_runners_up_that_are_not_Hindustani_are_no_evidence()
    {
        Assert.Equal(new LanguageDecision("en", false), AutoLanguagePolicy.Decide(Odds("en", float.NaN, "hi", float.NaN), null));
        Assert.Equal(new LanguageDecision("en", false), AutoLanguagePolicy.Decide(Odds("en", -1f, "hi", 0.05f), null));
        Assert.Equal(new LanguageDecision("en", true), AutoLanguagePolicy.Decide(Odds("en", 7f, "hi", 0.01f), null)); // clamped to 1
        Assert.Equal(new LanguageDecision("en", false), AutoLanguagePolicy.Decide(Odds("en", 0.5f, "pa", 0.4f), null)); // only Hindi or Urdu is a runner-up
        Assert.Equal(new LanguageDecision("hi", true), AutoLanguagePolicy.Decide(Odds("en", 0.5f, "hi", float.PositiveInfinity), "hi")); // a bad runner-up is 0: still not decisive
        Assert.Equal(new LanguageDecision("en", true), AutoLanguagePolicy.Decide(Odds("en", 0.95f), "xx")); // a code that is not Hindustani is no history
    }

    // Over a grid of every odds Whisper can give: the policy never decodes as English what it should not, and only ever decodes in the top, Hindi or Urdu.
    [Theory]
    [InlineData(null)]
    [InlineData("hi")]
    [InlineData("ur")]
    [Trait("Feature", "VT-3")]
    [Trait("Decision", "ED-NEW-voicefix-4")]
    public void Over_every_odds_English_is_decoded_only_under_the_policys_rule(string? before)
    {
        for (var p = 0; p <= 100; p++)
        for (var q = 0; q <= 100 - p; q++)
        foreach (var runnerUp in new[] { "hi", "ur" })
        {
            var top = p / 100f;
            var second = q / 100f;
            var decision = AutoLanguagePolicy.Decide(Odds("en", top, runnerUp, second), before);
            Assert.Contains(decision.Language, new[] { "en", "hi", "ur" });
            if (decision.Language == "en")
            {
                if (before is null) Assert.True(top >= AutoLanguagePolicy.Sure || second < AutoLanguagePolicy.RunnerUp, $"en at {top}/{second}");
                else Assert.True(top >= AutoLanguagePolicy.Decisive && second < AutoLanguagePolicy.DecisiveRunnerUp, $"en at {top}/{second} after {before}");
                Assert.Equal(top >= AutoLanguagePolicy.Sure, decision.Trusted);
            }
            else
            {
                Assert.True(decision.Trusted);
                Assert.Equal(before ?? runnerUp, decision.Language);
            }
        }
    }
}
