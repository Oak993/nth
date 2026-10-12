using System.Diagnostics;
using System.Text;
using Marrow.Core.NthUi;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.NthUi;

// DESIGN.md 13.1 NthUiTests, fuzzing: no exceptions, no hangs, bounded evaluation (web surfaces W15; ED-NEW-web-w15-1,
// traceability alias ED-NEW-webnthui-1). Fixed seeds, so a failure reproduces. Mutated samples, random text, random
// token soup and generated expression trees go through the streaming parser in random chunks, the structural checks,
// and a runtime that reads, presses, drags, saves and restores everything; each run sits inside a hard time limit.
[Trait("Decision", "ED-NEW-webnthui-1")]
public sealed class NthUiFuzzTests
{
    private static readonly string[] Snippets =
    [
        "card \"x\"", "  ", "    ", "\n", "\n  ", "row", "row total", "table cols=\"a,b\"", "state x = 1", "state s type=selection = []",
        "let y = x * 2", "let x = y", "= ", " = ", "fmt(", ", \"$0\")", "map(range(10000), n => n)", "range(", "[", "]", "(", ")", "{", "}",
        "\"", "'", "\\", "#", "=", "==", "chart line editable=points bind=x", "stepper x min=1 max=3", "slider q", "chips bind=c", "chip \"a\" selected",
        "checklist", "item \"i\" done", "tabs", "tab \"t\"", "steps", "step \"s\"", "button \"b\"", "set x = x + 1", "toggle t", "copy value=table",
        "export csv", "open \"https://example.com\"", "call a.b {\"k\": x}", "send = \"n\" + x", "context = {a: x}", "followups", "code", "svg",
        "diagram", "when=(x > 1)", "id=dup", "```", "\t", "\r", "text = sum(filter([1, 2, 3], it > 1))", "segmented x options=\"a:A,b:B\"",
        "form submit=\"go\"", "date d", "map", "stop \"p\" lat=1 lon=2", "route via=\"p\"", "image \"https://e.com/i.png\"", "é", "😀",
    ];

    private static readonly string Alphabet = " \t\n\"'()[]{}=,.:-+*/%<>!?#\\abcxyz019_ é";

    [Fact]
    public async Task Mutated_samples_never_throw_hang_or_break_the_tree()
    {
        await Task.Run(() =>
        {
            var random = new Random(20261011);
            var slowest = 0.0;
            for (var i = 0; i < 2500; i++)
            {
                var text = Mutate(NthUiSamples.All[random.Next(NthUiSamples.All.Count)].Text, random);
                var watch = Stopwatch.StartNew();
                Check(text, random, $"case {i}");
                slowest = Math.Max(slowest, watch.Elapsed.TotalMilliseconds);
            }
            Assert.True(slowest < 3000, $"the slowest case took {slowest:0} ms");
        }).WaitAsync(TimeSpan.FromSeconds(240));
    }

    [Fact]
    public async Task Random_text_never_throws_or_hangs()
    {
        await Task.Run(() =>
        {
            var random = new Random(7);
            for (var i = 0; i < 1500; i++)
            {
                var builder = new StringBuilder();
                var length = random.Next(0, 600);
                for (var j = 0; j < length; j++)
                {
                    if (random.Next(6) == 0) builder.Append(Snippets[random.Next(Snippets.Length)]);
                    else builder.Append(Alphabet[random.Next(Alphabet.Length)]);
                }
                Check(builder.ToString(), random, $"random {i}");
            }
        }).WaitAsync(TimeSpan.FromSeconds(240));
    }

