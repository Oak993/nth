using Marrow.App.Port.Settings;
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using SkiaSharp;
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

// Source: DroppyCode/App/Views/Settings/SettingsView.swift (B3 52bc792), :138-289,296-368,522-580,648-675,818-846,1130-1210.
// All tests mount the production controls; expected literals below quote the pinned Swift, not a second implementation.
[Collection("Headless")]
public sealed class SettingsViewCoreProofTests(HeadlessSession headless)
{
    private static T Field<T>(object o, string name) => (T)o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o)!;
    private static T[] Nodes<T>(Control root) where T : class => LogicalQuery.SelfAndDescendants<T>(root).ToArray();
    private static string[] Text(Control root) => Nodes<TextBlock>(root).Select(x => x is MiddleTruncatingText m ? m.FullText : x.Text ?? "").ToArray();
    private static Button Button(Control root, string name) => Nodes<Button>(root).First(b => AutomationProperties.GetName(b) == name || Text(b).Contains(name));
    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
    private static void Sync() => Dispatcher.UIThread.RunJobs();
    private static void Drain(Control root) { var w = (Avalonia.Controls.Window)TopLevel.GetTopLevel(root)!; for (var i = 0; i < 5; i++) { w.UpdateLayout(); Sync(); } }
    private static void Frame(AppModelHarness h, double ms) { h.Main.AdvanceMilliseconds(ms); ((ManualFrameSource)FrameClock.Source.Current).Advance(TimeSpan.FromMilliseconds(ms)); Sync(); }
    private static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
    private static void Foreground(TextBlock text, SwiftColor color) => Assert.Equal(color.Resolve(text.ActualThemeVariant), ColorOf(text.Foreground));
    private static void Glyph(SwiftImage image, SwiftFont font, SwiftColor color) { Assert.Equal(font, SwiftImage.GetFont(image)); Assert.Equal(color.Resolve(image.ActualThemeVariant), ColorOf(image.Foreground)); }
    private static ChromeRow Row(Control root, string name) => Nodes<ChromeRow>(root).Single(x => Text(x).First() == name);
    private static void Seed(AppModel model, ProviderKind kind, ProviderStatus status) => typeof(ProviderRegistry).GetProperty("Statuses")!.SetValue(model.Providers, model.Providers.Statuses.Mutating(map => map[kind] = status));
    private static void Hover(Control c, bool on) => c.RaiseEvent(new PointerEventArgs(on ? InputElement.PointerEnteredEvent : InputElement.PointerExitedEvent, c, new Avalonia.Input.Pointer(903, PointerType.Mouse, true), c, default, 0, default, default));
    // A real pointer over a control, as the window's own input pipeline delivers it: Entered goes to the control and to every
    // ancestor it is now inside (a SidebarRow's hover is on its overlay host, around the button and its accessory, Chrome.swift:731-737),
    // which a PointerEntered raised on the control alone (Hover above) does not reach.
    private static void PointerOver(Control control)
    {
        var window = (Avalonia.Controls.Window)TopLevel.GetTopLevel(control)!; Drain(control);
        window.MouseMove(control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value); Sync();
    }
    private static KeyEventArgs Key(Control c, Avalonia.Input.Key key) { var e = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Source = c, Key = key }; c.RaiseEvent(e); Sync(); return e; }
    private static Control Opened(Popup p) { ((EventHandler?)typeof(Popup).GetField("Opened", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(p))?.Invoke(p, EventArgs.Empty); return PopoverHost.Content(p)!; } // the site's view, not the card the presenter hangs it on (OD-0095)
    private static void Press(Control control)
    {
        var window = (Avalonia.Controls.Window)TopLevel.GetTopLevel(control)!; Drain(control);
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Sync();
    }

    private void WithView(Func<Control> make, Action<Control, AppModel, AppModelHarness> check, Action<AppModel>? seed = null)
    {
        headless.OnUiThread(() =>
        {
            using var h = new AppModelHarness();
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            using var wallpaper = WallpaperStore.Shared.Override((WallpaperStore)Activator.CreateInstance(typeof(WallpaperStore), nonPublic: true)!);
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            using var updates = UpdateChecker.OverrideShared(new UpdateChecker(ReleaseFeed.Running("1.0.0"), [], ""));
            var model = new AppModel(); seed?.Invoke(model);
            var host = new Border(); AppEnvironment.SetAppModel(host, model); var view = make(); host.Child = view;
            var window = new Avalonia.Controls.Window { Content = host, Width = 780, Height = 580 };
            try { window.Show(); Drain(view); check(view, model, h); }
            finally { window.Close(); Sync(); SettingsNavigation.Shared.RequestedPage = null; }
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-027")] // App/Views/Settings/SettingsView.swift:96,211-215,251-253,280
    public void Mcp_header_query_reaches_the_real_page_filters_and_resets_on_navigation() => WithView(() => new SettingsView(), (view, _, _) =>
    {
        SettingsNavigation.Shared.RequestedPage = SettingsPage.Mcp; Sync();
        var page = Assert.Single(Nodes<MCPSettingsPage>(view));
        var search = Assert.Single(Nodes<ChromeSearchField>(view)); var box = Assert.Single(Nodes<TextBox>(search));
        Assert.Equal("Search servers", box.PlaceholderText); Assert.Equal("", box.Text ?? "");
        Assert.Equal("", Field<string>(page, "query"));
        box.Text = "github"; Sync(); Assert.Equal("github", Field<string>(page, "query"));
        Assert.Contains("GitHub", Text(page)); Assert.DoesNotContain("GitLab", Text(page));
        box.Text = "no-such-mcp-server"; Sync(); Assert.DoesNotContain("GitHub", Text(page));
        Assert.Same(page, Assert.Single(Nodes<MCPSettingsPage>(view))); // Swift body rebuild, retained keyed page lifetime.
        Assert.True(Key(box, Avalonia.Input.Key.Escape).Handled); Assert.Equal("", Field<string>(page, "query"));
        box.Text = "github"; Sync(); SettingsNavigation.Shared.RequestedPage = SettingsPage.General; Sync();
        Assert.Empty(Nodes<MCPSettingsPage>(view)); Assert.Null(TopLevel.GetTopLevel(page));
        SettingsNavigation.Shared.RequestedPage = SettingsPage.Mcp; Sync();
        Assert.NotSame(page, Assert.Single(Nodes<MCPSettingsPage>(view))); Assert.Equal("", Field<string>(Nodes<MCPSettingsPage>(view).Single(), "query"));
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-028")] // App/Views/Settings/SettingsView.swift:230,274-289
    public void All_ten_routes_mount_one_page_and_detach_the_previous_keyed_lifetime() => WithView(() => new SettingsView(), (view, model, h) =>
    {
        // :198 archive needs a thread to mount its page rather than the empty-state exception.
        var project = model.AddProject(SwiftURL.FileURLWithPath("/" + h.NewFolder("routing").Replace('\\', '/')));
        var thread = model.NewThread(project); model.Archive(thread!.Id); Sync();
        var mapping = new (SettingsPage page, Type type)[] {
            (SettingsPage.General, typeof(GeneralSettingsPage)), (SettingsPage.Hydra, typeof(HydraSettingsPage)),
            (SettingsPage.Appearance, typeof(AppearanceSettingsPage)), (SettingsPage.Providers, typeof(ProvidersSettingsPage)),
            (SettingsPage.Mcp, typeof(MCPSettingsPage)), (SettingsPage.Models, typeof(ModelsSettingsPage)),
            (SettingsPage.SourceControl, typeof(SourceControlSettingsPage)), (SettingsPage.Shortcuts, typeof(ShortcutsSettingsPage)),
            (SettingsPage.Archive, typeof(ArchiveSettingsPage)), (SettingsPage.About, typeof(AboutSettingsPage)) };
        Control? previous = null;
        foreach (var (page, type) in mapping.Concat(mapping.Reverse()))
        {
            SettingsNavigation.Shared.RequestedPage = page; Sync();
            var mounted = Nodes<Control>(view).Where(c => mapping.Any(p => c.GetType() == p.type)).ToArray();
            var current = Assert.Single(mounted); Assert.Equal(type, current.GetType());
            if (previous is not null && !ReferenceEquals(previous, current)) { Assert.Null(TopLevel.GetTopLevel(previous)); Assert.DoesNotContain(previous, Nodes<Control>(view)); }
            var searches = Nodes<ChromeSearchField>(view);
            if (page is SettingsPage.Mcp or SettingsPage.Models)
            {
                var box = Assert.Single(Nodes<TextBox>(Assert.Single(searches))); Assert.Equal("", box.Text ?? "");
                box.Text = "no-such-query"; Sync(); Assert.Equal("no-such-query", Field<string>(current, "query"));
            }
            else { Assert.Empty(searches); Assert.Null(type.GetField("query", BindingFlags.Instance | BindingFlags.NonPublic)); }
            previous = current;
        }
    });

    [Fact]
    [Trait("Req", "UI-GIT-058")] // App/Views/Settings/SettingsView.swift:818-834
    public void Commit_section_asserts_title_card_padding_and_placeholder_font_colour_and_live_binding() => WithView(() => new SourceControlSettingsPage(), (view, model, _) =>
    {
        // :818 "Commit message instructions"; :824 minHeight 90; :830 horizontal 5; :833 padding 12.
        var section = Nodes<ChromeSection>(view).Single(s => Text(s).Contains("Commit message instructions"));
        var editor = Assert.Single(Nodes<TextBox>(section)); var hint = Nodes<TextBlock>(section).Single(t => t.Text == "For example: use conventional commit prefixes");
        Assert.Equal(90, editor.MinHeight); Assert.Equal(13, editor.FontSize); Assert.Equal(13, hint.FontSize); Foreground(hint, Chrome.SecondaryText);
        Assert.Equal(new Thickness(5, 0), hint.Margin); Assert.False(hint.IsHitTestVisible); Assert.True(hint.IsVisible);
        Assert.Single(Nodes<Border>(section), b => b.Padding == new Thickness(12) && Nodes<TextBox>(b).Contains(editor));
        editor.Text = " first\nsecond "; Sync(); Assert.Equal(" first\nsecond ", model.Settings.CommitInstructions); Assert.False(hint.IsVisible);
        editor.Text = ""; Sync(); Assert.True(hint.IsVisible);
    });

    // STOP opencode-proofs-settings-core #2
    // UI-MCP-090 UI-SETTINGS-009 UI-SETTINGS-010 UI-SETTINGS-011 UI-SETTINGS-013 UI-SETTINGS-016 UI-SETTINGS-017:
    // glass sites below retain source-defined descriptors, but F.GlassMaterial's app-content material and
    // interactive press response have not landed (SwiftGlass.cs MARROW_DEFERRED ED-180; engineering_decisions ED-180).
    // ED-180 explicitly calls this incomplete work, so these partial tests intentionally have no Req traits.
    [Fact]
    [Trait("Req", "PR-GIT-150")] // SettingsView.swift:836-846; the reference runtime shows the section (S 199.5), SettingsViewFinishWorktreesTests carries its measured Reveal pill
    [Trait("Req", "UI-GIT-059")] // SettingsView.swift:836-846; Chrome.swift:1006-1087
    public void Worktree_section_has_ordered_associated_rows_divider_no_PR_control_and_real_reveal() => WithView(() => new SourceControlSettingsPage(), (view, _, _) =>
    {
        // SettingsView.swift:836-846: Worktrees row, Divider, Pull requests row with EmptyView.
        var section = Nodes<ChromeSection>(view).Single(s => Text(s).First() == "Worktrees and pull requests");
        var card = Assert.Single(Nodes<ChromeCard>(section)); var rows = Nodes<ChromeRow>(card);
        Assert.Equal(new[] { "Worktrees", "Pull requests" }, rows.Select(r => Text(r).First()));
        Assert.Equal(new[] { "Worktrees", Storage.WorktreesDirectory, "Reveal" }, Text(rows[0]));
        Assert.Equal(new[] { "Pull requests", "Opened with the GitHub CLI (gh) or GitLab CLI (glab) from your PATH." }, Text(rows[1]));
        var stack = Nodes<StackPanel>(card).Single(s => s.Children.Count == 3 && s.Children[0] is ChromeRow);
        Assert.Same(rows[0], stack.Children[0]); Assert.IsType<ChromeRowDivider>(stack.Children[1]); Assert.Same(rows[1], stack.Children[2]);
        Assert.Empty(Nodes<Button>(rows[1]));
        foreach (var row in rows) { var text = Nodes<TextBlock>(row); Assert.Equal(13, text[0].FontSize); Assert.Equal(11, text[1].FontSize); Foreground(text[0], Chrome.PrimaryText); Foreground(text[1], Chrome.SecondaryText); }
        var back = Assert.IsType<Border>(card.Child); Assert.Equal(new CornerRadius(16), back.CornerRadius); Assert.Equal(Chrome.Overlay(0.03).Resolve(back.ActualThemeVariant), ColorOf(back.Background)); // Chrome.swift:1023-1025.
        var revealEffect = SwiftGlass.GetEffect(Assert.Single(Button(rows[0], "Reveal").GetVisualDescendants().OfType<Control>(), c => SwiftGlass.GetEffect(c) is not null))!; // :840 .buttonStyle(.glass): the accent capsule (SettingsViewFinishWorktreesTests carries the measured geometry)
        Assert.Equal(GlassShapeKind.Capsule, revealEffect.Shape.Kind); Assert.Equal(Chrome.Accent, revealEffect.TintColor); Assert.Equal(1, revealEffect.TintOpacity); Assert.True(revealEffect.IsInteractive);
        string? path = null; using var workspace = Marrow.UI.Common.Workspace.Platform.Override(new Marrow.UI.Common.Workspace.Backend { Reveal = p => path = p });
        var reveal = Button(rows[0], "Reveal"); var trailing = ((Grid)((Border)rows[0].Child!).Child!).Children[1]; Assert.Equal(HorizontalAlignment.Right, trailing.HorizontalAlignment); Click(reveal); Sync(); Assert.Equal(Storage.WorktreesDirectory, path);
    });

    private static void CheckGlass(Control root, bool interactive)
    {
        // Chrome.swift:164-182 / ED-180: regular capsule, theme surface tint at .3, optional .interactive().
        var surface = Assert.Single(Nodes<Control>(root), c => SwiftGlass.GetEffect(c) is not null);
        var effect = SwiftGlass.GetEffect(surface)!; Assert.Equal(GlassShapeKind.Capsule, effect.Shape.Kind);
        Assert.Equal(RoundedCornerStyle.Continuous, effect.Shape.Style); Assert.Equal(0.3, effect.TintOpacity);
        Assert.Equal(Chrome.GlassTint, effect.TintColor); Assert.Equal(interactive, effect.IsInteractive);
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-002")] // App/Views/Settings/SettingsView.swift:138-140,192-193
    [Trait("Req", "UI-SETTINGS-006")] // App/Views/Settings/SettingsView.swift:166-177
    [Trait("Req", "UI-SETTINGS-007")] // App/Views/Settings/SettingsView.swift:198-204
    public void Sidebar_strip_empty_results_and_empty_archive_have_exact_geometry_colours_and_placement() => WithView(() => new SettingsView(), (view, _, _) =>
    {
        var sidebar = Field<Control>(view, "sidebar"); Assert.Single(Nodes<WindowDragArea>(sidebar));
        var spacer = Nodes<Border>(sidebar).Single(b => b.Height == 14 && b.Margin.Top == 16); Assert.Null(spacer.Background); Assert.Equal(212, sidebar.Width);
        var drag = Nodes<WindowDragArea>(sidebar).Single(); // :192 background drag area covers the reserved strip.
        var stripCentre = spacer.TranslatePoint(new Point(spacer.Bounds.Width / 2, spacer.Bounds.Height / 2), drag)!.Value;
        Assert.True(new Rect(drag.Bounds.Size).Contains(stripCentre)); Assert.True(drag.IsHitTestVisible);
        var field = Nodes<TextBox>(Nodes<SidebarSearchField>(view).Single()).Single(); Assert.DoesNotContain("No results", Text(sidebar));
        field.Text = "no-such-setting"; Sync(); var rows = Field<StackPanel>(view, "rows");
        var block = Assert.Single(rows.Children.OfType<StackPanel>()); Assert.Equal(6, block.Spacing); Assert.Equal(new Thickness(8, 14, 8, 0), block.Margin);
        Assert.Equal(new[] { "No results", "Try a setting or a provider name." }, Text(block));
        var labels = Nodes<TextBlock>(block); Assert.Equal(13, labels[0].FontSize); Assert.Equal(FontWeight.SemiBold, labels[0].FontWeight); Assert.Equal(11, labels[1].FontSize);
        Foreground(labels[0], Chrome.PrimaryText.Opacity(0.92)); Foreground(labels[1], Chrome.SecondaryText);
        field.Text = ""; Sync(); SettingsNavigation.Shared.RequestedPage = SettingsPage.Archive; Sync(); Drain(view);
        var detail = Field<Border>(view, "detail"); var image = Assert.Single(Nodes<SwiftImage>(detail));
        Assert.Equal("archivebox", image.SymbolName); Glyph(image, SwiftFont.System(44, SwiftFont.Weight.Light), Chrome.SecondaryText.Opacity(0.6));
        Assert.Equal("No archived threads", AutomationProperties.GetName(image)); Assert.Equal(HorizontalAlignment.Center, image.HorizontalAlignment); Assert.Equal(VerticalAlignment.Center, image.VerticalAlignment);
        Assert.Empty(Nodes<ScrollViewer>(detail)); Assert.Empty(Nodes<ChromeCompactTitle>(detail)); Assert.Empty(Nodes<ArchiveDeleteAllButton>(detail));
        Assert.Equal(detail.Bounds.Width / 2, image.TranslatePoint(new Point(22, 22), detail)!.Value.X, 1); Assert.Equal(detail.Bounds.Height / 2, image.TranslatePoint(new Point(22, 22), detail)!.Value.Y, 1);
        SettingsNavigation.Shared.RequestedPage = SettingsPage.General; Sync(); Assert.DoesNotContain(image, Nodes<SwiftImage>(view));
    });

    [Fact]
    [Trait("Rule", "R-TEST-2")] // Partial proof only; see precise STOP.
    // UI-SETTINGS-003 source properties/interactions; the Req test is SettingsViewFinishSidebarTests.Sidebar_search_is_placed_and_drawn_per_the_source_...
    public void Sidebar_search_pins_shared_metrics_fonts_colours_focus_and_keyboard_actions() => WithView(() => new SettingsView(), (view, _, _) =>
    {
        var search = Assert.Single(Nodes<SidebarSearchField>(view)); var field = Assert.Single(Nodes<TextBox>(search));
        var capsule = Assert.IsType<Border>(search.Child); Assert.Equal(30, capsule.Height); Assert.Equal(new Thickness(9, 0), capsule.Padding);
        Assert.Equal(new Thickness(10, 14, 10, 0), search.Margin); Assert.Equal(13, field.FontSize); Assert.Equal("Search", field.PlaceholderText);
        Assert.Equal(HorizontalAlignment.Stretch, field.HorizontalAlignment); Assert.False(field.IsSet(Control.FocusAdornerProperty)); // Swift :841 only disables this on ChromeSearchField.
        Assert.Equal(Chrome.Overlay(0.07).Resolve(capsule.ActualThemeVariant), ColorOf(capsule.Background));
        var magnifier = Nodes<SwiftImage>(search).Single(); Glyph(magnifier, SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Medium), Chrome.SecondaryText.Opacity(0.85)); Assert.Equal(new Thickness(0, 0, 6, 0), magnifier.Margin); Assert.Empty(Nodes<Button>(search));
        field.Text = "api key"; Sync(); Assert.Equal(new[] { "Providers" }, Nodes<SidebarRow>(view).Select(r => Text(r).Single()));
        var clear = Button(search, "Clear search"); Glyph(Nodes<SwiftImage>(clear).Single(), SwiftFont.System(SwiftFont.TextStyle.Callout), Chrome.SecondaryText.Opacity(0.75)); Assert.Equal(new Thickness(6, 0, 0, 0), clear.Margin);
        Assert.True(Key(field, Avalonia.Input.Key.Enter).Handled); Assert.Single(Nodes<ProvidersSettingsPage>(view));
        // Chrome.swift:819-821: the shell move handler declines; TextBox may consume the caret key later.
        foreach (var offset in new[] { -1, 1 }) Assert.False((bool)typeof(SidebarSearchField).GetMethod("Move", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(search, [offset])!);
        Key(field, Avalonia.Input.Key.Up); Key(field, Avalonia.Input.Key.Down); Assert.Single(Nodes<ProvidersSettingsPage>(view));
        Assert.True(Key(field, Avalonia.Input.Key.Escape).Handled); Assert.Equal("", field.Text); Assert.Empty(Nodes<Button>(search));
        field.Text = "models"; Sync(); Click(Button(search, "Clear search")); Sync(); Assert.Equal("", field.Text);
    });

    [Fact]
    [Trait("Rule", "R-TEST-2")] // Partial proof only; see precise STOP.
    // UI-SETTINGS-004 source properties/interactions only; continuous curve awaits STOP #6 below.
    public void Sidebar_rows_pin_selected_hover_and_frozen_geometry_fonts_colours_and_click() => WithView(() => new SettingsView(), (view, _, h) =>
    {
        var rows = Field<StackPanel>(view, "rows"); Assert.Equal(1, rows.Spacing); Assert.Equal(Marrow.App.Port.Settings.SettingsSidebar.Pages("").Count, Nodes<SidebarRow>(rows).Length); // CZ-4: Customization is listed
        foreach (var row in Nodes<SidebarRow>(rows))
        {
            var button = Nodes<Button>(row).Single(); var shape = Assert.IsType<Border>(button.Content); var label = Nodes<TextBlock>(row).Single(); bool selected = label.Text == "General";
            Assert.Equal(28, shape.Height); Assert.Equal(new CornerRadius(7), shape.CornerRadius); Assert.Equal(new Thickness(8, 0), shape.Padding);
            Assert.Equal(8, Nodes<StackPanel>(shape).Single().Spacing); Assert.Equal(20, Nodes<SidebarIconBadge>(row).Single().Width);
            Assert.Equal(13, label.FontSize); Assert.Equal(selected ? FontWeight.Medium : FontWeight.Normal, label.FontWeight); Foreground(label, Chrome.PrimaryText.Opacity(selected ? 1 : 0.92));
            Assert.Equal((selected ? Chrome.Overlay(0.12) : SwiftColor.Clear).Resolve(shape.ActualThemeVariant), ColorOf(shape.Background));
            Assert.Equal(selected, ((ISelectionItemProvider)ControlAutomationPeer.CreatePeerForElement(button)!).IsSelected);
            var badge = Nodes<SidebarIconBadge>(row).Single(); var glyph = ((Panel)badge.Child!).Children.Single(); Assert.Equal(Chrome.PrimaryText.Opacity(0.85).Resolve(glyph.ActualThemeVariant), ColorOf(Avalonia.Controls.Documents.TextElement.GetForeground(glyph)));
        }
        var models = Nodes<SidebarRow>(view).Single(r => Text(r).Contains("Models")); var modelsButton = Nodes<Button>(models).Single(); var background = (Border)modelsButton.Content!;
        PointerOver(modelsButton); Frame(h, 50); Assert.NotEqual(SwiftColor.Clear.Resolve(background.ActualThemeVariant), ColorOf(background.Background)); Frame(h, 51); Assert.Equal(Chrome.Overlay(0.06).Resolve(background.ActualThemeVariant), ColorOf(background.Background));
        Click(modelsButton); Sync(); Assert.Single(Nodes<ModelsSettingsPage>(view));
        Frame(h, 110); // Chrome.swift:729 the wash leaves General on Chrome.hover (easeOut 0.1 s): read the rows once its 0.1 s are over
        var general = Nodes<SidebarRow>(view).Single(r => Text(r).Contains("General")); var generalButton = Nodes<Button>(general).Single();
        Field<SettingsScrollTracker>(view, "scrollTracker").Note(); Sync(); Assert.False(rows.IsHitTestVisible);
        Press(generalButton); Assert.Single(Nodes<ModelsSettingsPage>(view)); // :182 real pointer hit testing blocks the click.
        Frame(h, 101); // Chrome.swift:723-724 the old row's wash fades out over Chrome.hover
        Assert.Equal(SwiftColor.Clear.Resolve(general.ActualThemeVariant), ColorOf(((Border)generalButton.Content!).Background));
        Field<SettingsScrollTracker>(view, "scrollTracker").Coast(); Sync(); Assert.True(rows.IsHitTestVisible);
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-008")] // App/Views/Settings/SettingsView.swift:246-267; UI/Chrome/Chrome.swift:571-576,630-647
    public void Compact_title_pins_title_chrome_insets_quantised_opacity_rise_and_accessibility() => WithView(() => new SettingsView(), (view, _, _) =>
    {
        SettingsNavigation.Shared.RequestedPage = SettingsPage.Models; Sync(); Drain(view);
        var title = Nodes<ChromeCompactTitle>(view).Single(); var text = Nodes<TextBlock>(title).Single(); Assert.Equal("Models", text.Text); Assert.Equal(15, text.FontSize); Assert.Equal(FontWeight.SemiBold, text.FontWeight); Foreground(text, Chrome.PrimaryText);
        var chrome = Nodes<StackPanel>(view).Single(s => s.MinHeight == 32); Assert.Equal(new Thickness(14, 12, 156, 0), chrome.Margin); /* trailing: OD-0044, clear of the Windows caption buttons */ Assert.Equal(10, chrome.Spacing);
        var row = Nodes<Grid>(chrome).Single(g => g.MinHeight == 32); Assert.Equal(10, row.ColumnSpacing); Assert.Equal(chrome.Bounds.Width - Nodes<ChromeSearchField>(view).Single().Bounds.Width - 10, title.Bounds.Width, 1);
        Assert.False(title.IsHitTestVisible); var model = Field<ChromeScrollModel>(view, "scrollChrome");
        // Chrome.swift:34-36 start 10,end44, quantum .04: 0...1 in 26 values.
        var values = new HashSet<double>();
        for (var i = 0; i <= 25; i++) { model.Update(10 + 34 * i / 25.0); Sync(); values.Add(Math.Round(text.Opacity, 8)); Assert.Equal(i * 0.04, text.Opacity, 8); Assert.Equal((1 - i * 0.04) * 5, ((TranslateTransform)text.RenderTransform!).Y, 8); Assert.Equal(i < 13 ? AccessibilityView.Raw : AccessibilityView.Content, AutomationProperties.GetAccessibilityView(title)); }
        Assert.Equal(26, values.Count); model.Update(10); Sync(); Assert.Equal(0, text.Opacity); Assert.Equal(5, ((TranslateTransform)text.RenderTransform!).Y);
    });

    [Theory]
    [Trait("Rule", "R-TEST-2")] // Partial proof only; see precise STOP.
    [InlineData(SettingsPage.Models, "Search models")]
    [InlineData(SettingsPage.Mcp, "Search servers")]
    public void Pane_search_capsules_pin_shared_style_and_filter_real_content(SettingsPage route, string prompt) => WithView(() => new SettingsView(), (view, _, h) =>
    {
        // SettingsView.swift:248-253; Chrome.swift:828,834-867. UI-MCP-090/UI-SETTINGS-009/010 subset (the Req tests are in SettingsViewFinishPaneSearchTests).
        SettingsNavigation.Shared.RequestedPage = route; Sync(); Drain(view); var search = Nodes<ChromeSearchField>(view).Single();
        var page = route == SettingsPage.Mcp ? (Control)Nodes<MCPSettingsPage>(view).Single() : Nodes<ModelsSettingsPage>(view).Single();
        Assert.DoesNotContain(search, Nodes<Control>(page)); var chrome = Nodes<StackPanel>(view).Single(s => s.MinHeight == 32); Assert.Contains(search, Nodes<ChromeSearchField>(chrome));
        Assert.Equal(200, search.Bounds.Width); Assert.Equal(32, search.Bounds.Height);
        var framed = Nodes<Border>(search).Single(b => b.Width == 200); Assert.Equal(28, framed.Height); Assert.Equal(new Thickness(10, 0), framed.Padding); Assert.Single(Nodes<Border>(search), b => b.Padding == new Thickness(0, 2));
        var box = Nodes<TextBox>(search).Single(); Assert.Equal(prompt, box.PlaceholderText); Assert.Equal(12.5, box.FontSize); Assert.True(box.IsSet(Control.FocusAdornerProperty)); Assert.Null(box.FocusAdorner); Assert.Equal(Chrome.PrimaryText.Resolve(box.ActualThemeVariant), ColorOf(box.Foreground));
        var mag = Nodes<SwiftImage>(search).Single(i => i.SymbolName == "magnifyingglass"); Glyph(mag, SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Semibold), Chrome.SecondaryText);
        var clear = Button(search, "Clear search"); Glyph(Nodes<SwiftImage>(clear).Single(), SwiftFont.System(SwiftFont.TextStyle.Callout), Chrome.SecondaryText.Opacity(0.8)); Assert.Equal(0, clear.Opacity); Assert.False(clear.IsEnabled); Assert.True(clear.IsVisible); Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(clear)); CheckGlass(search, false);
        box.Text = "no-such-model-or-server"; Sync(); Assert.Equal("no-such-model-or-server", Field<string>(page, "query")); Frame(h, 101); Assert.Equal(1, clear.Opacity); Assert.True(clear.IsEnabled); // OD-0076: the button fades over Chrome.hover (ChromeSearchField)
        Assert.True(Key(box, Avalonia.Input.Key.Escape).Handled); Assert.Equal("", Field<string>(page, "query")); Frame(h, 101); Assert.Equal(0, clear.Opacity);
    });

    [Fact]
    [Trait("Rule", "R-TEST-2")] // Partial proof only; see precise STOP.
    // UI-SETTINGS-014/015 source properties/interactions only; continuous curve awaits STOP #6 below.
    public void Workspace_tiles_pin_ring_symbols_captions_states_and_actual_selection() => WithView(() => new GeneralSettingsPage(), (view, model, h) =>
    {
        CheckTiles(Row(view, "Workspace"), ["Local", "New worktree"], ["folder", "arrow.triangle.branch"], h);
        Assert.Equal(WorkspaceMode.Worktree, model.Settings.DefaultWorkspaceMode);
    });

    [Fact]
    [Trait("Rule", "R-TEST-2")]
    // UI-SETTINGS-015 source properties/interactions only; continuous curve awaits STOP #6 below.
    public void Working_line_tiles_pin_ring_symbols_captions_states_and_actual_selection() => WithView(() => new GeneralSettingsPage(), (view, model, h) =>
    {
        Assert.True(model.Settings.ShowsWorkingCard); // AppSettings.swift:721 defaults.object(...) ?? true.
        CheckTiles(Row(view, "Working line"), ["Card", "Line"], ["rectangle.fill", "minus"], h);
        Assert.False(model.Settings.ShowsWorkingCard);
    });

    private static void CheckTiles(Control row, string[] names, string[] symbols, AppModelHarness h)
    {
        var tiles = Nodes<ChromeVisualTile>(row); Assert.Equal(names, tiles.Select(AutomationProperties.GetName)); Assert.True(tiles[0].IsSelected); Assert.False(tiles[1].IsSelected);
        Assert.Single(Nodes<StackPanel>(row), s => s.Orientation == Orientation.Horizontal && s.Spacing == 12); Assert.Single(Nodes<NoWindowDragArea>(row));
        for (var i = 0; i < tiles.Length; i++)
        {
            var tile = tiles[i]; var stage = Field<Border>(tile, "stageBack"); var ring = Field<SwiftShapeBorder>(tile, "paddedStage"); var caption = Field<TextBlock>(tile, "captionBlock");
            Assert.Equal(76, stage.Width); Assert.Equal(48, stage.Height); Assert.Equal(new CornerRadius(6), stage.CornerRadius); Assert.Equal(new Thickness(1), ring.Padding); Assert.Equal(new CornerRadius(9), ring.CornerRadius);
            // ChromeVisualPicker.swift:96-101: .padding(ringInset=3) then an overlay .strokeBorder(lineWidth: ringWidth=2) drawn
            // inward from that padded edge (Avalonia's Border draws its border inside its own bounds the same way), so the gap
            // between the stroke and the stage is ringInset - ringWidth = 1, and the stroke's own width is fixed at ringWidth = 2
            // regardless of selection (UI.Chrome.ChromeVisualPicker review findings, impl/b2); only the colour (BorderBrush) toggles.
            Assert.Equal(6, Nodes<StackPanel>(tile).Single().Spacing); Assert.Equal(76, caption.Width); Assert.Equal(2, caption.MaxLines); Assert.Equal(11, caption.FontSize);
            var image = Nodes<SwiftImage>(tile).Single(); Assert.Equal(symbols[i], image.SymbolName); Glyph(image, SwiftFont.System(SwiftFont.TextStyle.Title2, weight: SwiftFont.Weight.Medium), i == 0 ? Chrome.Accent : Chrome.SecondaryText);
            Foreground(caption, i == 0 ? Chrome.PrimaryText : Chrome.SecondaryText); Assert.Equal((i == 0 ? Chrome.Overlay(0.10) : Chrome.Overlay(0.06)).Resolve(stage.ActualThemeVariant), ColorOf(stage.Background));
            Assert.Equal(new Thickness(2), ring.BorderThickness); Assert.Equal((i == 0 ? Chrome.Accent : Chrome.Accent.Opacity(0)).Resolve(ring.ActualThemeVariant), ColorOf(ring.BorderBrush)); // the unselected tile's stroke is the accent at no opacity, so it can fade
            // ChromeVisualPicker.swift:92-102 (ED-181 closed): the stage is `.continuous` radius 6 and the ring a continuous radius-9 strokeBorder, whose outer edge is the 82 x 54 shape.
            Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(stage)); Assert.Equal(RoundedCornerStyle.Continuous, ring.CornerStyle); Assert.NotNull(stage.Clip);
            Assert.All(SwiftShapes.Outline(new Rect(0, 0, 82, 54), ring.CornerRadius, ring.CornerStyle).Corners, k => Assert.Equal(CornerKind.Continuous, k));
        }
        Hover(Nodes<Button>(tiles[1]).Single(), true); Sync(); Frame(h, 101); Assert.Equal(Chrome.Overlay(0.10).Resolve(tiles[1].ActualThemeVariant), ColorOf(Field<Border>(tiles[1], "stageBack").Background));
        Click(Nodes<Button>(tiles[1]).Single()); Sync(); Assert.True(tiles[1].IsSelected); Assert.False(tiles[0].IsSelected); Frame(h, 101); // the colours ride Chrome.hover (UI-SETTINGS-014/015)
        Glyph(Nodes<SwiftImage>(tiles[1]).Single(), SwiftFont.System(SwiftFont.TextStyle.Title2, weight: SwiftFont.Weight.Medium), Chrome.Accent);
        Assert.Equal(new Thickness(2), Field<SwiftShapeBorder>(tiles[1], "paddedStage").BorderThickness); Foreground(Field<TextBlock>(tiles[1], "captionBlock"), Chrome.PrimaryText);
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-005")] // App/Views/Settings/SettingsView.swift:159-163; AboutUpdateViews.swift:527-535; OD-0008 C6, OD-0012 I1
    public void About_dot_has_top_trailing_anchor_accent_accessibility_and_empty_feed_makes_no_HTTP_request() => WithView(() => new SettingsView(), (view, _, _) =>
    {
        using var requests = new SettingsCoreHttpRequests();
        // Validate the observer against the real transport using a closed ephemeral loopback port.
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start(); var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        using var client = new System.Net.Http.HttpClient();
        try { HeadlessSession.RunDispatcherUntil(client.GetAsync($"http://127.0.0.1:{port}/"), AppModelHarness.Bound); }
        catch (System.Net.Http.HttpRequestException) { }
        Assert.NotEmpty(requests.Requests); requests.Requests.Clear();
        Assert.Equal("https://github.com/Oak993/marrow-releases", UpdateChecker.DefaultFeed); Assert.Empty(Nodes<UpdateAvailableDot>(view)); // UE-1, OD-0100: the feed is the owner's repository; a run that is not a Velopack install has no feed to read, and says so as OD-0012 I1 does
        var check = UpdateChecker.Shared.Check(); HeadlessSession.RunDispatcherUntil(check, AppModelHarness.Bound); Sync();
        Assert.Empty(requests.Requests); Assert.Equal("No update feed is set.", UpdateChecker.Shared.LastError); Assert.Null(UpdateChecker.Shared.Update); Assert.Empty(Nodes<UpdateAvailableDot>(view)); // OD-0012 I1 empty-feed failure branch.
        typeof(UpdateChecker).GetProperty("Update")!.SetValue(UpdateChecker.Shared, new AvailableUpdate { Version = "999.0.0", Tag = "v999.0.0", DownloadURL = SwiftURL.Create("https://fixture.invalid/download")!, PageURL = SwiftURL.Create("https://fixture.invalid/release")!, Notes = "" }); Sync();
        var dot = Assert.Single(Nodes<UpdateAvailableDot>(view)); Assert.Equal(HorizontalAlignment.Right, dot.HorizontalAlignment); Assert.Equal(VerticalAlignment.Top, dot.VerticalAlignment);
        var about = Nodes<SidebarRow>(view).Single(r => Text(r).Contains("About")); Assert.Single(Nodes<SidebarIconBadge>(about)); Assert.Same(dot, Nodes<UpdateAvailableDot>(about).Single());
        var badge = Nodes<SidebarIconBadge>(about).Single(); Assert.Equal(20, ((Panel)badge.Child!).Width); Assert.Equal(20, ((Panel)badge.Child!).Height);
        var circle = Assert.IsType<Ellipse>(dot.Child); Assert.Equal(7, circle.Width); Assert.Equal(7, circle.Height); Assert.Equal(Chrome.Accent.Resolve(circle.ActualThemeVariant), ColorOf(circle.Fill)); Assert.Equal("Update available", AutomationProperties.GetName(circle));
        Assert.Equal(2.5, ((TranslateTransform)circle.RenderTransform!).X); Assert.Equal(-2.5, ((TranslateTransform)circle.RenderTransform!).Y);
        Assert.Empty(Nodes<Button>(dot)); typeof(UpdateChecker).GetProperty("Update")!.SetValue(UpdateChecker.Shared, null); Sync(); Assert.Empty(Nodes<UpdateAvailableDot>(view));
    });

    [Fact]
    [Trait("Rule", "R-TEST-2")] // Partial proof only; see precise STOP.
    // UI-SETTINGS-012 pointer/Space/accent; the measured look and motion are SmallSwitchMotionTests.
    public void Small_switch_renders_accent_without_label_and_real_pointer_and_focused_space_toggle() => WithView(() => new GeneralSettingsPage(), (view, model, h) =>
    {
        var row = Row(view, "Show thinking"); var switchView = Nodes<SettingsSwitch>(row).Single(); var toggle = Nodes<SmallSwitch>(row).Single();
        Assert.Empty(Text(switchView)); Assert.Equal(toggle.Bounds.Width, switchView.Bounds.Width); Assert.True(toggle.Focusable); // :301 labelsHidden, no label width.
        model.Settings.ShowReasoning = false; Sync(); Assert.Equal(ToggleState.Off, ((IToggleProvider)ControlAutomationPeer.CreatePeerForElement(toggle)!).ToggleState);
        Press(toggle); Assert.True(model.Settings.ShowReasoning); Assert.True(toggle.Focus()); Assert.True(Key(toggle, Avalonia.Input.Key.Space).Handled); Assert.False(model.Settings.ShowReasoning);
        Key(toggle, Avalonia.Input.Key.Space); Assert.True(model.Settings.ShowReasoning);
        // The switch's look is the Mac's, measured (ED-280); the source tint is exact once the track has crossfaded (0.25 s), read from rendered pixels.
        Frame(h, 300); using var bitmap = new RenderTargetBitmap(new PixelSize((int)toggle.Bounds.Width, (int)toggle.Bounds.Height)); using (var dc = bitmap.CreateDrawingContext()) toggle.Render(dc);
        using var stream = new MemoryStream(); bitmap.Save(stream, new PngBitmapEncoderOptions()); using var pixels = SKBitmap.Decode(stream.ToArray());
        var accent = Chrome.Accent.Resolve(toggle.ActualThemeVariant); Assert.Contains(pixels.Pixels, p => p.Red == accent.R && p.Green == accent.G && p.Blue == accent.B && p.Alpha == accent.A);
        var disabled = Row(view, "Sound when settling"); model.Settings.ThreadFinishAction = ThreadFinishAction.Archive; Sync(); Assert.False(disabled.IsEnabled); Assert.Equal(0.5, disabled.Opacity);
        var child = Nodes<SmallSwitch>(disabled).Single(); var before = model.Settings.SettleSound; Key(child, Avalonia.Input.Key.Space); Assert.Equal(before, model.Settings.SettleSound);
    });

    [Fact]
    [Trait("Rule", "R-TEST-2")] // Partial proof only; see precise STOP.
    public void Provider_refresh_pins_capsule_metrics_symbol_dim_and_starts_the_real_single_flight() => WithView(() => new ProvidersRefreshButton(), (view, model, _) =>
    {
        // SettingsView.swift:566-580; Chrome.swift:315-316,343,355. UI-SETTINGS-011 subset (the Req test is in SettingsViewFinishProvidersTests).
        Assert.Single(Nodes<ChromeCapsule>(view)); Assert.Single(Nodes<ChromeIconButton>(view)); CheckGlass(view, true);
        Assert.Single(Nodes<Border>(view), b => b.Padding.Top == 2 && b.Padding.Bottom == 2);
        var icon = Nodes<SwiftImage>(view).Single(); Assert.Equal("arrow.clockwise", icon.SymbolName); Glyph(icon, SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Semibold), Chrome.PrimaryText.Opacity(0.92));
        Assert.Single(Nodes<Panel>(view), p => p.Width == 28 && p.Height == 28); Assert.Empty(Text(view));
        var button = Nodes<Button>(view).Single(); Assert.True(button.IsEnabled); Assert.Equal("Check providers again", ToolTip.GetTip(button));
        using var executable = ProviderRegistry.SetCLIUpdaterExecutableForTesting(_ => null); // ED-211: final missing-executable result, never real PATH.
        var finished = new TaskCompletionSource();
        void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e) { if (e.PropertyName == "IsRefreshing" && !model.Providers.IsRefreshing) finished.TrySetResult(); }
        model.Providers.PropertyChanged += Changed;
        try
        {
            Click(button); Sync(); Assert.True(model.Providers.IsRefreshing); var checking = Nodes<Button>(view).Single(); Assert.False(checking.IsEnabled); Assert.Equal("Checking providers…", ToolTip.GetTip(checking));
            Glyph(Nodes<SwiftImage>(view).Single(), SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Semibold), Chrome.PrimaryText.Opacity(0.32));
        }
        finally { HeadlessSession.RunDispatcherUntil(finished.Task, AppModelHarness.Bound); model.Providers.PropertyChanged -= Changed; }
    });

    [Fact]
    [Trait("Rule", "R-TEST-2")] // Partial proof only; see precise STOP.
    public void Permissions_capsule_pins_metrics_fonts_hover_open_symbols_checkmark_and_selection() => WithView(() => new GeneralSettingsPage(), (view, model, h) =>
    {
        // SettingsView.swift:323-325; Chrome.swift:896-928; Provider.swift:161-195. UI-SETTINGS-013 subset (the Req test is in SettingsViewFinishGeneralTests).
        var picker = Nodes<GlassPickerButton<RuntimeMode>>(Row(view, "Permissions")).Single(); CheckGlass(picker, true);
        var label = Nodes<TextBlock>(picker).Single(); Assert.Equal(12.5, label.FontSize); Assert.Equal(FontWeight.Medium, label.FontWeight);
        var stack = Nodes<StackPanel>(picker).Single(); Assert.Equal(6, stack.Spacing);
        Assert.Single(Nodes<Border>(picker), b => b.Height == 28 && b.Padding == new Thickness(12, 0));
        var arrow = Nodes<SwiftImage>(picker).Single(i => i.SymbolName == "chevron.down"); Glyph(arrow, SwiftFont.System(8, SwiftFont.Weight.Bold), Chrome.SecondaryText);
        var mode = Nodes<SwiftImage>(picker).Single(i => i.SymbolName != "chevron.down"); Glyph(mode, SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Medium), Chrome.SecondaryText);
        Assert.Equal(Chrome.PrimaryText.Opacity(0.92).Resolve(stack.ActualThemeVariant), ColorOf(Avalonia.Controls.Documents.TextElement.GetForeground(stack)));
        Hover((Panel)Nodes<Panel>(picker).Single(p => p.Children.Count == 2 && p.Children.Any(c => c is Popup)), true); Sync(); Frame(h, 101); Assert.Equal(Chrome.PrimaryText.Resolve(stack.ActualThemeVariant), ColorOf(Avalonia.Controls.Documents.TextElement.GetForeground(stack)));
        var presenter = new RecordingPresenter(); using var seam = Presentation.Presenter.Override(presenter); Click(Nodes<Button>(picker).Single()); Sync(); var popup = presenter.Calls.Last(c => c.Presented).Popup; Assert.Equal(PlacementMode.Bottom, popup.Placement);
        var menu = Opened(popup); var items = Nodes<PopoverItem>(menu); Assert.Equal(new[] { "Supervised", "Auto-accept edits", "Auto", "Full access" }, items.Select(i => Text(i).First()));
        Assert.Equal(new[] { "hand.raised", "pencil.line", "wand.and.sparkles", "lock.open" }, items.Select(i => Nodes<SwiftImage>(i).Single(image => image.SymbolName != "checkmark").SymbolName)); // Provider.swift:190-194.
        Assert.Equal(1, Nodes<SwiftImage>(items[0]).Single(i => i.SymbolName == "checkmark").Opacity); Assert.All(items.Skip(1), i => Assert.Equal(0, Nodes<SwiftImage>(i).Single(image => image.SymbolName == "checkmark").Opacity)); // Popovers.swift:279 reserves the column.
        Click(Button(menu, "Full access")); Sync(); Assert.Equal(RuntimeMode.FullAccess, model.Settings.DefaultRuntimeMode); Assert.Contains("Runs commands and edits without asking", Text(Row(view, "Permissions")));
    }, model => model.Settings.DefaultRuntimeMode = RuntimeMode.Supervised);

    [Fact]
    [Trait("Rule", "R-TEST-2")] // Partial proof only; see precise STOP.
    public void Marks_button_and_popover_pin_small_glass_style_anchor_width_names_and_paths() => WithView(() => new ProjectMarksRow(), (view, model, h) =>
    {
        // SettingsView.swift:1130-1154 .buttonStyle(.glass).controlSize(.small), popover arrowEdge .bottom,width400.
        // UI-SETTINGS-016 subset; STOP #2 (glass material) and #3 (native small-control metrics).
        var edit = Button(view, "Edit"); Assert.False(edit.IsEnabled); Assert.Equal("Choose a mark for each project", ToolTip.GetTip(edit));
        model.AddProject(SwiftURL.FileURLWithPath("/" + h.NewFolder("One").Replace('\\', '/'))); model.AddProject(SwiftURL.FileURLWithPath("/" + h.NewFolder("Two").Replace('\\', '/'))); Sync(); Assert.True(edit.IsEnabled);
        var presenter = new RecordingPresenter(); using var seam = Presentation.Presenter.Override(presenter); Click(edit); Sync(); var popup = presenter.Calls.Last(c => c.Presented).Popup; Assert.Equal(PlacementMode.Bottom, popup.Placement); Assert.Same(edit, popup.PlacementTarget);
        var list = Opened(popup); Assert.Equal(400, list.Width); var rows = Nodes<ChromeRow>(list); Assert.Equal(model.Projects.Select(p => p.Name), rows.Select(r => Text(r).First())); Assert.Equal(model.Projects.Select(p => p.Path), rows.Select(r => Text(r)[1])); Assert.Single(Nodes<ChromeRowDivider>(list)); Assert.Equal(2, Nodes<ProjectIconButton>(list).Length);
    });

    // Resolved STOP opencode-proofs-settings-core #3 (UI-SETTINGS-016): the small .glass Edit is the accent glass pill measured in the reference runtime
    // (ED-280 (2): 20 high, white 11 point label, 10.5 either side); SettingsViewBlitzBTests.Marks_edit_is_the_small_accent_glass_pill_... asserts it.

    // Resolved STOP opencode-proofs-settings-core #5 (UI-SETTINGS-003, UI-SETTINGS-012):
    // UI-SETTINGS-003's "system focus ring is left on" is answered by the reference runtime: the main sidebar's "Search threads" field is the same
    // SidebarSearchField (SidebarView.swift:83) and the video shows no ring on focus, only a caret fading in over 0.18-0.24 s (CATALOGUE
    // composer.caret-focus-fade-in, S 5.785-5.900, C 80.115-80.333); the port draws no ring either (FocusAdorner unset, asserted above).
    // UI-SETTINGS-012's switch is drawn at the Mac's measured size, colours and motion (ED-280 (5); SmallSwitchMotionTests).
    // Former STOP opencode-proofs-settings-core #6 (ED-181) is closed: Chrome.swift:722 / ChromeVisualPicker.swift:92,95,99
    // continuous corners are Apple's curve (SwiftShapes.Outline, SwiftShapeBorder; the tile stage and the sidebar row by
    // Visual.Clip, the ring by its own path). CheckTiles asserts the style, the continuous outline and the clip; the curve
    // itself is SwiftShapesContinuousTests / SwiftShapesOracleTests in Marrow.UI.Tests.

    [Fact]
    [Trait("Rule", "R-TEST-2")] // Partial proof only; see precise STOP.
    public void Theme_capsule_pins_swatch_menu_names_sections_checkmark_hover_and_immediate_selection() => WithView(Marrow.App.Tests.Fixtures.ThemePickerHost.Page, (view, model, h) => // OD-0088: the capsule beside the page
    {
        // SettingsView.swift:1159-1210; AppTheme.swift:44-73,109-176. UI-SETTINGS-017 subset; STOP #2.
        var picker = Nodes<ThemePickerButton>(view).Single(); var label = Nodes<TextBlock>(picker).Single(); var stack = Nodes<StackPanel>(picker).Single();
        Assert.Equal(28, stack.Height); Assert.Equal(new Thickness(12, 0), stack.Margin); Assert.Equal(6, stack.Spacing); Assert.Equal(12.5, label.FontSize); Assert.Equal(FontWeight.Medium, label.FontWeight); CheckGlass(picker, true);
        var swatch = Nodes<ThemeSwatch>(picker).Single(); Assert.Equal(14, swatch.Width); Assert.Equal(14, swatch.Height); Assert.Equal(0.5, swatch.StrokeThickness); Assert.Equal(SwiftColor.Primary.Opacity(0.22).Resolve(swatch.ActualThemeVariant), ColorOf(swatch.Stroke));
        var split = Assert.IsType<LinearGradientBrush>(swatch.Fill); Assert.Equal(new[] { 0.0, 0.5, 0.5, 1.0 }, split.GradientStops.Select(s => s.Offset)); Assert.Equal(new[] { Colors.White, Colors.White, Color.Parse("#1E1E20"), Color.Parse("#1E1E20") }, split.GradientStops.Select(s => s.Color));
        var button = Nodes<Button>(picker).Single(); Hover(button, true); Sync(); Frame(h, 101); Foreground(label, Chrome.PrimaryText); Glyph(Nodes<SwiftImage>(picker).Single(), SwiftFont.System(8, SwiftFont.Weight.Bold), Chrome.SecondaryText);
        var presenter = new RecordingPresenter(); using var seam = Presentation.Presenter.Override(presenter); Click(button); Sync(); Foreground(label, Chrome.PrimaryText); var menu = Opened(presenter.Calls.Last(c => c.Presented).Popup);
        Assert.Equal(new[] { "Dark", "Light" }, Nodes<PopoverSectionHeader>(menu).Select(s => Text(s).Single()));
        var names = new[] { "System", "Dark", "Catppuccin Mocha", "Dracula", "Tokyo Night", "Nord", "Gruvbox", "One Dark", "Everforest", "Kanagawa", "Rosé Pine", "Solarized Dark", "GitHub Dark", "Ayu", "Night Owl", "Monokai", "Claude", "Codex", "Cursor", "Matrix", "Light", "Catppuccin Latte", "Gruvbox Light", "Solarized Light", "GitHub Light", "Claude Light" };
        Assert.Equal(names, Nodes<PopoverItem>(menu).Select(i => Text(i).First())); // Swift palette is 19 dark/6 light; spec notes 17/8 is stale.
        Assert.Single(Nodes<SwiftImage>(Nodes<PopoverItem>(menu)[0]), i => i.SymbolName == "checkmark");
        Click(Button(menu, "Nord")); Sync(); Assert.Equal(AppTheme.Nord, model.Settings.Theme); Assert.Contains("Dark · Frost", Text(Row(view, "Theme")));
    });

    // Resolved STOP opencode-proofs-settings-core #4 (UI-SETTINGS-018): the Slider has a production template (PortControlThemes.SliderTheme: a 4 pt track, the
    // 18 x 14 knob, the accent fill); the drag is proved with a real pointer by SettingsViewFinishAppearanceTests and SettingsViewBlitzBTests.
    [Fact]
    [Trait("Rule", "R-TEST-2")] // Partial proof only; see precise STOP.
    public void Transparency_pins_source_row_glyphs_threshold_reset_geometry_and_live_value_binding() => WithView(() => new AppearanceSettingsPage(), (view, model, h) =>
    {
        // SettingsView.swift:524-563,427-429; UI-SETTINGS-018 subset, STOP #4.
        var row = Row(view, "Transparency"); Assert.Contains("How much shows through the window", Text(row)); var group = Nodes<BackdropOpacitySlider>(row).Single(); Assert.Equal("Window transparency", AutomationProperties.GetName(group));
        var slider = Nodes<Slider>(group).Single(); Assert.Equal(140, slider.Width); Assert.Equal(0, slider.Minimum); Assert.Equal(1, slider.Maximum); Assert.Equal(8, Nodes<StackPanel>(group).Single().Spacing);
        foreach (var image in Nodes<SwiftImage>(group)) Glyph(image, image.SymbolName == "arrow.counterclockwise" ? SwiftFont.System(11, SwiftFont.Weight.Medium) : SwiftFont.System(11), Chrome.SecondaryText);
        Assert.Equal("Clear", ToolTip.GetTip(Assert.IsType<Border>(Nodes<SwiftImage>(group).Single(i => i.SymbolName == "circle.dotted").Parent))); Assert.Equal("Solid", ToolTip.GetTip(Assert.IsType<Border>(Nodes<SwiftImage>(group).Single(i => i.SymbolName == "circle.fill").Parent))); // the tip sits on the glyph's transparent frame (UI-THEME-010)
        var reset = Nodes<Button>(group).Single(); Assert.Equal("Back to the stock look", ToolTip.GetTip(reset)); Assert.Equal(22, reset.Width); Assert.Equal(22, reset.Height); Assert.False(reset.IsVisible);
        var width = group.DesiredSize.Width; slider.Value = 0.504; Sync(); Assert.Equal(0.504, model.Settings.BackdropOpacity); Frame(h, 201); Assert.False(reset.IsVisible);
        slider.Value = 0.506; Sync(); Assert.Equal(0.506, model.Settings.BackdropOpacity); Assert.True(reset.IsVisible); Assert.Equal(0, reset.Opacity); Assert.Equal(-22, ((TranslateTransform)reset.RenderTransform!).X); Frame(h, 100); Assert.InRange(reset.Opacity, 0.001, 1.0); Frame(h, 101); Assert.Equal(1, reset.Opacity); Assert.Equal(-28, ((TranslateTransform)reset.RenderTransform!).X);
        Drain(view); Assert.Equal(width, group.DesiredSize.Width); Click(reset); Sync(); Assert.Equal(0.5, model.Settings.BackdropOpacity); Frame(h, Animation.Snappy(0.2).Duration * 1000 + 1); Assert.False(reset.IsVisible); Assert.Equal(0.5, slider.Value, 10); // ED-011/092: spring settling duration, not response period.
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-049")] // App/Views/Settings/SettingsView.swift:648-675,763-766
    public void Provider_status_rows_pin_mark_frame_fonts_colours_all_subtitle_states_collapse_and_switch() => WithView(() => new ProvidersSettingsPage(), (view, model, h) =>
    {
        Assert.Equal(16, Nodes<ProviderSettingsSection>(view).Length);
        foreach (var kind in new[] { ProviderKind.Commandcode, ProviderKind.Deepseek })
        {
            var section = Nodes<ProviderSettingsSection>(view).Single(s => Field<ProviderKind>(s, "provider") == kind);
            var reveal = Nodes<ChromeRevealLayout>(section).Single(); Assert.Equal(0, reveal.Fraction); Assert.False(reveal.IsEnabled); Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(reveal));
            Click(Button(Nodes<CollapsibleProviderHeader>(section).Single(), kind.DisplayName)); Sync(); Frame(h, Chrome.PanelSlide.Duration * 1000 + 1); Assert.Equal(1, reveal.Fraction); Assert.True(reveal.IsEnabled); // Chrome.swift:100-104 .spring(response:.32,dampingFraction:.9), ED-011 settling.
            var grid = Nodes<Grid>(section).Single(g => g.ColumnDefinitions.Count == 3 && g.ColumnDefinitions[0].Width.Value == 22); Assert.Equal(12, grid.ColumnSpacing); Assert.Equal(new Thickness(16, 11, 10, 11), grid.Margin);
            var mark = Nodes<ProviderIcon>(grid).Single(); Assert.Equal(18, mark.Size); var markImage = Nodes<SwiftImage>(mark).Single(); Assert.Equal(18, markImage.Width); Assert.Equal(Chrome.PrimaryText.Resolve(markImage.ActualThemeVariant), ColorOf(markImage.Foreground)); Assert.Equal(22, grid.ColumnDefinitions[0].Width.Value); // :652 .foregroundStyle(Chrome.primaryText).
            var labels = (StackPanel)grid.Children[1]; Assert.Equal(2, labels.Spacing);
            Seed(model, kind, new ProviderStatus { IsChecking = true }); Sync(); Assert.Equal("Checking…", Text(labels).First()); Assert.Equal(13, Nodes<TextBlock>(labels).First().FontSize); Foreground(Nodes<TextBlock>(labels).First(), Chrome.PrimaryText);
            Seed(model, kind, kind == ProviderKind.Commandcode ? new ProviderStatus { Version = "1.2.3" } : new ProviderStatus { ApiKeyConfigured = true }); Sync();
            Assert.Equal(kind == ProviderKind.Commandcode ? "Version 1.2.3" : "Native API · api.deepseek.com", Text(labels)[1]); Assert.Equal(11, Nodes<TextBlock>(labels)[1].FontSize); Foreground(Nodes<TextBlock>(labels)[1], Chrome.SecondaryText);
            Seed(model, kind, new ProviderStatus()); Sync(); Assert.Single(Nodes<TextBlock>(labels));
            var toggle = Nodes<SmallSwitch>(grid).Single(); var enabled = model.Settings.IsEnabled(kind); Key(toggle, Avalonia.Input.Key.Space); Assert.Equal(!enabled, model.Settings.IsEnabled(kind));
            Click(Button(Nodes<CollapsibleProviderHeader>(section).Single(), kind.DisplayName)); Sync(); Frame(h, Chrome.PanelSlide.Duration * 1000 + 1); Assert.Equal(0, reveal.Fraction); Assert.False(reveal.IsEnabled); Assert.False(reveal.IsHitTestVisible); Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(reveal));
        }
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-252")] // App/Views/Settings/SettingsView.swift:280; MCPSettingsPage.swift:554-568; OD-0008 A1, OD-0012 E1
    public void Shell_custom_server_delete_asks_first_cancels_preserves_then_confirms_and_bypasses_when_off() => WithView(() => new SettingsView(), (view, model, _) =>
    {
        // Coverage inventory for all MCP controls is in oc-proofs-settings-core.report.md, mapped to docs/b3 MCP.json.
        var server = MCPSettingsPageFixture.Custom(); Assert.Null(model.Mcp.SaveCustom(server)); model.Settings.ConfirmBeforeDeleting = true;
        SettingsNavigation.Shared.RequestedPage = SettingsPage.Mcp; Sync(); var row = Nodes<MCPCustomRow>(view).Single();
        var presenter = new RecordingPresenter(); using var seam = Presentation.Presenter.Override(presenter);
        var menu = (Control)typeof(MCPCustomRow).GetMethod("BuildMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(row, null)!;
        // The actual menu's Delete action (not the private deletion method) is driven through its production button.
        Click(Button(menu, "Delete")); Sync(); Assert.Single(model.Mcp.Customs); var popup = presenter.Calls.Last(c => c.Presented).Popup; var confirmation = Opened(popup);
        Assert.Contains("Delete this server?", Text(confirmation)); Assert.Contains("“Example” and the values it keeps in Credential Manager will be removed.", Text(confirmation)); Assert.Equal("Delete server", Text(Nodes<PopoverItem>(confirmation).Single()).First());
        Foreground(Nodes<MiddleTruncatingText>(Nodes<PopoverItem>(confirmation).Single()).Single(), Chrome.Danger); // MCPSettingsPage.swift:554-568 destructive confirmation, Popovers.swift:315-317.
        Popovers.GetClosePopover(confirmation)!.Invoke(); Sync(); Assert.Single(model.Mcp.Customs);
        Click(Button(menu, "Delete")); Sync(); confirmation = Opened(presenter.Calls.Last(c => c.Presented).Popup); Click(Button(confirmation, "Delete server")); Sync(); Assert.Empty(model.Mcp.Customs);
        Assert.Null(model.Mcp.SaveCustom(server)); Sync(); row = Nodes<MCPCustomRow>(view).Single(); menu = (Control)typeof(MCPCustomRow).GetMethod("BuildMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(row, null)!;
        model.Settings.ConfirmBeforeDeleting = false; var count = presenter.Calls.Count; Click(Button(menu, "Delete")); Sync(); Assert.Empty(model.Mcp.Customs); Assert.Equal(count, presenter.Calls.Count);
    });
}
