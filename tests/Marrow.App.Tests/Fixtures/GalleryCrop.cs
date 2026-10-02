namespace Marrow.App.Tests.Window.Native;

// What a gallery capture covers.
internal enum GalleryCrop
{
    /// <summary>The window's own frame.</summary>
    Window,

    /// <summary>The window and the popup windows it has opened (a popover may reach past the window's edge).</summary>
    WithPopups,

    /// <summary>The popup windows alone, with a small margin: a close view of a popover.</summary>
    PopupsOnly,
}
