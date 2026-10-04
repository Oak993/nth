using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Port.Voice.Session;
using Marrow.Core.Port.Voice.Transcript;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Core.Tests.Port.Voice;

// VT-3 (voice-SPEC V-14.8, V-T89 pure part): the scripted session source replays a speaker's word arrivals, first-guess revisions and final pass,
// and touches no device, model, network or draft. The design's own SCRIPT table (voice-composer.js:42-83, :221-240) is not in the repository
// (ED-NEW-f-vt-3 (7)); these tests replay a script of this unit's own with the parameters the clause names (first guesses theme -> team, switch, -> switch,
// tooltip -> tool, too. -> too; arrivals after each word ends, revisions after that) so the mechanism is proved and the design's rows can be added.
public sealed class ScriptedVoiceSourceTests
{
    private static VoiceScript Speaker() => new(
    [
        new ScriptedWord("switch,", 0.4, 0.8, FirstGuess: "switch", ArriveAfter: 0.3, ReviseAfter: 0.35),
        new ScriptedWord("theme", 0.9, 1.4, FirstGuess: "team", ArriveAfter: 0.5, ReviseAfter: 0.5),
        new ScriptedWord("tooltip", 1.5, 2.1, FirstGuess: "tool", ArriveAfter: 0.7, ReviseAfter: 0.6),
        new ScriptedWord("too.", 2.3, 2.6, FirstGuess: "too", ArriveAfter: 0.4, ReviseAfter: 0.45),
    ], "en");

    private static ScriptedDecode Decode(double start, double end, DecodeMode mode = DecodeMode.Live) =>
        new(TimeSpan.FromSeconds(end - start), new DecodeRequest("auto", "", mode, TimeSpan.FromSeconds(start)), new float[(int)Math.Round((end - start) * 16000)]);

