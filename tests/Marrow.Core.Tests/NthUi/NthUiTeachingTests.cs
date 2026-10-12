using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Marrow.Core.NthUi;

namespace Marrow.Core.Tests.NthUi;

// DESIGN.md 14 row W17 and 7.3 (web surfaces W17; ED-NEW-web-w17-1): the words models are taught from. The worked examples are
// parsed and computed, so nothing a model learns would degrade; the text follows the tools a session offers; the MCP
// instructions' core is self-sufficient under 2,000 bytes for every combination; the compact references name every kind and
// function of the language; the lite teaching names only the lite subset; and nothing is another product's wording.
[Trait("Decision", "ED-NEW-web-w17-1")]
public sealed class NthUiTeachingTests
{
    private static readonly string[] Vendors = ["Claude", "Anthropic", "ChatGPT", "OpenAI", "GPT-", "Gemini", "Intelligent UI", "Copilot", "Cursor"];

    private static IEnumerable<NthUiTeachingOptions> EveryOptionSet()
    {
        foreach (var profile in new[] { NthUiProfile.Full, NthUiProfile.Lite })
        foreach (var live in new[] { false, true })
        foreach (var artifacts in new[] { false, true })
        foreach (var html in new[] { false, true })
        foreach (var ask in new[] { false, true })
        foreach (var reference in new[] { false, true })
        foreach (var level in Enum.GetValues<VisualAnswerLevel>())
            yield return new NthUiTeachingOptions { Profile = profile, LiveTools = live, ArtifactTools = artifacts, HtmlWidget = html, AskUser = ask, Reference = reference, Visual = level };
    }

    private static void AssertComputes(NthUiDocument document, string id)
    {
        var runtime = new NthUiRuntime(document);
        runtime.Sync();
        foreach (var name in document.Declarations.Keys) Assert.False(runtime.Get(name).IsError, $"{id}: {name} is an error value");
        foreach (var node in document.Nodes.Where(n => !n.Degraded))
        {
            foreach (var arg in node.Args.Values) Assert.False(runtime.Evaluate(arg).IsError, $"{id}: {node.Kind} {arg.Name} is an error value");
            foreach (var cell in node.Cells) Assert.False(runtime.Evaluate(cell).IsError, $"{id}: a cell of {node.Kind} is an error value");
        }
    }

    [Fact]
    public void Every_worked_example_is_clean_without_warnings_and_computes_without_an_error_value()
    {
        Assert.Equal(6, NthUiExamples.Six.Count);
        Assert.Equal(6, NthUiExamples.More.Count);
        Assert.Equal(3, NthUiExamples.Lite.Count);
        var all = NthUiExamples.Six.Concat(NthUiExamples.More).Concat(NthUiExamples.Lite).ToArray();
        Assert.Equal(all.Length, all.Select(e => e.Id).Distinct().Count());
        Assert.Equal(all.Length, all.Select(e => e.Ask).Distinct().Count());
        foreach (var example in all)
        {
            var document = NthUiParser.Parse(example.View + "\n");
            var notes = string.Join("; ", document.Diagnostics.Select(d => d.ToString()));
            Assert.Empty(NthUiValidator.Problems(document));
            Assert.True(document.IsClean, $"{example.Id}: {notes}");
            Assert.True(document.Diagnostics.Count == 0, $"{example.Id} warns: {notes}");
            AssertComputes(document, example.Id);
            Assert.False(string.IsNullOrWhiteSpace(example.Lead));
            Assert.False(string.IsNullOrWhiteSpace(example.Why));
        }
    }

    [Fact]
    public void The_lite_examples_use_only_the_lite_subset_and_draw_the_same_in_both_profiles()
    {
        var lite = NthUiSchema.LiteKinds.Append(NthUiSchema.TableRow.Kind).ToHashSet(StringComparer.Ordinal);
        foreach (var example in NthUiExamples.Lite)
        {
            var asLite = NthUiParser.Parse(example.View + "\n", NthUiProfile.Lite);
            Assert.True(asLite.IsClean, example.Id);
            Assert.Empty(asLite.Diagnostics);
            Assert.All(asLite.Nodes, node => Assert.Contains(node.Kind, lite));
            var asFull = NthUiParser.Parse(example.View + "\n");
            Assert.Equal(asFull.Nodes.Select(n => n.Key + " " + n.Kind), asLite.Nodes.Select(n => n.Key + " " + n.Kind));
        }
        // The full examples are not in the subset, so a lite parse degrades part of them.
        Assert.Contains(NthUiExamples.Six, e => NthUiParser.Parse(e.View + "\n", NthUiProfile.Lite).Nodes.Any(n => n.Degraded));
    }

