using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Marrow.Core.NthUi;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.NthUi;

// DESIGN.md 7.3 and 14 row W17 (web surfaces W17; ED-NEW-web-w17-1): the lite views a Local model is recorded writing
// (tests/fixtures/web-surfaces/teaching/local.jsonl) are the views a llama.cpp server would let it write: each one fits the lite
// grammar, and each is a clean lite view. So the recordings and the grammar cannot drift apart.
[Trait("Decision", "ED-NEW-web-w17-1")]
public sealed class NthUiRecordedLocalViewTests
{
    private static readonly GbnfGrammar Grammar = GbnfGrammar.Parse(NthUiGrammar.Gbnf);

    private static IEnumerable<(string Id, string Text)> Answers()
    {
        foreach (var line in File.ReadLines(Path.Combine(FixtureFolder(), "local.jsonl")).Where(l => l.Length > 0))
        {
            using var row = JsonDocument.Parse(line);
            var text = new StringBuilder();
            foreach (var chunk in row.RootElement.GetProperty("events").EnumerateArray())
                text.Append(chunk.GetProperty("choices")[0].GetProperty("delta").GetProperty("content").GetString());
            yield return (row.RootElement.GetProperty("id").GetString()!, text.ToString());
        }
    }

    [Fact]
    public void Every_lite_view_a_local_model_is_recorded_writing_fits_the_grammar_and_is_a_clean_lite_view()
    {
        var views = 0;
        foreach (var (id, text) in Answers())
        {
            var open = text.IndexOf("```nth-ui\n", StringComparison.Ordinal);
            if (open < 0) continue;
            var close = text.IndexOf("\n```", open, StringComparison.Ordinal);
            Assert.True(close > open, id);
            var fenced = text[open..(close + 4)];
            Assert.True(Grammar.Matches(fenced), $"{id} stops fitting at character {Grammar.LongestPrefix(fenced)}");
            var document = NthUiParser.Parse(fenced["```nth-ui\n".Length..^3], NthUiProfile.Lite);
            Assert.True(document.IsClean && document.Diagnostics.Count == 0, id + ": " + string.Join("; ", document.Diagnostics));
            views++;
        }
        Assert.Equal(6, views);
    }

    private static string FixtureFolder([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", "..", "tests", "fixtures", "web-surfaces", "teaching"));
}
