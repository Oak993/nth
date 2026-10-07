// PORT-ONLY: MA-3b-13. A rich title also exposes its complete Text to accessibility and plain-text readers.
// Avalonia clears Text when Inlines changes; retain the explicit text after constructing the name spans.
using Avalonia;
using Avalonia.Controls;
namespace Marrow.App.Port.MultiAccount.Cards;
internal sealed class CardTitleBlock : TextBlock
{
    private bool keepingInlines;
    internal void KeepText(string text)
    {
        keepingInlines = true; try { Text = text; } finally { keepingInlines = false; }
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        if (keepingInlines && change.Property == TextProperty) return;
        base.OnPropertyChanged(change);
    }
}
