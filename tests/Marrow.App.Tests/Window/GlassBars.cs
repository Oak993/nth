using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Marrow.App.Tests.Window.Native;

// ED-356: colour bars 6 pt wide, moved by Phase (GlassMaterialNativeTests moves them 2 pt every frame): what lies under the glass changes
// each frame, so it is drawn each frame.
internal sealed class GlassBars : Control
{
    private static readonly IBrush[] Colours =
    [
        new SolidColorBrush(Color.FromRgb(230, 60, 60)), new SolidColorBrush(Color.FromRgb(250, 210, 70)), new SolidColorBrush(Color.FromRgb(60, 190, 90)),
        new SolidColorBrush(Color.FromRgb(50, 120, 230)), new SolidColorBrush(Color.FromRgb(240, 240, 240)), new SolidColorBrush(Color.FromRgb(20, 20, 24)),
    ];
    public double Phase { get; set; }
    public override void Render(DrawingContext context)
    {
        for (var i = -1; i * 6 < Bounds.Width + 6; i++)
            context.FillRectangle(Colours[((i % Colours.Length) + Colours.Length) % Colours.Length], new Rect(i * 6 + Phase % 6 - 6, 0, 6, Bounds.Height));
    }
}
