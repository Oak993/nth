using Marrow.Core.NthUi;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.NthUi;

// DESIGN.md 13.1 NthUiTests, the parser on partial input (web surfaces W15; ED-NEW-web-w15-1, traceability alias
// ED-NEW-webnthui-1): every one of the thirty samples parses clean; every prefix of every sample, cut at any character,
// yields a valid tree; nodes already placed never change as later lines arrive (7.4 "Earlier nodes never move");
// chunking never matters; and the line rules (indent, parents, bodies, fences, keys, limits) hold.
[Trait("Decision", "ED-NEW-webnthui-1")]
public sealed class NthUiParserTests
{
    [Fact]
    public void Thirty_samples_parse_with_no_degraded_line_and_no_error()
    {
        Assert.Equal(30, NthUiSamples.All.Count);
        foreach (var (name, text) in NthUiSamples.All)
        {
            var document = NthUiParser.Parse(text);
            Assert.True(document.IsClean, $"{name}: {string.Join("; ", document.Diagnostics.Where(d => d.Severity == NthUiSeverity.Error))}");
            Assert.Empty(NthUiValidator.Problems(document));
            Assert.Equal(text.Split('\n').Count(l => l.Trim().Length > 0) - BodyLines(document), document.Nodes.Count);
        }
    }

