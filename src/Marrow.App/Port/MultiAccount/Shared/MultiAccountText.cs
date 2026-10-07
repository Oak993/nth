// PORT-ONLY: locked multi-account text tokens over the retained shared app font.
using Avalonia.Controls;
using Marrow.App.Port.Skills.Pages;
using Marrow.UI.Port;
namespace Marrow.App.Port.MultiAccount.Shared;
internal static class MultiAccountText
{
    internal static TextBlock Text(string text, double size, bool secondary = false, SwiftFont.Weight? weight = null, bool trim = false, bool wrap = false)
    {
        var words = LibraryUi.Text(text, size, secondary, weight, trim, wrap);
        words.Foreground = (secondary ? MultiAccountPalette.Secondary : MultiAccountPalette.Primary).Brush(words);
        return words;
    }
}
