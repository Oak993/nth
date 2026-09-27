using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Theme;

namespace Marrow.App.Tests.Views.Settings;

// Source: DroppyCode/App/Views/Settings/SettingsView.swift (B3 52bc792), :138-215,246-289,648-675,818-834.
[Collection("Headless")]
public sealed class SettingsViewCoreRound2Tests(HeadlessSession headless)
{
    private static T Field<T>(object o, string name) => (T)o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o)!;
    private static T[] Nodes<T>(Control root) where T : class => LogicalQuery.SelfAndDescendants<T>(root).ToArray();
    private static string[] Text(Control root) => Nodes<TextBlock>(root).Select(x => x is MiddleTruncatingText m ? m.FullText : x.Text ?? "").ToArray();
    private static void Sync() => Dispatcher.UIThread.RunJobs();
    private static void Layout(Control view)
    {
        var window = (Avalonia.Controls.Window)TopLevel.GetTopLevel(view)!;
        window.UpdateLayout(); Sync(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Sync();
    }
    private static void Frame(AppModelHarness h, Control view, double milliseconds)
    {
        h.Main.AdvanceMilliseconds(milliseconds); ((ManualFrameSource)FrameClock.Source.Current).Advance(TimeSpan.FromMilliseconds(milliseconds)); Sync(); Layout(view);
    }
    private static Point Centre(Control control, Avalonia.Controls.Window window) => control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
    private static void Press(Control control)
    {
        Layout(control); var window = (Avalonia.Controls.Window)TopLevel.GetTopLevel(control)!; var point = Centre(control, window);
        var hit = Assert.IsAssignableFrom<Visual>(window.InputHitTest(point)); Assert.True(ReferenceEquals(hit, control) || hit.GetVisualAncestors().Contains(control));
        window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Sync(); Layout(control);
    }
    private void WithView(Func<Control> make, Action<Control, AppModel, AppModelHarness, Avalonia.Controls.Window> check)
    {
        headless.OnUiThread(() =>
        {
            using var h = new AppModelHarness();
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            using var wallpaper = WallpaperStore.Shared.Override((WallpaperStore)Activator.CreateInstance(typeof(WallpaperStore), nonPublic: true)!);
            using var updates = UpdateChecker.OverrideShared(new UpdateChecker(ReleaseFeed.Running("1.0.0"), [], ""));
            var model = new AppModel(); var host = new Border(); AppEnvironment.SetAppModel(host, model); var view = make(); host.Child = view;
            var window = new Avalonia.Controls.Window { Content = host, Width = 780, Height = 580 };
            try { window.Show(); Layout(view); check(view, model, h, window); }
            finally { window.Close(); Sync(); SettingsNavigation.Shared.RequestedPage = null; }
        });
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-002")] // App/Views/Settings/SettingsView.swift:138-140,192; ED-063/200 background drag region.
    public void Reserved_strip_real_hit_and_press_reach_the_caption_region() => WithView(() => new SettingsView(), (view, _, _, window) =>
    {
        var sidebar = Field<Control>(view, "sidebar"); var strip = Nodes<Border>(sidebar).Single(b => b.Height == 14 && b.Margin.Top == 16);
        var drag = Nodes<WindowDragArea>(sidebar).Single(); var column = strip.Parent as Grid; Assert.NotNull(column); Assert.Null(column.Background);
        var point = Centre(strip, window); Assert.Same(drag, window.InputHitTest(point));
        Assert.Equal(Win32Properties.Win32HitTestValue.Caption, Win32Properties.GetNonClientHitTestResult(drag));
        var presses = new List<(object? source, bool handled)>(); var bubbles = 0;
        drag.AddHandler(InputElement.PointerPressedEvent, (_, e) => presses.Add((e.Source, e.Handled)), RoutingStrategies.Bubble, handledEventsToo: true);
        sidebar.AddHandler(InputElement.PointerPressedEvent, (_, _) => bubbles++, RoutingStrategies.Bubble);
        window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Sync();
        Assert.Same(drag, Assert.Single(presses).source); Assert.True(presses[0].handled); Assert.Equal(0, bubbles);
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-007")] // App/Views/Settings/SettingsView.swift:198-204: archive count gate and live dependency.
    public void Empty_archive_placeholder_tracks_actual_archive_changes_and_non_archive_routes() => WithView(() => new SettingsView(), (view, model, h, _) =>
    {
        foreach (var page in new[] { SettingsPage.General, SettingsPage.Models, SettingsPage.Mcp, SettingsPage.About })
        { SettingsNavigation.Shared.RequestedPage = page; Sync(); Assert.DoesNotContain(Nodes<SwiftImage>(view), i => AutomationProperties.GetName(i) == "No archived threads"); }
        SettingsNavigation.Shared.RequestedPage = SettingsPage.Archive; Sync(); Assert.Single(Nodes<SwiftImage>(view), i => AutomationProperties.GetName(i) == "No archived threads");
        var project = model.AddProject(SwiftURL.FileURLWithPath("/" + h.NewFolder("archive-round2").Replace('\\', '/')));
        var thread = model.NewThread(project); model.Archive(thread!.Id); Sync(); Layout(view); Assert.Single(model.ArchivedThreads);
        Assert.DoesNotContain(Nodes<SwiftImage>(view), i => AutomationProperties.GetName(i) == "No archived threads"); Assert.Single(Nodes<ArchiveSettingsPage>(view));
        Assert.Single(Nodes<ArchiveDeleteAllButton>(view)); Assert.Single(Nodes<ChromeCompactTitle>(view));
        SettingsNavigation.Shared.RequestedPage = SettingsPage.General; Sync(); SettingsNavigation.Shared.RequestedPage = SettingsPage.Archive; Sync();
        Assert.Single(Nodes<ArchiveSettingsPage>(view)); Assert.DoesNotContain(Nodes<SwiftImage>(view), i => AutomationProperties.GetName(i) == "No archived threads");
        model.DeleteArchivedThreads(); Sync(); Assert.Single(Nodes<SwiftImage>(view), i => AutomationProperties.GetName(i) == "No archived threads"); Assert.Empty(Nodes<ArchiveSettingsPage>(view));
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-008")] // App/Views/Settings/SettingsView.swift:246-267; UI/Chrome/Chrome.swift:571-576,635-646.
    public void Compact_title_quantises_off_grid_clamps_travel_and_aligns_leading() => WithView(() => new SettingsView(), (view, _, _, _) =>
    {
        SettingsNavigation.Shared.RequestedPage = SettingsPage.Models; Sync(); Layout(view);
        var title = Nodes<ChromeCompactTitle>(view).Single(); var text = Nodes<TextBlock>(title).Single(); Assert.Equal(HorizontalAlignment.Left, text.HorizontalAlignment);
        var model = Field<ChromeScrollModel>(view, "scrollChrome");
        // :572-574 start10/end44, rounded to nearest .04. Off-grid points distinguish floor/ceil/continuous.
        foreach (var (travel, progress) in new[] { (-20.0, 0.0), (10.51, 0.0), (10.85, 0.04), (26.3, 0.48), (27.0, 0.52), (43.49, 1.0), (80.0, 1.0) })
        { model.Update(travel); Sync(); Assert.Equal(progress, model.Progress, 10); Assert.Equal(progress, text.Opacity, 10); Assert.Equal((1 - progress) * 5, ((TranslateTransform)text.RenderTransform!).Y, 10); Assert.Equal(progress < 0.5 ? AccessibilityView.Raw : AccessibilityView.Content, AutomationProperties.GetAccessibilityView(title)); }
        Assert.False(title.IsHitTestVisible);
        SettingsNavigation.Shared.RequestedPage = SettingsPage.Appearance; Sync(); Assert.Equal("Appearance", Text(Nodes<ChromeCompactTitle>(view).Single()).Single());
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-006")] // App/Views/Settings/SettingsView.swift:166-177 VStack(alignment:.leading); shown only if no visible page.
    public void No_results_order_alignment_and_visibility_follow_real_search_input() => WithView(() => new SettingsView(), (view, _, _, window) =>
    {
        var field = Nodes<TextBox>(Nodes<SidebarSearchField>(view).Single()).Single(); Press(field); Assert.True(field.Focus()); window.KeyTextInput("unmatched-round2"); Sync(); Layout(view);
        var labels = Nodes<TextBlock>(Field<StackPanel>(view, "rows")); Assert.Equal(new[] { "No results", "Try a setting or a provider name." }, labels.Select(l => l.Text));
        Assert.All(labels, l => Assert.Equal(TextAlignment.Left, l.TextAlignment)); Assert.True(labels[0].Bounds.Top < labels[1].Bounds.Top);
        var column = Assert.IsType<StackPanel>(labels[0].Parent); Assert.Equal(Orientation.Vertical, column.Orientation); Assert.Equal(FontWeight.Normal, labels[1].FontWeight);
        field.Text = "models"; Sync(); Assert.DoesNotContain("No results", Text(Field<StackPanel>(view, "rows"))); Assert.Single(Nodes<SidebarRow>(Field<StackPanel>(view, "rows")));
        field.Text = ""; Sync(); Assert.DoesNotContain("No results", Text(Field<StackPanel>(view, "rows")));
    });

    [Fact]
    [Trait("Req", "UI-GIT-058")] // App/Views/Settings/SettingsView.swift:818-834 ZStack(alignment:.topLeading), immediate TextEditor binding; AppSettings.swift:305-306.
    public void Commit_placeholder_rendered_position_hit_testing_and_real_typing_write_immediately() => WithView(() => new SourceControlSettingsPage(), (view, model, h, window) =>
    {
        var section = Nodes<ChromeSection>(view).Single(s => Text(s).Contains("Commit message instructions")); Assert.Single(Nodes<ChromeCard>(section));
        var editor = Nodes<TextBox>(section).Single(); var hint = Nodes<TextBlock>(section).Single(t => t.Text == "For example: use conventional commit prefixes");
        Assert.Equal(HorizontalAlignment.Left, hint.HorizontalAlignment); Assert.Equal(VerticalAlignment.Top, hint.VerticalAlignment); Assert.Equal(TextAlignment.Left, hint.TextAlignment);
        var overlay = Assert.IsType<Panel>(hint.Parent); Assert.Equal(new Control[] { editor, hint }, overlay.Children);
        Assert.Equal(5, hint.Bounds.X); Assert.Equal(0, hint.Bounds.Y); Assert.True(hint.Bounds.Height < editor.Bounds.Height); // caption is at the top, not stretch-centred.
        var hintPoint = hint.TranslatePoint(new Point(10, hint.Bounds.Height / 2), window)!.Value;
        var hit = Assert.IsAssignableFrom<Visual>(window.InputHitTest(hintPoint)); Assert.True(ReferenceEquals(hit, editor) || hit.GetVisualAncestors().Contains(editor));
        Press(editor); Assert.True(editor.Focus()); window.KeyTextInput("first"); Sync(); Assert.Equal("first", model.Settings.CommitInstructions); Assert.Equal("first", h.Defaults.String("commitInstructions")); Assert.False(hint.IsVisible);
        window.KeyTextInput(" second"); Sync(); Assert.Equal("first second", model.Settings.CommitInstructions); Assert.Equal("first second", h.Defaults.String("commitInstructions"));
        editor.Text = ""; Sync(); Assert.True(hint.IsVisible); Assert.Equal("", h.Defaults.String("commitInstructions"));
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-049")] // App/Views/Settings/SettingsView.swift:648-675: mark/text/spacer/switch, clipped collapsed card, actual switch input.
    public void Provider_status_rendered_columns_collapse_and_real_pointer_toggle() => WithView(() => new ScrollViewer { Content = new ProvidersSettingsPage() }, (view, model, h, _) =>
    {
        var section = Nodes<ProviderSettingsSection>(view).First(); var provider = Field<ProviderKind>(section, "provider");
        var header = Nodes<CollapsibleProviderHeader>(section).Single(); Assert.Contains(provider.DisplayName, Text(header)); Press(Nodes<Button>(header).Single()); Frame(h, view, Chrome.PanelSlide.Duration * 1000 + 1);
        var grid = Nodes<Grid>(section).Single(g => g.ColumnDefinitions.Count == 3 && g.ColumnDefinitions[0].Width.Value == 22); var image = Nodes<ProviderIcon>(grid).Single(); var labels = Assert.IsType<StackPanel>(grid.Children[1]); var control = Assert.IsType<SettingsSwitch>(grid.Children[2]); var toggle = Nodes<SmallSwitch>(control).Single();
        Assert.Same(image, grid.Children[0]); Assert.Equal(0, Grid.GetColumn(image)); Assert.Equal(1, Grid.GetColumn(labels)); Assert.Equal(2, Grid.GetColumn(control));
        Assert.Equal(new GridLength(22), grid.ColumnDefinitions[0].Width); Assert.Equal(GridUnitType.Star, grid.ColumnDefinitions[1].Width.GridUnitType); Assert.True(grid.ColumnDefinitions[2].Width.IsAuto);
        Assert.True(image.Bounds.Right <= labels.Bounds.Left); Assert.True(labels.Bounds.Right <= control.Bounds.Left); Assert.Equal(22, image.Bounds.Width);
        var mark = Nodes<SwiftImage>(image).Single(); Assert.Equal(18, mark.Bounds.Width); Assert.Equal(18, mark.Bounds.Height);
        Assert.Equal(grid.Bounds.Width, control.Bounds.Right, 6); Assert.True(toggle.IsEffectivelyEnabled);
        var enabled = model.Settings.IsEnabled(provider); Press(toggle); Assert.Equal(!enabled, model.Settings.IsEnabled(provider)); Press(toggle); Assert.Equal(enabled, model.Settings.IsEnabled(provider));
        var reveal = Nodes<ChromeRevealLayout>(section).Single(); Press(Nodes<Button>(header).Single()); Frame(h, view, Chrome.PanelSlide.Duration * 1000 + 1);
        Assert.Equal(0, reveal.Bounds.Height); Assert.True(reveal.ClipToBounds); Assert.False(toggle.IsEffectivelyEnabled); Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(reveal));
    });
}
