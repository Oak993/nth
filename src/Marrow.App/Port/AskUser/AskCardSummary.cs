// PORT-ONLY: web surfaces W25, ask_user (DESIGN.md 7.5 look; ED-NEW-web-w25-1). The card once it is closed: what was asked and what the
// person answered, or that they skipped it or stopped the turn. Quiet by design, since it only records: a state line (a mark and
// "Answered", "Skipped" or "Stopped") and each question over its answer.
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.AskUser;

internal sealed class AskCardSummary : Border
{
    public AskCardSummary(AskCard card, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var column = new StackPanel { Spacing = 8 };

        var (symbol, word) = card.State switch
        {
            AskState.Answered => ("checkmark.circle", AskCardStrings.Answered),
            AskState.Skipped => ("forward", AskCardStrings.Skipped),
            _ => ("stop.circle", AskCardStrings.Stopped),
        };
        var mark = SwiftImage.SystemName(symbol).Font(SwiftFont.System(14)).ForegroundStyle(ChromeTokens.SecondaryText);
        var state = TimelineRows.ChatText(word, SwiftFont.TextStyle.Subheadline, SwiftFont.Weight.Medium, ChromeTokens.SecondaryText, singleLine: true);
        state.VerticalAlignment = VerticalAlignment.Center;
        column.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { mark, state } });

        foreach (var question in card.Questions)
        {
            var item = new StackPanel { Spacing = 1 };
            var asked = TimelineRows.ChatText(question.Prompt, SwiftFont.TextStyle.Subheadline, color: ChromeTokens.SecondaryText);
            item.Children.Add(asked);
            if (card.State == AskState.Answered)
            {
                card.Answers.TryGetValue(question.Id, out var values);
                var given = values is { Count: > 0 };
                var line = TimelineRows.SelectableText(wraps: true);
                line.Text = given ? AskUserWords.Line(question, values, culture) : AskCardStrings.NoAnswer;
                ChatFont.Chat(SwiftFont.TextStyle.Callout, given ? SwiftFont.Weight.Medium : SwiftFont.Weight.Regular).Apply(line);
                line.Foreground = (given ? ChromeTokens.PrimaryText : ChromeTokens.SecondaryText).Brush(line);
                item.Children.Add(line);
            }
            column.Children.Add(item);
        }

        Child = column;
        Padding = new Thickness(14, 10);
        HorizontalAlignment = HorizontalAlignment.Left;
        MaxWidth = AskCardRow.CardMaxWidth;
        this.RoundedRectangle(12, RoundedCornerStyle.Continuous);
        Background = ChromeTokens.Overlay(0.04).Brush(this);
        AutomationProperties.SetName(this, AskCardStrings.CardName(card));
        AutomationProperties.SetControlTypeOverride(this, Avalonia.Automation.Peers.AutomationControlType.Group);
    }
}
