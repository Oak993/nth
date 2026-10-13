using Marrow.App.Assistant;
using Marrow.App.Safety;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Services.Assistant;
using Marrow.Testing;
using static Marrow.App.Tests.Fixtures.HomeSamples;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-18 (DESIGN.md rev 5, 2.4, 12.6, 16 "done when"): a whole voice conversation on virtual time (VoiceRig): synthetic voices for the owner and for
// Dextra, Dextra's echo in the microphone at -6 dB, a scripted speech engine and speaker. Barge-in stops the speech within 200 ms; Dextra's own voice
// never does; Stop everything, Pause everything and Resume everything act at once; voice never answers a Send, Pay, Delete, Install or Commitment card;
// nothing is said unprompted; the speech engine is the local one.
[Trait("Feature", "AS-18")]
[Trait("Decision", "ED-NEW-dextra-as18-8")]
public sealed class VoiceConversationTests
{
    private static float[] ShortReply() => SyntheticSpeech.Voice(7, 0.8, 0.1);

    private static DecisionItem Card(DecisionKind kind, DecisionWeight weight, ActionClass? cls, string title, params string[] options) =>
        new(Guid.NewGuid(), kind, weight, title) { Class = cls, Options = new ValueList<string>(options), AssistantId = "dextra", Why = "It asked.", Raised = Now };

    [Fact]
    public void The_owner_speaking_over_Dextra_stops_its_speech_within_200_ms_and_is_heard()
    {
        using var rig = new VoiceRig();
        rig.Conversation.Start(VoiceStart.Toggle);
        rig.Ask("what is running");
        Assert.Equal(VoicePhase.Speaking, rig.Conversation.Phase);
        rig.Wait(1.0);
        Assert.Equal(0, rig.Player.StopCount);

        var owner = SyntheticSpeech.Voice(3, 2.0, 0.15);
        var first = Array.FindIndex(owner, s => Math.Abs(s) > 0.02f);
        var startedAt = rig.Time.GetUtcNow();
        rig.Heard.Enqueue("stop everything");
        rig.Say(owner);

        Assert.True(rig.Player.StopCount >= 1, "the owner talking over Dextra must stop it");
        // the chunk that completed the barge-in ended one tick after the clock the speaker stamped; the speaker's own flush is 40 ms of the 200
        var latency = rig.Player.StoppedAt!.Value - startedAt + VoiceRig.Tick - TimeSpan.FromSeconds(first / 16000.0);
        Assert.InRange(latency.TotalMilliseconds, 0, 200 - 40);
        rig.Wait(1.5);
        Assert.Equal(["StopAll Voice"], rig.Controls.Calls);
    }

    [Fact]
    public void Dextras_own_speech_fed_back_at_minus_6_dB_never_triggers_barge_in_through_the_whole_conversation()
    {
        using var rig = new VoiceRig();
        rig.Conversation.Start(VoiceStart.Toggle);
        rig.Ask("what is running");
        Assert.Equal(VoicePhase.Speaking, rig.Conversation.Phase);

        var finished = rig.WaitUntil(() => rig.Conversation.Phase != VoicePhase.Speaking, 15);

        Assert.True(finished);
        Assert.Equal(0, rig.Player.StopCount);
        Assert.Equal(VoicePhase.Listening, rig.Conversation.Phase); // a follow-up is listened for
        Assert.Empty(rig.Brain.Asked);
    }

    [Theory]
    [InlineData(0.35, 400)]
    [InlineData(0.5, 2400)]
    [InlineData(0.7, 1200)]
    public void The_echo_never_triggers_it_at_other_gains_and_delays_either(double gain, int delay)
    {
        using var rig = new VoiceRig();
        rig.EchoGain = gain;
        rig.EchoDelay = delay;
        rig.Conversation.Start(VoiceStart.Toggle);
        rig.Ask("what is running");

        rig.WaitUntil(() => rig.Conversation.Phase != VoicePhase.Speaking, 15);

        Assert.Equal(0, rig.Player.StopCount);
    }