    [Fact]
    public void The_plain_text_examples_show_that_no_view_is_the_default()
    {
        Assert.Equal(3, NthUiExamples.PlainText.Count);
        Assert.All(NthUiExamples.PlainText, p => Assert.DoesNotContain("```", p.Answer, StringComparison.Ordinal));
        foreach (var options in EveryOptionSet())
            Assert.Contains("No view needed", NthUiTeaching.SystemBlock(options), StringComparison.Ordinal);
    }

    [Fact]
    public void The_system_block_carries_the_rules_the_language_and_every_example_as_it_was_written()
    {
        var full = NthUiTeaching.SystemBlock(new NthUiTeachingOptions());
        foreach (var phrase in new[] { "Plain text is the default", "At most one view per answer", "THE VIEW LANGUAGE", "KEEP VIEWS VALID", "EXAMPLES", "SHOWING A VIEW", "SAVING AN ARTIFACT" })
            Assert.Contains(phrase, full, StringComparison.Ordinal);
        Assert.Equal(NthUiExamples.Six.Select(e => e.View), Views(full));
        foreach (var example in NthUiExamples.Six)
        {
            Assert.Contains(example.Ask, full, StringComparison.Ordinal);
            Assert.Contains(example.Lead, full, StringComparison.Ordinal);
        }
        var lite = NthUiTeaching.SystemBlock(new NthUiTeachingOptions { Profile = NthUiProfile.Lite });
        Assert.Equal(NthUiExamples.Lite.Select(e => e.View), Views(lite));
        Assert.Contains("THE VIEW LANGUAGE (SMALL SET)", lite, StringComparison.Ordinal);
        Assert.DoesNotContain("THE VIEW LANGUAGE\n", lite, StringComparison.Ordinal);
        Assert.True(lite.Length < full.Length * 0.6, $"the lite block is {lite.Length} against {full.Length}");
    }

    [Fact]
    public void The_block_follows_the_tools_a_session_offers()
    {
        var fenced = NthUiTeaching.SystemBlock(new NthUiTeachingOptions { ArtifactTools = false });
        Assert.Contains("a fenced block", fenced, StringComparison.Ordinal);
        Assert.Contains("nth-artifact", fenced, StringComparison.Ordinal);
        Assert.DoesNotContain("ui_show", fenced, StringComparison.Ordinal);
        Assert.DoesNotContain("artifact_create", fenced, StringComparison.Ordinal);
        Assert.DoesNotContain("html_widget", fenced, StringComparison.Ordinal);
        Assert.DoesNotContain("ask_user", fenced, StringComparison.Ordinal);

        var tools = NthUiTeaching.SystemBlock(new NthUiTeachingOptions { LiveTools = true, HtmlWidget = true, AskUser = true, Reference = true });
        Assert.Contains("Call ui_show with the view text as its ui argument", tools, StringComparison.Ordinal);
        Assert.Contains("ui_update", tools, StringComparison.Ordinal);
        Assert.Contains("html_widget", tools, StringComparison.Ordinal);
        Assert.Contains("ask_user", tools, StringComparison.Ordinal);
        Assert.Contains("artifact_create", tools, StringComparison.Ordinal);
        Assert.DoesNotContain("nth-html", tools, StringComparison.Ordinal);

        var html = NthUiTeaching.SystemBlock(new NthUiTeachingOptions { HtmlWidget = true });
        Assert.Contains("nth-html", html, StringComparison.Ordinal);
        var lite = NthUiTeaching.SystemBlock(new NthUiTeachingOptions { Profile = NthUiProfile.Lite, HtmlWidget = true, AskUser = true });
        Assert.DoesNotContain("html_widget", lite, StringComparison.Ordinal);
        Assert.DoesNotContain("nth-html", lite, StringComparison.Ordinal);
    }

    [Fact]
    public void The_visual_answers_setting_changes_only_its_own_line()
    {
        string Block(VisualAnswerLevel level) => NthUiTeaching.SystemBlock(new NthUiTeachingOptions { Visual = level });
        var balanced = Block(VisualAnswerLevel.Balanced);
        var fewer = Block(VisualAnswerLevel.Fewer);
        var more = Block(VisualAnswerLevel.More);
        Assert.DoesNotContain("fewer visual answers", balanced, StringComparison.Ordinal);
        Assert.DoesNotContain("likes visual answers", balanced, StringComparison.Ordinal);
        Assert.Contains("asked for fewer visual answers", fewer, StringComparison.Ordinal);
        Assert.Contains("only when they ask for one", fewer, StringComparison.Ordinal);
        Assert.Contains("likes visual answers", more, StringComparison.Ordinal);
        Assert.Contains("never for a plain fact", more, StringComparison.Ordinal);
        Assert.Equal(Without(balanced, "The reader"), Without(fewer, "The reader"));
        Assert.Equal(Without(balanced, "The reader"), Without(more, "The reader"));
    }

