using Avalonia.Controls.Primitives;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Window.Native;

// The popover seam with the real presenter underneath: every popup the app presents still opens as a real Avalonia popup window,
// and this keeps a list of them so a gallery scene can reach the content of the one that is open (its controls live in a window
// of their own, outside the main window's visual tree).
internal sealed class GalleryPresenter(IPopoverPresenter real) : IPopoverPresenter
{
    private readonly List<Popup> presented = [];

    /// <summary>Puts the recording presenter in as the process default; the real one carries on underneath.</summary>
    internal static GalleryPresenter Install()
    {
        var presenter = new GalleryPresenter(Presentation.Presenter.Current);
        Presentation.Presenter.SetProcessDefault(presenter);
        return presenter;
    }

    public void SetPresented(Popup popup, bool presented)
    {
        if (presented && !this.presented.Contains(popup)) this.presented.Add(popup);
        real.SetPresented(popup, presented);
    }

    /// <summary>The popups that are open now, the most recent last.</summary>
    internal IReadOnlyList<Popup> Open => presented.Where(popup => popup.IsOpen).ToArray();
}