    [Fact]
    public void Stop_everything_acts_at_once_without_a_question_and_Dextra_says_what_it_did()
    {
        using var rig = new VoiceRig { Said = ShortReply() };
        rig.Conversation.Start(VoiceStart.Toggle);

        rig.Ask("Stop everything.");

        Assert.Equal(["StopAll Voice"], rig.Controls.Calls);
        Assert.Equal(VoiceConversation.StoppedLine, rig.Conversation.Reply);
        Assert.Equal([VoiceConversation.StoppedLine], rig.Speech.Said);
        Assert.Empty(rig.Brain.Asked);
        Assert.Empty(rig.Shown);
    }

    [Fact]
    public void Pause_everything_and_Resume_everything_work_and_the_answer_is_two_sentences_at_most()
    {
        using var rig = new VoiceRig { Said = ShortReply() };
        rig.Conversation.Start(VoiceStart.Toggle);

        rig.Ask("pause everything");
        rig.WaitUntil(() => rig.Conversation.Phase == VoicePhase.Listening, 5);
        rig.Ask("resume everything");

        Assert.Equal(["PauseAll Voice: Pause everything", "ResumeAll"], rig.Controls.Calls);
        Assert.Equal("Resumed 2 tasks.", rig.Conversation.Reply);
        Assert.True(SpokenText.CountSentences(rig.Conversation.Reply) <= 2);
        Assert.Empty(rig.Brain.Asked);
    }

    [Theory]
    [InlineData(ActionClass.Outward, "Tap Send on the screen to send it.")]
    [InlineData(ActionClass.Money, "Tap Pay on the screen to pay.")]
    [InlineData(ActionClass.Destructive, "Tap Delete on the screen to delete it.")]
    [InlineData(ActionClass.Install, "Tap Install on the screen to install it.")]
    [InlineData(ActionClass.Commitment, "Tap Commit on the screen to commit.")]
    public void Voice_never_answers_a_Send_Pay_Delete_Install_or_Commitment_card_it_opens_it_on_the_screen(ActionClass cls, string line)
    {
        foreach (var said in new[] { "yes", "do it", "go ahead", "send it", "no", "don't", "approve it", "the first one" })
        {
            using var rig = new VoiceRig { Said = ShortReply() };
            var card = Card(DecisionKind.Approval, DecisionWeight.Irreversible, cls, "The weekly update for the team");
            rig.Decisions.Add(card);
            rig.Conversation.Start(VoiceStart.Toggle);
            rig.Ask("what needs me");
            Assert.Equal(card.Id, rig.Conversation.Focus?.Id);
            rig.WaitUntil(() => rig.Conversation.Phase == VoicePhase.Listening, 5);

            rig.Ask(said, 0.6);

            Assert.Empty(rig.Decisions.Answers);
            Assert.Equal([card.Id], rig.Shown);
            Assert.Equal(line, rig.Conversation.Reply);
            Assert.Empty(rig.Brain.Asked);
        }
    }

    [Fact]
    public void A_yes_with_no_card_read_out_is_an_ordinary_message_and_answers_nothing()
    {
        using var rig = new VoiceRig { Said = ShortReply() };
        rig.Decisions.Add(Card(DecisionKind.Approval, DecisionWeight.Reversible, ActionClass.ReadWeb, "Channel A wants to read the vendor site"));
        rig.Conversation.Start(VoiceStart.Toggle);

        rig.Ask("yes");

        Assert.Empty(rig.Decisions.Answers);
        Assert.Equal(["yes"], rig.Brain.Asked);
    }

    [Fact]
    public void A_card_voice_may_answer_is_answered_after_Dextra_reads_it_out_and_not_before()
    {
        using var rig = new VoiceRig { Said = ShortReply() };
        var reserve = Card(DecisionKind.Reserve, DecisionWeight.Reversible, null, "Espresso research needs 4 percent more on Claude", "Move the rest to Codex A", "Wait for the reset", "Stop the task");
        rig.Decisions.Add(reserve);
        rig.Conversation.Start(VoiceStart.Toggle);

        rig.Ask("what needs me");
        Assert.Equal(reserve.Id, rig.Conversation.Focus?.Id);
        Assert.Contains("Wait for the reset", rig.Conversation.Reply, StringComparison.Ordinal);
        rig.WaitUntil(() => rig.Conversation.Phase == VoicePhase.Listening, 5);
        rig.Ask("wait for the reset", 1.0);

        var answer = Assert.Single(rig.Decisions.Answers);
        Assert.Equal((reserve.Id, DecisionAnswer.Pick("Wait for the reset")), answer);
        Assert.Equal("Done: Wait for the reset.", rig.Conversation.Reply);
        Assert.Null(rig.Conversation.Focus);
    }

