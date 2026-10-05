using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Marrow.App.Tests.Fixtures;

// Headless support for the staging accessibility regressions. Pending-surface helpers stay on the review branch.
internal static class A11yReview
{

    // Headless Linux cannot resolve Windows system-family names or $Default. Map them to
    // the same bundled release fonts that WithSwiftFonts already registers. Production is untouched.
    public static AppBuilder WithA11yLinuxFonts(this AppBuilder builder) => !OperatingSystem.IsLinux() ? builder : builder.With(new Avalonia.Media.FontManagerOptions
    {
        DefaultFamilyName = "fonts:Inter#Inter",
        FontFamilyMappings = new Dictionary<string, Avalonia.Media.FontFamily>
        {
            ["Cascadia Mono"] = new("fonts:CascadiaMono#Cascadia Mono"),
            ["Consolas"] = new("fonts:CascadiaMono#Cascadia Mono"),
            ["monospace"] = new("fonts:CascadiaMono#Cascadia Mono"),
        },
    });

    public static IEnumerable<Control> Tree(Control root) => root.GetVisualDescendants().OfType<Control>().Prepend(root);

    public static byte[] Pixels(Avalonia.Controls.Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        using var stream = new MemoryStream();
        frame.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        return stream.ToArray();
    }

    public static void VisibleFocus(Avalonia.Controls.Window window, Control target)
    {
        window.FocusManager!.Focus(null);
        var before = Pixels(window);
        Assert.True(target.Focus(NavigationMethod.Tab), "The interactive control must accept keyboard focus.");
        Assert.Same(target, window.FocusManager.GetFocusedElement());
        var after = Pixels(window);
        Assert.False(before.SequenceEqual(after), "Keyboard focus changed no rendered pixel: the control has no visible focus indicator.");
    }

    public static void HasLiveText(Control root, string expected)
    {
        var regions = Tree(root).Where(c => AutomationProperties.GetLiveSetting(c) != AutomationLiveSetting.Off).ToArray();
        Assert.True(regions.Length > 0, "Progress/state changes have no live region in the mounted UI.");
        Assert.Contains(regions, region => Tree(region).OfType<TextBlock>().Any(t => t.Text?.Contains(expected, StringComparison.Ordinal) == true));
    }
}