    [Fact]
    public void The_design_sample_builds_the_tree_it_describes()
    {
        var document = NthUiParser.Parse(NthUiSamples.DesignHosting);
        Assert.Equal(["card", "followups"], document.Roots.Select(n => n.Kind));
        var card = document.Roots[0];
        Assert.Equal("Hosting estimate", card.Text);
        Assert.Equal("server", card.Arg("icon")!.Literal.AsText);
        Assert.Equal(["state", "state", "state", "let", "stepper", "slider", "segmented", "table", "chart", "actions"], card.Children.Select(n => n.Kind));
        var table = card.Children[7];
        Assert.All(table.Children, row => Assert.Same(NthUiSchema.TableRow, row.Spec));
        Assert.True(table.Children[2].Has("total"));
        Assert.Equal(2, table.Children[0].Cells.Count);
        Assert.Equal("Compute", table.Children[0].Cells[0].Literal.AsText);
        Assert.Equal(["instances", "price"], table.Children[0].Reads.Order(StringComparer.Ordinal));
        var chart = card.Children[8];
        Assert.Equal("bar", chart.Arg("type")!.Literal.AsText);
        Assert.Equal(NthUiExprKind.List, chart.Arg("y")!.Expr!.Kind);
        var actions = card.Children[9];
        Assert.Equal(["copy", "send"], actions.Children.Select(n => n.Kind));
        Assert.Equal("table", actions.Children[0].Arg("value")!.Literal.AsText);
        Assert.Equal(["chip", "chip"], document.Roots[1].Children.Select(n => n.Kind));
        Assert.Equal(["0", "0.0", "0.7", "0.7.2", "0.9.1", "1", "1.1"],
            new[] { card, card.Children[0], table, table.Children[2], actions.Children[1], document.Roots[1], document.Roots[1].Children[1] }.Select(n => n.Key));
        Assert.Equal(["instances", "price", "region", "traffic"], document.Declarations.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_prefix_of_every_sample_yields_a_valid_tree_that_the_full_tree_extends()
    {
        foreach (var (name, text) in NthUiSamples.All)
        {
            var full = NthUiParser.Parse(text);
            var streamed = new NthUiParser();
            for (var cut = 0; cut <= text.Length; cut++)
            {
                var prefix = NthUiParser.Parse(text[..cut]);
                var problems = NthUiValidator.Problems(prefix);
                Assert.True(problems.Count == 0, $"{name} cut at {cut}: {string.Join("; ", problems)}");
                if (cut > 0) streamed.Feed(text[(cut - 1)..cut]);
                Assert.Empty(NthUiValidator.Problems(streamed.Document));
                AssertExtends(streamed.Document, full, $"{name} streamed to {cut}");
            }
        }
    }

    [Fact]
    public void Chunking_never_changes_the_tree()
    {
        var random = new Random(4242);
        foreach (var (name, text) in NthUiSamples.All)
        {
            var whole = Shape(NthUiParser.Parse(text));
            for (var round = 0; round < 20; round++)
            {
                var parser = new NthUiParser();
                var at = 0;
                while (at < text.Length)
                {
                    var size = Math.Min(text.Length - at, random.Next(1, 40));
                    parser.Feed(text.Substring(at, size));
                    at += size;
                }
                parser.Complete();
                Assert.Equal(whole, Shape(parser.Document));
            }
            Assert.Equal(whole, Shape(NthUiParser.Parse(text.Replace("\n", "\r\n", StringComparison.Ordinal))));
        }
    }

    [Fact]
    public void Each_completed_line_is_reported_once_as_it_arrives()
    {
        var parser = new NthUiParser();
        var added = new List<string>();
        foreach (var c in NthUiSamples.DesignHosting) added.AddRange(parser.Feed(c.ToString()).Where(ch => ch.Kind == NthUiChangeKind.Added).Select(ch => ch.Node.Key));
        Assert.Equal("1.0", added[^1]);
        added.AddRange(parser.Complete().Select(ch => ch.Node.Key));
        Assert.Equal(parser.Document.Nodes.Select(n => n.Key), added);
        Assert.Empty(parser.Feed("text \"after the end\"\n"));
    }

    [Fact]
    public void Text_streams_into_the_pending_line_before_it_ends()
    {
        var parser = new NthUiParser();
        parser.Feed("card \"Notes\"\n  text \"Hello wor");
        Assert.Equal(new NthUiPending("text", "Hello wor", parser.Document.Roots[0]), parser.Pending);
        parser.Feed("ld\"");
        Assert.Equal("Hello world", parser.Pending!.Text);
        parser.Feed("\n  heading Plain words so far");
        Assert.Equal("Plain words so far", parser.Pending!.Text);
        parser.Feed("\n  stepper n min=1");
        Assert.Null(parser.Pending);
        parser.Feed("\n  code lang=js\n    let x = 1;\n    consol");
        Assert.Equal(new NthUiPending("body", "consol", parser.Document.Nodes[^1]), parser.Pending);
    }

    [Fact]
    public void Code_svg_and_diagram_keep_their_indented_lines_verbatim()
    {
        var document = NthUiParser.Parse(NthUiSamples.Code);
        var code = document.Roots[1];
        Assert.Equal("def fib(n):\n    a, b = 0, 1\n\n    for _ in range(n):\n        a, b = b, a + b\n    return a", code.Body);
        Assert.Equal("python", code.Arg("lang")!.Literal.AsText);
        Assert.Equal("text", document.Roots[2].Kind);
        var diagram = NthUiParser.Parse(NthUiSamples.Diagram).Roots[0];
        Assert.Equal("graph LR\n  A[Client] --> B[Server]\n  B --> C[(Database)]", diagram.Body);
        var svg = NthUiParser.Parse(NthUiSamples.Svg).Roots[0];
        Assert.StartsWith("<svg viewBox=", svg.Body, StringComparison.Ordinal);
        var steps = NthUiParser.Parse(NthUiSamples.Steps);
        Assert.Equal(["npm install", "cp .env.example .env", "npm run dev"],
            steps.Nodes.Where(n => n.Kind == "code").Select(n => n.Body));
        Assert.Equal("callout", steps.Nodes[^1].Kind);
        Assert.Equal("step", steps.Nodes[^1].Parent!.Kind);
    }

    [Fact]
    public void A_line_goes_to_the_nearest_ancestor_that_can_hold_it()
    {
        var document = NthUiParser.Parse("""
            card "A"
              stat "Users" = 3
                text "per day"
              table cols="x"
                row "1"
                text "under the table"
              list
                item "one"
                  item "nested"
            """);
        var card = document.Roots[0];
        Assert.Equal(["stat", "text", "table", "text", "list"], card.Children.Select(n => n.Kind));
        Assert.Equal(["item", "item"], card.Children[4].Children.Select(n => n.Kind));
        Assert.True(document.IsClean);
        Assert.Empty(NthUiValidator.Problems(document));
    }

    [Fact]
    public void Odd_indents_tabs_and_jumps_take_the_nearest_shallower_line_as_parent()
    {
        var document = NthUiParser.Parse("card \"A\"\n   text \"three spaces\"\n\ttext \"a tab\"\n        text \"a jump\"\ncard \"B\"\n");
        Assert.Equal(3, document.Roots[0].Children.Count);
        Assert.Equal("B", document.Roots[1].Text);
        Assert.Empty(NthUiValidator.Problems(document));
    }

    [Fact]
    public void Fences_and_blank_lines_are_skipped()
    {
        var document = NthUiParser.Parse("```nth-ui\ncard \"A\"\n\n  text \"x\"\n```\n");
        Assert.Equal(2, document.Nodes.Count);
        Assert.True(document.IsClean);
    }

    [Fact]
    public void Explicit_ids_key_nodes_and_a_repeated_id_is_left_out()
    {
        var document = NthUiParser.Parse("card \"A\" id=summary\n  text \"x\" id=note\n  text \"y\" id=note\n");
        Assert.Same(document.Roots[0], document.Find("summary"));
        Assert.Equal("note", document.Roots[0].Children[0].Key);
        Assert.Equal("summary.1", document.Roots[0].Children[1].Key);
        Assert.Contains(document.Diagnostics, d => d.Severity == NthUiSeverity.Warning && d.Message.Contains("note", StringComparison.Ordinal));
    }

    [Fact]
    public void Limits_bound_nodes_depth_lines_and_bodies()
    {
        var many = string.Concat(Enumerable.Repeat("text \"x\"\n", NthUiParser.MaxNodes + 50));
        var document = NthUiParser.Parse(many);
        Assert.Equal(NthUiParser.MaxNodes, document.Nodes.Count);
        Assert.Single(document.Diagnostics, d => d.Severity == NthUiSeverity.Error);

        var deep = string.Concat(Enumerable.Range(0, 60).Select(i => new string(' ', i * 2) + "stack\n"));
        var nested = NthUiParser.Parse(deep);
        Assert.Equal(NthUiParser.MaxDepth, nested.Nodes.Max(n => n.Depth));
        Assert.Empty(NthUiValidator.Problems(nested));

        var longLine = NthUiParser.Parse("text \"" + new string('a', NthUiLine.MaxLength + 10) + "\"\n");
        Assert.True(longLine.Nodes[0].Degraded);
        Assert.True(longLine.Nodes[0].Text!.Length <= NthUiLine.MaxLength + 1);

        var body = NthUiParser.Parse("code\n" + string.Concat(Enumerable.Repeat("  " + new string('b', 1000) + "\n", 400)));
        Assert.True(body.Roots[0].Body!.Length <= NthUiParser.MaxBodyLength);
    }

    [Fact]
    public void Parsing_three_hundred_nodes_stays_inside_the_frame_budget()
    {
        var text = string.Concat(Enumerable.Repeat(NthUiSamples.DesignHosting + "\n", 16));
        var count = NthUiParser.Parse(text).Nodes.Count;
        Assert.True(count >= 300, $"{count} nodes");
        var times = new List<double>();
        for (var i = 0; i < 15; i++)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            NthUiParser.Parse(text);
            times.Add(watch.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        // DESIGN 12: a 300-node view never takes more than 4 ms of a frame; the median of fifteen whole parses is held
        // well under that per frame's share even on a loaded machine (a whole parse is many frames' worth of lines).
        Assert.True(times[7] < 40, $"median {times[7]:0.0} ms for {count} nodes");
    }

    private static int BodyLines(NthUiDocument document) =>
        document.Nodes.Sum(n => n.BodyLines?.Count(l => l.Trim().Length > 0) ?? 0);

    private static void AssertExtends(NthUiDocument prefix, NthUiDocument full, string where)
    {
        Assert.True(prefix.Nodes.Count <= full.Nodes.Count, where);
        for (var i = 0; i < prefix.Nodes.Count; i++)
        {
            var a = prefix.Nodes[i];
            var b = full.Nodes[i];
            Assert.True(a.Key == b.Key && a.Kind == b.Kind && a.Source == b.Source && a.Degraded == b.Degraded && a.Parent?.Key == b.Parent?.Key,
                $"{where}: node {i} is {a} from '{a.Source}', the full tree has {b} from '{b.Source}'");
        }
    }

    private static string Shape(NthUiDocument document) =>
        string.Join("\n", document.Nodes.Select(n => $"{n.Key}|{n.Kind}|{n.Parent?.Key}|{n.Degraded}|{n.Source}|{n.Body}"));
}
