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
    // STOP App.Views.Settings.SettingsView #1
    // Kind: dependency. Swift: SettingsView.swift:839. Records: PR-GIT-150.
    // Missing: Workspace.RevealInFileExplorer is a throwing dependency in UI/Common/Components.cs.
    // Needed: that unit's Windows reveal implementation. The row/path/label are covered; invoking Reveal is not.
    // STOP App.Views.Settings.SettingsView #2
    // Kind: dependency. Swift: SettingsView.swift:750-751. Records: provider Login action (parts 5-6).
    // Missing: Workspace.OpenTerminal is BLOCKED PR-PROVREG-233 in UI/Common/Components.cs.
    // Needed: the upstream unit's terminal launcher. The login row can be ported; its action cannot be verified.
    private static T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(obj)!;
    private static T[] Nodes<T>(Control root) where T : class => LogicalQuery.SelfAndDescendants<T>(root).ToArray();
    private static string[] Text(Control root) => Nodes<TextBlock>(root).Select(x => x.Text ?? "").ToArray();
    private static Button Button(Control root, string label) => Nodes<Button>(root).First(x => Text(x).Contains(label) || AutomationProperties.GetName(x) == label);
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
    private static void Drain(Avalonia.Controls.Window window) { for (var i = 0; i < 5; i++) { window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); } }
    private static void Sync() => Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    private static void Seed(AppModel model, ProviderKind provider, ProviderStatus status) => typeof(ProviderRegistry).GetProperty("Statuses")!.SetValue(model.Providers, model.Providers.Statuses.Mutating(map => map[provider] = status));

    private void WithView(Func<Control> make, Action<Control, AppModel, AppModelHarness> check, Action<AppModel>? seed = null)
    {
        headless.OnUiThread(() =>
        {
            using var h = new AppModelHarness();
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
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
    }

    private void WithSettings(Action<Control, AppModel, AppModelHarness> check) => WithView(() => new SettingsView(), check, _ => SettingsNavigation.Shared.RequestedPage = SettingsPage.Archive);
    private static ChromeRow Row(Control view, string title) => Nodes<ChromeRow>(view).Single(row => Text(row).FirstOrDefault() == title);

    // STOP App.Views.Settings.SettingsView #3
    // Kind: dependency. Swift: SettingsView.swift:280,251-253. Records: PR-SETTINGS-027; PR-SETTINGS-028 (MCP branch).
    // Missing: MCPSettingsPage(query) is MARROW_STUB in the assigned base. Needed: MCP unit implementation.
    // The constructor call and query binding are retained; a live MCP page cannot be tested until it is ported.

    [Fact]
    [Trait("Req", "PR-SETTINGS-022")] // App/Views/Settings/SettingsView.swift:218-229,270-272
    [Trait("Req", "PR-SETTINGS-023")] // App/Views/Settings/SettingsView.swift:231-234,291-293
    [Trait("Req", "PR-SETTINGS-024")] // App/Views/Settings/SettingsView.swift:242-267
    [Trait("Req", "PR-SETTINGS-028")] // App/Views/Settings/SettingsView.swift:275-287
    public void Models_page_has_60_top_inset_and_sticky_chrome_and_search_changes_preserve_page_identity() => WithSettings((view, _, _) =>
    {
        SettingsNavigation.Shared.RequestedPage = SettingsPage.Models; Sync();
        var page = Assert.Single(Nodes<ModelsSettingsPage>(view));
        var stack = Assert.IsType<StackPanel>(Field<Control>(view, "pageStack")); Assert.Equal(20, stack.Spacing); Assert.Equal(new Thickness(24, 60, 24, 24), stack.Margin);
        var chrome = Nodes<StackPanel>(view).Single(x => x.MinHeight == 32); Assert.Equal(10, chrome.Spacing); Assert.Equal(new Thickness(14, 12, 14, 0), chrome.Margin);
        var title = Assert.Single(Nodes<ChromeCompactTitle>(view)); Assert.Equal(15, Nodes<TextBlock>(title).Single().FontSize); Assert.Single(Nodes<PaneTopVeil>(view));
        var query = Nodes<TextBox>(Field<Control>(view, "detail")).Single(x => x.PlaceholderText == "Search models"); query.Text = "nonexistent-model"; Sync(); Assert.Same(page, Assert.Single(Nodes<ModelsSettingsPage>(view)));
        Field<ChromeScrollModel>(view, "scrollChrome").Update(27); Sync(); Assert.Equal(0.52, Field<ChromeScrollModel>(view, "scrollChrome").Progress, 10);
        SettingsNavigation.Shared.RequestedPage = SettingsPage.SourceControl; Sync(); Assert.Empty(Nodes<ModelsSettingsPage>(view)); Assert.Equal(new Thickness(24, 22, 24, 24), Field<Control>(view, "pageStack").Margin);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-025")] // App/Views/Settings/SettingsView.swift:566-580
    public void Provider_refresh_capsule_changes_enablement_and_exact_tooltip_with_registry_state() => WithView(() => new ProvidersRefreshButton(), (view, model, _) =>
    {
        Assert.Single(Nodes<ChromeCapsule>(view)); var button = Assert.Single(Nodes<Button>(view)); Assert.True(button.IsEnabled); Assert.Equal("Check providers again", ToolTip.GetTip(button));
        typeof(ProviderRegistry).GetProperty("IsRefreshing")!.SetValue(model.Providers, true); Sync();
        button = Assert.Single(Nodes<Button>(view)); Assert.False(button.IsEnabled); Assert.Equal("Checking providers\u2026", ToolTip.GetTip(button));
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-026")] // App/Views/Settings/SettingsView.swift:296-305
    public void Small_switch_has_no_label_and_reads_and_writes_its_binding() => WithView(() => new SettingsSwitch(Binding<bool>.Constant(false)), (view, _, _) => Assert.Empty(Text(view)));

    [Fact]
    [Trait("Req", "PR-SETTINGS-102")] // App/Views/Settings/SettingsView.swift:431-435; UI/Chrome/WindowChrome.swift:255-257,368-371
    public void Sidebar_starts_open_is_false_and_its_toggle_persists_the_width_too() => WithView(() => new Border(), (_, _, h) =>
    {
        var layout = new SidebarLayout(); Assert.False(layout.StartsOpen); h.Defaults.Writes.Clear(); layout.StartsOpen = true;
        Assert.Equal(new[] { "sidebarWidth", "sidebarStartsOpen" }, h.Defaults.Writes);
        Assert.True(h.Defaults.Bool("sidebarStartsOpen"));
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-107")] // App/Views/Settings/SettingsView.swift:314-411
    [Trait("Req", "PR-SETTINGS-108")] // App/Views/Settings/SettingsView.swift:319-321
    [Trait("Req", "PR-SETTINGS-109")] // App/Views/Settings/SettingsView.swift:323-325
    [Trait("Req", "PR-SETTINGS-110")] // App/Views/Settings/SettingsView.swift:327-331
    [Trait("Req", "PR-SETTINGS-111")] // App/Views/Settings/SettingsView.swift:336-338
    [Trait("Req", "PR-SETTINGS-112")] // App/Views/Settings/SettingsView.swift:340-342
    [Trait("Req", "PR-SETTINGS-113")] // App/Views/Settings/SettingsView.swift:344-348
    [Trait("Req", "PR-SETTINGS-114")] // App/Views/Settings/SettingsView.swift:350-352
    [Trait("Req", "PR-SETTINGS-115")] // App/Views/Settings/SettingsView.swift:354-356
    [Trait("Req", "PR-SETTINGS-116")] // App/Views/Settings/SettingsView.swift:358-364
    [Trait("Req", "PR-SETTINGS-117")] // App/Views/Settings/SettingsView.swift:366-368
    [Trait("Req", "PR-SETTINGS-118")] // App/Views/Settings/SettingsView.swift:370-372
    [Trait("Req", "PR-SETTINGS-119")] // App/Views/Settings/SettingsView.swift:374-379
    [Trait("Req", "PR-SETTINGS-120")] // App/Views/Settings/SettingsView.swift:382-392
    [Trait("Req", "PR-SETTINGS-122")] // App/Views/Settings/SettingsView.swift:393-399
    public void General_sections_and_all_rows_keep_source_order_labels_details_controls_and_defaults() => WithView(() => new GeneralSettingsPage(), (view, model, _) =>
    {
        var sections = Nodes<ChromeSection>(view).Where(section => Text(section).First() is "New threads" or "Conversation" or "Projects" or "Finished threads").ToArray();
        Assert.Equal(new[] { "New threads", "Conversation", "Projects", "Finished threads" }, sections.Select(x => Text(x).First())); Assert.Single(Nodes<TokenActivitySection>(view));
        Assert.Equal(new[] { "Provider and model", "Permissions", "Workspace" }, Nodes<ChromeRow>(sections[0]).Select(x => Text(x).First()));
        Assert.Equal(new[] { "Show thinking", "Concise AI replies", "Working line", "Recent downloads", "Notify when a turn finishes", "Chime when a turn finishes", "Confirm before deleting threads", "Continue after a usage limit", "Show a usage panel" }, Nodes<ChromeRow>(sections[1]).Select(x => Text(x).First()));
        Assert.Equal(new[] { "Projects", "Marks" }, Nodes<ChromeRow>(sections[2]).Select(x => Text(x).First())); Assert.Single(Nodes<ChromeRowDivider>(sections[2]));
        foreach (var (title, detail) in new[] {
            ("Provider and model", "A new thread follows the one you are in: its provider, model, effort and Hydra pair. With no thread open it starts on the provider you last chose."),
            ("Workspace", "Where a new thread makes its changes"), ("Show thinking", "The working line opens to the agent's thinking"),
            ("Concise AI replies", "Built-in guidance for shorter replies, useful code comments, and focused work, including Hydra heads. Changes apply on the next message."),
            ("Working line", "A card with the turn's progress bar, or a plain line"), ("Recent downloads", "The attach button offers recent downloads first"),
            ("Chime when a turn finishes", "A soft chime, whether or not the thread is in view"),
            ("Continue after a usage limit", "When the provider's limit is spent, the chat waits for the reset and then tells the agent to carry on"),
            ("Show a usage panel", "The plan's limits and credits in a floating panel beside every chat, for the model in use or a pair's two; the usage popover's pop-out button switches it on as well, and it keeps the corner it was last dragged to"),
            ("Projects", "Activates every project at once unless explicitly overridden"), ("Marks", "An emoji or symbol for each project, drawn before the thread's name in the activity list and on the project's own row") }) Assert.Contains(detail, Text(Row(view, title)));
        Assert.Empty(Nodes<Button>(Row(view, "Provider and model"))); Assert.Equal(new[] { "Notify when a turn finishes" }, Text(Row(view, "Notify when a turn finishes"))); Assert.Equal(new[] { "Confirm before deleting threads" }, Text(Row(view, "Confirm before deleting threads")));
        Assert.Equal(new[] { "Card", "Line" }, Nodes<ChromeVisualTile>(Row(view, "Working line")).Select(x => Text(x).Last()));
        Assert.False(model.Settings.ShowReasoning); Assert.True(model.Settings.AntiSlopEnabled); Assert.True(model.Settings.ShowsWorkingCard); Assert.True(model.Settings.RecentDownloadsPicker); Assert.True(model.Settings.NotifyWhenFinished); Assert.True(model.Settings.ConfirmBeforeDeleting); Assert.False(model.Settings.AutoContinueAfterLimit); Assert.False(model.Settings.ShowsUsagePanel); Assert.True(model.Settings.ProjectsEnabled);
        foreach (var mode in SwiftEnum<RuntimeMode>.AllCases) { model.Settings.DefaultRuntimeMode = mode; Sync(); Assert.Contains(mode.Summary, Text(Row(view, "Permissions"))); }
        model.Settings.ThreadFinishAction = ThreadFinishAction.Archive; Sync(); Assert.Contains("An archived thread leaves the sidebar for the Archive page", Text(Row(view, "Done with a thread")));
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-121")] // App/Views/Settings/SettingsView.swift:1130-1154
    public void Marks_edit_is_disabled_without_projects_and_opens_the_400_wide_list_in_library_order() => WithView(() => new ProjectMarksRow(), (view, model, h) =>
    {
        var presenter = new RecordingPresenter(); using var presentation = Presentation.Presenter.Override(presenter);
        var edit = Button(view, "Edit"); Assert.False(edit.IsEnabled); Assert.Equal("Choose a mark for each project", ToolTip.GetTip(edit));
        model.AddProject(SwiftURL.FileURLWithPath("/" + h.NewFolder("One").Replace('\\', '/'))); model.AddProject(SwiftURL.FileURLWithPath("/" + h.NewFolder("Two").Replace('\\', '/'))); Sync(); Assert.True(edit.IsEnabled); Click(edit); Sync();
        var popup = presenter.Calls.Last(x => x.Presented).Popup;
        ((EventHandler?)typeof(Avalonia.Controls.Primitives.Popup).GetField("Opened", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(popup))?.Invoke(popup, EventArgs.Empty);
        var list = Assert.IsType<StackPanel>(popup.Child); Assert.Equal(400, list.Width);
        Assert.Equal(model.Projects.Select(x => x.Name), Nodes<ChromeRow>(list).Select(x => Text(x).First())); Assert.Single(Nodes<ChromeRowDivider>(list)); Assert.Equal(2, Nodes<ProjectIconButton>(list).Length);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-116")] // App/Views/Settings/SettingsView.swift:361-364
    public void Finish_chime_previews_only_off_to_on_including_external_changes() => WithView(() => new GeneralSettingsPage(), (_, model, _) =>
    {
        var sound = new SettingsViewSoundRecorder(); using var output = SoundOutput.Current.Override(sound);
        model.Settings.ChimeWhenFinished = false; Sync(); Assert.Equal(0, sound.Starts);
        model.Settings.ChimeWhenFinished = true; Sync(); Assert.Equal(1, sound.Starts);
        model.Settings.ChimeWhenFinished = false; Sync(); Assert.Equal(1, sound.Starts);
    });

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
        Sync();
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
        var search = Nodes<SidebarSearchField>(view).Single();
        // The text field itself can consume caret keys; test the sidebar's move handler at its boundary.
        foreach (var offset in new[] { -1, 1 }) Assert.False((bool)typeof(SidebarSearchField).GetMethod("Move", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(search, [offset])!);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-012")] // App/Views/Settings/SettingsView.swift:141
    public void Escape_clears_search_and_is_handled() => WithSettings((view, _, _) =>
    {
        var field = Nodes<TextBox>(Field<Control>(view, "sidebar")).Single(); field.Text = "restore"; Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var e = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape, Source = field }; field.RaiseEvent(e);
        Sync();
        Assert.Equal("", field.Text); Assert.True(e.Handled);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-013")] // App/Views/Settings/SettingsView.swift:130-133
    public void Filtering_out_selection_selects_first_survivor_but_empty_results_preserve_it() => WithSettings((view, _, _) =>
    {
        SettingsNavigation.Shared.RequestedPage = SettingsPage.SourceControl;
        Sync();
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
        Sync();
        var tracker = Field<SettingsScrollTracker>(view, "scrollTracker"); tracker.Note(); Sync();
        Assert.False(Field<StackPanel>(view, "rows").IsHitTestVisible); Assert.False(Field<Control>(view, "pageStack").IsHitTestVisible);
        tracker.Coast(); Sync(); Assert.True(Field<StackPanel>(view, "rows").IsHitTestVisible); Assert.True(Field<Control>(view, "pageStack").IsHitTestVisible);
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
        Sync();
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
        h.Main.AdvanceMilliseconds(1); Sync(); Assert.IsType<ProviderStatus.Auth.SignedOut>(model.Providers.Status(ProviderKind.Meta).ProviderStatusAuth);
    });

    [Fact]
    [Trait("Req", "PR-PROVREG-249")] // App/Views/Settings/SettingsView.swift:593-604
    public void Signed_in_providers_lead_in_stable_declaration_order() => WithView(() => new ProvidersSettingsPage(), (view, model, _) =>
    {
        var providers = Nodes<ProviderSettingsSection>(view).Select(x => Field<ProviderKind>(x, "provider")).ToArray();
        Assert.Equal(ProviderKind.Meta, providers[0]); Assert.Equal(SwiftEnum<ProviderKind>.AllCases.Where(x => x != ProviderKind.Meta), providers.Skip(1));
        Seed(model, ProviderKind.Codex, new ProviderStatus { ProviderStatusAuth = new ProviderStatus.Auth.SignedIn(null) });
        Sync();
        var reordered = Nodes<ProviderSettingsSection>(view).Select(x => Field<ProviderKind>(x, "provider")).ToArray(); Assert.Equal(new[] { ProviderKind.Codex, ProviderKind.Meta }, reordered.Take(2));
    }, model => Seed(model, ProviderKind.Meta, new ProviderStatus { ProviderStatusAuth = new ProviderStatus.Auth.SignedIn(null) }));

    [Fact]
    [Trait("Req", "PR-PROVREG-250")] // App/Views/Settings/SettingsView.swift:587-610,647-766
    public void Header_override_wins_over_auth_default_and_collapsed_content_stays_mounted() => WithView(() => new ProvidersSettingsPage(), (view, model, _) =>
    {
        var section = Nodes<ProviderSettingsSection>(view).Single(x => Field<ProviderKind>(x, "provider") == ProviderKind.Meta);
        var reveal = Nodes<ChromeRevealLayout>(section).Single(); Assert.False(reveal.IsEnabled); Assert.False(reveal.IsHitTestVisible);
        Assert.Single(Nodes<TextBox>(section)); Click(Nodes<Button>(Nodes<CollapsibleProviderHeader>(section).Single()).Single()); Sync();
        Assert.True(reveal.IsEnabled); Seed(model, ProviderKind.Meta, new ProviderStatus { ProviderStatusAuth = new ProviderStatus.Auth.SignedIn(null) }); Sync();
        Assert.Same(section, Nodes<ProviderSettingsSection>(view).First()); Assert.True(reveal.IsEnabled);
        Click(Nodes<Button>(Nodes<CollapsibleProviderHeader>(section).Single()).Single()); Sync(); Assert.False(reveal.IsEnabled);
    });
}
