using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.App.Views.Usage;
using Marrow.Core.Support;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Theme;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Settings;

// Source: DroppyCode/App/Views/Settings/SettingsView.swift (B3 52bc792), :66-79,566-799.
// Shared values: Chrome.swift:100-104,1012-1026,1055-1087. ED-059/OD-0009: body 13, subheadline 11, Inter.
// ED-062: secondaryText = primaryText.opacity(0.7). ED-089: direct Windows Terminal login, no macOS script.
// ED-180/181: recorded glass/continuous data; macOS pixel parity remains UNVERIFIED OD-0001.
[Collection("Headless")]
public sealed class SettingsProviderProofsTests(HeadlessSession headless)
{
    private static T[] Nodes<T>(Control root) where T : class => LogicalQuery.SelfAndDescendants<T>(root).ToArray();
    private static string[] Text(Control root) => Nodes<TextBlock>(root).Select(x => x.Text ?? "").ToArray();
    private static T Field<T>(object root, string name) => (T)root.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(root)!;
    private static void Seed(AppModel model, ProviderKind kind, ProviderStatus status) => typeof(ProviderRegistry).GetProperty("Statuses")!.SetValue(model.Providers, model.Providers.Statuses.Mutating(map => map[kind] = status));
    private static ChromeRow Row(Control root, string title) => Nodes<ChromeRow>(root).Single(x => Text(x).First() == title);
    private static ProviderSettingsSection Section(Control root, ProviderKind kind) => Nodes<ProviderSettingsSection>(root).Single(x => Field<ProviderKind>(x, "provider") == kind);
    private static Grid StatusRow(Control root) => Nodes<Grid>(root).Single(x => x.ColumnDefinitions.Count == 3 && x.Children.FirstOrDefault() is ProviderIcon);
    private static StackPanel Labels(Control root) => Assert.IsType<StackPanel>(StatusRow(root).Children[1]);
    private static Button Header(Control root) => Assert.Single(Nodes<Button>(Assert.Single(Nodes<CollapsibleProviderHeader>(root))));
    private static void Sync() => Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    private static void Layout(Control root) { for (var i = 0; i < 5; i++) { Assert.IsType<Avalonia.Controls.Window>(TopLevel.GetTopLevel(root)).UpdateLayout(); Sync(); } }
    private static void Click(Button button) => PointerClick(button);
    private static void Key(Control target, Avalonia.Input.Key key)
    {
        Assert.True(target.Focus()); var window = Assert.IsType<Avalonia.Controls.Window>(TopLevel.GetTopLevel(target));
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.Space, null); window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.Space, null);
    }
    private static void PointerClick(Control target)
    {
        var window = Assert.IsType<Avalonia.Controls.Window>(TopLevel.GetTopLevel(target)); Layout(target); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Sync(); using var rendered = window.CaptureRenderedFrame();
        var point = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
        Assert.InRange(point.X, 0, window.ClientSize.Width); Assert.InRange(point.Y, 0, window.ClientSize.Height);
        var hit = Assert.IsAssignableFrom<Control>(window.InputHitTest(point));
        Assert.True(ReferenceEquals(hit, target) || hit.GetVisualAncestors().Contains(target), $"Pointer hit {hit.GetType().Name} {hit.Bounds}, expected {target.GetType().Name} {target.Bounds} at {point}; content={(target as Button)?.Content}.");
        window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
    }
    private static void Frames(AppModelHarness h, double ms) { h.Main.AdvanceMilliseconds(ms); ((ManualFrameSource)FrameClock.Source.Current).Advance(TimeSpan.FromMilliseconds(ms)); Sync(); }
    private static void Wait(Func<bool> done)
    {
        var elapsed = Stopwatch.StartNew();
        while (!done() && elapsed.Elapsed < AppModelHarness.Bound) { Sync(); Thread.Sleep(1); }
        Sync(); Assert.True(done(), "Offline production operation did not settle.");
    }
    private void WithView(Func<Control> make, Action<Control, AppModel, AppModelHarness> check, Action<AppModel>? seed = null)
    {
        headless.OnUiThread(() =>
        {
            using var h = new AppModelHarness();
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            using var wallpaper = WallpaperStore.Shared.Override((WallpaperStore)Activator.CreateInstance(typeof(WallpaperStore), true)!);
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            using var updates = UpdateChecker.OverrideShared(new UpdateChecker(AppInfo.Current, [], ""));
            var model = new AppModel();
            // ED-211 / R-TEST-10: refresh can resolve only deliberately nonexistent fixture paths.
            foreach (var provider in SwiftEnum<ProviderKind>.AllCases.Where(x => !x.IsAPIKeyBased)) model.Settings.SetBinaryPath(Path.Combine(h.Scratch, "missing-cli.exe"), provider);
            seed?.Invoke(model);
            var host = new Border(); AppEnvironment.SetAppModel(host, model); var view = make(); host.Child = view;
            var window = new Avalonia.Controls.Window { Content = new Avalonia.Controls.Primitives.VisualLayerManager { EnableOverlayLayer = true, Child = host }, Width = 780, Height = view is SettingsView ? 580 : 6000 };
            SettingsProviderPopupLayers.Install(window);
            try { window.Show(); Layout(view); check(view, model, h); }
            finally { window.Close(); Sync(); SettingsNavigation.Shared.RequestedPage = null; }
        });
    }
    [Fact]
    [Trait("Rule", "R-TEST-7")]
    public void The_provider_fixture_hosts_a_real_tooltip_and_preserves_pointer_delivery() => WithView(
        () => new Border { Width = 220, Height = 40, Background = Brushes.White, Child = new TextBlock { Text = "Owned fixture target" } },
        (root, _, _) =>
        {
            var window = Assert.IsType<Avalonia.Controls.Window>(TopLevel.GetTopLevel(root));
            var tip = new ToolTip { Content = "Owned fixture tooltip" };
            ToolTip.SetTip(root, tip);
            root.SetValue(ToolTip.ShowDelayProperty, 0);
            var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var watch = ToolTip.ToolTipOpeningEvent.AddClassHandler(typeof(Control), (sender, _) =>
            {
                if (ReferenceEquals(sender, root)) opened.TrySetResult();
            }, RoutingStrategies.Direct | RoutingStrategies.Bubble, handledEventsToo: true);
            var presses = 0;
            root.PointerPressed += (_, _) => presses++;
            try
            {
                Layout(root);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Sync();
                window.MouseMove(new Point(1, 1)); Sync();
                var at = root.TranslatePoint(new Point(root.Bounds.Width / 2, root.Bounds.Height / 2), window)!.Value;
                var hit = Assert.IsAssignableFrom<Control>(window.InputHitTest(at));
                Assert.True(ReferenceEquals(hit, root) || hit.GetVisualAncestors().Contains(root));
                window.MouseMove(at); Sync();
                HeadlessSession.RunDispatcherUntil(opened.Task, TimeSpan.FromSeconds(5)); Sync();
                Assert.True(ToolTip.GetIsOpen(root));
                Assert.Same(window, TopLevel.GetTopLevel(tip));
                Assert.Contains(window.GetVisualDescendants(), visual => visual is Avalonia.Controls.Primitives.OverlayPopupHost);
                window.MouseDown(at, MouseButton.Left); window.MouseUp(at, MouseButton.Left); Sync();
                Assert.Equal(1, presses);
            }
            finally { ToolTip.SetIsOpen(root, false); window.MouseMove(new Point(1, 1)); Sync(); }
        });

    private static void ColorIs(Control owner, IBrush? actual, SwiftColor expected)
    {
        // Chrome.swift:76,79 / ED-051,062: appearance-dependent tokens, including the 0.7 alpha derivation.
        var window = Assert.IsType<Avalonia.Controls.Window>(TopLevel.GetTopLevel(owner)); var saved = window.RequestedThemeVariant;
        try
        {
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            { window.RequestedThemeVariant = theme; Sync(); Assert.Equal(theme, owner.ActualThemeVariant); Assert.Equal(expected.Resolve(theme), Assert.IsAssignableFrom<ISolidColorBrush>(actual).Color); }
        }
        finally { window.RequestedThemeVariant = saved; Sync(); }
    }
    private static void HeaderMetrics(Control section)
    {
        // SettingsView.swift:650-675 `HStack(spacing: 12)`, `.frame(width: 22)`, VStack spacing 2, padding 16/10/11.
        var grid = StatusRow(section); Assert.Equal(12, grid.ColumnSpacing); Assert.Equal(new Thickness(16, 11, 10, 11), grid.Margin);
        Assert.Equal(new GridLength(22), grid.ColumnDefinitions[0].Width); Assert.Equal(GridUnitType.Star, grid.ColumnDefinitions[1].Width.GridUnitType);
        Assert.Equal(GridLength.Auto, grid.ColumnDefinitions[2].Width); Assert.Equal(2, Labels(section).Spacing);
        Assert.IsType<SettingsSwitch>(grid.Children[2]); Assert.Equal(2, Grid.GetColumn(grid.Children[2]));
        Layout(section); Assert.Equal(1, Grid.GetColumn(Labels(section)));
        foreach (var text in Labels(section).Children.Cast<TextBlock>()) { Inter(text); Leading(text, Labels(section)); }
        if (Labels(section).Children.Count == 2) Assert.Equal(2, Labels(section).Children[1].Bounds.Y - Labels(section).Children[0].Bounds.Bottom, 6);
    }
    private static void Inter(TextBlock text) => Assert.Equal("fonts:Inter#Inter", text.FontFamily.ToString()); // OD-0009
    private static void Leading(Control child, Control parent)
    {
        Layout(parent); Assert.Equal(0, child.TranslatePoint(default, parent)!.Value.X, 6);
        // Avalonia Start is leading in this left-to-right window; Left is the explicit equivalent.
        if (child is TextBlock text) Assert.True(text.TextAlignment is TextAlignment.Start or TextAlignment.Left);
    }
    private static void Collapsed(Control section, bool collapsed)
    {
        // :648,763-766 fraction, clipped/disabled/hit-testing/accessibilityHidden.
        var reveal = Assert.Single(Nodes<ChromeRevealLayout>(section)); Assert.True(reveal.ClipToBounds);
        Assert.Equal(!collapsed, reveal.IsEnabled); Assert.Equal(!collapsed, reveal.IsHitTestVisible);
        Assert.Equal(collapsed ? AccessibilityView.Raw : AccessibilityView.Content, AutomationProperties.GetAccessibilityView(reveal));
        foreach (var control in Nodes<Control>(reveal).Where(x => x is Button or SmallSwitch or TextBox)) Assert.Equal(!collapsed, control.IsEffectivelyEnabled);
        if (collapsed) { Assert.Equal(0, reveal.Fraction); Layout(section); Assert.Equal(0, reveal.Bounds.Height); }
        else if (reveal.Fraction == 1) { Layout(section); Assert.Equal(Assert.Single(Nodes<ChromeCard>(reveal)).Bounds.Height, reveal.Bounds.Height); }
    }
    private static void Open(Control section, AppModelHarness h) { Click(Header(section)); Sync(); Frames(h, 1000); Layout(section); Collapsed(section, false); Assert.Equal(1, Assert.Single(Nodes<ChromeRevealLayout>(section)).Fraction); }
    private static void Close(Control section, AppModelHarness h) { Click(Header(section)); Sync(); Frames(h, 1000); Layout(section); Collapsed(section, true); }
    private static void RowMetrics(ChromeRow row, string title, string? detail)
    {
        // Chrome.swift:1055-1087 HStack spacing 16 + Spacer(minLength:12), VStack spacing 2, padding 16/10/11 (vertical Chrome.SettingsRowVerticalPadding, ED-NEW-textsize-2).
        var border = Assert.IsType<Border>(row.Child); Assert.Equal(new Thickness(16, Chrome.SettingsRowVerticalPadding, 10, Chrome.SettingsRowVerticalPadding), border.Padding);
        var grid = Assert.IsType<Grid>(border.Child); Assert.Equal(VerticalAlignment.Center, grid.VerticalAlignment);
        var labels = Assert.IsType<StackPanel>(grid.Children[0]); Assert.Equal(2, labels.Spacing);
        Layout(row); Leading(labels, grid);
        var texts = labels.Children.Cast<TextBlock>().ToArray(); foreach (var text in texts) { Inter(text); Leading(text, labels); }
        if (detail is not null) { Assert.Equal(2, texts[1].Bounds.Y - texts[0].Bounds.Bottom, 6); Assert.Equal(0, texts[1].MaxLines); Assert.Equal(double.PositiveInfinity, texts[1].MaxHeight); }
        Assert.Equal(title, texts[0].Text); Assert.Equal(Chrome.SettingsRowTitleSize, texts[0].FontSize); Assert.Equal(FontWeight.Normal, texts[0].FontWeight); ColorIs(texts[0], texts[0].Foreground, SwiftColor.LabelColor);
        if (detail is not null) { Assert.Equal(detail, texts[1].Text); Assert.Equal(Chrome.SettingsRowDetailSize, texts[1].FontSize); Assert.Equal(Chrome.SettingsRowDetailLineHeight, texts[1].LineHeight); Assert.Equal(FontWeight.Normal, texts[1].FontWeight); Assert.Equal(TextWrapping.WrapWithOverflow, texts[1].TextWrapping); Assert.Equal(TextTrimming.CharacterEllipsis, texts[1].TextTrimming); ColorIs(texts[1], texts[1].Foreground, SwiftColor.LabelColor.Opacity(0.7)); } // ED-NEW-textsize-1: a word wider than the line ends in an ellipsis
        var control = grid.Children.Single(x => !ReferenceEquals(x, labels));
        Assert.Equal(44, control.Bounds.Left - labels.Bounds.Right, 6); // :1062,1072: rendered 16 + 12 + 16 gap around Spacer(minLength: 12).
        Assert.Equal(grid.Bounds.Width, control.Bounds.Right, 6); Assert.Equal(new Thickness(0), control.Margin); Assert.Equal(HorizontalAlignment.Right, control.HorizontalAlignment); Assert.Equal(VerticalAlignment.Center, control.VerticalAlignment);
    }
    private static void Divider(ChromeRowDivider divider)
    {
        // Chrome.swift:1082-1087 leading 16 only; the hairline is the label colour at 0.10 (SOURCE FACT, reference runtime S 199.5 dark and gap-R2-05 S 141.5 light).
        Assert.Equal(new Thickness(16, 0, 0, 0), divider.Margin); var line = Assert.IsType<Border>(divider.Child);
        Assert.Equal(new Thickness(0, 0, 0, 1), line.BorderThickness); Assert.Equal(1, line.Height);
        Assert.Equal(Chrome.Overlay(0.12).Resolve(line.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(line.BorderBrush).Color); // the label colour is 0.85 opaque: Overlay(0.12) is the 0.10 line
    }
    private static void PrecededByDivider(ChromeRow row)
    {
        var siblings = Assert.IsType<StackPanel>(row.Parent).Children; var index = siblings.IndexOf(row); Assert.True(index > 0); Divider(Assert.IsType<ChromeRowDivider>(siblings[index - 1]));
    }
    private static Button GlassLink(ChromeRow row, string label)
    {
        var button = Assert.Single(Nodes<Button>(row)); var text = Assert.Single(Nodes<TextBlock>(button)); Assert.Equal(label, text.Text); Assert.Equal(16, text.Height); // :724,743,754
        var glass = Assert.Single(button.GetVisualDescendants().OfType<Control>(), x => SwiftGlass.GetEffect(x) is not null); var effect = SwiftGlass.GetEffect(glass)!; // the regular accent glass pill (ED-280), its surface in the button's template
        Assert.Equal(GlassShapeKind.Capsule, effect.Shape.Kind); Assert.Equal(RoundedCornerStyle.Continuous, effect.Shape.Style); Assert.True(effect.IsInteractive); Assert.Equal(Chrome.Accent, effect.TintColor); Assert.Equal(1, effect.TintOpacity); Assert.Equal(SwiftGlass.GlassButtonRegularHeight, glass.Bounds.Height, 3);
        Assert.Equal(SwiftGlass.GlassButtonRegularFontSize, text.FontSize); Assert.Equal(Colors.White, Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color); // the label is the pill's white 13 point
        return button;
    }
    // Tests-first reflection: minimal endpoint boundary, default identity; HTTP and parsing stay production.
    private static IDisposable Endpoints(Type api, LoopbackFeedServer server) => Endpoints(api, server.Url);
    private static IDisposable Endpoints(Type api, Func<string, string> url)
    {
        var property = api.GetProperty("Endpoint", BindingFlags.Static | BindingFlags.NonPublic); Assert.NotNull(property);
        return Assert.IsType<Ambient<Func<Uri, Uri>>>(property.GetValue(null)).Override(endpoint => new Uri(url(endpoint.PathAndQuery)));
    }
    private static byte[] Draw(Control control)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)control.Width, (int)control.Height));
        bitmap.Render(control);
        using var stream = new MemoryStream(); bitmap.Save(stream, new PngBitmapEncoderOptions()); return stream.ToArray();
    }
    [Fact]
    [Trait("Rule", "R-TEST-10")] // ProviderRegistry.swift:299-310; CommandCodeAPI.swift:15-35; isolate both home paths.
    public void Provider_usage_home_and_HOME_are_bound_to_the_fixture_before_any_login_lookup() => WithView(() => new ProvidersSettingsPage(), (_, model, h) =>
    {
        Assert.Equal(h.Home, LoginEnvironment.HomeDirectory);
        Assert.Equal(h.Home, LoginEnvironment.Current["HOME"]);
        Assert.Equal(h.Home, model.Providers.Environment(ProviderKind.Commandcode)["HOME"]);
    });

    [Fact]
    [Trait("Req", "UI-PROVREG-001")] // App/Views/Settings/SettingsView.swift:9,23,38,55,72,138-177
    public void Providers_sidebar_matches_every_keyword_in_source_order_and_click_mounts_the_page() => WithView(() => new SettingsView(), (view, _, _) =>
    {
        // :72 quoted, including B3 Go, spark and coding-plan terms; match every term through real shell search.
        var words = "codex|claude|cursor|opencode|opencode zen|opencode go|zen|grok|deepseek|meta|muse|spark|zai|z.ai|glm|coding plan|devin|cognition|antigravity|agy|google|gemini|copilot|github|command code|commandcode|cmd|binary|path|sign in|login|api key|usage|limits|limit|plan|quota|credits|balance".Split('|');
        Assert.Equal(words, SettingsPage.Providers.Keywords); Assert.Equal(SettingsPage.Providers, UpstreamSettingsPages.All[3]); /* SA-1: upstream index, the port-added pages taken out */
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark }) Assert.Equal(new SwiftColor(0.040, 0.478, 1.000).Resolve(theme), SettingsPage.Providers.Tint.Resolve(theme));
        var sidebar = Field<Control>(view, "sidebar"); var field = Assert.Single(Nodes<TextBox>(sidebar));
        foreach (var word in words) { field.Text = word.ToUpperInvariant(); Sync(); Assert.Contains(Nodes<SidebarRow>(sidebar), row => Text(row).Contains("Providers")); }
        field.Text = ""; Sync(); SettingsNavigation.Shared.RequestedPage = SettingsPage.General; Sync(); var providerRow = Nodes<SidebarRow>(sidebar).Single(row => Text(row).Contains("Providers"));
        var shape = Nodes<Border>(providerRow).Single(x => x.Height == Chrome.SettingsNavRowHeight); Assert.Equal(new CornerRadius(7), shape.CornerRadius); Assert.Equal(new Thickness(8, 0), shape.Padding);
        var title = Assert.Single(Nodes<TextBlock>(providerRow)); Assert.Equal(Chrome.SettingsNavTitleSize, title.FontSize); Assert.Equal(FontWeight.Normal, title.FontWeight);
        Assert.Equal("cpu", Assert.Single(Nodes<SwiftImage>(providerRow)).SymbolName); Assert.Equal(8, Nodes<StackPanel>(providerRow).Single().Spacing);
        PointerClick(Nodes<Button>(providerRow).Single()); Sync(); Assert.Single(Nodes<ProvidersSettingsPage>(view)); Assert.Equal(16, Nodes<ProviderSettingsSection>(view).Length);
        var selected = Nodes<SidebarRow>(sidebar).Single(row => Text(row).Contains("Providers")); Assert.Equal(FontWeight.Medium, Nodes<TextBlock>(selected).Single().FontWeight);
    });

    [Fact]
    [Trait("Req", "UI-PROVREG-003")] // App/Views/Settings/SettingsView.swift:254-255,585-614: scrolling keeps every eager card mounted.
    public void Refresh_is_the_only_probe_and_is_symbol_only_disabled_and_single_flight() => WithView(() => new SettingsView(), (view, model, h) =>
    {
        SettingsNavigation.Shared.RequestedPage = SettingsPage.General; Sync(); Assert.Empty(Nodes<ProvidersRefreshButton>(view));
        Assert.Empty(model.Providers.Statuses); SettingsNavigation.Shared.RequestedPage = SettingsPage.Providers; Sync();
        Layout(view); Assert.Empty(model.Providers.Statuses);
        var codex = Section(view, ProviderKind.Codex); Open(codex, h); var keptField = Nodes<TextBox>(codex).Single(); keptField.Text = Path.Combine(h.Scratch, "scroll-preserved-cli.exe"); Sync();
        var scroll = Nodes<ScrollViewer>(Field<Control>(view, "detail")).First(); /* the page's own; the rounded-border fields carry one each (their PART_ScrollViewer) */ var scrollWindow = Assert.IsType<Avalonia.Controls.Window>(TopLevel.GetTopLevel(scroll)); Layout(scroll); using var scrollFrame = scrollWindow.CaptureRenderedFrame();
        var wheelPoint = scroll.TranslatePoint(new Point(scroll.Bounds.Width / 2, scroll.Bounds.Height / 2), scrollWindow)!.Value; Assert.NotNull(scrollWindow.InputHitTest(wheelPoint)); scrollWindow.MouseMove(wheelPoint); scrollWindow.MouseWheel(wheelPoint, new Vector(0, -3)); Layout(view); Assert.True(scroll.Offset.Y > 0, $"Wheel hit {scrollWindow.InputHitTest(wheelPoint)?.GetType().Name}; extent={scroll.Extent}, viewport={scroll.Viewport}, presenter={scroll.GetVisualDescendants().OfType<ScrollContentPresenter>().First().Offset}."); Assert.Equal(16, Nodes<ProviderSettingsSection>(view).Length); Assert.Empty(model.Providers.Statuses);
        Assert.Same(codex, Section(view, ProviderKind.Codex)); Assert.Same(keptField, Nodes<TextBox>(codex).Single()); Assert.Equal(Path.Combine(h.Scratch, "scroll-preserved-cli.exe"), keptField.Text);
        var refresh = Assert.Single(Nodes<ProvidersRefreshButton>(view)); Assert.Single(Nodes<ChromeCapsule>(refresh)); Assert.Single(Nodes<ChromeIconButton>(refresh)); Assert.Empty(Text(refresh));
        var symbol = Assert.Single(Nodes<SwiftImage>(refresh)); Assert.Equal("arrow.clockwise", symbol.SymbolName); Assert.Equal(SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Semibold), SwiftImage.GetFont(symbol));
        Assert.Single(Nodes<Border>(refresh), x => x.Padding == new Thickness(3, 2));
        Assert.Single(Nodes<Control>(refresh), x => SwiftGlass.GetEffect(x) is not null); var button = Assert.Single(Nodes<Button>(refresh)); Assert.Equal("Check providers again", ToolTip.GetTip(button)); Assert.True(button.IsEnabled);
        // Hold RefreshAll's task-group hops so the real click and single-flight guard can be observed, and hold the executable lookups (off the main actor
        // since T44, on the thread pool) at the seam until the refresh in flight has been looked at: however fast the pool is, the refresh cannot finish
        // inside the pump that delivers the click (ED-NEW-handoverfix-1, as ED-NEW-ignis-integration's amendment does for SettingsViewFinishProvidersTests).
        var lookups = 0; using var probes = new ManualResetEventSlim(false); using var seam = ProviderRegistry.SetCLIUpdaterExecutableForTesting(_ => { Interlocked.Increment(ref lookups); probes.Wait(TimeSpan.FromSeconds(30)); return null; });
        Task? duplicate = null;
        using (MainActor.Executor.Override(h.Main.Loop))
        {
            h.Main.Loop.Enqueue(() => Click(button)); h.Main.RunUntilIdle();
            h.Main.Loop.Enqueue(() => duplicate = model.Providers.RefreshEverything()); h.Main.RunUntilIdle();
        }
        Assert.True(model.Providers.IsRefreshing); Assert.True(duplicate!.IsCompletedSuccessfully);
        Sync(); button = Assert.Single(Nodes<Button>(refresh)); Assert.False(button.IsEnabled); Assert.Equal("Checking providers…", ToolTip.GetTip(button));
        var disabledIcon = Assert.Single(Nodes<SwiftImage>(refresh)); ColorIs(disabledIcon, disabledIcon.Foreground, SwiftColor.LabelColor.Opacity(0.32)); // Chrome.swift:355
        probes.Set(); Wait(() => { h.Main.RunUntilIdle(); return !model.Providers.IsRefreshing; }); Assert.True(lookups > 0); Assert.Equal(16, model.Providers.Statuses.Count); Assert.True(Assert.Single(Nodes<Button>(refresh)).IsEnabled);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "UI-PROVREG-003")] // App/Views/Settings/SettingsView.swift:585-615,646-766; UI/Chrome/Chrome.swift:100-104,1012-1026
    public void Eager_cards_keep_source_shape_auth_order_collapsed_height_and_exact_reveal_curve(bool reduce) => WithView(() => new ProvidersSettingsPage(), (view, model, h) =>
    {
        using var reduceMotion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = reduce });
        var stack = Assert.IsType<StackPanel>(((ProvidersSettingsPage)view).Child); Assert.Equal(20, stack.Spacing);
        var all = Nodes<ProviderSettingsSection>(view); Assert.Equal(16, all.Length); Assert.All(all, section => Leading(section, stack));
        Assert.Equal(new[] { "Codex", "Claude", "Cursor", "OpenCode Zen", "OpenCode Go", "Grok", "DeepSeek", "Meta", "Z.ai", "Devin", "Antigravity", "Copilot", "Command Code", "Pi", "OpenRouter", "Local" }, all.Select(section => AutomationProperties.GetName(Header(section))));
        foreach (var section in all)
        {
            var group = Assert.IsType<StackPanel>(section.Child); Assert.Equal(10, group.Spacing); Assert.IsType<CollapsibleProviderHeader>(group.Children[0]); Assert.IsType<ChromeRevealLayout>(group.Children[1]); Assert.All(group.Children, child => Leading(child, group)); Assert.Equal(22, Header(section).Height); Collapsed(section, true); Assert.Equal(32, section.Bounds.Height);
            var title = Nodes<TextBlock>(Nodes<CollapsibleProviderHeader>(section).Single()).Single(); Inter(title); Assert.Equal(13, title.FontSize); Assert.Equal(FontWeight.SemiBold, title.FontWeight); ColorIs(title, title.Foreground, SwiftColor.Secondary);
            var card = Assert.Single(Nodes<ChromeCard>(section)); Assert.False(card.Lazy); var shape = Assert.IsType<Border>(card.Child); Assert.Equal(new CornerRadius(16), shape.CornerRadius); Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(shape)); ColorIs(shape, shape.Background, SwiftColor.LabelColor.Opacity(0.03)); Assert.Equal(0, Assert.IsType<StackPanel>(shape.Child).Spacing);
        }
        var meta = Section(view, ProviderKind.Meta); var cardBefore = Assert.Single(Nodes<ChromeCard>(meta));
        Seed(model, ProviderKind.Meta, new ProviderStatus { ApiKeyConfigured = true, ProviderStatusAuth = new ProviderStatus.Auth.SignedIn(null) }); Sync(); Layout(view);
        Assert.Same(meta, Nodes<ProviderSettingsSection>(view)[0]); Assert.Equal(1, Nodes<ChromeRevealLayout>(meta).Single().Fraction); Collapsed(meta, false);
        Seed(model, ProviderKind.Meta, new ProviderStatus()); Sync(); Layout(view); Assert.Same(meta, Section(view, ProviderKind.Meta)); Collapsed(meta, true); Assert.Contains("Get a key", Text(meta));
        var reveal = Nodes<ChromeRevealLayout>(meta).Single(); Click(Header(meta)); Sync(); Assert.Equal(0, reveal.Fraction);
        Frames(h, 90); Layout(view); Assert.Equal(0, cardBefore.Bounds.X); Assert.Equal(0, cardBefore.Bounds.Y); Assert.Equal(Math.Ceiling(cardBefore.Bounds.Height * reveal.Fraction), reveal.Bounds.Height, 6); Assert.Equal((reduce ? Animation.EaseOut(0.18) : Animation.Spring(response: 0.32, dampingFraction: 0.9)).Progress(0.09), reveal.Fraction, 7); // :100-104 exact curve
        Frames(h, 1000); Layout(view); Assert.Equal(1, reveal.Fraction); Assert.Equal(cardBefore.Bounds.Height, reveal.Bounds.Height); Assert.Same(cardBefore, Nodes<ChromeCard>(meta).Single());
        Seed(model, ProviderKind.Meta, new ProviderStatus { ApiKeyConfigured = true, ProviderStatusAuth = new ProviderStatus.Auth.SignedOut() }); Sync(); Assert.Equal(1, reveal.Fraction); Assert.DoesNotContain("Get a key", Text(meta)); Close(meta, h);
        Assert.DoesNotContain(model.Providers.Statuses.Values, status => status.IsChecking);
    });

    [Fact]
    [Trait("Req", "UI-PROVREG-004")] // App/Views/Settings/SettingsView.swift:651-653; UI/Common/Components.swift:4-14; Core/Models/Provider.swift:59-60
    public void Status_marks_are_eighteen_in_a_twenty_two_frame_with_thirteen_assets_and_no_state_variant() => WithView(() => new ProvidersSettingsPage(), (view, _, h) =>
    {
        var assets = new[] { "provider-codex", "provider-claude", "provider-cursor", "provider-opencode", "provider-opencode", "provider-grok", "provider-deepseek", "provider-meta", "provider-zai", "provider-devin", "provider-antigravity", "provider-copilot", "provider-commandcode", "provider-pi", "provider-openrouter", "provider-local" };
        var sections = Nodes<ProviderSettingsSection>(view); var seen = new List<string>();
        for (var i = 0; i < sections.Length; i++)
        {
            var section = sections[i]; HeaderMetrics(section); var icon = Assert.IsType<ProviderIcon>(StatusRow(section).Children[0]); Assert.Equal(18, icon.Size);
            var image = Assert.IsType<SwiftImage>(icon.Child); Assert.Equal(18, image.Width); Assert.Equal(18, image.Height); Assert.True(image.IsResizable); Assert.Equal(ContentMode.Fit, image.AspectContentMode); Assert.Equal(assets[i], image.AssetName); ColorIs(image, image.Foreground, SwiftColor.LabelColor); seen.Add(image.AssetName!);
            Assert.Empty(Nodes<Button>(icon)); Open(section, h); var before = Draw(image);
            var window = Assert.IsType<Avalonia.Controls.Window>(TopLevel.GetTopLevel(image));
            // Draw(image) renders the icon alone. Refresh the window hit-test scene after the card's
            // layout change before delivering real input, as PointerClick already does above.
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Sync(); using var rendered = window.CaptureRenderedFrame();
            var point = image.TranslatePoint(new Point(9, 9), window)!.Value; Assert.NotNull(window.InputHitTest(point));
            window.MouseMove(point); Sync(); Assert.Equal(before, Draw(image));
            window.MouseDown(point, MouseButton.Left); Sync(); Assert.Equal(before, Draw(image)); window.MouseUp(point, MouseButton.Left);
            image.IsEnabled = false; Sync(); Assert.Equal(before, Draw(image)); image.IsEnabled = true;
            Close(section, h); Assert.Same(image, Nodes<SwiftImage>(icon).Single()); Assert.Equal(before, Draw(image));
        }
        Assert.Equal(15, seen.Distinct().Count()); // source marks, OpenRouter (ED-234), Local (ED-410)
    });

    [Fact]
    [Trait("Req", "UI-PROVREG-005")] // App/Views/Settings/SettingsView.swift:650-656; Services/Providers/ProviderRegistry.swift:17-26
    [Trait("Req", "UI-PROVREG-006")] // App/Views/Settings/SettingsView.swift:657-660,763-766
    public void Summary_uses_all_six_literal_branches_and_checking_override_with_cli_only_version() => WithView(() => new ProvidersSettingsPage(), (view, model, h) =>
    {
        var section = Section(view, ProviderKind.Codex); Open(section, h); var executable = SwiftURL.FileURLWithPath("/fixture/no-real-cli");
        foreach (var (status, expected) in new (ProviderStatus, string)[] {
            (new(), "Not installed"),
            (new() { Executable = executable, ProviderStatusAuth = new ProviderStatus.Auth.SignedIn("fixture@example.invalid") }, "Signed in as fixture@example.invalid"),
            (new() { Executable = executable, ProviderStatusAuth = new ProviderStatus.Auth.SignedIn(null) }, "Signed in"),
            (new() { Executable = executable, ProviderStatusAuth = new ProviderStatus.Auth.SignedIn("**fixture** `literal` [text](https://example.invalid)") }, "Signed in as **fixture** `literal` [text](https://example.invalid)"),
            (new() { Executable = executable, ProviderStatusAuth = new ProviderStatus.Auth.SignedOut() }, "Not signed in"),
            (new() { Executable = executable, Version = "1.2.3-beta+1" }, "Installed · 1.2.3-beta+1"),
            (new() { Executable = executable }, "Installed") })
        {
            Seed(model, ProviderKind.Codex, status); Sync(); HeaderMetrics(section); var summary = Assert.IsType<TextBlock>(Labels(section).Children[0]); Assert.Equal(expected, summary.Text); Assert.Equal(13, summary.FontSize); Assert.Equal(FontWeight.Normal, summary.FontWeight); ColorIs(summary, summary.Foreground, SwiftColor.LabelColor);
            Seed(model, ProviderKind.Codex, status with { IsChecking = true }); Sync(); Assert.Equal("Checking…", Assert.IsType<TextBlock>(Labels(section).Children[0]).Text);
        }
        Seed(model, ProviderKind.Codex, new ProviderStatus { Version = " 1.2.3+raw " }); Sync(); var version = Assert.IsType<TextBlock>(Labels(section).Children[1]); Assert.Equal("Version  1.2.3+raw ", version.Text); Assert.Equal(11, version.FontSize); Assert.Equal(FontWeight.Normal, version.FontWeight); ColorIs(version, version.Foreground, SwiftColor.LabelColor.Opacity(0.7));
        Seed(model, ProviderKind.Codex, new ProviderStatus { Version = "" }); Sync(); Assert.Equal(new[] { "Not installed", "Version " }, Text(Labels(section)));
        Close(section, h); Seed(model, ProviderKind.Codex, new ProviderStatus()); Sync(); Assert.Single(Labels(section).Children);
        foreach (var kind in new[] { ProviderKind.Deepseek, ProviderKind.Meta, ProviderKind.Zai }) { Seed(model, kind, new ProviderStatus { Version = "API" }); Sync(); Assert.Single(Labels(Section(view, kind)).Children); }
    });
    [Theory]
    [InlineData(ProviderKind.Deepseek, "platform.deepseek.com", "api.deepseek.com", "https://platform.deepseek.com/api_keys")]
    [InlineData(ProviderKind.Meta, "dev.meta.ai", "api.meta.ai/v1", "https://dev.meta.ai/")]
    [InlineData(ProviderKind.Zai, "z.ai/manage-apikey/apikey-list", "api.z.ai/api/coding/paas/v4", "https://z.ai/manage-apikey/apikey-list")]
    [Trait("Req", "UI-PROVAPI-001")] // App/Views/Settings/SettingsView.swift:650-665,763-766
    [Trait("Req", "UI-PROVAPI-002")] // App/Views/Settings/SettingsView.swift:689-717; Chrome.swift:1055-1087
    [Trait("Req", "UI-PROVAPI-003")] // App/Views/Settings/SettingsView.swift:689-717; Chrome.swift:1055-1087
    [Trait("Req", "UI-PROVAPI-004")] // App/Views/Settings/SettingsView.swift:689-717; Chrome.swift:1055-1087
    [Trait("Req", "UI-PROVAPI-007")] // App/Views/Settings/SettingsView.swift:719-728,594-610
    [Trait("Req", "UI-PROVAPI-008")] // App/Views/Settings/SettingsView.swift:719-728,594-610
    [Trait("Req", "UI-PROVAPI-009")] // App/Views/Settings/SettingsView.swift:719-728,594-610
    [Trait("Req", "UI-PROVREG-007")] // App/Views/Settings/SettingsView.swift:657-665
    [Trait("Req", "UI-PROVREG-040")] // App/Views/Settings/SettingsView.swift:719-728
    public void Native_API_and_key_rows_have_all_metrics_gates_colors_and_literal_links(ProviderKind provider, string source, string host, string url) => WithView(() => new ProvidersSettingsPage(), (view, model, h) =>
    {
        var name = provider switch { ProviderKind.Deepseek => "DeepSeek", ProviderKind.Meta => "Meta", ProviderKind.Zai => "Z.ai", _ => throw new UnreachableException() }; // Provider.swift:29-31 literal display names
        var section = Section(view, provider); Collapsed(section, true); Assert.Single(Labels(section).Children); Open(section, h); HeaderMetrics(section);
        var key = Row(section, "API key"); RowMetrics(key, "API key", $"Stored in your Credential Manager. Get one at {source}."); PrecededByDivider(key);
        var get = Row(section, "Get a key"); RowMetrics(get, "Get a key", name + " needs an API key. Nothing to install."); PrecededByDivider(get); var link = GlassLink(get, "Get a " + name + " key");
        string? opened = null; using var browser = Marrow.Services.Port.Workspace.Open.Override(value => { opened = value.AbsoluteString; return true; }); Click(link); Assert.Equal(url, opened);
        Seed(model, provider, new ProviderStatus { ApiKeyConfigured = true, IsChecking = true, Version = "API", ProviderStatusAuth = new ProviderStatus.Auth.SignedOut() }); Sync();
        Assert.DoesNotContain("Get a key", Text(section)); Assert.DoesNotContain("Version API", Text(section));
        Assert.DoesNotContain("Binary path", Text(section)); Assert.DoesNotContain("Install", Text(section)); Assert.DoesNotContain("Sign in", Text(section)); HeaderMetrics(section); Assert.Equal(2, Labels(section).Children.Count); Assert.Equal("Checking…", ((TextBlock)Labels(section).Children[0]).Text);
        var native = Assert.IsType<TextBlock>(Labels(section).Children[1]); Assert.Equal($"Native API · {host}", native.Text); Assert.Equal(11, native.FontSize); Assert.Equal(FontWeight.Normal, native.FontWeight); ColorIs(native, native.Foreground, SwiftColor.LabelColor.Opacity(0.7));
        Close(section, h); Assert.Same(native, Labels(section).Children[1]); Seed(model, provider, new ProviderStatus()); Sync(); Assert.Single(Labels(section).Children);
        // Command Code is optional-key CLI: nil apiHost never occupies Native API's slot.
        Seed(model, ProviderKind.Commandcode, new ProviderStatus { ApiKeyConfigured = true, Version = "CLI raw" }); Sync(); var command = Section(view, ProviderKind.Commandcode); Assert.Equal(new[] { "Installed · CLI raw", "Version CLI raw" }, Text(Labels(command))); Assert.DoesNotContain(Text(command), text => text.StartsWith("Native API", StringComparison.Ordinal));
    });

    [Fact]
    [Trait("Req", "UI-PROVREG-037")] // App/Views/Settings/SettingsView.swift:689-717,784-799
    [Trait("Req", "UI-PROVREG-039")] // App/Views/Settings/SettingsView.swift:708-717
    [Trait("Req", "UI-PROVAPI-006")] // App/Views/Settings/SettingsView.swift:708-717
    public void Four_key_rows_and_plain_eyes_edit_real_presenters_keep_local_state_and_reset_on_rebuild() => WithView(() => new ProvidersSettingsPage(), (view, model, h) =>
    {
        // Provider.swift:122-130 sources; SettingsView.swift:693-695 detail; OD-0012 G2 substitution.
        var sources = new Dictionary<ProviderKind, string> { [ProviderKind.Deepseek] = "platform.deepseek.com", [ProviderKind.Meta] = "dev.meta.ai", [ProviderKind.Zai] = "z.ai/manage-apikey/apikey-list", [ProviderKind.Commandcode] = "commandcode.ai/studio", [ProviderKind.OpenRouter] = "openrouter.ai/keys" };
        var shown = new List<Button>();
        foreach (var provider in SwiftEnum<ProviderKind>.AllCases)
        {
            var section = Section(view, provider); Collapsed(section, true);
            if (!sources.TryGetValue(provider, out var source)) { Assert.DoesNotContain(Text(section), text => text.StartsWith("API key", StringComparison.Ordinal)); continue; }
            Open(section, h); var title = provider == ProviderKind.Commandcode ? "API key (optional)" : "API key";
            var detail = provider == ProviderKind.Commandcode ? "Instead of `cmd login`: a Studio key from commandcode.ai/studio, stored in your Credential Manager and handed to the CLI." : "Stored in your Credential Manager. Get one at " + source + ".";
            var row = Row(section, title); RowMetrics(row, title, detail); PrecededByDivider(row);
            var field = Nodes<TextBox>(row).Single(); Assert.Equal(200, field.Width); Assert.Equal(12, field.FontSize); Assert.Equal("sk-…", field.PlaceholderText); Assert.Equal(SwiftFont.System(12, design: SwiftFont.Design.Monospaced).Resolve().FontFamily, field.FontFamily);
            var pair = Nodes<StackPanel>(row).Single(x => x.Orientation == Orientation.Horizontal); Assert.Equal(8, pair.Spacing); Assert.Same(field, pair.Children[0]); var eye = Assert.IsType<Button>(pair.Children[1]); Assert.Equal(new Thickness(0), eye.BorderThickness); Assert.Equal(new Thickness(0), eye.Padding); Assert.DoesNotContain(Nodes<Control>(eye), x => SwiftGlass.GetEffect(x) is not null);
            var image = Assert.Single(Nodes<SwiftImage>(eye)); Assert.Equal("eye", image.SymbolName); Assert.Equal(SwiftFont.System(12), SwiftImage.GetFont(image)); ColorIs(image, image.Foreground, SwiftColor.LabelColor.Opacity(0.7)); Assert.Equal("Show the API key", ToolTip.GetTip(eye));
            PointerClick(field); Assert.True(field.Focus()); Assert.IsType<Avalonia.Controls.Window>(TopLevel.GetTopLevel(field)).KeyTextInput("fixture-key-never-sent"); Sync(); Assert.Equal("fixture-key-never-sent", model.Settings.ApiKeyInput(provider)); var presenter = Assert.Single(field.GetVisualDescendants().OfType<TextPresenter>()); Assert.Equal('•', presenter.PasswordChar);
            Click(eye); Sync(); Assert.Equal('\0', presenter.PasswordChar); Assert.Equal("Hide the API key", ToolTip.GetTip(eye)); Assert.Equal("eye.slash", Nodes<SwiftImage>(eye).Single().SymbolName); Assert.Equal(SwiftFont.System(12), SwiftImage.GetFont(Nodes<SwiftImage>(eye).Single())); ColorIs(Nodes<SwiftImage>(eye).Single(), Nodes<SwiftImage>(eye).Single().Foreground, SwiftColor.LabelColor.Opacity(0.7));
            field.Text = ""; Sync(); Close(section, h); shown.Add(eye); // Empty key replaces debounce, no credential leaves fixture.
        }
        Assert.Equal(5, shown.Count); Assert.All(shown, eye => Assert.Equal("Hide the API key", ToolTip.GetTip(eye)));
        var parent = Assert.IsType<Border>(view.Parent); parent.Child = new ProvidersSettingsPage(); Sync(); Layout(parent);
        foreach (var eye in Nodes<Button>(parent).Where(x => ToolTip.GetTip(x) is string text && text.Contains("the API key", StringComparison.Ordinal))) Assert.Equal("Show the API key", ToolTip.GetTip(eye));
    });

    [Fact]
    [Trait("Req", "UI-PROVAPI-010")] // App/Views/Settings/SettingsView.swift:667-672; App/AppSettings.swift disabledProviders
    [Trait("Req", "UI-PROVREG-008")] // App/Views/Settings/SettingsView.swift:296-305,667-672
    public void Provider_switches_toggle_and_persist_with_accent_pixels_picker_filter_and_collapsed_gates() => WithView(() => new ProvidersSettingsPage(), (view, model, h) =>
    {
        foreach (var provider in SwiftEnum<ProviderKind>.AllCases)
        {
            var section = Section(view, provider); HeaderMetrics(section); Open(section, h); var toggle = Assert.Single(Nodes<SmallSwitch>(section)); Assert.Empty(Text(Assert.Single(Nodes<SettingsSwitch>(section)))); Assert.True(toggle.Focusable);
            var peer = Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(toggle)); Assert.Equal(ToggleState.On, peer.ToggleState);
            var raw = SwiftEnum<ProviderKind>.RawValue(provider); Assert.DoesNotContain(raw, model.Settings.DisabledProviders); if (provider == ProviderKind.Local) Assert.DoesNotContain(provider, model.Providers.AvailableProviders); else Assert.Contains(provider, model.Providers.AvailableProviders); // ED-410: Local is available only when discovered
            var before = Draw(toggle); using var png = SKBitmap.Decode(before); var expected = Chrome.AccentNSColor.Resolve(toggle.ActualThemeVariant); var pixel = png.GetPixel(5, 8); Assert.Equal(expected.R, pixel.Red); Assert.Equal(expected.G, pixel.Green); Assert.Equal(expected.B, pixel.Blue);
            PointerClick(toggle); Sync(); Assert.Equal(ToggleState.Off, peer.ToggleState); Assert.Contains(raw, model.Settings.DisabledProviders); Assert.Contains("disabledProviders", h.Defaults.Writes); Assert.DoesNotContain(provider, model.Providers.AvailableProviders);
            Key(toggle, Avalonia.Input.Key.Space); Sync(); Assert.Equal(ToggleState.On, peer.ToggleState); Assert.DoesNotContain(raw, model.Settings.DisabledProviders);
            Seed(model, provider, new ProviderStatus { IsChecking = true }); Sync(); Assert.True(toggle.IsEffectivelyEnabled); Close(section, h); peer.Toggle(); Assert.Equal(ToggleState.On, peer.ToggleState);
        }
    });
    [Fact]
    [Trait("Req", "UI-PROVREG-042")] // App/Views/Settings/SettingsView.swift:730-757; Core/Models/Provider.swift:62-100
    [Trait("Req", "UI-PROVREG-043")] // App/Views/Settings/SettingsView.swift:636-642,747-757
    [Trait("Req", "UI-PROVREG-044")] // App/Views/Settings/SettingsView.swift:750-755; UI/Common/Components.swift:117-132; ED-089
    public void Every_CLI_install_and_login_row_has_literal_text_glass_gates_and_real_terminal_callback() => WithView(() => new ProvidersSettingsPage(), (view, model, h) =>
    {
        var urls = new[] { "https://developers.openai.com/codex/cli", "https://claude.com/product/claude-code", "https://cursor.com/cli", "https://opencode.ai", "https://opencode.ai", "https://x.ai/cli", "https://cli.devin.ai", "https://antigravity.google/docs/cli/overview", "https://github.com/github/copilot-cli", "https://commandcode.ai/docs/quickstart", "https://pi.dev/docs/latest" };
        var names = new[] { "Codex", "Claude", "Cursor", "OpenCode Zen", "OpenCode Go", "Grok", "Devin", "Antigravity", "Copilot", "Command Code", "Pi" }; // Provider.swift:21-38
        var commands = new[] { "codex login", "claude auth login", "cursor-agent login", "opencode auth login", "opencode auth login", "grok login", "devin auth login", "agy", "copilot login", "cmd login", "pi" };
        var launches = new List<(string, string[], string?, bool)>(); using var workspace = Marrow.UI.Common.Workspace.Platform.Override(new Marrow.UI.Common.Workspace.Backend { Home = @"C:\fixture home", Which = name => name == "pwsh.exe" ? @"C:\fixture\pwsh.exe" : name == "wt.exe" ? @"C:\fixture\wt.exe" : null, Launch = (exe, args, cwd, console) => launches.Add((exe, args.ToArray(), cwd, console)) });
        string? opened = null; using var browser = Marrow.Services.Port.Workspace.Open.Override(value => { opened = value.AbsoluteString; return true; });
        var cli = SwiftEnum<ProviderKind>.AllCases.Where(x => !x.IsAPIKeyBased && x != ProviderKind.Local).ToArray(); // ED-410: Local has no CLI
        for (var i = 0; i < cli.Length; i++)
        {
            var provider = cli[i]; var section = Section(view, provider); Collapsed(section, true); Open(section, h);
            var install = Row(section, "Install"); RowMetrics(install, "Install", names[i] + " was not found on your PATH."); PrecededByDivider(install); Assert.DoesNotContain("Sign in", Text(section)); Click(GlassLink(install, "Get " + names[i])); Assert.Equal(urls[i], opened);
            foreach (var auth in new ProviderStatus.Auth[] { new ProviderStatus.Auth.Unknown(), new ProviderStatus.Auth.SignedOut() })
            {
                Seed(model, provider, new ProviderStatus { Executable = SwiftURL.FileURLWithPath("/fixture/missing-cli"), ProviderStatusAuth = auth }); Sync(); Assert.DoesNotContain("Install", Text(section)); var login = Row(section, "Sign in"); RowMetrics(login, "Sign in", "Click here to login again."); PrecededByDivider(login); var button = GlassLink(login, "Login"); Assert.True(button.IsEffectivelyEnabled); Click(button); var launch = launches.Last();
                Assert.Equal(@"C:\fixture\wt.exe", launch.Item1); Assert.Equal(new[] { "-w", "new", "new-tab", "-d", @"C:\fixture home", @"C:\fixture\pwsh.exe", "-NoExit", "-Command", commands[i] }, launch.Item2); Assert.Equal(@"C:\fixture home", launch.Item3); Assert.False(launch.Item4);
            }
            Seed(model, provider, model.Providers.Status(provider) with { IsChecking = true }); Sync(); Assert.DoesNotContain("Sign in", Text(section)); Seed(model, provider, model.Providers.Status(provider) with { IsChecking = false, ProviderStatusAuth = new ProviderStatus.Auth.SignedIn(null) }); Sync(); Assert.DoesNotContain("Sign in", Text(section)); Close(section, h);
        }
        Assert.Equal(22, launches.Count);
        foreach (var api in new[] { ProviderKind.Deepseek, ProviderKind.Meta, ProviderKind.Zai }) { Assert.DoesNotContain("Install", Text(Section(view, api))); Assert.DoesNotContain("Sign in", Text(Section(view, api))); }
    });

    [Fact]
    [Trait("Req", "UI-PROVREG-009")] // App/Views/Settings/SettingsView.swift:677,690,720,731,739,748; UI/Chrome/Chrome.swift:1082-1087; the colour is the reference runtime's (S 199.5, S 141.5), see Row_divider_is_white_at_a_tenth_in_dark_and_black_at_a_tenth_in_light
    public void Every_present_row_and_usage_block_has_one_preceding_inset_divider_in_all_states() => WithView(() => new ProvidersSettingsPage(), (view, model, h) =>
    {
        foreach (var provider in SwiftEnum<ProviderKind>.AllCases)
        {
            var section = Section(view, provider); Open(section, h);
            foreach (var status in new[] { new ProviderStatus(), new ProviderStatus { Executable = SwiftURL.FileURLWithPath("/fixture/missing-cli"), ApiKeyConfigured = true, ProviderStatusAuth = new ProviderStatus.Auth.Unknown() }, new ProviderStatus { ApiKeyConfigured = true, Executable = SwiftURL.FileURLWithPath("/fixture/missing-cli"), ProviderStatusAuth = new ProviderStatus.Auth.SignedOut() }, new ProviderStatus { ApiKeyConfigured = true, Executable = SwiftURL.FileURLWithPath("/fixture/missing-cli"), ProviderStatusAuth = new ProviderStatus.Auth.SignedIn(null) } })
            {
                Seed(model, provider, status); Sync(); foreach (var row in Nodes<ChromeRow>(section)) PrecededByDivider(row);
                var usage = Nodes<StackPanel>(section).SingleOrDefault(x => x.Spacing == 14);
                if (usage is not null) { var slot = Assert.IsType<StackPanel>(usage.Parent); Assert.Equal(2, slot.Children.Count); Divider(Assert.IsType<ChromeRowDivider>(slot.Children[0])); Assert.Same(usage, slot.Children[1]); }
                Assert.Equal(Nodes<ChromeRow>(section).Length + (usage is null ? 0 : 1), Nodes<ChromeRowDivider>(section).Length); Assert.All(Nodes<ChromeRowDivider>(section), Divider);
            }
            Close(section, h);
        }
    });

    [Fact]
    [Trait("Req", "UI-PROVREG-009")] // the divider's colour: SOURCE FACT, the reference runtime measures it as the label colour at 0.10 on the dark card (frames/w-1-settings-tour-198.0/stills/win_199.5.png, y402: (22,66,72) over (43,86,92)) and on the light card (frames/gap-R2-05, S 141.5)
    public void Row_divider_is_white_at_a_tenth_in_dark_and_black_at_a_tenth_in_light() => WithView(() => new ProvidersSettingsPage(), (view, _, _) =>
    {
        var window = Assert.IsType<Avalonia.Controls.Window>(TopLevel.GetTopLevel(view)); var saved = window.RequestedThemeVariant;
        try
        {
            var divider = Nodes<ChromeRowDivider>(view).First(); var line = Assert.IsType<Border>(divider.Child);
            window.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark; Layout(view); Assert.Equal(Color.FromArgb(26, 255, 255, 255), Assert.IsAssignableFrom<ISolidColorBrush>(line.BorderBrush).Color);
            window.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light; Layout(view); Assert.Equal(Color.FromArgb(26, 0, 0, 0), Assert.IsAssignableFrom<ISolidColorBrush>(line.BorderBrush).Color);
        }
        finally { window.RequestedThemeVariant = saved; }
    });

    [Fact]
    [Trait("Req", "UI-PROVREG-010")] // App/Views/Settings/SettingsView.swift:629-634,676-688,772-779; ProviderRegistry.swift:606-641,682-700
    public void Usage_gates_cover_every_provider_and_both_kinds_in_source_order() => WithView(() => new ProvidersSettingsPage(), (view, model, h) =>
    {
        // PlanLimits.swift:76-90 / ProviderCredits.swift:58 exact capability lists, not inferred from output.
        var limitsKinds = new[] { ProviderKind.Claude, ProviderKind.Codex, ProviderKind.Antigravity, ProviderKind.Copilot, ProviderKind.Commandcode, ProviderKind.Pi, ProviderKind.Zai };
        var creditKinds = new[] { ProviderKind.Deepseek, ProviderKind.Commandcode };
        foreach (var kind in SwiftEnum<ProviderKind>.AllCases)
        {
            var section = Section(view, kind);
            foreach (var status in new[] { new ProviderStatus(), new ProviderStatus { ApiKeyConfigured = true, ProviderStatusAuth = new ProviderStatus.Auth.SignedOut() }, new ProviderStatus { ApiKeyConfigured = true, ProviderStatusAuth = new ProviderStatus.Auth.Unknown() }, new ProviderStatus { ApiKeyConfigured = true, ProviderStatusAuth = new ProviderStatus.Auth.SignedIn(null) } })
            {
                Seed(model, kind, status); Sync(); var shown = status.IsInstalled && status.ProviderStatusAuth is not ProviderStatus.Auth.SignedOut;
                Assert.Equal(shown && limitsKinds.Contains(kind) ? 1 : 0, Nodes<PlanLimitsView>(section).Length); Assert.Equal(shown && creditKinds.Contains(kind) ? 1 : 0, Nodes<CreditsView>(section).Length);
                if (Nodes<PlanLimitsView>(section).Length + Nodes<CreditsView>(section).Length > 0) { UsageMetrics(section); var usage = Nodes<StackPanel>(section).Single(x => x.Spacing == 14); Assert.Equal(limitsKinds.Contains(kind) ? typeof(PlanLimitsView) : typeof(CreditsView), usage.Children[0].GetType()); if (usage.Children.Count == 2) Assert.IsType<CreditsView>(usage.Children[1]); }
            }
        }
    });

    private const string Quota = """{"code":200,"data":{"level":"pro","limits":[{"type":"CREDIT_LIMIT","usage":100,"currentValue":25},{"type":"TOKENS_LIMIT","usage":200,"currentValue":50},{"type":"TIME_LIMIT","usage":40,"currentValue":10}]}}""";
    private const string Cards = """{"code":200,"data":{"fiveHourResets":[{"recordId":1,"available":true},{"recordId":2,"available":false}],"weekResets":[{"recordId":3}]}}""";
    private const string Balance = """{"is_available":true,"balance_infos":[{"currency":"USD","total_balance":"12.34","topped_up_balance":"10.00","granted_balance":"2.34"}]}""";
    private static void UsageMetrics(Control section)
    {
        var usage = Nodes<StackPanel>(section).Single(x => x.Spacing == 14); Assert.Equal(new Thickness(16, 12), usage.Margin);
        var slot = Assert.IsType<StackPanel>(usage.Parent); Divider(Assert.IsType<ChromeRowDivider>(slot.Children[0]));
        foreach (var child in usage.Children) Leading(child, usage);
        if (usage.Children.Count == 2) Assert.Equal(14, usage.Children[1].Bounds.Y - usage.Children[0].Bounds.Bottom, 6);
    }
    [Fact]
    [Trait("Req", "UI-PROVAPI-029")] // App/Views/Settings/SettingsView.swift:676-688; Services/Providers/ZaiAPI.swift:55-181
    [Trait("Req", "UI-PROVREG-010")] // App/Views/Settings/SettingsView.swift:629-634,772-779; ProviderRegistry.swift:606-641
    public void Zai_usage_reads_real_quota_and_cards_throttles_and_posts_reset_before_two_second_reread() => WithView(() => new ProvidersSettingsPage(), (view, model, h) =>
    {
        var mode = 0;
        using var server = new SettingsProviderProofServer(request => request.Target.Contains("/use", StringComparison.Ordinal) ? CannedResponse.Json("""{"code":200,"success":true}""") : request.Target.Contains("/list", StringComparison.Ordinal) ? mode == 1 ? CannedResponse.Plain(503, "Unavailable") : CannedResponse.Json(Cards) : mode == 2 ? CannedResponse.Plain(503, "Unavailable") : CannedResponse.Json(mode == 3 ? Quota.Replace("\"usage\":100,", "\"usage\":1000000.4,", StringComparison.Ordinal).Replace("\"currentValue\":25", "\"currentValue\":250000.4", StringComparison.Ordinal).Replace("\"usage\":40,", "\"usage\":40000.4,", StringComparison.Ordinal).Replace("\"currentValue\":10", "\"currentValue\":10000.4", StringComparison.Ordinal) : Quota));
        using var endpoints = Endpoints(typeof(ZaiAPI), server.Url);
        model.Settings.SetAPIKeyInput("fixture-key-never-sent", ProviderKind.Zai);
        Seed(model, ProviderKind.Zai, new ProviderStatus { ApiKeyConfigured = true, ProviderStatusAuth = new ProviderStatus.Auth.SignedIn(null) }); Sync();
        Wait(() => model.Providers.PlanLimits.ContainsKey(ProviderKind.Zai) && !model.Providers.LoadingLimits.Contains(ProviderKind.Zai));
        var section = Section(view, ProviderKind.Zai); Collapsed(section, false); UsageMetrics(section);
        // ZaiAPI.swift:99-122 literal titles/count detail; :69 plan prefix; :143-151 false-only availability filter.
        Assert.Contains("Plan usage limits · Coding Plan Pro", Text(section));
        foreach (var text in new[] { "5-hour limit", "Weekly", "Monthly · MCP tools", "25 of 100 credits", "50 of 200 credits", "10 of 40 calls", "Resets the 5-hour limit", "Resets the weekly limit" }) Assert.Contains(text, Text(section));
        Assert.Equal(2, Nodes<BankedResetRow>(section).Length); Assert.Equal(new[] { "FIVE_HOUR:1", "WEEK:3" }, model.Providers.PlanLimits[ProviderKind.Zai].PlanLimitsResetCredits!.Credits.Select(x => x.Id));
        var initial = server.Requests.Count; Assert.Equal(2, initial);
        Close(section, h); Open(section, h); Assert.Equal(initial, server.Requests.Count);
        Seed(model, ProviderKind.Zai, new ProviderStatus { ApiKeyConfigured = true, ProviderStatusAuth = new ProviderStatus.Auth.SignedOut() }); Sync(); Assert.Empty(Nodes<PlanLimitsView>(section));
        Seed(model, ProviderKind.Zai, new ProviderStatus { ApiKeyConfigured = true, ProviderStatusAuth = new ProviderStatus.Auth.Unknown() }); Sync(); Assert.Single(Nodes<PlanLimitsView>(section)); Assert.Equal(initial, server.Requests.Count);
        // initial:true when reattached is throttled; then a later sign-in after the 60-second gate re-reads once.
        var host = Assert.IsType<Border>(view.Parent); host.Child = new ProvidersSettingsPage(); Sync(); Layout(host); section = Section(host, ProviderKind.Zai); Assert.Equal(initial, server.Requests.Count);
        Frames(h, 60000); Seed(model, ProviderKind.Zai, model.Providers.Status(ProviderKind.Zai) with { ProviderStatusAuth = new ProviderStatus.Auth.SignedOut() }); Sync();
        Seed(model, ProviderKind.Zai, model.Providers.Status(ProviderKind.Zai) with { ProviderStatusAuth = new ProviderStatus.Auth.SignedIn(null) }); Sync(); Wait(() => server.Requests.Count == initial + 2 && !model.Providers.LoadingLimits.Contains(ProviderKind.Zai));
        var resetRow = Nodes<BankedResetRow>(section)[0]; Click(Nodes<Button>(resetRow).Single(x => Equals(x.Content, "Use reset"))); Sync(); Assert.DoesNotContain(server.Requests, x => x.Method == "POST");
        var quotaBefore = server.Requests.Count(x => x.Target.Contains("/quota/", StringComparison.Ordinal));
        var clock = new SettingsProviderProofClock(h.Main.Time); using var recordedClock = SwiftClock.Provider.Override(clock);
        Click(Nodes<Button>(section).Single(x => Equals(x.Content, "Reset now"))); Sync();
        Wait(() => server.Requests.Any(x => x.Method == "POST") && clock.Durations.Any()); Assert.Contains(TimeSpan.FromSeconds(2), clock.Durations);
        using var body = JsonDocument.Parse(server.PostBody!); Assert.Equal("PERSONAL", body.RootElement.GetProperty("targetType").GetString()); Assert.Equal("FIVE_HOUR", body.RootElement.GetProperty("resetType").GetString()); Assert.Equal(1, body.RootElement.GetProperty("recordId").GetInt32()); Assert.True(Guid.TryParse(body.RootElement.GetProperty("requestId").GetString(), out _));
        var post = server.Requests.Single(x => x.Method == "POST"); Assert.Equal("/api/biz/customer-package-reset/use", post.Target); Assert.Equal(new[] { "Bearer fixture-key-never-sent" }, post.HeaderValues("Authorization"));
        // :178-180: actual production sleeper must retain the old quota for 1999 ms, then read at 2000 ms.
        Frames(h, 1999); Assert.Equal(quotaBefore, server.Requests.Count(x => x.Target.Contains("/quota/", StringComparison.Ordinal))); Assert.NotNull(model.Providers.RedeemingReset);
        Frames(h, 1); Wait(() => server.Requests.Count(x => x.Target.Contains("/quota/", StringComparison.Ordinal)) == quotaBefore + 1 && model.Providers.RedeemingReset is null);
        mode = 3; var savedCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
            model.Providers.RefreshPlanLimits(ProviderKind.Zai, force: true); Wait(() => !model.Providers.LoadingLimits.Contains(ProviderKind.Zai));
            Assert.Contains("250,000 of 1,000,000 credits", Text(section)); Assert.Contains("10,000 of 40,000 calls", Text(section));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = savedCulture; }
        mode = 1; model.Providers.RefreshPlanLimits(ProviderKind.Zai, force: true); Wait(() => !model.Providers.LoadingLimits.Contains(ProviderKind.Zai)); Assert.Empty(Nodes<BankedResetRow>(section)); Assert.Null(model.Providers.PlanLimits[ProviderKind.Zai].PlanLimitsResetCredits);
        // Nil quota has no meters; shared UsageViews.swift:66-68 retains its informative empty-state line.
        typeof(ProviderRegistry).GetProperty("PlanLimits")!.SetValue(model.Providers, ValueMap<ProviderKind, PlanLimits>.Empty); mode = 2; model.Providers.RefreshPlanLimits(ProviderKind.Zai, force: true); Wait(() => !model.Providers.LoadingLimits.Contains(ProviderKind.Zai)); Assert.Empty(Nodes<LimitRow>(section)); Assert.Empty(Nodes<BankedResetRow>(section));
    });

    [Fact]
    [Trait("Req", "UI-PROVAPI-030")] // App/Views/Settings/SettingsView.swift:676-688; Services/Providers/DeepSeekAPI.swift:21-47; ProviderRegistry.swift:396-408
    [Trait("Req", "UI-PROVREG-010")] // App/Views/Settings/SettingsView.swift:772-779; ProviderRegistry.swift:682-700
    public void Deepseek_balance_is_formatted_refreshed_after_validation_cleared_on_rejection_and_throttled() => WithView(() => new ProvidersSettingsPage(), (view, model, h) =>
    {
        var reject = false; var unreachable = false;
        using var server = new LoopbackFeedServer(request => reject ? CannedResponse.Plain(401, "Rejected") : unreachable ? null : CannedResponse.Json(request.Target == "/models" ? """{"data":[]}""" : Balance));
        using var endpoints = Endpoints(typeof(DeepSeekAPI), server);
        model.Settings.SetAPIKeyInput("fixture-key-never-sent", ProviderKind.Deepseek);
        Seed(model, ProviderKind.Deepseek, new ProviderStatus { ApiKeyConfigured = true, ProviderStatusAuth = new ProviderStatus.Auth.SignedIn(null) }); Sync(); Wait(() => model.Providers.Credits.ContainsKey(ProviderKind.Deepseek) && !model.Providers.LoadingCredits.Contains(ProviderKind.Deepseek));
        var section = Section(view, ProviderKind.Deepseek); Collapsed(section, false); UsageMetrics(section); Assert.Contains("Remaining credits", Text(section)); Assert.Contains("$12.34", Text(section)); Assert.Contains("Topped up $10.00 · Granted $2.34", Text(section)); // ProviderCredits.swift:23-49
        var count = server.Requests.Count; Assert.Equal(1, count); Close(section, h); Open(section, h); Assert.Equal(count, server.Requests.Count);
        Seed(model, ProviderKind.Deepseek, model.Providers.Status(ProviderKind.Deepseek) with { ProviderStatusAuth = new ProviderStatus.Auth.SignedOut() }); Sync(); Assert.Empty(Nodes<CreditsView>(section));
        Seed(model, ProviderKind.Deepseek, model.Providers.Status(ProviderKind.Deepseek) with { ProviderStatusAuth = new ProviderStatus.Auth.Unknown() }); Sync(); Assert.Equal(count, server.Requests.Count);
        // Real validation entry point triggers an immediate forced balance read even within the minute gate.
        var valid = model.Providers.Refresh(ProviderKind.Deepseek); Wait(() => valid.IsCompleted && !model.Providers.LoadingCredits.Contains(ProviderKind.Deepseek)); valid.GetAwaiter().GetResult(); Assert.Equal(2, server.Requests.Count(x => x.Target == "/user/balance")); Assert.Contains("$12.34", Text(section));
        reject = true; var invalid = model.Providers.Refresh(ProviderKind.Deepseek); Wait(() => invalid.IsCompleted); invalid.GetAwaiter().GetResult(); Assert.False(model.Providers.Credits.ContainsKey(ProviderKind.Deepseek)); Assert.Empty(Nodes<CreditsView>(section)); Assert.DoesNotContain("$12.34", Text(section));
        reject = false; unreachable = true; Seed(model, ProviderKind.Deepseek, model.Providers.Status(ProviderKind.Deepseek) with { ProviderStatusAuth = new ProviderStatus.Auth.Unknown() }); Sync(); model.Providers.RefreshCredits(ProviderKind.Deepseek, force: true); Wait(() => !model.Providers.LoadingCredits.Contains(ProviderKind.Deepseek)); Assert.DoesNotContain("$12.34", Text(section));
        unreachable = false; model.Providers.RefreshCredits(ProviderKind.Deepseek, force: true); Wait(() => !model.Providers.LoadingCredits.Contains(ProviderKind.Deepseek)); Assert.Contains("$12.34", Text(section));
        model.Settings.SetAPIKeyInput("", ProviderKind.Deepseek); model.Providers.RefreshCredits(ProviderKind.Deepseek, force: true); Sync(); Assert.False(model.Providers.Credits.ContainsKey(ProviderKind.Deepseek)); Assert.DoesNotContain("$12.34", Text(section));
        Close(section, h);
    });

    [Fact]
