// PORT-ONLY: Dextra AS-20 (DESIGN.md rev 5, 12.7 "The switch", "The card", "Limits", "History", "Never allowed"; 16.2's fallback;
// the canvas's section 9 "Purchases", work/design/assistant/index.html lines 948-993; ED-NEW-dextra-as20-13). No Swift twin.
// Settings › Assistant › Purchases. With a fill path: "Let Dextra buy things" (off until a card, valid limits and Windows Hello),
// the card as "•••• 4242" with Add card, Replace and Remove, the three limits and each Astur's, the virtual-card advice, the
// history and the never-allowed line. Without one (this build, 16.2): what Dextra does instead (it fills the cart and hands the
// payment over), why, and the history; the switch is not shown. What Windows Hello is asking, and how it went, is said in a live
// line under it all. Nothing here ever shows more of the card than its last 4 digits, and nothing here can reveal it.
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Views.Settings;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Assistant.Views;

public sealed class PurchaseSettingsSection : Decorator
{
    /// <summary>The history lists this many rows, newest first; the rest are counted.</summary>
    public const int HistoryShown = 50;

    private readonly Func<PurchaseDesk?> source;
    private readonly Func<Guid, string?>? missionTitle;
    private readonly StackPanel body = new() { Orientation = Orientation.Vertical, Spacing = Chrome.SectionHeaderSpacing };
    private readonly LiveStatusText status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Dictionary<PurchaseLimitKind, TextBox> limitBoxes = [];
    private readonly List<(AssistantProfile Astur, TextBox Box)> asturBoxes = [];
    private readonly List<string> historyLines = [];
    private readonly StackPanel history = new() { Orientation = Orientation.Vertical };
    private readonly StackPanel asturRows = new() { Orientation = Orientation.Vertical };
    private readonly StackPanel cardButtons = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    private readonly StackPanel sheetHost = new() { Orientation = Orientation.Vertical };
    private PurchaseDesk? desk;
    private string mode = "";
    private string cardKey = "", asturKey = "", historyKey = "";
    private TextBlock? switchDetail, cardTitle;
    private int queued;
    private bool watching;

