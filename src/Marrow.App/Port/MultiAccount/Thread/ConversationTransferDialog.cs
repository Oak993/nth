// PORT-ONLY: MA-3b-E1, OD-0120. Wording beyond the owner's checkbox is proposed in ED-456.
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount;

namespace Marrow.App.Port.MultiAccount.Thread;

internal sealed class ConversationTransferDialog : StackPanel
{
    private bool answered;
    internal CheckBox RememberBox { get; } = new() { Content = "Don't ask again", IsChecked = false };
    internal Button SendButton { get; } = new() { Content = "Send conversation", IsDefault = false };
    internal Button CancelButton { get; } = new() { Content = "Cancel", IsCancel = true };

    internal ConversationTransferDialog(ConversationTransferQuestion question, Action<ConversationTransferAnswer> answer)
    {
        Margin = new(20); Spacing = 16; MaxWidth = 440;
        Children.Add(new TextBlock { Text = "Send this conversation?", FontWeight = FontWeight.Bold });
        Children.Add(new TextBlock
        {
            Text = "Send the recorded conversation from " + question.Source.DisplayName + " to "
                + string.Join(", ", question.Destinations.Select(service => service.DisplayName)) + " for this request?",
            TextWrapping = TextWrapping.Wrap,
        });
        Children.Add(RememberBox);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(SendButton); buttons.Children.Add(CancelButton); Children.Add(buttons);
        void Decide(bool accepted)
        {
            if (answered) return;
            answered = true;
            answer(new(accepted, accepted && RememberBox.IsChecked == true));
        }
        SendButton.Click += (_, _) => Decide(true);
        CancelButton.Click += (_, _) => Decide(false);
    }
}
