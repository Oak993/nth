// PORT-ONLY: Dextra AS-8 (DESIGN.md rev 5, 4.2 "Card anatomy" (exact inputs, why, who, which rule; answers per kind; Always...
// opens a structured grant, never a one-tap Allow always; keyboard; held cards), 12.2 "Grants", 12.6b "Cards while paused"; the
// canvas's dCard, answer and holdTx, work/design/assistant/index.html lines 2133-2215, and its .dc CSS; ED-NEW-dextra-as8-6).
// No Swift twin.
// One card of the decision queue. It states what will happen with the exact inputs, why it is asked, who asks (assistant,
// mission, agent) and the rule that sent it. Its answers follow its kind. Irreversible buttons (Do it, Merge, Discard) are
// HoldToActButtons: a click or a one-second Space hold acts, Enter never does, and none is ever given focus. Number keys pick an
// answer on reversible cards only. A held card says what holds it and its buttons are off. An answer kept while its task is
// paused says so. Always... swaps the body for a structured grant (who, may, where, not, until), pre-filled from the card.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Armilla;

namespace Marrow.App.Assistant.Views;

/// <summary>What a card needs from outside: names, the answer path and the clock.</summary>
public sealed record DecisionContext(Func<Guid, DecisionAnswer, string?> Answer, Func<string, string>? AssistantName = null,
    Func<Guid, string?>? MissionTitle = null, TimeProvider? Clock = null)
{
    public string NameOf(string assistantId) => AssistantName?.Invoke(assistantId) is { Length: > 0 } name ? name : AssistantNames.Assistant;
}

public static class DecisionWords
{
    /// <summary>The words of one answer button.</summary>
    public static string Label(DecisionAnswerKind kind, DecisionItem item) => kind switch
    {
        DecisionAnswerKind.AllowOnce => "Allow once",
        DecisionAnswerKind.ForThisMission => "For this mission",
        DecisionAnswerKind.Always => "Always…",
        DecisionAnswerKind.Deny => "Deny",
        DecisionAnswerKind.DoIt => Verb(item),
        DecisionAnswerKind.Dont => "Don't " + Verb(item).ToLowerInvariant(),
        DecisionAnswerKind.Discuss => "Discuss",
        DecisionAnswerKind.Merge => "Merge",
        DecisionAnswerKind.RebaseFirst => "Rebase first",
        DecisionAnswerKind.Discard => "Discard",
        _ => kind.ToString(),
    };

    /// <summary>The verb of an irreversible card's Do it (4.2 "Do it (Send / Pay / Delete)").</summary>
    public static string Verb(DecisionItem item) => item.Class switch
    {
        ActionClass.Outward or ActionClass.PersonalDataOut => "Send",
        ActionClass.Money => "Pay",
        ActionClass.Destructive => "Delete",
        ActionClass.Install => "Install",
        ActionClass.Commitment => "Commit",
        _ => "Do it",
    };

    /// <summary>The rule line ("which rule sent it to the owner"): the card's own, else its class's.</summary>
    public static string Rule(DecisionItem item) => item.Rule.Length > 0 ? item.Rule : item.Class is { } c ? ActionClasses.RuleText(c) : item.Why;

    /// <summary>"Held by Stop. ..." (the canvas's holdTx).</summary>
    public static string Held(DecisionItem item) =>
        (item.HeldBy == DecisionHold.Pause ? "Held by Pause. " : "Held by Stop. ")
        + (item.Weight == DecisionWeight.Irreversible || item.Kind == DecisionKind.Merge
            ? "On Resume it comes back for a new tap, its inputs checked again."
            : "Re-raised on Resume, bound to the same inputs.");

    public static string Kept(DecisionItem item, string? mission) =>
        $"Answer kept: “{(item.Kept is { } k ? KeptLabel(k, item) : "")}”. It acts when you resume {(mission is { Length: > 0 } m ? "“" + m + "”" : "its task")}.";

    private static string KeptLabel(DecisionAnswer answer, DecisionItem item) =>
        answer.Kind == DecisionAnswerKind.Pick && answer.Text is { } t ? t : Label(answer.Kind, item);

    /// <summary>What a grant may do, in words (12.2's "allowed fields").</summary>
    public static string May(ActionClass c) => c switch
    {
        ActionClass.Login => "Sign in",
        ActionClass.WriteElsewhere => "Write files there",
        ActionClass.ConnectorWrite => "Write through the connector",
        _ => ActionClasses.RuleText(c),
    };

    public static string Where(GrantTarget target) => target.Kind switch
    {
        GrantTargetKind.Recipients => "To " + ControlWords.AndList(target.Values),
        _ => ControlWords.AndList(target.Values),
    };
}

