using Marrow.Core.NthUi;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.NthUi;

// DESIGN.md 13.1 NthUiTests, the validator's degrade-to-text and the lite subset (web surfaces W15; ED-NEW-web-w15-1,
// traceability alias ED-NEW-webnthui-1): 7.2 "Unknown or invalid lines degrade to their text"; text-like lines that do
// not read are taken as plain words; cosmetic slips (an unknown attribute, a bad icon) are left out with a warning;
// `editable` and `bind` are parsed (14, W15 "done when"); 7.3's lite subset keeps only its components.
[Trait("Decision", "ED-NEW-webnthui-1")]
public sealed class NthUiValidatorTests
{
    [Theory]
    [InlineData("widget \"x\"", "there is no component 'widget'")]
    [InlineData("- a markdown bullet", "the line does not start with a component")]
    [InlineData("# A markdown heading", "the line does not start with a component")]
    [InlineData("stepper", "stepper needs bind")]
    [InlineData("stepper n min=\"low\"", "min needs a number")]
    [InlineData("stepper n min=1 min=2", "min is given twice")]
    [InlineData("chart donut", "chart does not take the word 'donut'")]
    [InlineData("card \"A\" = 3", "card takes no = value")]
    [InlineData("state", "state needs name")]
    [InlineData("state x", "state needs = and a value")]
    [InlineData("state fmt = 1", "'fmt' cannot be a name")]
    [InlineData("let total = sum(", "a value is missing")]
    [InlineData("let total = price(region)", "there is no function 'price'")]
    [InlineData("row \"a\" = (1 +", "a value is missing")]
    [InlineData("image \"http://example.com/a.png\"", "an image loads only from https, data:image or artifact: sources")]
    [InlineData("image \"data:image/svg+xml;base64,PHN2Zz4=\"", "an image loads only from https, data:image or artifact: sources")]
    [InlineData("open \"Docs\" url=\"javascript:alert(1)\"", "'javascript:alert(1)' is not an address nth opens")]
    [InlineData("open \"Docs\"", "open needs an http or https address in quotes")]
    [InlineData("call promote", "call needs server.tool")]
    [InlineData("call a.b [1, 2]", "call does not take the value [1, 2]")]
    [InlineData("segmented region", "segmented needs options")]
    [InlineData("map\n  stop \"Lisbon\" lat=38.7", "stop needs lon")]
    [InlineData("export pdf", "export does not take the word 'pdf'")]
    [InlineData("stepper n min=1 > 0", "unexpected '>' at 17")]
    [InlineData("chip \"loose\"", "chip cannot be at the top level")]
    [InlineData("item \"loose\"", "item cannot be at the top level")]
    [InlineData("followups\n  item \"no\"", "item cannot be inside followups")]
    public void Lines_that_do_not_fit_degrade_to_their_text_with_the_reason(string source, string reason)
    {
        var document = NthUiParser.Parse(source);
        var degraded = document.Nodes.Last();
        Assert.True(degraded.Degraded, $"{degraded} from '{source}' did not degrade");
        Assert.Equal("text", degraded.Kind);
        Assert.Equal(source.Split('\n')[^1].Trim(), degraded.Text);
        Assert.Equal(reason, degraded.Problem);
        Assert.Contains(document.Diagnostics, d => d.Severity == NthUiSeverity.Error && d.Message.StartsWith(reason, StringComparison.Ordinal));
        Assert.Empty(NthUiValidator.Problems(document));
        Assert.False(document.IsClean);
    }

    [Fact]
    public void Text_like_lines_that_do_not_read_become_plain_words()
    {
        var document = NthUiParser.Parse("""
            text Hello, world!
            heading Monthly costs at a glance
            chip "Add a CDN" now
            card "A"
              callout Remember to save first.
            followups
              chip Ask about pricing
            send Plan for twice the traffic
            text "Unclosed at the end
            """);
        Assert.Equal(["Hello, world!", "Monthly costs at a glance", "chip \"Add a CDN\" now", "A", "Remember to save first.", null, "Ask about pricing",
            "Plan for twice the traffic", "Unclosed at the end"], document.Nodes.Select(n => n.Text));
        Assert.True(document.Nodes[2].Degraded, "a chip at the top level still degrades");
        Assert.All(document.Nodes.Where((_, i) => i != 2), n => Assert.False(n.Degraded, n.ToString()));
    }

