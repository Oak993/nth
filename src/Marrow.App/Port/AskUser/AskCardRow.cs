// PORT-ONLY: web surfaces W25, ask_user (DESIGN.md 7.3 "a structured question card ... that blocks the turn for the answer", 7.5 look, 12
// motion; ED-NEW-web-w25-1). The timeline row of an ask_user card. It holds the waiting form while the card waits and the summary once the
// card is closed, and crossfades from one to the other as the card's height eases; the form keeps what the person typed through any change
// to the thread around it. Reduce Motion, or no frame clock (a headless test), swaps at once.
using System.Globalization;
using Avalonia.Controls;
using Marrow.App.Runtime;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.UI.Port;

namespace Marrow.App.Port.AskUser;

public sealed class AskCardRow : Decorator
{
    /// <summary>The card's widest: a question reads best in a column no wider than a paragraph.</summary>
    public const double CardMaxWidth = 560;

    private readonly TimelineEntry entry;
    private readonly ThreadRuntime runtime;
    private readonly CrossfadeSlot slot = new() { ClipToBounds = true };
    private string? shownKey;

    public AskCardRow(TimelineEntry entry, ThreadRuntime runtime)
    {
        this.entry = entry;
        this.runtime = runtime;
        slot.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left; // the slot is as wide as the card on show, at the column's left edge
        Child = slot;
        Refresh(first: true);
        this.Track(() => entry.Item.TimelineItemContent, _ => Refresh(first: false));
    }

    /// <summary>The waiting form on show, or null.</summary>
    internal AskCardForm? Form => slot.Live as AskCardForm;

    /// <summary>The closed summary on show, or null.</summary>
    internal AskCardSummary? Summary => slot.Live as AskCardSummary;

    private void Refresh(bool first)
    {
        if (entry.Item.TimelineItemContent is not TimelineItem.Content.Ask { Value: { Questions.Count: > 0 } card })
        {
            slot.Show(new Border(), null);
            shownKey = null;
            return;
        }
        // The form stays while the card waits, whatever else changes around it; a closed card is drawn once per state.
        var key = card.Id + "/" + card.State;
        if (key == shownKey) return;
        var next = card.IsWaiting ? (Control)new AskCardForm(card, Answer, Skip) : new AskCardSummary(card);
        var animation = first || shownKey is null ? null : Motion();
        shownKey = key;
        slot.Show(next, animation);
    }

    private Animation? Motion() =>
        !FrameClock.IsBound || EnvironmentValues.GetAccessibilityReduceMotion(this) ? null : Animation.Smooth(0.3);

    private AskCheck? Answer(IReadOnlyDictionary<string, IReadOnlyList<string>> given, CultureInfo culture) =>
        entry.Item.TimelineItemContent is TimelineItem.Content.Ask { Value: var card } ? runtime.AnswerAsk(card.Id, given, culture) : null;

    private void Skip()
    {
        if (entry.Item.TimelineItemContent is TimelineItem.Content.Ask { Value: var card }) runtime.SkipAsk(card.Id);
    }
}
