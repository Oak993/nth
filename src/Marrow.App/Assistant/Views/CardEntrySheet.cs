// PORT-ONLY: Dextra AS-20 (DESIGN.md rev 5, 12.7 "The card"; the canvas's add-card sheet, work/design/assistant/index.html lines
// 3174-3230; ED-NEW-dextra-as20-13). No Swift twin.
// The add-card sheet: the number, the expiry and the security code, each a SecretDigitsBox over one CardEntry, checked as they are
// typed (a mistyped number or a past expiry is said without showing it), where the card is kept and that nothing reveals it, and
// "Save with Windows Hello". Escape and Cancel close it and put the focus back on the button that opened it; closing it in any
// way zeroes what was typed.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Assistant.Views;

public sealed class CardEntrySheet : Border
{
    private readonly PurchaseDesk desk;
    private readonly Control? returnFocusTo;
    private readonly CardEntry entry = new();
    private readonly LiveStatusText check = new() { TextWrapping = TextWrapping.Wrap };
    private readonly LiveStatusText status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button save;
    private bool finished;
    private bool saving;
    private bool closed;

    public CardEntrySheet(PurchaseDesk desk, Control? returnFocusTo = null)
    {
        this.desk = desk ?? throw new ArgumentNullException(nameof(desk));
        this.returnFocusTo = returnFocusTo;
        Padding = new Thickness(14);
        this.RoundedRectangle(Chrome.CardCornerRadius, RoundedCornerStyle.Continuous);
        Background = Chrome.Overlay(.04).Brush(this);
        AutomationProperties.SetName(this, PurchaseWords.AddTitle);
        SwiftFont.System(SwiftFont.TextStyle.Footnote).Apply(check);
        check.Foreground = TaskLook.Ink2.Brush(check);
        SwiftFont.System(SwiftFont.TextStyle.Footnote).Apply(status);
        status.Foreground = Chrome.PrimaryText.Brush(status);

        Number = new SecretDigitsBox(entry, CardPart.Number, PurchaseWords.NumberName, "•••• •••• •••• ••••");
        Expiry = new SecretDigitsBox(entry, CardPart.Expiry, PurchaseWords.ExpiryName, "MM YY");
        Code = new SecretDigitsBox(entry, CardPart.Code, PurchaseWords.CodeName, "•••");
        (save, var saveShown) = TaskLook.Small(PurchaseWords.Save, () => _ = Save(), primary: true);
        var (cancel, cancelShown) = TaskLook.Small(PurchaseWords.Cancel, Close);
        CancelButton = cancel;

        var title = TaskLook.Text(PurchaseWords.AddTitle, 14, SwiftFont.Weight.Semibold, TaskLook.Ink, wrap: true);
        var small = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*") };
        var expiry = Field(PurchaseWords.ExpiryLabel, Expiry);
        var code = Field(PurchaseWords.CodeLabel, Code);
        Grid.SetColumn(code, 2);
        small.Children.Add(expiry);
        small.Children.Add(code);
        Child = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 10,
            Children =
            {
                title,
                Note(PurchaseWords.VaultHidden),
                Field(PurchaseWords.NumberLabel, Number),
                small,
                check,
                Note(PurchaseWords.VaultWhere),
                Note(PurchaseWords.VaultAfter),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { saveShown, cancelShown } },
                status,
            },
        };
        entry.Changed += Checked;
        Number.LostFocus += (_, _) => Checked();
        desk.Changed += DeskChanged;
        AddHandler(KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Bubble);
        DetachedFromVisualTree += (_, _) => Done();
        Checked();
    }

    public SecretDigitsBox Number { get; }

    public SecretDigitsBox Expiry { get; }

    public SecretDigitsBox Code { get; }

    public Button SaveButton => save;

    public Button CancelButton { get; }

    public string CheckText => check.Text ?? "";

    public string StatusText => status.Text ?? "";

    /// <summary>Fires once when the sheet is done: Cancel, Escape, or a card saved. The host removes it.</summary>
    public event Action? Finished;

    private static Control Field(string label, Control box) => new StackPanel
    {
        Orientation = Orientation.Vertical,
        Spacing = 4,
        Children = { TaskLook.Text(label, 11.5, SwiftFont.Weight.Medium, TaskLook.Ink2), box },
    };

    private static TextBlock Note(string text) => TaskLook.Text(text, 11.5, SwiftFont.Weight.Regular, TaskLook.Ink2, wrap: true);

    private void Checked()
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(Checked);
            return;
        }
        if (closed) return;
        // Checked as it is typed, and said in words with a mark, not by colour: a number is judged once it looks whole (16
        // digits or more, or the owner moved on), so a half-typed one is not called wrong.
        var problem = entry.Problem(Marrow.Core.Port.SwiftClock.Provider.Current.GetUtcNow());
        var number = entry.Length(CardPart.Number);
        var numberWrong = number >= CardEntry.NumberMin && entry.LastFour is null && (number >= 16 || !Number.IsFocused);
        check.Text = problem switch
        {
            null => "✓ " + PurchaseWords.Checked,
            CardEntryProblem.BadNumber => "! " + PurchaseWords.BadNumber,
            CardEntryProblem.BadMonth or CardEntryProblem.PastExpiry => "! " + PurchaseWords.PastExpiry,
            CardEntryProblem.BadCode => "! " + PurchaseWords.BadCode,
            _ when numberWrong => "! " + PurchaseWords.BadNumber,
            _ => "",
        };
        check.IsVisible = check.Text.Length > 0;
        save.IsEnabled = problem is null && !saving;
    }

    private void DeskChanged()
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(DeskChanged);
            return;
        }
        if (closed) return;
        var asking = desk.State.Asking;
        status.Text = asking is { } what ? PurchaseWords.Asking(what) + ". " + PurchaseWords.HelloDetail : status.Text;
    }

    private async Task Save()
    {
        if (saving || finished) return;
        saving = true;
        save.IsEnabled = false;
        var result = await desk.SaveCard(entry);
        saving = false;
        if (closed)
        {
            entry.Dispose();    // the sheet went while Windows asked: what was typed is zeroed now
            return;
        }
        if (finished) return;
        if (result.Outcome == CardVaultOutcome.Done)
        {
            Finish();
            return;
        }
        status.Text = desk.State.Status ?? "";
        Checked();
    }

    private void Finish()
    {
        if (finished) return;
        finished = true;
        Done();
        returnFocusTo?.Focus();
        Finished?.Invoke();
    }

    private void Close()
    {
        if (finished || saving) return;
        Finish();
    }

    // Whatever way the sheet goes, what was typed is zeroed and the sheet stops listening.
    private void Done()
    {
        if (closed) return;
        closed = true;
        entry.Changed -= Checked;
        desk.Changed -= DeskChanged;
        if (!saving) entry.Dispose();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || e.Handled) return;
        Close();
        e.Handled = true;
    }
}
