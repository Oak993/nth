using System.Text;
using Marrow.Core.Port;

namespace Marrow.Core.Tests.Port;

// ED-NEW-stutterfix-7: SwiftString.Split made a string of every Character of the text before splitting it (TokenHistoryScanner splits each
// session file's head and tail by "\n": about 2.5 GB of one-character strings in the first minutes after launch, with gen2 collections
// under the first Settings and Hydra opens). It now walks the Characters in place; the pieces are the same, Character equality included.
// Every non-ASCII character is written as a \U escape (R-STR-9).
[Trait("Decision", "ED-NEW-stutterfix-7")]
public sealed class SwiftStringSplitCostTests
{
    [Fact]
    public void Pieces_are_the_stdlibs_for_canonically_equal_separators_and_clusters()
    {
        Assert.Equal(["a", "b"], SwiftString.Split("a\U0000037Eb", ";"));                  // GREEK QUESTION MARK is ";" (NFC)
        Assert.Equal(["x", "y"], SwiftString.Split("x\U0000212Ay", "K"));                  // KELVIN SIGN is "K" (NFC)
        Assert.Equal(["a", "b"], SwiftString.Split("a\U000000E9b", "e\U00000301"));        // composed text, decomposed separator
        Assert.Equal(["a", "b"], SwiftString.Split("ae\U00000301b", "\U000000E9"));        // decomposed text, composed separator
        Assert.Equal(["ae\U00000301b"], SwiftString.Split("ae\U00000301b", "e"));          // "e" inside a Character is not a separator
        Assert.Equal(["a\r\nb", "c"], SwiftString.Split("a\r\nb\nc", "\n"));
        Assert.Equal(["", "a", "", "b\r\n"], SwiftString.Split("\na\n\nb\r\n", "\n", omittingEmptySubsequences: false));
        Assert.Equal(["a", "b\nc\n"], SwiftString.Split("\na\nb\nc\n", "\n", maxSplits: 1));
        Assert.Equal(["\U0001F468\U0000200D\U0001F469", "\U0001F1FA\U0001F1F8"], SwiftString.Split("\U0001F468\U0000200D\U0001F469,\U0001F1FA\U0001F1F8", ","));
    }

    [Fact]
    public void Splitting_an_ascii_text_allocates_its_pieces_not_its_characters()
    {
        var text = new StringBuilder();
        for (var i = 0; i < 20000; i++) text.Append("{\"type\":\"event_msg\",\"payload\":{\"token_count\":").Append(i).Append("}}\n");
        var whole = text.ToString();
        SwiftString.Split(whole, "\n"); // warm
        var before = GC.GetAllocatedBytesForCurrentThread();
        var pieces = SwiftString.Split(whole, "\n");
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(20000, pieces.Count);
        Assert.True(allocated < 4L * whole.Length, $"{allocated} bytes allocated splitting {whole.Length} characters");
    }
}