[Trait("Req", "UI-PROVAPI-001")] // App/Views/Settings/SettingsView.swift:594-610,648,763-779
[Trait("Req", "UI-PROVAPI-002")] // App/Views/Settings/SettingsView.swift:594-610,648,763-779
[Trait("Req", "UI-PROVAPI-003")] // App/Views/Settings/SettingsView.swift:594-610,648,763-779
[Trait("Req", "UI-PROVAPI-004")] // App/Views/Settings/SettingsView.swift:594-610,648,763-779
[Trait("Req", "UI-PROVAPI-006")] // App/Views/Settings/SettingsView.swift:594-610,648,763-779
[Trait("Req", "UI-PROVAPI-007")] // App/Views/Settings/SettingsView.swift:594-610,648,763-779
[Trait("Req", "UI-PROVAPI-008")] // App/Views/Settings/SettingsView.swift:594-610,648,763-779
[Trait("Req", "UI-PROVAPI-009")] // App/Views/Settings/SettingsView.swift:594-610,648,763-779
[Trait("Req", "UI-PROVAPI-010")] // App/Views/Settings/SettingsView.swift:594-610,648,763-779
[Trait("Req", "UI-PROVAPI-029")] // App/Views/Settings/SettingsView.swift:594-610,648,763-779
[Trait("Req", "UI-PROVAPI-030")] // App/Views/Settings/SettingsView.swift:594-610,648,763-779
[Trait("Req", "UI-PROVREG-003")] // App/Views/Settings/SettingsView.swift:594-610,648,763-779
[Trait("Req", "UI-PROVREG-004")] // App/Views/Settings/SettingsView.swift:594-610,648,763-779
[Trait("Req", "UI-PROVREG-005")] // App/Views/Settings/SettingsView.swift:594-610,648,763-779
[Trait("Req", "UI-PROVREG-006")] // App/Views/Settings/SettingsView.swift:594-610,648,763-779
[Trait("Req", "UI-PROVREG-007")] // App/Views/Settings/SettingsView.swift:594-610,648,763-779
[Trait("Req", "UI-PROVREG-008")] // App/Views/Settings/SettingsView.swift:594-610,648,763-779
[Trait("Req", "UI-PROVREG-010")] // App/Views/Settings/SettingsView.swift:594-610,648,763-779
[Trait("Req", "UI-PROVREG-037")] // App/Views/Settings/SettingsView.swift:594-610,648,763-779
[Trait("Req", "UI-PROVREG-039")] // App/Views/Settings/SettingsView.swift:594-610,648,763-779
[Trait("Req", "UI-PROVREG-040")] // App/Views/Settings/SettingsView.swift:594-610,648,763-779
[Trait("Req", "UI-PROVREG-042")] // App/Views/Settings/SettingsView.swift:594-610,648,763-779
[Trait("Req", "UI-PROVREG-043")] // App/Views/Settings/SettingsView.swift:594-610,648,763-779
[Trait("Req", "UI-PROVREG-044")] // App/Views/Settings/SettingsView.swift:594-610,648,763-779
    public void Default_expansion_and_every_collapsed_gate_follow_auth_until_the_real_header_is_clicked() => WithView(() => new ProvidersSettingsPage(), (view, model, h) =>
    {
        var host = Assert.IsType<Border>(view.Parent);
        foreach (var kind in SwiftEnum<ProviderKind>.AllCases) model.Settings.SetEnabled(false, kind); // :594-610, cards include disabled providers.
        foreach (var kind in SwiftEnum<ProviderKind>.AllCases) Seed(model, kind, new ProviderStatus { ApiKeyConfigured = true, Executable = SwiftURL.Create(new Uri(Path.Combine(h.Scratch, "not-launched.exe")).AbsoluteUri)!, ProviderStatusAuth = new ProviderStatus.Auth.SignedIn(null) });
        host.Child = new ProvidersSettingsPage(); Sync(); Layout(host);
        foreach (var kind in SwiftEnum<ProviderKind>.AllCases)
        {
            var section = Section(host, kind); Collapsed(section, false); Assert.Equal(1, Nodes<ChromeRevealLayout>(section).Single().Fraction);
            Close(section, h);
            var reveal = Nodes<ChromeRevealLayout>(section).Single(); var card = Assert.Single(Nodes<ChromeCard>(reveal));
            Assert.Equal(0, card.Bounds.X); Assert.Equal(0, card.Bounds.Y); Assert.True(card.Bounds.Height > 0);
            // :763-766 actual collapsed hit testing must not reach any of the mounted card.
            var window = Assert.IsType<Avalonia.Controls.Window>(TopLevel.GetTopLevel(section));
            var point = card.TranslatePoint(new Point(20, 10), window)!.Value;
            var hit = window.InputHitTest(point) as Control;
            Assert.False(hit is not null && (ReferenceEquals(hit, reveal) || hit.GetVisualAncestors().Contains(reveal)));
            Seed(model, kind, model.Providers.Status(kind) with { ProviderStatusAuth = new ProviderStatus.Auth.SignedOut() }); Sync();
            Seed(model, kind, model.Providers.Status(kind) with { ProviderStatusAuth = new ProviderStatus.Auth.SignedIn(null) }); Sync(); Collapsed(section, true);
        }
        Assert.DoesNotContain("providerExpansion", h.Defaults.Writes);
        host.Child = new ProvidersSettingsPage(); Sync(); Layout(host);
        Assert.All(Nodes<ProviderSettingsSection>(host), section => Collapsed(section, false));
    });

    [Fact]
