// PORT-ONLY: Tactus pill v2, the show-window card's body (OD-0145, ED-NEW-pcctl-26; DESIGN.md 2.10, 10.4 "Show-window body:
// what will show and why ("reading the October chart"), and that it comes to the front without taking the keyboard and
// goes back after one step. "Not now" and "Show""; canvas `bodyOf` 'sw').
using Avalonia;
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.UI.Port.PcControl;

public sealed class ShowWindowCardBody : CardBody
{
    public const string NotNowId = "notnow";
    public const string ShowId = "show";

    private readonly PillShowBody model;

    public ShowWindowCardBody(PillShowBody model)
        : base(model)
    {
        this.model = model;
        Above.Children.Add(CardLook.Line(Lead(model), 14, CardLook.Text1, lineHeight: 21));
        Above.Children.Add(CardLook.Line(Detail(model.App), 13, CardLook.Text2, lineHeight: 18.85).With(t => t.Margin = new Thickness(0, 9, 0, 0)));
        var notNow = Add(NotNowId, CardControlKind.Answer, Labelled(CardButtonLook.Plain, "Not now"));
        var show = Add(ShowId, CardControlKind.Answer, Labelled(CardButtonLook.Primary, "Show"));
        Below.Children.Add(Row(notNow, show));
        Footer = new CardFooter([PillHintPart.Key("Esc"), new(" Later"), PillHintPart.Dot, new(CardShield.EnterHint)]);
        Below.Children.Add(Footer);
    }

    /// <summary>"Tactus wants to show Excel for one step: reading the October chart." (the reason only when the model gave one).</summary>
    public static string Lead(PillShowBody model) =>
        model.Why is { Length: > 0 } why ? $"Tactus wants to show {model.App} for one step: {why}." : $"Tactus wants to show {model.App} for one step.";

    public static string Detail(string app) => $"{app} comes to the front without taking your keyboard, then goes back where it was. Nothing else changes.";

    public override PillCardAnswer? Act(string id) => id switch
    {
        ShowId => new PillCardAnswer(model.AskId, model.Revision, PillCardChoice.Show),
        NotNowId => new PillCardAnswer(model.AskId, model.Revision, PillCardChoice.NotNow),
        _ => null,
    };
}
