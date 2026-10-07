// PORT-ONLY: MA-3b-2, locked page AccountPanel :2674-2797; content of the existing PopoverHost glass.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Views.Settings;
using Marrow.Core.Port.MultiAccount.Popover;
using Marrow.Core.Port.MultiAccount.Thread;
using Marrow.UI.Port;

namespace Marrow.App.Port.MultiAccount.Popover;

internal sealed class AccountPopover : Decorator
{
    private readonly AccountPopoverModel model;
    private readonly CrossfadeSlot content = new();
    internal AccountPopover(AccountPopoverModel model)
    {
        this.model = model; Width = 340; Child = content; Refresh();
        AttachedToVisualTree += (_, _) => { model.Changed += OnChanged; Refresh(); };
        DetachedFromVisualTree += (_, _) => model.Changed -= OnChanged;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { CloseRequested?.Invoke(); e.Handled = true; } };
    }
    internal event Action? CloseRequested;
    internal event Action? CreatePillRequested;
    internal IReadOnlyList<AccountPopoverRowControl> Rows { get; private set; } = [];
    private void OnChanged()
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) Refresh(); else Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);
    }
    private void Refresh()
    {
        var panel = new StackPanel { Spacing = 2, Margin = new Thickness(8) };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(8, 6, 8, 4) };
        header.Children.Add(LibraryUi.Text(model.Header, 12, secondary: true, weight: SwiftFont.Weight.Semibold));
        var hint = LibraryUi.Text(model.OrderHint, 11, secondary: true); Grid.SetColumn(hint, 1); header.Children.Add(hint); panel.Children.Add(header);
        var pin = model.PinnedText;
        if (pin is null) panel.Children.Add(LibraryUi.Text(model.Subtitle, 12, secondary: true));
        else panel.Children.Add(Line(pin.Replace(" · Back to order", "", StringComparison.Ordinal), "Back to order", "popover-back-to-order", model.BackToOrder));
        var list = new StackPanel { Spacing = 2 }; Rows = model.Rows.Select(row => new AccountPopoverRowControl(row, Pin)).ToArray();
        foreach (var row in Rows) list.Children.Add(row); panel.Children.Add(list);
        panel.Children.Add(LibraryUi.Text(model.Footer, 11.5, secondary: true));
        if (model.CanCreatePill) panel.Children.Add(Button(model.CreatePillText, "popover-create-pill", () => CreatePillRequested?.Invoke()));
        panel.Children.Add(model.RememberText.EndsWith(" · Use Settings", StringComparison.Ordinal)
            ? Line(model.RememberText.Replace(" · Use Settings", "", StringComparison.Ordinal), "Use Settings", "popover-use-settings", model.UseSettings)
            : LibraryUi.Text(model.RememberText, 12, secondary: true));
        content.Show(panel, FrameClock.IsBound ? Animation.EaseOut(duration: .25) : null);
    }
    private async void Pin(Guid id)
    {
        var result = await model.PinAsync(id);
        if (result == PinResult.OutOfService) Rows.FirstOrDefault(r => r.AccountId == id)?.Refuse();
    }
    private static Control Line(string text, string action, string id, Action click) => new StackPanel
    {
        Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6, Children = { LibraryUi.Text(text, 12), Button(action, id, click) },
    };
    private static Button Button(string text, string id, Action click)
    {
        var button = PlainControls.Button(LibraryUi.Text(text, 12), click); button.Focusable = true; button.Named(text, id); return button;
    }
}
