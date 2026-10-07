using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Port.MultiAccount.Shared;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.MultiAccount.Shared;
using Marrow.Testing.MultiAccountSettings;
using Marrow.UI.Port;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Tests.Views.MultiAccount.Shared;

// MA-10 (step 3b, job 0; checklist 4 and 9): the shared config line of an account, "<model> · <effort>", with the yellow Fast bolt the composer's model chip draws (EffortSlider.cs:180-183) and never the word.
[Collection("Headless")]
public sealed class AccountConfigLineTests(HeadlessSession headless)
{
    private void Run(AccountConfigText start, Action<MultiAccountHarness, AccountConfigLine> check)
    {
        AccountConfigLine? line = null;
        MultiAccountHarness.Run(headless, new AccountsRig(), () => line = new AccountConfigLine(start) { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top }, h => check(h, line!));
    }

    [Fact]
    [Trait("Feature", "MA-10")]
    [Trait("Rule", "R-TEST-4")]
    public void The_line_shows_the_model_and_the_effort_and_no_bolt_when_fast_is_off() => Run(AccountConfigText.Of("Opus 5.5", "high", false), (h, line) =>
    {
        Assert.Equal("Opus 5.5 · High", line.TextShown);
        Assert.False(line.BoltShown);
        Assert.DoesNotContain(line.GetVisualDescendants().OfType<SwiftImage>(), i => i.IsEffectivelyVisible);
        Assert.Equal(["Opus 5.5 · High"], h.Words(line));
        Assert.Equal("Opus 5.5 · High", AutomationProperties.GetName(line));
    });

    [Fact]
    [Trait("Feature", "MA-10")]
    [Trait("Rule", "R-TEST-4")]
    public void Fast_is_the_yellow_bolt_before_the_words_and_the_word_fast_is_never_shown() => Run(AccountConfigText.Of("Opus 5.5", "high", true), (h, line) =>
    {
        Assert.True(line.BoltShown);
        var bolt = Assert.Single(line.GetVisualDescendants().OfType<SwiftImage>());
        Assert.Equal("bolt.fill", bolt.SymbolName);
        Assert.Equal(ChromeTokens.InlineIconFont, SwiftImage.GetFont(bolt)); // the model chip's bolt (EffortSlider.cs:79)
        Assert.Equal(SwiftColor.Yellow.Resolve(bolt.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(bolt.Foreground).Color); // :80 .yellow
        var text = Assert.Single(line.GetVisualDescendants().OfType<TextBlock>());
        Assert.True(bolt.TranslatePoint(default, line)!.Value.X + bolt.Bounds.Width <= text.TranslatePoint(default, line)!.Value.X + 0.5); // the page puts the bolt first
        Assert.Equal("Opus 5.5 · High", line.TextShown);
        Assert.DoesNotContain(h.Words(line), w => w.Contains("Fast", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("Fast", AutomationProperties.GetName(line) ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Fast mode", AutomationProperties.GetName(bolt)); // the page's own label for the glyph, spoken only
    });

    [Fact]
    [Trait("Feature", "MA-10")]
    [Trait("Rule", "R-TEST-4")]
    public void Applying_a_new_line_changes_the_words_and_the_bolt_in_place() => Run(AccountConfigText.Of("Opus 5.5", "high", false), (h, line) =>
    {
        line.Apply(AccountConfigText.Of("Sonnet 5.5", null, true));
        h.Settle();
        Assert.Equal("Sonnet 5.5", line.TextShown);
        Assert.True(line.BoltShown);
        line.Apply(AccountConfigText.Of("Sonnet 5.5", null, false));
        h.Settle();
        Assert.False(line.BoltShown);
        Assert.Equal("Sonnet 5.5", AutomationProperties.GetName(line));
    });

    [Fact]
    [Trait("Feature", "MA-10")]
    [Trait("Rule", "R-TEST-4")]
    public void The_text_is_the_pages_small_secondary_size_and_gives_way_to_an_ellipsis_when_the_row_is_narrow() => Run(AccountConfigText.Of("A very long model name indeed", "xhigh", false), (h, line) =>
    {
        var text = Assert.Single(line.GetVisualDescendants().OfType<TextBlock>());
        Assert.Equal(11, text.FontSize);
        Assert.Equal(TextTrimming.CharacterEllipsis, text.TextTrimming);
        Assert.Equal(TextWrapping.NoWrap, text.TextWrapping);
    });
}
