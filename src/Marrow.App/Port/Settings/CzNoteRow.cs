// PORT-ONLY CZ-4 (customization-SPEC CZ-8; DESIGN gen.py `.cznote`): a note inside a card (Off's "The composer has no light..."): 12.5 / 18 in the secondary colour, 12 above and below, 16 in.
using Avalonia;
using Avalonia.Controls;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Port.Settings;

internal sealed class CzNoteRow : Decorator
{
    internal TextBlock Text { get; }

    internal CzNoteRow(string text)
    {
        Text = new TextBlock { Text = text, LineHeight = 18, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(16, 12) };
        SwiftFont.System(12.5, SwiftFont.Weight.Regular).Apply(Text);
        Text.Foreground = Chrome.SecondaryText.Brush(Text);
        Child = Text;
    }
}
