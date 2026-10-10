// PORT-ONLY: Ignis U4 (OD-0134, ED-NEW-ignis-views). The quiet timeline line an "ignis:event:" notice draws as (design
// "Timeline event lines", ".evt"): centred, a hairline either side (up to 56 pt), a 12 pt flame, the line's words in 11.5 pt
// secondary, the amount (a saving in green, a cost or a loss in secondary) and the time in tertiary tabular figures. A line
// that arrives while it is new rises 8 pt and fades in (0.42 s, the design's rise curve). Above the first keep-warm line of a
// Claude Code or Codex chat, once, sits the first-time notice "Ignis hides its keep-warm pings" (Show pings, Got it).
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;
using Marrow.UI.Port;

namespace Marrow.App.Port.Ignis.Views;

public sealed class IgnisEventLine : Decorator
{
    /// <summary>How recent an event must be to arrive with its rise (an older one, from history, is simply there).</summary>
    public static readonly TimeSpan ArrivalWindow = TimeSpan.FromSeconds(10);

    private readonly IgnisEvent? ignisEvent;

    private IgnisEventLine(IIgnisController controller, TimelineItem item, IgnisEvent? found)
    {
        ignisEvent = found;
        var words = found is { } e ? IgnisWords.EventLine(e) : (item.TimelineItemContent as TimelineItem.Content.Notice)?.Value.Message ?? "";
        Words = words;
        // ED-NEW-ignis-guards: the switch-cost notice says its amount in its own sentence.
        Amount = found is { Kind: not IgnisEventKind.SwitchCost } withAmount ? IgnisWords.EventAmount(withAmount) : null;
        Time = found is { } at ? IgnisWords.EventTime(at.At.ToLocalTime()) : null;

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, HorizontalAlignment = HorizontalAlignment.Center };
        row.Children.Add(new FlameMark { Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center, HeatLevel = found?.Kind == IgnisEventKind.WentCold ? 0 : .85 });
        row.Children.Add(KindleUi.Text(words, 11.5));
        if (Amount is { } amount) row.Children.Add(KindleUi.Text(amount, 11.5, found!.AmountKind == IgnisAmountKind.Saved ? KindleTone.Good : KindleTone.Secondary, tabular: true));
        if (Time is { } time) row.Children.Add(KindleUi.Text(time, 11.5, KindleTone.Tertiary, tabular: true));
        foreach (var child in row.Children) child.VerticalAlignment = VerticalAlignment.Center;
        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*"), ColumnSpacing = 7 };
        Grid.SetColumn(row, 1);
        line.Children.Add(Rule(HorizontalAlignment.Right)); line.Children.Add(row);
        var after = Rule(HorizontalAlignment.Left); Grid.SetColumn(after, 2); line.Children.Add(after);
        AutomationProperties.SetName(line, string.Join(", ", new[] { words, Amount, Time }.Where(part => !string.IsNullOrEmpty(part))));

        // the first-time notice: once, above the first keep-warm line of a chat whose pings are hidden
        var stack = new StackPanel { Spacing = 12 };
        if (found is { Kind: IgnisEventKind.KeptWarm or IgnisEventKind.WarmedAgain } && !controller.Settings.Value.NoticeSeen
            && controller.Status(found.ThreadID).Provider is ProviderKind.Claude or ProviderKind.Codex
            && (IgnisViewState.Shared.NoticeHolder is null || IgnisViewState.Shared.NoticeHolder == item.Id))
        {
            IgnisViewState.Shared.NoticeHolder = item.Id;
            stack.Children.Add(Notice = new IgnisPingNotice(controller, controller.Status(found.ThreadID).Provider!.Value));
        }
        stack.Children.Add(line);
        Child = stack;
        Margin = new Thickness(0, 4);

