using System.Text;
using Marrow.Testing;
using Marrow.UI.Markdown;

namespace Marrow.UI.Tests.Markdown;

// ED-NEW-lean-13 budget: bytes one mention scan allocates over a 2,000-character paragraph with five Hydra heads.
// Headless and deterministic (AllocationProbe on its own thread, 20 warm-up scans). Numbers are for the Debug
// build the heavy slot runs.
public sealed class MentionScannerAllocationBudgetTests
{
    // Before the lean pass (Debug): 18,698,264 B; limit about 1.5x.
    private const long ScanLimit = 28_000_000;

    private static readonly string[] Heads = ["Hank", "Hank 2", "Nova", "Orion", "Vega"];

    [Fact]
    [Trait("Decision", "ED-NEW-lean-13")]
    public void A_mention_scan_of_a_two_thousand_character_paragraph_with_five_heads_stays_within_its_allocation_budget()
    {
        var text = Paragraph(2_000);
        var bytes = AllocationProbe.Measure(
            () => { for (var i = 0; i < 20; i++) MentionScanner.Matches(text, Heads, name => name); },
            () => MentionScanner.Matches(text, Heads, name => name));
        Assert.True(bytes <= ScanLimit, $"Scanning 2,000 characters for five heads allocated {bytes:N0} B (limit {ScanLimit:N0} B).");
        Assert.NotEmpty(MentionScanner.Matches(text, Heads, name => name));
    }

    private static string Paragraph(int length)
    {
        string[] words =
        [
            "Hank", "looked", "at", "the", "build", "and", "asked", "Nova", "whether", "Hank 2", "had", "finished",
            "the", "review;", "Orion", "said", "Vega", "would", "check", "the", "Hankering", "tests", "after", "lunch,",
            "while", "Novak", "and", "Orion2", "waited", "for", "the", "next", "step", "of", "the", "plan.",
        ];
        var builder = new StringBuilder();
        for (var i = 0; builder.Length < length; i++)
        {
            if (builder.Length > 0) builder.Append(' ');
            builder.Append(words[i % words.Length]);
        }
        return builder.ToString(0, length);
    }
}
