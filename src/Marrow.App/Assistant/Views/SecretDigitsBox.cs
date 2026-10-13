// PORT-ONLY: Dextra AS-20 (DESIGN.md rev 5, 12.7 "Only the last 4 digits are ever shown"; the canvas's add-card sheet, "every digit
// is hidden as you type"; ED-NEW-dextra-as20-13). No Swift twin.
// A field the card's digits are typed into and never read back from. Each digit goes straight into the CardEntry's pinned buffer;
// the box draws one dot per digit, and once a whole number passes its check digit, its last 4. It has no text a screen reader,
// the clipboard or a drag could take: copy, cut, paste, select-all and drops do nothing, and there is no reveal.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Assistant.Views;

public sealed class SecretDigitsBox : Border
{
    private readonly CardEntry entry;
    private readonly TextBlock shown;
    private readonly TextBlock hint;
    private readonly IBrush rest, focused;

    public SecretDigitsBox(CardEntry entry, CardPart part, string name, string placeholder)
    {
        this.entry = entry ?? throw new ArgumentNullException(nameof(entry));
        Part = part;
        Focusable = true;
        IsTabStop = true;
        Cursor = new Cursor(StandardCursorType.Ibeam);
        MinHeight = Chrome.CapsuleContentHeight;
        Padding = new Thickness(10, 6);
        this.RoundedRectangle(10, RoundedCornerStyle.Continuous);
        rest = Chrome.Overlay(.06).Brush(this);
        focused = Chrome.Overlay(.1).Brush(this);
        Background = rest;
        BorderThickness = new Thickness(1);
        BorderBrush = Brushes.Transparent;
        shown = TaskLook.Text("", 13, SwiftFont.Weight.Medium, TaskLook.Ink);
        shown.VerticalAlignment = VerticalAlignment.Center;
        hint = TaskLook.Text(placeholder, 13, SwiftFont.Weight.Regular, TaskLook.Ink2);
        hint.VerticalAlignment = VerticalAlignment.Center;
        Child = new Panel { Children = { hint, shown } };
        AutomationProperties.SetName(this, name);
        AutomationProperties.SetControlTypeOverride(this, AutomationControlType.Edit);
        DragDrop.SetAllowDrop(this, false);
        entry.Changed += Show;
        Show();
    }

    public CardPart Part { get; }

    /// <summary>What the box draws: dots, and the last 4 of a whole number that checks out. Never the digits themselves.</summary>
    public string Shown => shown.Text ?? "";

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        Background = focused;
        BorderBrush = Chrome.PrimaryText.Opacity(.5).Brush(this); // the focus ring: a line, not only a colour change of the fill
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        Background = rest;
        BorderBrush = Brushes.Transparent;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus(NavigationMethod.Pointer);
        e.Handled = true;
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        foreach (var c in e.Text ?? "")
        {
            if (c is >= '0' and <= '9') entry.Type(Part, c);
        }
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var command = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (e.Key is Key.Back or Key.Delete)
        {
            entry.Erase(Part);
            e.Handled = true;
        }
        else if ((command && e.Key is Key.C or Key.X or Key.V or Key.A or Key.Insert) || (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key is Key.Insert or Key.Delete))
        {
            // Nothing goes to or comes from the clipboard: the card is typed, digit by digit, and never copied.
            e.Handled = true;
        }
    }

    private void Show()
    {
        // The entry is cleared off the UI thread once the vault saved it; the box redraws on its own thread.
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(Show);
            return;
        }
        var length = entry.Length(Part);
        var dots = new string('•', length);
        if (Part == CardPart.Number && entry.LastFour is { } four && length > 4)
            dots = new string('•', length - 4) + " " + four;
        shown.Text = dots;
        hint.IsVisible = length == 0;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new SecretPeer(this);

    // The box tells a screen reader its name and how many digits it holds, never what they are.
    private sealed class SecretPeer(SecretDigitsBox owner) : ControlAutomationPeer(owner)
    {
        protected override string? GetHelpTextCore() =>
            owner.entry.Length(owner.Part) is var n and > 0 ? (n == 1 ? "1 digit" : n.ToString(System.Globalization.CultureInfo.InvariantCulture) + " digits") : null;

        protected override bool IsContentElementCore() => true;

        protected override bool IsControlElementCore() => true;
    }
}
