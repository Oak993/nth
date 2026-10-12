// PORT-ONLY: Tactus pill v2, the confirm card's body (OD-0145, ED-NEW-pcctl-26; DESIGN.md 4.3, 10.4 "Confirm body"; canvas
// `bodyOf` 'cf', `.cdt`, `.rep`, `.chg`). "nth wants to click [Send] in Outlook." with the element drawn the way the app
// shows it, then the exact content read from the window (From, To, Cc and Bcc on one row when both are empty, Subject,
// attachments, the first 200 characters), a field that could not be read showing "Could not read" (and the first line
// saying so). After the re-read at the click found a change, the changed rows are marked and the card says what changed.
// Confirm and Cancel, each one deliberate click; no "always". "Reply instead": Cancel plus a note to the model.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.UI.Port.PcControl;

public sealed class ConfirmCardBody : CardBody
{
    public const string CancelId = "cancel";
    public const string ConfirmId = "confirm";
    public const string ReplyId = "reply";
    public const string ReplyPlaceholder = "Cancel, and tell the model what to change";
    public const string UnreadableLine = "Some of it could not be read.";

    private readonly PillConfirmBody model;
    private readonly CardButton reply;
    private readonly MiniComposer composer;

    public ConfirmCardBody(PillConfirmBody model)
        : base(model)
    {
        this.model = model;
        Above.Children.Add(FirstLine(model));
        if (model.ChangedNote is { Length: > 0 } note) Above.Children.Add(Changed(note));
        if (model.Rows.Count > 0) Above.Children.Add(Rows(model.Rows));
        var cancel = Add(CancelId, CardControlKind.Answer, Labelled(CardButtonLook.Plain, "Cancel"));
        var confirm = Add(ConfirmId, CardControlKind.Answer, Labelled(CardButtonLook.Primary, "Confirm " + model.Verb.ToLowerInvariant()));
        Below.Children.Add(Row(cancel, confirm));
        reply = Add(ReplyId, CardControlKind.Free, Labelled(CardButtonLook.Link, "Reply instead"));
        reply.Margin = new Thickness(0, 10, 0, 0);
        reply.HorizontalAlignment = HorizontalAlignment.Left;
        Below.Children.Add(reply);
        composer = new MiniComposer(ReplyPlaceholder) { IsVisible = false };
        Add(MiniComposer.SendId, CardControlKind.Free, composer.Send);
        composer.Grew += Relayout;
        Composer = composer;
        Below.Children.Add(composer);
        Footer = new CardFooter([PillHintPart.Key("Esc"), new(" Later"), PillHintPart.Dot, new(CardShield.EnterHint)]);
        Below.Children.Add(Footer);
    }

    public new PillConfirmBody Model => model;

    public override PillCardAnswer? Act(string id)
    {
        switch (id)
        {
            case ConfirmId:
                return new PillCardAnswer(model.AskId, model.Revision, PillCardChoice.Confirm);
            case CancelId:
                return new PillCardAnswer(model.AskId, model.Revision, PillCardChoice.Cancel);
            case ReplyId:
                if (!composer.IsVisible)
                {
                    composer.IsVisible = true;
                    reply.IsVisible = false;
                    Relayout();
                }
                return null;
            case MiniComposer.SendId when composer.HasText:
                return new PillCardAnswer(model.AskId, model.Revision, PillCardChoice.Cancel, Text: composer.Text.Trim());
            default:
                return null;
        }
    }

    public override void Opened()
    {
        if (composer.IsVisible) composer.Field.Focus();
    }

    // "nth wants to click [Send] in Outlook." (canvas `.kln` with `.rep`, the element as the app draws it)
    private static Control FirstLine(PillConfirmBody model)
    {
        var element = new Border
        {
            Height = 26, Padding = new Thickness(11, 0), CornerRadius = new CornerRadius(5), Background = CardLook.Element, BoxShadow = CardLook.ElementRing,
            Margin = new Thickness(5, 0), VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6,
                Children = { CardLook.Icon(CardLook.Send, 13, Brushes.White), new TextBlock { Text = model.Element, FontFamily = CardLook.Text, FontSize = 12.5, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center } },
            },
        };
        var line = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                CardLook.Line("nth wants to click", 14, CardLook.Text1, lineHeight: 21, wrap: TextWrapping.NoWrap).With(t => t.VerticalAlignment = VerticalAlignment.Center),
                element,
                CardLook.Line($"in {model.App}.", 14, CardLook.Text1, lineHeight: 21, wrap: TextWrapping.NoWrap).With(t => t.VerticalAlignment = VerticalAlignment.Center),
            },
        };
        // a field that could not be read: the first line says so (PcConfirmContent.FirstLine)
        if (model.Unreadable) line.Children.Add(CardLook.Line(" " + UnreadableLine, 14, CardLook.Text1, lineHeight: 21, wrap: TextWrapping.NoWrap).With(t => t.VerticalAlignment = VerticalAlignment.Center));
        AutomationProperties.SetName(line, $"nth wants to click {model.Element} in {model.App}." + (model.Unreadable ? " " + UnreadableLine : ""));
        return line;
    }

    private static Control Changed(string note) => new Border
    {
        Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(11, 8), CornerRadius = new CornerRadius(12), Background = CardLook.Change, BoxShadow = CardLook.ChangeBar,
        Child = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { CardLook.Icon(CardLook.Alert, 14, CardLook.Text1, 2), CardLook.Line(note, 13, CardLook.Text1, lineHeight: 18.85).With(t => t.MaxWidth = 360) },
        },
    }.With(b => AutomationProperties.SetLiveSetting(b, AutomationLiveSetting.Polite));

    // canvas `.cdt`: a well with one row per field (label 70 DIP, value), hairlines between; changed rows marked
    private static Control Rows(IReadOnlyList<PillConfirmRow> rows)
    {
        var stack = new StackPanel();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("70,*"), ColumnSpacing = 8 };
            var label = CardLook.Line(row.Label, 13, row.Changed ? CardLook.Text1 : CardLook.Label, wrap: TextWrapping.NoWrap);
            var value = new TextBlock
            {
                Text = row.Value, FontFamily = CardLook.Text, FontSize = 13, FontWeight = FontWeight.Medium,
                Foreground = row.Unreadable ? CardLook.Text3 : CardLook.Text1,
                TextWrapping = row.Long ? TextWrapping.Wrap : TextWrapping.NoWrap, MaxLines = row.Long ? 2 : 1,
                LineHeight = row.Long ? 18.2 : double.NaN, TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(label, 0);
            Grid.SetColumn(value, 1);
            grid.Children.Add(label);
            grid.Children.Add(value);
            AutomationProperties.SetName(grid, $"{row.Label}: {row.Value}{(row.Changed ? ", changed" : "")}");
            var cell = new Border
            {
                Padding = row.Changed ? new Thickness(12, 6, 14, 6) : new Thickness(0, 6),
                Margin = row.Changed ? new Thickness(-14, 0) : default,
                Background = row.Changed ? CardLook.Change : null,
                BoxShadow = row.Changed ? CardLook.ChangeBar : default,
                BorderBrush = i < rows.Count - 1 ? CardLook.RowLine : null,
                BorderThickness = i < rows.Count - 1 ? new Thickness(0, 0, 0, 1) : default,
                Child = grid,
                Tag = row.Changed ? "changed" : null,
            };
            stack.Children.Add(cell);
        }
        return new Border { Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(14, 4), CornerRadius = new CornerRadius(14), Background = CardLook.Rows, BoxShadow = CardLook.RowsRing, ClipToBounds = true, Child = stack };
    }
}