    [Fact]
    public void Unknown_attributes_and_bad_icons_are_left_out_with_a_warning()
    {
        var document = NthUiParser.Parse("card \"A\" icon=\"Not An Icon\" colour=red\n  stepper n min=1 color=\"blue\"\n");
        Assert.True(document.IsClean);
        Assert.Null(document.Roots[0].Arg("icon"));
        Assert.Null(document.Roots[0].Arg("colour"));
        Assert.Equal(1, document.Roots[0].Children[0].Arg("min")!.Literal.AsNumber);
        Assert.Equal(3, document.Diagnostics.Count(d => d.Severity == NthUiSeverity.Warning));
    }

    [Fact]
    public void Editable_and_bind_are_parsed_and_editable_needs_bind()
    {
        var document = NthUiParser.Parse(NthUiSamples.EditableChart);
        var chart = document.Nodes.Single(n => n.Kind == "chart");
        Assert.Equal("points", chart.Arg("editable")!.Literal.AsText);
        Assert.Equal("demand", chart.Arg("bind")!.Literal.AsText);
        Assert.Equal("demand", chart.Name);
        Assert.Contains("demand", chart.Reads);
        Assert.Equal([0.0, 60.0, 1.0], new[] { "min", "max", "step" }.Select(a => chart.Arg(a)!.Literal.AsNumber));

        var loose = NthUiParser.Parse("chart line editable=points y=[1, 2]\n");
        Assert.False(loose.Roots[0].Degraded);
        Assert.Null(loose.Roots[0].Arg("editable"));
        Assert.Contains(loose.Diagnostics, d => d.Message.Contains("needs bind=", StringComparison.Ordinal));

        Assert.True(NthUiParser.Parse("chart line editable=bars bind=x\n").Roots[0].Degraded);
        Assert.True(NthUiParser.Parse("chart line bind=\"x\"\n").Roots[0].Degraded);
        foreach (var input in new[] { "stepper", "slider", "toggle", "textfield", "select options=\"a,b\"", "segmented options=\"a,b\"", "date" })
        {
            var kind = input.Split(' ')[0];
            var positional = NthUiParser.Parse($"{kind} level {string.Join(' ', input.Split(' ').Skip(1))}\n").Roots[0];
            var named = NthUiParser.Parse($"{input} bind=level\n").Roots[0];
            Assert.Equal("level", positional.Name);
            Assert.Equal("level", named.Name);
            Assert.Contains("level", named.Reads);
        }
        foreach (var owner in new[] { "checklist", "chips", "tabs", "steps" })
        {
            Assert.Equal("picked", NthUiParser.Parse($"{owner} bind=picked\n").Roots[0].Name);
            Assert.True(NthUiParser.Parse($"{owner} bind=\"picked\"\n").Roots[0].Degraded);
        }
    }

    [Fact]
    public void Names_are_declared_once_and_lets_are_never_set()
    {
        var document = NthUiParser.Parse("""
            state a = 1
            state a = 2
            let b = a * 2
            stepper b
            set b = 3
            slider c
            let c = 1
            state if_ = 2
            """);
        Assert.Equal([false, true, false, true, true, false, true, false], document.Nodes.Select(n => n.Degraded));
        Assert.StartsWith("a is already declared on line 1", document.Nodes[1].Problem, StringComparison.Ordinal);
        Assert.Equal("b is computed by a let, so nothing can set it", document.Nodes[3].Problem);
        Assert.Equal("c is set by a control, so it cannot be a let", document.Nodes[6].Problem);
    }

