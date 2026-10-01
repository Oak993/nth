// PORT-ONLY. The default control themes this app registers, for controls built without their own template.
//
// ENGINEERING DECISION: the app carries no theme package (Marrow.App/Port/MarrowApplication.cs), so every control is
// expected to carry its own template. A ScrollViewer built without one fell back to a bare ContentPresenter: its extent
// was zero, the wheel and Offset did nothing, and tall content was centred and clipped at both ends (probe, 2026-10-01;
// 22 sites, among them the composer text view, the diff inspector, popover lists, Markdown code blocks). SwiftUI's
// ScrollView scrolls by default, so the default theme here gives a ScrollViewer the port's own scroll template, the same
// recipe as ThreadTimeline's and SettingsView's (a ScrollContentPresenter with the touch gesture recognizer, no visible
// scroll bars), and a transparent background so the wheel works over its whole frame, as NSScrollView's does
// (ThreadTimeline's fix of the same date). A control with an explicit Template keeps it: a local value wins over the
// theme's setter. Registered at the one shared builder point, next to WithSwiftFonts (ED-107 (5)).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input.GestureRecognizers;
using Avalonia.Media;
using Avalonia.Styling;

namespace Marrow.UI.Port;

/// <summary>PORT-ONLY. Default templates for controls built without one (see the file note).</summary>
public static class PortControlThemes
{
    private static readonly FuncControlTemplate<ScrollViewer> ScrollTemplate = new((viewer, scope) =>
    {
        var presenter = new ScrollContentPresenter
        {
            Name = "PART_ContentPresenter",
            Background = Brushes.Transparent,
            [!ContentPresenter.ContentProperty] = viewer[!ContentControl.ContentProperty],
            [!ContentPresenter.PaddingProperty] = viewer[!TemplatedControl.PaddingProperty],
        };
        presenter.GestureRecognizers.Add(new ScrollGestureRecognizer { CanVerticallyScroll = true, CanHorizontallyScroll = true });
        return presenter.RegisterInNameScope(scope);
    });

    /// <summary>The ScrollViewer default theme: the port's scroll template.</summary>
    public static ControlTheme ScrollViewerTheme { get; } = new(typeof(ScrollViewer))
    {
        Setters = { new Setter(TemplatedControl.TemplateProperty, ScrollTemplate) },
    };

    /// <summary>Registers the default themes on the application, after setup (app and test builders alike).</summary>
    public static AppBuilder WithPortControlThemes(this AppBuilder builder) =>
        builder.AfterSetup(setup => Register(setup.Instance!));

    /// <summary>Puts the default themes into <paramref name="application"/>'s resources, keyed by control type.</summary>
    public static void Register(Application application) => application.Resources[typeof(ScrollViewer)] = ScrollViewerTheme;
}
