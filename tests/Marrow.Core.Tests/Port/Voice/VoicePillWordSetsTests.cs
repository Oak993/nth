using Marrow.Core.Port.Voice;

namespace Marrow.Core.Tests.Port.Voice;

// VT-1 (OD-0097; voice-SPEC V-12.9): the pill's word sets L1 to L6. The 18 strings are transcribed from the table of V-12.9
// (the Voice Options page's LSETS); the fixture extracted from voice-options.js by a node script is not in the repository
// (see the unit's report). U+2026 HORIZONTAL ELLIPSIS is written as \U00002026 (R-STR-9).
public sealed class VoicePillWordSetsTests
{
    private const string E = "\U00002026"; // U+2026 HORIZONTAL ELLIPSIS

    [Theory]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T121")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-12.9 table, byte for byte
    [InlineData(VoicePillWords.L1, "Listening" + E, "Transcribing" + E, "Finishing" + E)]
    [InlineData(VoicePillWords.L2, "Listening" + E, "Writing" + E, "Finishing" + E)]
    [InlineData(VoicePillWords.L3, "Hearing you" + E, "Writing" + E, "Tidying up" + E)]
    [InlineData(VoicePillWords.L4, "Speak now", "Transcribing" + E, "Almost done" + E)]
    [InlineData(VoicePillWords.L5, "Listening" + E, "Typing" + E, "Wrapping up" + E)]
    [InlineData(VoicePillWords.L6, "Listening", "Transcribing", "Finishing")]
    public void Each_set_has_the_words_of_the_decision_page(VoicePillWords set, string listening, string transcribing, string finishing)
    {
        var words = VoicePillWordSets.For(set);
        Assert.Equal(listening, words.Listening);
        Assert.Equal(transcribing, words.Transcribing);
        Assert.Equal(finishing, words.Finishing);
        Assert.Equal(listening, VoicePillWordSets.Word(set, VoiceLabelState.Listening));
        Assert.Equal(transcribing, VoicePillWordSets.Word(set, VoiceLabelState.Transcribing));
        Assert.Equal(finishing, VoicePillWordSets.Word(set, VoiceLabelState.Finishing));
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T121")]
    [Trait("Decision", "ED-267")] // voice-SPEC V-12.9: L4's "Speak now" and all of L6 have no ellipsis; the others have U+2026 as their last scalar
    public void Only_Speak_now_and_the_L6_words_lack_the_ellipsis_and_it_is_the_single_character_U_2026()
    {
        var without = new List<string>();
        foreach (var set in Enum.GetValues<VoicePillWords>())
        foreach (var state in Enum.GetValues<VoiceLabelState>())
        {
            var word = VoicePillWordSets.Word(set, state);
            if (word.EndsWith(E, StringComparison.Ordinal)) Assert.DoesNotContain("...", word, StringComparison.Ordinal);
            else without.Add(word);
            Assert.DoesNotContain('·', word); // U+00B7 MIDDLE DOT: the separator dot of OD-0097's amendment is never in a word
        }
        Assert.Equal(["Speak now", "Listening", "Transcribing", "Finishing"], without);
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T121")]
    [Trait("Decision", "ED-267")] // OD-0097: L1 is the default set; there are exactly six
    public void There_are_six_sets_and_L1_is_the_default()
    {
        Assert.Equal(6, Enum.GetValues<VoicePillWords>().Length);
        Assert.Equal(VoicePillWords.L1, VoiceOrbSettings.Default.Labels);
        Assert.Equal(3, Enum.GetValues<VoiceLabelState>().Length);
        Assert.Equal(18, Enum.GetValues<VoicePillWords>().SelectMany(s => Enum.GetValues<VoiceLabelState>().Select(t => VoicePillWordSets.Word(s, t))).Count());
    }
}
