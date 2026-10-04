// PORT-ONLY CZ-1: measured moving pill, DESIGN gen.py:228-240; cz.js:420-436.
using Avalonia;
using Avalonia.Media;
namespace Marrow.UI.Port.Settings;

public sealed class CzSegmentedControl : CzRadioGroup
{
    private readonly CzNumber left, width;
    private bool placed;
    private readonly CzSegmentTrack track;
    public Rect Pill => new(left.Value, 2, width.Value, Small ? 22 : 24);
    public bool Small { get; }
    public CzSegmentedControl(string name, IReadOnlyList<(string Id, string Text)> options, string selected, bool small = false) : base(name, options, selected)
    {
        Small = small; Height = small ? 26 : 28; Margin = new Thickness(0);
        track = new CzSegmentTrack(this); Children.Insert(0, track);
        var motion = new CzMotion(this);
        left = new CzNumber(motion, 2, _ => track.InvalidateVisual()); width = new CzNumber(motion, 0, _ => track.InvalidateVisual());
        foreach (var button in Buttons)
        {
            button.Height = small ? 22 : 24; button.Padding = new Thickness(small ? 9 : 11, 0); button.CornerRadius = new CornerRadius(small ? 11 : 12);
            if (button.Child is Avalonia.Controls.TextBlock text) text.FontSize = small ? 11.5 : 12;
        }
        SelectionChanged += _ => Place();
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        var size = base.MeasureOverride(new Size(Math.Max(0, availableSize.Width - 4), availableSize.Height));
        return new Size(size.Width + 4, Small ? 26 : 28);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        track.Arrange(new Rect(finalSize));
        double x = 2;
        foreach (var button in Buttons) { button.Arrange(new Rect(x, 2, button.DesiredSize.Width, Small ? 22 : 24)); x += button.DesiredSize.Width; }
        Place(); return finalSize;
    }
    private void Place()
    {
        var button = Buttons.First(b => b.Picked);
        if (button.Bounds.Width == 0) return;
        if (!placed) { left.Jump(button.Bounds.X); width.Jump(button.Bounds.Width); placed = true; }
        else { left.To(button.Bounds.X, CzMotion.Ez(.3)); width.To(button.Bounds.Width, CzMotion.Ez(.3)); }
    }
}
