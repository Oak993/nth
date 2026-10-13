using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-18 (DESIGN.md rev 5, 2.4 "What voice may answer", "Spoken status on request", 12.6 "Voice: Stop everything", 12.6b "Pause everything" and "Resume
// everything"): which spoken requests the voice layer answers itself. A command must be the whole request (a few filler words aside).
[Trait("Feature", "AS-18")]
[Trait("Decision", "ED-NEW-dextra-as18-4")]
public sealed class VoiceCommandsTests
{
    [Theory]
    [InlineData("Stop everything")]
    [InlineData("stop everything.")]
    [InlineData("STOP EVERYTHING!")]
    [InlineData("Dextra, stop everything now please")]
    [InlineData("hey dextra stop everything")]
    [InlineData("stop all")]
    [InlineData("Stop all the missions")]
    [InlineData("stop it all")]
    [InlineData("halt everything")]
    [InlineData("emergency stop")]
    public void Stop_everything_is_recognised_as_the_whole_request(string said) =>
        Assert.Equal(VoiceCommandKind.StopEverything, VoiceCommands.Parse(said).Kind);

    [Theory]
    [InlineData("I think you should stop everything")]
    [InlineData("don't stop everything")]
    [InlineData("do not stop everything")]
    [InlineData("stop the video")]
    [InlineData("stop everything on channel A")]
    [InlineData("stop everything except the store")]
    [InlineData("please do not stop all")]
    [InlineData("start the next video")]
    [InlineData("")]
    [InlineData("   ")]
    public void Anything_longer_or_negated_is_a_message_to_Dextra_and_not_a_stop(string said) =>
        Assert.Equal(VoiceCommandKind.None, VoiceCommands.Parse(said).Kind);

    [Theory]
    [InlineData("pause everything", VoiceCommandKind.PauseEverything)]
    [InlineData("Pause all", VoiceCommandKind.PauseEverything)]
    [InlineData("please pause all missions", VoiceCommandKind.PauseEverything)]
    [InlineData("hold everything", VoiceCommandKind.PauseEverything)]
    [InlineData("resume everything", VoiceCommandKind.ResumeEverything)]
    [InlineData("Resume all", VoiceCommandKind.ResumeEverything)]
    [InlineData("continue everything", VoiceCommandKind.ResumeEverything)]
    [InlineData("unpause all the tasks", VoiceCommandKind.ResumeEverything)]
    [InlineData("pause the video", VoiceCommandKind.None)]
    [InlineData("pause", VoiceCommandKind.None)]
    [InlineData("resume the voiceover", VoiceCommandKind.None)]
    [InlineData("don't pause everything", VoiceCommandKind.None)]
    public void Pause_everything_and_resume_everything_work(string said, VoiceCommandKind expected) =>
        Assert.Equal(expected, VoiceCommands.Parse(said).Kind);

    [Theory]
    [InlineData("What's running?", VoiceStatusAsk.Running)]
    [InlineData("what are you working on", VoiceStatusAsk.Running)]
    [InlineData("status", VoiceStatusAsk.Running)]
    [InlineData("What needs me?", VoiceStatusAsk.NeedsYou)]
    [InlineData("does anything need me", VoiceStatusAsk.NeedsYou)]
    [InlineData("what's waiting for me", VoiceStatusAsk.NeedsYou)]
    [InlineData("Read my digest", VoiceStatusAsk.Digest)]
    [InlineData("what's in my digest", VoiceStatusAsk.Digest)]
    [InlineData("what's running and does anything need me", VoiceStatusAsk.Running | VoiceStatusAsk.NeedsYou)]
    public void Status_questions_are_recognised_with_what_they_ask(string said, VoiceStatusAsk ask)
    {
        var command = VoiceCommands.Parse(said);
        Assert.Equal(VoiceCommandKind.Status, command.Kind);
        Assert.Equal(ask, command.Ask);
    }

    [Fact]
    public void A_long_request_that_mentions_a_status_phrase_is_a_message_to_Dextra()
    {
        Assert.Equal(VoiceCommandKind.None, VoiceCommands.Parse("plan my week around what is running on channel A and tell me which video should go out first on Friday").Kind);
        Assert.Equal(VoiceCommandKind.None, VoiceCommands.Parse("what is the weather like in Lisbon").Kind);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("Yeah, go ahead")]
    [InlineData("allow it")]
    [InlineData("do it")]
    [InlineData("sure")]
    [InlineData("okay")]
    [InlineData("sounds good")]
    public void Yes_is_recognised(string said) => Assert.Equal(VoiceCommandKind.Yes, VoiceCommands.Parse(said).Kind);

    [Theory]
    [InlineData("no")]
    [InlineData("nope")]
    [InlineData("don't")]
    [InlineData("deny")]
    [InlineData("do not allow it")]
    [InlineData("no don't do it")]
    public void No_is_recognised_and_beats_a_yes_word_it_contains(string said) => Assert.Equal(VoiceCommandKind.No, VoiceCommands.Parse(said).Kind);

    [Theory]
    [InlineData("the second one", 2)]
    [InlineData("second", 2)]
    [InlineData("option three", 3)]
    [InlineData("number 1", 1)]
    [InlineData("go with the first one", 1)]
    [InlineData("the last one", -1)]
    [InlineData("option one", 1)]
    public void A_place_is_recognised(string said, int place)
    {
        var command = VoiceCommands.Parse(said);
        Assert.Equal(VoiceCommandKind.Ordinal, command.Kind);
        Assert.Equal(place, command.Place);
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("be quiet")]
    [InlineData("that's enough")]
    [InlineData("never mind")]
    [InlineData("Enough.")]
    public void A_bare_stop_is_a_hush_that_asks_nothing_of_Dextra(string said) => Assert.Equal(VoiceCommandKind.Hush, VoiceCommands.Parse(said).Kind);

    [Fact]
    public void Thanks_alone_and_a_very_long_request_are_not_commands()
    {
        Assert.Equal(VoiceCommandKind.None, VoiceCommands.Parse("thank you").Kind);
        Assert.Equal(VoiceCommandKind.None, VoiceCommands.Parse(string.Join(' ', Enumerable.Repeat("stop everything", 40))).Kind);
    }
}