    [Fact]
    public void Undefined_names_are_noted_when_the_stream_ends()
    {
        var parser = new NthUiParser();
        parser.Feed("text = fmt(total, \"$0\")\nlet total = cost * 2\n");
        Assert.Empty(parser.Document.Diagnostics);
        parser.Complete();
        var warning = Assert.Single(parser.Document.Diagnostics);
        Assert.Equal(new NthUiDiagnostic(2, NthUiSeverity.Warning, "cost is not defined, so it shows as a dash"), warning);
        Assert.True(parser.Document.IsClean);
    }

    [Fact]
    public void Place_decides_what_row_toggle_and_chip_mean()
    {
        var document = NthUiParser.Parse("""
            row gap=4
              text "a layout row"
            table
              row "a" "table" "row"
            toggle dark label="Dark"
            actions
              toggle dark "Flip"
            button "Twice"
              toggle dark
              toggle dark
            chips bind=f
              chip "filter"
            followups
              chip "send"
            """);
        Assert.True(document.IsClean);
        Assert.Equal(NthUiRole.Container, document.Roots[0].Role);
        Assert.Same(NthUiSchema.TableRow, document.Roots[1].Children[0].Spec);
        Assert.Equal(3, document.Roots[1].Children[0].Cells.Count);
        Assert.Equal(NthUiRole.Input, document.Roots[2].Role);
        Assert.Same(NthUiSchema.ToggleAction, document.Roots[3].Children[0].Spec);
        Assert.All(document.Roots[4].Children, c => Assert.Same(NthUiSchema.ToggleAction, c.Spec));
    }

    [Fact]
    public void The_lite_profile_keeps_only_the_lite_subset()
    {
        Assert.Equal(["text", "card", "chart", "series", "table", "checklist", "item", "chips", "chip", "followups", "stepper", "slider", "state", "let"], NthUiSchema.LiteKinds);
        var lite = NthUiParser.Parse(NthUiSamples.Lite, NthUiProfile.Lite);
        Assert.True(lite.IsClean, string.Join("; ", lite.Diagnostics));
        Assert.Empty(NthUiValidator.Problems(lite));

        var full = NthUiParser.Parse(NthUiSamples.DesignHosting, NthUiProfile.Lite);
        Assert.Empty(NthUiValidator.Problems(full));
        Assert.Equal(["segmented", "actions", "copy", "send"], full.Nodes.Where(n => n.Degraded).Select(n => n.Source.Split(' ')[0]));
        Assert.Equal("segmented is not in the lite subset", full.Nodes.First(n => n.Degraded).Problem);
        Assert.All(full.Nodes.Where(n => n.Source.StartsWith("copy", StringComparison.Ordinal) || n.Source.StartsWith("send", StringComparison.Ordinal)),
            n => Assert.True(n.Degraded));
        foreach (var (name, text) in NthUiSamples.All)
        {
            var document = NthUiParser.Parse(text, NthUiProfile.Lite);
            Assert.True(NthUiValidator.Problems(document).Count == 0, name);
        }
    }

    [Fact]
    public void Images_load_only_from_allowed_sources()
    {
        Assert.True(NthUiValidator.IsAllowedUrl("image", "https://example.com/a.png"));
        Assert.True(NthUiValidator.IsAllowedUrl("image", "data:image/webp;base64,UklGRg=="));
        Assert.True(NthUiValidator.IsAllowedUrl("image", "artifact:a1b2/assets/logo.png"));
        Assert.False(NthUiValidator.IsAllowedUrl("image", "artifact:a1b2/../secret"));
        Assert.False(NthUiValidator.IsAllowedUrl("image", "file:///C:/Windows/win.ini"));
        Assert.False(NthUiValidator.IsAllowedUrl("image", "data:text/html,<script>"));
        Assert.True(NthUiValidator.IsAllowedUrl("open", "http://localhost:5173/"));
        Assert.False(NthUiValidator.IsAllowedUrl("open", "ms-settings:privacy"));
        Assert.False(NthUiValidator.IsAllowedUrl("open", "https://exa mple.com"));
    }
}
