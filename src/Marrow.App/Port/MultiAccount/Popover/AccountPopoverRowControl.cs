// PORT-ONLY: MA-3b-2, locked page AccountPanel :2682-2718, .prow :227-246; OD-0115.
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Marrow.UI.Port.Settings;
using Avalonia.Media;
using Marrow.App.Port.MultiAccount.Capsule;
using Marrow.App.Port.MultiAccount.Shared;
using Marrow.App.Port.MultiAccount.Settings;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.App.Port.Skills.Pages;
using Marrow.Core.Port.MultiAccount.Popover;
using Marrow.UI.Port;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.MultiAccount.Popover;

internal sealed class AccountPopoverRowControl : Decorator
{
    private readonly TextBlock reset;
    private readonly AnimatedScalar feedback;
    private readonly Action<Guid, int> move;
    private int index;
    private int count;
    private readonly Button pinButton;
    internal Button LinkButton { get; }
    internal bool LinkBound { get; set; }
    private readonly Action<Guid, int>? navigate;
    private readonly Border dot;
    private readonly TextBlock mono;
    internal Border LinkMark { get; }
    private readonly Action<Guid> pin;
    private readonly Action? backToOrder;
    private readonly CzColor rowFill;
    private bool pinned, hovered;
    private readonly TextBlock name;
    private readonly TextBlock chip;
    private readonly TextBlock number;
    private readonly Border numberPill, grip;
    private readonly TextBlock hs;
    private readonly TextBlock note;
    private readonly StackPanel main;
    private readonly Control fiveMeter;
    private readonly Control weekMeter;
    private readonly TextBlock noFive;
    private readonly TextBlock localWords;
    private readonly AccountConfigLine config;
    private readonly AccountPopoverContext? context;
    internal AccountPopoverDialogs? Dialogs { get; }
    private bool pressed, dragging;
    private double dragStart, dragPitch, dragGoal;
    private int dragFrom, dragTo;
    private AccountPopoverRowControl[] dragRows = [];
    private readonly AnimatedScalar dragOffset, dragScale;
    private readonly OpacityTransition dragOpacity;
    private readonly MatrixTransform dragTransform = new();
    private readonly Func<IReadOnlyList<AccountPopoverRowControl>>? peers;
    internal AccountPopoverRowControl(AccountPopoverRow row, Action<Guid> pin, Action<Guid, int> move, int index, int count, AccountPopoverContext? context = null, Action<Guid, int>? navigate = null, Action? backToOrder = null, Func<IReadOnlyList<AccountPopoverRowControl>>? peers = null)
    {
        this.peers = peers; this.context = context; this.navigate = navigate; this.pin = pin; this.backToOrder = backToOrder;
        this.move = move; this.index = index; this.count = count;
        AccountId = row.Account.Id; Dimmed = row.Dimmed;
        dragOffset = new AnimatedScalar(this, 0, _ => PaintDrag());
        dragScale = new AnimatedScalar(this, 1, _ => PaintDrag());
        dragOpacity = new OpacityTransition(this, 1, value => { Opacity = value; if (value == 1 && !dragging) ZIndex = 0; });
        RenderTransformOrigin = new RelativePoint(.5, .5, RelativeUnit.Relative);
        MinHeight = 68; // final .prow.cf :1347: auto height and 7px vertical padding
        main = new StackPanel { Spacing = 5, Opacity = row.Account.Enabled ? row.Dimmed ? .5 : 1 : .42 };
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto") };
        var good = SwiftColor.Dynamic(SwiftColor.Rgb(0x0F7B3F), SwiftColor.Rgb(0x6CCB5F));
        dot = new Border { Width = 7, Height = 7, Background = good.Brush(this), VerticalAlignment = VerticalAlignment.Center }.RoundedRectangle(3.5, RoundedCornerStyle.Circular);
        var halo = new Border { Width = 13, Height = 13, Margin = new Thickness(-3), Background = good.Opacity(.12).Brush(this) }.RoundedRectangle(6.5, RoundedCornerStyle.Circular);
        var active = new Panel { Width = 7, Height = 7, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Children = { halo, dot } };
        dot.PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) halo.IsVisible = dot.IsVisible; }; top.Children.Add(active);
        name = MultiAccountText.Text(row.Account.Name, 13); name.TextTrimming = TextTrimming.CharacterEllipsis; Grid.SetColumn(name, 1); top.Children.Add(name);
        chip = MultiAccountText.Text(row.Chip ?? "", 10, secondary: true); chip.Margin = new Thickness(6, 0); Grid.SetColumn(chip, 2); top.Children.Add(chip);
        reset = MultiAccountText.Text(row.ResetText, 11, secondary: true); Grid.SetColumn(reset, 3); top.Children.Add(reset); main.Children.Add(top);
        FiveHour = new AccountUsageBar(); Week = new AccountUsageBar(); FiveHour.Apply(row.FiveHour); Week.Apply(row.Week);
        var meters = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        fiveMeter = Meter("5 h", FiveHour); noFive = MultiAccountText.Text("No 5-hour limit", 10.5, secondary: true);
        meters.Children.Add(new Panel { Children = { fiveMeter, noFive } });
        weekMeter = Meter("Week", Week); Grid.SetColumn(weekMeter, 1); weekMeter.Margin = new Thickness(10, 0, 0, 0); meters.Children.Add(weekMeter);
        FiveHour.IsVisible = row.LocalText is null && row.NoFiveHourText is null; Week.IsVisible = row.LocalText is null;
        localWords = MultiAccountText.Text("No limit · Stays on this PC", 11, secondary: true); main.Children.Add(new Panel { Children = { meters, localWords } });
        config = new AccountConfigLine(row.Config); var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var configButton = PlainControls.Button(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { config, LibraryUi.Glyph("chevron.down", 9, ChromeTokens.SecondaryText) } }, () => Dialogs?.OpenDefaults()); configButton.Focusable = context is not null; configButton.Named("This account’s defaults", "popover-config-" + AccountId.ToString("N"));
        line.Children.Add(configButton); hs = MultiAccountText.Text(row.HandshakeText, 10.5, secondary: true); hs.Named(row.HandshakeAria, "popover-handshake-" + AccountId.ToString("N")); Grid.SetColumn(hs, 1); line.Children.Add(hs); main.Children.Add(line);
        note = MultiAccountText.Text("", 10.5, secondary: true); note.IsVisible = false; main.Children.Add(note);
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("22,20,26,*,26,22"), Margin = new Thickness(2, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        grip = new Border { Width = 16, Height = 24, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Background = Brushes.Transparent, Child = LibraryUi.Glyph("line.3.horizontal", 12, ChromeTokens.SecondaryText), Cursor = new Cursor(StandardCursorType.SizeAll) };
        grip.Named("Drag to set the order", "popover-grip-" + AccountId.ToString("N"));
        grip.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed) return;
            pressed = true; dragging = false; dragStart = e.GetPosition(null).Y;
            dragRows = peers?.Invoke().ToArray() ?? [this]; dragFrom = Array.IndexOf(dragRows, this); dragTo = dragFrom;
            dragPitch = dragRows.Length > 1 ? dragRows[1].Bounds.Y - dragRows[0].Bounds.Y : Bounds.Height;
            if (dragPitch <= 0) dragPitch = Bounds.Height + 2;
            e.Pointer.Capture(grip); e.Handled = true;
        };
        grip.PointerMoved += (_, e) =>
        {
            if (!pressed) return;
            var distance = e.GetPosition(null).Y - dragStart;
            if (!dragging && Math.Abs(distance) < 5) return;
            dragging = true; ZIndex = 5;
            var low = -dragFrom * dragPitch; var high = (dragRows.Length - 1 - dragFrom) * dragPitch;
            var carried = distance < low ? low - Math.Min(dragPitch * .35, (low - distance) * .3)
                : distance > high ? high + Math.Min(dragPitch * .35, (distance - high) * .3) : distance;
            dragTo = Math.Clamp(dragFrom + (int)Math.Floor(carried / dragPitch + .5), 0, dragRows.Length - 1);
            dragOffset.Set(carried, null); dragScale.Set(1.02, null);
            if (CapsuleMotion.Reduce(this)) { dragOpacity.Set(.5, null); }
            for (var i = 0; i < dragRows.Length; i++)
            {
                if (i == dragFrom) continue;
                var target = dragFrom < dragTo && i > dragFrom && i <= dragTo ? -dragPitch
                    : dragFrom > dragTo && i >= dragTo && i < dragFrom ? dragPitch : 0;
                dragRows[i].GlideDrag(target);
            }
            e.Handled = true;
        };
        grip.PointerReleased += (_, e) =>
        {
            if (!pressed) return; pressed = false;
            if (dragging)
            {
                dragging = false;
                var before = dragRows.Select(row => row.Bounds.Y + row.dragOffset.Value).ToArray();
                move(AccountId, dragTo);
                // FLIP: retained rows are reordered in the same stack; compensate their new positions before the next layout.
                for (var i = 0; i < dragRows.Length; i++)
                {
                    var to = i == dragFrom ? dragTo : dragFrom < dragTo && i > dragFrom && i <= dragTo ? i - 1
                        : dragFrom > dragTo && i >= dragTo && i < dragFrom ? i + 1 : i;
                    dragRows[i].SettleDrag(before[i] - (dragRows[0].Bounds.Y + to * dragPitch), i == dragFrom);
                }
            }
            e.Pointer.Capture(null); e.Handled = true;
        };
        grip.PointerCaptureLost += (_, _) =>
        {
            if (!pressed) return; pressed = dragging = false;
            foreach (var row in dragRows) row.SettleDrag(row.dragOffset.Value, row == this);
        };
        body.Children.Add(grip);
        number = MultiAccountText.Text(row.Account.Number.ToString(CultureInfo.InvariantCulture), 10.5, secondary: true, weight: SwiftFont.Weight.Semibold);
        number.HorizontalAlignment = HorizontalAlignment.Center; number.VerticalAlignment = VerticalAlignment.Center;
        numberPill = new Border { MinWidth = 18, Height = 18, Padding = new Thickness(5, 0), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = number, Opacity = main.Opacity,
            Background = SwiftColor.Dynamic(SwiftColor.Black.Opacity(.055), SwiftColor.White.Opacity(.06)).Brush(this) }.RoundedRectangle(9, RoundedCornerStyle.Circular);
        Grid.SetColumn(numberPill, 1); body.Children.Add(numberPill);
        mono = MultiAccountText.Text(row.Account.Mono, 10, weight: SwiftFont.Weight.Semibold);
        mono.HorizontalAlignment = HorizontalAlignment.Center; mono.VerticalAlignment = VerticalAlignment.Center;
        var hue = AccountMarkColors.Of(row.Account);
        LinkMark = new Border { Width = 22, Height = 22, Child = mono, Background = new SolidColorBrush(hue, .12), BorderBrush = new SolidColorBrush(hue, .5), BorderThickness = new Thickness(.5), VerticalAlignment = VerticalAlignment.Center }.RoundedRectangle(7, RoundedCornerStyle.Continuous);
        Grid.SetColumn(LinkMark, 2); body.Children.Add(LinkMark);
        Grid.SetColumn(main, 3); body.Children.Add(main);
        var button = PlainControls.Button(body, () => pin(AccountId)); button.Focusable = true; button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.MinHeight = 68; button.Padding = new Thickness(0, 7);
        pinButton = button;
        AccountButtonFace.Apply(button, 9, focusRing: true);
        rowFill = new CzColor(new CzMotion(button), Colors.Transparent, color => button.Background = new SolidColorBrush(color));
        button.PointerEntered += (_, _) => { hovered = true; Paint(); }; button.PointerExited += (_, _) => { hovered = false; Paint(); };
        button.ActualThemeVariantChanged += (_, _) => Paint();
        LinkButton = PlainControls.Button(LibraryUi.Glyph("link", 13, ChromeTokens.SecondaryText), () => { });
        LinkButton.Focusable = true; LinkButton.IsEnabled = false; Grid.SetColumn(LinkButton, 4); body.Children.Add(LinkButton);
        var more = MenuPopover.More("Options for " + row.Account.Name, "popover-more-" + AccountId.ToString("N"), () => Menu, Pick);
        Grid.SetColumn(more, 5); body.Children.Add(more);
        // A nested row action must not bubble to the account pin button (page stopPropagation).
        body.AddHandler(Button.ClickEvent, (_, e) => e.Handled = true, Avalonia.Interactivity.RoutingStrategies.Bubble);
        var aria = row.Account.Number.ToString(CultureInfo.InvariantCulture) + ". " + row.Account.Name + (row.Account.IsOut ? ", out until " + row.ResetText + ", skipped" : ", 5 hours " + row.FiveHour.Percent + ", week " + row.Week.Percent) + (row.Active ? ", in use" : "") + ". Alt+Up or Alt+Down moves it.";
        button.Named(aria, "popover-pin-" + AccountId.ToString("N")); this.Named(aria, "popover-row-" + AccountId.ToString("N")); Child = button;
        if (context is not null) { Dialogs = new AccountPopoverDialogs(context, AccountId, row.Account.Name, row.Account.Service, configButton); Child = null; Child = new Panel { Children = { button, Dialogs.Host } }; }
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Apps || e.Key == Key.F10 && e.KeyModifiers == KeyModifiers.Shift)
            {
                more.GetLogicalDescendants().OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); e.Handled = true; return;
            }
            if (e.Key is not (Key.Up or Key.Down)) return;
            if (e.KeyModifiers == KeyModifiers.Alt) Pick(e.Key == Key.Up ? "up" : "down");
            else if (e.KeyModifiers == KeyModifiers.None) navigate?.Invoke(AccountId, e.Key == Key.Up ? -1 : 1);
            else return;
            e.Handled = true;
        };
        feedback = new AnimatedScalar(this, 1, progress =>
        {
            FeedbackRunning = progress < 1;
            if (!CapsuleMotion.Reduce(this)) reset.RenderTransform = progress >= 1 ? null : new TranslateTransform(Math.Sin(progress * Math.PI * 4) * 3, 0);
            else reset.Opacity = .6 + .4 * progress;
        });
        Apply(row, index, count);
    }
    internal Guid AccountId { get; }
    internal AccountUsageBar FiveHour { get; }
    internal AccountUsageBar Week { get; }
    internal bool Dimmed { get; private set; }
    internal bool FeedbackRunning { get; private set; }
    internal string? HandshakeNote => context is not null && context.Store.Get(AccountId).Defaults.HandshakePoint is { } own && own != context.Store.GeneralHandshakePoint
        ? "Handshake at " + (own.Percentage is { } n ? n.ToString(CultureInfo.InvariantCulture) + "%" : "Automatically") : null;
    internal IReadOnlyList<MenuEntry> Menu => [new(pinned ? "back" : "pin", pinned ? "Back to order" : "Keep this thread on it"), MenuEntry.Line, new("up", "Move up", "Alt+↑", Disabled: index == 0), new("top", "Move to top", Disabled: index == 0), new("down", "Move down", "Alt+↓", Disabled: index == count - 1), .. context is null ? Array.Empty<MenuEntry>() : new[] { MenuEntry.Line, new MenuEntry("handshake", "Handshake at…") }];
    private void Pick(string key)
    {
        if (key == "pin") { pin(AccountId); return; }
        if (key == "back") { backToOrder?.Invoke(); return; }
        if (key == "handshake") { Dialogs?.OpenHandshake(); return; }
        move(AccountId, key switch { "up" => Math.Max(0, index - 1), "top" => 0, "down" => Math.Min(count - 1, index + 1), _ => index });
    }
    internal void Apply(AccountPopoverRow row, int at, int total)
    {
        index = at; count = total; Dimmed = row.Dimmed;
        if (!LinkBound)
        {
            LinkButton.IsVisible = !row.Active && row.Account.Enabled && !row.Account.IsOut;
            LinkButton.Named("Link " + row.Account.Name + " to check this thread’s work", "link-account-" + AccountId.ToString("N"));
        }
        name.Text = row.Account.Name; mono.Text = row.Account.Mono; chip.Text = row.Chip ?? ""; dot.IsVisible = row.Active; reset.Text = row.ResetText;
        number.Text = row.Account.Number.ToString(CultureInfo.InvariantCulture); main.Opacity = row.Account.Enabled ? row.Dimmed ? .5 : 1 : .42; numberPill.Opacity = main.Opacity; grip.Opacity = row.Account.Enabled ? .8 : .42;
        FiveHour.Apply(row.FiveHour); Week.Apply(row.Week); config.Apply(row.Config); hs.Text = row.HandshakeText;
        noFive.IsVisible = row.LocalText is null && row.NoFiveHourText is not null; fiveMeter.IsVisible = row.LocalText is null && row.NoFiveHourText is null; weekMeter.IsVisible = row.LocalText is null; localWords.IsVisible = row.LocalText is not null;
        FiveHour.IsVisible = fiveMeter.IsVisible; Week.IsVisible = weekMeter.IsVisible;
        pinned = row.Pinned; Paint();
        note.Text = HandshakeNote ?? ""; note.IsVisible = HandshakeNote is not null;
        var aria = row.Account.Number.ToString(CultureInfo.InvariantCulture) + ". " + row.Account.Name + (row.Account.IsOut ? ", out until " + row.ResetText.Replace("resets ", "", StringComparison.Ordinal) + ", skipped" : row.LocalText is not null ? ", No limit · Stays on this PC" : row.NoFiveHourText is not null ? ", No 5-hour limit, week " + row.Week.Percent : ", 5 hours " + row.FiveHour.Percent + ", week " + row.Week.Percent) + (row.Active ? ", in use" : "") + ". Alt+Up or Alt+Down moves it.";
        pinButton.Named(aria, "popover-pin-" + AccountId.ToString("N")); this.Named(aria, "popover-row-" + AccountId.ToString("N")); hs.Named(row.HandshakeAria, "popover-handshake-" + AccountId.ToString("N"));
    }
    private void Paint()
    {
        var dark = pinButton.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        var color = pinned ? Color.Parse(dark ? "#2460CDFF" : "#1A0067C0") : hovered ? Color.Parse(dark ? "#0FFFFFFF" : "#0D000000") : Colors.Transparent;
        rowFill.To(color, CzMotion.Ease(.15));
    }
    private void PaintDrag()
    {
        if (dragOffset is null || dragScale is null) return;
        if (CapsuleMotion.Reduce(this) || dragOffset.Value == 0 && dragScale.Value == 1) { RenderTransform = null; if (!dragging) ZIndex = 0; return; }
        dragTransform.Matrix = new Matrix(dragScale.Value, 0, 0, dragScale.Value, 0, dragOffset.Value); RenderTransform = dragTransform;
    }
    private void GlideDrag(double target)
    {
        if (dragGoal == target) return; dragGoal = target;
        if (CapsuleMotion.Reduce(this)) { dragOffset.Set(target, null); dragOpacity.Set(.4, null); dragOpacity.Set(1, Animation.TimingCurve(.25, .1, .25, 1, .2)); }
        else dragOffset.Set(target, Animation.TimingCurve(.32, .72, 0, 1, .34));
    }
    private void SettleDrag(double displacement, bool carried)
    {
        dragGoal = 0; dragOffset.Set(displacement, null); dragScale.Set(carried ? 1.02 : 1, null);
        var curve = carried ? PopoverSettleCurve.Instance : Animation.Spring(response: 2 * Math.PI / Math.Sqrt(280), dampingFraction: 32 / (2 * Math.Sqrt(280)));
        dragOffset.Set(0, FrameClock.IsBound ? curve : null); dragScale.Set(1, FrameClock.IsBound ? curve : null);
        if (CapsuleMotion.Reduce(this)) { dragOpacity.Set(carried ? .5 : .4, null); dragOpacity.Set(1, Animation.TimingCurve(.25, .1, .25, 1, .22)); }
        ZIndex = carried && FrameClock.IsBound ? 5 : 0;
    }
    internal void DragBy(double distance) => move(AccountId, AccountRowControl.DragTarget(index, distance, Bounds.Height > 0 ? Bounds.Height + 2 : 70, count));
    internal void FocusPin() => pinButton.Focus();
    internal void Refuse()
    {
        feedback.Set(0, null); FeedbackRunning = true;
        feedback.Set(1, FrameClock.IsBound ? Animation.Linear(duration: .3) : null);
    }
    private static Control Meter(string label, AccountUsageBar bar)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("30,*,26") };
        row.Children.Add(MultiAccountText.Text(label, 10.5, secondary: true)); Grid.SetColumn(bar, 1); bar.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(bar);
        var percent = MultiAccountText.Text(bar.Percent, 10, secondary: true, weight: SwiftFont.Weight.Medium); bar.Applied += () => percent.Text = bar.Percent; percent.TextAlignment = TextAlignment.Right; Grid.SetColumn(percent, 2); row.Children.Add(percent); return row;
    }
}