    [Fact]
    public void Allow_once_by_voice_works_on_a_read_only_action_the_card_names()
    {
        using var rig = new VoiceRig { Said = ShortReply() };
        var read = Card(DecisionKind.Approval, DecisionWeight.Reversible, ActionClass.ReadWeb, "Channel A wants to read the vendor site");
        rig.Decisions.Add(read);
        rig.Conversation.Start(VoiceStart.Toggle);
        rig.Ask("what needs me");
        rig.WaitUntil(() => rig.Conversation.Phase == VoicePhase.Listening, 5);

        rig.Ask("yes go ahead", 1.0);

        Assert.Equal([(read.Id, DecisionAnswer.AllowOnce)], rig.Decisions.Answers);
    }

    [Fact]
    public void A_name_that_fits_two_Asturs_is_asked_aloud_and_answered_by_saying_the_name()
    {
        using var rig = new VoiceRig { Said = ShortReply() };
        var which = Card(DecisionKind.Question, DecisionWeight.Question, null, "Which one: Channel A or Channel B", "Channel A", "Channel B");
        rig.Decisions.Add(which);
        rig.Brain.Reply = _ => new VoiceAnswer("Channel A or Channel B?") { Card = which };
        rig.Conversation.Start(VoiceStart.Toggle);

        rig.Ask("channel start the next video");
        Assert.Equal("Channel A or Channel B?", rig.Conversation.Reply);
        Assert.Equal(which.Id, rig.Conversation.Focus?.Id);
        rig.WaitUntil(() => rig.Conversation.Phase == VoicePhase.Listening, 5);
        rig.Ask("channel b", 1.0);

        Assert.Equal([(which.Id, DecisionAnswer.Pick("Channel B"))], rig.Decisions.Answers);
    }

    [Fact]
    public void A_status_question_is_answered_from_the_board_in_three_sentences_at_most_and_asks_no_model()
    {
        using var rig = new VoiceRig { Said = ShortReply() };
        rig.Jobs.Set(Enumerable.Range(1, 20).Select(i => Job($"Mission number {i}. With a stop in its title!", MissionState.Working)).ToArray());
        for (var i = 0; i < 9; i++) rig.Decisions.Add(Card(DecisionKind.Question, DecisionWeight.Question, null, $"Question {i}. Which one?", "A", "B"));
        rig.Conversation.Start(VoiceStart.Toggle);

        rig.Ask("what's running and does anything need me");

        Assert.InRange(SpokenText.CountSentences(rig.Conversation.Reply), 1, 3);
        Assert.StartsWith("20 missions are running.", rig.Conversation.Reply, StringComparison.Ordinal);
        Assert.Contains("9 things need you.", rig.Conversation.Reply, StringComparison.Ordinal);
        Assert.Empty(rig.Brain.Asked);
    }

    [Fact]
    public void Nothing_is_said_unprompted_whatever_happens_on_the_board_while_the_sheet_is_open()
    {
        using var rig = new VoiceRig();
        rig.Conversation.Start(VoiceStart.Toggle);

        rig.Wait(3);
        rig.Jobs.Set(Job("Fix the login timeout", MissionState.NeedsYou));
        rig.Decisions.Add(Card(DecisionKind.Approval, DecisionWeight.Irreversible, ActionClass.Outward, "Send the weekly update"));
        rig.Controls.Raise();
        rig.Wait(3);
        rig.Jobs.Replace(Moved(rig.Jobs.Jobs[0], MissionState.Done));

        Assert.Empty(rig.Synth.Requests);
        Assert.Empty(rig.Speech.Said);
        Assert.Equal("", rig.Conversation.Reply);
        Assert.Equal(0, rig.Player.StopCount);
        Assert.False(rig.Speech.Speaking);
    }

