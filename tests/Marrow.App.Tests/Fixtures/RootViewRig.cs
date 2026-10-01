using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Fixtures;

// Shared rig for the RootView page tests (App/Views/RootView.swift:299-450: WelcomeView, ProviderChecklist,
// ProviderStatusRow, OpenProvidersSettingsButton, NoThreadView). Helper functions only; every test body lives in a test class.
public static class RootViewRig
{
    /// <summary>Hosts <paramref name="view"/> in a shown window of the given size, laid out, with the model as its environment.</summary>
    public static Control Show(AppModel model, Control view, double width = 900, double height = 1000)
    {
        var host = new Border();
        AppEnvironment.SetAppModel(host, model);
        host.Child = view;
        var window = new AvaloniaWindow { Content = host, Width = width, Height = height };
        window.Show();
        Layout(host);
        return host;
    }

    public static void Layout(Control host)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            ((AvaloniaWindow)TopLevel.GetTopLevel(host)!).UpdateLayout();
        }
    }

    public static void Close(Control host)
    {
        if (TopLevel.GetTopLevel(host) is AvaloniaWindow window) window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    public static T[] All<T>(Control root) where T : class => LogicalQuery.SelfAndDescendants<T>(root).ToArray();

    public static string AllText(Control control) =>
        string.Join("\n", All<TextBlock>(control).Select(block => block.Text ?? ""));

    /// <summary>The one TextBlock under <paramref name="root"/> whose text is exactly <paramref name="text"/>.</summary>
    public static TextBlock Text(Control root, string text) =>
        Assert.Single(All<TextBlock>(root), block => block.Text == text);

    /// <summary>The rectangle of <paramref name="control"/> in <paramref name="ancestor"/>'s coordinate space.</summary>
    public static Rect In(Control control, Control ancestor) =>
        new(control.TranslatePoint(default, ancestor) ?? throw new InvalidOperationException("not under the ancestor"), control.Bounds.Size);

    /// <summary>Chrome.primaryText: the label colour (UI/Chrome/Chrome.swift:76).</summary>
    public static SwiftColor Primary => SwiftColor.LabelColor;

    /// <summary>Chrome.secondaryText: the label colour at 0.7 (UI/Chrome/Chrome.swift:79, ED-062).</summary>
    public static SwiftColor Secondary => SwiftColor.LabelColor.Opacity(0.7);

    public static Color Resolved(SwiftColor token, Control control) => token.Resolve(control.ActualThemeVariant);

    /// <summary>The block's foreground is exactly the colour <paramref name="token"/> names.</summary>
    public static void AssertForeground(TextBlock block, SwiftColor token) =>
        Assert.Equal(Resolved(token, block), Assert.IsAssignableFrom<ISolidColorBrush>(block.Foreground).Color);

    /// <summary>The symbol's foreground is exactly the colour <paramref name="token"/> names.</summary>
    public static void AssertForeground(SwiftImage image, SwiftColor token) =>
        Assert.Equal(Resolved(token, image), Assert.IsAssignableFrom<ISolidColorBrush>(image.Foreground).Color);

    /// <summary>A text style at the size ED-059 gives it, in Inter, at the given weight.</summary>
    public static void AssertStyle(TextBlock block, double size, FontWeight weight)
    {
        Assert.Equal(size, block.FontSize);
        Assert.Equal(weight, block.FontWeight);
        Assert.Equal("Inter", block.FontFamily.Name);
    }

    /// <summary>Puts one status into the registry before any row is built (ProviderRegistry's Statuses has a private setter).</summary>
    public static void Seed(AppModel model, ProviderKind kind, ProviderStatus status) =>
        typeof(ProviderRegistry).GetProperty("Statuses")!.SetValue(model.Providers,
            model.Providers.Statuses.Mutating(map => map[kind] = status));

    /// <summary>An installed provider: a CLI one has an executable (a path nothing ever launches), an API-key one a key.</summary>
    public static ProviderStatus Installed(ProviderKind kind, ProviderStatus.Auth auth) => kind.IsAPIKeyBased
        ? new() { ApiKeyConfigured = true, ProviderStatusAuth = auth }
        : new() { Executable = SwiftURL.FileURLWithPath("/fixture/never-launch"), ProviderStatusAuth = auth };

    /// <summary>The row's trailing control as a user sees it: "spinner", "check", the label of the single button, or a description of a mix.</summary>
    public static string Trailing(Control row)
    {
        var buttons = All<Button>(row);
        var bars = All<ProgressBar>(row);
        var checks = All<SwiftImage>(row).Where(image => image.SymbolName == "checkmark.circle.fill").ToArray();
        if (bars.Length == 1 && buttons.Length == 0 && checks.Length == 0) return "spinner";
        if (checks.Length == 1 && bars.Length == 0 && buttons.Length == 0) return "check";
        if (buttons.Length == 1 && bars.Length == 0 && checks.Length == 0) return AllText(buttons[0]);
        return $"mixed: {buttons.Length} buttons, {bars.Length} spinners, {checks.Length} checks";
    }

    /// <summary>Hosts a provider row at the 480-point width of its card, at its own height.</summary>
    public static Control ShowRow(AppModel model, Control row) =>
        Show(model, new StackPanel { Children = { row }, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top }, 480, 300);
}