    private static string Without(string text, string prefix) => string.Join("\n", text.Split('\n').Where(line => !line.StartsWith(prefix, StringComparison.Ordinal)));

    [Fact]
    public void The_MCP_core_is_self_sufficient_and_under_2000_bytes_for_every_combination()
    {
        var count = 0;
        foreach (var options in EveryOptionSet())
        {
            count++;
            var core = NthUiTeaching.Core(options);
            Assert.True(core.Length <= NthUiTeaching.McpCoreLimit, $"{core.Length} characters for {options}");
            Assert.Equal(core.Length, System.Text.Encoding.UTF8.GetByteCount(core));
            Assert.All(core, c => Assert.True(c == '\n' || (c >= ' ' && c <= '~'), "not ASCII: " + (int)c));
            foreach (var phrase in new[] { "Text is the default", "at most one view", "state n = 3", "table cols=", "followups" })
                Assert.Contains(phrase, core, StringComparison.Ordinal);
            Assert.Equal(options.LiveTools, core.Contains("ui_show", StringComparison.Ordinal));
            Assert.Equal(options.Reference, core.Contains("ui_reference", StringComparison.Ordinal));
            Assert.Equal(options.AskUser, core.Contains("ask_user", StringComparison.Ordinal));
            Assert.Equal(options.HtmlWidget && options.LiveTools, core.Contains("html_widget", StringComparison.Ordinal));
            Assert.Equal(options.HtmlWidget && !options.LiveTools, core.Contains("nth-html", StringComparison.Ordinal));
            Assert.Equal(options.ArtifactTools, core.Contains("artifact_create", StringComparison.Ordinal));
            var instructions = NthUiTeaching.McpInstructions(options);
            Assert.StartsWith(core + "\n\n", instructions, StringComparison.Ordinal);
            Assert.Contains("EXAMPLES", instructions, StringComparison.Ordinal);
        }
        Assert.Equal(2 * 2 * 2 * 2 * 2 * 2 * 3, count);
    }

    [Fact]
    public void The_reference_topics_are_the_pages_sections_and_the_examples()
    {
        foreach (var topic in NthUiTeaching.Topics)
        {
            var text = NthUiTeaching.Reference(topic);
            Assert.False(string.IsNullOrWhiteSpace(text), topic);
            Assert.NotEqual("", NthUiTeaching.TopicSummary(topic));
        }
        var language = NthUiTeaching.Reference("language")!;
        Assert.Contains("## 1. Lines", language, StringComparison.Ordinal);
        Assert.Contains("## 4. State and let", language, StringComparison.Ordinal);
        Assert.Contains("## 7. Streaming", language, StringComparison.Ordinal);
        Assert.DoesNotContain("## 5. Components", language, StringComparison.Ordinal);
        var components = NthUiTeaching.Reference("components")! + NthUiTeaching.Reference("actions")!;
        foreach (var spec in NthUiSchema.Kinds.Where(s => s.Role is not NthUiRole.Declaration)) Assert.Contains($"`{spec.Kind}`", components, StringComparison.Ordinal);
        Assert.Contains("## 5. Components", NthUiTeaching.Reference("components")!, StringComparison.Ordinal);
        Assert.Contains("## 6. Actions", NthUiTeaching.Reference("actions")!, StringComparison.Ordinal);
        Assert.Contains("## 10. Limits", NthUiTeaching.Reference("errors")!, StringComparison.Ordinal);
        var lite = NthUiTeaching.Reference("lite")!;
        Assert.Contains("## 8. Lite", lite, StringComparison.Ordinal);
        Assert.Equal(NthUiExamples.Lite.Count + 1, Views(lite).Count); // the page's own lite example, then ours
        var examples = NthUiTeaching.Reference("examples")!;
        Assert.Equal(NthUiExamples.Six.Concat(NthUiExamples.More).Select(e => e.View), Views(examples));
        Assert.Null(NthUiTeaching.Reference("nonsense"));
        Assert.Null(NthUiTeaching.Reference(null));
        Assert.Equal(NthUiTeaching.Reference("components"), NthUiTeaching.Reference("  Components "));
        Assert.Equal(File.ReadAllText(DocPath()).Replace("\r\n", "\n", StringComparison.Ordinal), NthUiTeaching.ReferencePage);
    }

