using System.Runtime.CompilerServices;
using Marrow.Core.NthUi;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.NthUi;

// DESIGN.md 7.3 (web surfaces W17; ED-NEW-web-w17-1): the GBNF grammar of the lite subset that a llama.cpp server applies to a
// small local model. The grammar is read the way llama.cpp reads it (rule names, one rule per line, escapes); every lite
// example and every lite view in the reference fits it; a view with a full-only kind, a wrong indent or text around it does
// not; and a thousand random derivations of the grammar are each a clean lite view, so the grammar can never let a model write
// a line the language would degrade.
[Trait("Decision", "ED-NEW-web-w17-1")]
public sealed class NthUiGrammarTests
{
    private static readonly GbnfGrammar Grammar = GbnfGrammar.Parse(NthUiGrammar.Gbnf);

    private static string Fenced(string view) => "```nth-ui\n" + view + "\n```";

    [Fact]
    public void The_grammar_reads_as_llama_cpp_reads_it()
    {
        var text = NthUiGrammar.Gbnf;
        Assert.EndsWith("\n", text, StringComparison.Ordinal);
        Assert.Equal("root", Grammar.RuleNames[0]);
        Assert.Equal(NthUiGrammar.Root, Grammar.Root);
        Assert.DoesNotContain("\n\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", text, StringComparison.Ordinal); // no repetition counts: older readers refuse them
        Assert.All(text.TrimEnd('\n').Split('\n'), line => Assert.Contains(" ::= ", line, StringComparison.Ordinal));
        Assert.InRange(Grammar.RuleNames.Count, 60, 200);
        Assert.Equal("```nth-ui", NthUiGrammar.FenceOpen);
        Assert.Equal(["```nth-ui"], NthUiGrammar.Triggers);
        Assert.Same(NthUiGrammar.Gbnf, NthUiGrammar.Gbnf);
    }

