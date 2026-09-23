using Marrow.UI.Port;

namespace Marrow.UI.Tests.Port;

// ED-092(3): pure value coverage only. Render(TextBlock), including numeric baseline offset,
// is still unimplemented because Avalonia 12.1.2 Run has no numeric baseline extension point.
public sealed class StyledTextTests
{
    [Theory]
    [Trait("Decision", "ED-092")]
    [InlineData("")] // App/Views/Chat/ThreadTimeline.swift:2141
    [InlineData("Merge")] // UI/Markdown/LinkPopover.swift:80
    public void Plain_text_keeps_the_exact_string_and_has_no_explicit_style(string text)
    {
        var run = Assert.Single(new StyledText(text).Runs);
        Assert.Equal(text, run.Text);
        Assert.Null(run.Font);
        Assert.Null(run.Color);
        Assert.Null(run.Weight);
        Assert.Null(run.IsItalic);
        Assert.Null(run.IsUnderlined);
        Assert.Null(run.IsStrikethrough);
        Assert.Null(run.HasMonospacedDigits);
        Assert.Null(run.BaselineOffset);
    }

    [Fact]
    [Trait("Decision", "ED-092")]
    public void Concatenation_preserves_order_and_the_style_of_each_operand()
    {
        // UI/Common/HydraGlyph.swift:226-230; App/Views/Chat/TimelineRows.swift:454-455.
        var color = SwiftColor.Primary.Opacity(0.75);
        var name = new StyledText("Otto 2").ForegroundColor(color);
        var suffix = new StyledText(" failed");
        var joined = name + suffix;
        Assert.Equal(2, joined.Runs.Count);
        Assert.Equal("Otto 2", joined.Runs[0].Text);
        Assert.Same(color, joined.Runs[0].Color);
        Assert.Equal(" failed", joined.Runs[1].Text);
        Assert.Null(joined.Runs[1].Color);
        Assert.Single(name.Runs);
        Assert.Single(suffix.Runs);
    }

    [Fact]
    [Trait("Decision", "ED-092")]
    public void Each_modifier_applies_to_every_run_without_changing_the_receiver()
    {
        // ED-092(3) specifies every trait, including underline/strike/italic not used as Text modifiers at B2.
        // TimelineRows.swift:454-455 supplies runs and font; MarkdownView.swift:386,490-493 supplies -3.2.
        var original = new StyledText("Otto 2") + new StyledText(" failed");
        var font = SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Medium);
        var color = SwiftColor.Primary;
        var styled = original.Font(font).ForegroundColor(color).FontWeight(SwiftFont.Weight.Bold)
            .Italic().Underline().Strikethrough().MonospacedDigit().BaselineOffset(-3.2);
        Assert.Equal(2, styled.Runs.Count);
        Assert.Equal(new[] { "Otto 2", " failed" }, styled.Runs.Select(run => run.Text));
        Assert.All(styled.Runs, run =>
        {
            Assert.Same(font, run.Font);
            Assert.Same(color, run.Color);
            Assert.Equal(SwiftFont.Weight.Bold, run.Weight);
            Assert.True(run.IsItalic);
            Assert.True(run.IsUnderlined);
            Assert.True(run.IsStrikethrough);
            Assert.True(run.HasMonospacedDigits);
            Assert.Equal(-3.2, run.BaselineOffset);
        });
        Assert.All(original.Runs, run =>
        {
            Assert.Null(run.Font);
            Assert.Null(run.Color);
            Assert.Null(run.Weight);
            Assert.Null(run.IsItalic);
            Assert.Null(run.IsUnderlined);
            Assert.Null(run.IsStrikethrough);
            Assert.Null(run.HasMonospacedDigits);
            Assert.Null(run.BaselineOffset);
        });
    }

    [Fact]
    [Trait("Decision", "ED-092")]
    public void Applying_a_trait_does_not_erase_the_other_run_styles()
    {
        // UI/Markdown/LinkPopover.swift:80: styled Merge followed by another Text.
        var font = SwiftFont.Callout;
        var color = SwiftColor.Primary;
        var first = new StyledText("Merge").Font(font).ForegroundColor(color)
            .FontWeight(SwiftFont.Weight.Semibold).Italic().Underline().Strikethrough()
            .MonospacedDigit().BaselineOffset(-3.2);
        var joined = (first + new StyledText("")).FontWeight(SwiftFont.Weight.Bold);
        var run = joined.Runs[0];
        Assert.Equal("Merge", run.Text);
        Assert.Same(font, run.Font);
        Assert.Same(color, run.Color);
        Assert.Equal(SwiftFont.Weight.Bold, run.Weight);
        Assert.True(run.IsItalic);
        Assert.True(run.IsUnderlined);
        Assert.True(run.IsStrikethrough);
        Assert.True(run.HasMonospacedDigits);
        Assert.Equal(-3.2, run.BaselineOffset);
        Assert.Equal(SwiftFont.Weight.Semibold, first.Runs[0].Weight);
        Assert.Null(joined.Runs[1].Font);
        Assert.Null(joined.Runs[1].Color);
    }

    [Fact]
    [Trait("Decision", "ED-092")]
    public void Concatenation_keeps_empty_runs_and_does_not_coalesce_equal_styles()
    {
        // ED-092 packet: immutable list; + concatenates runs; one rendered Run per stored run.
        // Strings: TimelineRows.swift:454; ThreadTimeline.swift:2141.
        var text = new StyledText(" failed");
        var joined = new StyledText("") + text + text;
        Assert.Equal(new[] { "", " failed", " failed" }, joined.Runs.Select(run => run.Text));
        var changed = joined.Italic();
        Assert.All(joined.Runs, run => Assert.Null(run.IsItalic));
        Assert.All(changed.Runs, run => Assert.True(run.IsItalic));
    }

    [Fact]
    [Trait("Decision", "ED-092")]
    public void Exposed_runs_cannot_be_mutated_through_a_collection_cast()
    {
        var text = new StyledText("Merge"); // UI/Markdown/LinkPopover.swift:80
        var list = Assert.IsAssignableFrom<IList<StyledText.Run>>(text.Runs);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list.Clear());
        Assert.Equal("Merge", Assert.Single(text.Runs).Text);
    }
}
