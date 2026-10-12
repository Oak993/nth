using System.Text;
using Marrow.Testing;
using Marrow.UI.Markdown;

namespace Marrow.UI.Tests.Markdown;

// ED-NEW-lean-13 budget: bytes the block parser allocates for a 10,000-character reply, whole and as one
// streaming step. Headless and deterministic (AllocationProbe on its own thread, 20 warm-up parses). Numbers are
// for the Debug build the heavy slot runs.
public sealed class MarkdownParserAllocationBudgetTests
{
    // Before the lean pass (Debug): 1,332,720 B whole and 54,608 B per streaming step (limits then 2,000,000 and
    // 82,000). After it: 247,056 B and 8,672 B; limits about 1.3x those.
    private const long WholeParseLimit = 321_000;
    private const long StreamStepLimit = 11_300;

    [Fact]
    [Trait("Decision", "ED-NEW-lean-13")]
    public void Parsing_a_ten_thousand_character_document_stays_within_its_allocation_budget()
    {
        var text = Document(10_000);
        var bytes = AllocationProbe.Measure(
            () => { for (var i = 0; i < 20; i++) MarkdownParser.MarkdownParserParse(text); },
            () => MarkdownParser.MarkdownParserParse(text));
        Assert.True(bytes <= WholeParseLimit, $"Parsing 10,000 characters allocated {bytes:N0} B (limit {WholeParseLimit:N0} B).");
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-13")]
    public void Extending_a_streamed_ten_thousand_character_document_stays_within_its_allocation_budget()
    {
        var full = Document(10_040);
        var previous = full[..10_000];
        var previousParse = MarkdownParser.MarkdownParserParse(previous, null);
        var bytes = AllocationProbe.Measure(
            () => { for (var i = 0; i < 20; i++) MarkdownParser.MarkdownParserParse(full, (previous, previousParse)); },
            () => MarkdownParser.MarkdownParserParse(full, (previous, previousParse)));
        Assert.True(bytes <= StreamStepLimit, $"One 40-character streaming step on 10,000 characters allocated {bytes:N0} B (limit {StreamStepLimit:N0} B).");
    }

    // A reply with every block kind the parser knows, repeated and cut at `length` characters.
    private static string Document(int length)
    {
        const string section =
            "## Section {0}\n\n" +
            "Paragraph line with **bold** text and a [link](https://example.com) that runs on for a while, mentioning 42 things.\n" +
            "Second paragraph line continues the thought without a blank line between them.\n\n" +
            "- first bullet item with some text\n" +
            "- second bullet item\n" +
            "  - nested child bullet\n" +
            "- [ ] task not done\n" +
            "- [x] task done\n\n" +
            "1. ordered one\n" +
            "2. ordered two\n" +
            "10) ordered ten\n\n" +
            "> quoted line one\n" +
            "> quoted line two\n\n" +
            "```csharp\n" +
            "var x = 1;\n" +
            "Console.WriteLine(x);\n" +
            "```\n\n" +
            "| Name | Value |\n" +
            "| --- | :-: |\n" +
            "| a | 1 |\n" +
            "| b | 2 |\n\n" +
            "---\n\n";
        var builder = new StringBuilder();
        for (var i = 0; builder.Length < length; i++) builder.Append(string.Format(System.Globalization.CultureInfo.InvariantCulture, section, i));
        return builder.ToString(0, length);
    }
}
