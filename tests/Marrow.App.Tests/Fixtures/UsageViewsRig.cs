using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// Shared rig for the App/Views/Usage/UsageViews.swift tests. One AppModel on a throwaway harness (R-TEST-9, R-TEST-10:
/// nothing here reads a provider, a CLI or the network), the usage registry state published from fixtures, the view shown
/// in a headless window on the Avalonia dispatcher, and small queries over the logical tree. Built and disposed inside
/// <c>HeadlessSession.OnUiThread</c>.
/// </summary>
public sealed class UsageViewsRig : IDisposable
{
    private readonly AppModelHarness harness = new();
    private readonly IDisposable actor;
    private readonly IDisposable frames;
    private readonly IDisposable workspace;
    private readonly List<Avalonia.Controls.Window> windows = [];

    public UsageViewsRig()
    {
        actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        frames = FrameClock.Source.Override(new ManualFrameSource());
        workspace = Marrow.UI.Common.Workspace.Platform.Override(new Marrow.UI.Common.Workspace.Backend
        {
            Home = @"C:\fixture home",
            Which = name => name == "pwsh.exe" ? @"C:\fixture\pwsh.exe" : name == "wt.exe" ? @"C:\fixture\wt.exe" : null,
            Launch = (executable, arguments, _, _) => Launches.Add((executable, arguments.ToArray())),
        });
        Model = new AppModel();
        // The one real spend path a test can take without a provider: Codex with no executable fails closed with
        // ResetCreditError.CodexUnavailable before any process or network is touched.
        Model.Settings.SetBinaryPath(System.IO.Path.Combine(harness.Scratch, "no-cli", "codex.exe"), ProviderKind.Codex);
    }

    public AppModel Model { get; }

    /// <summary>Every terminal launch (executable, arguments) a click made while the rig lives; nothing real is started
    /// (Workspace.Platform fails closed in tests unless a test overrides it).</summary>
    public List<(string Executable, string[] Arguments)> Launches { get; } = [];

    public ProviderRegistry Registry => Model.Providers;

    /// <summary>The harness's manual clock: <c>SwiftDate.Now</c> and every <c>Task.sleep</c> follow it.</summary>
    public Marrow.Testing.ManualTimeProvider Time => harness.Main.Time;

    public static void Drain() => Dispatcher.UIThread.RunJobs();

    /// <summary>Runs the settle sleep forward by <paramref name="by"/> and the main-actor work it releases.</summary>
    public void Advance(TimeSpan by)
    {
        Time.Advance(by, Drain);
        Drain();
    }

    // ---- registry state: the published properties have private setters, so a fixture writes them by reflection ----

    public void PublishLimits(ProviderKind provider, PlanLimits? limits) =>
        SetMap(nameof(ProviderRegistry.PlanLimits), provider, limits);

    public void PublishCredits(ProviderKind provider, ProviderCredits? credits) =>
        SetMap(nameof(ProviderRegistry.Credits), provider, credits);

    public void SetLoadingLimits(ProviderKind provider, bool loading) =>
        SetSet(nameof(ProviderRegistry.LoadingLimits), provider, loading);

    public void SetLoadingCredits(ProviderKind provider, bool loading) =>
        SetSet(nameof(ProviderRegistry.LoadingCredits), provider, loading);

    private void SetMap<T>(string property, ProviderKind provider, T? value) where T : class
    {
        var current = (ValueMap<ProviderKind, T>)typeof(ProviderRegistry).GetProperty(property)!.GetValue(Registry)!;
        var next = current.Mutating(map =>
        {
            if (value is null) map.Remove(provider);
            else map[provider] = value;
        });
        typeof(ProviderRegistry).GetProperty(property)!.SetValue(Registry, next);
        Drain();
    }

    private void SetSet(string property, ProviderKind provider, bool present)
    {
        var current = (ValueSet<ProviderKind>)typeof(ProviderRegistry).GetProperty(property)!.GetValue(Registry)!;
        var next = current.Mutating(set =>
        {
            if (present) set.Add(provider);
            else set.Remove(provider);
        });
        typeof(ProviderRegistry).GetProperty(property)!.SetValue(Registry, next);
        Drain();
    }

    // ---- showing a view ----

    /// <summary>Hosts <paramref name="view"/> (given this rig's AppModel) in a shown, laid-out window of the given width.</summary>
    public T Show<T>(T view, double width = 400, double height = 600) where T : Control
    {
        view.HorizontalAlignment = HorizontalAlignment.Stretch;
        view.VerticalAlignment = VerticalAlignment.Top;
        var host = new Border { Child = view };
        AppEnvironment.SetAppModel(host, Model);
        var window = new Avalonia.Controls.Window { Width = width, Height = height, Content = host };
        windows.Add(window);
        window.Show();
        Drain();
        Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Drain();
        return view;
    }

    /// <summary>Lays the view out again after a state change.</summary>
    public void Settle()
    {
        Drain();
        Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Drain();
    }

    public void Dispose()
    {
        foreach (var window in windows) window.Close();
        Drain();
        frames.Dispose();
        workspace.Dispose();
        actor.Dispose();
        harness.Dispose();
    }
}