    private static string Texts(Hypothesis hypothesis) => string.Join(" ", hypothesis.Words.Select(w => w.Text));

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T89")]
    [Trait("Decision", "ED-267")] // V-14.8: a word appears once its arrival delay has passed, as its first guess, and is revised after the revision delay
    public void The_script_shows_each_word_after_its_arrival_delay_first_as_a_guess_then_revised()
    {
        var script = Speaker();
        Assert.Equal("", Texts(script.Answer(Decode(0, 1.0))));
        Assert.Equal("", Texts(script.Answer(Decode(0, 1.09))));
        Assert.Equal("switch", Texts(script.Answer(Decode(0, 1.11)))); // 0.8 + 0.3
        Assert.Equal("switch", Texts(script.Answer(Decode(0, 1.44)))); // 1.1 + 0.35 = 1.45: still the guess
        Assert.Equal("switch,", Texts(script.Answer(Decode(0, 1.46))));
        Assert.Equal("switch, team", Texts(script.Answer(Decode(0, 1.91)))); // theme: 1.4 + 0.5
        Assert.Equal("switch, theme", Texts(script.Answer(Decode(0, 2.41)))); // 1.9 + 0.5
        Assert.Equal("switch, theme tool", Texts(script.Answer(Decode(0, 2.81)))); // tooltip: 2.1 + 0.7
        Assert.Equal("switch, theme tooltip", Texts(script.Answer(Decode(0, 3.41)))); // 2.8 + 0.6
        Assert.Equal("switch, theme tooltip too", Texts(script.Answer(Decode(0, 3.01)))); // too.: 2.6 + 0.4 = 3.0, its guess
        Assert.Equal("switch, theme tooltip too.", Texts(script.Answer(Decode(0, 3.46)))); // 3.0 + 0.45
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T89")]
    [Trait("Decision", "ED-267")] // V-14.8: the final pass shows every word in its final form (punctuation included), whatever the clock says
    public void The_final_pass_shows_every_word_in_its_final_form()
    {
        var script = Speaker();
        Assert.Equal("switch, theme tooltip too.", Texts(script.Answer(Decode(0, 3.0, DecodeMode.Final))));
        Assert.Equal("switch,", Texts(script.Answer(Decode(0, 0.85, DecodeMode.Final))));
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T89")]
    [Trait("Decision", "ED-267")] // V-7.2: word times are seconds from the first decoded sample; a window that starts mid-speech drops earlier words and clamps the rest
    public void Word_times_are_relative_to_the_window_and_earlier_words_are_left_out()
    {
        var script = Speaker();
        var hypothesis = script.Answer(Decode(1.0, 3.4, DecodeMode.Final));
        Assert.Equal("theme tooltip too.", Texts(hypothesis));
        Assert.Equal(0.0, hypothesis.Words[0].Start);
        Assert.Equal(1.4 - 1.0, hypothesis.Words[0].End, 6);
        Assert.Equal(1.5 - 1.0, hypothesis.Words[1].Start, 6);
        Assert.Equal(2.1 - 1.0, hypothesis.Words[1].End, 6);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T89")]
    [Trait("Decision", "ED-267")] // V-14.8: the engine reports the script's language once it has recognised a word, not before
    public void The_script_reports_its_language_only_with_words()
    {
        var script = Speaker();
        Assert.Null(script.Answer(Decode(0, 0.5)).Language);
        Assert.Equal("en", script.Answer(Decode(0, 1.2)).Language);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T89")]
    [Trait("Decision", "ED-267")] // V-14.8: the VAD sees speech where words are, merging gaps under 0.3 s
    public void Speech_spans_join_words_closer_than_0_3_s()
    {
        var script = new VoiceScript(
        [
            new ScriptedWord("a", 0.5, 0.9), new ScriptedWord("b", 1.1, 1.5), new ScriptedWord("c", 2.0, 2.4),
        ]);
        Assert.Equal([(0.5, 1.5), (2.0, 2.4)], script.SpeechSpans);
        Assert.Equal(2.4, script.SpeechEnd);
        Assert.Equal(0, new VoiceScript([]).SpeechEnd);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T89")]
    [Trait("Decision", "ED-267")] // V-14.8, V-4.5: the speaker's signal is never exactly zero (a silent room is not a blocked microphone) and loud only while speaking
    public void The_signal_is_loud_while_speaking_and_never_exactly_zero()
    {
        var signal = Speaker().Signal(16000);
        var peakSpeaking = Enumerable.Range((int)(0.5 * 16000), 1600).Max(i => Math.Abs(signal(i)));
        var peakSilent = Enumerable.Range(0, 4000).Max(i => Math.Abs(signal(i)));
        Assert.True(peakSpeaking > 0.1f);
        Assert.True(peakSilent < 0.001f);
        Assert.All(Enumerable.Range(0, 16000), i => Assert.NotEqual(0f, signal(i)));
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T89")]
    [Trait("Decision", "ED-267")] // V-14.8: a session on the source replays the speaker: arrivals, revisions, the final pass, Landing, Rest
    public void A_session_on_the_source_replays_arrivals_revisions_and_the_final_pass()
    {
        using var runtime = new TestRuntime();
        var settings = new VoiceSessionSettings(VoiceOrbSettings.Default, VoiceTypingSettings.Default, "en-US", new SpeechModel("preview", null));
        var source = new ScriptedVoiceSource(Speaker(), () => settings, engineLatency: TimeSpan.FromSeconds(0.1));
        var states = new List<VoiceSessionState>();
        var updates = new List<(TimeSpan At, TranscriptUpdate Update)>();
        source.Session.StateChanged += (_, to) => states.Add(to);
        source.Session.TranscriptChanged += u => updates.Add((runtime.Time.Elapsed, u));
        source.Session.Activate();
        runtime.Advance(TimeSpan.FromSeconds(4.5));
        source.Session.Activate();
        runtime.Advance(TimeSpan.FromSeconds(3.0));
        Assert.Equal([VoiceSessionState.Listening, VoiceSessionState.Processing, VoiceSessionState.Landing, VoiceSessionState.Rest], states);
        var ops = updates.Where(u => u.Update.Kind == TranscriptUpdateKind.Live).SelectMany(u => u.Update.Ops.Select(o => (u.At, o.Op))).ToList();
        // Each word's first guess was added before its revision, and the revision is of that very word.
        var guessAt = ops.First(o => o.Op is { Kind: WordOpKind.Add, Text: "team" }).At;
        var reviseAt = ops.First(o => o.Op is { Kind: WordOpKind.Revise, Text: "theme" }).At;
        Assert.True(reviseAt > guessAt);
        Assert.True(guessAt >= TimeSpan.FromSeconds(1.4 + 0.5), "a word is not shown before its arrival delay: " + guessAt);
        Assert.Equal("switch, theme tooltip too.", string.Join(" ", source.Session.Words.Select(w => w.Text)));
        Assert.All(source.Session.Words, w => Assert.False(w.Tentative));
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T89")]
    [Trait("Decision", "ED-267")] // V-14.8 / V-2.5: the source uses only its own scripted seams; the ambient ones stay closed and nothing is downloaded or opened
    public void The_source_touches_no_ambient_seam_and_loads_only_its_scripted_model()
    {
        using var runtime = new TestRuntime();
        var settings = new VoiceSessionSettings(VoiceOrbSettings.Default, VoiceTypingSettings.Default, "en-US", new SpeechModel("preview", null));
        var source = new ScriptedVoiceSource(Speaker(), () => settings);
        source.Session.Activate();
        runtime.Advance(TimeSpan.FromSeconds(4.5));
        source.Session.Activate();
        runtime.Advance(TimeSpan.FromSeconds(3.0));
        Assert.IsType<FailClosedAudioCapture>(VoiceSeams.Capture.Current);
        Assert.IsType<FailClosedSpeechEngine>(VoiceSeams.Engine.Current);
        Assert.IsType<FailClosedMicrophoneAccess>(VoiceSeams.Microphone.Current);
        Assert.IsType<FailClosedModelTransport>(VoiceSeams.ModelTransport.Current);
        var load = Assert.Single(source.Engine.Loads);
        Assert.Null(load.Model.Path);
        Assert.Equal(1, source.Capture.StartCount);
        Assert.Equal(1, source.Capture.StopCount);
        Assert.Equal(1, source.Microphone.Asked);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T89")]
    [Trait("Decision", "ED-267")] // V-14.8: two sources are two speakers; their sessions share a hub only when given one
    public void A_source_given_a_hub_keeps_one_session_at_a_time()
    {
        using var runtime = new TestRuntime();
        var hub = new VoiceSessionHub();
        var settings = new VoiceSessionSettings(VoiceOrbSettings.Default, VoiceTypingSettings.Default, "en-US", new SpeechModel("preview", null));
        var first = new ScriptedVoiceSource(Speaker(), () => settings, hub: hub);
        var second = new ScriptedVoiceSource(Speaker(), () => settings, hub: hub);
        first.Session.Activate();
        runtime.Advance(TimeSpan.FromSeconds(3.0));
        second.Session.Activate();
        runtime.Advance(TimeSpan.FromSeconds(0.001));
        Assert.Equal(VoiceSessionState.Processing, first.Session.State);
        Assert.True(second.Session.StartQueued);
    }
}
