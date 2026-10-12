using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Marrow.App.Views.Composer;

namespace Marrow.App.Tests.Views.Composer;

// ED-NEW-lean-20 guard: the paragraph walk (each paragraph's start and natural base) is computed once per text and reused. A reused
// walk must never answer for another text or another fallback base: sources built in turn for different texts, the same text again,
// an equal text in a new string, and the same text under the other fallback each give their own paragraphs' bases.
[Collection("Headless")]
public sealed class ComposerParagraphWalkTests
{
    private static ComposerParagraphSource Source(string text, FlowDirection fallback) =>
        new(text, new GenericTextRunProperties(Typeface.Default, 14), fallback, null);

    [Fact]
    [Trait("Decision", "ED-NEW-lean-20")]
    public void A_reused_walk_answers_only_for_its_own_text_and_fallback()
    {
        const string rtlFirst = "123 اردو\n123 English";
        const string ltrFirst = "123 English\n123 اردو";
        const string neutral = "(123)!\n(456)";
        for (var round = 0; round < 3; round++)
        {
            var a = Source(rtlFirst, FlowDirection.LeftToRight);
            Assert.Equal(FlowDirection.RightToLeft, a.DirectionAt(0));
            Assert.Equal(FlowDirection.LeftToRight, a.DirectionAt(9));
            var b = Source(ltrFirst, FlowDirection.LeftToRight);
            Assert.Equal(FlowDirection.LeftToRight, b.DirectionAt(0));
            Assert.Equal(FlowDirection.RightToLeft, b.DirectionAt(12));
            var again = Source(new string(rtlFirst.AsSpan()), FlowDirection.LeftToRight); // equal text, another string
            Assert.Equal(FlowDirection.RightToLeft, again.DirectionAt(0));
            Assert.Equal(FlowDirection.LeftToRight, again.DirectionAt(int.MaxValue));
            Assert.Equal(FlowDirection.LeftToRight, Source(neutral, FlowDirection.LeftToRight).DirectionAt(7));
            Assert.Equal(FlowDirection.RightToLeft, Source(neutral, FlowDirection.RightToLeft).DirectionAt(7));
            Assert.Equal(FlowDirection.RightToLeft, Source(neutral, FlowDirection.RightToLeft).DirectionAt(0));
            Assert.Equal(FlowDirection.LeftToRight, Source(neutral, FlowDirection.LeftToRight).DirectionAt(0));
            // Still right after a longer and a shorter text with the same prefix.
            Assert.Equal(FlowDirection.LeftToRight, Source(rtlFirst + "\nاردو", FlowDirection.LeftToRight).DirectionAt(9));
            Assert.Equal(FlowDirection.RightToLeft, Source(rtlFirst + "\nاردو", FlowDirection.LeftToRight).DirectionAt(rtlFirst.Length + 1));
            Assert.Equal(FlowDirection.RightToLeft, Source("123 اردو", FlowDirection.LeftToRight).DirectionAt(int.MaxValue));
            Assert.Equal(FlowDirection.LeftToRight, Source("", FlowDirection.LeftToRight).DirectionAt(0));
            Assert.Equal(FlowDirection.RightToLeft, Source("", FlowDirection.RightToLeft).DirectionAt(0));
        }
    }
}
