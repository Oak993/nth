using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Port.MultiAccount.Shared;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.Testing.MultiAccountSettings;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Tests.Views.MultiAccount.Shared;

// MA-10 (step 3b, job 0; checklist 4 and 9, OD-0115): the "No 5-hour limit" cell, dim, there only when the service reported that the account has no 5-hour window.
[Collection("Headless")]
public sealed class NoFiveHourCellTests(HeadlessSession headless)
{
    private void Run(AccountUsageLine? usage, Action<MultiAccountHarness, NoFiveHourCell> check)
    {
        NoFiveHourCell? cell = null;
        MultiAccountHarness.Run(headless, new AccountsRig(), () => cell = new NoFiveHourCell(usage) { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top }, h => check(h, cell!));
    }

    [Fact]
    [Trait("Feature", "MA-10")]
    [Trait("Rule", "R-TEST-4")]
    public void A_usage_line_that_flags_it_shows_the_pages_words_dim_and_on_one_line() => Run(new AccountUsageLine(null, null, 0.2, null, true, false), (h, cell) =>
    {
        Assert.True(cell.IsVisible);
        Assert.Equal(["No 5-hour limit"], h.Words(cell));
        Assert.Equal(MultiAccountWords.NoFiveHour, cell.TextShown);
        var text = Assert.Single(cell.GetVisualDescendants().OfType<TextBlock>());
        Assert.Equal(10.5, text.FontSize);
        Assert.Equal(TextWrapping.NoWrap, text.TextWrapping);
        Assert.Equal(TextTrimming.CharacterEllipsis, text.TextTrimming);
        Assert.Equal(ChromeTokens.SecondaryText.Opacity(0.75).Resolve(text.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color); // dimmer than the secondary text
        Assert.Equal(MultiAccountWords.NoFiveHour, AutomationProperties.GetName(cell));
    });

    [Fact]
    [Trait("Feature", "MA-10")]
    [Trait("Rule", "R-TEST-4")]
    public void No_usage_line_or_one_without_the_flag_shows_nothing() => Run(null, (h, cell) =>
    {
        Assert.False(cell.IsVisible);
        Assert.Empty(h.Words(cell));
        cell.Apply(new AccountUsageLine(0.4, null, 0.2, null, false, false));
        h.Settle();
        Assert.False(cell.IsVisible);
        cell.Apply(new AccountUsageLine(null, null, null, null, false, true));
        h.Settle();
        Assert.False(cell.IsVisible);
    });

    [Fact]
    [Trait("Feature", "MA-10")]
    [Trait("Rule", "R-TEST-4")]
    public void A_later_report_turns_the_cell_on_and_off_in_place() => Run(null, (h, cell) =>
    {
        cell.Apply(new AccountUsageLine(null, null, 0.2, null, true, false));
        h.Settle();
        Assert.True(cell.IsVisible);
        Assert.Equal(["No 5-hour limit"], h.Words(cell));
        cell.Apply(new AccountUsageLine(0.5, null, 0.2, null, false, false));
        h.Settle();
        Assert.False(cell.IsVisible);
    });
}
