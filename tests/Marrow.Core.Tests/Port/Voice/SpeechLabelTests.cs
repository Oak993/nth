using Marrow.Core.Port;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Session;

namespace Marrow.Core.Tests.Port.Voice;

// VT-3 (voice-SPEC V-6.3, V-12.9): the pill's label. Expected values are the clause's numbers: speech or a word less than 0.35 s old
// keeps "speechAt" at now; Transcribing while now - speechAt < 0.6 s; Finishing from the second click. The 18 strings are VT-1's table.
public sealed class SpeechLabelTests
{
    private static ContinuousInstant At(double seconds) => new(TimeSpan.FromSeconds(seconds).Ticks);

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T21")]
    [Trait("Decision", "ED-267")] // V-6.3: the session starts on Listening
    public void A_new_label_reads_Listening()
    {
        var label = new SpeechLabel();
        Assert.Equal(VoiceLabelState.Listening, label.State(At(0)));
        Assert.Equal(VoiceLabelState.Listening, label.State(At(30)));
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T21")]
    [Trait("Decision", "ED-267")] // V-6.3: speechAt = now whenever the speech state is on
    public void Speech_turns_the_label_to_Transcribing_and_it_stays_while_speech_is_on()
    {
        var label = new SpeechLabel();
        label.SetSpeech(true, At(0.5));
        Assert.Equal(VoiceLabelState.Transcribing, label.State(At(0.5)));
        Assert.Equal(VoiceLabelState.Transcribing, label.State(At(5.0)));
        Assert.Equal(VoiceLabelState.Transcribing, label.State(At(60.0)));
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T21")]
    [Trait("Decision", "ED-267")] // V-6.3: back to Listening once now - speechAt reaches 0.6 s (speech last on at 1.0)
    public void The_label_goes_back_to_Listening_0_6_s_after_the_speech_state_went_off()
    {
        var label = new SpeechLabel();
        label.SetSpeech(true, At(0.5));
        label.SetSpeech(false, At(1.0));
        Assert.Equal(VoiceLabelState.Transcribing, label.State(At(1.0)));
        Assert.Equal(VoiceLabelState.Transcribing, label.State(At(1.599)));
        Assert.Equal(VoiceLabelState.Listening, label.State(At(1.6)));
        Assert.Equal(VoiceLabelState.Listening, label.State(At(9.0)));
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T21")]
    [Trait("Decision", "ED-267")] // V-6.3: a word that arrived less than 0.35 s ago counts as speech: Transcribing until 0.35 + 0.6 after it
    public void A_word_counts_as_speech_for_0_35_s_and_the_label_holds_0_6_s_more()
    {
        var label = new SpeechLabel();
        label.WordArrived(At(2.0));
        Assert.Equal(VoiceLabelState.Transcribing, label.State(At(2.0)));
        Assert.Equal(VoiceLabelState.Transcribing, label.State(At(2.949)));
        Assert.Equal(VoiceLabelState.Listening, label.State(At(2.95)));
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T21")]
    [Trait("Decision", "ED-267")] // V-6.3: the rule is the same whenever the label is read: a late read after a word sees the same state
    public void A_label_read_only_late_still_applies_the_word_window()
    {
        var late = new SpeechLabel();
        late.WordArrived(At(0));
        // Read for the first time at 0.5 s: the word was 0.35 s of speech, so speechAt = 0.35 and 0.5 - 0.35 < 0.6.
        Assert.Equal(VoiceLabelState.Transcribing, late.State(At(0.5)));
        var later = new SpeechLabel();
        later.WordArrived(At(0));
        Assert.Equal(VoiceLabelState.Listening, later.State(At(0.95)));
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T21")]
    [Trait("Decision", "ED-267")] // V-6.3 "no flicker over gaps under 0.6 s": speech 0-1.0 and 1.5-2.5 (a 0.5 s gap), read every 16 ms
    public void A_gap_under_0_6_s_does_not_flicker_back_to_Listening()
    {
        var label = new SpeechLabel();
        label.SetSpeech(true, At(0));
        var seen = new List<VoiceLabelState>();
        for (var ms = 0; ms <= 3200; ms += 16)
        {
            var now = At(ms / 1000.0);
            if (ms == 1008) label.SetSpeech(false, now);
            if (ms == 1504) label.SetSpeech(true, now);
            if (ms == 2512) label.SetSpeech(false, now);
            seen.Add(label.State(now));
        }
        var firstListening = seen.FindIndex(s => s == VoiceLabelState.Listening);
        // Transcribing from the start until 0.6 s after the second speech ended (about 3.1 s), never Listening in between.
        Assert.True(firstListening * 16 / 1000.0 >= 3.1, "the label went back to Listening at " + firstListening * 16 + " ms");
        Assert.All(seen.Take(firstListening), s => Assert.Equal(VoiceLabelState.Transcribing, s));
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T21")]
    [Trait("Decision", "ED-267")] // V-6.3: a gap of 0.7 s (over 0.6 s) does go back to Listening, once
    public void A_gap_over_0_6_s_goes_back_to_Listening_and_returns_with_the_next_speech()
    {
        var label = new SpeechLabel();
        label.SetSpeech(true, At(0));
        label.SetSpeech(false, At(1.0));
        Assert.Equal(VoiceLabelState.Listening, label.State(At(1.65)));
        label.SetSpeech(true, At(1.7));
        Assert.Equal(VoiceLabelState.Transcribing, label.State(At(1.7)));
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T21")]
    [Trait("Decision", "ED-267")] // V-6.3: Finishing from the second click until the final pass completes
    public void Finishing_holds_from_the_second_click_until_the_pass_completes_whatever_the_speech_does()
    {
        var label = new SpeechLabel();
        label.SetSpeech(true, At(0));
        label.Finish();
        Assert.Equal(VoiceLabelState.Finishing, label.State(At(0.1)));
        label.SetSpeech(true, At(0.2));
        label.WordArrived(At(0.3));
        Assert.Equal(VoiceLabelState.Finishing, label.State(At(5)));
        label.Reset();
        Assert.Equal(VoiceLabelState.Listening, label.State(At(5)));
    }

    [Theory]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T121")]
    [Trait("Decision", "ED-267")] // V-12.9: the picked set's word per state, with the unchanged V-6.3 timing
    [InlineData(VoicePillWords.L1)]
    [InlineData(VoicePillWords.L2)]
    [InlineData(VoicePillWords.L3)]
    [InlineData(VoicePillWords.L4)]
    [InlineData(VoicePillWords.L5)]
    [InlineData(VoicePillWords.L6)]
    public void Every_set_gives_its_word_for_listening_transcribing_and_finishing_with_the_same_timing(VoicePillWords set)
    {
        var words = VoicePillWordSets.For(set);
        var label = new SpeechLabel();
        Assert.Equal(words.Listening, label.Words(set, At(0)));
        label.SetSpeech(true, At(0.2));
        Assert.Equal(words.Transcribing, label.Words(set, At(0.2)));
        label.SetSpeech(false, At(0.4));
        Assert.Equal(words.Transcribing, label.Words(set, At(0.9)));
        Assert.Equal(words.Listening, label.Words(set, At(1.0)));
        label.Finish();
        Assert.Equal(words.Finishing, label.Words(set, At(1.0)));
    }
}
