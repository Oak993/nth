// PORT-ONLY: Tactus pill v2, the question card's body (OD-0145, ED-NEW-pcctl-26; DESIGN.md 10.4 "Question body", 10.9 pc_ask;
// canvas `bodyOf` question, `.kqa`, `.kq`, `.qcs`, `.mc`, `.pkf`). A label "GPT-6.1 Sol asks" (the model's name, never
// nth's), the question as plain text (at most 600 characters; 8 lines, then it scrolls; its direction follows the text),
// at most 4 quick-reply chips (40 characters each, wrapping onto two rows; one click sends that answer, or with a
// multi-select question they toggle and Send sends them), the reply field ("Answer, or say what to do instead"), and the
// footer ("Enter sends · Shift+Enter new line · Esc Later") with Dismiss. A question that asks for a secret never shows
// here: it is drawn as "Answer this in the chat" with Open chat (ProviderPointerBody).
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.UI.Port.PcControl;

public sealed class QuestionCardBody : CardBody
{
    public const string DismissId = "dismiss";
    public const string SendChipsId = "sendchips";
    public const string ChipPrefix = "chip:";
    public const string Placeholder = "Answer, or say what to do instead";
    public const string SecretLine = "Answer this in the chat";
    public const int MaxQuestion = 600;
    public const int MaxChips = 4;
    public const int MaxChip = 40;
    public const double QuoteLines = 8;
    public const double QuoteLineHeight = 21;

    private readonly PillQuestionBody model;
    private readonly List<CardButton> chips = [];
    private readonly MiniComposer? composer;

    public QuestionCardBody(PillQuestionBody model)
        : base(model)
    {
        this.model = model;
        var asks = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6,
            Children = { CardLook.Icon(CardLook.Cpu, 13, CardLook.Text2), CardLook.Line($"{model.Model} asks", 12, CardLook.Text2, FontWeight.Medium, wrap: TextWrapping.NoWrap) },
        };
        if (model.Page is { Length: > 0 } page)
            asks.Children.Add(new TextBlock { Text = page, FontFamily = CardLook.Mono, FontSize = 11, Foreground = CardLook.Text3, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        Above.Children.Add(asks);
        var quote = new TextBlock
        {
            Text = Clipped(model.Question), FontFamily = CardLook.Text, FontSize = 14, LineHeight = QuoteLineHeight, Foreground = CardLook.Text1,
            TextWrapping = TextWrapping.Wrap, FlowDirection = Direction(model.Question),
        };
        Above.Children.Add(new ScrollViewer
        {
            Margin = new Thickness(0, 6, 0, 0), MaxHeight = QuoteLines * QuoteLineHeight,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
            Content = quote,
        });
        if (model.Choices.Count > 0)
        {
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6, Margin = new Thickness(0, 12, 0, 0) };
            foreach (var choice in model.Choices.Take(MaxChips))
            {
                var text = choice.Length > MaxChip ? choice[..MaxChip] : choice;
                var chip = Add(ChipPrefix + text, CardControlKind.Answer, Labelled(CardButtonLook.Quick, text));
                chip.MaxWidth = PillMorph.CardWidth - 32;
                AutomationProperties.SetName(chip, text);
                chips.Add(chip);
                wrap.Children.Add(chip);
            }
            Above.Children.Add(wrap);
        }
        if (model.AllowText)
        {
            composer = new MiniComposer(Placeholder);
            Add(MiniComposer.SendId, CardControlKind.Free, composer.Send);
            composer.Grew += Relayout;
            Composer = composer;
            Below.Children.Add(composer);
            if (model.Multi) composer.Field.PropertyChanged += (_, e) =>
            {
                if (e.Property == TextBox.TextProperty) composer.Send.IsEnabled = composer.HasText || Chosen.Count > 0;
            };
        }
        else if (model.Multi)
        {
            var send = Add(SendChipsId, CardControlKind.Answer, Labelled(CardButtonLook.Primary, "Send"));
            send.IsEnabled = false;
            send.HorizontalAlignment = HorizontalAlignment.Right;
            send.Margin = new Thickness(0, 12, 0, 0);
            Below.Children.Add(send);
        }
        CardButton? dismiss = null;
        if (model.CanDismiss)
        {
            dismiss = Add(DismissId, CardControlKind.Answer, Labelled(CardButtonLook.Link, "Dismiss"));
            dismiss.VerticalAlignment = VerticalAlignment.Center;
        }
        IReadOnlyList<PillHintPart> quiet = model.AllowText
            ? [new("Enter sends"), PillHintPart.Dot, new("Shift+Enter new line"), PillHintPart.Dot, PillHintPart.Key("Esc"), new(" Later")]
            : [PillHintPart.Key("Esc"), new(" Later"), PillHintPart.Dot, new(CardShield.EnterHint)];
        Footer = new CardFooter(quiet, dismiss);
        Below.Children.Add(Footer);
    }

    public new PillQuestionBody Model => model;

    /// <summary>A multi-select question's chips that are on.</summary>
    public IReadOnlyList<string> Chosen => chips.Where(c => c.IsOn).Select(c => AutomationProperties.GetName(c) ?? "").ToList();

    internal IReadOnlyList<CardButton> Chips => chips;

    /// <summary>The question as the card shows it: at most 600 characters, plain text.</summary>
    public static string Clipped(string question) => question.Length > MaxQuestion ? question[..MaxQuestion] + "…" : question;

    /// <summary>Right to left when the text's first strong character is Hebrew or Arabic script (Urdu included).</summary>
    public static FlowDirection Direction(string text)
    {
        foreach (var c in text)
        {
            if (c is >= '֐' and <= 'ࣿ' or >= 'יִ' and <= '﷿' or >= 'ﹰ' and <= '﻿') return FlowDirection.RightToLeft;
            if (char.IsLetter(c)) return FlowDirection.LeftToRight;
        }
        return FlowDirection.LeftToRight;
    }

    public override PillCardAnswer? Act(string id)
    {
        if (id.StartsWith(ChipPrefix, StringComparison.Ordinal))
        {
            var chip = Controls.First(c => c.Id == id).Button;
            var text = AutomationProperties.GetName(chip) ?? "";
            if (!model.Multi) return new PillCardAnswer(model.AskId, model.Revision, PillCardChoice.Chip, Text: text);
            chip.IsOn = !chip.IsOn;
            if (composer is not null) composer.Send.IsEnabled = composer.HasText || Chosen.Count > 0;
            else if (Controls.FirstOrDefault(c => c.Id == SendChipsId) is { } send) send.Button.IsEnabled = Chosen.Count > 0;
            return null;
        }
        switch (id)
        {
            case DismissId when model.CanDismiss:
                return new PillCardAnswer(model.AskId, model.Revision, PillCardChoice.Dismiss);
            case SendChipsId when Chosen.Count > 0:
                return new PillCardAnswer(model.AskId, model.Revision, PillCardChoice.Send, Chips: Chosen);
            case MiniComposer.SendId when composer is not null && (composer.HasText || (model.Multi && Chosen.Count > 0)):
                return new PillCardAnswer(model.AskId, model.Revision, PillCardChoice.Send, Text: composer.HasText ? composer.Text.Trim() : null, Chips: model.Multi ? Chosen : null);
            default:
                return null;
        }
    }
}
