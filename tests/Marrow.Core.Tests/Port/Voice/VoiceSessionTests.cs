using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Port.Voice.Session;
using Marrow.Core.Port.Voice.Transcript;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Port.Voice;

// VT-3 (voice-SPEC V-15, V-4.5, V-6.4, V-7.9, V-7.10): the session's state table, row by row, on the manual clock with scripted seams.
// The speaker is VoiceSessionRig.SwitchTheme (four words over 2.5 s, "theme" first heard as "team"): a test script of this unit's own, not the
// design's SCRIPT (ED-292 (7)). The pill's words are the default set L1 unless a test picks another.
public sealed class VoiceSessionTests
{
    private const string E = "\U00002026"; // U+2026 HORIZONTAL ELLIPSIS

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-15 Rest -> Hover -> Rest: the pointer; the model load starts on pointer-enter (V-7.9)
    public void The_pointer_over_the_orb_moves_Rest_to_Hover_and_starts_the_model_load()
    {
        using var rig = new VoiceSessionRig();
        rig.Session.PointerEnter();
        rig.Settle();
        Assert.Equal(VoiceSessionState.Hover, rig.Session.State);
        var load = Assert.Single(rig.Engine.Loads);
        Assert.Equal(rig.Model, load.Model);
        Assert.Equal(0, rig.Capture.StartCount);
        rig.Session.PointerLeave();
        Assert.Equal(VoiceSessionState.Rest, rig.Session.State);
        Assert.Equal([(VoiceSessionState.Rest, VoiceSessionState.Hover), (VoiceSessionState.Hover, VoiceSessionState.Rest)], rig.States);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-15 Rest -> Listening: capture starts, the VAD is reset, the model loads, the label reads Listening
    public void A_click_at_Rest_with_the_microphone_allowed_starts_listening()
    {
        using var rig = new VoiceSessionRig();
        rig.Session.Activate();
        rig.Settle();
        Assert.Equal(VoiceSessionState.Listening, rig.Session.State);
        Assert.Equal([(VoiceSessionState.Rest, VoiceSessionState.Listening)], rig.States);
        Assert.Equal(1, rig.Capture.StartCount);
        Assert.True(rig.Capture.IsRunning);
        Assert.Equal(1, rig.Vad.ResetCount);
        Assert.Equal(1, rig.Microphone.Asked);
        Assert.Single(rig.Engine.Loads);
        Assert.Equal(VoiceLabelState.Listening, rig.Session.Label);
        Assert.Equal("Listening" + E, rig.Session.LabelText);
        Assert.Equal("auto", rig.Session.ResolvedLanguage);
        Assert.Same(rig.Hub.Active, rig.Session);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-15 Hover -> Listening: the load that began on hover is not started again
    public void A_click_after_hover_listens_without_loading_the_model_a_second_time()
    {
        using var rig = new VoiceSessionRig();
        rig.Session.PointerEnter();
        rig.Settle();
        rig.Session.Activate();
        rig.Settle();
        Assert.Equal(VoiceSessionState.Listening, rig.Session.State);
        Assert.Single(rig.Engine.Loads);
        Assert.Equal([VoiceSessionState.Hover, VoiceSessionState.Listening], rig.Visited);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-15: the pointer means nothing once listening
    public void The_pointer_is_ignored_while_listening()
    {
        using var rig = new VoiceSessionRig();
        rig.Session.Activate();
        rig.Session.PointerEnter();
        rig.Session.PointerLeave();
        Assert.Equal(VoiceSessionState.Listening, rig.Session.State);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-15 Rest -> Blocked (V-4.5): permission denied opens no device; the blocked note; Rest after 5.2 s
    public void A_denied_microphone_gives_Blocked_without_opening_the_device_and_then_Rest_after_5_2_s()
    {
        using var rig = new VoiceSessionRig { Microphone = new ScriptedMicrophone(MicrophoneAccessStatus.Denied) };
        rig.Session.Activate();
        rig.Settle();
        Assert.Equal(VoiceSessionState.Blocked, rig.Session.State);
        Assert.Equal([VoiceNoteKind.Blocked], rig.Notes);
        Assert.Equal(0, rig.Capture.StartCount);
        Assert.Empty(rig.Engine.Loads);
        rig.Advance(5.19);
        Assert.Equal(VoiceSessionState.Blocked, rig.Session.State);
        rig.Advance(0.01);
        Assert.Equal(VoiceSessionState.Rest, rig.Session.State);
        Assert.Equal([VoiceSessionState.Blocked, VoiceSessionState.Rest], rig.Visited);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-15: Blocked leaves only by its 5.2 s; a click meanwhile does nothing (ED-292 (8))
    public void A_click_while_Blocked_does_nothing()
    {
        using var rig = new VoiceSessionRig { Microphone = new ScriptedMicrophone(MicrophoneAccessStatus.Denied) };
        rig.Session.Activate();
        rig.Microphone.Current = MicrophoneAccessStatus.Allowed;
        rig.Session.Activate();
        rig.Settle();
        Assert.Equal(VoiceSessionState.Blocked, rig.Session.State);
        Assert.Equal(1, rig.Microphone.Asked);
        rig.Advance(5.2);
        rig.Session.Activate();
        rig.Settle();
        Assert.Equal(VoiceSessionState.Listening, rig.Session.State);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-4.5: the first 1.0 s of exact zeros means Windows hands a blocked app silence: capture stops, Blocked shows
    public void A_first_second_of_exact_zeros_stops_capture_and_shows_Blocked()
    {
        using var rig = new VoiceSessionRig();
        rig.Capture.PlayOnStart(ScriptedSignals.AllZero, TimeSpan.FromSeconds(10));
        rig.Session.Activate();
        rig.Advance(0.99);
        Assert.Equal(VoiceSessionState.Listening, rig.Session.State);
        rig.Advance(0.01);
        Assert.Equal(VoiceSessionState.Blocked, rig.Session.State);
        Assert.Equal(1, rig.Capture.StopCount);
        Assert.False(rig.Capture.IsRunning);
        Assert.Equal([VoiceNoteKind.Blocked], rig.Notes);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-4.5: a quiet room (never exactly zero) is not blocked, and zeros after the first second are only a quiet moment
    public void A_quiet_room_and_zeros_after_the_first_second_do_not_block()
    {
        using var rig = new VoiceSessionRig();
        rig.Capture.PlayOnStart(i => i < 19200 ? ScriptedSignals.RoomNoise(i) : 0f, TimeSpan.FromSeconds(10));
        rig.Session.Activate();
        rig.Advance(5.0);
        Assert.Equal(VoiceSessionState.Listening, rig.Session.State);
        Assert.Empty(rig.Notes);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-17.6 (errors without a design, Q-V-7 A): no microphone, the device would not open, the model would not load
    public void No_microphone_a_device_that_will_not_open_and_a_model_that_will_not_load_each_end_the_session_with_their_note()
    {
        using (var noDevice = new VoiceSessionRig { Microphone = new ScriptedMicrophone(MicrophoneAccessStatus.NoDevice) })
        {
            noDevice.Session.Activate();
            noDevice.Settle();
            Assert.Equal(VoiceSessionState.Rest, noDevice.Session.State);
            Assert.Equal([VoiceNoteKind.NoMicrophone], noDevice.Notes);
            Assert.Equal(0, noDevice.Capture.StartCount);
        }
        using (var busy = new VoiceSessionRig())
        {
            busy.Capture.FailStart = new InvalidOperationException("device in use");
            busy.Session.Activate();
            busy.Settle();
            Assert.Equal(VoiceSessionState.Rest, busy.Session.State);
            Assert.Equal([VoiceNoteKind.CaptureFailed], busy.Notes);
        }
        using (var noModel = new VoiceSessionRig { Model = null })
        {
            noModel.Session.Activate();
            noModel.Settle();
            Assert.Equal(VoiceSessionState.Rest, noModel.Session.State);
            Assert.Equal([VoiceNoteKind.ModelLoadFailed], noModel.Notes);
            Assert.Equal(0, noModel.Microphone.Asked);
            Assert.Equal(0, noModel.Capture.StartCount);
        }
        using (var broken = new VoiceSessionRig())
        {
            broken.Engine.FailLoads(new InvalidOperationException("cannot load"));
            broken.Session.Activate();
            broken.Settle();
            Assert.Equal(VoiceSessionState.Rest, broken.Session.State);
            Assert.Equal([VoiceNoteKind.ModelLoadFailed], broken.Notes);
            Assert.False(broken.Capture.IsRunning);
        }
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-15 Listening -> Listening (voice): the label follows the speech (V-6.3) and live words arrive (V-7.3)
    public void While_listening_the_label_follows_the_speech_and_live_words_arrive()
    {
        using var rig = new VoiceSessionRig();
        rig.Speak(VoiceSessionRig.SwitchTheme());
        rig.Session.Activate();
        rig.Advance(0.4);
        Assert.Equal(VoiceLabelState.Listening, rig.Session.Label);
        rig.Advance(0.6);
        Assert.Equal(VoiceLabelState.Transcribing, rig.Session.Label);
        Assert.Equal("Transcribing" + E, rig.Session.LabelText);
        rig.Advance(5.0);
        Assert.Equal(VoiceLabelState.Listening, rig.Session.Label);
        Assert.Equal("switch the theme please.", rig.FinalText);
        Assert.Equal(VoiceSessionState.Listening, rig.Session.State);
        Assert.Empty(rig.Notes);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-7.5: a first guess is shown, then revised in the tail (theme was first heard as team); committed words never change
    public void A_first_guess_is_revised_and_no_committed_word_is_ever_revised()
    {
        using var rig = new VoiceSessionRig();
        rig.Speak(VoiceSessionRig.SwitchTheme());
        rig.Session.Activate();
        rig.Advance(6.0);
        var ops = rig.Updates.Where(u => u.Kind == TranscriptUpdateKind.Live).SelectMany(u => u.Ops).Select(o => o.Op).ToList();
        Assert.Contains(WordOp.Add(2, "team", true), ops);
        Assert.Contains(WordOp.Revise(2, "theme"), ops);
        // Replay the operations: a Revise only ever touches a word that is still tentative.
        var shown = new List<TranscriptWord>();
        foreach (var op in ops)
        {
            switch (op.Kind)
            {
                case WordOpKind.Add: shown.Insert(op.Index, new TranscriptWord(op.Text!, op.Tentative)); break;
                case WordOpKind.Revise:
                    Assert.True(shown[op.Index].Tentative, "revised a committed word at " + op.Index);
                    shown[op.Index] = shown[op.Index] with { Text = op.Text! };
                    break;
                case WordOpKind.Confirm: shown[op.Index] = shown[op.Index] with { Tentative = false }; break;
                case WordOpKind.Remove: shown.RemoveAt(op.Index); break;
            }
        }
        Assert.Equal(rig.Session.Words, shown);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-7.5: adds in one batch are at least 60 ms apart
    public void Adds_within_one_live_update_are_at_least_60_ms_apart()
    {
        using var rig = new VoiceSessionRig();
        rig.Speak(VoiceSessionRig.SwitchTheme());
        rig.Session.Activate();
        rig.Advance(6.0);
        foreach (var update in rig.Updates.Where(u => u.Kind == TranscriptUpdateKind.Live))
        {
            var adds = update.Ops.Where(o => o.Op.Kind == WordOpKind.Add).Select(o => o.At).ToList();
            for (var i = 1; i < adds.Count; i++) Assert.True(adds[i] - adds[i - 1] >= TimeSpan.FromMilliseconds(60));
        }
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-15 Listening -> Processing -> Landing -> Rest, with V-7.6's Processing time and Landing's 0.5 s
    public void A_second_click_goes_through_Processing_and_Landing_with_the_timings_of_the_table()
    {
        using var rig = new VoiceSessionRig();
        rig.Speak(VoiceSessionRig.SwitchTheme(), TimeSpan.FromSeconds(0.2));
        rig.Session.Activate();
        rig.Advance(3.0);
        rig.Updates.Clear();
        rig.Session.Activate(); // the second click
        rig.Settle();
        Assert.Equal(VoiceSessionState.Processing, rig.Session.State);
        Assert.Equal(1, rig.Capture.StopCount);
        Assert.Equal(VoiceLabelState.Finishing, rig.Session.Label);
        Assert.Equal("Finishing" + E, rig.Session.LabelText);
        Assert.Equal(VoiceStopReason.Click, rig.Session.LastStopReason);
        rig.Advance(0.2); // the final decode takes 0.2 s: the result arrives now
        var final = Assert.Single(rig.Updates, u => u.Kind == TranscriptUpdateKind.Final);
        Assert.Equal("switch the theme please.", string.Join(" ", final.Words.Select(w => w.Text)));
        // the design's planFinal: the slot after the last operation (each operation followed by its kind's spacing)
        var scheduleEnd = final.Ops.Aggregate(TranscriptOps.FinalStart, (t, o) => t + (o.Op.Kind == WordOpKind.Add ? TranscriptOps.FinalAddSpacing : TranscriptOps.FinalReviseSpacing));
        var expected = TimeSpan.FromTicks(Math.Max(TranscriptOps.FinalMinimum.Ticks, (TimeSpan.FromSeconds(0.2) + scheduleEnd + TranscriptOps.FinalTail).Ticks));
        var remaining = expected - TimeSpan.FromSeconds(0.2);
        rig.Runtime.Advance(remaining - TimeSpan.FromMilliseconds(1));
        Assert.Equal(VoiceSessionState.Processing, rig.Session.State);
        rig.Advance(0.001);
        Assert.Equal(VoiceSessionState.Landing, rig.Session.State);
        var ink = Assert.Single(rig.Updates, u => u.Kind == TranscriptUpdateKind.Landing);
        Assert.All(ink.Words, w => Assert.False(w.Tentative));
        rig.Advance(0.499);
        Assert.Equal(VoiceSessionState.Landing, rig.Session.State);
        rig.Advance(0.001);
        Assert.Equal(VoiceSessionState.Rest, rig.Session.State);
        Assert.Null(rig.Hub.Active);
        Assert.Equal([VoiceSessionState.Listening, VoiceSessionState.Processing, VoiceSessionState.Landing, VoiceSessionState.Rest], rig.Visited);
        Assert.Empty(rig.Notes);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-7.6: Processing lasts at least 0.75 s even when the pass is instant and nothing is left to write
    public void Processing_lasts_at_least_0_75_s()
    {
        using var rig = new VoiceSessionRig();
        rig.Speak(VoiceSessionRig.SwitchTheme());
        rig.Session.Activate();
        rig.Advance(6.0); // everything committed by the long pause; the final pass has nothing left to decode
        rig.Session.Activate();
        rig.Settle();
        Assert.Equal(VoiceSessionState.Processing, rig.Session.State);
        rig.Advance(0.74);
        Assert.Equal(VoiceSessionState.Processing, rig.Session.State);
        rig.Advance(0.01);
        Assert.Equal(VoiceSessionState.Landing, rig.Session.State);
        Assert.Equal("switch the theme please.", rig.FinalText);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-15 Processing | click: ignored
    public void A_click_during_Processing_is_ignored()
    {
        using var rig = new VoiceSessionRig();
        rig.Speak(VoiceSessionRig.SwitchTheme(), TimeSpan.FromSeconds(0.2));
        rig.Session.Activate();
        rig.Advance(3.0);
        rig.Session.Activate();
        rig.Settle();
        var visited = rig.Visited.Count;
        rig.Session.Activate();
        rig.Settle();
        Assert.Equal(VoiceSessionState.Processing, rig.Session.State);
        Assert.Equal(visited, rig.Visited.Count);
        Assert.Equal(1, rig.Capture.StopCount);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-15 Listening | stop with nothing written and no speech seen -> Rest + "No speech was heard"; V-6.4: the engine is never called
    public void Stopping_a_session_the_VAD_never_heard_speech_in_ends_at_Rest_with_the_no_speech_note()
    {
        using var rig = new VoiceSessionRig();
        rig.QuietRoom();
        rig.Session.Activate();
        rig.Advance(2.0);
        rig.Session.Activate();
        rig.Settle();
        Assert.Equal(VoiceSessionState.Rest, rig.Session.State);
        Assert.Equal([VoiceSessionState.Listening, VoiceSessionState.Rest], rig.Visited);
        Assert.Equal([VoiceNoteKind.NoSpeech], rig.Notes);
        Assert.Empty(rig.Engine.Decodes);
        Assert.Equal(1, rig.Capture.StopCount);
        Assert.Empty(rig.Session.Words);
        Assert.Null(rig.Hub.Active);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T26")]
    [Trait("Decision", "ED-267")] // V-7.8, V-6.4: silence for a minute is never sent to the engine
    public void Minutes_of_silence_never_reach_the_engine()
    {
        using var rig = new VoiceSessionRig();
        rig.QuietRoom();
        rig.Session.Activate();
        rig.Advance(60.0);
        Assert.Empty(rig.Engine.Decodes);
        Assert.Equal(VoiceSessionState.Listening, rig.Session.State);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-15 Listening -> Rest (Esc / the cross) with nothing written: capture off, the cancelled note
    public void Escape_while_listening_with_nothing_written_ends_at_Rest_with_the_cancelled_note()
    {
        using var rig = new VoiceSessionRig();
        rig.QuietRoom();
        rig.Session.Activate();
        rig.Advance(1.0);
        rig.Session.Cancel();
        rig.Settle();
        Assert.Equal(VoiceSessionState.Rest, rig.Session.State);
        Assert.Equal([VoiceNoteKind.Cancelled], rig.Notes);
        Assert.Equal(1, rig.Capture.StopCount);
        Assert.Empty(rig.Engine.Decodes);
        Assert.Null(rig.Hub.Active);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-15, V-8.6: with words written the cancel withdraws them from the end and shows no note
    public void Escape_with_words_written_withdraws_them_and_shows_no_note()
    {
        using var rig = new VoiceSessionRig();
        rig.Speak(VoiceSessionRig.SwitchTheme());
        rig.Session.Activate();
        rig.Advance(3.2);
        var written = rig.Session.Words.Count;
        Assert.True(written >= 2, "words were written before the cancel");
        rig.Session.Cancel();
        rig.Settle();
        Assert.Equal(VoiceSessionState.Rest, rig.Session.State);
        Assert.Empty(rig.Notes);
        Assert.Empty(rig.Session.Words);
        var withdraw = Assert.Single(rig.Updates, u => u.Kind == TranscriptUpdateKind.Withdraw);
        Assert.Equal(Enumerable.Range(0, written).Reverse().Select(WordOp.Remove).ToList(), withdraw.Ops.Select(o => o.Op).ToList());
        Assert.Equal(Enumerable.Range(0, written).Select(i => TimeSpan.FromMilliseconds(30 * i)).ToList(), withdraw.Ops.Select(o => o.At).ToList());
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-7.10: Esc cancels the pass in flight (the token reaches the engine) and discards the words
    public void Escape_aborts_the_pass_in_flight()
    {
        using var rig = new VoiceSessionRig();
        rig.Speak(VoiceSessionRig.SwitchTheme(), TimeSpan.FromSeconds(30));
        rig.Session.Activate();
        rig.Advance(1.2);
        Assert.Equal(1, rig.Engine.InFlight);
        rig.Session.Cancel();
        rig.Settle();
        Assert.Equal(0, rig.Engine.InFlight);
        Assert.Equal(1, rig.Engine.Aborted);
        Assert.Equal(VoiceSessionState.Rest, rig.Session.State);
        rig.Advance(60.0);
        Assert.DoesNotContain(rig.Updates, u => u.Kind != TranscriptUpdateKind.Withdraw);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-15 Processing -> Rest (Esc): the final pass is cancelled, the text never lands
    public void Escape_during_Processing_cancels_the_final_pass_and_nothing_lands()
    {
        using var rig = new VoiceSessionRig();
        rig.Speak(VoiceSessionRig.SwitchTheme(), TimeSpan.FromSeconds(0.2));
        rig.Session.Activate();
        rig.Advance(3.0);
        rig.Session.Activate();
        rig.Settle();
        Assert.Equal(VoiceSessionState.Processing, rig.Session.State);
        rig.Advance(0.1);
        rig.Session.Cancel();
        rig.Settle();
        Assert.Equal(VoiceSessionState.Rest, rig.Session.State);
        Assert.DoesNotContain(VoiceSessionState.Landing, rig.Visited);
        Assert.DoesNotContain(rig.Updates, u => u.Kind is TranscriptUpdateKind.Final or TranscriptUpdateKind.Landing);
        Assert.Equal(0, rig.Engine.InFlight);
        Assert.True(rig.Engine.Aborted >= 1, "the final decode was aborted");
        Assert.Equal(DecodeMode.Final, rig.Engine.Decodes[^1].Request.Mode);
        Assert.Empty(rig.Notes);
        rig.Advance(10.0);
        Assert.Equal(VoiceSessionState.Rest, rig.Session.State);
        Assert.Null(rig.Hub.Active);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-14.4: Esc keeps its other meaning outside Listening and Processing; it does nothing here
    public void Escape_at_Rest_and_during_Landing_is_ignored()
    {
        using var rig = new VoiceSessionRig();
        rig.Session.Cancel();
        Assert.Equal(VoiceSessionState.Rest, rig.Session.State);
        Assert.Empty(rig.States);
        rig.Speak(VoiceSessionRig.SwitchTheme());
        rig.Session.Activate();
        rig.Advance(6.0);
        rig.Session.Activate();
        rig.Settle();
        rig.Advance(0.75);
        Assert.Equal(VoiceSessionState.Landing, rig.Session.State);
        rig.Session.Cancel();
        Assert.Equal(VoiceSessionState.Landing, rig.Session.State);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-15 Landing | click -> Listening: a new dictation may begin at once
    public void A_click_during_Landing_starts_a_new_session()
    {
        using var rig = new VoiceSessionRig();
        rig.Speak(VoiceSessionRig.SwitchTheme());
        rig.Session.Activate();
        rig.Advance(6.0);
        rig.Session.Activate();
        rig.Settle();
        rig.Advance(0.75);
        Assert.Equal(VoiceSessionState.Landing, rig.Session.State);
        rig.Session.Activate();
        rig.Settle();
        Assert.Equal(VoiceSessionState.Listening, rig.Session.State);
        Assert.Equal(2, rig.Capture.StartCount);
        Assert.Empty(rig.Session.Words);
        rig.Advance(1.0);
        Assert.Equal(VoiceSessionState.Listening, rig.Session.State);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-15 "or a stop event", V-4.4: the device is removed mid-session: stop, and the final pass on what was captured
    public void A_removed_device_stops_the_session_like_a_click_and_the_final_pass_runs_on_what_was_captured()
    {
        using var rig = new VoiceSessionRig();
        rig.Speak(VoiceSessionRig.SwitchTheme(), TimeSpan.FromSeconds(0.2));
        rig.Session.Activate();
        rig.Advance(3.0);
        rig.Capture.RaiseDeviceEvent(CaptureDeviceEventKind.DeviceRemoved);
        rig.Settle();
        Assert.Equal(VoiceSessionState.Processing, rig.Session.State);
        Assert.Equal(VoiceStopReason.DeviceEvent, rig.Session.LastStopReason);
        rig.Advance(5.0);
        Assert.Equal(VoiceSessionState.Rest, rig.Session.State);
        Assert.Contains(VoiceSessionState.Landing, rig.Visited);
        Assert.Contains(rig.Updates, u => u.Kind == TranscriptUpdateKind.Final);
        Assert.Equal("switch the theme please.", rig.FinalText);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-4.4: another app taking the device exclusively is an error: the session ends, discarding what it has
    public void Another_app_taking_the_device_ends_the_session_with_the_capture_note()
    {
        using var rig = new VoiceSessionRig();
        rig.QuietRoom();
        rig.Session.Activate();
        rig.Advance(0.5);
        rig.Capture.RaiseDeviceEvent(CaptureDeviceEventKind.InUse);
        rig.Settle();
        Assert.Equal(VoiceSessionState.Rest, rig.Session.State);
        Assert.Equal([VoiceNoteKind.CaptureFailed], rig.Notes);
        Assert.False(rig.Capture.IsRunning);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-17.6: an engine failure ends the session with the engine note and discards the words
    public void A_failing_live_pass_ends_the_session_with_the_engine_note()
    {
        using var rig = new VoiceSessionRig();
        rig.Speak(VoiceSessionRig.SwitchTheme());
        rig.Engine.FailNextDecode(new InvalidOperationException("engine fault"));
        rig.Session.Activate();
        rig.Advance(2.0);
        Assert.Equal(VoiceSessionState.Rest, rig.Session.State);
        Assert.Equal([VoiceNoteKind.EngineFailed], rig.Notes);
        Assert.False(rig.Capture.IsRunning);
        Assert.Empty(rig.Session.Words);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-17.6: a failing final pass ends the session the same way (nothing lands)
    public void A_failing_final_pass_ends_the_session_with_the_engine_note()
    {
        using var rig = new VoiceSessionRig();
        rig.Speak(VoiceSessionRig.SwitchTheme());
        rig.Session.Activate();
        rig.Advance(2.7);
        rig.Engine.FailNextDecode(new InvalidOperationException("engine fault"));
        rig.Session.Activate();
        rig.Settle();
        Assert.Equal(VoiceSessionState.Rest, rig.Session.State);
        Assert.Equal([VoiceNoteKind.EngineFailed], rig.Notes);
        Assert.DoesNotContain(VoiceSessionState.Landing, rig.Visited);
    }

    [Theory]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-7.13, V-7.14: the engine gets the resolved language: forced, auto, or Windows' display language
    [InlineData(null, VoiceLangMode.Auto, "en-US", "auto", false)]
    [InlineData("ur", VoiceLangMode.Auto, "en-US", "ur", false)]
    [InlineData(null, VoiceLangMode.Windows, "hi-IN", "hi", false)]
    [InlineData(null, VoiceLangMode.Windows, "sw-KE", "auto", false)]
    [InlineData(null, VoiceLangMode.Show, "en-US", "auto", true)]
    [InlineData("auto", VoiceLangMode.Show, "en-US", "auto", true)]
    public void The_session_decodes_in_the_language_Resolve_gives(string? lang, VoiceLangMode mode, string windowsUi, string expected, bool named)
    {
        using var rig = new VoiceSessionRig();
        rig.Typing = new VoiceTypingSettings { Lang = lang };
        rig.Orb = new VoiceOrbSettings { LangMode = mode };
        rig.WindowsUiLanguage = windowsUi;
        rig.Speak(VoiceSessionRig.SwitchTheme());
        rig.Session.Activate();
        rig.Advance(1.2);
        Assert.Equal(expected, rig.Session.ResolvedLanguage);
        Assert.Equal(named, rig.Session.LanguageNamed);
        Assert.NotEmpty(rig.Engine.Decodes);
        Assert.All(rig.Engine.Decodes, d => Assert.Equal(expected, d.Request.Language));
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-12.10: the language the engine reports is kept for the pill; nothing before a word has been heard
    public void The_language_the_engine_reports_is_exposed_once_a_word_is_heard()
    {
        using var rig = new VoiceSessionRig();
        rig.Speak(VoiceSessionRig.SwitchTheme("hi"));
        rig.Session.Activate();
        rig.Advance(0.4);
        Assert.Null(rig.Session.DetectedLanguage);
        rig.Advance(3.0);
        Assert.Equal("hi", rig.Session.DetectedLanguage);
    }

    [Theory]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T121")]
    [Trait("Decision", "ED-267")] // V-12.9: the label text comes from the picked set, read when asked
    [InlineData(VoicePillWords.L3, "Hearing you" + E)]
    [InlineData(VoicePillWords.L4, "Speak now")]
    [InlineData(VoicePillWords.L6, "Listening")]
    public void The_label_text_uses_the_picked_word_set(VoicePillWords set, string expected)
    {
        using var rig = new VoiceSessionRig();
        rig.Orb = new VoiceOrbSettings { Labels = set };
        rig.Session.Activate();
        Assert.Equal(expected, rig.Session.LabelText);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-4.2, V-5.1, V-5.2: a 48 kHz stereo mix format is averaged to mono and handed to the resampler seam; 16 kHz mono never is
    public void A_48_kHz_stereo_capture_reaches_the_recogniser_through_the_resampler_seam()
    {
        var resampler = new DecimatingResampler();
        using var rig = new VoiceSessionRig { Resampler = resampler };
        var script = VoiceSessionRig.SwitchTheme();
        rig.Capture = new ScriptedCapture(new CaptureFormat(48000, 2));
        rig.Capture.PlayOnStart(script.Signal(48000), TimeSpan.FromSeconds(120));
        rig.Vad = script.CreateVad();
        rig.Engine = script.CreateEngine();
        rig.Session.Activate();
        rig.Advance(6.0);
        Assert.True(resampler.Calls > 0);
        Assert.Equal(48000, resampler.LastInputRate);
        Assert.Equal("switch the theme please.", rig.FinalText);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-2.5: a mix format that needs a resampler nobody bound fails closed into the capture note, not a crash in the audio callback
    public void A_capture_the_session_cannot_convert_ends_with_the_capture_note()
    {
        using var rig = new VoiceSessionRig();
        rig.Capture = new ScriptedCapture(new CaptureFormat(44100, 1));
        rig.Capture.PlayOnStart(ScriptedSignals.RoomNoise, TimeSpan.FromSeconds(10));
        rig.Session.Activate();
        rig.Advance(1.0);
        Assert.Equal(VoiceSessionState.Rest, rig.Session.State);
        Assert.Equal([VoiceNoteKind.CaptureFailed], rig.Notes);
        Assert.False(rig.Capture.IsRunning);
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T30")]
    [Trait("Decision", "ED-267")] // V-4.7: the session's audio is cleared when it ends
    public void The_audio_is_cleared_when_the_session_ends()
    {
        using var rig = new VoiceSessionRig();
        rig.Speak(VoiceSessionRig.SwitchTheme());
        rig.Session.Activate();
        rig.Advance(3.0);
        Assert.True(rig.Session.BufferedAudio > TimeSpan.Zero);
        rig.Session.Cancel();
        rig.Settle();
        Assert.Equal(TimeSpan.Zero, rig.Session.BufferedAudio);
    }
}
