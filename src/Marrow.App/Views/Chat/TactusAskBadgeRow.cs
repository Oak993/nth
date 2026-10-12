// PORT-ONLY: Tactus, an ask in the chat (OD-0145, ED-NEW-pcctl-27; DESIGN.md 10.4 "Resolution", 10.7). No Swift twin. A
// Tactus approval, confirm or show-window ask waiting in this chat is a badge in the timeline, the existing pattern
// (ChatBadge with needsAttention, as QuestionBadgeRow): "Needs your OK. Outlook" with "to answer". Its popover holds the
// same card body as the pill's, with the same shield and keys; a choice there resolves the same broker entry, answered from
// the chat, so the first answer in either view wins and the other closes. The answer row then takes the badge's place.
using Avalonia;
using Avalonia.Controls;
using Marrow.App.Port.PcControl;
using Marrow.Core.Port;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.Services.Port.PcControl;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.PcControl;

namespace Marrow.App.Views.Chat;

public sealed class TactusAskBadgeRow : Decorator
{
    public TactusAskBadgeRow(PcPendingAsk ask, Func<TactusChatBridge?>? source = null)
    {
        var bridge = source ?? (() => TactusChatBridge.Current);
        var isShowing = false;
        Ask = ask;
        Title = AskLifetimes.Title(ask);
        Child = new ChatBadge(
            Title,
            new Binding<bool>(() => isShowing, value => isShowing = value),
            () => SwiftImage.SystemName("hand.raised").ForegroundStyle(Chrome.Accent),
            () => Sheet(ask, bridge(), () => isShowing = false),
            caption: "to answer",
            needsAttention: true);
    }

    public PcPendingAsk Ask { get; }

    public string Title { get; }

    /// <summary>The popover's content: the pill's card body for the ask, answered from the chat.</summary>
    internal static Control Sheet(PcPendingAsk ask, TactusChatBridge? bridge, Action close)
    {
        if (bridge?.Body(ask.Id) is not { } body) return new TextBlock { Text = "This ask has ended.", Margin = new Thickness(16) };
        var card = new PillCard(new CompositionPillAnimator(), () => TimeProvider.System.GetTimestamp() / (double)TimeProvider.System.TimestampFrequency,
            (seconds, run) => Avalonia.Threading.DispatcherTimer.RunOnce(run, TimeSpan.FromSeconds(seconds)))
        {
            Width = PillMorph.CardWidth,
        };
        card.Answered += answer =>
        {
            if (bridge.Answer(answer) == PcResolve.Done) close();
        };
        card.Show(body, 0, PillMorph.MaxCardHeight);
        return new Border { Child = card, Width = PillMorph.CardWidth, Height = card.BodyHeight, ClipToBounds = true };
    }
}
