// PORT-ONLY CZ-1: shared 26-high capsule with optional Change chevron (CZ-12.4).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
namespace Marrow.UI.Port.Settings;

public sealed class CzCapsuleButton : CzButton
{
    private readonly CzNumber idleOpacity;
    public CzCapsuleButton(string label, bool link = false, string detail = "") : base(label, detail)
    {
        idleOpacity = new CzNumber(Motion, 1, v => Opacity = v);
        if (!link) return;
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        line.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeight.Medium, FontFamily = new FontFamily("fonts:Inter#Inter"), Foreground = CzTheme.Text(this) });
        line.Children.Add(new CzChevron(pointingRight: true) { VerticalAlignment = VerticalAlignment.Center });
        Child = line;
    }
    protected override void ApplyInk(IBrush brush)
    {
        base.ApplyInk(brush);
        if (Child is StackPanel row) foreach (var label in row.Children.OfType<TextBlock>()) label.Foreground = brush;
    }
    /// <param name="animate">False for the first placement, before the button is shown: it starts at its look (a page does not fade its Reset in as it opens).</param>
    public void SetAtDefaults(bool atDefaults, bool animate = true)
    {
        IsEnabled = Focusable = !atDefaults;
        if (animate) idleOpacity.To(atDefaults ? .42 : 1, CzMotion.Ease(.25)); else idleOpacity.Jump(atDefaults ? .42 : 1);
    }
}
