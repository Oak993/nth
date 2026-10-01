using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Fixtures;

// Menu rigs of the TimelineRows "finish" tests: a real right click through the window's hit test, the recorded popup's
// content hosted in a window of its own so its rows take real clicks, and the item titles/symbols read back.
internal static class TimelineRowsFinRig
{
    // A secondary-button press and release at the control's corner, as RightClickCatcher's tunnel handler receives it.
    internal static (Popup Popup, RowActionItems Items) RightClick(RecordingPresenter presenter, AvaloniaWindow window, Control on)
    {
        var point = on.TranslatePoint(new Point(4, 4), window) ?? throw new Xunit.Sdk.XunitException("not in the window");
        window.MouseDown(point, MouseButton.Right, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Right, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var popup = presenter.Calls.Last(call => call.Presented).Popup;
        Assert.NotNull(popup.Child);
        return (popup, Assert.Single(LogicalQuery.SelfAndDescendants<RowActionItems>(popup.Child)));
    }

    // The recorded popup never opens headless; its content is hosted here so the menu's buttons react to real clicks.
    internal static AvaloniaWindow HostMenu(Popup popup)
    {
        var content = popup.Child!;
        popup.Child = null;
        var window = new AvaloniaWindow { Content = content, Width = 400, Height = 400 };
        window.Show();
        for (var pass = 0; pass < 4; pass++) { window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
        return window;
    }

    internal static void ClickButton(AvaloniaWindow window, Button button)
    {
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window) ?? throw new Xunit.Sdk.XunitException("not in the window");
        window.MouseMove(point, RawInputModifiers.None);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    // A PopoverItem's title is a MiddleTruncatingText, whose Text is set only when measured; FullText is the title.
    internal static string Title(PopoverItem item) => LogicalQuery.SelfAndDescendants<MiddleTruncatingText>(item).First().FullText;

    internal static string[] ItemTitles(RowActionItems items) => LogicalQuery.SelfAndDescendants<PopoverItem>(items).Select(Title).ToArray();

    internal static string[] ItemSymbols(RowActionItems items) =>
        LogicalQuery.SelfAndDescendants<PopoverItem>(items).Select(item => LogicalQuery.SelfAndDescendants<SwiftImage>(item).First().SymbolName ?? "").ToArray();
}
