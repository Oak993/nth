using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Chrome;

// Audit fixes for UI/Chrome/Popovers.swift (B3 52bc792): PopoverItem's leading slot :282-305, Spacer(minLength: 16)
// :312, middle truncation of the title :307-311. Expected values are read off the Swift, not the C#.
[Collection("Headless")]
public sealed class PopoversAuditTests(HeadlessSession headless)
{
    private static Window Show(Control content, double width = 400, double height = 300)
    {
        content.HorizontalAlignment = HorizontalAlignment.Left;
        content.VerticalAlignment = VerticalAlignment.Top;
        var window = new Window { Width = width, Height = height, Content = content, RequestedThemeVariant = ThemeVariant.Light };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static StackPanel RowOf(Control item) =>
        Assert.Single(VisualQuery.Descendants<StackPanel>(item), p => p.Orientation == Orientation.Horizontal);

    [Fact]
    [Trait("Req", "PR-SIDEBAR-106")] // UI/Chrome/Popovers.swift:275-312: no leading view, no slot, the title follows the row padding directly
    public void PopoverItem_without_an_icon_puts_the_title_at_the_row_start() => headless.OnUiThread(() =>
    {
        var item = new PopoverItem("Rename", () => { });
        var window = Show(item);
        var row = RowOf(item);
        var title = Assert.Single(VisualQuery.Descendants<MiddleTruncatingText>(item));
        Assert.Equal(0, title.TranslatePoint(new Point(0, 0), row)!.Value.X);
        Assert.DoesNotContain(VisualQuery.Descendants<Panel>(item), p => p.Width == 16 && p.Height == 16);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-106")] // UI/Chrome/Popovers.swift:301-305,275: a symbol row keeps the 16pt column plus the 8pt spacing before the title
    public void PopoverItem_with_a_symbol_puts_the_title_after_the_16_point_column_and_spacing() => headless.OnUiThread(() =>
    {
        var item = new PopoverItem("Rename", () => { }, symbol: "pencil");
        var window = Show(item);
        var title = Assert.Single(VisualQuery.Descendants<MiddleTruncatingText>(item));
        Assert.Equal(24, title.TranslatePoint(new Point(0, 0), RowOf(item))!.Value.X);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-106")] // UI/Chrome/Popovers.swift:295-300: a confirmed row without an icon gets the checkmark column once confirmed
    public void PopoverItem_without_an_icon_gains_the_checkmark_column_when_confirmed() => headless.OnUiThread(() =>
    {
        var item = new PopoverItem("Copy", () => { }, confirmation: "Copied");
        var window = Show(item);
        var title = Assert.Single(VisualQuery.Descendants<MiddleTruncatingText>(item));
        Assert.Equal(0, title.TranslatePoint(new Point(0, 0), RowOf(item))!.Value.X);
        var button = Assert.Single(VisualQuery.Descendants<Button>(item));
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        Assert.True(item.IsConfirmed);
        Assert.Equal(24, title.TranslatePoint(new Point(0, 0), RowOf(item))!.Value.X);
        window.Close();
    });
}