[Trait("Req", "UI-PROVAPI-002")] // App/Views/Settings/SettingsView.swift:594-610,648,763-779
[Trait("Req", "UI-PROVAPI-003")] // App/Views/Settings/SettingsView.swift:594-610,648,763-779
[Trait("Req", "UI-PROVAPI-004")] // App/Views/Settings/SettingsView.swift:594-610,648,763-779
[Trait("Req", "UI-PROVAPI-007")] // App/Views/Settings/SettingsView.swift:594-610,648,763-779
[Trait("Req", "UI-PROVAPI-008")] // App/Views/Settings/SettingsView.swift:594-610,648,763-779
[Trait("Req", "UI-PROVAPI-009")] // App/Views/Settings/SettingsView.swift:594-610,648,763-779
[Trait("Req", "UI-PROVREG-037")] // App/Views/Settings/SettingsView.swift:594-610,648,763-779
[Trait("Req", "UI-PROVREG-040")] // App/Views/Settings/SettingsView.swift:594-610,648,763-779
[Trait("Req", "UI-PROVREG-042")] // App/Views/Settings/SettingsView.swift:594-610,648,763-779
[Trait("Req", "UI-PROVREG-043")] // App/Views/Settings/SettingsView.swift:594-610,648,763-779
    public void Row_fonts_remain_Inter_and_style_sized_while_the_real_app_zoom_scales_the_rendered_window() => WithView(() => new ProvidersSettingsPage(), (view, _, h) =>
    {
        // Chrome.swift:1055-1079; WindowManager.swift:175; AppZoom.swift:6,22-25; ED-059 and OD-0009.
        foreach (var section in Nodes<ProviderSettingsSection>(view)) Open(section, h);
        var host = Assert.IsType<Border>(view.Parent); var window = Assert.IsType<Avalonia.Controls.Window>(TopLevel.GetTopLevel(view));
        var layers = Assert.IsType<Avalonia.Controls.Primitives.VisualLayerManager>(window.Content); layers.Child = null; layers.Child = new AppZoomModifier(100).Body(host); Layout(view);
        foreach (var percent in new[] { 75, 90, 100, 110, 125, 150, 175, 200 })
        {
            new AppZoomModifier(percent).Body(host); Layout(view);
            var transform = Assert.IsType<ScaleTransform>(host.RenderTransform); Assert.Equal(percent / 100d, transform.ScaleX); Assert.Equal(percent / 100d, transform.ScaleY);
            Assert.Equal(new RelativePoint(0, 0, RelativeUnit.Relative), host.RenderTransformOrigin);
            var from = host.TranslatePoint(default, window)!.Value; var to = host.TranslatePoint(new Point(100, 100), window)!.Value;
            Assert.Equal((double)percent, to.X - from.X, 6); Assert.Equal((double)percent, to.Y - from.Y, 6);
            foreach (var row in Nodes<ChromeRow>(view))
            {
                var labels = Assert.IsType<StackPanel>(Assert.IsType<Grid>(Assert.IsType<Border>(row.Child).Child).Children[0]);
                var text = labels.Children.OfType<TextBlock>().ToArray(); Assert.Equal(Chrome.SettingsRowTitleSize, text[0].FontSize); Inter(text[0]);
                if (text.Length == 2) { Assert.Equal(Chrome.SettingsRowDetailSize, text[1].FontSize); Inter(text[1]); Assert.Equal(TextWrapping.WrapWithOverflow, text[1].TextWrapping); }
            }
        }
    });

    [Fact]
    [Trait("Req", "UI-PROVREG-010")] // App/Views/Settings/SettingsView.swift:629-634,676-688,772-779; ProviderRegistry.swift:606-641,682-700
    public void Command_Code_initial_appearance_and_later_sign_in_read_exactly_one_limits_and_one_credits_result() => WithView(() => new ProvidersSettingsPage(), (view, model, h) =>
    {
        const string credits = """{"windowLimits":{"fiveHour":{"cap":100,"used":25}},"credits":{"monthlyCredits":4,"purchasedCredits":3,"freeCredits":2}}""";
        using var server = new LoopbackFeedServer(request => CannedResponse.Json(request.Target.Contains("subscriptions", StringComparison.Ordinal) ? """{"data":{"planId":"individual-pro"}}""" : credits));
        var endpoint = typeof(CommandCodeAPI).GetProperty("FetchBaseURL", BindingFlags.Static | BindingFlags.NonPublic); Assert.NotNull(endpoint);
        using var routes = Assert.IsType<Ambient<Uri>>(endpoint.GetValue(null)).Override(new Uri(server.Url("/")));
        // Existing ED-211 resolver seam: plan-limit reader does not execute Command Code; no real CLI can resolve here.
        using var executable = ProviderRegistry.SetCLIUpdaterExecutableForTesting(kind => kind == ProviderKind.Commandcode ? SwiftURL.Create(new Uri(Path.Combine(h.Scratch, "never-launched-commandcode.exe")).AbsoluteUri)! : null);
        model.Settings.SetAPIKeyInput("fixture-command-key", ProviderKind.Commandcode);
        Seed(model, ProviderKind.Commandcode, new ProviderStatus { ApiKeyConfigured = true, ProviderStatusAuth = new ProviderStatus.Auth.SignedIn(null) }); Sync();
        Wait(() => model.Providers.PlanLimits.ContainsKey(ProviderKind.Commandcode) && model.Providers.Credits.ContainsKey(ProviderKind.Commandcode) && !model.Providers.LoadingLimits.Contains(ProviderKind.Commandcode) && !model.Providers.LoadingCredits.Contains(ProviderKind.Commandcode));
        Assert.Equal(2, server.Requests.Count(x => x.Target == "/alpha/billing/credits")); Assert.Single(server.Requests, x => x.Target == "/alpha/billing/subscriptions");
        var host = Assert.IsType<Border>(view.Parent); host.Child = null; Sync();
        Frames(h, 60_000); Assert.Equal(3, server.Requests.Count);
        host.Child = new ProvidersSettingsPage(); Sync(); Layout(host);
        Assert.True(model.Providers.LoadingLimits.Contains(ProviderKind.Commandcode) || server.Requests.Count >= 5, "A shown initial usage block starts its limits read.");
        Assert.True(model.Providers.LoadingCredits.Contains(ProviderKind.Commandcode) || server.Requests.Count >= 6, "A shown initial usage block starts its credits read.");
        Wait(() => server.Requests.Count == 6 && !model.Providers.LoadingLimits.Contains(ProviderKind.Commandcode) && !model.Providers.LoadingCredits.Contains(ProviderKind.Commandcode));
        var fetched = h.Main.Time.GetUtcNow(); Assert.Equal(4, server.Requests.Count(x => x.Target == "/alpha/billing/credits")); Assert.Equal(2, server.Requests.Count(x => x.Target == "/alpha/billing/subscriptions"));
        var section = Section(host, ProviderKind.Commandcode); Collapsed(section, false); UsageMetrics(section);
        var usage = Nodes<StackPanel>(section).Single(x => x.Spacing == 14); Assert.IsType<PlanLimitsView>(usage.Children[0]); Assert.IsType<CreditsView>(usage.Children[1]); Assert.Equal(6, server.Requests.Count);
        Close(section, h); Open(section, h); Assert.Equal(6, server.Requests.Count);
        Seed(model, ProviderKind.Commandcode, model.Providers.Status(ProviderKind.Commandcode) with { ProviderStatusAuth = new ProviderStatus.Auth.SignedOut() }); Sync();
        Seed(model, ProviderKind.Commandcode, model.Providers.Status(ProviderKind.Commandcode) with { ProviderStatusAuth = new ProviderStatus.Auth.SignedIn(null) }); Sync(); Assert.Equal(6, server.Requests.Count);
        Frames(h, 59_999 - (h.Main.Time.GetUtcNow() - fetched).TotalMilliseconds);
        Seed(model, ProviderKind.Commandcode, model.Providers.Status(ProviderKind.Commandcode) with { ProviderStatusAuth = new ProviderStatus.Auth.SignedOut() }); Sync();
        Seed(model, ProviderKind.Commandcode, model.Providers.Status(ProviderKind.Commandcode) with { ProviderStatusAuth = new ProviderStatus.Auth.SignedIn(null) }); Sync();
        Wait(() => !model.Providers.LoadingLimits.Contains(ProviderKind.Commandcode) && !model.Providers.LoadingCredits.Contains(ProviderKind.Commandcode)); Assert.Equal(6, server.Requests.Count);
        Frames(h, 1);
        Seed(model, ProviderKind.Commandcode, model.Providers.Status(ProviderKind.Commandcode) with { ProviderStatusAuth = new ProviderStatus.Auth.SignedOut() }); Sync();
        Seed(model, ProviderKind.Commandcode, model.Providers.Status(ProviderKind.Commandcode) with { ProviderStatusAuth = new ProviderStatus.Auth.SignedIn(null) }); Sync();
        Assert.True(model.Providers.LoadingLimits.Contains(ProviderKind.Commandcode) || server.Requests.Count >= 8); Assert.True(model.Providers.LoadingCredits.Contains(ProviderKind.Commandcode) || server.Requests.Count >= 9);
        Wait(() => server.Requests.Count == 9 && !model.Providers.LoadingLimits.Contains(ProviderKind.Commandcode) && !model.Providers.LoadingCredits.Contains(ProviderKind.Commandcode));
        Assert.Equal(6, server.Requests.Count(x => x.Target == "/alpha/billing/credits")); Assert.Equal(3, server.Requests.Count(x => x.Target == "/alpha/billing/subscriptions"));
        Assert.All(server.Requests, x => Assert.Equal(new[] { "Bearer fixture-command-key" }, x.HeaderValues("Authorization")));
    });

    [Fact]
    [Trait("Req", "UI-PROVREG-005")] // App/Views/Settings/SettingsView.swift:655-656; ProviderRegistry.swift:19-26
    public void Checking_shows_its_own_literal_and_never_the_summary_it_would_otherwise_disagree_with() => WithView(() => new ProvidersSettingsPage(), (view, model, h) =>
    {
        // status.summary for this exact status would read "Signed in as prover" (ProviderRegistry.swift:22):
        // installed (Executable set) and signedIn with an account. If the call site's ternary (SettingsView.swift:655-656)
        // ever stopped short-circuiting on isChecking and fell through to status.summary, the row would show that
        // text instead. Asserting the literal proves the branch was actually taken, not merely that both exist somewhere.
        var checking = new ProviderStatus { IsChecking = true, Executable = SwiftURL.Create(new Uri(Path.Combine(h.Scratch, "not-launched.exe")).AbsoluteUri)!, ProviderStatusAuth = new ProviderStatus.Auth.SignedIn("prover") };
        Assert.Equal("Signed in as prover", checking.Summary); // sanity: the getter really does disagree with "Checking…" for this status
        Seed(model, ProviderKind.Codex, checking); Sync();
        Assert.Equal("Checking…", Text(Labels(Section(view, ProviderKind.Codex)))[0]);
        Assert.DoesNotContain("Signed in as prover", Text(Section(view, ProviderKind.Codex)));
        Seed(model, ProviderKind.Codex, checking with { IsChecking = false }); Sync();
        Assert.Equal("Signed in as prover", Text(Labels(Section(view, ProviderKind.Codex)))[0]);
    });

    [Fact]
    [Trait("Rule", "R-TEST-2")] // Partial UI-PROVREG-002, STOP #2: SettingsView.swift:789-794 contradicts the ONLY-control clause.
    public void Key_edit_really_probes_after_six_hundred_ms_without_clicking_any_refresh_control() => WithView(() => new ProvidersSettingsPage(), (view, model, h) =>
    {
        using var server = new LoopbackFeedServer(request => CannedResponse.Json(request.Target == "/models" ? """{"data":[]}""" : Balance));
        using var endpoint = Endpoints(typeof(DeepSeekAPI), server);
        var section = Section(view, ProviderKind.Deepseek); Open(section, h);
        var field = Assert.Single(Nodes<TextBox>(section)); PointerClick(field); Assert.True(field.Focus());
        Assert.IsType<Avalonia.Controls.Window>(TopLevel.GetTopLevel(field)).KeyTextInput("fixture-edit-key"); Sync();
        Frames(h, 599); Assert.Empty(server.Requests); Frames(h, 1);
        Wait(() => server.Requests.Any(x => x.Target == "/models") && !model.Providers.Status(ProviderKind.Deepseek).IsChecking);
        Assert.True(model.Providers.Status(ProviderKind.Deepseek).ApiKeyConfigured); Assert.Empty(Nodes<ProvidersRefreshButton>(view));
    });

}
