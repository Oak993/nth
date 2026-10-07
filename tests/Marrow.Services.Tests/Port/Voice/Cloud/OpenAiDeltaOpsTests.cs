using Marrow.Core.Port.Voice.Transcript;
using Marrow.Services.Port.Voice.Cloud;

namespace Marrow.Services.Tests.Port.Voice.Cloud;

// VT-16, voice-SPEC V-18.1 and V-7.5: a growing transcript becomes the same word operations the local engine's passes make.
[Trait("Spec", "V-T111")]
public sealed class OpenAiDeltaOpsTests
{
    [Fact]
    [Trait("Feature", "VT-16")]
    [Trait("Rule", "R-TEST-4")]
    public void V_T111_a_word_is_tentative_until_whitespace_or_the_end_follows_it()
    {
        var ops = new OpenAiDeltaOps();
        Assert.Equal([WordOp.Add(0, "Hello", true)], ops.Delta("Hello"));
        Assert.Equal([WordOp.Confirm(0)], ops.Delta(" "));
        Assert.Equal([WordOp.Add(1, "wor", true)], ops.Delta("wor"));
        Assert.Equal([WordOp.Revise(1, "world")], ops.Delta("ld"));
        Assert.Equal([WordOp.Confirm(1)], ops.Done("Hello world"));
        Assert.Equal("Hello world", ops.Text);
        Assert.Equal([new TranscriptWord("Hello", false), new TranscriptWord("world", false)], ops.Words);
    }

    [Fact]
    [Trait("Feature", "VT-16")]
    [Trait("Rule", "R-TEST-4")]
    public void V_T111_a_delta_with_only_whitespace_changes_nothing_and_line_breaks_split_words()
    {
        var ops = new OpenAiDeltaOps();
        Assert.Empty(ops.Delta("  "));
        Assert.Equal([WordOp.Add(0, "a", false), WordOp.Add(1, "b", true)], ops.Delta("a\nb"));
        Assert.Empty(ops.Delta(""));
    }

    [Fact]
    [Trait("Feature", "VT-16")]
    [Trait("Rule", "R-TEST-4")]
    public void V_T111_the_final_text_may_drop_or_change_words_the_deltas_showed()
    {
        var ops = new OpenAiDeltaOps();
        ops.Delta("one two three ");
        Assert.Equal([WordOp.Revise(1, "2"), WordOp.Remove(2)], ops.Done("one 2"));
    }
}