        if (found is { } fresh && SwiftDate.Now - fresh.At < ArrivalWindow)
        {
            AttachedToVisualTree += (_, _) =>
            {
                if (KindleMotion.Reduce(this)) return;
                var rise = new AnimatedScalar(line, 0, p => { line.Opacity = p; line.RenderTransform = new TranslateTransform(0, 8 * (1 - p)); });
                rise.Set(1, KindleMotion.Rise(.42));
            };
        }
    }

    /// <summary>The line for a notice item whose id is "ignis:event:{id}"; the ledger gives its words and amount (null: not an event item).</summary>
    public static IgnisEventLine? For(IIgnisController controller, TimelineItem item)
    {
        if (!IgnisEvent.IsEventItem(item.Id)) return null;
        var id = item.Id[IgnisEvent.TimelinePrefix.Length..];
        return new IgnisEventLine(controller, item, controller.Ledger.Event(id));
    }

    public string Words { get; }
    public string? Amount { get; }
    public string? Time { get; }
    internal IgnisPingNotice? Notice { get; }

    private static Border Rule(HorizontalAlignment side)
    {
        var rule = new Border { Height = 1, MaxWidth = 56, HorizontalAlignment = side, VerticalAlignment = VerticalAlignment.Center };
        rule.Background = KindleUi.TrackFill.Brush(rule);
        AutomationProperties.SetAccessibilityView(rule, AccessibilityView.Raw);
        return rule;
    }
}

/// <summary>
/// The first-time notice (design "First-time notice", ".notice"): a glass card at most 400 pt wide, 16 pt corners, a 16 pt flame,
/// the title (13 pt semibold), one sentence (12 pt secondary) and, at the end, Show pings (Hide pings while shown) and Got it.
/// Got it marks it seen (Settings › Ignis keeps it) and the card folds away; Show pings turns "Show keep-warm pings" on.
/// </summary>
internal sealed class IgnisPingNotice : Decorator
{
    private readonly IIgnisController controller;
    private readonly RevealSlot slot = new() { Reveal = 1 };
    private readonly AnimatedScalar fold;

    public IgnisPingNotice(IIgnisController controller, ProviderKind provider)
    {
        this.controller = controller;
        var title = KindleUi.Text(IgnisWords.NoticeTitle, 13, KindleTone.Primary, strong: true);
        title.Margin = new Thickness(0, 0, 0, 2);
        var body = KindleUi.Text(IgnisWords.NoticeBody(provider.DisplayName), 12, wraps: true);
        body.Margin = new Thickness(0, 0, 0, 10);
        ShowPingsButton = KindleUi.Pill(PingsWord(), TogglePings);
        GotItButton = KindleUi.Pill(IgnisWords.GotIt, GotIt);
        var foot = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { ShowPingsButton, GotItButton } };
        var flame = new FlameMark { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) };
        var content = new DockPanel();
        DockPanel.SetDock(flame, Dock.Left);
        content.Children.Add(flame);
        content.Children.Add(new StackPanel { Margin = new Thickness(10, 0, 0, 0), Children = { title, body, foot } });
        var card = new Border { Padding = new Thickness(14, 12), CornerRadius = new CornerRadius(16), Child = content, MaxWidth = 400, HorizontalAlignment = HorizontalAlignment.Center };
        var glass = card.ChromeGlassSurfaceCard();
        slot.Child = glass;
        Child = slot;
        fold = new AnimatedScalar(slot, 1, value => slot.Reveal = value);
        AutomationProperties.SetName(this, IgnisWords.NoticeTitle);
    }

    internal Button ShowPingsButton { get; }
    internal Button GotItButton { get; }
    internal bool Folded => slot.Reveal <= 0;

    private string PingsWord() => controller.Settings.Value.ShowPings ? IgnisWords.HidePings : IgnisWords.ShowPings;

    private void TogglePings()
    {
        controller.Settings.Update(p => p with { ShowPings = !p.ShowPings });
        var word = PingsWord();
        ((TextBlock)((Border)ShowPingsButton.Content!).Child!).Text = word;
        AutomationProperties.SetName(ShowPingsButton, word);
    }

    private void GotIt()
    {
        controller.Settings.Update(p => p with { NoticeSeen = true });
        fold.Set(0, KindleMotion.Ease(this, KindleMotion.Smooth(.36)));
    }
}

internal static class KindleGlass
{
    /// <summary>The notice's glass: the popover material's look on a timeline card (the app's popover fill and rim, theme-aware).</summary>
    public static Control ChromeGlassSurfaceCard(this Border card)
    {
        card.Background = SwiftColor.Dynamic(new SwiftColor(248 / 255.0, 249 / 255.0, 252 / 255.0, .78), new SwiftColor(34 / 255.0, 38 / 255.0, 48 / 255.0, .76)).Brush(card);
        card.BorderBrush = SwiftColor.Dynamic(new SwiftColor(1, .95), new SwiftColor(1, .13)).Brush(card);
        card.BorderThickness = new Thickness(1);
        card.BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 14, Blur = 34, Spread = -18, Color = Color.FromArgb(0x29, 30, 40, 80) });
        return card;
    }
}
