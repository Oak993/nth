using System.Text;
using Marrow.Core.Port;
using Marrow.Testing;

namespace Marrow.Core.Tests.Port;

// ED-NEW-lean-1: allocation budgets for the two hot SwiftString shapes. Contains: the sidebar and command-palette search, one typed needle
// against 500 lowercased thread titles (one in ten carries an em dash, so the non-ASCII path stays in the mix). Split: a 10,000-character
// ASCII text split by "\n" (the TokenHistoryScanner shape). Measured on a dedicated thread after warm-up (AllocationProbe), no clock.
// Numbers are for the Debug build the test runs use; the ledger entry records the before and after values the limits come from.
[Trait("Decision", "ED-NEW-lean-1")]
public sealed class SwiftStringLeanBudgetTests
{
    private const long ContainsBytesPerCallLimit = 2_150;  // measured 1,434 before the fast paths (Debug)
    private const long SplitBytesLimit = 45_000;          // measured 29,984 before the fast paths (Debug)

    [Fact]
    public void Searching_500_thread_titles_stays_within_its_allocation_budget()
    {
        var titles = Titles();
        string[] needles = ["sync", "the side", "zz"];
        var hits = 0;
        void Pass()
        {
            foreach (var needle in needles)
                foreach (var title in titles)
                    if (SwiftString.Contains(title, needle)) hits++;
        }
        var bytes = AllocationProbe.Measure(() => { for (var i = 0; i < 20; i++) Pass(); }, Pass);
        Assert.True(hits > 0);
        var perCall = bytes / (needles.Length * titles.Length);
        Assert.True(perCall <= ContainsBytesPerCallLimit, $"{perCall} bytes per Contains ({bytes} for {needles.Length * titles.Length} calls)");
    }

    [Fact]
    public void Splitting_a_10k_ascii_text_stays_within_its_allocation_budget()
    {
        var text = new StringBuilder();
        for (var i = 0; text.Length < 10_000; i++) text.Append("{\"type\":\"event_msg\",\"payload\":{\"n\":").Append(i).Append("}}\n");
        var whole = text.ToString()[..10_000];
        var pieces = 0;
        var bytes = AllocationProbe.Measure(() => { for (var i = 0; i < 20; i++) SwiftString.Split(whole, "\n"); },
            () => pieces = SwiftString.Split(whole, "\n").Count);
        Assert.True(pieces > 200);
        Assert.True(bytes <= SplitBytesLimit, $"{bytes} bytes for one Split of {whole.Length} characters into {pieces} pieces");
    }

    private static string[] Titles()
    {
        string[] words = ["fix", "the", "sidebar", "sync", "lag", "when", "scrolling", "composer", "glass", "model", "picker", "terminal",
            "build", "release", "notes", "draft", "hydra", "settings", "voice", "update"];
        var random = new Random(5001);
        var titles = new string[500];
        for (var i = 0; i < titles.Length; i++)
        {
            var title = new StringBuilder();
            var count = random.Next(3, 9);
            for (var w = 0; w < count; w++)
            {
                if (w > 0) title.Append(i % 10 == 3 && w == 2 ? " \U00002014 " : " ");
                title.Append(words[random.Next(words.Length)]);
            }
            titles[i] = title.ToString();
        }
        return titles;
    }
}
