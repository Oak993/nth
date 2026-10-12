// PORT-ONLY: Tactus pill v2, a card whose ask ended while it held a draft (OD-0145, ED-NEW-pcctl-26; DESIGN.md 10.4 "If its
// card is open with a draft, the card keeps the text and says "The model stopped waiting for this." with "Send to chat"
// (the draft goes as a normal chat message) and "Discard"", and "Answered in the chat. Your draft is still here."; canvas
// `bodyOf` withdrawn).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.UI.Port.PcControl;

public sealed class WithdrawnCardBody : CardBody
{
    public const string DiscardId = "discard";
    public const string SendToChatId = "tochat";
    public const string AnsweredLine = "Answered in the chat. Your draft is still here.";
    public const string StoppedLine = "The model stopped waiting for this.";

    private readonly PillWithdrawnBody model;

    public WithdrawnCardBody(PillWithdrawnBody model)
        : base(model)
    {
        this.model = model;
        Above.Children.Add(CardLook.Line(model.AnsweredInChat ? AnsweredLine : StoppedLine, 14, CardLook.Text1, lineHeight: 21));
        Above.Children.Add(new Border
        {
            Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(14, 10), CornerRadius = new CornerRadius(14), Background = CardLook.Rows, BoxShadow = CardLook.RowsRing,
            Child = new ScrollViewer
            {
                MaxHeight = 120,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
                Content = new TextBlock { Text = model.Draft, FontFamily = CardLook.Text, FontSize = 13.5, LineHeight = 19.6, Foreground = CardLook.Text1, TextWrapping = TextWrapping.Wrap },
            },
        });
        // Discard drops the draft, so it is protected like an answer; Send to chat sends it as a normal chat message.
        var discard = Add(DiscardId, CardControlKind.Answer, Labelled(CardButtonLook.Plain, "Discard"));
        var send = Add(SendToChatId, CardControlKind.Answer, Labelled(CardButtonLook.Primary, "Send to chat"));
        Below.Children.Add(Row(discard, send));
    }

    public override PillCardAnswer? Act(string id) => id switch
    {
        DiscardId => new PillCardAnswer(model.AskId, model.Revision, PillCardChoice.Discard),
        SendToChatId => new PillCardAnswer(model.AskId, model.Revision, PillCardChoice.SendToChat, Text: model.Draft),
        _ => null,
    };
}