    [Fact]
    public void The_compact_references_name_every_kind_and_function_and_the_lite_one_only_its_own()
    {
        var full = NthUiTeaching.FullReference;
        foreach (var spec in NthUiSchema.Kinds)
            Assert.Matches(new Regex(@"(?<![A-Za-z])" + spec.Kind + @"(?![A-Za-z])"), full);
        foreach (var name in NthUiExpr.Functions.Keys)
            Assert.Matches(new Regex(@"(?<![A-Za-z])" + name + @"(?![A-Za-z])"), full);
        var lite = NthUiTeaching.LiteReference;
        foreach (var kind in NthUiSchema.LiteKinds) Assert.Contains(kind, lite, StringComparison.Ordinal);
        var liteKinds = NthUiSchema.LiteKinds.ToHashSet(StringComparer.Ordinal);
        foreach (var spec in NthUiSchema.Kinds.Where(s => !liteKinds.Contains(s.Kind)))
            Assert.DoesNotMatch(new Regex(@"^\s*" + spec.Kind + @"\s", RegexOptions.Multiline), lite);
        foreach (var name in new[] { "map", "filter", "sort", "join", "avg" }) Assert.DoesNotContain(name + "(", lite, StringComparison.Ordinal);
    }

    [Fact]
    public void The_teaching_is_ours_ascii_and_never_names_another_product()
    {
        var texts = new List<string>();
        foreach (var options in EveryOptionSet())
        {
            texts.Add(NthUiTeaching.SystemBlock(options));
            texts.Add(NthUiTeaching.McpInstructions(options));
        }
        texts.Add(NthUiTeaching.ShowToolDescription);
        texts.AddRange(NthUiTeaching.Topics.Select(t => NthUiTeaching.Reference(t)!));
        foreach (var text in texts.Distinct())
        {
            foreach (var vendor in Vendors) Assert.DoesNotContain(vendor, text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain('\r', text);
        }
        Assert.All(NthUiTeaching.FullReference + NthUiTeaching.LiteReference, c => Assert.True(c <= '~', "not ASCII: " + (int)c));
        Assert.All(NthUiExamples.Six.Concat(NthUiExamples.More).Concat(NthUiExamples.Lite).Select(e => e.Ask + e.Lead + e.View + e.Why).SelectMany(t => t),
            c => Assert.True(c <= '~', "not ASCII: " + (int)c));
    }

    [Fact]
    public void The_blocks_stay_inside_a_prompt_budget()
    {
        var full = NthUiTeaching.SystemBlock(new NthUiTeachingOptions { LiveTools = true, HtmlWidget = true, AskUser = true, Reference = true, Visual = VisualAnswerLevel.More });
        var lite = NthUiTeaching.SystemBlock(new NthUiTeachingOptions { Profile = NthUiProfile.Lite, Visual = VisualAnswerLevel.More });
        Assert.InRange(full.Length, 8_000, 18_000);
        Assert.InRange(lite.Length, 3_000, 9_000);
        Assert.Equal(full, NthUiTeaching.SystemBlock(new NthUiTeachingOptions { LiveTools = true, HtmlWidget = true, AskUser = true, Reference = true, Visual = VisualAnswerLevel.More }));
    }

    [Fact]
    public void The_show_tool_description_is_short_and_points_to_the_reference()
    {
        Assert.InRange(NthUiTeaching.ShowToolDescription.Length, 200, 700);
        Assert.Contains("ui_reference", NthUiTeaching.ShowToolDescription, StringComparison.Ordinal);
        Assert.Contains("One view per answer", NthUiTeaching.ShowToolDescription, StringComparison.Ordinal);
    }

    /// <summary>The views in the fenced nth-ui blocks of a text, in order.</summary>
    private static List<string> Views(string text)
    {
        var views = new List<string>();
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith("```nth-ui", StringComparison.Ordinal)) continue;
            var close = i + 1;
            while (close < lines.Length && lines[close] != "```") close++;
            Assert.True(close < lines.Length, "an unclosed fence");
            views.Add(string.Join("\n", lines[(i + 1)..close]));
            i = close;
        }
        return views;
    }

    private static string DocPath([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", "..", "docs", "nth-ui.md"));
}