    [Fact]
    public void Every_lite_kind_has_a_rule_and_no_other_kind_does()
    {
        var text = NthUiGrammar.Gbnf;
        var lite = NthUiSchema.LiteKinds.ToHashSet(StringComparer.Ordinal);
        foreach (var kind in lite.Append("row"))
        {
            Assert.Contains("\"" + kind + "\"", text, StringComparison.Ordinal);
            Assert.Contains(kind + "-line ::= ", text, StringComparison.Ordinal);
        }
        foreach (var name in Grammar.RuleNames.Where(n => n.EndsWith("-line", StringComparison.Ordinal)))
        {
            var kind = name[..^5];
            Assert.True(lite.Contains(kind) || kind == "row", kind);
        }
        Assert.Equal(lite.Count + 1, Grammar.RuleNames.Count(n => n.EndsWith("-line", StringComparison.Ordinal)));
        foreach (var function in NthUiExpr.Functions.Keys) Assert.Contains("\"" + function + "(\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_lite_example_fits()
    {
        foreach (var example in NthUiExamples.Lite)
            Assert.True(Grammar.Matches(Fenced(example.View)), $"{example.Id} stops fitting at character {Grammar.LongestPrefix(Fenced(example.View))}");
        Assert.True(Grammar.Matches(Fenced(EveryForm)), $"the every-form sample stops fitting at {Grammar.LongestPrefix(Fenced(EveryForm))}");
        foreach (var view in DocLiteViews())
            Assert.True(Grammar.Matches(Fenced(view)), $"a lite view of the reference stops fitting at {Grammar.LongestPrefix(Fenced(view))}: {view[..Math.Min(40, view.Length)]}");
    }

    private const string EveryForm = """
        card "Plan"
          state n = 3
          let doubled = n * 2
          stepper n label="N" min=1 max=9 step=1 unit="x"
          text muted = "Doubled is " + doubled
          table cols="A,B"
            row total "Sum" = fmt(n + doubled, "0.0")
            row "Pair" n doubled when=(n > 1)
          chart line x="a,b,c" y=[n, doubled, -1.5] unit="$" stacked
          chart pie series="One" y=(map(range(1, 4), k => k * n))
          chips "Show" bind=picked single
            chip "One" value=one selected
            chip "Two" value=two
          checklist "Do" bind=done
            item "First" value=a done
            item = "Second " + n
        followups
          chip "Next"
        """;

    [Fact]
    public void The_expression_forms_of_the_language_fit()
    {
        foreach (var expression in new[]
        {
            "n", "42", "-3.5", ".5", "\"text\"", "true", "(n + 1)", "n * 2 + 1", "a ? b : c", "a > 1 and b < 2 or not c", "not done",
            "len(items)", "sum(map(xs, x => x * 2))", "fmt(total, \"$#,##0\")", "xs[0]", "xs[n - 1]", "obj.key", "[1, 2, n]", "[]",
            "max(a, b) - min(a, b)", "round(n / 3)", "contains(picked, \"pg\")", "(a + b) * (c - d) / e % f",
        })
            Assert.True(Grammar.Matches("expr", expression), expression);
        foreach (var expression in new[] { "a+b", "a ? b", "map(xs, x =>)", "xs[", "\"open", "1 +", "f(1,2)" })
            Assert.False(Grammar.Matches("expr", expression), expression);
    }

    [Fact]
    public void A_view_outside_the_subset_or_the_canonical_form_does_not_fit()
    {
        string One(string line) => Fenced("card \"X\"\n  " + line);
        Assert.True(Grammar.Matches(One("text \"ok\"")));
        foreach (var line in new[]
        {
            "heading \"x\"", "stat \"A\" = 1", "callout \"x\"", "grid cols=2", "badge \"x\"", "section \"x\"", "toggle t label=\"T\"", "segmented s options=\"a:A\"",
            "form \"x\"", "tabs bind=t", "map \"m\"", "timeline \"t\"", "steps \"s\"", "code lang=bash", "diagram \"d\"", "image \"https://example.com/a.png\"", "actions",
            "text \"a\" \"b\"", "stepper n label=\"A\" label=\"B\"", "stepper label=\"A\"", "stepper n min=1 label=\"A\"", "slider n step=1 min=0", "text 'single quotes'",
            "chart donut x=\"a\" y=[1]", "table cols=\"A\" cols=\"B\"", "state n", "let x", "row \"x\"", "chip \"x\"", "item \"x\"", "series \"x\"",
        })
            Assert.False(Grammar.Matches(One(line)), line);
        // Layout and the fence.
        Assert.False(Grammar.Matches("card \"X\"\n"));
        Assert.False(Grammar.Matches("```nth-ui\ncard \"X\"\n"));
        Assert.False(Grammar.Matches("Here you go.\n" + Fenced("card \"X\"")));
        Assert.False(Grammar.Matches(Fenced("card \"X\"") + "\nAnd that is it."));
        Assert.False(Grammar.Matches(Fenced("card \"X\"\n   text \"three spaces\"")));
        Assert.False(Grammar.Matches(Fenced("card \"X\"\n\ttext \"tab\"")));
        Assert.False(Grammar.Matches(Fenced("card \"X\"\n  card \"nested\"")));
        Assert.False(Grammar.Matches(Fenced("row \"x\"")));
        Assert.False(Grammar.Matches(Fenced("card \"X\"\n  followups")));
        Assert.False(Grammar.Matches(Fenced("card \"X\"\n  table cols=\"A\"\n  row \"x\"")));
        Assert.False(Grammar.Matches(Fenced("card \"X\"\n  chips \"C\"\n    chip \"a\"\n      chip \"deeper\"")));
        Assert.False(Grammar.Matches(Fenced("")));
        Assert.True(Grammar.Matches(Fenced("text \"alone\"")));
        Assert.True(Grammar.Matches(Fenced("text \"alone\"") + "\n"));
    }

    [Fact]
    public void Random_derivations_are_clean_lite_views()
    {
        const int Total = 500;
        var random = new Random(20261011);
        var clean = 0;
        var kinds = new HashSet<string>(StringComparer.Ordinal);
        var unexpected = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < Total; i++)
        {
            var sample = Grammar.Sample(random, 44, 900);
            Assert.True(Grammar.Matches(sample), "a sample the grammar itself does not match: " + sample);
            Assert.StartsWith("```nth-ui\n", sample, StringComparison.Ordinal);
            var view = sample["```nth-ui\n".Length..sample.LastIndexOf("```", StringComparison.Ordinal)];
            var document = NthUiParser.Parse(view, NthUiProfile.Lite);
            foreach (var node in document.Nodes) kinds.Add(node.Kind);
            var degraded = document.Nodes.Where(n => n.Degraded).ToArray();
            foreach (var node in degraded)
            {
                // Two random names can clash (a name declared twice, a control on a let): the only reasons a derivation may degrade for.
                var problem = node.Problem!;
                if (problem.Contains("is already declared", StringComparison.Ordinal) || problem.Contains("so it cannot be a let", StringComparison.Ordinal)
                    || problem.Contains("nothing can set it", StringComparison.Ordinal)) continue;
                unexpected.TryAdd(System.Text.RegularExpressions.Regex.Replace(problem, "'[^']*'", "'..'"), node.Source[..Math.Min(160, node.Source.Length)]);
            }
            if (degraded.Length == 0) clean++;
        }
        Assert.True(unexpected.Count == 0, string.Join("\n", unexpected.Select(p => p.Key + "   <=   " + p.Value)));
        Assert.True(clean >= Total * 85 / 100, $"only {clean} of {Total} derivations were clean");
        foreach (var kind in NthUiSchema.LiteKinds.Append("row"))
            Assert.Contains(kind, kinds);
    }

    [Fact]
    public void Sampling_is_bounded_in_size_and_a_grammar_that_cannot_end_is_refused()
    {
        // The first version sampled to depth 44 with no size limit: one derivation of the expression rules ran to millions of
        // characters and the matcher's memo grew past 10 GB. A sample now stays inside its budget plus the shortest completion of
        // what was open, whatever the depth.
        var random = new Random(7);
        var longest = 0;
        long total = 0;
        for (var i = 0; i < 400; i++)
        {
            var sample = Grammar.Sample(random);
            longest = Math.Max(longest, sample.Length);
            total += sample.Length;
        }
        Assert.InRange(longest, 200, 1_500 + 1_000);
        Assert.InRange(total / 400, 100, 1_500);
        Assert.InRange(Grammar.Sample(new Random(1), depth: 200, budget: 60).Length, 10, 1_000); // a small budget gives a small view
        Assert.InRange(Grammar.Sample(new Random(1), depth: 0).Length, 10, 1_000); // and so does a depth of zero
        Assert.InRange(Grammar.Sample(new Random(1), depth: 500).Length, 10, 2_500); // and an enormous depth cannot grow it past its budget

        var endless = GbnfGrammar.Parse("root ::= root \"a\"\n");
        Assert.Throws<InvalidOperationException>(() => endless.Sample(new Random(1)));
        Assert.False(endless.Matches("aaa"));
    }

    [Fact]
    public void The_every_form_sample_is_a_clean_lite_view_with_the_tree_it_names()
    {
        var parsed = NthUiParser.Parse(EveryForm + "\n", NthUiProfile.Lite);
        Assert.True(parsed.IsClean, string.Join("; ", parsed.Diagnostics));
        Assert.DoesNotContain(parsed.Nodes, n => n.Degraded);
        Assert.Equal(["card", "followups"], parsed.Roots.Select(n => n.Kind));
        var runtime = new NthUiRuntime(parsed);
        runtime.Sync();
        foreach (var node in parsed.Nodes) foreach (var arg in node.Args.Values) Assert.False(runtime.Evaluate(arg).IsError, node.Kind + " " + arg.Name);
    }

    private static List<string> DocLiteViews()
    {
        var lines = File.ReadAllText(DocPath()).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var views = new List<string>();
        for (var i = 0; i < lines.Length; i++)
        {
            var words = lines[i].StartsWith("```", StringComparison.Ordinal) ? lines[i][3..].Split(' ', StringSplitOptions.RemoveEmptyEntries) : [];
            if (words is not ["nth-ui", "lite"]) continue;
            var close = i + 1;
            while (!lines[close].StartsWith("```", StringComparison.Ordinal)) close++;
            views.Add(string.Join("\n", lines[(i + 1)..close]));
            i = close;
        }
        Assert.NotEmpty(views);
        return views;
    }

    private static string DocPath([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", "..", "docs", "nth-ui.md"));
}
