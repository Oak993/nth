using System.Reflection;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-18 (DESIGN.md rev 5, 15.2 SpeechOut "local voices only; no cloud voice", 2.4, 16 "done when": speech output is local only; nothing is spoken unprompted).
// The synth and the speakers are the scripted doubles: no voice is made and no sound plays. Time is the player's own clock, moved by the test.
[Trait("Feature", "AS-18")]
[Trait("Decision", "ED-NEW-dextra-as18-7")]
public sealed class SpeechOutTests
{
    private static (SpeechOut Out, ScriptedSpeechSynth Synth, ScriptedSpeechPlayer Player, VoiceEchoReference Reference, List<ScriptedSpeechPlayer> Made) Rig(SpeechOutOptions? options = null)
    {
        var synth = new ScriptedSpeechSynth();
        var player = new ScriptedSpeechPlayer();
        var reference = new VoiceEchoReference();
        var made = new List<ScriptedSpeechPlayer>();
        var speech = new SpeechOut(synth, () =>
        {
            made.Add(player);
            return player;
        }, reference, options);
        return (speech, synth, player, reference, made);
    }

    [Fact]
    public async Task Nothing_is_spoken_without_a_turn_and_nothing_is_made_or_played()
    {
        var (speech, synth, _, reference, made) = Rig();
        using var disposeSpeech = speech;

        await Assert.ThrowsAsync<InvalidOperationException>(() => speech.SpeakAsync("Good morning."));

        Assert.Empty(synth.Requests);
        Assert.Empty(made);
        Assert.Empty(speech.Said);
        Assert.False(speech.Speaking);
        Assert.Equal(0, reference.Written);
    }

    [Fact]
    public async Task A_turn_needs_something_the_owner_said_and_ends_when_it_is_disposed()
    {
        var (speech, _, _, _, _) = Rig();
        using var disposeSpeech = speech;
        Assert.Throws<ArgumentException>(() => speech.BeginTurn("  "));

        var turn = speech.BeginTurn("what is running");
        turn.Dispose();

        await Assert.ThrowsAsync<InvalidOperationException>(() => speech.SpeakAsync("Two missions."));
    }

    [Fact]
    public async Task A_reply_in_a_turn_plays_out_word_by_word_and_ends_Finished()
    {
        var (speech, synth, player, _, made) = Rig();
        using var disposeSpeech = speech;
        using var turn = speech.BeginTurn("what is running");

        var done = speech.SpeakAsync("Two missions are running.");

        Assert.True(speech.Speaking);
        Assert.Equal("Two missions are running.", speech.Text);
        Assert.Equal(0, speech.WordIndex);
        Assert.Equal([("Two missions are running.", "scripted-1", 1.0)], synth.Requests);
        // 0.35 s a word: the second word starts at 0.35 s, the fourth at 1.05 s
        player.Advance(TimeSpan.FromSeconds(0.40));
        Assert.Equal(1, speech.WordIndex);
        player.Advance(TimeSpan.FromSeconds(0.70));
        Assert.Equal(3, speech.WordIndex);
        Assert.False(done.IsCompleted);
        player.Advance(TimeSpan.FromSeconds(1.0));

        Assert.Equal(SpeechEnd.Finished, await done);
        Assert.False(speech.Speaking);
        Assert.Equal(-1, speech.WordIndex);
        Assert.Equal(0, speech.Level);
        Assert.True(made[0].Disposed);
        Assert.Equal(["Two missions are running."], speech.Said);
    }

    [Fact]
    public async Task The_level_follows_the_sound_at_the_audible_position_and_is_silent_in_the_gaps()
    {
        var (speech, _, player, _, _) = Rig();
        using var disposeSpeech = speech;
        using var turn = speech.BeginTurn("hello");
        var done = speech.SpeakAsync("One two");

        player.Advance(TimeSpan.FromSeconds(0.15)); // in the middle of the first word's tone
        Assert.InRange(speech.Level, 0.5, 1.0);
        player.Advance(TimeSpan.FromSeconds(0.17)); // 0.32 s: the gap between the words
        Assert.InRange(speech.Level, 0.0, 0.2);
        player.Advance(TimeSpan.FromSeconds(5));
        await done;
    }

    [Fact]
    public async Task What_is_handed_to_the_speakers_goes_into_the_echo_reference_in_step()
    {
        var (speech, _, player, reference, _) = Rig();
        using var disposeSpeech = speech;
        using var turn = speech.BeginTurn("hello");
        var done = speech.SpeakAsync("One two three");

        // the card is kept filled 60 ms ahead of what is audible: 0.5 s played is 0.56 s handed, which is 8960 samples at 16 kHz
        player.Advance(TimeSpan.FromSeconds(0.5));
        Assert.Equal(8960, reference.Written);
        Assert.True(reference.AudibleWithin(VoiceEchoReference.Rate));
        player.Advance(TimeSpan.FromSeconds(5));
        await done;
        Assert.Equal(16_800, reference.Written);
    }

