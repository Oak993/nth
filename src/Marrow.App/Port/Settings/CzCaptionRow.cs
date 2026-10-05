// PORT-ONLY CZ-4 (customization-SPEC CZ-6.4; DESIGN gen.py `.czcap`): a caption line inside a card ("Outside glow", "Line"): 12 / 16 semibold in the secondary colour, 12 above and 16 in, 3 below. It has no divider above it and
// the row after it has none (CzRowStack).
using Avalonia;
using Avalonia.Controls;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Port.Settings;

internal sealed class CzCaptionRow : Decorator
{
    internal TextBlock Text { get; }

    internal CzCaptionRow(string text)
    {
        Text = new TextBlock { Text = text, LineHeight = 16, Margin = new Thickness(16, 12, 16, 3) };
        SwiftFont.System(12, SwiftFont.Weight.Semibold).Apply(Text);
        Text.Foreground = Chrome.SecondaryText.Brush(Text);
        Child = Text;
        Avalonia.Automation.AutomationProperties.SetName(this, text);
    }
}
