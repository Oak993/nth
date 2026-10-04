using Marrow.Services.Port.Voice.Engine;
using Whisper.net;

namespace Marrow.Services.Tests.Port.Voice.Engine;

// VT-6, V-7.2 on Windows review: Whisper.net's WhisperToken.Start and End are whisper.cpp's t0 and t1, 10 ms units. The voice-evidence run on the real model gave 22 words
// between 0.23 s and 10.01 s for the 10.02 s clip, which holds only with 0.01; this pins the conversion without a model.
public sealed class WhisperNetModelTokenTests
{
    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T2")]
    public void A_token_time_is_in_ten_millisecond_units()
    {
        var token = WhisperNetModel.ToToken(new WhisperToken { Text = " hello", Start = 150, End = 210 });
        Assert.Equal(" hello", token.Text);
        Assert.Equal(1.5, token.Start, 6);
        Assert.Equal(2.1, token.End, 6);
        Assert.False(token.IsSpecial);
    }

    [Theory]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [InlineData("[_BEG_]")]
    [InlineData("[_TT_42]")]
    [InlineData("<|en|>")]
    [InlineData("<|endoftext|>")]
    public void The_special_tokens_are_marked_and_the_text_ones_are_not(string text)
    {
        Assert.True(WhisperNetModel.ToToken(new WhisperToken { Text = text }).IsSpecial);
        Assert.False(WhisperNetModel.ToToken(new WhisperToken { Text = " [_not_special" }).IsSpecial);
        Assert.False(WhisperNetModel.ToToken(new WhisperToken { Text = " <|not" }).IsSpecial);
    }
}