    /// <param name="source">The running desk, read at every redraw (Dextra may start or stop while Settings is open).</param>
    public PurchaseSettingsSection(Func<PurchaseDesk?> source, Func<Guid, string?>? missionTitle = null)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        this.missionTitle = missionTitle;
        SwiftFont.System(SwiftFont.TextStyle.Footnote).Apply(status);
        status.Foreground = Chrome.PrimaryText.Brush(status);
        status.Margin = new Thickness(4, 0);
        Child = new ChromeSection(PurchaseWords.Section, () => body);
        AttachedToVisualTree += (_, _) => Watch(true);
        DetachedFromVisualTree += (_, _) => Watch(false);
        Show();
    }

    public SettingsSwitch? Switch { get; private set; }

    public Button? AddButton { get; private set; }

    public Button? ReplaceButton { get; private set; }

    public Button? RemoveButton { get; private set; }

    public CardEntrySheet? Sheet { get; private set; }

    public IReadOnlyDictionary<PurchaseLimitKind, TextBox> LimitBoxes => limitBoxes;

    public IReadOnlyList<(AssistantProfile Astur, TextBox Box)> AsturBoxes => asturBoxes;

    public IReadOnlyList<string> HistoryLines => historyLines;

    public string StatusText => status.IsVisible ? status.Text ?? "" : "";

    /// <summary>Re-reads the desk and redraws what changed.</summary>
    public void Show()
    {
        var now = source();
        if (!ReferenceEquals(now, desk))
        {
            if (watching && desk is not null) desk.Changed -= Queue;
            desk = now;
            if (watching && desk is not null) desk.Changed += Queue;
        }
        var state = desk?.State;
        var next = state is null ? "none" : !state.CanPay ? "hand-over" : state.ReadOnly ? "read-only" : "full";
        if (next != mode)
        {
            mode = next;
            Build(state);
        }
        if (state is not null) Update(state);
    }

    private void Watch(bool on)
    {
        if (on == watching) return;
        watching = on;
        if (desk is not null)
        {
            if (on) desk.Changed += Queue;
            else desk.Changed -= Queue;
        }
        if (on) Show();
    }

    // Changes come from any thread (a Windows Hello answer, a purchase); the section redraws once on the UI thread.
    private void Queue()
    {
        if (Interlocked.Exchange(ref queued, 1) == 1) return;
        Dispatcher.UIThread.Post(() =>
        {
            Volatile.Write(ref queued, 0);
            if (watching) Show();
        });
    }

    // ---- the four ways it looks ----

    private void Build(PurchaseDeskState? state)
    {
        body.Children.Clear();
        limitBoxes.Clear();
        asturBoxes.Clear();
        Switch = null;
        AddButton = ReplaceButton = RemoveButton = null;
        switchDetail = cardTitle = null;
        cardKey = asturKey = historyKey = "";
        CloseSheet(focusBack: false);
        if (state is null)
        {
            body.Children.Add(new ChromeCard(() => Line(PurchaseWords.NotRunning, secondary: true)));
            return;
        }
        if (!state.CanPay)
        {
            // 16.2: no fill path in this build. Dextra fills the cart and hands the payment over; the switch is not offered.
            body.Children.Add(new ChromeCard(() => new StackPanel
            {
                Children = { Line(PurchaseWords.HandOverLead, secondary: false), Line(PurchaseWords.HandOverWhy, secondary: true) },
            }));
            AddHistory();
            body.Children.Add(Note(PurchaseWords.NeverHint));
            body.Children.Add(status);
            return;
        }
        if (state.ReadOnly)
        {
            body.Children.Add(new ChromeCard(() => Line(state.Unreadable ? PurchaseWords.UnreadableFile : PurchaseWords.NewerFile, secondary: false)));
            AddHistory();
            body.Children.Add(status);
            return;
        }

        var switchControl = new SettingsSwitch(new Binding<bool>(() => desk?.State.Policy.On ?? false, Toggle));
        AutomationProperties.SetName(switchControl, PurchaseWords.Switch);
        Switch = switchControl;
        switchDetail = DetailText("");
        cardTitle = TitleText("");
        var main = new StackPanel
        {
            Children =
            {
                Row(TitleText(PurchaseWords.Switch), switchDetail, switchControl),
                new ChromeRowDivider(),
                Row(cardTitle, DetailText(PurchaseWords.OnlyLastFour + ". " + PurchaseWords.Kept), cardButtons),
                sheetHost,
            },
        };
        body.Children.Add(new ChromeCard(() => main));
        body.Children.Add(Note(PurchaseWords.Help));

        var limits = new StackPanel();
        foreach (var kind in new[] { PurchaseLimitKind.PerPurchase, PurchaseLimitKind.PerDay, PurchaseLimitKind.PerMonth })
        {
            if (limits.Children.Count > 0) limits.Children.Add(new ChromeRowDivider());
            var box = MoneyBox(PurchaseWords.LimitName(kind) + ", in " + state.Policy.Currency, value => desk!.SetLimit(kind, value));
            limitBoxes[kind] = box;
            limits.Children.Add(Row(TitleText(kind switch { PurchaseLimitKind.PerPurchase => PurchaseWords.PerPurchase, PurchaseLimitKind.PerDay => PurchaseWords.PerDay, _ => PurchaseWords.PerMonth }), null, Boxed(box, state.Policy.Currency)));
        }
        body.Children.Add(new ChromeCard(() => limits));
        body.Children.Add(Note(PurchaseWords.LimitsNote));
        body.Children.Add(Heading(PurchaseWords.AsturLimits));
        body.Children.Add(new ChromeCard(() => asturRows));
        body.Children.Add(Note(PurchaseWords.AsturNote));
        AddHistory();
        body.Children.Add(Note(PurchaseWords.NeverHint));
        body.Children.Add(status);
    }

    private void AddHistory()
    {
        body.Children.Add(Heading(PurchaseWords.History));
        body.Children.Add(new ChromeCard(() => history));
    }

    private void Update(PurchaseDeskState state)
    {
        var hasCard = state.Card == CardVaultState.Held;
        if (switchDetail is not null) switchDetail.Text = PurchaseWords.SwitchDetail(hasCard, state.Policy.On);
        if (cardTitle is not null)
        {
            cardTitle.Text = hasCard ? PurchaseWords.LastFour(state.LastFour)
                : state.Card == CardVaultState.NoCard ? PurchaseWords.NoCardYet
                : PurchaseWords.CardDamaged;
            var key = state.Card.ToString() + state.LastFour;
            if (key != cardKey)
            {
                cardKey = key;
                CardButtons(state);
            }
        }
        foreach (var (kind, box) in limitBoxes)
            if (!box.IsFocused) box.Text = Amount(state.Policy.Limits.Of(kind));
        if (mode == "full") AsturRows(state);
        HistoryRows(state);
        status.Text = state.Asking is { } what ? PurchaseWords.Asking(what) + ". " + PurchaseWords.HelloDetail : state.Status ?? "";
        status.IsVisible = status.Text.Length > 0;
    }

    private void CardButtons(PurchaseDeskState state)
    {
        cardButtons.Children.Clear();
        AddButton = ReplaceButton = RemoveButton = null;
        if (state.Card == CardVaultState.NoCard)
        {
            var (add, shown) = TaskLook.Small(PurchaseWords.AddCard, () => OpenSheet(AddButton), primary: true);
            AddButton = add;
            cardButtons.Children.Add(shown);
            return;
        }
        var (replace, replaceShown) = TaskLook.Small(PurchaseWords.Replace, () => OpenSheet(ReplaceButton), name: "Replace the card");
        var (remove, removeShown) = TaskLook.Small(PurchaseWords.Remove, () => _ = Remove(), name: "Remove the card");
        ReplaceButton = replace;
        RemoveButton = remove;
        cardButtons.Children.Add(replaceShown);
        cardButtons.Children.Add(removeShown);
    }

    private void AsturRows(PurchaseDeskState state)
    {
        var asturs = desk?.Asturs ?? [];
        var key = string.Join("|", asturs.Select(a => a.Id + ":" + a.Name)) + "#" + state.Policy.Currency;
        if (key != asturKey)
        {
            asturKey = key;
            asturRows.Children.Clear();
            asturBoxes.Clear();
            if (asturs.Count == 0) asturRows.Children.Add(Line(PurchaseWords.NoAsturs, secondary: true));
            foreach (var astur in asturs)
            {
                if (asturRows.Children.Count > 0) asturRows.Children.Add(new ChromeRowDivider());
                var id = astur.Id;
                var box = MoneyBox(PurchaseWords.LimitName(PurchaseLimitKind.PerAstur, astur.Name) + ", in " + state.Policy.Currency, value => desk!.SetLimit(PurchaseLimitKind.PerAstur, value, id));
                asturBoxes.Add((astur, box));
                asturRows.Children.Add(Row(TitleText(astur.Name), null, Boxed(box, state.Policy.Currency)));
            }
        }
        foreach (var (astur, box) in asturBoxes)
            if (!box.IsFocused) box.Text = Amount(state.Policy.Limits.AsturLimit(astur.Id));
    }

    private void HistoryRows(PurchaseDeskState state)
    {
        var key = state.History.Count.ToString(CultureInfo.InvariantCulture) + string.Concat(state.History.Take(HistoryShown).Select(r => r.Id.ToString("N")[..8] + (int)r.State));
        if (key == historyKey) return;
        historyKey = key;
        history.Children.Clear();
        historyLines.Clear();
        if (state.History.Count == 0)
        {
            history.Children.Add(new StackPanel { Children = { Line(PurchaseWords.NoHistory, secondary: false), Line(PurchaseWords.NoHistoryDetail, secondary: true) } });
            return;
        }
        foreach (var row in state.History.Take(HistoryShown))
        {
            if (history.Children.Count > 0) history.Children.Add(new ChromeRowDivider());
            var line = HistoryLine(row, missionTitle);
            historyLines.Add(line);
            history.Children.Add(Line(line, secondary: false));
        }
        if (state.History.Count > HistoryShown)
            history.Children.Add(Line($"and {(state.History.Count - HistoryShown).ToString(CultureInfo.InvariantCulture)} more", secondary: true));
    }

    /// <summary>One history row: the shop, the amount, the date and the mission. Never card data.</summary>
    public static string HistoryLine(PurchaseRow row, Func<Guid, string?>? missionTitle = null)
    {
        ArgumentNullException.ThrowIfNull(row);
        var when = TimeZoneInfo.ConvertTime(row.At, TimeZoneInfo.Local).ToString("d MMM yyyy", CultureInfo.InvariantCulture);
        var mission = row.Mission is { } m && missionTitle?.Invoke(m) is { Length: > 0 } title ? " · “" + title + "”" : "";
        var state = row.State switch
        {
            PurchaseRowState.NotPaid => " · not paid",
            PurchaseRowState.Unknown => " · check with the shop",
            PurchaseRowState.Paying => " · paying",
            _ => "",
        };
        return $"{row.Shop} · {PurchaseMoney.Format(row.Amount, row.Currency)} · {when}{mission}{state}";
    }

    // ---- what the owner does ----

    private void Toggle(bool on)
    {
        if (desk is not { } d) return;
        if (on) _ = d.TurnOn();
        else d.TurnOff();
    }

    private async Task Remove()
    {
        if (desk is not { } d) return;
        await d.RemoveCard();
        Show();
        (AddButton ?? (Control?)ReplaceButton)?.Focus();
    }

    private void OpenSheet(Control? opener)
    {
        if (desk is not { } d || Sheet is not null) return;
        var sheet = new CardEntrySheet(d, opener);
        sheet.Finished += () =>
        {
            CloseSheet(focusBack: false);
            Show();
            (ReplaceButton ?? (Control?)AddButton)?.Focus();    // the card's own button: Replace once one is saved
        };
        Sheet = sheet;
        sheetHost.Children.Add(sheet);
        sheet.Number.Focus(NavigationMethod.Tab);
    }

    private void CloseSheet(bool focusBack)
    {
        if (Sheet is not { } sheet) return;
        Sheet = null;
        sheetHost.Children.Remove(sheet);
        if (focusBack) (AddButton ?? (Control?)ReplaceButton)?.Focus();
    }

    // A money box commits on Enter or when the focus leaves it; a limit that is not taken shows the one that holds.
    private TextBox MoneyBox(string name, Func<decimal, Task<string?>> commit)
    {
        var box = new TextBox { Width = 92, AcceptsReturn = false, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        SwiftFont.System(13).Apply(box);
        AutomationProperties.SetName(box, name);
        var busy = false;
        async Task Commit()
        {
            if (busy || desk is null) return;
            var typed = (box.Text ?? "").Trim().TrimStart('$', '€', '£').Trim();
            if (!decimal.TryParse(typed, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            {
                status.Text = PurchaseWords.LimitTyped;
                status.IsVisible = true;
                return;
            }
            busy = true;
            await commit(value);
            busy = false;
            Show();
        }
        box.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            _ = Commit();
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        box.LostFocus += (_, _) => _ = Commit();
        return box;
    }

    private static Control Boxed(TextBox box, string currency)
    {
        var sign = TaskLook.Text(PurchaseMoney.Format(0m, currency).Replace("0.00", "", StringComparison.Ordinal).Trim(), 13, SwiftFont.Weight.Regular, TaskLook.Ink2);
        sign.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetAccessibilityView(sign, AccessibilityView.Raw);
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { sign, box } };
    }

    private static string Amount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    // ---- small pieces ----

    private static Control Row(TextBlock title, TextBlock? detail, Control control)
    {
        var text = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2, Children = { title } };
        if (detail is not null) text.Children.Add(detail);
        control.HorizontalAlignment = HorizontalAlignment.Right;
        control.VerticalAlignment = VerticalAlignment.Center;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,Auto") };
        Grid.SetColumn(control, 2);
        row.Children.Add(text);
        row.Children.Add(control);
        return new Border { Padding = new Thickness(16, 11, Chrome.RowControlTrailingPadding, 11), Child = row };
    }

    private static TextBlock TitleText(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        SwiftFont.System(SwiftFont.TextStyle.Body).Apply(block);
        block.Foreground = Chrome.PrimaryText.Brush(block);
        return block;
    }

    private static TextBlock DetailText(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        SwiftFont.System(SwiftFont.TextStyle.Subheadline).Apply(block);
        block.Foreground = Chrome.SecondaryText.Brush(block);
        return block;
    }

    private static Control Line(string text, bool secondary)
    {
        var block = secondary ? DetailText(text) : TitleText(text);
        return new Border { Padding = new Thickness(16, 11), Child = block };
    }

    private static TextBlock Note(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0) };
        SwiftFont.System(SwiftFont.TextStyle.Footnote).Apply(block);
        block.Foreground = Chrome.SecondaryText.Brush(block);
        return block;
    }

    private static TextBlock Heading(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        SwiftFont.System(SwiftFont.TextStyle.Body, weight: SwiftFont.Weight.Semibold).Apply(block);
        block.Foreground = Chrome.SecondaryText.Brush(block);
        return block;
    }
}
