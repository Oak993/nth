using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// ED-290 (1), SETTLED 2026-10-04 (PR-MCP-199, PR-CHAT-115): an explicitly written SwiftUI text style in the default design
// draws in an absolute line box, body 18 (its baseline 13 below the box's top), callout 17 (baseline 12) and subheadline 16
// (baseline 12), at every weight, with its glyphs at their true size (13 / 12 / 11, ED-059). Measured on the reference
// videos (porting E_lineboxes_verify, table "to implement"): body at 1-settings-tour.mov S 183.5, S 187.5, S 213.0 and S 23.5;
// callout at S 183.5, S 186.0, S 213.0 and 2-chat-use.mov C 152.6 and C 9.0; subheadline at S 183.5, S 187.5, S 23.5,
// S 212.0-213.0 and C 152.6. The extra height is split half above and half below the face's own box, so the baseline sits
// the face's ascent plus half the extra below the top: Inter's 13.7 / 12.9 / 12.0, within a point of the Mac's 13 / 12 / 12.
// SwiftUI's implicit default (a Text with no .font in its ancestry) keeps the face's own box: the sent bubble at C 102.0 is
// 34 = 9 + 16 + 9 (SF's plain 13-point box). So do the fixed sizes (.system(size:), the NSFont factories: S 168.0, S 190.0
// to 194.0, S 186.0), the monospaced design and a text input. Headline, the titles, footnote and the captions are not
// measured and keep the face's own box (TODO in ED-290 (1)). The face's own box is read from the text's layout, unrounded: a
// control's DesiredSize is rounded up to the pixel by layout rounding.
[Collection("Headless")]
public sealed class SwiftFontLineHeightTests(HeadlessSession headless)
{
    private static (double Ascent, double Natural) FaceBox(SwiftFont font)
    {
        var resolved = font.Resolve();
        Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(resolved.FontFamily, resolved.FontStyle, resolved.FontWeight), out var face));
        var scale = resolved.FontSize / face.Metrics.DesignEmHeight;
        return (-face.Metrics.Ascent * scale, (face.Metrics.Descent - face.Metrics.Ascent + face.Metrics.LineGap) * scale);
    }

    private static Window Show(Control content)
    {
        var window = new Window { Width = 600, Height = 300, Content = content };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static TextBlock Applied(SwiftFont font, string text = "Filesystem")
    {
        var block = new TextBlock { Text = text };
        font.Apply(block);
        return block;
    }

    [Theory]
    [Trait("Req", "PR-MCP-199")]
    [Trait("Decision", "ED-290")]
    [InlineData(SwiftFont.TextStyle.Body, SwiftFont.Weight.Regular, 18.0, 13.0)] // S 213.0 the About paragraph, .system(.body)
    [InlineData(SwiftFont.TextStyle.Body, SwiftFont.Weight.Medium, 18.0, 13.0)] // S 183.5 the Add server row, body medium
    [InlineData(SwiftFont.TextStyle.Body, SwiftFont.Weight.Semibold, 18.0, 13.0)] // S 183.5 the Connected rows, S 187.5 a wrapped catalog title
    [InlineData(SwiftFont.TextStyle.Callout, SwiftFont.Weight.Regular, 17.0, 12.0)] // S 183.5 the lead card, C 152.6 the popover note, C 9.0 the notice
    [InlineData(SwiftFont.TextStyle.Callout, SwiftFont.Weight.Medium, 17.0, 12.0)]
    [InlineData(SwiftFont.TextStyle.Subheadline, SwiftFont.Weight.Regular, 16.0, 12.0)] // S 183.5 the GitHub row's second line, S 212.0-213.0
    [InlineData(SwiftFont.TextStyle.Subheadline, SwiftFont.Weight.Semibold, 16.0, 12.0)] // C 152.6 the popover header
    public void An_explicit_text_style_draws_each_line_in_its_absolute_box_with_the_extra_split_above_and_below_and_glyphs_at_true_size(
        SwiftFont.TextStyle style, SwiftFont.Weight weight, double box, double macBaseline) => headless.OnUiThread(() =>
    {
        var font = SwiftFont.System(style, weight: weight);
        Assert.Equal(box, font.LineBox);
        var one = Applied(font);
        var two = Applied(font, "Filesystem\nGitHub");
        var window = Show(new StackPanel { Children = { one, two } });
        try
        {
            Assert.Equal(box, one.DesiredSize.Height, 3);
            Assert.Equal(2 * box, two.DesiredSize.Height, 3);
            Assert.Equal(style == SwiftFont.TextStyle.Body ? 13 : style == SwiftFont.TextStyle.Callout ? 12 : 11, one.FontSize); // ED-059: true size
            var (ascent, natural) = FaceBox(font);
            Assert.True(natural < box, $"the face's own box {natural} is not shorter than {box}");
            var line = one.TextLayout.TextLines[0];
            Assert.Equal(box, line.Height, 3);
            Assert.Equal(ascent + (box - natural) / 2, line.Baseline, 3); // half the extra above
            Assert.InRange(line.Baseline, macBaseline - 1, macBaseline + 1); // the Mac's baseline within a point
            Assert.All(two.TextLayout.TextLines, wrapped => { Assert.Equal(box, wrapped.Height, 3); Assert.Equal(line.Baseline, wrapped.Baseline, 3); }); // a pitch of one box
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-MCP-199")]
    [Trait("Decision", "ED-290")]
    public void A_containers_font_hands_its_box_to_every_text_below_it_and_a_nearer_font_or_a_later_change_replaces_it() => headless.OnUiThread(() =>
    {
        // TimelineRows.swift:2397: .font(.chat(.callout)) on the notice's HStack reaches its message; Avalonia's own
        // TextBlock.LineHeight is not inherited, so the box travels with the font.
        var inner = new TextBlock { Text = "callout" };
        var middle = new TextBlock { Text = "MorphingTextBlock" };
        var truncating = new MiddleTruncatingTextBlock { FullText = "MiddleTruncatingTextBlock" };
        var fixedSize = new TextBlock { Text = "fixed 13" };
        var fixedHost = new StackPanel { Children = { fixedSize } }.Font(SwiftFont.System(size: 13));
        var container = new StackPanel { Children = { inner, new MorphingTextBlock { Text = "morph" }, truncating, fixedHost } }.Font(SwiftFont.Callout);
        var window = Show(container);
        try
        {
            var morph = (MorphingTextBlock)container.Children[1];
            Assert.Equal(17, inner.DesiredSize.Height, 3);
            Assert.Equal(17, morph.DesiredSize.Height, 3);
            Assert.Equal(17, truncating.DesiredSize.Height, 3);
            Assert.Equal(FaceBox(SwiftFont.System(size: 13)).Natural, fixedSize.TextLayout.Height, 3); // the nearer fixed font wins

            // A text put under the container after the font was set takes the box too.
            container.Children.Add(middle);
            window.UpdateLayout();
            Assert.Equal(17, middle.DesiredSize.Height, 3);

            // The container's font changes: every text below follows.
            container.Font(SwiftFont.Subheadline);
            window.UpdateLayout();
            Assert.Equal(16, inner.DesiredSize.Height, 3);
            Assert.Equal(16, middle.DesiredSize.Height, 3);
            container.Font(SwiftFont.System(size: 12));
            window.UpdateLayout();
            Assert.Equal(FaceBox(SwiftFont.System(size: 12)).Natural, inner.TextLayout.Height, 3);

            // A text's own font beats the container's, and a LineHeight the control sets on itself beats both.
            container.Font(SwiftFont.Callout);
            SwiftFont.Body.Apply(inner);
            var own = new TextBlock { Text = "own", LineHeight = 16 };
            SwiftFont.Body.Apply(own);
            container.Children.Add(own);
            window.UpdateLayout();
            Assert.Equal(18, inner.DesiredSize.Height, 3);
            Assert.Equal(16, own.DesiredSize.Height, 3);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-MCP-199")]
    [Trait("Decision", "ED-290")]
    public void The_implicit_default_is_the_body_size_and_weight_in_the_faces_own_box_not_the_explicit_body_box() => headless.OnUiThread(() =>
    {
        // 2-chat-use.mov C 102.0: the sent bubble (TimelineRows.swift:122-128, no .font anywhere above it) is 34 = 9 + 16 + 9.
        Assert.Null(SwiftFont.Default.LineBox);
        Assert.Null(SwiftFont.Default.FontWeight(SwiftFont.Weight.Medium).MonospacedDigit().LineBox);
        Assert.Equal(SwiftFont.Body.Resolve().FontSize, SwiftFont.Default.Resolve().FontSize);
        Assert.Equal(SwiftFont.Body.Resolve().FontWeight, SwiftFont.Default.Resolve().FontWeight);
        Assert.Equal(SwiftFont.Body.Resolve().FontFamily, SwiftFont.Default.Resolve().FontFamily);
        Assert.Equal(13, SwiftFont.Default.PointSize);
        var plain = Applied(SwiftFont.Default);
        var underBody = new TextBlock { Text = "inherits" };
        var host = new StackPanel { Children = { underBody } }.Font(SwiftFont.Body);
        var underDefault = new TextBlock { Text = "inherits" };
        var defaultHost = new StackPanel { Children = { underDefault } }.Font(SwiftFont.Default);
        var window = Show(new StackPanel { Children = { plain, host, new Border { Child = defaultHost }.Font(SwiftFont.Callout) } });
        try
        {
            var natural = FaceBox(SwiftFont.Default).Natural;
            Assert.Equal(natural, plain.TextLayout.Height, 3);
            Assert.Equal(18, underBody.TextLayout.Height, 3);
            Assert.Equal(natural, underDefault.TextLayout.Height, 3); // the default cuts an outer style's box off
        }
        finally { window.Close(); }
    });

    [Theory]
    [Trait("Req", "PR-MCP-199")]
    [Trait("Decision", "ED-290")]
    [InlineData("system(size: 13)")] // S 168.0 "Signed in as OpenRouter", SettingsView.swift:656: box 16 on the Mac, SF's own
    [InlineData("system(size: 11)")] // S 168.0 "Version 1.18.31", SettingsView.swift:663: box 14, SF's own
    [InlineData("system(size: 10.5, design: .monospaced)")] // S 186.0 the catalog card's mono line: box 13, SF Mono's own
    [InlineData("NSFont.systemFont(ofSize: 13)")]
    [InlineData("system(.callout, design: .monospaced)")] // not measured: the face's own box
    [InlineData("headline")] // not measured (TODO)
    [InlineData("title2")] // not measured (TODO)
    [InlineData("footnote")] // not measured (TODO)
    [InlineData("caption")] // not measured (TODO)
    public void A_fixed_size_the_monospaced_design_and_the_unmeasured_styles_keep_the_faces_own_box(string swift) => headless.OnUiThread(() =>
    {
        var font = swift switch
        {
            "system(size: 13)" => SwiftFont.System(size: 13),
            "system(size: 11)" => SwiftFont.System(size: 11),
            "system(size: 10.5, design: .monospaced)" => SwiftFont.System(size: 10.5, design: SwiftFont.Design.Monospaced),
            "NSFont.systemFont(ofSize: 13)" => SwiftFont.SystemFont(13),
            "system(.callout, design: .monospaced)" => SwiftFont.System(SwiftFont.TextStyle.Callout, SwiftFont.Design.Monospaced),
            "headline" => SwiftFont.Headline,
            "title2" => SwiftFont.Title2,
            "footnote" => SwiftFont.Footnote,
            _ => SwiftFont.Caption,
        };
        Assert.Null(font.LineBox);
        var own = Applied(font);
        var inherited = new TextBlock { Text = "inherits" };
        var window = Show(new StackPanel { Children = { own, new StackPanel { Children = { inherited } }.Font(font) } }.Font(SwiftFont.Body));
        try
        {
            var natural = FaceBox(font).Natural;
            Assert.Equal(natural, own.TextLayout.Height, 3);
            Assert.Equal(natural, inherited.TextLayout.Height, 3);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-MCP-199")]
    [Trait("Decision", "ED-290")]
    public void A_text_input_and_its_prompt_keep_the_faces_own_box_under_a_text_style() => headless.OnUiThread(() =>
    {
        var field = PlainControls.TextField("Search servers", new Binding<string>(() => "", _ => { }));
        SwiftFont.Body.Apply(field);
        Assert.True(double.IsNaN(field.GetValue(SwiftFont.LineBoxProperty)));
        var bare = PlainControls.TextField("Name", new Binding<string>(() => "", _ => { }));
        var window = Show(new StackPanel { Children = { field, bare } }.Font(SwiftFont.Callout));
        try
        {
            foreach (var box in new[] { field, bare })
            {
                var prompt = box.GetVisualDescendants().OfType<TextBlock>().Single();
                Assert.True(double.IsNaN(prompt.LineHeight), "the prompt took a text style's line box");
            }
        }
        finally { window.Close(); }
    });
}
