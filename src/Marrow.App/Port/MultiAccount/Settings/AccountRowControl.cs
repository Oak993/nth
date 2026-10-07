// PORT-ONLY: T11 step 2. One account's row in the list of a service, as the page draws it: a grip (drag to reorder, or Alt+Up and Alt+Down), its number, its name (tap to rename, up to 24 characters), one grey line (plan, the Handshake point
// when it differs, a custom name, off, out until), its week bar, the port's own switch (on = used; off = signed in, never used) and a ⋯ menu (Rename, Move up / top / down, Handshake at…, Sign out of this account).
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Marrow.App.Port.MultiAccount.Capsule;
using Marrow.App.Port.MultiAccount.Shared;
using Avalonia.Media;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Core.Port.Accounts;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.MultiAccount.Settings;

internal sealed class AccountRowControl : Decorator
{
    internal const double RowHeight = 46;

    private readonly AccountsSettingsModel model;
    private readonly Guid id;
    private readonly ModelPulse pulse;
    private readonly Action<Guid, int, double> dragged;
    private readonly CrossfadeSlot nameSlot = new();
    private readonly CzCrossfadeText subtitle = new("") { FontSize = 12, Secondary = true };
    private readonly CzCrossfadeText usage = new("") { FontSize = 11.5, Secondary = true };
    private readonly LibraryBar bar = new() { Width = 96 };
    private readonly Control grip;
    private readonly Button nameButton;
    private readonly TextBlock numberText;
    private readonly TextBlock nameText;
    private readonly StackPanel usageBox;
    private readonly StackPanel words;
    private readonly Action openHandshake;
    private readonly SettingsSwitch toggle;
    private readonly OpacityTransition dragFade;
    private AccountRowView view;
    private double dragStart;
    private bool dragging;
    private bool renaming;

