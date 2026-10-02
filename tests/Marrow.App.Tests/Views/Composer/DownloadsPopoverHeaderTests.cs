using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Views.Composer;

// The Downloads popover's "Recent downloads" section header: DroppyCode/App/Views/Composer/DownloadsPopover.swift (B3 52bc792)
// :108 (the record cites :114, the B2 line) through PopoverSectionHeader, UI/Chrome/Popovers.swift:124-132
// (.font(.system(.subheadline, weight: .semibold)), Chrome.secondaryText, padding 8/6/8/3, leading aligned).
// The folder is pointed at nothing (RecentDownloads.DirectoryProvider), so the real Downloads folder is never read.
[Collection("Headless")]
public sealed class DownloadsPopoverHeaderTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "UI-COMPOSER-024")] // DownloadsPopover.swift:108; Popovers.swift:124-132
    public void The_Recent_downloads_header_is_the_popovers_first_row_in_subheadline_semibold_secondary_text() => headless.OnUiThread(() =>
    {
        using var directed = RecentDownloads.DirectoryProvider.Override(() => null);
        var popover = new DownloadsPopover(_ => { }, () => { });
        var window = new Avalonia.Controls.Window { Width = 500, Height = 800, Content = popover };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var header = Assert.Single(LogicalQuery.SelfAndDescendants<PopoverSectionHeader>(popover)); // the only section header
        var text = Assert.IsType<TextBlock>(header.Child);
        Assert.Equal("Recent downloads", text.Text);
        Assert.Equal(11d, text.FontSize); // .subheadline is 11 point at the macOS default
        Assert.Equal(FontWeight.SemiBold, text.FontWeight);
        Assert.Equal(Chrome.SecondaryText.Resolve(text.ActualThemeVariant), ((ISolidColorBrush)text.Foreground!).Color);
        Assert.Equal(new Thickness(8, 6, 8, 3), text.Margin);
        Assert.Equal(HorizontalAlignment.Left, text.HorizontalAlignment);

        // It is the first thing in the popover, above the list and the divider.
        var content = LogicalQuery.SelfAndDescendants<StackPanel>(popover).Single(stack => stack.Children.OfType<PopoverSectionHeader>().Any());
        Assert.Same(header, content.Children[0]);
        window.Close();
    });
}
