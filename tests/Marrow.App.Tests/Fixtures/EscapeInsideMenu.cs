using Avalonia;
using Avalonia.Controls;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// ED-253 (round 4): a stand-in for a paragraph's text menu (ParagraphTextPresenter's Copy / Select All through
/// NativeContextMenus), which is open for exactly the span of <see cref="Present"/>, as the Win32 menu's modal loop is.
/// While open it presses Escape through <c>escape</c>, which returns whether the window still shows; with none it closes
/// without Escape (a row chosen, a click outside it). <see cref="Dismiss"/> counts the window's request to close it.
/// </summary>
public sealed class EscapeInsideMenu(Func<bool>? escape) : IContextMenuPresenter
{
    public bool? ShownAfterEscape { get; private set; }

    public int Dismissed { get; private set; }

    public void Present(Control target, Point at, IReadOnlyList<ContextMenuEntry> entries)
    {
        Assert.Contains(entries, e => e.Title == "Select All");
        if (escape is null) return;
        ShownAfterEscape = escape();
    }

    public void Dismiss() => Dismissed++;
}
