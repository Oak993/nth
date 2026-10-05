using Avalonia.Controls;
using Avalonia.Input;

namespace Marrow.UI.Tests.Fixtures;

// ED-246: a focusable list row that pages on its own (a custom list's item container), for the key-order tests.
public sealed class PagingRow : Border
{
    public int Pages;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.PageDown)
        {
            Pages++;
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }
}
