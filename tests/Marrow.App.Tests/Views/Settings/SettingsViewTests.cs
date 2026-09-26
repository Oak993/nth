using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

// Source: DroppyCode/App/Views/Settings/SettingsView.swift (B3 52bc792), :4-846.
[Collection("Headless")]
public sealed class SettingsViewTests(HeadlessSession headless)
{
    private static T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(obj)!;
    private static T[] Nodes<T>(Control root) where T : class => LogicalQuery.SelfAndDescendants<T>(root).ToArray();
    private static string[] Text(Control root) => Nodes<TextBlock>(root).Select(x => x.Text ?? "").ToArray();
    private static Button Button(Control root, string label) => Nodes<Button>(root).First(x => Text(x).Contains(label) || AutomationProperties.GetName(x) == label);
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
    private static void Drain(Avalonia.Controls.Window window) { for (var i = 0; i < 5; i++) { window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); } }
    private static void Seed(AppModel model, ProviderKind provider, ProviderStatus status) => typeof(ProviderRegistry).GetProperty("Statuses")!.SetValue(model.Providers, model.Providers.Statuses.Mutating(map => map[provider] = status));

    private void WithView(Func<Control> make, Action<Control, AppModel, AppModelHarness> check, Action<AppModel>? seed = null)
    {
        headless.OnUiThread(() =>
        {
            using var h = new AppModelHarness();
            h.Main.OnMain(() =>
            {
                var model = new AppModel();
                seed?.Invoke(model);
                var host = new Border();
                AppEnvironment.SetAppModel(host, model);
                var view = make();
                host.Child = view;
                var window = new Avalonia.Controls.Window { Content = host, Width = 780, Height = 580 };
                try { window.Show(); Drain(window); check(view, model, h); Drain(window); }
                finally { window.Close(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); SettingsNavigation.Shared.RequestedPage = null; }
            });
        });
    }

    private void WithSettings(Action<Control, AppModel, AppModelHarness> check) => WithView(() => new SettingsView(), check, _ => SettingsNavigation.Shared.RequestedPage = SettingsPage.Archive);

    [Fact]
    [Trait("Req", "PR-SETTINGS-005")] // App/Views/Settings/SettingsView.swift:4-32
    public void Pages_keep_declaration_order_exact_titles_and_raw_identifiers()
    {
        var pages = SwiftEnum<SettingsPage>.AllCases;
        Assert.Equal(new[] { "general", "hydra", "appearance", "providers", "mcp", "models", "sourceControl", "shortcuts", "archive", "about" }, pages.Select(x => x.Id));
        Assert.Equal(new[] { "General", "Hydra", "Appearance", "Providers", "MCP", "Models", "Source control", "Shortcuts", "Archive", "About" }, pages.Select(x => x.Title));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-006")] // App/Views/Settings/SettingsView.swift:34-49,151-157
    public void Symbols_keep_the_exhaustive_source_mapping() => Assert.Equal(new[] { "gear", "point.3.connected.trianglepath.dotted", "paintpalette", "cpu", "puzzlepiece.extension", "slider.horizontal.3", "arrow.triangle.branch", "command", "archivebox", "info.circle" }, SwiftEnum<SettingsPage>.AllCases.Select(x => x.Symbol));

    [Fact]
    [Trait("Req", "PR-SETTINGS-007")] // App/Views/Settings/SettingsView.swift:51-64
    public void Tints_keep_source_rgb_and_theme_tokens()
    {
        var theme = ThemeVariant.Light;
        Assert.Equal(Chrome.Gray.Resolve(theme), SettingsPage.General.Tint.Resolve(theme));
        Assert.Equal(Chrome.Blue.Resolve(theme), SettingsPage.Providers.Tint.Resolve(theme));
        Assert.Equal(Chrome.Orange.Resolve(theme), SettingsPage.Archive.Tint.Resolve(theme));
        foreach (var (page, rgb) in new[] { (SettingsPage.Appearance, (0.85, 0.44, 0.36)), (SettingsPage.Mcp, (0.95, 0.55, 0.20)), (SettingsPage.Models, (0.686, 0.322, 0.871)), (SettingsPage.Hydra, (0.188, 0.690, 0.780)), (SettingsPage.SourceControl, (0.345, 0.337, 0.839)), (SettingsPage.Shortcuts, (0.32, 0.48, 0.93)), (SettingsPage.About, (0.204, 0.780, 0.349)) })
            Assert.Equal(new SwiftColor(rgb.Item1, rgb.Item2, rgb.Item3).Resolve(theme), page.Tint.Resolve(theme));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-008")] // App/Views/Settings/SettingsView.swift:66-79
    public void Keyword_arrays_are_verbatim_including_B3_provider_additions()
    {
        Assert.Equal("permissions|worktree|reasoning|thinking|notifications|token|tokens|activity|usage|panel|limits|credits|heatmap|daily|weekly|cumulative|settle|settled|finish|finished|done|sound|chime|icon|icons|emoji|symbol|symbols|project mark|compact|concise|anti-slop|slop|verbose|comments|replies", string.Join('|', SettingsPage.General.Keywords));
        Assert.Equal("appearance|theme|transparency|transparent|opacity|glass|dark|light|accent|tint|catppuccin|dracula|tokyo|nord|gruvbox|solarized|github|claude|codex|cursor|matrix|text|size|font|zoom|wallpaper|sidebar", string.Join('|', SettingsPage.Appearance.Keywords));
        Assert.Equal("codex|claude|cursor|opencode|opencode zen|opencode go|zen|grok|deepseek|meta|muse|spark|zai|z.ai|glm|coding plan|devin|cognition|antigravity|agy|google|gemini|copilot|github|command code|commandcode|cmd|binary|path|sign in|login|api key|usage|limits|limit|plan|quota|credits|balance", string.Join('|', SettingsPage.Providers.Keywords));
        Assert.Equal("model|effort|reasoning|fast|slider|picker", string.Join('|', SettingsPage.Models.Keywords));
        Assert.Equal("hydra|heads|subagents|sub-agents|agents|team|orchestrator|worker|pair|pairs|parallel|delegate|queue", string.Join('|', SettingsPage.Hydra.Keywords));
        Assert.Equal("mcp|model context protocol|servers|tools|connect|integrations|github|playwright|notion|linear|slack|figma|supabase|stripe|resend|email|context7|sentry|postgres|browser|netlify|deploy|hosting", string.Join('|', SettingsPage.Mcp.Keywords));
        Assert.Equal("git|commit|pull request|titles|text generation", string.Join('|', SettingsPage.SourceControl.Keywords));
        Assert.Equal(new[] { "keyboard", "keys" }, SettingsPage.Shortcuts.Keywords);
        Assert.Equal(new[] { "archived", "restore" }, SettingsPage.Archive.Keywords);
        Assert.Equal(new[] { "version", "license" }, SettingsPage.About.Keywords);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-003")] // App/Views/Settings/SettingsView.swift:123-127
    public void Requested_page_is_consumed_on_appearance_and_on_later_change() => WithSettings((view, _, _) =>
    {
        Assert.Null(SettingsNavigation.Shared.RequestedPage);
        Assert.Equal(SettingsPage.Archive, Field<object>(view, "state").GetType().GetProperty("Page")!.GetValue(Field<object>(view, "state")));
        SettingsNavigation.Shared.RequestedPage = SettingsPage.SourceControl;
        Assert.Null(SettingsNavigation.Shared.RequestedPage);
        Assert.Contains("Worktrees and pull requests", Text(view));
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-004")] // App/Views/Settings/SettingsView.swift:110-122
    public void Window_content_is_780_by_580_with_a_212_sidebar_and_26_corner() => WithSettings((view, _, _) =>
    {
        Assert.Equal(780, view.Width); Assert.Equal(580, view.Height);
        Assert.Equal(212, Field<Control>(view, "sidebar").Width);
        Assert.Equal(26, Field<Border>(view, "frame").CornerRadius.TopLeft);
        Assert.Equal(new Thickness(0), Field<Control>(view, "detail").Margin);
        Assert.Single(Nodes<WindowBackdrop>(view));
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-009")] // App/Views/Settings/SettingsView.swift:100-106
    public void Whitespace_search_keeps_all_pages_and_matching_is_case_insensitive() => WithSettings((view, _, _) =>
    {
        var field = Nodes<TextBox>(Field<Control>(view, "sidebar")).Single(); field.Text = " \n\t "; Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(10, Nodes<SidebarRow>(view).Length);
        field.Text = "  RESTORE  "; Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Assert.Single(Nodes<SidebarRow>(view));
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-010")] // App/Views/Settings/SettingsView.swift:141-143
    public void Enter_selects_the_first_match_and_empty_results_leave_selection_alone() => WithSettings((view, _, _) =>
    {
        var field = Nodes<TextBox>(Field<Control>(view, "sidebar")).Single(); field.Text = "restore"; Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = field });
        Assert.Equal(SettingsPage.Archive, Field<object>(view, "state").GetType().GetProperty("Page")!.GetValue(Field<object>(view, "state")));
        field.Text = "no-such-setting"; Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = field });
        Assert.Contains(Nodes<Control>(view), x => AutomationProperties.GetName(x) == "No archived threads");
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-011")] // App/Views/Settings/SettingsView.swift:141-143
    public void Search_arrows_are_unhandled() => WithSettings((view, _, _) =>
    {
        var field = Nodes<TextBox>(Field<Control>(view, "sidebar")).Single();
        foreach (var key in new[] { Key.Up, Key.Down }) { var e = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = field }; field.RaiseEvent(e); Assert.False(e.Handled); }
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-012")] // App/Views/Settings/SettingsView.swift:141
    public void Escape_clears_search_and_is_handled() => WithSettings((view, _, _) =>
    {
        var field = Nodes<TextBox>(Field<Control>(view, "sidebar")).Single(); field.Text = "restore"; Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var e = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape, Source = field }; field.RaiseEvent(e);
        Assert.Equal("", field.Text); Assert.True(e.Handled);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-013")] // App/Views/Settings/SettingsView.swift:130-133
    public void Filtering_out_selection_selects_first_survivor_but_empty_results_preserve_it() => WithSettings((view, _, _) =>
    {
        SettingsNavigation.Shared.RequestedPage = SettingsPage.SourceControl;
        var field = Nodes<TextBox>(Field<Control>(view, "sidebar")).Single(); field.Text = "restore"; Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Contains(Nodes<Control>(view), x => AutomationProperties.GetName(x) == "No archived threads");
        field.Text = "absent-settings-page"; Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Contains(Nodes<Control>(view), x => AutomationProperties.GetName(x) == "No archived threads");
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-014")] // App/Views/Settings/SettingsView.swift:166-177
    public void No_results_has_two_exact_lines_and_source_geometry() => WithSettings((view, _, _) =>
    {
        Nodes<TextBox>(Field<Control>(view, "sidebar")).Single().Text = "nothing-matches-this"; Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var heading = Nodes<TextBlock>(view).Single(x => x.Text == "No results"); Assert.Equal(13, heading.FontSize);
        var note = Nodes<TextBlock>(view).Single(x => x.Text == "Try a setting or a provider name."); Assert.Equal(11, note.FontSize);
        var block = Assert.IsType<StackPanel>(heading.Parent); Assert.Equal(6, block.Spacing); Assert.Equal(new Thickness(8, 14, 8, 0), block.Margin);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-015")] // App/Views/Settings/SettingsView.swift:138-140,192-193
    public void Sidebar_reserves_14_plus_16_and_installs_drag_background() => WithSettings((view, _, _) =>
    {
        var spacer = Nodes<Border>(Field<Control>(view, "sidebar")).Single(x => x.Height == 14 && x.Margin.Top == 16);
        Assert.Equal(14, spacer.Height); Assert.Single(Nodes<WindowDragArea>(Field<Control>(view, "sidebar")));
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-016")] // App/Views/Settings/SettingsView.swift:144-190
    public void Sidebar_rows_use_one_point_spacing_and_source_insets_without_scrollbars() => WithSettings((view, _, _) =>
    {
        var rows = Field<StackPanel>(view, "rows"); Assert.Equal(1, rows.Spacing); Assert.Equal(new Thickness(10, 12, 10, 8), rows.Margin);
        var scroll = Nodes<ScrollViewer>(Field<Control>(view, "sidebar")).Single(); Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, scroll.VerticalScrollBarVisibility);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-017")] // App/Views/Settings/SettingsView.swift:159-163
    public void Empty_feed_shows_no_update_dot_in_any_sidebar_row() => WithSettings((view, _, _) => Assert.Empty(Nodes<UpdateAvailableDot>(view)));

    [Fact]
    [Trait("Req", "PR-SETTINGS-018")] // App/Views/Settings/SettingsView.swift:182-189,227-241
    public void Scroll_freezes_both_contents_and_coasting_restores_hit_testing() => WithSettings((view, _, _) =>
    {
        SettingsNavigation.Shared.RequestedPage = SettingsPage.SourceControl;
        var tracker = Field<SettingsScrollTracker>(view, "scrollTracker"); tracker.Note();
        Assert.False(Field<StackPanel>(view, "rows").IsHitTestVisible); Assert.False(Field<Control>(view, "pageStack").IsHitTestVisible);
        tracker.Coast(); Assert.True(Field<StackPanel>(view, "rows").IsHitTestVisible); Assert.True(Field<Control>(view, "pageStack").IsHitTestVisible);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-020")] // App/Views/Settings/SettingsView.swift:198-208
    public void Empty_archive_bypasses_scroll_and_chrome_entirely() => WithSettings((view, _, _) =>
    {
        Assert.Empty(Nodes<ScrollViewer>(Field<Control>(view, "detail"))); Assert.DoesNotContain("Delete all", Text(Field<Control>(view, "detail")));
        var symbol = Nodes<Control>(view).Single(x => AutomationProperties.GetName(x) == "No archived threads"); Assert.Equal(44, symbol.Width);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-021")] // App/Views/Settings/SettingsView.swift:211-215,230
    public void A_page_change_resets_chrome_and_both_queries() => WithSettings((view, _, _) =>
    {
        var state = Field<object>(view, "state"); state.GetType().GetProperty("ModelSearch")!.SetValue(state, "model"); state.GetType().GetProperty("McpSearch")!.SetValue(state, "server");
        Field<ChromeScrollModel>(view, "scrollChrome").Update(200); SettingsNavigation.Shared.RequestedPage = SettingsPage.SourceControl;
        Assert.Equal(0, Field<ChromeScrollModel>(view, "scrollChrome").Progress);
        Assert.Equal("", state.GetType().GetProperty("ModelSearch")!.GetValue(state)); Assert.Equal("", state.GetType().GetProperty("McpSearch")!.GetValue(state));
        Assert.Equal(new Vector(0, 0), Nodes<ScrollViewer>(Field<Control>(view, "detail")).Single().Offset);
    });

    [Fact]
    [Trait("Req", "PR-GIT-150")] // App/Views/Settings/SettingsView.swift:836-846
    public void Worktree_section_shows_resolved_path_reveal_and_exact_pull_request_detail() => WithView(() => new SourceControlSettingsPage(), (view, _, _) =>
    {
        Assert.Contains("Worktrees and pull requests", Text(view)); Assert.Contains(Storage.WorktreesDirectory, Text(view));
        Assert.Contains("Opened with the GitHub CLI (gh) or GitLab CLI (glab) from your PATH.", Text(view));
        Assert.NotNull(Button(view, "Reveal"));
    });

    [Fact]
    [Trait("Req", "PR-PROVREG-093")] // App/Views/Settings/SettingsView.swift:768-783
    public void Binary_path_loads_and_differing_edits_save_without_reprobe() => WithView(() => new ProviderSettingsSection(ProviderKind.Codex, Binding<bool>.Constant(true)), (view, model, _) =>
    {
        var field = Nodes<TextBox>(view).Single(); Assert.Equal("missing-original", field.Text); field.Text = "missing-changed"; Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal("missing-changed", model.Settings.BinaryPath(ProviderKind.Codex)); Assert.Empty(model.Providers.Statuses);
    }, model => model.Settings.SetBinaryPath("missing-original", ProviderKind.Codex));

    [Fact]
    [Trait("Req", "PR-PROVREG-092")] // App/Views/Settings/SettingsView.swift:796-799
    public void External_key_update_resyncs_the_field_without_scheduling_a_check() => WithView(() => new ProviderSettingsSection(ProviderKind.Meta, Binding<bool>.Constant(true)), (view, model, _) =>
    {
        var field = Nodes<TextBox>(view).Single(); model.Settings.SetAPIKeyInput("synthetic-test-key", ProviderKind.Meta);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Assert.Equal("synthetic-test-key", field.Text); Assert.Null(Field<TaskHandle?>(view, "keyCheck"));
    });

    [Fact]
    [Trait("Req", "PR-PROVREG-091")] // App/Views/Settings/SettingsView.swift:784-795
    public void Edits_save_immediately_replace_debounce_and_recheck_at_600_ms() => WithView(() => new ProviderSettingsSection(ProviderKind.Meta, Binding<bool>.Constant(true)), (view, model, h) =>
    {
        var field = Nodes<TextBox>(view).Single(); field.Text = "synthetic-test-key"; Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal("synthetic-test-key", model.Settings.ApiKeyInput(ProviderKind.Meta)); var first = Field<TaskHandle>(view, "keyCheck");
        h.Main.RunUntilIdle(); h.Main.AdvanceMilliseconds(300); field.Text = ""; Avalonia.Threading.Dispatcher.UIThread.RunJobs(); h.Main.RunUntilIdle();
        Assert.NotSame(first, Field<TaskHandle>(view, "keyCheck")); h.Main.AdvanceMilliseconds(599); Assert.Empty(model.Providers.Statuses);
        h.Main.AdvanceMilliseconds(1); Assert.IsType<ProviderStatus.Auth.SignedOut>(model.Providers.Status(ProviderKind.Meta).ProviderStatusAuth);
    });

    [Fact]
    [Trait("Req", "PR-PROVREG-249")] // App/Views/Settings/SettingsView.swift:593-604
    public void Signed_in_providers_lead_in_stable_declaration_order() => WithView(() => new ProvidersSettingsPage(), (view, model, _) =>
    {
        var providers = Nodes<ProviderSettingsSection>(view).Select(x => Field<ProviderKind>(x, "provider")).ToArray();
        Assert.Equal(ProviderKind.Meta, providers[0]); Assert.Equal(SwiftEnum<ProviderKind>.AllCases.Where(x => x != ProviderKind.Meta), providers.Skip(1));
        Seed(model, ProviderKind.Codex, new ProviderStatus { ProviderStatusAuth = new ProviderStatus.Auth.SignedIn(null) });
        var reordered = Nodes<ProviderSettingsSection>(view).Select(x => Field<ProviderKind>(x, "provider")).ToArray(); Assert.Equal(new[] { ProviderKind.Codex, ProviderKind.Meta }, reordered.Take(2));
    }, model => Seed(model, ProviderKind.Meta, new ProviderStatus { ProviderStatusAuth = new ProviderStatus.Auth.SignedIn(null) }));

    [Fact]
    [Trait("Req", "PR-PROVREG-250")] // App/Views/Settings/SettingsView.swift:587-610,647-766
    public void Header_override_wins_over_auth_default_and_collapsed_content_stays_mounted() => WithView(() => new ProvidersSettingsPage(), (view, model, _) =>
    {
        var section = Nodes<ProviderSettingsSection>(view).Single(x => Field<ProviderKind>(x, "provider") == ProviderKind.Meta);
        var reveal = Nodes<ChromeRevealLayout>(section).Single(); Assert.False(reveal.IsEnabled); Assert.False(reveal.IsHitTestVisible);
        Assert.Single(Nodes<TextBox>(section)); Click(Nodes<Button>(Nodes<CollapsibleProviderHeader>(section).Single()).Single());
        Assert.True(reveal.IsEnabled); Seed(model, ProviderKind.Meta, new ProviderStatus { ProviderStatusAuth = new ProviderStatus.Auth.SignedIn(null) });
        Assert.Same(section, Nodes<ProviderSettingsSection>(view).First()); Assert.True(reveal.IsEnabled);
        Click(Nodes<Button>(Nodes<CollapsibleProviderHeader>(section).Single()).Single()); Assert.False(reveal.IsEnabled);
    });
}
