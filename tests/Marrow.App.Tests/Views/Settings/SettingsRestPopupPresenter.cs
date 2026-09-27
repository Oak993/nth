using Avalonia.Controls.Primitives;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

// Avalonia 12.1.2 headless needs a real VisualLayerManager; forcing the documented overlay host
// keeps real popup hit testing and light-dismiss input, rather than fabricating routed click events.
public sealed class SettingsRestPopupPresenter : IPopoverPresenter
{
    public void SetPresented(Popup popup, bool presented) { popup.ShouldUseOverlayLayer = true; if (presented) { Assert.NotNull(popup.PlacementTarget); Assert.True(OverlayLayer.GetOverlayLayer(popup.PlacementTarget) is not null, "Popup anchor detached: " + popup.PlacementTarget.GetType().Name); Assert.True(OverlayLayer.GetOverlayLayer(Avalonia.Controls.TopLevel.GetTopLevel(popup.PlacementTarget)!) is not null, "TopLevel overlay missing"); } popup.IsOpen = presented; if (presented && popup.Host is OverlayPopupHost host) host.Template = new FuncControlTemplate<OverlayPopupHost>((owner, scope) => new ContentPresenter { Name = "PART_ContentPresenter", [!ContentPresenter.ContentProperty] = owner[!ContentControl.ContentProperty] }.RegisterInNameScope(scope)); }
}