    [Fact]
    public async Task Pathological_inputs_stay_bounded()
    {
        await Task.Run(() =>
        {
            var random = new Random(3);
            string[] cases =
            [
                new string('[', 50_000),
                "text = " + new string('(', 20_000) + "1" + new string(')', 20_000),
                "let x = " + string.Join(" + ", Enumerable.Repeat("x", 3000)),
                string.Concat(Enumerable.Range(0, 3000).Select(i => new string(' ', i % 200) + "stack\n")),
                string.Concat(Enumerable.Repeat("text \"x\"\n", 20_000)),
                "code\n" + string.Concat(Enumerable.Repeat("  " + new string('z', 7000) + "\n", 100)),
                new string('a', 400_000),
                "text \"" + new string('\\', 9000) + "\"",
                string.Concat(Enumerable.Range(0, 1500).Select(i => $"let v{i} = v{i + 1} + 1\n")) + "text = v0\n",
                "state l = map(range(10000), n => map(range(10000), m => n * m))\ntext = len(l)\n",
                "table rows=(map(range(10000), n => range(10000)))\n",
                "chart line y=(range(10000)) editable=points bind=p\nstate p = range(10000)\n",
                "text = fmt(1e308 * 10, \"$0\")\ntext = fmt(-0.0, \"0.0000000000000000000000\")\n",
                string.Concat(Enumerable.Repeat("chip \"x\"\n", 3000)),
            ];
            foreach (var text in cases)
            {
                var watch = Stopwatch.StartNew();
                Check(text, random, text.Length > 40 ? text[..40] : text);
                Assert.True(watch.ElapsedMilliseconds < 10_000, $"{(text.Length > 40 ? text[..40] : text)} took {watch.ElapsedMilliseconds} ms");
            }
        }).WaitAsync(TimeSpan.FromSeconds(240));
    }

    [Fact]
    public async Task Token_soup_never_throws_and_whatever_parses_evaluates_inside_its_budget()
    {
        await Task.Run(() =>
        {
            string[] tokens =
            [
                "0", "1", "2.5", "1e308", "-1", ".5", "\"a\"", "'b'", "\"\"", "x", "y", "lst", "obj", "it", "true", "false", "null",
                "min(", "max(", "round(", "fmt(", "len(", "sum(", "avg(", "map(", "filter(", "sort(", "range(", "join(", "slice(", "pow(",
                "num(", "str(", "contains(", "clamp(", "+", "-", "*", "/", "%", "==", "!=", "<", "<=", ">", ">=", "=", "and", "or", "not", "!",
                "?", ":", ",", "(", ")", "[", "]", "{", "}", ".", "=>", "length", "&&", "||", "\"$0\"", "\"0%\"", "n =>",
            ];
            var random = new Random(99);
            var parsed = 0;
            for (var i = 0; i < 30_000; i++)
            {
                var count = random.Next(1, 30);
                var text = string.Join(random.Next(3) == 0 ? "" : " ", Enumerable.Range(0, count).Select(_ => tokens[random.Next(tokens.Length)]));
                if (!NthUiExpr.TryParse(text, out var expr, out var error))
                {
                    Assert.False(string.IsNullOrEmpty(error), text);
                    continue;
                }
                parsed++;
                _ = expr!.Names.Count;
                var value = Evaluator().Evaluate(expr);
                _ = value.Display();
                _ = value.ToJson();
            }
            Assert.True(parsed > 100, $"only {parsed} of 30,000 parsed");
        }).WaitAsync(TimeSpan.FromSeconds(240));
    }

    [Fact]
    public async Task Generated_expression_trees_always_parse_and_finish()
    {
        await Task.Run(() =>
        {
            var random = new Random(1234);
            var slowest = 0.0;
            for (var i = 0; i < 8000; i++)
            {
                var text = Generate(random, random.Next(1, 7));
                Assert.True(NthUiExpr.TryParse(text, out var expr, out var error), $"{text}: {error}");
                var watch = Stopwatch.StartNew();
                var value = Evaluator().Evaluate(expr!);
                slowest = Math.Max(slowest, watch.Elapsed.TotalMilliseconds);
                _ = NthUiFormat.Format(value, "$#,##0.00");
            }
            Assert.True(slowest < 2000, $"the slowest evaluation took {slowest:0} ms");
        }).WaitAsync(TimeSpan.FromSeconds(240));
    }

