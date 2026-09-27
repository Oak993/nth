using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Tests.Fixtures;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Port;

// ED-214 measure contract; pinned minimumScaleFactor sites are cited on each test.
[Collection("Headless")]
public sealed class TextScalingTests(HeadlessSession headless)
{
    private static Type Primitive => Assert.IsAssignableFrom<Type>(typeof(SwiftFont).Assembly.GetType("Marrow.UI.Port.TextScaling"));
    private static Control Apply(Control view, double factor) =>
        Assert.IsAssignableFrom<Control>(Primitive.GetMethod("Apply", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [view, factor]));

    private static TextBlock Text(string value, double size = 24) => new()
    {
        Text = value, FontSize = size, FontFamily = new FontFamily("fonts:Inter#Inter"),
        TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis,
        MaxLines = 1, UseLayoutRounding = false,
    };

    [Fact]
    [Trait("Req", "PR-COMPOSER-114")] // App/Views/Composer/EffortSlider.swift:614
    [Trait("Decision", "ED-214")]
    public void A_text_that_fits_keeps_its_font_and_height() => headless.OnUiThread(() =>
    {
        var text = Text("A short subtitle");
        text.Measure(Size.Infinity);
        var natural = text.DesiredSize;
        var modified = Apply(text, 0.85);
        modified.Measure(new Size(natural.Width + 20, double.PositiveInfinity));
        Assert.Equal(24, text.FontSize);
        Assert.Equal(natural.Height, modified.DesiredSize.Height);
        Assert.DoesNotContain(text.TextLayout.TextLines, line => line.HasCollapsed);
        Assert.Null(text.RenderTransform);
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-114")] // App/Views/Composer/EffortSlider.swift:614
    [Trait("Decision", "ED-214")]
    public void Slight_overflow_scales_to_the_exact_fitting_font_size_and_measured_height() => headless.OnUiThread(() =>
    {
        var text = Text("A subtitle just wider than its slot");
        text.Measure(Size.Infinity);
        var natural = text.DesiredSize;
        var modified = Apply(text, 0.85);
        modified.Measure(new Size(natural.Width * 0.95, double.PositiveInfinity));
        Assert.Equal(24 * 0.95, text.FontSize, 7); // ED-214: available / natural, above the .85 floor.
        Assert.True(modified.DesiredSize.Height < natural.Height);
        Assert.DoesNotContain(text.TextLayout.TextLines, line => line.HasCollapsed);
        Assert.Null(text.RenderTransform);
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-116")] // App/Views/Composer/EffortSlider.swift:648
    [Trait("Decision", "ED-214")]
    public void Large_overflow_reaches_the_minimum_font_then_uses_the_texts_own_ellipsis() => headless.OnUiThread(() =>
    {
        var text = Text("The lead's effort · heads run at high effort");
        text.Measure(Size.Infinity);
        var natural = text.DesiredSize.Width;
        var modified = Apply(text, 0.9);
        modified.Measure(new Size(natural * 0.2, double.PositiveInfinity));
        Assert.Equal(24 * 0.9, text.FontSize, 7);
        Assert.Equal(TextTrimming.CharacterEllipsis, text.TextTrimming);
        Assert.Contains(text.TextLayout.TextLines, line => line.HasCollapsed);
        Assert.Equal("The lead's effort · heads run at high effort", text.Text);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-139")] // App/Views/Chat/NewThreadPrompt.swift:46
    [Trait("Decision", "ED-214")]
    public void Infinite_or_wider_proposals_restore_the_original_size_instead_of_compounding_the_scale() => headless.OnUiThread(() =>
    {
        var text = Text("What should we build?");
        text.Measure(Size.Infinity);
        var natural = text.DesiredSize.Width;
        var modified = Apply(text, 0.6);
        modified.Measure(new Size(natural * 0.2, double.PositiveInfinity));
        Assert.Equal(24 * 0.6, text.FontSize, 7);
        modified.Measure(Size.Infinity);
        Assert.Equal(24, text.FontSize);
        modified.Measure(new Size(natural * 0.95, double.PositiveInfinity));
        Assert.Equal(24 * 0.95, text.FontSize, 7);
        modified.Measure(new Size(natural + 100, double.PositiveInfinity));
        Assert.Equal(24, text.FontSize);
    });

    [Fact]
    [Trait("Req", "PR-UPDATE-078")] // App/Views/Settings/AboutUpdateViews.swift:218-227
    [Trait("Decision", "ED-214")]
    public void A_container_applies_the_factor_to_each_text_and_each_scales_independently() => headless.OnUiThread(() =>
    {
        var title = Text("New", 12.5);
        var count = Text("A count description much wider than the title", 11);
        var stack = new StackPanel { Orientation = Orientation.Vertical, Children = { title, count } };
        var modified = Apply(stack, 0.8);
        modified.Measure(new Size(45, double.PositiveInfinity));
        Assert.Equal(12.5, title.FontSize);
        Assert.Equal(11 * 0.8, count.FontSize, 7);
        Assert.DoesNotContain(title.TextLayout.TextLines, line => line.HasCollapsed);
        Assert.Contains(count.TextLayout.TextLines, line => line.HasCollapsed);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-139")] // App/Views/Chat/NewThreadPrompt.swift:22-46
    [Trait("Decision", "ED-214")]
    public void A_horizontal_container_passes_finite_slots_to_its_texts() => headless.OnUiThread(() =>
    {
        var prefix = Text("What should we build in ", 28);
        var name = Text("A very long project name with a long suffix", 28);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { prefix, name } };
        var modified = Apply(row, 0.6);
        modified.Measure(new Size(160, double.PositiveInfinity));
        Assert.Equal(28 * 0.6, prefix.FontSize, 7);
        Assert.Equal(28 * 0.6, name.FontSize, 7);
        Assert.Contains(prefix.TextLayout.TextLines, line => line.HasCollapsed);
        Assert.Contains(name.TextLayout.TextLines, line => line.HasCollapsed);
        Assert.True(modified.DesiredSize.Width <= 160);
    });
}
