// PORT-ONLY: Dextra AS-8 (DESIGN.md rev 5, 4.2 "Keyboard" (arrow keys move between cards; focus never lands on an irreversible
// button after a card above it closes), "Batching"; the canvas's renderDecisions and dropDecision, work/design/assistant/index.html
// lines 2148-2160; ED-NEW-dextra-as8-6). No Swift twin.
// The Decisions tab: every card that waits on the owner (open or held), oldest first, each a DecisionCard. Up and Down move between
// the cards themselves. When a card closes, focus goes to the next card (or the one above, or the empty line), never to a button.
// Cards that arrived together are introduced as "3 decisions"; each is still answered on its own.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Port;

namespace Marrow.App.Assistant.Views;

/// <summary>Where the Decisions tab reads its cards.</summary>
public interface IDecisionSource
{
    IReadOnlyList<DecisionItem> Waiting { get; }

    string? Answer(Guid id, DecisionAnswer answer);

    event Action? Changed;
}

/// <summary>
/// The decision queue as the tab's source. <paramref name="answer"/> is the door answers go through when it is not the queue's
/// own (the controller's AnswerCard: Pay on a purchase card goes to the purchase desk, AS-20).
/// </summary>
public sealed class QueueDecisions(DecisionQueue queue, Func<Guid, DecisionAnswer, string?>? answer = null) : IDecisionSource
{
    public IReadOnlyList<DecisionItem> Waiting => queue.Waiting;

    public string? Answer(Guid id, DecisionAnswer given) => answer is null ? queue.Answer(id, given) : answer(id, given);

    public event Action? Changed
    {
        add => queue.Changed += value;
        remove => queue.Changed -= value;
    }
}

public sealed class DecisionList : Decorator
{
    /// <summary>Cards raised within this span of each other count as one batch.</summary>
    public static readonly TimeSpan Together = TimeSpan.FromSeconds(10);

    private readonly IDecisionSource source;
    private readonly DecisionContext context;
    private readonly StackPanel list = new() { Orientation = Orientation.Vertical, Spacing = 10 };
    private readonly TextBlock batch = TaskLook.Text("", 11.5, SwiftFont.Weight.Semibold, TaskLook.Ink2);
    private readonly TextBlock empty = TaskLook.Text("Nothing waiting on you.", 12, SwiftFont.Weight.Regular, TaskLook.Ink2);
    private readonly Dictionary<Guid, DecisionCard> cards = [];

    public DecisionList(IDecisionSource source, DecisionContext context)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        empty.Focusable = true;
        Child = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8, Children = { batch, list, empty } };
        AutomationProperties.SetName(this, "Decisions");
        AddHandler(KeyDownEvent, OnKey, Avalonia.Interactivity.RoutingStrategies.Bubble);
    }

    public IReadOnlyList<DecisionCard> Cards => [.. list.Children.OfType<DecisionCard>()];

    public int Count => cards.Count;

    public TextBlock Empty => empty;

    public string BatchText => batch.IsVisible ? batch.Text ?? "" : "";

    public event Action<int>? CountChanged;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        source.Changed += OnChanged;
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        source.Changed -= OnChanged;
    }

    private void OnChanged() => Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);

    public void Refresh()
    {
        var waiting = source.Waiting;
        var keep = waiting.Select(d => d.Id).ToHashSet();
        var focusedCard = FocusedCard();
        var closedIndex = -1;
        foreach (var gone in cards.Keys.Where(k => !keep.Contains(k)).ToList())
        {
            var card = cards[gone];
            if (card == focusedCard) closedIndex = list.Children.IndexOf(card);
            list.Children.Remove(card);
            cards.Remove(gone);
        }
        for (var i = 0; i < waiting.Count; i++)
        {
            var item = waiting[i];
            if (!cards.TryGetValue(item.Id, out var card))
            {
                card = new DecisionCard(item, context with { Answer = source.Answer });
                cards[item.Id] = card;
                list.Children.Insert(Math.Min(i, list.Children.Count), card);
            }
            else card.Show(item);
        }
        empty.IsVisible = cards.Count == 0;
        var latest = waiting.Count > 0 ? waiting.Max(d => d.Raised) : default;
        var together = waiting.Count(d => latest - d.Raised <= Together);
        batch.Text = $"{together} decisions";
        batch.IsVisible = together > 1;
        // focus never lands on a button after a card closes: the next card itself, the one above, or the empty line
        if (closedIndex >= 0)
        {
            var next = Cards.ElementAtOrDefault(closedIndex) ?? Cards.ElementAtOrDefault(closedIndex - 1);
            if (next is not null) next.Target.Focus(NavigationMethod.Tab);
            else empty.Focus(NavigationMethod.Tab);
        }
        CountChanged?.Invoke(cards.Count);
    }

    /// <summary>
    /// Brings one card in front (a toast's Review, AS-9): the list is refreshed, and the card scrolls into view and takes focus once laid out. Focus
    /// goes to the card itself, never to one of its buttons (4.2 "Keyboard"). False when no card waits with that id.
    /// </summary>
    public bool Reveal(Guid id)
    {
        Refresh();
        if (!cards.TryGetValue(id, out var card)) return false;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            card.Target.BringIntoView();
            card.Target.Focus(NavigationMethod.Tab);
        }, Avalonia.Threading.DispatcherPriority.Loaded);
        return true;
    }

    private DecisionCard? FocusedCard()
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual;
        return focused?.FindAncestorOfType<DecisionCard>(includeSelf: true);
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Up or Key.Down)) return;
        var at = FocusedCard();
        if (at is null) return;
        var all = Cards;
        var i = all.ToList().IndexOf(at) + (e.Key == Key.Down ? 1 : -1);
        if (i < 0 || i >= all.Count) return;
        e.Handled = true;
        all[i].Target.Focus(NavigationMethod.Directional);
    }
}
