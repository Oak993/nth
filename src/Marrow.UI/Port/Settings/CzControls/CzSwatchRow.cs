// PORT-ONLY CZ-1: eight border-beam palettes (CZ-6.2), stops 0/4/6/8 at 0/.4/.7/1.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.Core.Models.ComposerLights;
using Marrow.Core.Port.Settings;
namespace Marrow.UI.Port.Settings;

public sealed class CzSwatchRow : CzRadioGroup
{
    public CzSwatchRow(string name, IReadOnlyList<(string Id, string Text, IBrush Fill)> swatches, string? selected)
        : base(name, swatches.Select(x => (x.Id, x.Text)).ToArray(), selected)
    {
        Spacing = 2;
        for (var i = 0; i < Buttons.Count; i++)
        {
            var button = Buttons[i]; button.Width = button.Height = 24; button.Padding = new Thickness(0); button.Child = null;
            button.CornerRadius = new CornerRadius(12); button.SwatchFill = swatches[i].Fill; button.PressScale = 1;
            ToolTip.SetTip(button, swatches[i].Text);
        }
    }
    public static CzSwatchRow BeamPalettes(string selected)
    {
        var offsets = new[] { 0.0, .4, .7, 1.0 }; var indices = new[] { 0, 4, 6, 8 };
        var swatches = CzText.PaletteNames.Select(p =>
        {
            var brush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative) };
            var colors = BorderBeamTables.Shared.LargeBlobs[p.Key];
            for (var i = 0; i < 4; i++)
            {
                var c = colors[indices[i]].Color;
                brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)Math.Round(c.A * 255), (byte)c.R, (byte)c.G, (byte)c.B), offsets[i]));
            }
            return (p.Key, p.Value, (IBrush)brush);
        }).ToArray();
        return new CzSwatchRow("Colours", swatches, selected);
    }

    /// <summary>OD-0150: at most this many swatches to a line, the rest wrapping onto further lines (null: one line).</summary>
    public int? PerLine { get; init; }

    private const double Size = 24, Gap = 2, LineGap = 4;

    protected override Avalonia.Size MeasureOverride(Avalonia.Size availableSize)
    {
        if (PerLine is not { } per) return base.MeasureOverride(availableSize);
        foreach (var b in Buttons) b.Measure(new Avalonia.Size(Size, Size));
        var columns = Math.Min(per, Buttons.Count); var lines = (Buttons.Count + per - 1) / per;
        return new Avalonia.Size(columns * Size + (columns - 1) * Gap, lines * Size + (lines - 1) * LineGap);
    }

    protected override Avalonia.Size ArrangeOverride(Avalonia.Size finalSize)
    {
        if (PerLine is not { } per) return base.ArrangeOverride(finalSize);
        for (var i = 0; i < Buttons.Count; i++) Buttons[i].Arrange(new Rect(i % per * (Size + Gap), i / per * (Size + LineGap), Size, Size));
        return finalSize;
    }

    /// <summary>A swatch's fill: four colours from top left to bottom right.</summary>
    public static IBrush Fill(IReadOnlyList<string> colours)
    {
        var brush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative) };
        for (var i = 0; i < colours.Count; i++) brush.GradientStops.Add(new GradientStop(Color.Parse(colours[i]), colours.Count == 1 ? 0 : i / (double)(colours.Count - 1)));
        return brush;
    }

    /// <summary>OD-0150: the shader lights' colour packs (the catalogue's, Custom last), eight to a line; Accent and Custom take their colours from <paramref name="colours"/>.</summary>
    public static CzSwatchRow ShaderPacks(string selected, Func<string, IReadOnlyList<string>> colours) =>
        new("Colours", ShaderLightCatalog.Packs.Select(p => (p.Id, p.Name, Fill(p.Colours ?? colours(p.Id)))).ToArray(), selected) { PerLine = 8 };

    /// <summary>Gives swatch <paramref name="id"/> new colours (Accent, Custom).</summary>
    public void Recolour(string id, IReadOnlyList<string> colours)
    {
        if (Buttons.FirstOrDefault(b => b.Id == id) is not { } button) return;
        button.SwatchFill = Fill(colours);
        button.InvalidateVisual();
    }
}