    [Fact]
    public void A_held_button_waits_for_the_release_however_long_the_pauses_and_then_ends_the_request()
    {
        using var rig = new VoiceRig { Said = ShortReply() };
        rig.Heard.Enqueue("pause everything");
        rig.Conversation.Start(VoiceStart.Hold);

        rig.Say(SyntheticSpeech.Voice(2, 1.0, 0.12));
        rig.Wait(3.0); // far past 1.2 s of silence
        Assert.Equal(VoicePhase.Listening, rig.Conversation.Phase);
        Assert.Empty(rig.Controls.Calls);

        rig.Conversation.Release();

        Assert.Equal(["PauseAll Voice: Pause everything"], rig.Controls.Calls);
    }

    [Fact]
    public void A_hold_with_no_speech_rests_and_says_to_speak()
    {
        using var rig = new VoiceRig();
        rig.Conversation.Start(VoiceStart.Hold);
        rig.Wait(1.0);

        rig.Conversation.Release();

        Assert.Equal(VoicePhase.Idle, rig.Conversation.Phase);
        Assert.Equal("Hold the button and speak.", rig.Conversation.Note);
        Assert.False(rig.Capture.IsRunning);
    }

    [Fact]
    public void After_a_held_reply_the_microphone_shuts_and_after_a_toggled_one_it_listens_for_a_follow_up()
    {
        using var held = new VoiceRig { Said = ShortReply() };
        held.Conversation.Start(VoiceStart.Hold);
        held.Heard.Enqueue("what is running");
        held.Say(SyntheticSpeech.Voice(2, 1.0, 0.12));
        held.Wait(0.1);
        held.Conversation.Release();
        held.WaitUntil(() => held.Conversation.Phase != VoicePhase.Speaking, 5);
        Assert.Equal(VoicePhase.Idle, held.Conversation.Phase);
        Assert.False(held.Capture.IsRunning);

        using var toggled = new VoiceRig { Said = ShortReply() };
        toggled.Conversation.Start(VoiceStart.Toggle);
        toggled.Ask("what is running");
        toggled.WaitUntil(() => toggled.Conversation.Phase != VoicePhase.Speaking, 5);
        Assert.Equal(VoicePhase.Listening, toggled.Conversation.Phase);
        Assert.True(toggled.Capture.IsRunning);
        toggled.Wait(8.5); // the follow-up window is 8 s
        Assert.Equal(VoicePhase.Idle, toggled.Conversation.Phase);
        Assert.False(toggled.Capture.IsRunning);
    }

    [Fact]
    public void Pressing_the_chord_again_ends_the_request_now_and_pressing_it_while_Dextra_speaks_cuts_it_and_listens()
    {
        using var rig = new VoiceRig();
        rig.Conversation.Start(VoiceStart.Toggle);
        rig.Heard.Enqueue("what is running");
        rig.Say(SyntheticSpeech.Voice(2, 1.0, 0.12));
        rig.Wait(0.3);
        Assert.Equal(VoicePhase.Listening, rig.Conversation.Phase);

        rig.Conversation.Toggle();

        Assert.Equal(VoicePhase.Speaking, rig.Conversation.Phase);
        rig.Wait(0.5);
        rig.Conversation.Toggle();
        Assert.Equal(VoicePhase.Listening, rig.Conversation.Phase);
        Assert.True(rig.Player.StopCount >= 1);
    }

    [Fact]
    public void Stop_all_cuts_the_speech_ends_the_turn_and_shuts_the_microphone_without_waiting()
    {
        using var rig = new VoiceRig();
        rig.Conversation.Start(VoiceStart.Toggle);
        rig.Ask("what is running");
        Assert.Equal(VoicePhase.Speaking, rig.Conversation.Phase);

        ((IStopScope)rig.Conversation).Stop();

        Assert.True(rig.Player.StopCount >= 1);
        Assert.Equal(VoicePhase.Idle, rig.Conversation.Phase);
        Assert.False(rig.Capture.IsRunning);
        Assert.Equal("Voice", ((IStopScope)rig.Conversation).Name);
    }