public sealed class DecisionCard : Decorator
{
    private readonly Border card = new() { BorderThickness = new Thickness(1), Padding = new Thickness(12), Focusable = true };
    private readonly StackPanel body = new() { Orientation = Orientation.Vertical, Spacing = 8 };
    private readonly WrapPanel answers = new() { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6 };
    private readonly LiveStatusText status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly DecisionContext context;
    private readonly IBrush line1, line2;
    private StackPanel? grantPanel;
    private RevealBox? discuss;

    public DecisionCard(DecisionItem item, DecisionContext context)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        Item = item ?? throw new ArgumentNullException(nameof(item));
        card.RoundedRectangle(14, RoundedCornerStyle.Continuous);
        card.Background = Chrome.Overlay(.05).Brush(card);
        line1 = TaskLook.Line.Brush(card);
        line2 = TaskLook.Line2.Brush(card);
        card.Child = body;
        Child = card;
        SwiftFont.System(11.5, SwiftFont.Weight.Medium).Apply(status);
        status.Foreground = Chrome.PrimaryText.Brush(status);
        AutomationProperties.SetControlTypeOverride(card, AutomationControlType.Group);
        KeyboardNavigation.SetIsTabStop(card, true);
        card.KeyDown += OnKey;
        Build();
    }

    public DecisionItem Item { get; private set; }

    /// <summary>The focusable card itself (arrow keys move between these).</summary>
    public Control Target => card;

    public IReadOnlyList<Button> AnswerButtons => [.. answers.Children.OfType<Button>()];

    public string StatusText => status.Text ?? "";

    public bool ShowsGrant => grantPanel is not null;

    /// <summary>The card closed (answered), or an answer was refused (the text says why).</summary>
    public event Action<DecisionCard, string?>? Answered;

    public void Show(DecisionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var same = item with { State = Item.State, HeldBy = Item.HeldBy, Kept = Item.Kept, Note = Item.Note } == Item;
        Item = item;
        if (!same && grantPanel is null) Build();
        else PaintState();
    }

    private void Build()
    {
        body.Children.Clear();
        answers.Children.Clear();
        grantPanel = null;
        discuss = null;
        var item = Item;
        var who = context.NameOf(item.AssistantId);
        var mission = item.MissionId is { } m ? context.MissionTitle?.Invoke(m) : null;

        var mark = new ArmillaMark { MarkSize = 24, Kind = ArmillaKind.Row, State = ArmillaState.NeedsYou, VerticalAlignment = VerticalAlignment.Top };
        AutomationProperties.SetAccessibilityView(mark, AccessibilityView.Raw);
        var title = TaskLook.Text(item.Title, 13, SwiftFont.Weight.Semibold, TaskLook.Ink, wrap: true);
        var asking = string.Join(" · ", new[] { who, mission is null ? null : "“" + mission + "”", item.Agent is { Length: > 0 } a ? a : null }.Where(s => s is not null));
        var whoLine = TaskLook.Text(asking, 11, SwiftFont.Weight.Regular, TaskLook.Ink2, wrap: true);
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 9 };
        var names = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2, Children = { title, whoLine } };
        Grid.SetColumn(names, 1);
        head.Children.Add(mark);
        head.Children.Add(names);
        body.Children.Add(head);

        // why it is asked and which rule sent it
        var why = TaskLook.Text(DecisionWords.Rule(item), 11, SwiftFont.Weight.Regular, TaskLook.Ink2, wrap: true);
        var whyBox = new Border { Padding = new Thickness(8, 3), Child = why, HorizontalAlignment = HorizontalAlignment.Left };
        whyBox.RoundedRectangle(8, RoundedCornerStyle.Continuous);
        whyBox.Background = Chrome.Overlay(.06).Brush(whyBox);
        body.Children.Add(whyBox);
        if (item.Why.Length > 0 && item.Why != DecisionWords.Rule(item)) body.Children.Add(TaskLook.Text(item.Why, 12.5, SwiftFont.Weight.Regular, TaskLook.Ink2, wrap: true));

        // the exact inputs: the full message, every recipient, the amount, the command
        foreach (var input in item.Inputs)
        {
            var name = TaskLook.Text(input.Name, 11, SwiftFont.Weight.Medium, TaskLook.Ink2);
            var value = TaskLook.Text(input.Value, 12.5, SwiftFont.Weight.Regular, TaskLook.Ink, wrap: true);
            var quote = new Border { Padding = new Thickness(10, 8), Child = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2, Children = { name, value } } };
            quote.RoundedRectangle(10, RoundedCornerStyle.Continuous);
            quote.Background = Chrome.Overlay(.06).Brush(quote);
            body.Children.Add(quote);
        }

        // the answers, by kind (4.2's table)
        var number = 0;
        foreach (var kind in item.Answers)
        {
            if (kind == DecisionAnswerKind.Pick)
            {
                foreach (var option in item.Options) answers.Children.Add(Answer(DecisionAnswer.Pick(option), option, primary: number == 0, ++number));
                continue;
            }
            var primary = kind is DecisionAnswerKind.AllowOnce or DecisionAnswerKind.DoIt or DecisionAnswerKind.Merge;
            answers.Children.Add(kind switch
            {
                DecisionAnswerKind.Always => Plain(DecisionWords.Label(kind, item), OpenGrant, ++number),
                DecisionAnswerKind.Discuss => Plain(DecisionWords.Label(kind, item), OpenDiscuss, ++number),
                DecisionAnswerKind.Deny => Answer(DecisionAnswer.Deny(), DecisionWords.Label(kind, item), false, ++number),
                DecisionAnswerKind.Dont => Answer(DecisionAnswer.Dont(), DecisionWords.Label(kind, item), false, ++number),
                _ => Answer(new DecisionAnswer(kind), DecisionWords.Label(kind, item), primary, ++number),
            });
        }
        body.Children.Add(answers);
        body.Children.Add(status);
        if (item.Weight == DecisionWeight.Irreversible || item.Kind == DecisionKind.Merge)
            body.Children.Add(TaskLook.Text("Bound to these exact inputs. " + HoldToActButton.EnterHint, 11, SwiftFont.Weight.Regular, TaskLook.Ink2, wrap: true));
        AutomationProperties.SetName(card, item.Title);
        AutomationProperties.SetHelpText(card, DecisionWords.Rule(item));
        PaintState();
    }

    private Control Answer(DecisionAnswer answer, string label, bool primary, int number)
    {
        if (DecisionRules.IsIrreversibleButton(answer.Kind))
        {
            var face = Face(label, primary);
            var hold = new HoldToActButton(face.Face, label) { Focusable = true };
            hold.KeyboardFocus(13);
            hold.Acted += () => Send(answer);
            hold.PointerEntered += (_, _) => face.Hot(true);
            hold.PointerExited += (_, _) => face.Hot(false);
            return hold;
        }
        var (button, shown) = TaskLook.Small(label, () => Send(answer), primary);
        button.Tag = number;
        return shown;
    }

    private Control Plain(string label, Action action, int number)
    {
        var (button, shown) = TaskLook.Small(label, action);
        button.Tag = number;
        return shown;
    }

    private static (Border Face, Action<bool> Hot) Face(string label, bool primary)
    {
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        SwiftFont.System(12, SwiftFont.Weight.Medium).Apply(text);
        var face = new Border { Padding = new Thickness(10, 0), Height = 26, Child = text };
        face.Capsule(RoundedCornerStyle.Continuous);
        var rest = primary ? Chrome.PrimaryText.Brush(face) : Chrome.Overlay(.06).Brush(face);
        var hot = primary ? Chrome.PrimaryText.Opacity(.86).Brush(face) : Chrome.Overlay(.1).Brush(face);
        text.Foreground = primary
            ? SwiftColor.Dynamic(new SwiftColor(red: .97, green: .97, blue: .98), new SwiftColor(red: .08, green: .08, blue: .09)).Brush(text)
            : Chrome.PrimaryText.Brush(text);
        face.Background = rest;
        return (face, on => face.Background = on ? hot : rest);
    }

    private void Send(DecisionAnswer answer)
    {
        var refused = context.Answer(Item.Id, answer);
        if (refused is not null) status.Text = refused;
        Answered?.Invoke(this, refused);
    }

    private void PaintState()
    {
        var held = Item.State == DecisionState.Held;
        foreach (var control in answers.Children) control.IsEnabled = !held;
        answers.Opacity = held ? .45 : 1;
        card.BorderBrush = held ? line1 : line2;
        status.Text = held ? DecisionWords.Held(Item)
            : Item.Kept is not null ? DecisionWords.Kept(Item, Item.MissionId is { } m ? context.MissionTitle?.Invoke(m) : null)
            : Item.Note ?? "";
        status.IsVisible = status.Text.Length > 0;
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        // number keys pick an answer on reversible cards only; an irreversible card takes no key at all
        if (Item.Weight == DecisionWeight.Irreversible || Item.Kind == DecisionKind.Merge || Item.State != DecisionState.Open || grantPanel is not null) return;
        var n = e.Key switch
        {
            >= Key.D1 and <= Key.D9 => e.Key - Key.D1 + 1,
            >= Key.NumPad1 and <= Key.NumPad9 => e.Key - Key.NumPad1 + 1,
            _ => 0,
        };
        if (n == 0) return;
        var target = answers.Children.OfType<Button>().FirstOrDefault(b => b.Tag is int i && i == n);
        if (target is null) return;
        e.Handled = true;
        target.Focus(NavigationMethod.Tab);
        target.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    }

    // ---- Always...: a structured grant, pre-filled from the card ----

    private void OpenGrant()
    {
        var item = Item;
        if (item.Class is not { } actionClass || item.GrantTarget is not { } target) { status.Text = "This card cannot be granted."; status.IsVisible = true; return; }
        var clock = context.Clock ?? TimeProvider.System;
        var scope = GrantScope.Standing;
        var rows = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12, RowSpacing = 6 };
        void Row(string label, string value)
        {
            var r = rows.RowDefinitions.Count;
            rows.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var l = TaskLook.Text(label, 11.5, SwiftFont.Weight.Regular, TaskLook.Ink2);
            var v = TaskLook.Text(value, 12, SwiftFont.Weight.Semibold, TaskLook.Ink, wrap: true);
            Grid.SetRow(l, r);
            Grid.SetRow(v, r);
            Grid.SetColumn(v, 1);
            rows.Children.Add(l);
            rows.Children.Add(v);
        }
        Row("Who", context.NameOf(item.AssistantId));
        Row("May", DecisionWords.May(actionClass));
        Row("Where", DecisionWords.Where(target));
        Row("Not", "Sending, paying or deleting: no grant can cover them");
        var choices = new List<(string Label, Func<GrantScope> Make)> { ("Until you revoke it", () => GrantScope.Standing), ("For 7 days", () => GrantScope.UntilDate(clock.GetUtcNow().AddDays(7))) };
        if (item.MissionId is { } mission) choices.Add(("This mission only", () => GrantScope.ForMission(mission)));
        var picked = 0;
        var until = new ChromeSegmentedPicker<int>(choices.Select((c, i) => (i, c.Label)).ToArray(), new Binding<int>(() => picked, v => picked = v));
        AutomationProperties.SetName(until, "Until");
        var untilRow = rows.RowDefinitions.Count;
        rows.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var untilLabel = TaskLook.Text("Until", 11.5, SwiftFont.Weight.Regular, TaskLook.Ink2);
        untilLabel.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetRow(untilLabel, untilRow);
        Grid.SetRow(until, untilRow);
        Grid.SetColumn(until, 1);
        rows.Children.Add(untilLabel);
        rows.Children.Add(until);

        var (save, saveShown) = TaskLook.Small("Save grant", () =>
        {
            scope = choices[Math.Clamp(picked, 0, choices.Count - 1)].Make();
            var grant = new Grant(Guid.NewGuid(), item.AssistantId, actionClass, target, scope) { FromDecision = item.Id, Created = clock.GetUtcNow() };
            var refused = context.Answer(item.Id, DecisionAnswer.Always(grant));
            if (refused is not null) { status.Text = refused; status.IsVisible = true; }
            Answered?.Invoke(this, refused);
        }, primary: true);
        var (back, backShown) = TaskLook.Small("Back", () => { Build(); card.Focus(NavigationMethod.Tab); });
        grantPanel = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10, Children = { rows, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { saveShown, backShown } } } };
        AutomationProperties.SetName(grantPanel, "Grant");
        AutomationProperties.SetControlTypeOverride(grantPanel, AutomationControlType.Group);
        var keep = body.Children.Take(2).ToList();
        body.Children.Clear();
        foreach (var c in keep) body.Children.Add(c);
        body.Children.Add(grantPanel);
        body.Children.Add(status);
        save.Focus(NavigationMethod.Tab);
        _ = back;
    }

    private void OpenDiscuss()
    {
        if (discuss is not null) { discuss.Open(!discuss.IsOpen, TaskLook.Still(this)); return; }
        var text = new TextBox { PlaceholderText = "What to discuss", AcceptsReturn = false };
        SwiftFont.System(12).Apply(text);
        AutomationProperties.SetName(text, "What to discuss");
        void Go()
        {
            var said = text.Text?.Trim() ?? "";
            if (said.Length == 0) return;
            Send(DecisionAnswer.Discuss(said));
        }
        text.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; Go(); }
            else if (e.Key == Key.Escape) { e.Handled = true; discuss?.Open(false, TaskLook.Still(this)); card.Focus(NavigationMethod.Tab); }
        };
        var (send, sendShown) = TaskLook.Small("Send", Go, primary: true);
        _ = send;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
        Grid.SetColumn(sendShown, 1);
        row.Children.Add(text);
        row.Children.Add(sendShown);
        discuss = new RevealBox { Child = row };
        body.Children.Insert(body.Children.IndexOf(answers) + 1, discuss);
        discuss.Open(true, TaskLook.Still(this));
        text.Focus(NavigationMethod.Tab);
    }
}