    [Fact]
    public async Task Stop_cuts_the_speech_at_once_and_the_reply_ends_Cut()
    {
        var (speech, _, player, _, _) = Rig();
        using var disposeSpeech = speech;
        using var turn = speech.BeginTurn("hello");
        var done = speech.SpeakAsync("A long answer that is still being said.");
        player.Advance(TimeSpan.FromSeconds(0.4));

        speech.Stop();

        Assert.Equal(SpeechEnd.Cut, await done);
        Assert.True(player.StopCount >= 1);
        Assert.False(speech.Speaking);
        speech.Stop(); // again, with nothing speaking: nothing happens
    }

    [Fact]
    public async Task A_new_reply_cuts_the_old_one()
    {
        var (speech, _, player, _, _) = Rig();
        using var disposeSpeech = speech;
        using var turn = speech.BeginTurn("hello");
        var first = speech.SpeakAsync("First answer here.");
        var second = speech.SpeakAsync("Second one.");

        Assert.Equal(SpeechEnd.Cut, await first);
        Assert.Equal("Second one.", speech.Text);
        player.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(SpeechEnd.Finished, await second);
    }

    [Fact]
    public async Task A_voice_that_cannot_be_made_ends_the_reply_Failed_and_never_throws()
    {
        var (speech, synth, _, _, made) = Rig();
        using var disposeSpeech = speech;
        using var turn = speech.BeginTurn("hello");
        synth.Fail = new InvalidOperationException("the voice is broken");

        Assert.Equal(SpeechEnd.Failed, await speech.SpeakAsync("Anything."));

        Assert.Empty(made);
        Assert.False(speech.Speaking);
    }

    [Fact]
    public async Task Only_voices_installed_on_this_PC_are_ever_used_and_no_voice_means_no_speech()
    {
        var (speech, synth, _, _, made) = Rig();
        using var disposeSpeech = speech;
        using var turn = speech.BeginTurn("hello");
        synth.Installed.Clear();
        synth.Installed.Add(new SynthVoice("edge-1", "Microsoft Aria Online (Natural)", "en-US"));
        synth.Installed.Add(new SynthVoice("cloud-1", "Some Cloud Voice", "en-US"));
        synth.Installed.Add(new SynthVoice("https://speech.example/voice", "Fetched", "en-US"));
        synth.Installed.Add(new SynthVoice("remote-1", "Remote", "en-US", Local: false));

        Assert.Equal(SpeechEnd.Failed, await speech.SpeakAsync("Hello there."));

        Assert.Empty(synth.Requests);
        Assert.Empty(made);
    }

    [Fact]
    public void The_voice_is_chosen_from_the_installed_ones_by_the_owners_pick_then_the_language_then_a_natural_one()
    {
        SynthVoice[] voices =
        [
            new("a", "Microsoft David", "en-US"),
            new("b", "Microsoft Zira Natural", "en-US"),
            new("c", "Microsoft Hedda", "de-DE"),
            new("d", "Microsoft Aria Online (Natural)", "en-US"),
        ];

        Assert.Equal("b", SpeechVoices.Choose(voices, null, "en-US")!.Id);
        Assert.Equal("a", SpeechVoices.Choose(voices, "a", "en-US")!.Id);
        Assert.Equal("a", SpeechVoices.Choose(voices, "Microsoft David", null)!.Id);
        Assert.Equal("c", SpeechVoices.Choose(voices, null, "de-DE")!.Id);
        Assert.Equal("b", SpeechVoices.Choose(voices, "d", "en-US")!.Id);                    // an online voice is never the pick
        Assert.Equal("b", SpeechVoices.Choose(voices, "missing", "en-GB")!.Id);              // another English will do
        Assert.Equal("a", SpeechVoices.Choose([voices[0], voices[2]], null, "fr-FR")!.Id);   // nothing French: the first installed one
        Assert.Null(SpeechVoices.Choose([voices[3]], null, "en-US"));
        Assert.Null(SpeechVoices.Choose([], null, null));
    }

    [Fact]
    public void The_speech_types_hold_and_call_nothing_that_reaches_a_network()
    {
        var types = new[] { typeof(SpeechOut), typeof(WasapiSpeechPlayer), typeof(ISpeechSynth), typeof(ISpeechPlayer), typeof(SpeechVoices) }
            .SelectMany(Nested).Distinct().ToList();
        var reached = new List<string>();
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        foreach (var type in types)
        {
            foreach (var field in type.GetFields(all)) Note(reached, type, field.FieldType);
            foreach (var property in type.GetProperties(all)) Note(reached, type, property.PropertyType);
            foreach (var method in type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)))
            {
                foreach (var parameter in method.GetParameters()) Note(reached, type, parameter.ParameterType);
                if (method is MethodInfo info) Note(reached, type, info.ReturnType);
                if (method.GetMethodBody() is { } body)
                {
                    foreach (var local in body.LocalVariables) Note(reached, type, local.LocalType);
                }
            }
        }
        Assert.Empty(reached);

        static IEnumerable<Type> Nested(Type type) => new[] { type }.Concat(type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).SelectMany(Nested));

        static void Note(List<string> reached, Type owner, Type type)
        {
            foreach (var part in Parts(type))
            {
                var space = part.Namespace ?? "";
                if (space.StartsWith("System.Net", StringComparison.Ordinal) || space.StartsWith("Windows.Web", StringComparison.Ordinal)
                    || space.StartsWith("Windows.Networking", StringComparison.Ordinal) || part.Name.Contains("HttpClient", StringComparison.Ordinal))
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
