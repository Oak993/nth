using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

// The production presenter (the port's popover host: PopoverHost.Prepare, the content placeholder, Follow, the grow-in), asked for the
// documented overlay host that Avalonia 12.1.2 headless can open (as Marrow.UI.Tests' PopoverContentSwapReviewTests does) with real
// light dismissal and hit testing, and the host given the content template a theme would give it. It also remembers the popups it was
// asked to present, for a view that keeps its popups out of the visual tree.
public sealed class SettingsRestPopupRecorder(IPopoverPresenter production) : IPopoverPresenter
{
    public List<Popup> Popups { get; } = [];

    public void SetPresented(Popup popup, bool presented)
    {
        if (!Popups.Contains(popup)) Popups.Add(popup);
        popup.ShouldUseOverlayLayer = true;
        production.SetPresented(popup, presented);
        if (presented && typeof(Popup).GetProperty("Host", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(popup) is OverlayPopupHost host)
            host.Template = new FuncControlTemplate<OverlayPopupHost>((owner, scope) => new ContentPresenter { Name = "PART_ContentPresenter", [!ContentPresenter.ContentProperty] = owner[!ContentControl.ContentProperty] }.RegisterInNameScope(scope));
    }
}