    private static NthUiEvaluator Evaluator()
    {
        var names = new Dictionary<string, NthUiValue>(StringComparer.Ordinal)
        {
            ["x"] = NthUiValue.Of(3),
            ["y"] = NthUiValue.Of("eu"),
            ["lst"] = NthUiValue.List([NthUiValue.Of(1), NthUiValue.Of(2), NthUiValue.Of(3)]),
            ["obj"] = NthUiValue.Object([new("a", NthUiValue.Of(1)), new("eu", NthUiValue.Of(0.046))]),
        };
        return new NthUiEvaluator(n => names.TryGetValue(n, out var v) ? v : NthUiValue.Error($"'{n}' is not defined"));
    }

    private static string Generate(Random random, int depth)
    {
        if (depth <= 0)
        {
            return random.Next(8) switch
            {
                0 => random.Next(-5, 100).ToString(System.Globalization.CultureInfo.InvariantCulture),
                1 => (random.NextDouble() * 1000).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                2 => "\"" + "abc"[random.Next(3)] + "\"",
                3 => "x",
                4 => "lst",
                5 => "obj",
                6 => random.Next(2) == 0 ? "true" : "null",
                _ => "y",
            };
        }
        string Sub() => Generate(random, depth - 1);
        return random.Next(16) switch
        {
            0 => $"({Sub()} {new[] { "+", "-", "*", "/", "%" }[random.Next(5)]} {Sub()})",
            1 => $"({Sub()} {new[] { "==", "!=", "<", "<=", ">", ">=" }[random.Next(6)]} {Sub()})",
            2 => $"({Sub()} {(random.Next(2) == 0 ? "and" : "or")} {Sub()})",
            3 => $"(not {Sub()})",
            4 => $"-{Sub()}",
            5 => $"({Sub()} ? {Sub()} : {Sub()})",
            6 => $"{new[] { "min", "max", "sum", "avg", "len", "sort", "reverse", "str", "num", "abs", "floor", "ceil", "sqrt" }[random.Next(13)]}({Sub()})",
            7 => $"{new[] { "pow", "round", "fmt", "join", "contains", "slice" }[random.Next(6)]}({Sub()}, {Sub()})",
            8 => $"map({Sub()}, v => {Sub()} + v)",
            9 => $"filter({Sub()}, it > {Sub()})",
            10 => $"range({Sub()})",
            11 => $"range({Sub()}, {Sub()}, {Sub()})",
            12 => $"[{Sub()}, {Sub()}]",
            13 => $"{{a: {Sub()}, \"b\": {Sub()}}}",
            14 => $"{Sub()}[{Sub()}]",
            _ => $"clamp({Sub()}, {Sub()}, {Sub()})",
        };
    }

    private static string Mutate(string text, Random random)
    {
        var builder = new StringBuilder(text);
        var edits = random.Next(1, 9);
        for (var e = 0; e < edits; e++)
        {
            var at = builder.Length == 0 ? 0 : random.Next(builder.Length);
            switch (random.Next(7))
            {
                case 0:
                    builder.Insert(at, Alphabet[random.Next(Alphabet.Length)]);
                    break;
                case 1:
                    builder.Insert(at, Snippets[random.Next(Snippets.Length)]);
                    break;
                case 2 when builder.Length > 0:
                    builder.Remove(at, Math.Min(builder.Length - at, random.Next(1, 30)));
                    break;
                case 3:
                    {
                        var lines = builder.ToString().Split('\n').ToList();
                        var a = random.Next(lines.Count);
                        var b = random.Next(lines.Count);
                        (lines[a], lines[b]) = (lines[b], lines[a]);
                        builder.Clear().Append(string.Join('\n', lines));
                        break;
                    }
                case 4:
                    {
                        var lines = builder.ToString().Split('\n').ToList();
                        var a = random.Next(lines.Count);
                        lines[a] = new string(' ', random.Next(0, 9)) + lines[a].TrimStart();
                        builder.Clear().Append(string.Join('\n', lines));
                        break;
                    }
                case 5:
                    {
                        var lines = builder.ToString().Split('\n').ToList();
                        var a = random.Next(lines.Count);
                        lines.Insert(a, lines[random.Next(lines.Count)]);
                        builder.Clear().Append(string.Join('\n', lines));
                        break;
                    }
                default:
                    builder.Length = random.Next(builder.Length + 1);
                    break;
            }
        }
        return builder.ToString();
    }

