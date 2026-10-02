using Avalonia;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Platform;
using Avalonia.Styling;

namespace Marrow.UI.Tests.Fixtures;

/// <summary>
/// What a native Popup builds when it opens, in a headless window. Avalonia 12.1.2 headless returns null from
/// CreatePopup, so Popup.Open cannot make its PopupRoot here (the reason IPopoverPresenter has a recording stand-in).
/// <see cref="Open"/> makes the same PopupRoot, from a headless implementation of IPopupImpl, and wires it as
/// Popup.Open does (Popup.cs, Open(): SetChild, SetParent(popup), and the root's ActualThemeVariant bound to the
/// popup's), so what the popup's content resolves in its own top-level can be read.
/// </summary>
internal static class HeadlessPopupHost
{
    /// <summary>A window whose template has the overlay layers a popup in the overlay needs (the same template the
    /// App tests use), so a real presenter can open a Popup headless.</summary>
    internal static Window OverlayWindow(Control content)
    {
        var window = new Window { Width = 400, Height = 300, Content = content };
        window.Template = new FuncControlTemplate<Window>((owner, scope) =>
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
            typeof(VisualLayerManager).GetProperty("EnablePopupOverlayLayer", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(layers, true);
            return layers;
        });
        return window;
    }

    /// <summary>The popup's own top-level, holding <paramref name="content"/>.</summary>
    internal static PopupRoot Open(Popup popup, TopLevel owner, Control content)
    {
        var implementation = typeof(Avalonia.Headless.AvaloniaHeadlessPlatformOptions).Assembly.GetType("Avalonia.Headless.HeadlessWindowImpl")!
            .GetConstructors().Single().Invoke([true, PixelFormat.Bgra8888]);
        var root = new PopupRoot(owner, (IPopupImpl)implementation);
        root.SetChild(content);
        ((ISetLogicalParent)root).SetParent(popup);
        root.Bind(ThemeVariantScope.ActualThemeVariantProperty, popup.GetBindingObservable(ThemeVariantScope.ActualThemeVariantProperty));
        return root;
    }
}