/// <summary>Fixtures and queries for the UsageViews tests: plan limits, banked resets and credits as the readers shape them.</summary>
public static class UsageViewsProbe
{
    public static PlanLimits.Window Meter(string id, string title, double percent, DateTimeOffset? resetsAt = null, string? detail = null) =>
        new() { Id = id, Title = title, Percent = percent, ResetsAt = resetsAt, Detail = detail };

    public static PlanLimits Limits(
        PlanLimits.Window[]? windows = null,
        string? planName = null,
        PlanLimits.ResetCredits? resets = null,
        string? problem = null) =>
        new()
        {
            PlanName = planName,
            Windows = new ValueList<PlanLimits.Window>(windows ?? []),
            PlanLimitsResetCredits = resets,
            Problem = problem,
        };

    public static PlanLimits.ResetCredit Credit(string id, string? title = null, string? detail = null, DateTimeOffset? expiresAt = null) =>
        new() { Id = id, Title = title, Detail = detail, ExpiresAt = expiresAt };

    public static PlanLimits.ResetCredits Resets(long available, params PlanLimits.ResetCredit[] listed) =>
        new() { AvailableCount = available, Credits = new ValueList<PlanLimits.ResetCredit>(listed) };

    public static ProviderCredits Balance(double total = 12.34, bool usable = true, double? monthly = null, double? toppedUp = null, double? granted = null) =>
        new() { Total = total, Currency = "USD", IsUsable = usable, Monthly = monthly, ToppedUp = toppedUp, Granted = granted };

    // ---- queries over the logical tree ----

    public static T[] All<T>(ILogical root) where T : class => LogicalQuery.SelfAndDescendants<T>(root).ToArray();

    public static string[] Texts(ILogical root) => All<TextBlock>(root).Select(block => block.Text ?? "").ToArray();

    public static TextBlock Text(ILogical root, string value) =>
        All<TextBlock>(root).FirstOrDefault(block => block.Text == value)
        ?? throw new Xunit.Sdk.XunitException("no TextBlock reading \"" + value + "\"; found: " + string.Join(" | ", Texts(root)));

    public static bool HasText(ILogical root, string value) => All<TextBlock>(root).Any(block => block.Text == value);

    public static Button[] Buttons(ILogical root) => All<Button>(root);

    public static Button ButtonTitled(ILogical root, string title) =>
        Buttons(root).FirstOrDefault(button => button.Content as string == title)
        ?? throw new Xunit.Sdk.XunitException("no Button titled \"" + title + "\"; found: " + string.Join(" | ", Buttons(root).Select(b => b.Content)));

    public static bool HasButton(ILogical root, string title) => Buttons(root).Any(button => button.Content as string == title);

    public static GlassEffect? GlassBehind(Button button) =>
        Assert.IsAssignableFrom<Panel>(button.Parent).Children.Select(SwiftGlass.GetEffect).FirstOrDefault(effect => effect is not null);

    public static void Click(Button button)
    {
        Assert.True(button.IsEffectivelyEnabled, "the button is disabled");
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        UsageViewsRig.Drain();
    }

    public static ProgressBar[] Spinners(ILogical root) => All<ProgressBar>(root).Where(bar => bar.IsIndeterminate).ToArray();

    /// <summary>Left, top, right and bottom of <paramref name="control"/> in <paramref name="relativeTo"/>'s coordinates.</summary>
    public static Rect BoundsIn(Control control, Visual relativeTo)
    {
        var origin = control.TranslatePoint(default, relativeTo) ?? throw new Xunit.Sdk.XunitException("control is not in the same visual tree");
        return new Rect(origin, control.Bounds.Size);
    }

    // ---- fonts and colours ----

    public static Color Resolved(SwiftColor token) => token.Resolve(ThemeVariant.Light);

    /// <summary>The block's resolved foreground is exactly the colour <paramref name="token"/> names.</summary>
    public static void AssertColor(TextBlock block, SwiftColor token)
    {
        var brush = Assert.IsAssignableFrom<ISolidColorBrush>(block.Foreground);
        Assert.Equal(Resolved(token), brush.Color);
    }

    /// <summary>A Swift text style at the size ED-059 gives it, Inter, with the weight and digit traits.</summary>
    public static void AssertStyle(TextBlock block, double size, FontWeight? weight = null, bool monospacedDigit = false)
    {
        Assert.Equal(size, block.FontSize);
        Assert.Equal(weight ?? FontWeight.Normal, block.FontWeight);
        Assert.Equal("Inter", block.FontFamily.Name);
        var features = (block.FontFeatures ?? []).Select(feature => feature.Tag + "=" + feature.Value).ToArray();
        if (monospacedDigit) Assert.Contains(features, tag => tag.StartsWith("tnum", StringComparison.Ordinal) || tag.Contains("tnum", StringComparison.Ordinal));
        else Assert.Empty(features);
    }

    /// <summary>The width a single line of <paramref name="text"/> takes in <paramref name="font"/>, margins aside.</summary>
    public static double NaturalWidth(string text, SwiftFont font)
    {
        var probe = new TextBlock { Text = text, TextWrapping = TextWrapping.NoWrap };
        font.Apply(probe);
        probe.Measure(Size.Infinity);
        return probe.DesiredSize.Width;
    }

    public const double Body = 13;
    public const double Callout = 12;
    public const double Subheadline = 11;
    public const double Footnote = 10;
}
