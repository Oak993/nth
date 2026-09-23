using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// ED-092(3), ED-158: SwiftUI Text values render one inline per run, including image runs.
// UNVERIFIED: image baseline expectations rest on open oracle O-49; inner modifier precedence
// rests on open oracle O-50. These are parked assumptions, not established macOS requirements.
[Collection("Headless")]
public sealed class StyledTextTests(HeadlessSession headless)
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
        Assert.Null(run.Image);
        Assert.False(run.IsTemplateImage);
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

    [Fact]
    [Trait("Decision", "ED-158")]
    [Trait("Oracle", "O-50")]
    public void An_inner_trait_wins_over_the_same_trait_applied_outside()
    {
        var c1 = SwiftColor.Primary;
        var c2 = SwiftColor.Secondary;
        var f1 = SwiftFont.Callout;
        var f2 = SwiftFont.Body;
        var text = (new StyledText("Merge").FontWeight(SwiftFont.Weight.Semibold).ForegroundColor(c1)
            .Font(f1).BaselineOffset(-3.2) + new StyledText(" x"))
            .FontWeight(SwiftFont.Weight.Bold).ForegroundColor(c2).Font(f2).BaselineOffset(2);
        Assert.Equal(SwiftFont.Weight.Semibold, text.Runs[0].Weight);
        Assert.Same(c1, text.Runs[0].Color);
        Assert.Same(f1, text.Runs[0].Font);
        Assert.Equal(-3.2, text.Runs[0].BaselineOffset);
        Assert.Equal(SwiftFont.Weight.Bold, text.Runs[1].Weight);
        Assert.Same(c2, text.Runs[1].Color);
        Assert.Same(f2, text.Runs[1].Font);
        Assert.Equal(2, text.Runs[1].BaselineOffset);
    }

    [Fact]
    [Trait("Decision", "ED-158")]
    public void Image_constructor_keeps_an_empty_image_run() => headless.OnUiThread(() =>
    {
        using var bitmap = Bitmap();
        var run = Assert.Single(new StyledText(bitmap, isTemplate: true).Runs);
        Assert.Equal("", run.Text);
        Assert.Same(bitmap, run.Image);
        Assert.True(run.IsTemplateImage);
    });

    [Fact]
    [Trait("Decision", "ED-158")]
    public void Rendering_keeps_three_runs_and_resolves_weight_and_font() => headless.OnUiThread(() =>
    {
        // UI/Markdown/LinkPopover.swift:80.
        var text = new StyledText("Merge").FontWeight(SwiftFont.Weight.Semibold)
            + new StyledText(" ") + new StyledText("request");
        var host = new TextBlock();
        text.Render(host);
        Assert.Equal(3, host.Inlines!.Count);
        Assert.Equal(600, (int)Assert.IsType<Run>(host.Inlines[0]).FontWeight);
        Assert.Equal(" ", Assert.IsType<Run>(host.Inlines[1]).Text);
    });

    [Fact]
    [Trait("Decision", "ED-158")]
    public void Rendering_applies_tabular_digits_and_text_decorations() => headless.OnUiThread(() =>
    {
        foreach (var font in new[] { SwiftFont.System(12, weight: SwiftFont.Weight.Medium),
                                     SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Medium) })
        {
            var host = new TextBlock();
            new StyledText("123").Font(font).MonospacedDigit().Underline().Strikethrough().Render(host);
            var run = Assert.IsType<Run>(Assert.Single(host.Inlines!));
            Assert.Equal(500, (int)run.FontWeight);
            Assert.Equal(12, run.FontSize);
            Assert.Equal("Inter", run.FontFamily.Name);
            Assert.Contains(run.FontFeatures!, feature => feature.Tag == "tnum" && feature.Value == 1);
            Assert.Equal(2, run.TextDecorations!.Count);
        }
    });

    [Fact]
    [Trait("Decision", "ED-158")]
    public void Rendering_reuses_a_brush_on_one_host_and_re_resolves_on_theme_change() => headless.OnUiThread(() =>
    {
        var host = new TextBlock();
        var scope = new ThemeVariantScope { RequestedThemeVariant = ThemeVariant.Light, Child = host };
        var window = new Window { Content = scope };
        window.Show();
        var text = new StyledText("a").ForegroundColor(SwiftColor.LabelColor);
        text.Render(host);
        var brush = Assert.IsAssignableFrom<ISolidColorBrush>(Assert.IsType<Run>(Assert.Single(host.Inlines!)).Foreground);
        text.Render(host);
        Assert.Same(brush, Assert.IsType<Run>(Assert.Single(host.Inlines!)).Foreground);
        var light = brush.Color;
        scope.RequestedThemeVariant = ThemeVariant.Dark;
        Assert.NotEqual(light, brush.Color);
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-158")]
    public void AppendTo_keeps_existing_inlines_and_render_replaces_them() => headless.OnUiThread(() =>
    {
        var host = new TextBlock { Inlines = new InlineCollection { new Run("before") } };
        (new StyledText("") + new StyledText("after")).AppendTo(host.Inlines!, host);
        Assert.Equal(new[] { "before", "", "after" }, host.Inlines!.Cast<Run>().Select(r => r.Text));
        new StyledText("again").Render(host);
        Assert.Equal("again", Assert.IsType<Run>(Assert.Single(host.Inlines)).Text);
    });

    [Fact]
    [Trait("Decision", "ED-158")]
    [Trait("Oracle", "O-49")]
    public void Image_baseline_is_always_local_and_uses_height_plus_offset() => headless.OnUiThread(() =>
    {
        using var bitmap = Bitmap();
        var host = new TextBlock();
        TextBlock.SetBaselineOffset(host, 5);
        foreach (var (offset, expected) in new (double?, double)[] { (-3.2, 12.8), (null, 16), (-16, 1e-6) })
        {
            var text = new StyledText(bitmap, isTemplate: false);
            if (offset is { } value) text = text.BaselineOffset(value);
            text.Render(host);
            var child = Assert.IsType<Image>(Assert.IsType<InlineUIContainer>(Assert.Single(host.Inlines!)).Child);
            Assert.Equal(expected, TextBlock.GetBaselineOffset(child), 6);
            Assert.True(child.IsSet(TextBlock.BaselineOffsetProperty));
        }
    });

    [Fact]
    [Trait("Decision", "ED-158")]
    [Trait("Oracle", "O-49")]
    public void Mention_image_bottom_is_below_the_first_line_baseline() => headless.OnUiThread(() =>
    {
        using var bitmap = Bitmap();
        var host = new TextBlock { UseLayoutRounding = false };
        (new StyledText("name ") + new StyledText(bitmap, isTemplate: false).BaselineOffset(-3.2)).Render(host);
        host.Measure(new Size(300, 100));
        host.Arrange(new Rect(host.DesiredSize));
        var image = Assert.IsType<Image>(Assert.IsType<InlineUIContainer>(host.Inlines![1]).Child);
        var baseline = host.TextLayout.TextLines[0].Baseline;
        Assert.InRange(image.Bounds.Bottom - baseline, 3.19, 3.21);
    });

    [Fact]
    [Trait("Decision", "ED-158")]
    public void Template_and_plain_images_use_the_expected_controls() => headless.OnUiThread(() =>
    {
        using var bitmap = Bitmap();
        var host = new TextBlock { Foreground = Brushes.Red };
        new StyledText(bitmap, isTemplate: true).Render(host);
        var border = Assert.IsType<Border>(Assert.IsType<InlineUIContainer>(Assert.Single(host.Inlines!)).Child);
        Assert.Equal(16, border.Width);
        Assert.Equal(16, border.Height);
        Assert.IsType<ImageBrush>(border.OpacityMask);
        Assert.Equal(Brushes.Red, border.Background);
        host.Foreground = Brushes.Blue;
        Assert.Equal(Brushes.Blue, border.Background);
        new StyledText(bitmap, isTemplate: false).Render(host);
        Assert.IsType<Image>(Assert.IsType<InlineUIContainer>(Assert.Single(host.Inlines!)).Child);

        var drawing = new DrawingImage(new GeometryDrawing
        {
            Brush = Brushes.Red,
            Geometry = new RectangleGeometry(new Rect(0, 0, 16, 16)),
        });
        new StyledText(drawing, isTemplate: true).Render(host);
        var drawn = Assert.IsType<Border>(Assert.IsType<InlineUIContainer>(Assert.Single(host.Inlines!)).Child);
        Assert.IsType<DrawingBrush>(drawn.OpacityMask);
    });

    [Fact]
    [Trait("Decision", "ED-158")]
    public void Numeric_offset_on_text_throws_but_zero_renders() => headless.OnUiThread(() =>
    {
        var host = new TextBlock();
        Assert.Throws<NotSupportedException>(() => new StyledText("x").BaselineOffset(-3.2).Render(host));
        new StyledText("x").BaselineOffset(0).Render(host);
        Assert.IsType<Run>(Assert.Single(host.Inlines!));
    });

    private static WriteableBitmap Bitmap() => new(new PixelSize(16, 16), new Vector(96, 96), PixelFormat.Bgra8888);
}