    [Fact]
    public void Closing_the_sheet_stops_everything_and_nothing_is_listened_to_afterwards()
    {
        using var rig = new VoiceRig();
        rig.Conversation.Start(VoiceStart.Toggle);
        rig.Ask("what is running");

        rig.Conversation.Close();

        Assert.Equal(VoicePhase.Closed, rig.Conversation.Phase);
        Assert.False(rig.Conversation.IsOpen);
        Assert.False(rig.Capture.IsRunning);
        Assert.True(rig.Player.StopCount >= 1);
        Assert.Equal("", rig.Conversation.Reply);
        rig.Capture.Push(new float[1600]);
        Assert.Equal(VoicePhase.Closed, rig.Conversation.Phase);
    }

    [Fact]
    public void Without_a_speech_model_or_with_the_microphone_blocked_the_sheet_says_why_and_the_microphone_is_never_opened()
    {
        using var noModel = new VoiceRig { Model = null };
        noModel.Conversation.Start(VoiceStart.Toggle);
        Assert.Equal(VoicePhase.Idle, noModel.Conversation.Phase);
        Assert.Contains("speech model", noModel.Conversation.Note, StringComparison.Ordinal);
        Assert.Equal(0, noModel.Capture.StartCount);

        using var denied = new VoiceRig();
        denied.Microphone.Current = MicrophoneAccessStatus.Denied;
        denied.Conversation.Start(VoiceStart.Toggle);
        Assert.Contains("Microphone access is off", denied.Conversation.Note, StringComparison.Ordinal);
        Assert.Equal(0, denied.Capture.StartCount);

        using var absent = new VoiceRig();
        absent.Microphone.Current = MicrophoneAccessStatus.NoDevice;
        absent.Conversation.Start(VoiceStart.Toggle);
        Assert.Equal("No microphone was found.", absent.Conversation.Note);

        using var broken = new VoiceRig();
        broken.Capture.FailStart = new InvalidOperationException("in use");
        broken.Conversation.Start(VoiceStart.Toggle);
        Assert.Equal("The microphone could not be opened.", broken.Conversation.Note);
        Assert.Equal(VoicePhase.Idle, broken.Conversation.Phase);
    }

    [Fact]
    public void Starting_a_conversation_ends_a_dictation_first_and_a_second_start_changes_nothing()
    {
        using var rig = new VoiceRig();
        rig.Conversation.Start(VoiceStart.Toggle);
        rig.Conversation.Start(VoiceStart.Hold);

        Assert.Equal(1, rig.Preempted);
        Assert.Equal(1, rig.Capture.StartCount);
        Assert.Equal(VoiceStart.Toggle, rig.Conversation.Mode);
    }

    [Fact]
    public void Nothing_heard_is_said_so_and_a_request_that_cannot_be_understood_is_not_sent_to_Dextra()
    {
        using var rig = new VoiceRig();
        rig.Conversation.Start(VoiceStart.Toggle);

        rig.Ask("", 1.2);

        Assert.Equal("I did not catch that.", rig.Conversation.Note);
        Assert.Empty(rig.Brain.Asked);
        Assert.Equal(VoicePhase.Listening, rig.Conversation.Phase);
    }

    [Fact]
    public void A_request_for_Dextra_goes_to_the_brain_and_the_answer_is_cleaned_and_said_in_the_Asturs_own_voice()
    {
        using var rig = new VoiceRig { Said = ShortReply() };
        rig.Synth.Installed.Add(new SynthVoice("astur-voice", "Astur Voice", "en-US"));
        rig.Brain.Reply = _ => new VoiceAnswer("**Done.** The video is queued, see [the board](https://example.com).") { Voice = "Astur Voice", Speaker = "Channel A" };
        rig.Conversation.Start(VoiceStart.Toggle);

        rig.Ask("Channel A, start the next video");

        Assert.Equal(["Channel A, start the next video"], rig.Brain.Asked);
        Assert.Equal("Done. The video is queued, see the board.", rig.Conversation.Reply);
        Assert.Equal("Channel A", rig.Conversation.Speaker);
        Assert.Equal("astur-voice", rig.Synth.Requests[^1].VoiceId);
    }

