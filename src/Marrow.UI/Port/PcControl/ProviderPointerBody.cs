// PORT-ONLY: Tactus pill v2, the card for a provider's own approval (OD-0145, ED-NEW-pcctl-26; DESIGN.md 10.4 "Provider
// approvals stay in the chat. ... The card names the request, shows its first line (a command in a mono block), says
// "Commands, file changes and plans are approved in the chat", and offers only "Open chat""; canvas `bodyOf` 'pv'). The
// pill only points at the chat: nothing here answers the request. A secret question takes the same shape ("Answer this
// in the chat").
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.UI.Port.PcControl;

public sealed class ProviderPointerBody : CardBody
{
    public const string OpenChatId = "openchat";

    /// <summary>The canvas's words for DESIGN's line (AskLifetimes.ProviderApprovalLine, completed).</summary>
    public const string ChatLine = "Commands, file changes and plans are approved in the chat, where you can see all of them. The pill only shows that one waits.";

    private readonly PillCardBody model;

    public ProviderPointerBody(PillProviderBody model)
        : base(model)
    {
        this.model = model;
        Above.Children.Add(CardLook.Line(model.What, 14, CardLook.Text1, lineHeight: 21));
        if (model.FirstLine.Length > 0) Above.Children.Add(Command(model.FirstLine));
        Above.Children.Add(CardLook.Line(ChatLine, 13, CardLook.Text2, lineHeight: 18.85).With(t => t.Margin = new Thickness(0, 9, 0, 0)));
        Below.Children.Add(OpenChat());
    }

    /// <summary>A secret question: "Answer this in the chat" and Open chat, no composer (DESIGN.md 10.4).</summary>
    internal ProviderPointerBody(PillQuestionBody secret)
        : base(secret)
    {
        model = secret;
        Above.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6,
            Children = { CardLook.Icon(CardLook.Cpu, 13, CardLook.Text2), CardLook.Line($"{secret.Model} asks", 12, CardLook.Text2, FontWeight.Medium, wrap: TextWrapping.NoWrap) },
        });
        Above.Children.Add(CardLook.Line(QuestionCardBody.Clipped(secret.Question), 14, CardLook.Text1, lineHeight: 21).With(t =>
        {
            t.Margin = new Thickness(0, 6, 0, 0);
            t.FlowDirection = QuestionCardBody.Direction(secret.Question);
        }));
        Above.Children.Add(CardLook.Line(QuestionCardBody.SecretLine, 13, CardLook.Text2, lineHeight: 18.85).With(t => t.Margin = new Thickness(0, 9, 0, 0)));
        Below.Children.Add(OpenChat());
    }

    /// <summary>"Type your answer in the chat" and Open chat; the draft shows and goes with it.</summary>
    internal ProviderPointerBody(PillTypeInChatBody chat)
        : base(chat)
    {
        model = chat;
        Above.Children.Add(CardLook.Line(TypeInChatLine, 14, CardLook.Text1, lineHeight: 21));
        if (chat.Draft.Length > 0)
        {
            Above.Children.Add(Command(chat.Draft));
            Above.Children.Add(CardLook.Line(DraftGoesLine, 13, CardLook.Text2, lineHeight: 18.85).With(t => t.Margin = new Thickness(0, 9, 0, 0)));
        }
        Below.Children.Add(OpenChat());
    }

    public const string TypeInChatLine = "Type your answer in the chat";
    public const string DraftGoesLine = "Your draft goes with it.";

    public override PillCardAnswer? Act(string id) =>
        id != OpenChatId ? null
        : model is PillTypeInChatBody { Draft.Length: > 0 } chat ? new PillCardAnswer(model.AskId, model.Revision, PillCardChoice.OpenChat, Text: chat.Draft)
        : new PillCardAnswer(model.AskId, model.Revision, PillCardChoice.OpenChat);

    private Control OpenChat()
    {
        var open = Add(OpenChatId, CardControlKind.Free, Labelled(CardButtonLook.Primary, "Open chat"));
        open.HorizontalAlignment = HorizontalAlignment.Right;
        open.Margin = new Thickness(0, 14, 0, 0);
        return open;
    }

    // canvas `pre.kcm`: mono 12/1.5 in a dark well, wrapping anywhere
    private static Control Command(string line) => new Border
    {
        Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(11, 9), CornerRadius = new CornerRadius(10), Background = CardLook.Well,
        Child = new TextBlock { Text = line, FontFamily = CardLook.Mono, FontSize = 12, LineHeight = 18, Foreground = CardLook.Text1, TextWrapping = TextWrapping.Wrap },
    };
}