    internal AccountRowControl(AccountsSettingsModel model, AccountRowView first, ModelPulse pulse, Action<Guid, int, double> dragged)
    {
        this.model = model;
        this.pulse = pulse;
        this.dragged = dragged;
        id = first.Id;
        view = first;
        dragFade = new OpacityTransition(this, 1, value => Opacity = value);

        grip = new Border { Width = 22, Height = 22, Background = Brushes.Transparent, Child = LibraryUi.Glyph("line.3.horizontal", 12, ChromeTokens.SecondaryText, SwiftFont.Weight.Medium) };
        ((Control)((Border)grip).Child!).HorizontalAlignment = HorizontalAlignment.Center;
        ((Control)((Border)grip).Child!).VerticalAlignment = VerticalAlignment.Center;
        grip.Opacity = 0.7;
        grip.Cursor = new Cursor(StandardCursorType.SizeAll);
        grip.Named("Drag to reorder", "account-grip-" + id.ToString("N"));
        grip.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed) return; dragging = true; dragStart = e.GetPosition(null).Y; e.Pointer.Capture(grip); e.Handled = true; };
        grip.PointerMoved += (_, e) => { if (dragging) { if (CapsuleMotion.Reduce(this)) { RenderTransform = null; dragFade.Set(.7, FrameClock.IsBound ? Animation.EaseOut(duration: .3) : null); } else RenderTransform = new TranslateTransform(0, e.GetPosition(null).Y - dragStart); ZIndex = 1; } };
        grip.PointerReleased += (_, e) => EndDrag(e.GetPosition(null).Y - dragStart);
        grip.PointerCaptureLost += (_, _) => { if (dragging) EndDrag(0); };

        numberText = LibraryUi.Text("", 12, secondary: true);
        numberText.VerticalAlignment = VerticalAlignment.Center;
        nameText = LibraryUi.Text("", 14, weight: SwiftFont.Weight.Medium);
        nameButton = PlainControls.Button(nameText, StartRename);
        nameButton.Focusable = true;
        nameButton.Cursor = new Cursor(StandardCursorType.Hand);
        nameButton.Named(first.Name, "account-name-" + id.ToString("N"));
        ToolTip.SetTip(nameButton, MultiAccountWords.Rename);
        nameSlot.HorizontalAlignment = HorizontalAlignment.Left;
        nameSlot.Show(nameButton, null);

        usageBox = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Children = { bar, usage } };
        toggle = new SettingsSwitch(new Binding<bool>(() => { _ = pulse.Revision; return view.Enabled; }, on => model.SetEnabled(id, on)));
        toggle.Named("Use " + first.Name, "account-switch-" + id.ToString("N"));
        toggle.VerticalAlignment = VerticalAlignment.Center;
        var moreButton = MenuPopover.More("More", "account-more-" + id.ToString("N"), () => model.Menu(id), Pick);
        var (handshakeHost, openHandshake) = MenuPopover.Anchored(moreButton, () => model.HandshakeMenu(id), PickHandshake);
        this.openHandshake = openHandshake;
        var more = new Panel { Width = 22, Height = 22, VerticalAlignment = VerticalAlignment.Center, Children = { moreButton, handshakeHost } };

        words = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { nameSlot, subtitle } };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("22,18,*,Auto,44,22"), ColumnSpacing = 8, Height = RowHeight };
        grip.VerticalAlignment = VerticalAlignment.Center;
        foreach (var (control, column) in new (Control, int)[] { (grip, 0), (numberText, 1), (words, 2), (usageBox, 3), (toggle, 4), (more, 5) })
        {
            Grid.SetColumn(control, column);
            grid.Children.Add(control);
        }
        Child = new Border { Padding = new Thickness(12, 0, 12, 0), Child = grid };
        Focusable = true;
        KeyDown += (_, e) =>
        {
            if (renaming || (e.KeyModifiers & KeyModifiers.Alt) == 0) return;
            if (e.Key == Key.Up) { model.Do(id, "up"); e.Handled = true; }
            if (e.Key == Key.Down) { model.Do(id, "down"); e.Handled = true; }
        };
        Apply(first, animate: false);
    }

    internal Guid Id => id;

    internal AccountRowView View => view;

    internal string SubtitleShown => subtitle.Text;

    internal string UsageShown => usage.Text;

    internal string NameShown => nameText.Text ?? "";

    internal bool Renaming => renaming;

    internal Button NameButton => nameButton;

    internal LibraryBar Bar => bar;

    internal void Apply(AccountRowView next, bool animate = true)
    {
        view = next;
        numberText.Text = next.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!renaming) nameText.Text = next.Name;
        AutomationProperties.SetName(nameButton, next.Name);
        AutomationProperties.SetName(toggle, "Use " + next.Name);
        subtitle.SetText(next.Subtitle);
        usageBox.IsVisible = next.UsageText.Length > 0 || next.WeekFraction is not null;
        usage.SetText(next.UsageText);
        bar.Set(next.WeekFraction ?? 0, animate);
        bar.IsVisible = next.WeekFraction is not null;
        words.Opacity = usageBox.Opacity = numberText.Opacity = next.Enabled ? 1 : .45;
        AutomationProperties.SetName(this, next.Number + ". " + next.Name);
    }

    internal void Pick(string key)
    {
        switch (key)
        {
            case "rename": StartRename(); break;
            case "up" or "top" or "down": model.Do(id, key); break;
            case "signout": _ = model.SignOutAsync(id); break;
            case "handshake": openHandshake(); break;
        }
    }

    private void PickHandshake(string key) => model.SetHandshake(id, key switch
    {
        "like" => null,
        "smart" => HandshakePoint.Smart,
        _ => new HandshakePoint(int.Parse(key, System.Globalization.CultureInfo.InvariantCulture)),
    });

    private void EndDrag(double dy)
    {
        if (!dragging) return;
        dragging = false;
        dragFade.Set(1, FrameClock.IsBound ? Animation.EaseOut(duration: .3) : null);
        RenderTransform = null;
        ZIndex = 0;
        var target = DragTarget(view.Number - 1, dy, RowHeight, int.MaxValue);
        if (target != view.Number - 1) dragged(id, target, dy);
    }

    /// <summary>Where a row dragged by <paramref name="dy"/> points lands (its place plus whole rows moved, kept inside the list).</summary>
    internal static int DragTarget(int index, double dy, double rowHeight, int count) =>
        Math.Clamp(index + (int)Math.Round(dy / rowHeight, MidpointRounding.AwayFromZero), 0, Math.Max(0, count - 1));

    private void StartRename()
    {
        if (renaming) return;
        renaming = true;
        var box = new TextBox { Text = view.Name, MaxLength = 24, MinWidth = 150, FontSize = 14, Padding = new Thickness(6, 2) };
        AutomationProperties.SetName(box, MultiAccountWords.AccountNameLabel);
        AutomationProperties.SetAutomationId(box, "account-rename-" + id.ToString("N"));
        var count = LibraryUi.Text(MultiAccountWords.Count(box.Text!.Length), 11, secondary: true);
        count.VerticalAlignment = VerticalAlignment.Center;
        count.Foreground = (box.Text!.Length >= 24 ? MultiAccountPalette.Hot : ChromeTokens.SecondaryText).Brush(count);
        box.TextChanged += (_, _) =>
        {
            var used = box.Text?.Length ?? 0;
            count.Text = MultiAccountWords.Count(used);
            count.Foreground = (used >= 24 ? MultiAccountPalette.Hot : ChromeTokens.SecondaryText).Brush(count); // the page turns the counter hot at 24
        };
        var done = false;
        void Finish(bool commit)
        {
            if (done) return;
            done = true;
            renaming = false;
            if (commit) model.Rename(id, box.Text);
            nameSlot.Show(nameButton, LibraryUi.Ease(this, LibraryUi.Fade));
            nameText.Text = view.Name;
        }
        box.KeyDown += (_, e) =>
        {
            e.Handled = true;
            if (e.Key == Key.Enter) Finish(true);
            else if (e.Key == Key.Escape) Finish(false);
        };
        box.LostFocus += (_, _) => Finish(true);
        nameSlot.Show(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { box, count } }, LibraryUi.Ease(this, LibraryUi.Fade));
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { box.Focus(); box.SelectAll(); });
    }
}
