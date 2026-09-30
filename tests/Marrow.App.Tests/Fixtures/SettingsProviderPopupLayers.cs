using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;

namespace Marrow.App.Tests.Fixtures;

// The same Avalonia 12.1.2 outer popup-layer template used by SettingsRestFixture.
// Keep the existing content layer: the provider zoom proof intentionally transforms it.
internal static class SettingsProviderPopupLayers
{
    internal static void Install(Avalonia.Controls.Window window)
    {
        window.Template = new FuncControlTemplate<Avalonia.Controls.Window>((owner, scope) =>
        {
            var layers = new VisualLayerManager
            {
                EnableOverlayLayer = true,
                EnableAdornerLayer = true,
                Child = new ContentPresenter
                {
                    Name = "PART_ContentPresenter",
                    [!ContentPresenter.ContentProperty] = owner[!ContentControl.ContentProperty],
                }.RegisterInNameScope(scope),
            };
            var popupLayer = typeof(VisualLayerManager).GetProperty("EnablePopupOverlayLayer", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMemberException(typeof(VisualLayerManager).FullName, "EnablePopupOverlayLayer");
            popupLayer.SetValue(layers, true);
            return layers;
        });
    }
}
