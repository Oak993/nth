using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input.GestureRecognizers;
using Avalonia.Media;
using Avalonia.Styling;

namespace Marrow.Setup;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3 and 5; unit U1, ED-NEW-inst-u1-1). The setup's Avalonia application: no theme
/// package and no runtime XAML (every view is built in code, so the setup can be compiled ahead of time). The only
/// control it shows that needs a template is the body's ScrollViewer; it gets a presenter-only template (no visible bar:
/// the body scrolls only when the work area is smaller than the window's content).
/// </summary>
public sealed class SetupApp : Application
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
        presenter.GestureRecognizers.Add(new ScrollGestureRecognizer { CanVerticallyScroll = true, CanHorizontallyScroll = false });
        return presenter.RegisterInNameScope(scope);
    });

    /// <summary>The ScrollViewer's default theme.</summary>
    public static ControlTheme ScrollViewerTheme { get; } = new(typeof(ScrollViewer))
    {
        Setters = { new Setter(TemplatedControl.TemplateProperty, ScrollTemplate) },
    };

    public override void Initialize() => Resources[typeof(ScrollViewer)] = ScrollViewerTheme;

    /// <summary>
    /// The real app: Win32 with Skia and HarfBuzz only (no other platform's code is kept). The renderer is the build's
    /// <c>SetupRenderingMode</c> (PLAN.md 5, "Rendering"): <c>AngleEgl</c> (with Software as Windows' own fallback) unless the
    /// lead's measurement picks <c>Software</c>, which drops ANGLE's library.
    /// </summary>
    public static AppBuilder Build() => AppBuilder.Configure<SetupApp>()
        .UseWin32()
        .UseSkia()
        .UseHarfBuzz()
        .With(new Win32PlatformOptions { RenderingMode = RenderingModes(RenderingModeName()) });

    public static string RenderingModeName() =>
        typeof(SetupApp).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "SetupRenderingMode")?.Value ?? "AngleEgl";

    /// <summary>The renderers to try, in order, for a build's <c>SetupRenderingMode</c>.</summary>
    public static IReadOnlyList<Win32RenderingMode> RenderingModes(string name) =>
        string.Equals(name, "Software", StringComparison.OrdinalIgnoreCase)
            ? [Win32RenderingMode.Software]
            : [Win32RenderingMode.AngleEgl, Win32RenderingMode.Software];
}
