// PORT-ONLY CZ-1: selectable 96x56 orb preview tiles (DESIGN gen.py:215-222).
// The voice unit supplies the actual orb controls: there is no placeholder renderer or voice state here.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
namespace Marrow.UI.Port.Settings;

public sealed class CzOrbTiles : CzRadioGroup
{
    private readonly List<Border> tiles = [];
    private readonly List<TextBlock> labels = [];
    private readonly List<CzColor> fills = [], labelColors = [];
    private readonly List<CzNumber> rings = [];
    public CzOrbTiles(IReadOnlyList<(string Id, string Name, Control Orb)> options, string selected)
        : base("Orb style", options.Select(x => (x.Id, x.Name)).ToArray(), selected)
    {
        Spacing = 12;
        for (var i = 0; i < Buttons.Count; i++)
        {
            var button = Buttons[i]; button.Width = 96; button.Height = double.NaN; button.Padding = new Thickness(0); button.CornerRadius = new CornerRadius(9); button.PressScale = 1;
            var orb = options[i].Orb; orb.Width = orb.Height = 52; orb.HorizontalAlignment = HorizontalAlignment.Center; orb.VerticalAlignment = VerticalAlignment.Center;
            var tile = new Border { Width = 96, Height = 56, CornerRadius = new CornerRadius(8), Child = orb };
            tiles.Add(tile);
            var color = CzTheme.Token(this, button.Picked ? "tilesel" : "tile");
            tile.Background = CzTheme.Brush(color);
            fills.Add(new CzColor(button.Motion, color, c => tile.Background = CzTheme.Brush(c)));
            var ring = new CzNumber(button.Motion, button.Picked ? 2 : 0, v => tile.BorderThickness = new Thickness(v));
            rings.Add(ring); ring.Jump(button.Picked ? 2 : 0);
            var scale = new CzNumber(button.Motion, 1, v => tile.RenderTransform = new ScaleTransform(v, v));
            tile.RenderTransformOrigin = new RelativePoint(.5, .5, RelativeUnit.Relative);
            button.PointerPressed += (_, e) => { if (button.IsEffectivelyEnabled && e.GetCurrentPoint(button).Properties.IsLeftButtonPressed) scale.To(.97, CzMotion.Ease(.12)); };
            button.PointerReleased += (_, _) => scale.To(1, CzMotion.Ease(.12));
            button.PointerCaptureLost += (_, _) => scale.To(1, CzMotion.Ease(.12));
            var line = new StackPanel { Spacing = 6 }; line.Children.Add(tile);
            var label = new TextBlock { Text = options[i].Name, FontSize = 12, LineHeight = 16, FontFamily = new FontFamily("fonts:Inter#Inter"),
                Foreground = CzTheme.Text(this, !button.Picked), HorizontalAlignment = HorizontalAlignment.Center };
            labels.Add(label); line.Children.Add(label); button.Child = line;
            labelColors.Add(new CzColor(button.Motion, ((SolidColorBrush)label.Foreground!).Color, c => label.Foreground = CzTheme.Brush(c)));
            button.PointerEntered += (_, _) => UpdateTiles();
            button.PointerExited += (_, _) => UpdateTiles();
        }
        SelectionChanged += _ => UpdateTiles();
        PropertyChanged += (_, e) => { if (e.Property.Name == "ActualThemeVariant") UpdateTiles(); };
        UpdateTiles();
    }
    private void UpdateTiles()
    {
        for (var i = 0; i < Buttons.Count; i++)
        {
            fills[i].To(CzTheme.Token(this, Buttons[i].Picked || Buttons[i].IsPointerOver ? "tilesel" : "tile"), CzMotion.Ease(.2));
            labelColors[i].To(((SolidColorBrush)CzTheme.Text(this, !Buttons[i].Picked)).Color, CzMotion.Ease(.2));
            tiles[i].BorderBrush = CzTheme.Brush(CzTheme.Token(this, "sel")); rings[i].To(Buttons[i].Picked ? 2 : 0, CzMotion.Ease(.2));
        }
    }
}