    [Fact]
    public void A_reply_takes_the_stop_keys_lease_while_it_speaks_and_a_missing_key_is_said_but_does_not_silence_it()
    {
        using var rig = new VoiceRig { Said = ShortReply() };
        rig.Conversation.Start(VoiceStart.Toggle);
        rig.Ask("what is running");
        Assert.Equal(1, rig.Leases);
        Assert.Equal(1, rig.LeasesHeld);
        rig.WaitUntil(() => rig.Conversation.Phase != VoicePhase.Speaking, 5);
        Assert.Equal(0, rig.LeasesHeld);

        using var taken = new VoiceRig { Said = ShortReply(), LeaseAvailable = false };
        taken.Conversation.Start(VoiceStart.Toggle);
        taken.Ask("what is running");
        Assert.Equal(VoicePhase.Speaking, taken.Conversation.Phase);
        Assert.Contains("stop key", taken.Conversation.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void The_level_follows_the_microphone_while_listening_and_Dextras_speech_while_it_speaks()
    {
        using var rig = new VoiceRig();
        rig.Conversation.Start(VoiceStart.Toggle);
        Assert.Equal(0, rig.Conversation.Level, 3);
        rig.Say(SyntheticSpeech.Voice(2, 0.4, 0.2));
        var heardLevel = rig.Conversation.Level;
        Assert.InRange(heardLevel, 0.2, 1.0);

        rig.Heard.Enqueue("what is running");
        rig.Say(SyntheticSpeech.Voice(2, 0.6, 0.12));
        rig.Wait(1.5);
        Assert.Equal(VoicePhase.Speaking, rig.Conversation.Phase);
        var levels = new List<double>();
        for (var i = 0; i < 100; i++)
        {
            rig.Step();
            levels.Add(rig.Conversation.Level);
        }
        Assert.Contains(levels, level => level > 0.5);
        Assert.Equal(rig.Speech.Level, rig.Conversation.Level, 3);
    }

    [Fact]
    public void The_conversation_hears_through_the_local_engine_only_and_holds_nothing_that_reaches_a_network()
    {
        var types = new[] { typeof(VoiceConversation), typeof(VoiceConversationOptions), typeof(VoiceStatus), typeof(HomeChatBrain) }
            .SelectMany(Nested).Distinct().ToList();
        var reached = new List<string>();
        const System.Reflection.BindingFlags all = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;
        foreach (var type in types)
        {
            foreach (var field in type.GetFields(all)) Note(reached, type, field.FieldType);
            foreach (var property in type.GetProperties(all)) Note(reached, type, property.PropertyType);
            foreach (var method in type.GetMethods(all).Cast<System.Reflection.MethodBase>().Concat(type.GetConstructors(all)))
            {
                foreach (var parameter in method.GetParameters()) Note(reached, type, parameter.ParameterType);
                if (method is System.Reflection.MethodInfo info) Note(reached, type, info.ReturnType);
                if (method.GetMethodBody() is { } body)
                {
                    foreach (var local in body.LocalVariables) Note(reached, type, local.LocalType);
                }
            }
        }
        Assert.Empty(reached);

        static IEnumerable<Type> Nested(Type type) => new[] { type }.Concat(type.GetNestedTypes(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).SelectMany(Nested));

        static void Note(List<string> reached, Type owner, Type type)
        {
            foreach (var part in Parts(type))
            {
                var space = part.Namespace ?? "";
                if (space.StartsWith("System.Net", StringComparison.Ordinal) || space.StartsWith("Marrow.Services.Port.Voice.Cloud", StringComparison.Ordinal)
                    || part.Name.Contains("HttpClient", StringComparison.Ordinal) || part.Name.Contains("VoiceCloudEngine", StringComparison.Ordinal)
                    || part.Name.Contains("OpenAi", StringComparison.Ordinal))
                    reached.Add($"{owner.Name} reaches {part.FullName}");
            }
        }

        static IEnumerable<Type> Parts(Type type)
        {
            yield return type;
            if (type.HasElementType && type.GetElementType() is { } element) foreach (var inner in Parts(element)) yield return inner;
            if (type.IsGenericType) foreach (var argument in type.GetGenericArguments()) foreach (var inner in Parts(argument)) yield return inner;
        }
    }
}
