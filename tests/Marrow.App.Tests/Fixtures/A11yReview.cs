using System.Linq.Expressions;
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Marrow.App.Tests.Fixtures;

// Review support only. Reflection keeps the review branch buildable on the requested baseline;
// staged-only tests require the fetched feature branches and fail explicitly if their fixture is absent.
internal static class A11yReview
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

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

    public static object Get(object owner, string name) => owner.GetType().GetProperty(name, Members)!.GetValue(owner)!;

    public static object? Call(object owner, string name, params object?[] args) =>
        owner.GetType().GetMethods(Members).Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(owner, args);

    public static Type PendingFixture(string name) => typeof(HeadlessSession).Assembly.GetType("Marrow.App.Tests.Fixtures." + name)
        ?? throw new InvalidOperationException($"Review prerequisite: {name} requires its pending feature branch. See review-a11y-1.md; this is not a reproduced accessibility assertion.");

    public static void Skills(HeadlessSession headless, Action<object> check)
    {
        var run = PendingFixture("LibraryPageHarness").GetMethod("Run", Members)!;
        var parameters = run.GetParameters();
        var rig = Expression.Parameter(parameters[2].ParameterType.GenericTypeArguments[0]);
        var callback = Expression.Lambda(parameters[2].ParameterType,
            Expression.Invoke(Expression.Constant(check), Expression.Convert(rig, typeof(object))), rig).Compile();
        var args = parameters.Select(p => p.HasDefaultValue ? p.DefaultValue : null).ToArray();
        args[0] = headless;
        args[1] = Enum.Parse(parameters[1].ParameterType, "Skills");
        args[2] = callback;
        run.Invoke(null, args);
    }

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

    public static Control Id(Control root, string id) => Tree(root).Single(c => AutomationProperties.GetAutomationId(c) == id);

    public static void HasLiveText(Control root, string expected)
    {
        var regions = Tree(root).Where(c => AutomationProperties.GetLiveSetting(c) != AutomationLiveSetting.Off).ToArray();
        Assert.True(regions.Length > 0, "Progress/state changes have no live region in the mounted UI.");
        Assert.Contains(regions, region => Tree(region).OfType<TextBlock>().Any(t => t.Text?.Contains(expected, StringComparison.Ordinal) == true));
    }
}
