using System.Linq;
using System.Reflection;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Markdown;
using Marrow.UI.Port;

namespace Marrow.UI.Tests.Markdown;

// Source under test: DroppyCode/UI/Markdown/MarkdownView.swift (B2 1fbf81e). This part (part 1 of the unit)
// covers PR-THEME-105 to 129 except 122 (not in this part's spec). Helpers reflect into private members the
// same way tests/Marrow.UI.Tests/Chrome/ProjectIconPickerTests.cs does.
public sealed class ProseReflowTests
{
    [Fact]
    [Trait("Req", "PR-THEME-121")] // DroppyCode/UI/Markdown/MarkdownView.swift:1402-1468
    public void A_hard_wrapped_paragraph_that_meets_every_condition_joins_into_one_line()
    {
        var text = "This is a long enough line that definitely passes forty five characters.\n"
                  + "And a second line that also easily clears the forty five character floor.";

        var result = ProseReflow.Reflowed(text);

        Assert.Equal(
            "This is a long enough line that definitely passes forty five characters. "
            + "And a second line that also easily clears the forty five character floor.",
            result);
    }

    [Fact]
    [Trait("Req", "PR-THEME-121")] // DroppyCode/UI/Markdown/MarkdownView.swift:1417-1425: list/heading/quote markers block reflow
    public void A_paragraph_whose_first_line_starts_with_a_list_marker_is_never_reflowed()
    {
        var text = "- This is a long enough line that definitely passes forty five characters.\n"
                  + "And a second line that also easily clears the forty five character floor.";

        Assert.Equal(text, ProseReflow.Reflowed(text));
    }

    [Fact]
    [Trait("Req", "PR-THEME-121")] // DroppyCode/UI/Markdown/MarkdownView.swift:1426: every line but the last must be at least 45 characters
    public void A_paragraph_with_a_short_leading_line_is_never_reflowed()
    {
        var text = "short line\nAnd a second line that also easily clears the forty five character floor.";

        Assert.Equal(text, ProseReflow.Reflowed(text));
    }

    [Fact]
    [Trait("Req", "PR-THEME-121")] // DroppyCode/UI/Markdown/MarkdownView.swift:1427-1432: a line ending in a code-like character blocks reflow
    public void A_paragraph_whose_leading_line_ends_in_a_brace_is_never_reflowed()
    {
        var text = "This is a long enough opening line ending in a brace like code does {\n"
                  + "And a second line that also easily clears the forty five character floor.";

        Assert.Equal(text, ProseReflow.Reflowed(text));
    }

    [Fact]
    [Trait("Req", "PR-THEME-121")] // DroppyCode/UI/Markdown/MarkdownView.swift:1433: a double space or tab blocks reflow
    public void A_paragraph_containing_a_double_space_is_never_reflowed()
    {
        var text = "This is a long enough  line with a double space in the middle of it here.\n"
                  + "And a second line that also easily clears the forty five character floor.";

        Assert.Equal(text, ProseReflow.Reflowed(text));
    }

    [Fact]
    [Trait("Req", "PR-THEME-121")] // DroppyCode/UI/Markdown/MarkdownView.swift:1436: no sentence punctuation, no reflow
    public void A_paragraph_with_no_sentence_ending_punctuation_is_never_reflowed()
    {
        var text = "This is a long enough opening line without any sentence ending punctuation\n"
                  + "and a second line that also easily clears the forty five character floor";

        Assert.Equal(text, ProseReflow.Reflowed(text));
    }

    [Fact]
    [Trait("Req", "PR-THEME-121")] // DroppyCode/UI/Markdown/MarkdownView.swift:1446-1453: fenced regions are never reflowed
    public void A_fenced_code_block_is_left_untouched_even_when_it_looks_hard_wrapped()
    {
        var text = "```\n"
                  + "This is a long enough line that definitely passes forty five characters.\n"
                  + "And a second line that also easily clears the forty five character floor.\n"
                  + "```";

        Assert.Equal(text, ProseReflow.Reflowed(text));
    }

    [Fact]
    [Trait("Req", "PR-THEME-121")] // DroppyCode/UI/Markdown/MarkdownView.swift:1412-1413: a single line, or a tilde fence's own language, is not this file's fence test
    public void A_single_line_paragraph_is_never_reflowed()
    {
        var text = "Just one long enough single line right here that passes forty five chars.";

        Assert.Equal(text, ProseReflow.Reflowed(text));
    }
}