    private static void Check(string text, Random random, string label)
    {
        var whole = NthUiParser.Parse(text);
        var problems = NthUiValidator.Problems(whole);
        Assert.True(problems.Count == 0, $"{label}: {string.Join("; ", problems.Take(3))}\n{text}");
        var parser = new NthUiParser(random.Next(4) == 0 ? NthUiProfile.Lite : NthUiProfile.Full);
        var runtime = new NthUiRuntime(parser.Document);
        var at = 0;
        while (at < text.Length)
        {
            var size = Math.Min(text.Length - at, random.Next(1, 64));
            parser.Feed(text.Substring(at, size));
            _ = parser.Pending;
            runtime.Sync();
            at += size;
        }
        parser.Complete();
        runtime.Sync();
        Assert.Empty(NthUiValidator.Problems(parser.Document));
        if (parser.Document.Profile == NthUiProfile.Full)
            Assert.Equal(whole.Nodes.Select(n => n.Key + n.Kind + n.Source), parser.Document.Nodes.Select(n => n.Key + n.Kind + n.Source));
        Exercise(parser.Document, runtime, random);
    }

    private static void Exercise(NthUiDocument document, NthUiRuntime runtime, Random random)
    {
        NthUiValue[] pool =
        [
            NthUiValue.Of(0), NthUiValue.Of(-1), NthUiValue.Of(3.5), NthUiValue.Of(1e300), NthUiValue.Of(""), NthUiValue.Of("eu"),
            NthUiValue.Of("2026-10-12"), NthUiValue.True, NthUiValue.List([NthUiValue.Of(1)]), NthUiValue.Error("x"), NthUiValue.Null,
        ];
        foreach (var node in document.Nodes.Take(400))
        {
            runtime.BeginPass();
            _ = runtime.IsVisible(node);
            _ = runtime.Text(node);
            foreach (var arg in node.Args.Values) _ = runtime.Evaluate(arg).Display();
            _ = runtime.Cells(node);
            switch (node.Kind)
            {
                case "table":
                    _ = runtime.TableText(node);
                    _ = runtime.TableCsv(node);
                    break;
                case "chart":
                    _ = runtime.Chart(node);
                    runtime.SetPoint(node, random.Next(-1, 8), random.NextDouble() * 100 - 20);
                    runtime.NudgePoint(node, random.Next(-1, 8), random.Next(2) == 0 ? -1 : 1);
                    break;
                case "segmented" or "select":
                    _ = runtime.Options(node);
                    break;
                case "tabs":
                    _ = runtime.SelectedTab(node);
                    break;
                case "steps":
                    _ = runtime.CurrentStep(node);
                    runtime.MoveStep(node, 1);
                    break;
                case "chip":
                    _ = runtime.IsSelected(node);
                    break;
                case "item":
                    _ = runtime.IsChecked(node);
                    break;
            }
            if (node.Role == NthUiRole.Input)
            {
                runtime.SetInput(node, pool[random.Next(pool.Length)]);
                runtime.StepInput(node, random.Next(2) == 0 ? -1 : 1);
            }
            if (node.Role == NthUiRole.Action || node.Kind is "chip" or "item" or "tab" or "step" or "form") _ = runtime.Run(node);
        }
        _ = runtime.ViewText();
        var saved = runtime.SaveState();
        var restored = new NthUiRuntime(document);
        restored.LoadState(saved);
    }
}
