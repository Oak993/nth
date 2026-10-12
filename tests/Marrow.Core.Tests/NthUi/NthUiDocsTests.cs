using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Marrow.Core.NthUi;

namespace Marrow.Core.Tests.NthUi;

// DESIGN.md 14 row W15, docs/nth-ui.md (web surfaces W15; ED-NEW-web-w15-1, traceability alias ED-NEW-webnthui-1): the
// reference's examples are parsed, so the page cannot drift from the language. A block fenced `nth-ui` parses clean and
// without warnings, `nth-ui lite` the same in the lite profile, and `nth-ui degrades` degrades at least one line while
// keeping its text. Every kind and function the language knows is named, and the lite list is exactly the lite subset.
[Trait("Decision", "ED-NEW-webnthui-1")]
public sealed class NthUiDocsTests
{
    [Fact]
    public void Every_example_in_the_reference_parses_as_it_says()
    {
        var blocks = Blocks(File.ReadAllText(DocPath()));
        Assert.True(blocks.Count >= 12, $"only {blocks.Count} examples");
        Assert.Contains(blocks, b => b.Words.Contains("lite") && !b.Words.Contains("degrades"));
        Assert.Contains(blocks, b => b.Words.Contains("lite") && b.Words.Contains("degrades"));
        Assert.Contains(blocks, b => !b.Words.Contains("lite") && b.Words.Contains("degrades"));
        foreach (var (words, line, text) in blocks)
        {
            var document = NthUiParser.Parse(text, words.Contains("lite") ? NthUiProfile.Lite : NthUiProfile.Full);
            Assert.Empty(NthUiValidator.Problems(document));
            Assert.NotEmpty(document.Roots);
            var degraded = document.Nodes.Where(n => n.Degraded).ToArray();
            if (words.Contains("degrades"))
            {
                Assert.True(degraded.Length > 0, $"the example at line {line} does not degrade");
                Assert.All(degraded, n => Assert.Equal(n.Source, n.Arg("text")!.Literal.AsText));
                Assert.Contains(document.Nodes, n => !n.Degraded);
            }
            else
            {
                var notes = string.Join("; ", document.Diagnostics.Select(d => $"{d.Line}: {d.Message}"));
                Assert.True(document.IsClean, $"the example at line {line}: {notes}");
                Assert.True(document.Diagnostics.Count == 0, $"the example at line {line} warns: {notes}");
            }
        }
    }

    [Fact]
    public void The_reference_names_every_kind_and_every_function()
    {
        var text = File.ReadAllText(DocPath());
        foreach (var spec in NthUiSchema.Kinds) Assert.Contains($"`{spec.Kind}`", text, StringComparison.Ordinal);
        foreach (var name in NthUiExpr.Functions.Keys) Assert.Contains($"`{name}(", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_lite_list_is_the_lite_subset()
    {
        var text = File.ReadAllText(DocPath());
        var start = text.IndexOf("## 8. Lite", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = text.IndexOf("```", start, StringComparison.Ordinal);
        var named = Regex.Matches(text[start..end], "`([a-z]+)`").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var lite = NthUiSchema.LiteKinds.Append(NthUiSchema.TableRow.Kind).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(lite.Order(StringComparer.Ordinal), named.Order(StringComparer.Ordinal));
    }

    private static List<(string[] Words, int Line, string Text)> Blocks(string markdown)
    {
        var lines = markdown.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        var blocks = new List<(string[], int, string)>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith("```", StringComparison.Ordinal)) continue;
            var words = lines[i][3..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var first = i + 1;
            var close = first;
            while (close < lines.Length && !lines[close].StartsWith("```", StringComparison.Ordinal)) close++;
            Assert.True(close < lines.Length, $"the fence at line {i + 1} is not closed");
            if (words.Length > 0 && words[0] == "nth-ui") blocks.Add((words[1..], first, string.Join("\n", lines[first..close]) + "\n"));
            i = close;
        }
        return blocks;
    }

    private static string DocPath([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", "..", "docs", "nth-ui.md"));
}
