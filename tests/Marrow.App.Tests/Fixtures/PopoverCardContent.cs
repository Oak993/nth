using Avalonia.Controls;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// A test that takes a presented popover's view out to show it in a window of its own (a recorded popup never opens
/// headless): the presenter hangs the view on its PopoverCard (OD-0095, PopoverHost.Prepare), and a view has one parent.
/// </summary>
public static class PopoverCardContent
{
    /// <summary>The view, off the card it hung on (none: as it was).</summary>
    public static T Lift<T>(T content) where T : Control
    {
        if (content.Parent is PopoverCard card) card.Child = null;
        return content;
    }
}
