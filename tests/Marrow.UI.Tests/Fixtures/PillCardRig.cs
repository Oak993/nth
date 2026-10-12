using Avalonia;
using Avalonia.Controls;
using Marrow.UI.Port.PcControl;

namespace Marrow.UI.Tests.Fixtures;

/// <summary>A card in a headless window on a hand-moved clock, with what it answered and what it scheduled.</summary>
public sealed record PillCardRig(PillCard Card, RecordingPillAnimator Animator, Window Window, List<PillCardAnswer> Answers, List<(double Seconds, Action Run)> Later)
{
    public double Now { get; set; } = 10;

    public CardButton Button(string id) => Card.Current!.Controls.First(c => c.Id == id).Button;

    // the pointer rests on a control's centre (in the card's DIP)
    public void RestOn(string id)
    {
        var at = Card.Layout().First(c => c.Id == id).Bounds;
        Card.Moved(new Point(at.X + (at.Width / 2), at.Y + (at.Height / 2)));
    }
}
