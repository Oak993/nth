// PORT-ONLY: MA-3b-2, locked page AccountPanel :2674-2797; content of the existing PopoverHost glass.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Marrow.App.Port.MultiAccount.Shared;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Views.Settings;
using Marrow.Core.Port.MultiAccount.Popover;
using Marrow.Core.Port.MultiAccount.Thread;
using Marrow.UI.Port;

namespace Marrow.App.Port.MultiAccount.Popover;

internal sealed class AccountPopover : Decorator
{
    private readonly AccountPopoverModel model;
    private readonly CrossfadeSlot pinSlot = new();
    private readonly CrossfadeSlot footerSlot = new();
    private readonly CrossfadeSlot rememberSlot = new();
    private readonly StackPanel list = new() { Spacing = 2 };
    private readonly Dictionary<Guid, AccountPopoverRowControl> kept = [];
    private readonly Dictionary<CrossfadeSlot, string> shown = [];
    private readonly AccountPopoverContext? context;
    private readonly Button pill;
    private Guid? focusAccount;
    internal AccountPopover(AccountPopoverModel model, AccountPopoverContext? context = null)
    {
        this.model = model; this.context = context; Width = 340;
        var panel = new StackPanel { Spacing = 2, Margin = new Thickness(6) };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(8, 6, 8, 4) };
        header.Children.Add(MultiAccountText.Text(model.Header, 12, secondary: true, weight: SwiftFont.Weight.Semibold));
        var hint = MultiAccountText.Text(model.OrderHint, 11, secondary: true); hint.Foreground = MultiAccountPalette.Faint.Brush(hint); Grid.SetColumn(hint, 1); header.Children.Add(hint); panel.Children.Add(header);
        pill = Button(model.CreatePillText, "popover-create-pill", () => CreatePillRequested?.Invoke());
        panel.Children.Add(pinSlot); panel.Children.Add(list); panel.Children.Add(footerSlot); panel.Children.Add(pill); panel.Children.Add(rememberSlot); Child = panel; Refresh();
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
        var pin = model.PinnedText;
        TextSlot(pinSlot, pin ?? model.Subtitle, () => pin is null ? MultiAccountText.Text(model.Subtitle, 12, secondary: true)
            : Line(pin.Replace(" · Back to order", "", StringComparison.Ordinal), "Back to order", "popover-back-to-order", model.BackToOrder));
        var values = model.Rows;
        foreach (var id in kept.Keys.Where(id => !values.Any(r => r.Account.Id == id)).ToArray()) { list.Children.Remove(kept[id]); kept.Remove(id); }
        var wanted = new List<AccountPopoverRowControl>();
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index];
            if (!kept.TryGetValue(value.Account.Id, out var row)) { row = new AccountPopoverRowControl(value, Pin, Move, index, values.Count, context, Navigate, model.BackToOrder, () => Rows); kept.Add(value.Account.Id, row); list.Children.Add(row); }
            else row.Apply(value, index, values.Count);
            var old = list.Children.IndexOf(row); if (old != index) list.Children.Move(old, index); wanted.Add(row);
        }
        Rows = wanted; TextSlot(footerSlot, model.Footer, () => Footer(model.Footer)); pill.IsVisible = model.CanCreatePill;
        TextSlot(rememberSlot, model.RememberText, () => model.RememberText.EndsWith(" · Use Settings", StringComparison.Ordinal)
            ? Line(model.RememberText.Replace(" · Use Settings", "", StringComparison.Ordinal), "Use Settings", "popover-use-settings", model.UseSettings)
            : MultiAccountText.Text(model.RememberText, 12, secondary: true));
        if (focusAccount is { } focusedId) Avalonia.Threading.Dispatcher.UIThread.Post(() => Rows.FirstOrDefault(r => r.AccountId == focusedId)?.FocusPin());
    }
    private Control Footer(string value)
    {
        var text = MultiAccountText.Text(value, 11.5, secondary: true, wrap: true); text.LineHeight = 16;
        return new Border { Padding = new Thickness(8, 8, 8, 6), Margin = new Thickness(0, 4, 0, 0), BorderThickness = new Thickness(0, 1, 0, 0),
            BorderBrush = SwiftColor.Dynamic(SwiftColor.Black.Opacity(.1), SwiftColor.White.Opacity(.12)).Brush(this), Child = text };
    }
    private void TextSlot(CrossfadeSlot slot, string value, Func<Control> make)
    {
        if (shown.GetValueOrDefault(slot) == value) return; shown[slot] = value; slot.Show(make(), FrameClock.IsBound ? Animation.EaseOut(duration: .25) : null);
    }
    private void Navigate(Guid id, int direction)
    {
        var at = Rows.ToList().FindIndex(row => row.AccountId == id);
        if (at >= 0) Rows[Math.Clamp(at + direction, 0, Rows.Count - 1)].FocusPin();
    }
    private void Move(Guid id, int target)
    {
        var ids = model.Rows.Select(r => r.Account.Id).ToList(); var index = ids.IndexOf(id);
        if (index < 0 || target < 0 || target >= ids.Count) return;
        focusAccount = id;
        if (index == target) { Rows[index].FocusPin(); return; }
        ids.RemoveAt(index); ids.Insert(target, id); model.Reorder(ids);
    }
    private async void Pin(Guid id)
    {
        var result = await model.PinAsync(id);
        if (result == PinResult.OutOfService) Rows.FirstOrDefault(r => r.AccountId == id)?.Refuse();
    }
    private static Control Line(string text, string action, string id, Action click) => new StackPanel
    {
        Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6, Children = { MultiAccountText.Text(text, 12), Button(action, id, click) },
    };
    private static Button Button(string text, string id, Action click)
    {
        var button = PlainControls.Button(MultiAccountText.Text(text, 12), click); button.Focusable = true; button.Named(text, id); return button;
    }
}
