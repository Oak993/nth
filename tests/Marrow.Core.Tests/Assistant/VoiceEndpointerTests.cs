using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-18 (DESIGN.md rev 5, 2.4 "listening ends when the owner stops speaking (1.2 s of silence)"): where one request starts and ends,
// counted in the voice detector's 32 ms frames.
[Trait("Feature", "AS-18")]
[Trait("Decision", "ED-NEW-dextra-as18-3")]
public sealed class VoiceEndpointerTests
{
    private static EndpointKind FeedAll(VoiceEndpointer endpointer, float probability, int frames)
    {
        var last = EndpointKind.None;
        for (var i = 0; i < frames; i++)
        {
            var kind = endpointer.Feed(probability);
            if (kind != EndpointKind.None) last = kind;
        }
        return last;
    }

    [Fact]
    public void Speech_starts_after_two_speech_frames_in_a_row_and_not_after_one()
    {
        var endpointer = new VoiceEndpointer();
        Assert.Equal(EndpointKind.None, endpointer.Feed(0.9f));
        Assert.Equal(EndpointKind.None, endpointer.Feed(0.1f));
        Assert.Equal(EndpointKind.None, endpointer.Feed(0.9f));
        Assert.Equal(EndpointKind.SpeechStarted, endpointer.Feed(0.9f));
        Assert.True(endpointer.InSpeech);
    }

    [Fact]
    public void Speech_ends_after_exactly_1_2_seconds_of_silence_which_is_38_frames()
    {
        var endpointer = new VoiceEndpointer();
        FeedAll(endpointer, 0.9f, 10);
        Assert.Equal(EndpointKind.None, FeedAll(endpointer, 0.05f, 37));
        Assert.Equal(EndpointKind.SpeechEnded, endpointer.Feed(0.05f));
    }

    [Fact]
    public void A_word_in_the_gap_starts_the_silence_again()
    {
        var endpointer = new VoiceEndpointer();
        FeedAll(endpointer, 0.9f, 10);
        FeedAll(endpointer, 0.05f, 30);
        endpointer.Feed(0.8f);
        Assert.Equal(EndpointKind.None, FeedAll(endpointer, 0.05f, 37));
        Assert.Equal(EndpointKind.SpeechEnded, endpointer.Feed(0.05f));
    }

    [Fact]
    public void A_frame_between_the_two_bars_is_still_speech_inside_a_request()
    {
        var endpointer = new VoiceEndpointer();
        FeedAll(endpointer, 0.9f, 4);
        Assert.Equal(EndpointKind.None, FeedAll(endpointer, 0.4f, 200));
    }

    [Fact]
    public void A_toggled_listen_gives_up_after_8_seconds_of_nothing_and_a_held_one_never_does()
    {
        var toggled = new VoiceEndpointer();
        Assert.Equal(EndpointKind.None, FeedAll(toggled, 0.02f, 249));
        Assert.Equal(EndpointKind.NoSpeech, toggled.Feed(0.02f));
        var held = new VoiceEndpointer(new VoiceEndpointerOptions { NoSpeechAfter = null });
        Assert.Equal(EndpointKind.None, FeedAll(held, 0.02f, 5000));
    }

    [Fact]
    public void One_request_ends_at_28_seconds_whatever_is_said()
    {
        var endpointer = new VoiceEndpointer();
        EndpointKind seen = EndpointKind.None;
        var frames = 0;
        while (seen != EndpointKind.TooLong && frames < 2000)
        {
            frames++;
            var kind = endpointer.Feed(0.9f);
            if (kind != EndpointKind.None) seen = kind;
        }
        Assert.Equal(EndpointKind.TooLong, seen);
        Assert.InRange(frames, 870, 880);
    }

    [Fact]
    public void A_barge_in_begins_already_in_speech_and_the_silence_counts_from_there()
    {
        var endpointer = new VoiceEndpointer();
        endpointer.BeginInSpeech();
        Assert.True(endpointer.InSpeech);
        Assert.Equal(EndpointKind.None, FeedAll(endpointer, 0.05f, 37));
        Assert.Equal(EndpointKind.SpeechEnded, endpointer.Feed(0.05f));
    }

    [Fact]
    public void Reset_forgets_the_request()
    {
        var endpointer = new VoiceEndpointer();
        FeedAll(endpointer, 0.9f, 5);
        endpointer.Reset();
        Assert.False(endpointer.InSpeech);
        Assert.Equal(0, endpointer.Frames);
    }
}
