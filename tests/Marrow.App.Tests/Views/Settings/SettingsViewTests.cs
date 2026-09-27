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
using Marrow.UI.Theme;
using Marrow.UI.Markdown;
using Marrow.Core.Support;
using Marrow.Testing;
using Avalonia.VisualTree;
using Avalonia.Controls.Presenters;
using Marrow.Services.Port;
using Marrow.UI.Common;
using Avalonia.Automation.Provider;

namespace Marrow.App.Tests.Views.Settings;

// Source: DroppyCode/App/Views/Settings/SettingsView.swift (B3 52bc792), :4-846.
[Collection("Headless")]
public sealed class SettingsViewTests(HeadlessSession headless)
{
    // Resolved STOP App.Views.Settings.SettingsView #1 (Components fixes): Workspace.RevealInFileExplorer
    // is implemented. Worktree_reveal_calls_the_workspace_boundary_with_the_resolved_directory proves the
    // SettingsView.swift:839 callback through the offline Workspace.Platform seam. No dependency remains.
    // Resolved STOP App.Views.Settings.SettingsView #2 (Components fixes): Workspace.OpenTerminal
    // is implemented. Provider_login_calls_the_workspace_boundary_with_the_literal_command proves
    // SettingsView.swift:750-751 through the offline seam. No dependency remains at this site.
    private static T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(obj)!;
    private static T[] Nodes<T>(Control root) where T : class => LogicalQuery.SelfAndDescendants<T>(root).ToArray();
    private static string[] Text(Control root) => Nodes<TextBlock>(root).Select(x => x is MiddleTruncatingText middle ? middle.FullText : x.Text ?? "").ToArray();
    private static Button Button(Control root, string label) => Nodes<Button>(root).First(x => Text(x).Contains(label) || AutomationProperties.GetName(x) == label);
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
    private static void Drain(Avalonia.Controls.Window window) { for (var i = 0; i < 5; i++) { window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); } }
    private static void Sync() => Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    private static void Seed(AppModel model, ProviderKind provider, ProviderStatus status) => typeof(ProviderRegistry).GetProperty("Statuses")!.SetValue(model.Providers, model.Providers.Statuses.Mutating(map => map[provider] = status));

    private void WithView(Func<Control> make, Action<Control, AppModel, AppModelHarness> check, Action<AppModel>? seed = null, Action<AppModelHarness>? beforeModel = null)
    {
        headless.OnUiThread(() =>
        {
            using var h = new AppModelHarness();
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            using var wallpaper = WallpaperStore.Shared.Override((WallpaperStore)Activator.CreateInstance(typeof(WallpaperStore), nonPublic: true)!);
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            using var updates = UpdateChecker.OverrideShared(new UpdateChecker(AppInfo.Current, [], ""));
            beforeModel?.Invoke(h);
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

    [Fact]
    [Trait("Req", "PR-GIT-150")] // App/Views/Settings/SettingsView.swift:836-842
    [Trait("Req", "UI-SETTINGS-069")] // App/Views/Settings/SettingsView.swift:838-840
    public void Worktree_reveal_calls_the_workspace_boundary_with_the_resolved_directory() => WithView(() => new SourceControlSettingsPage(), (view, _, _) =>
    {
        string? revealed = null;
        using var workspace = Marrow.UI.Common.Workspace.Platform.Override(new Marrow.UI.Common.Workspace.Backend
        {
            Reveal = path => revealed = path,
        });
        Click(Button(view, "Reveal")); Sync();
        Assert.Equal(Storage.WorktreesDirectory, revealed); // :839 forwards the resolved directory exactly.
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-051")] // App/Views/Settings/SettingsView.swift:749-754; Core/Models/Provider.swift:76
    public void Provider_login_calls_the_workspace_boundary_with_the_literal_command() => WithView(() => new ProviderSettingsSection(ProviderKind.Commandcode, Binding<bool>.Constant(true)), (view, _, _) =>
    {
        var launches = new List<(string Executable, string[] Arguments, string? Directory, bool Console)>();
        using var workspace = Marrow.UI.Common.Workspace.Platform.Override(new Marrow.UI.Common.Workspace.Backend
        {
            Home = @"C:\fixture home",
            Which = name => name switch { "pwsh.exe" => @"C:\fixture\pwsh.exe", "wt.exe" => @"C:\fixture\wt.exe", _ => throw new InvalidOperationException("Unexpected lookup") },
            Launch = (exe, args, cwd, console) => launches.Add((exe, args.ToArray(), cwd, console)),
        });
        Click(Button(Row(view, "Sign in"), "Login")); Sync();
        var launch = Assert.Single(launches);
        Assert.Equal(@"C:\fixture\wt.exe", launch.Executable);
        Assert.Equal(new[] { "-w", "new", "new-tab", "-d", @"C:\fixture home", @"C:\fixture\pwsh.exe", "-NoExit", "-Command", "cmd login" }, launch.Arguments); // ED-089, Provider.swift:76.
        Assert.Equal(@"C:\fixture home", launch.Directory);
        Assert.False(launch.Console);
    }, model => Seed(model, ProviderKind.Commandcode, new ProviderStatus { Executable = SwiftURL.FileURLWithPath("/fixture/command-code"), ProviderStatusAuth = new ProviderStatus.Auth.SignedOut() }));
    private static ChromeRow Row(Control view, string title) => Nodes<ChromeRow>(view).Single(row => Text(row).FirstOrDefault() == title);

    [Fact]
    [Trait("Req", "UI-UPDATE-026")] // App/Views/Settings/SettingsView.swift:1060-1101
    [Trait("Req", "UI-UPDATE-027")] // App/Views/Settings/SettingsView.swift:1081-1088
    public void Attribution_hover_fades_the_trailing_link_over_Chrome_hover_without_changing_row_geometry() => WithView(() => new MadeByDroppyRow(SwiftURL.Create("https://getdroppy.app")!), (view, _, h) =>
    {
        var button = Assert.Single(Nodes<Button>(view)); var grid = Assert.Single(Nodes<Grid>(view)); Assert.Equal(new Thickness(16, 12, 14, 12), grid.Margin); Assert.Equal(12, grid.ColumnSpacing); var trailing = Assert.Single(Nodes<StackPanel>(view), stack => stack.Orientation == Avalonia.Layout.Orientation.Horizontal); Assert.Equal(4, trailing.Spacing); Assert.Equal(0.9, trailing.Opacity); Assert.Equal(12, Nodes<TextBlock>(trailing).Single().FontSize); Assert.Equal(Avalonia.Media.FontWeight.Medium, Nodes<TextBlock>(trailing).Single().FontWeight); Assert.Equal(SwiftFont.System(10, SwiftFont.Weight.Semibold), SwiftImage.GetFont(Nodes<SwiftImage>(trailing).Single()));
        button.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, button, new Avalonia.Input.Pointer(78, PointerType.Mouse, true), button, default, 0, default, default)); Sync(); Assert.Equal(0.9, trailing.Opacity); AdvanceFrames(h, 50); Assert.InRange(trailing.Opacity, 0.901, 0.999); AdvanceFrames(h, 51); Assert.Equal(1, trailing.Opacity); Assert.Equal(new Thickness(16, 12, 14, 12), grid.Margin);
    });

    [Fact]
    [Trait("Req", "UI-UPDATE-025")] // App/Views/Settings/SettingsView.swift:946-964
    [Trait("Req", "UI-UPDATE-028")] // App/Views/Settings/SettingsView.swift:950-952,1104-1121
    [Trait("Req", "UI-UPDATE-030")] // App/Views/Settings/SettingsView.swift:958-960,1104-1121
    public void Credits_header_and_resolved_repository_links_keep_exact_fonts_spacing_and_targets() => WithView(() => new AboutSettingsPage(), (view, _, _) =>
    {
        var section = Assert.Single(Nodes<ChromeSection>(view)); var header = Nodes<TextBlock>(section).Single(text => text.Text == "Credits"); Assert.Equal(13, header.FontSize); Assert.Equal(Avalonia.Media.FontWeight.SemiBold, header.FontWeight); Assert.Equal(Chrome.SecondaryText.Resolve(header.ActualThemeVariant), Assert.IsType<Avalonia.Media.SolidColorBrush>(header.Foreground).Color);
        var opened = new List<string>(); using var browser = Marrow.Services.Port.Workspace.Open.Override(url => { opened.Add(url.AbsoluteString); return true; });
        foreach (var (title, detail, url) in new[] { ("Working indicators", "Ported from Zeron by Wing, MIT License.", "https://github.com/zeronsh/zeron"), ("Welcome tour", "Ported from TourKit by Ram Patra, MIT License.", "https://github.com/rampatra/TourKit") }) { var row = Row(section, title); Assert.Contains(detail, Text(row)); var link = Assert.Single(Nodes<CreditLink>(row)); var button = Assert.Single(Nodes<Button>(link)); Assert.Equal(url, ToolTip.GetTip(button)); Assert.Equal(12, Nodes<TextBlock>(link).Single().FontSize); Assert.Equal(Avalonia.Media.FontWeight.Medium, Nodes<TextBlock>(link).Single().FontWeight); Assert.Equal(4, Nodes<StackPanel>(link).Single().Spacing); Assert.Equal(SwiftFont.System(10, SwiftFont.Weight.Semibold), SwiftImage.GetFont(Nodes<SwiftImage>(link).Single())); Click(button); }
        Assert.Equal(new[] { "https://github.com/zeronsh/zeron", "https://github.com/rampatra/TourKit" }, opened);
    });

    [Fact]
    [Trait("Req", "UI-UPDATE-031")] // App/Views/Settings/SettingsView.swift:1018-1023
    [Trait("Req", "UI-UPDATE-032")] // App/Views/Settings/SettingsView.swift:1012-1056
    public void Legal_popover_inherits_presented_chrome_and_selectable_markdown_with_all_missing_resource_names() => WithView(() => new LicensesRow(), (view, _, _) =>
    {
        var presenter = new RecordingPresenter(); using var presentation = Presentation.Presenter.Override(presenter); var show = Button(view, "Show"); Assert.True(show.IsEnabled); Click(show); Sync(); var popup = presenter.Calls.Last(x => x.Presented).Popup; Assert.Equal(Avalonia.Controls.PlacementMode.Bottom, popup.Placement); var content = Opened(popup); Assert.True(Popovers.GetIsCapsuleButtonBorderShape(content)); foreach (var body in Nodes<MarkdownView>(content)) Assert.True(MarkdownView.GetMarkdownBlockSelection(body)); var reader = typeof(LicensesRow).GetMethod("Text", BindingFlags.Static | BindingFlags.NonPublic)!; foreach (var resource in new[] { "LICENSE", "THIRD_PARTY_NOTICES", "TRADEMARK" }) Assert.Equal($"The {resource} file is missing from this copy of the app.", reader.Invoke(null, [resource]));
    });

    [Fact]
    [Trait("Req", "UI-THEME-002")] // App/Views/Settings/SettingsView.swift:1159-1202
    public void Theme_picker_holds_its_intrinsic_size_and_hover_animates_label_not_chevron() => WithView(() => new ThemePickerButton(Binding<AppTheme>.Constant(AppTheme.System)), (view, _, h) =>
    {
        Assert.Equal(28, view.Bounds.Height); Assert.True(view.Bounds.Width < 200); var label = Nodes<TextBlock>(view).Single(); var chevron = Assert.Single(Nodes<SwiftImage>(view)); var before = Assert.IsType<Avalonia.Media.SolidColorBrush>(label.Foreground).Color; var arrow = chevron.Foreground; var button = Assert.Single(Nodes<Button>(view)); button.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, button, new Avalonia.Input.Pointer(77, PointerType.Mouse, true), button, default, 0, default, default)); Sync();
        AdvanceFrames(h, 50); var halfway = Assert.IsType<Avalonia.Media.SolidColorBrush>(label.Foreground).Color; Assert.True(halfway.A > before.A); AdvanceFrames(h, 51); Assert.Equal(Chrome.PrimaryText.Resolve(label.ActualThemeVariant), Assert.IsType<Avalonia.Media.SolidColorBrush>(label.Foreground).Color); Assert.Same(arrow, chevron.Foreground);
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-075")] // App/Views/Settings/SettingsView.swift:27,44,148-157
    [Trait("Req", "UI-SETTINGS-076")] // App/Views/Settings/SettingsView.swift:28,45,148-157
    [Trait("Req", "UI-SETTINGS-077")] // App/Views/Settings/SettingsView.swift:29,46,148-157
    [Trait("Req", "UI-SETTINGS-078")] // App/Views/Settings/SettingsView.swift:22,37,148-157
    public void Remaining_sidebar_symbols_keep_source_sizes_order_and_selection() => WithSettings((view, _, _) =>
    {
        var rows = Nodes<SidebarRow>(view); Assert.Equal("Appearance", Text(rows[2]).Single()); foreach (var (title, symbol) in new[] { ("Source control", "arrow.triangle.branch"), ("Shortcuts", "command"), ("Archive", "archivebox"), ("Appearance", "paintpalette") }) { var row = rows.Single(row => Text(row).Contains(title)); var image = Assert.Single(Nodes<SwiftImage>(row)); Assert.Equal(symbol, image.SymbolName); Assert.Equal(14, image.Width); Assert.Equal(14, image.Height); Assert.Empty(Nodes<UpdateAvailableDot>(row)); }
        Click(Button(rows.Single(row => Text(row).Contains("Source control")), "Source control")); Sync(); Assert.Single(Nodes<SourceControlSettingsPage>(view));
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-015")] // App/Views/Settings/SettingsView.swift:344-348
    [Trait("Req", "UI-SETTINGS-065")] // App/Views/Settings/SettingsView.swift:395-399,432-442
    public void Owned_visual_pickers_keep_option_order_symbols_and_uniform_stage_and_caption_metrics()
    {
        WithView(() => new GeneralSettingsPage(), (view, _, _) => { Check(Row(view, "Working line"), ["Card", "Line"], ["rectangle.fill", "minus"]); Check(Row(view, "Done with a thread"), ["Settle", "Archive"], ["checkmark", "archivebox"]); });
        WithView(() => new AppearanceSettingsPage(), (view, _, _) => { Check(Row(view, "Sidebar"), ["Column", "Floating", "Panel only"], ["sidebar.left", "macwindow.on.rectangle", "macwindow"]); Check(Row(view, "Activity list"), ["Icon", "Project name"], ["list.bullet", "text.alignleft"]); });
        static void Check(Control row, string[] titles, string[] symbols)
        {
            var tiles = Nodes<ChromeVisualTile>(row); Assert.Equal(titles, tiles.Select(AutomationProperties.GetName)); Assert.Equal(symbols, tiles.Select(tile => Assert.Single(Nodes<SwiftImage>(tile)).SymbolName));
            foreach (var tile in tiles) { var stage = Field<Border>(tile, "stageBack"); Assert.Equal(76, stage.Width); Assert.Equal(48, stage.Height); Assert.Equal(new CornerRadius(6), stage.CornerRadius); var caption = Field<TextBlock>(tile, "captionBlock"); Assert.Equal(11, caption.FontSize); Assert.Equal(2, caption.MaxLines); Assert.Equal(6, Assert.Single(Nodes<StackPanel>(tile)).Spacing); }
        }
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-061")] // App/Views/Settings/SettingsView.swift:148-157
    [Trait("Req", "UI-SETTINGS-062")] // App/Views/Settings/SettingsView.swift:151-155
    [Trait("Req", "UI-SETTINGS-063")] // App/Views/Settings/SettingsView.swift:148-157
    [Trait("Req", "UI-SETTINGS-064")] // App/Views/Settings/SettingsView.swift:159-163
    [Trait("Req", "UI-SETTINGS-073")] // App/Views/Settings/SettingsView.swift:148-157
    [Trait("Req", "UI-SETTINGS-074")] // App/Views/Settings/SettingsView.swift:148-157
    public void Sidebar_icons_use_source_sizes_and_labels_without_using_page_tints() => WithSettings((view, _, _) =>
    {
        var rows = Nodes<SidebarRow>(view); foreach (var row in rows) { var title = Nodes<TextBlock>(row).Single(); Assert.Equal(13, title.FontSize); Assert.Equal(title.Text == "Archive" ? Avalonia.Media.FontWeight.Medium : Avalonia.Media.FontWeight.Normal, title.FontWeight); var badge = Assert.Single(Nodes<SidebarIconBadge>(row)); Assert.Equal(20, Assert.IsType<Panel>(badge.Child).Width); }
        foreach (var (title, symbol, side) in new[] { ("General", "gear", 16.1), ("Providers", "cpu", 14.0), ("MCP", "puzzlepiece.extension", 14.0), ("Models", "slider.horizontal.3", 14.0), ("About", "info.circle", 14.0) }) { var image = Assert.Single(Nodes<SwiftImage>(rows.Single(row => Text(row).Contains(title)))); Assert.Equal(symbol, image.SymbolName); Assert.Equal(side, image.Width, 4); Assert.Equal(side, image.Height, 4); }
        var hydra = Assert.Single(Nodes<HydraMarkImage>(rows.Single(row => Text(row).Contains("Hydra")))); Assert.Equal(16.1, hydra.Width, 4); Assert.Equal(16.1, hydra.Height, 4);
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-051")] // App/Views/Settings/SettingsView.swift:719-757
    [Trait("Req", "UI-SETTINGS-069")] // App/Views/Settings/SettingsView.swift:838-840
    public void Sign_in_and_reveal_keep_the_source_glass_capsule_wrappers_without_invoking_blocked_actions()
    {
        WithView(() => new SourceControlSettingsPage(), (view, _, _) => Assert.Single(Nodes<Border>(Row(view, "Worktrees")), border => !border.IsHitTestVisible));
        WithView(() => new ProviderSettingsSection(ProviderKind.Commandcode, Binding<bool>.Constant(true)), (view, _, _) => { var row = Row(view, "Sign in"); Assert.Equal(16, Nodes<TextBlock>(row).Single(text => text.Text == "Login").Height); Assert.Single(Nodes<Border>(row), border => !border.IsHitTestVisible); }, model => Seed(model, ProviderKind.Commandcode, new ProviderStatus { Executable = SwiftURL.FileURLWithPath("/fixture/command-code"), ProviderStatusAuth = new ProviderStatus.Auth.SignedOut() }));
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-052")] // App/Views/Settings/SettingsView.swift:818-835
    public void Commit_editor_binds_multiline_wrapping_caret_and_selection_into_its_actual_presenter() => WithView(() => new SourceControlSettingsPage(), (view, _, _) =>
    {
        var editor = Assert.Single(Nodes<TextBox>(view)); Assert.True(editor.AcceptsReturn); Assert.Equal(Avalonia.Media.TextWrapping.Wrap, editor.TextWrapping); editor.Text = "first\nsecond"; editor.CaretIndex = 3; editor.SelectionStart = 1; editor.SelectionEnd = 3; Sync(); editor.ApplyTemplate(); var presenter = Assert.Single(editor.GetVisualDescendants().OfType<TextPresenter>()); Assert.Equal("first\nsecond", presenter.Text); Assert.Equal(Avalonia.Media.TextWrapping.Wrap, presenter.TextWrapping); Assert.Equal(editor.CaretIndex, presenter.CaretIndex); Assert.Equal(1, presenter.SelectionStart); Assert.Equal(3, presenter.SelectionEnd);
    });

    private static Control Opened(Avalonia.Controls.Primitives.Popup popup)
    {
        ((EventHandler?)typeof(Avalonia.Controls.Primitives.Popup).GetField("Opened", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(popup))?.Invoke(popup, EventArgs.Empty);
        return popup.Child!;
    }

    private static void AdvanceFrames(AppModelHarness h, double milliseconds)
    {
        h.Main.AdvanceMilliseconds(milliseconds); ((ManualFrameSource)FrameClock.Source.Current).Advance(TimeSpan.FromMilliseconds(milliseconds)); Sync();
    }

    // STOP App.Views.Settings.SettingsView #6
    // Kind: dependency / adaptation. Swift: SettingsView.swift:705,735. Records: UI-PROVAPI-005, UI-PROVREG-038, UI-PROVREG-041.
    // Missing: the shared plain-field primitive does not implement the native roundedBorder style.
    // Needed: a Windows rounded-border field form with its platform styling defined. Masking is implemented
    // locally; guessed border radii, padding and colours are not substituted for an OS-defined source style.
    // STOP App.Views.Settings.SettingsView #7
    // Kind: dependency. Swift: SettingsView.swift:647; ModelsSettingsPage.swift:436. Records: UI-PROVREG-045 (motion only).
    // Missing: CollapsibleProviderHeader assigns a RotateTransform immediately, not an animated rotation.
    // Needed: the ModelsSettingsPage unit to animate its shared header's chevron. Do not change another unit.

    [Fact]
    [Trait("Req", "UI-PROVREG-009")] // App/Views/Settings/SettingsView.swift:677,690,720,731,739,748
    [Trait("Req", "UI-PROVREG-037")] // App/Views/Settings/SettingsView.swift:689-696
    [Trait("Req", "UI-PROVREG-038")] // App/Views/Settings/SettingsView.swift:697-707
    [Trait("Req", "UI-PROVREG-039")] // App/Views/Settings/SettingsView.swift:708-717
    [Trait("Req", "UI-SETTINGS-050")] // App/Views/Settings/SettingsView.swift:689-718,784-795
    [Trait("Req", "UI-PROVREG-040")] // App/Views/Settings/SettingsView.swift:719-728
    [Trait("Req", "UI-PROVREG-041")] // App/Views/Settings/SettingsView.swift:730-737,780-783
    [Trait("Req", "UI-PROVREG-042")] // App/Views/Settings/SettingsView.swift:738-746
    public void All_provider_cards_apply_the_exact_key_binary_and_install_row_gates_and_divider_insets()
    {
        foreach (var provider in SwiftEnum<ProviderKind>.AllCases) WithView(() => new ProviderSettingsSection(provider, Binding<bool>.Constant(true)), (view, _, _) =>
        {
            var titles = Nodes<ChromeRow>(view).Select(row => Text(row).First()).ToArray();
            Assert.Equal(provider.AcceptsAPIKey, titles.Contains(provider.IsAPIKeyBased ? "API key" : "API key (optional)")); Assert.Equal(!provider.IsAPIKeyBased, titles.Contains("Binary path")); Assert.Equal(provider.IsAPIKeyBased, titles.Contains("Get a key")); Assert.Equal(!provider.IsAPIKeyBased, titles.Contains("Install"));
            Assert.Equal(titles.Length, Nodes<ChromeRowDivider>(view).Length);
            foreach (var divider in Nodes<ChromeRowDivider>(view)) Assert.Equal(new Thickness(16, 0, 0, 0), divider.Margin);
            foreach (var field in Nodes<TextBox>(view)) { Assert.Equal(200, field.Width); Assert.Equal(12, field.FontSize); Assert.Equal(SwiftFont.System(12, design: SwiftFont.Design.Monospaced).Resolve().FontFamily, field.FontFamily); }
            if (!provider.IsAPIKeyBased) { Assert.Equal(provider.ExecutableName, Nodes<TextBox>(Row(view, "Binary path")).Single().PlaceholderText); Assert.Contains(provider.DisplayName + " was not found on your PATH.", Text(Row(view, "Install"))); }
        });
    }

    [Fact]
    [Trait("Req", "UI-PROVREG-010")] // App/Views/Settings/SettingsView.swift:629-634,676-688,772-779
    public void Unknown_auth_still_shows_usage_for_an_installed_provider_but_signed_out_hides_it() => WithView(() => new ProviderSettingsSection(ProviderKind.Zai, Binding<bool>.Constant(true)), (view, model, _) =>
    {
        Seed(model, ProviderKind.Zai, new ProviderStatus { ApiKeyConfigured = true, ProviderStatusAuth = new ProviderStatus.Auth.Unknown() }); Sync(); Assert.Single(Nodes<Marrow.App.Views.Usage.PlanLimitsView>(view)); Assert.Empty(Nodes<Marrow.App.Views.Usage.CreditsView>(view));
        Seed(model, ProviderKind.Zai, model.Providers.Status(ProviderKind.Zai) with { ProviderStatusAuth = new ProviderStatus.Auth.SignedOut() }); Sync(); Assert.Empty(Nodes<Marrow.App.Views.Usage.PlanLimitsView>(view));
    });

    [Fact]
    [Trait("Req", "UI-PROVREG-043")] // App/Views/Settings/SettingsView.swift:636-642,747-757
    [Trait("Req", "UI-PROVREG-044")] // App/Views/Settings/SettingsView.swift:750-755
    [Trait("Req", "UI-PROVREG-045")] // App/Views/Settings/SettingsView.swift:647; App/Views/Settings/ModelsSettingsPage.swift:423-446
    public void CLI_sign_in_is_hidden_during_check_and_disclosure_keeps_accessibility_labels_and_final_rotation() => WithView(() => new ProviderSettingsSection(ProviderKind.Commandcode, Binding<bool>.Constant(true)), (view, model, _) =>
    {
        var header = Assert.Single(Nodes<CollapsibleProviderHeader>(view)); var disclosure = Assert.Single(Nodes<Button>(header)); Assert.Equal(22, disclosure.Height); Assert.Equal("Hide the provider settings", ToolTip.GetTip(disclosure)); Assert.Equal("Expanded", AutomationProperties.GetItemStatus(disclosure)); Assert.Equal("Shows or hides the provider settings", AutomationProperties.GetHelpText(disclosure)); Assert.Equal("Command Code", AutomationProperties.GetName(disclosure));
        Seed(model, ProviderKind.Commandcode, new ProviderStatus { Executable = SwiftURL.FileURLWithPath("/fixture/command-code"), ProviderStatusAuth = new ProviderStatus.Auth.SignedOut(), IsChecking = true }); Sync(); Assert.DoesNotContain("Sign in", Text(view)); Assert.DoesNotContain("Install", Text(view));
        Seed(model, ProviderKind.Commandcode, model.Providers.Status(ProviderKind.Commandcode) with { IsChecking = false }); Sync(); var login = Row(view, "Sign in"); Assert.Contains("Click here to login again.", Text(login)); Assert.Equal(16, Nodes<TextBlock>(Button(login, "Login")).Single().Height); Assert.True(Button(login, "Login").IsEffectivelyEnabled);
        // STOP #2: do not invoke the unported terminal launcher.
        Seed(model, ProviderKind.Commandcode, model.Providers.Status(ProviderKind.Commandcode) with { ProviderStatusAuth = new ProviderStatus.Auth.SignedIn(null) }); Sync(); Assert.DoesNotContain("Sign in", Text(view));
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-002")] // App/Views/Settings/SettingsView.swift:138-140
    [Trait("Req", "UI-SETTINGS-003")] // App/Views/Settings/SettingsView.swift:141-145
    [Trait("Req", "UI-SETTINGS-004")] // App/Views/Settings/SettingsView.swift:148-164,179-182
    [Trait("Req", "UI-SETTINGS-006")] // App/Views/Settings/SettingsView.swift:166-177
    [Trait("Req", "UI-SETTINGS-007")] // App/Views/Settings/SettingsView.swift:198-204
    public void Sidebar_search_and_no_results_keep_source_insets_fonts_and_empty_archive_replaces_all_chrome() => WithSettings((view, _, _) =>
    {
        var search = Assert.Single(Nodes<SidebarSearchField>(view)); Assert.Equal(new Thickness(10, 14, 10, 0), search.Margin); var field = Assert.Single(Nodes<TextBox>(search)); Assert.Equal("Search", field.PlaceholderText); Assert.Equal(13, field.FontSize);
        var rows = Field<StackPanel>(view, "rows"); Assert.Equal(new Thickness(10, 12, 10, 8), rows.Margin); Assert.Equal(1, rows.Spacing); Assert.Equal(10, Nodes<SidebarRow>(view).Length);
        var empty = Field<Control>(view, "detail"); Assert.Empty(Nodes<ScrollViewer>(empty)); Assert.Empty(Nodes<ChromeCompactTitle>(empty)); Assert.Empty(Nodes<ArchiveDeleteAllButton>(empty)); Assert.Equal(SwiftFont.System(44, SwiftFont.Weight.Light), SwiftImage.GetFont(Nodes<SwiftImage>(empty).Single()));
        field.Text = "no-such-settings-page"; Sync(); Assert.Empty(Nodes<SidebarRow>(view)); var heading = Nodes<TextBlock>(rows).Single(text => text.Text == "No results"); Assert.Equal(13, heading.FontSize); Assert.Equal(Avalonia.Media.FontWeight.SemiBold, heading.FontWeight); var note = Nodes<TextBlock>(rows).Single(text => text.Text == "Try a setting or a provider name."); Assert.Equal(11, note.FontSize);
        var block = Assert.Single(Nodes<StackPanel>(rows), panel => panel.Spacing == 6); Assert.Equal(new Thickness(8, 14, 8, 0), block.Margin);
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-005")] // App/Views/Settings/SettingsView.swift:159-163; App/Views/Settings/AboutUpdateViews.swift:527-535
    [Trait("Req", "UI-UPDATE-011")] // App/Views/Settings/SettingsView.swift:159-163
    public void About_update_dot_is_conditionally_attached_to_the_icon_with_the_source_circle_and_offset() => WithSettings((view, _, _) =>
    {
        Assert.Empty(Nodes<UpdateAvailableDot>(view)); var checker = UpdateChecker.Shared; typeof(UpdateChecker).GetProperty("Update")!.SetValue(checker, new AvailableUpdate { Version = "999.0.0", Tag = "v999.0.0", DownloadURL = SwiftURL.Create("https://fixture.invalid/download")!, PageURL = SwiftURL.Create("https://fixture.invalid/release")!, Notes = "" }); Sync();
        var dot = Assert.Single(Nodes<UpdateAvailableDot>(view)); var circle = Assert.IsType<Avalonia.Controls.Shapes.Ellipse>(dot.Child); Assert.Equal(7, circle.Width); Assert.Equal(7, circle.Height); var offset = Assert.IsType<Avalonia.Media.TranslateTransform>(circle.RenderTransform); Assert.Equal(2.5, offset.X); Assert.Equal(-2.5, offset.Y);
        typeof(UpdateChecker).GetProperty("Update")!.SetValue(checker, null); Sync(); Assert.Empty(Nodes<UpdateAvailableDot>(view));
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-008")] // App/Views/Settings/SettingsView.swift:246-267
    [Trait("Req", "UI-SETTINGS-009")] // App/Views/Settings/SettingsView.swift:248-250
    public void Sticky_title_fills_width_left_by_search_and_fades_with_geometry_without_taking_hits() => WithSettings((view, _, _) =>
    {
        SettingsNavigation.Shared.RequestedPage = SettingsPage.Models; Sync(); var chrome = Nodes<StackPanel>(view).Single(stack => stack.MinHeight == 32); var title = Assert.Single(Nodes<ChromeCompactTitle>(view)); var search = Assert.Single(Nodes<ChromeSearchField>(view));
        var window = Assert.IsType<Avalonia.Controls.Window>(TopLevel.GetTopLevel(view)); Drain(window); Assert.Equal(chrome.Bounds.Width - search.DesiredSize.Width - 10, title.Bounds.Width, 1); Assert.False(title.IsHitTestVisible);
        Assert.Equal(200, search.Bounds.Width); var input = Nodes<TextBox>(search).Single(); Assert.Equal("Search models", input.PlaceholderText); Assert.Equal(12.5, input.FontSize);
        Field<ChromeScrollModel>(view, "scrollChrome").Update(44); Sync(); Assert.Equal(1, title.Opacity); Assert.Equal(AccessibilityView.Content, AutomationProperties.GetAccessibilityView(title));
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-011")] // App/Views/Settings/SettingsView.swift:566-580
    [Trait("Req", "UI-SETTINGS-012")] // App/Views/Settings/SettingsView.swift:296-305
    public void Refresh_symbol_uses_chrome_font_and_small_switch_exposes_keyboard_and_toggle_automation()
    {
        WithView(() => new ProvidersRefreshButton(), (view, _, _) => { var symbol = Nodes<SwiftImage>(view).Single(image => image.SymbolName == "arrow.clockwise"); Assert.Equal(SwiftFont.System(SwiftFont.TextStyle.Callout, weight: SwiftFont.Weight.Semibold), SwiftImage.GetFont(symbol)); Assert.Empty(Nodes<TextBlock>(view)); });
        var value = false; WithView(() => new SettingsSwitch(new Binding<bool>(() => value, on => value = on)), (view, _, _) => { var toggle = Assert.Single(Nodes<SmallSwitch>(view)); var peer = Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(toggle); var provider = Assert.IsAssignableFrom<Avalonia.Automation.Provider.IToggleProvider>(peer); Assert.Equal(ToggleState.Off, provider.ToggleState); provider.Toggle(); Assert.True(value); Assert.Equal(ToggleState.On, provider.ToggleState); toggle.IsEnabled = false; provider.Toggle(); Assert.True(value); });
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-013")] // App/Views/Settings/SettingsView.swift:323-325
    [Trait("Req", "UI-SETTINGS-014")] // App/Views/Settings/SettingsView.swift:327-331
    public void Permission_menu_and_workspace_tiles_keep_the_source_options_symbols_and_dimensions() => WithView(() => new GeneralSettingsPage(), (view, model, _) =>
    {
        var presenter = new RecordingPresenter(); using var presentation = Presentation.Presenter.Override(presenter); var permissions = Row(view, "Permissions"); Click(Assert.Single(Nodes<Button>(permissions))); Sync(); var menu = Opened(presenter.Calls.Last(x => x.Presented).Popup); Assert.Equal(new[] { "Supervised", "Auto-accept edits", "Auto", "Full access" }, Nodes<PopoverItem>(menu).Select(item => Text(item).First())); Click(Button(menu, "Full access")); Sync(); Assert.Equal(RuntimeMode.FullAccess, model.Settings.DefaultRuntimeMode); Assert.Contains("Runs commands and edits without asking", Text(Row(view, "Permissions")));
        var workspace = Row(view, "Workspace"); Assert.Equal(new[] { "Local", "New worktree" }, Nodes<ChromeVisualTile>(workspace).Select(tile => Text(tile).Single())); foreach (var tile in Nodes<ChromeVisualTile>(workspace)) { var stage = Nodes<Border>(tile).Single(border => border.Width == 76 && border.Height == 48); Assert.Equal(76, stage.Width); Assert.Equal(48, stage.Height); Assert.Equal(11, Nodes<TextBlock>(tile).Single().FontSize); }
        Assert.Equal(new[] { "folder", "arrow.triangle.branch" }, Nodes<SwiftImage>(workspace).Select(image => image.SymbolName));
    });

    [Theory]
    [InlineData(ProviderKind.Deepseek, "platform.deepseek.com", "api.deepseek.com", "https://platform.deepseek.com/api_keys")]
    [InlineData(ProviderKind.Meta, "dev.meta.ai", "api.meta.ai/v1", "https://dev.meta.ai/")]
    [InlineData(ProviderKind.Zai, "z.ai/manage-apikey/apikey-list", "api.z.ai/api/coding/paas/v4", "https://z.ai/manage-apikey/apikey-list")]
    [Trait("Req", "UI-PROVAPI-001")] // App/Views/Settings/SettingsView.swift:661-665
    [Trait("Req", "UI-PROVAPI-002")] // App/Views/Settings/SettingsView.swift:691-696
    [Trait("Req", "UI-PROVAPI-003")] // App/Views/Settings/SettingsView.swift:691-696
    [Trait("Req", "UI-PROVAPI-004")] // App/Views/Settings/SettingsView.swift:691-696
    [Trait("Req", "UI-PROVAPI-007")] // App/Views/Settings/SettingsView.swift:719-728
    [Trait("Req", "UI-PROVAPI-008")] // App/Views/Settings/SettingsView.swift:719-728
    [Trait("Req", "UI-PROVAPI-009")] // App/Views/Settings/SettingsView.swift:719-728
    [Trait("Req", "UI-PROVREG-007")] // App/Views/Settings/SettingsView.swift:661-665
    public void API_provider_rows_use_exact_host_key_source_and_install_URL_without_CLI_rows(ProviderKind provider, string source, string host, string url) => WithView(() => new ProviderSettingsSection(provider, Binding<bool>.Constant(true)), (view, model, _) =>
    {
        var key = Row(view, "API key"); Assert.Contains($"Stored in your Credential Manager. Get one at {source}.", Text(key)); var texts = Nodes<TextBlock>(key); Assert.Equal(13, texts[0].FontSize); Assert.Equal(11, texts[1].FontSize);
        Assert.Equal(new Thickness(16, 11, 10, 11), Assert.IsType<Border>(key.Child).Padding); Assert.DoesNotContain(Text(view), text => text is "Binary path" or "Install" or "Sign in");
        var get = Row(view, "Get a key"); Assert.Contains(provider.DisplayName + " needs an API key. Nothing to install.", Text(get)); var link = Button(get, "Get a " + provider.DisplayName + " key"); Assert.Equal(16, Nodes<TextBlock>(link).Single().Height);
        SwiftURL? opened = null; using var browser = Marrow.Services.Port.Workspace.Open.Override(value => { opened = value; return true; }); Click(link); Assert.Equal(url, opened!.AbsoluteString);
        Seed(model, provider, model.Providers.Status(provider) with { ApiKeyConfigured = true, Version = "API", ProviderStatusAuth = new ProviderStatus.Auth.SignedOut() }); Sync(); Assert.DoesNotContain("Get a key", Text(view)); Assert.Contains($"Native API \u00b7 {host}", Text(view)); Assert.DoesNotContain("Version API", Text(view));
        var native = Nodes<TextBlock>(view).Single(x => x.Text == $"Native API \u00b7 {host}"); Assert.Equal(11, native.FontSize); Assert.Equal(Chrome.SecondaryText.Resolve(native.ActualThemeVariant), Assert.IsType<Avalonia.Media.SolidColorBrush>(native.Foreground).Color);
    });

    [Fact]
    [Trait("Req", "UI-PROVAPI-005")] // App/Views/Settings/SettingsView.swift:697-707,768-799; OD-0012 E5
    [Trait("Req", "UI-PROVAPI-006")] // App/Views/Settings/SettingsView.swift:708-716
    public void Secure_key_presenter_masks_the_actual_rendered_text_and_refused_keys_show_the_owner_message_under_the_field() => WithView(() => new ProviderSettingsSection(ProviderKind.Meta, Binding<bool>.Constant(true)), (view, model, _) =>
    {
        var backend = new AppSettingsCredentialBackend(() => true) { RefuseWrites = true }; using var credentials = SecretStore.Shared.Override(new SecretStore(backend));
        var field = Assert.Single(Nodes<TextBox>(view)); Assert.Equal("sk-\u2026", field.PlaceholderText); field.Text = "fixture-key-never-sent"; Sync();
        var presenter = Assert.Single(field.GetVisualDescendants().OfType<TextPresenter>()); Assert.Equal('\u2022', presenter.PasswordChar);
        const string refusal = "Credential Manager did not accept this key, so it was not saved."; Assert.Equal(refusal, model.Settings.APIKeyRefusal(ProviderKind.Meta)); Assert.Contains(refusal, Text(view));
        var block = Assert.Single(Nodes<StackPanel>(view), stack => stack.Children.Count == 2 && stack.Children[0] is StackPanel && stack.Children[1] is TextBlock note && note.Text == refusal); Assert.Same(field, Nodes<TextBox>(block).Single());
        var eye = Nodes<Button>(view).Single(button => Equals(ToolTip.GetTip(button), "Show the API key")); Click(eye); Sync(); Assert.Equal('\0', presenter.PasswordChar); Assert.Equal("Hide the API key", ToolTip.GetTip(eye)); Assert.Equal(SwiftFont.System(12), SwiftImage.GetFont(Nodes<SwiftImage>(eye).Single()));
        field.Text = ""; Sync();
    });

    [Fact]
    [Trait("Req", "UI-PROVAPI-010")] // App/Views/Settings/SettingsView.swift:667-672
    [Trait("Req", "UI-PROVREG-008")] // App/Views/Settings/SettingsView.swift:667-672
    [Trait("Req", "UI-SETTINGS-066")] // App/Views/Settings/SettingsView.swift:668-671
    public void Provider_enable_switch_is_keyboard_accessible_even_during_a_check_and_updates_disabled_providers() => WithView(() => new ProviderSettingsSection(ProviderKind.Deepseek, Binding<bool>.Constant(true)), (view, model, _) =>
    {
        Seed(model, ProviderKind.Deepseek, model.Providers.Status(ProviderKind.Deepseek) with { IsChecking = true }); Sync(); var toggle = Assert.Single(Nodes<SmallSwitch>(view)); Assert.True(toggle.Focusable); Assert.True(toggle.IsEffectivelyEnabled);
        toggle.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space, Source = toggle }); Sync(); Assert.False(model.Settings.IsEnabled(ProviderKind.Deepseek));
        toggle.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space, Source = toggle }); Sync(); Assert.True(model.Settings.IsEnabled(ProviderKind.Deepseek));
    });

    [Theory]
    [InlineData(ProviderKind.Zai, 1, 0)]
    [InlineData(ProviderKind.Deepseek, 0, 1)]
    [InlineData(ProviderKind.Meta, 0, 0)]
    [Trait("Req", "PR-USAGE-077")] // App/Views/Settings/SettingsView.swift:679-684,772-779
    [Trait("Req", "UI-PROVAPI-029")] // App/Views/Settings/SettingsView.swift:676-688
    [Trait("Req", "UI-PROVAPI-030")] // App/Views/Settings/SettingsView.swift:676-688
    public void Provider_usage_embeds_the_shared_views_with_14_spacing_and_16_by_12_insets(ProviderKind provider, int limits, int credits) => WithView(() => new ProviderSettingsSection(provider, Binding<bool>.Constant(false)), (view, model, _) =>
    {
        Assert.Equal(limits, Nodes<Marrow.App.Views.Usage.PlanLimitsView>(view).Length); Assert.Equal(credits, Nodes<Marrow.App.Views.Usage.CreditsView>(view).Length);
        if (limits + credits > 0) { var usage = Assert.Single(Nodes<StackPanel>(view), stack => stack.Spacing == 14); Assert.Equal(new Thickness(16, 12), usage.Margin); }
        var reveal = Assert.Single(Nodes<ChromeRevealLayout>(view)); Assert.Equal(0, reveal.Fraction); Assert.False(reveal.IsEnabled); Assert.False(reveal.IsHitTestVisible); Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(reveal));
        Seed(model, provider, model.Providers.Status(provider) with { ProviderStatusAuth = new ProviderStatus.Auth.SignedOut() }); Sync(); Assert.Empty(Nodes<Marrow.App.Views.Usage.PlanLimitsView>(view)); Assert.Empty(Nodes<Marrow.App.Views.Usage.CreditsView>(view));
    }, model => Seed(model, provider, new ProviderStatus { ApiKeyConfigured = true, ProviderStatusAuth = new ProviderStatus.Auth.SignedIn(null) }));

    [Fact]
    [Trait("Req", "UI-PROVREG-001")] // App/Views/Settings/SettingsView.swift:9,23,38,55,72
    [Trait("Req", "UI-PROVREG-002")] // App/Views/Settings/SettingsView.swift:254-255,566-579
    [Trait("Req", "UI-PROVREG-003")] // App/Views/Settings/SettingsView.swift:585-615,646-766
    [Trait("Req", "UI-PROVREG-004")] // App/Views/Settings/SettingsView.swift:651-653
    [Trait("Req", "UI-PROVREG-005")] // App/Views/Settings/SettingsView.swift:650-656
    [Trait("Req", "UI-PROVREG-006")] // App/Views/Settings/SettingsView.swift:657-660
    [Trait("Req", "UI-SETTINGS-049")] // App/Views/Settings/SettingsView.swift:648-675
    public void Providers_keep_all_sections_and_status_header_metrics_and_animate_reveal_without_unmounting() => WithView(() => new ProvidersSettingsPage(), (view, model, h) =>
    {
        Assert.Equal("Providers", SettingsPage.Providers.Title); Assert.Equal("cpu", SettingsPage.Providers.Symbol); Assert.Equal(Chrome.Blue, SettingsPage.Providers.Tint); Assert.Equal(39, SettingsPage.Providers.Keywords.Count);
        var section = Nodes<ProviderSettingsSection>(view).Single(x => Field<ProviderKind>(x, "provider") == ProviderKind.Codex); var reveal = Assert.Single(Nodes<ChromeRevealLayout>(section)); var card = Assert.Single(Nodes<ChromeCard>(section));
        var header = Assert.Single(Nodes<CollapsibleProviderHeader>(section)); Assert.Equal(0, reveal.Fraction); Click(Assert.Single(Nodes<Button>(header))); Sync(); Assert.InRange(reveal.Fraction, 0, 0.001); AdvanceFrames(h, 160); Assert.InRange(reveal.Fraction, 0.001, 0.999); AdvanceFrames(h, 1000); Assert.Equal(1, reveal.Fraction); Assert.Same(card, Assert.Single(Nodes<ChromeCard>(section)));
        var statusRow = Nodes<Grid>(section).Single(grid => grid.ColumnDefinitions.Count == 3); Assert.Equal(new Thickness(16, 11, 10, 11), statusRow.Margin); Assert.Equal(12, statusRow.ColumnSpacing); Assert.Equal(22, statusRow.ColumnDefinitions[0].Width.Value);
        var icon = Nodes<ProviderIcon>(section).Single(x => x.Size == 18); Assert.Equal(18, icon.Size);
        Seed(model, ProviderKind.Codex, new ProviderStatus { Version = "1.2.3", IsChecking = true }); Sync(); Assert.Contains("Checking\u2026", Text(section)); Assert.Contains("Version 1.2.3", Text(section));
        var summary = Nodes<TextBlock>(statusRow).Single(x => x.Text == "Checking\u2026"); Assert.Equal(13, summary.FontSize); var version = Nodes<TextBlock>(statusRow).Single(x => x.Text == "Version 1.2.3"); Assert.Equal(11, version.FontSize);
    });

    [Fact]
    [Trait("Req", "UI-GIT-058")] // App/Views/Settings/SettingsView.swift:818-834
    [Trait("Req", "UI-GIT-059")] // App/Views/Settings/SettingsView.swift:836-847
    public void Source_control_editor_and_worktree_rows_keep_their_exact_geometry_and_placeholder_state() => WithView(() => new SourceControlSettingsPage(), (view, model, _) =>
    {
        var editor = Assert.Single(Nodes<TextBox>(view)); Assert.Equal(13, editor.FontSize); Assert.Equal(90, editor.MinHeight); var placeholder = Nodes<TextBlock>(view).Single(x => x.Text == "For example: use conventional commit prefixes"); Assert.Equal(new Thickness(5, 0), placeholder.Margin); Assert.False(placeholder.IsHitTestVisible); Assert.True(placeholder.IsVisible);
        editor.Text = " "; Sync(); Assert.Equal(" ", model.Settings.CommitInstructions); Assert.False(placeholder.IsVisible); editor.Text = ""; Sync(); Assert.True(placeholder.IsVisible);
        var section = Nodes<ChromeSection>(view).Single(x => Text(x).First() == "Worktrees and pull requests"); Assert.Equal(2, Nodes<ChromeRow>(section).Length); Assert.Single(Nodes<ChromeRowDivider>(section)); Assert.Empty(Nodes<Button>(Row(section, "Pull requests")));
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-267")] // App/Views/Settings/SettingsView.swift:417-457,246-272,278
    public void Appearance_uses_three_cards_10_apart_and_title_only_chrome_with_22_top_padding() => WithSettings((view, _, _) =>
    {
        SettingsNavigation.Shared.RequestedPage = SettingsPage.Appearance; Sync(); var appearance = Assert.Single(Nodes<AppearanceSettingsPage>(view));
        Assert.Equal(3, Nodes<ChromeCard>(appearance).Length); var group = Assert.Single(Nodes<StackPanel>(appearance), stack => stack.Children.Count == 3 && stack.Children[1] is WallpaperCard);
        Assert.Equal(10, group.Spacing); Assert.Equal(22, Field<Control>(view, "pageStack").Margin.Top);
        var heading = Nodes<TextBlock>(appearance).First(); Assert.Equal("Appearance", heading.Text); Assert.Equal(13, heading.FontSize); Assert.Equal(Avalonia.Media.FontWeight.SemiBold, heading.FontWeight);
        Assert.Empty(Nodes<ChromeSearchField>(Field<Control>(view, "detail"))); Assert.Empty(Nodes<AboutUpdateChromeAccessory>(view)); Assert.Empty(Nodes<ProvidersRefreshButton>(view));
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-268")] // App/Views/Settings/SettingsView.swift:462-517
    [Trait("Req", "UI-SETTINGS-079")] // App/Views/Settings/SettingsView.swift:460-517
    public void Zoom_buttons_step_all_eight_levels_and_reset_keeps_its_slot_with_primary_tabular_digits() => WithView(() => new AppearanceSettingsPage(), (view, model, h) =>
    {
        var zoom = Row(view, "Zoom"); var buttons = Nodes<Button>(zoom); Assert.Equal(3, buttons.Length); foreach (var button in buttons) { Assert.Equal(22, button.Width); Assert.Equal(22, button.Height); }
        var decrease = buttons.Single(x => Equals(ToolTip.GetTip(x), "Zoom out")); var increase = buttons.Single(x => Equals(ToolTip.GetTip(x), "Zoom in")); var reset = buttons.Single(x => Equals(ToolTip.GetTip(x), "Back to the stock size"));
        Assert.Equal(Avalonia.Media.FontWeight.Normal, SwiftImage.GetFont(Assert.Single(Nodes<SwiftImage>(decrease)))!.Resolve().FontWeight); // :474-476
        Assert.Equal(Avalonia.Media.FontWeight.Normal, SwiftImage.GetFont(Assert.Single(Nodes<SwiftImage>(increase)))!.Resolve().FontWeight); // :490-492
        Assert.Equal(Avalonia.Media.FontWeight.Medium, SwiftImage.GetFont(Assert.Single(Nodes<SwiftImage>(reset)))!.Resolve().FontWeight); // :502-504
        Assert.False(reset.IsEnabled); Assert.Equal(0, reset.Opacity); Assert.True(reset.IsVisible); Click(decrease); Sync(); Assert.Equal(90, model.Settings.AppZoom); Click(decrease); Sync(); Assert.Equal(75, model.Settings.AppZoom); Assert.False(decrease.IsEnabled);
        foreach (var level in new[] { 90, 100, 110, 125, 150, 175, 200 }) { Click(increase); Sync(); Assert.Equal(level, model.Settings.AppZoom); Assert.Equal((long)level, h.Defaults.Object("appZoom")); }
        AdvanceFrames(h, 201); Assert.False(increase.IsEnabled); Assert.True(reset.IsEnabled); Assert.Equal(1, reset.Opacity); Assert.Contains("200% of the stock size", Text(zoom));
        var value = Nodes<TextBlock>(zoom).Single(x => x.MinWidth == 46); Assert.Equal(11, value.FontSize); Assert.Equal(Avalonia.Media.FontWeight.Medium, value.FontWeight); Assert.Contains(value.FontFeatures!, f => f.Tag == "tnum" && f.Value == 1);
        Assert.Equal(SwiftFont.System(11, SwiftFont.Weight.Medium).Resolve().FontFamily, value.FontFamily); Assert.Equal(Chrome.PrimaryText.Resolve(value.ActualThemeVariant), Assert.IsType<Avalonia.Media.SolidColorBrush>(value.Foreground).Color);
        Click(reset); Sync(); Assert.Equal(100, model.Settings.AppZoom); Assert.Contains("The stock size", Text(zoom)); Assert.False(reset.IsEnabled); AdvanceFrames(h, 201); Assert.Equal(0, reset.Opacity);
    });

    [Fact]
    [Trait("Req", "PR-THEME-046")] // App/Views/Settings/SettingsView.swift:1165-1166,1194-1201
    [Trait("Req", "PR-THEME-047")] // App/Views/Settings/SettingsView.swift:1204-1208
    [Trait("Req", "PR-THEME-048")] // App/Views/Settings/SettingsView.swift:1168-1192
    [Trait("Req", "PR-THEME-049")] // App/Views/Settings/SettingsView.swift:424-426
    [Trait("Req", "UI-SETTINGS-017")] // App/Views/Settings/SettingsView.swift:1157-1209
    [Trait("Req", "UI-THEME-001")] // App/Views/Settings/SettingsView.swift:422-426
    [Trait("Req", "UI-THEME-003")] // App/Views/Settings/SettingsView.swift:1193-1201
    [Trait("Req", "UI-THEME-004")] // App/Views/Settings/SettingsView.swift:1196
    [Trait("Req", "UI-THEME-005")] // App/Views/Settings/SettingsView.swift:1198
    [Trait("Req", "UI-THEME-006")] // App/Views/Settings/SettingsView.swift:1204-1208
    public void Theme_capsule_has_source_metrics_and_menu_checks_the_selected_theme_and_updates_Nord_detail() => WithView(() => new AppearanceSettingsPage(), (view, model, _) =>
    {
        var picker = Assert.Single(Nodes<ThemePickerButton>(view)); var label = Assert.Single(Nodes<StackPanel>(picker), stack => stack.Height == 28); Assert.Equal(6, label.Spacing); Assert.Equal(new Thickness(12, 0), label.Margin);
        Assert.Equal(12.5, Nodes<TextBlock>(picker).Single().FontSize); Assert.Equal(SwiftFont.System(8, SwiftFont.Weight.Bold), SwiftImage.GetFont(Nodes<SwiftImage>(picker).Single()));
        var presenter = new RecordingPresenter(); using var presentation = Presentation.Presenter.Override(presenter); Click(Assert.Single(Nodes<Button>(picker))); Sync(); var menu = Opened(presenter.Calls.Last(x => x.Presented).Popup);
        Assert.Equal(26, Nodes<PopoverItem>(menu).Length); Assert.Equal(19, SwiftEnum<AppTheme>.AllCases.Count(theme => theme.Scheme() == ThemeVariant.Dark)); Assert.Equal(6, SwiftEnum<AppTheme>.AllCases.Count(theme => theme.Scheme() == ThemeVariant.Light));
        var system = Nodes<PopoverItem>(menu).Single(item => Text(item).Contains("System")); Assert.Equal(1, Nodes<SwiftImage>(system).Single(image => image.SymbolName == "checkmark").Opacity);
        Click(Button(menu, "Nord")); Sync(); Assert.Equal(AppTheme.Nord, model.Settings.Theme); Assert.Contains("Dark \u00b7 Frost", Text(Row(view, "Theme")));
    });

    [Fact]
    [Trait("Req", "PR-THEME-050")] // App/Views/Settings/SettingsView.swift:428-430,526-539
    [Trait("Req", "PR-THEME-051")] // App/Views/Settings/SettingsView.swift:524,540-562
    [Trait("Req", "PR-THEME-052")] // App/Views/Settings/SettingsView.swift:563
    [Trait("Req", "UI-SETTINGS-018")] // App/Views/Settings/SettingsView.swift:519-563
    [Trait("Req", "UI-THEME-008")] // App/Views/Settings/SettingsView.swift:427-430
    [Trait("Req", "UI-THEME-009")] // App/Views/Settings/SettingsView.swift:532-534
    [Trait("Req", "UI-THEME-010")] // App/Views/Settings/SettingsView.swift:528-531
    [Trait("Req", "UI-THEME-011")] // App/Views/Settings/SettingsView.swift:535-538
    [Trait("Req", "UI-THEME-012")] // App/Views/Settings/SettingsView.swift:524,543-560
    public void Transparency_reset_fades_and_offsets_over_200ms_without_consuming_layout_width() => WithView(() => new AppearanceSettingsPage(), (view, model, h) =>
    {
        var control = Assert.Single(Nodes<BackdropOpacitySlider>(view)); var reset = Assert.Single(Nodes<Button>(control)); var slider = Assert.Single(Nodes<Slider>(control));
        var row = Assert.Single(Nodes<StackPanel>(control)); Assert.Equal(8, row.Spacing); Assert.Equal("Window transparency", AutomationProperties.GetName(control)); Assert.Equal(0.5, slider.Value);
        Assert.Equal("Clear", ToolTip.GetTip(Nodes<SwiftImage>(control).Single(image => image.SymbolName == "circle.dotted"))); Assert.Equal("Solid", ToolTip.GetTip(Nodes<SwiftImage>(control).Single(image => image.SymbolName == "circle.fill")));
        var width = control.Bounds.Width; model.Settings.BackdropOpacity = 0.7; Sync(); Assert.True(reset.IsVisible); Assert.Equal(0, reset.Opacity);
        AdvanceFrames(h, 100); Assert.InRange(reset.Opacity, 0.001, 0.999); Assert.InRange(Assert.IsType<Avalonia.Media.TranslateTransform>(reset.RenderTransform).X, -28, -22);
        AdvanceFrames(h, 101); Assert.Equal(1, reset.Opacity); Assert.Equal(-28, Assert.IsType<Avalonia.Media.TranslateTransform>(reset.RenderTransform).X); Assert.Equal(width, control.Bounds.Width);
        Click(reset); Sync(); Assert.Equal(0.5, model.Settings.BackdropOpacity); AdvanceFrames(h, 201); Assert.False(reset.IsVisible);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-251")] // App/Views/Settings/SettingsView.swift:408-409,449-450
    [Trait("Req", "PR-THEME-084")] // App/Views/Settings/SettingsView.swift:361-364
    [Trait("Req", "PR-THEME-086")] // App/Views/Settings/SettingsView.swift:404-407
    [Trait("Req", "PR-THEME-087")] // App/Views/Settings/SettingsView.swift:408-409
    [Trait("Req", "PR-USAGE-076")] // App/Views/Settings/SettingsView.swift:374-379
    [Trait("Req", "UI-USAGE-042")] // App/Views/Settings/SettingsView.swift:373-379
    [Trait("Req", "UI-USAGE-043")] // App/Views/Settings/SettingsView.swift:374-377
    [Trait("Req", "UI-USAGE-044")] // App/Views/Settings/SettingsView.swift:378
    public void General_preview_transitions_are_not_replayed_on_equal_writes_and_usage_switch_is_last() => WithView(() => new GeneralSettingsPage(), (view, model, _) =>
    {
        var sound = new SettingsViewSoundRecorder(); using var output = SoundOutput.Current.Override(sound); model.Settings.ChimeWhenFinished = false; model.Settings.SettleSound = false; Sync(); Assert.Equal(0, sound.Starts);
        model.Settings.ChimeWhenFinished = true; Sync(); model.Settings.ChimeWhenFinished = true; Sync(); Assert.Equal(1, sound.Starts); model.Settings.SettleSound = true; Sync(); model.Settings.SettleSound = true; Sync(); Assert.Equal(2, sound.Starts);
        model.Settings.ThreadFinishAction = ThreadFinishAction.Archive; Sync(); var settle = Row(view, "Sound when settling"); Assert.False(settle.IsEnabled); Assert.Equal(0.5, settle.Opacity); Assert.True(settle.IsVisible); Assert.True(model.Settings.SettleSound);
        var conversation = Nodes<ChromeSection>(view).Single(x => Text(x).First() == "Conversation"); Assert.Equal("Show a usage panel", Text(Nodes<ChromeRow>(conversation).Last()).First());
        var binding = Field<Binding<bool>>(Assert.Single(Nodes<SmallSwitch>(Row(view, "Show a usage panel"))), "isOn"); binding.Value = true; Sync(); Assert.True(model.Settings.ShowsUsagePanel);
    });

    [Fact]
    [Trait("Req", "PR-UPDATE-070")] // App/Views/Settings/SettingsView.swift:942-945
    [Trait("Req", "PR-UPDATE-071")] // App/Views/Settings/SettingsView.swift:260-262
    [Trait("Req", "PR-UPDATE-081")] // App/Views/Settings/SettingsView.swift:968-983
    [Trait("Req", "PR-UPDATE-082")] // App/Views/Settings/SettingsView.swift:968-1004
    [Trait("Req", "PR-UPDATE-084")] // App/Views/Settings/SettingsView.swift:991-1002
    [Trait("Req", "UI-UPDATE-019")] // App/Views/Settings/SettingsView.swift:968-983
    public void About_accessory_is_conditional_and_app_header_uses_source_fonts_version_and_insets() => WithSettings((view, _, _) =>
    {
        SettingsNavigation.Shared.RequestedPage = SettingsPage.About; Sync(); Assert.Single(Nodes<AboutUpdateChromeAccessory>(view)); var about = Assert.Single(Nodes<AboutSettingsPage>(view));
        var root = Assert.IsType<StackPanel>(about.Child); Assert.Equal(20, root.Spacing); Assert.Equal(0, root.Children[0].DesiredSize.Height);
        var name = Nodes<TextBlock>(root.Children[1]).Single(x => x.Text == "Marrow"); Assert.Equal(15, name.FontSize); Assert.Equal(Avalonia.Media.FontWeight.SemiBold, name.FontWeight);
        Assert.Contains($"Version {AppInfo.Current.Version} ({AppInfo.Current.Build})", Text(root.Children[1])); var description = Nodes<TextBlock>(about).Single(x => x.Text == "A native home for your coding agents, built in C# with Avalonia."); Assert.Equal(13, description.FontSize); Assert.Equal(new Thickness(16, 11, 10, 11), description.Margin);
        SettingsNavigation.Shared.RequestedPage = SettingsPage.General; Sync(); Assert.Empty(Nodes<AboutUpdateChromeAccessory>(view));
    });

    [Fact]
    [Trait("Req", "PR-UPDATE-083")] // App/Views/Settings/SettingsView.swift:985-987
    [Trait("Req", "UI-SETTINGS-070")] // App/Views/Settings/SettingsView.swift:985-987
    [Trait("Req", "UI-UPDATE-020")] // App/Views/Settings/SettingsView.swift:985-987
    public void About_Show_replays_all_eight_tour_pages_even_when_the_tour_was_already_seen() => WithView(() => new AboutSettingsPage(), (view, model, _) =>
    {
        model.Settings.HasSeenTour = true; Sync(); try { Click(Button(Row(((StackPanel)((AboutSettingsPage)view).Child!).Children[1], "Welcome tour"), "Show")); Sync(); var window = Field<Avalonia.Controls.Window>(Marrow.App.Views.Tour.TourWindowController.Shared, "window"); Assert.True(window.IsVisible); Assert.Equal(8, Marrow.App.Views.Tour.Tour.Pages.Count); }
        finally { Marrow.App.Views.Tour.TourWindowController.Shared.Close(); Sync(); } Assert.True(model.Settings.HasSeenTour);
    });

    [Fact]
    [Trait("Req", "PR-UPDATE-085")] // App/Views/Settings/SettingsView.swift:946-964
    [Trait("Req", "PR-UPDATE-086")] // App/Views/Settings/SettingsView.swift:1060-1101
    [Trait("Req", "PR-UPDATE-087")] // App/Views/Settings/SettingsView.swift:1012-1056
    [Trait("Req", "UI-SETTINGS-059")] // App/Views/Settings/SettingsView.swift:1060-1102
    public void Droppy_attribution_link_and_legal_document_titles_are_exact() => WithView(() => new AboutSettingsPage(), (view, _, _) =>
    {
        var droppy = Assert.Single(Nodes<MadeByDroppyRow>(view)); var button = Assert.Single(Nodes<Button>(droppy)); Assert.Equal("Open getdroppy.app", ToolTip.GetTip(button)); Assert.Equal("Made by Droppy. Open getdroppy.app", AutomationProperties.GetName(button));
        SwiftURL? opened = null; using var browser = Marrow.Services.Port.Workspace.Open.Override(url => { opened = url; return true; }); Click(button); Assert.Equal("https://getdroppy.app", opened!.AbsoluteString);
        Assert.Contains("Ported from Zeron by Wing, MIT License.", Text(view)); Assert.Contains("Ported from TourKit by Ram Patra, MIT License.", Text(view));
        var presenter = new RecordingPresenter(); using var presentation = Presentation.Presenter.Override(presenter); Click(Button(Assert.Single(Nodes<LicensesRow>(view)), "Show")); Sync(); var content = Opened(presenter.Calls.Last(x => x.Presented).Popup); Assert.Equal(3, Nodes<MarkdownView>(content).Length); Assert.Contains("Marrow", Text(content)); Assert.Contains("Third-party notices", Text(content)); Assert.Contains("Trademarks", Text(content));
    });

    // STOP App.Views.Settings.SettingsView #4
    // Kind: dependency / owner decision. Swift: SettingsView.swift:954-955,1002 (terminal fragments only).
    // Records: PR-SETTINGS-238, PR-SETTINGS-248, PR-UPDATE-082, PR-UPDATE-084, PR-UPDATE-085, UI-SETTINGS-072, UI-UPDATE-024/029 (terminal clauses only).
    // Missing: TerminalHostView explicitly has no renderer or chosen terminal component.
    // Needed: the shipped terminal's name, author, licence and project URL; do not credit SwiftTerm on Windows.
    // Keep the Terminal title/divider and the AGPL sentence prefix; only its CreditLink, component detail and trailing licence fragment are blocked.
    // STOP App.Views.Settings.SettingsView #5
    // Kind: dependency. Swift: SettingsView.swift:1046-1056. Records: PR-SETTINGS-249 (shipping only).
    // Missing: the App project bundles only Assets/icon.png, not the three legal documents.
    // Needed: the packaging unit to ship LICENSE, THIRD_PARTY_NOTICES.md and TRADEMARK.md.
    // The independent reader, caching, missing-file path and popover are covered below.

    [Fact]
    [Trait("Req", "PR-SETTINGS-123")] // App/Views/Settings/SettingsView.swift:401-409
    [Trait("Req", "UI-THEME-027")] // App/Views/Settings/SettingsView.swift:401-409
    public void Settle_preview_only_plays_on_enable_and_archive_disables_the_whole_row_without_changing_value() => WithView(() => new GeneralSettingsPage(), (view, model, _) =>
    {
        var sound = new SettingsViewSoundRecorder(); using var output = SoundOutput.Current.Override(sound);
        var row = Row(view, "Sound when settling"); Assert.True(row.IsEnabled); Assert.Equal(1, row.Opacity);
        model.Settings.SettleSound = false; Sync(); Assert.Equal(0, sound.Starts);
        model.Settings.SettleSound = true; Sync(); Assert.Equal(1, sound.Starts);
        model.Settings.ThreadFinishAction = ThreadFinishAction.Archive; Sync(); Assert.False(row.IsEnabled); Assert.Equal(0.5, row.Opacity); Assert.True(model.Settings.SettleSound);
        model.Settings.ThreadFinishAction = ThreadFinishAction.Settle; Sync(); Assert.True(row.IsEnabled); Assert.Equal(1, row.Opacity); Assert.Equal(1, sound.Starts);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-124")] // App/Views/Settings/SettingsView.swift:424-426,1159-1210
    [Trait("Req", "PR-SETTINGS-126")] // App/Views/Settings/SettingsView.swift:432-436
    [Trait("Req", "PR-SETTINGS-127")] // App/Views/Settings/SettingsView.swift:438-442
    public void Appearance_rows_follow_the_source_and_selected_details_are_live() => WithView(() => new AppearanceSettingsPage(), (view, model, _) =>
    {
        var section = Assert.Single(Nodes<ChromeSection>(view)); Assert.Equal("Appearance", Text(section).First());
        Assert.Equal(new[] { "Theme", "Transparency", "Sidebar", "Activity list", "Open the sidebar at launch", "Wallpaper", "Zoom" }, Nodes<ChromeRow>(view).Select(x => Text(x).First()));
        Assert.Contains("Follows Windows", Text(Row(view, "Theme"))); model.Settings.Theme = AppTheme.CatppuccinMocha; Sync(); Assert.Contains("Dark \u00b7 Mauve", Text(Row(view, "Theme")));
        var sidebar = Row(view, "Sidebar"); Assert.Equal(new[] { "Column", "Floating", "Panel only" }, Nodes<Button>(sidebar).Select(x => Text(x).Last()));
        Click(Button(sidebar, "Panel only")); Sync(); Assert.Equal(SidebarMode.PanelOnly, model.Settings.SidebarMode); Assert.Contains("No column and no toolbar button; the floating panel is the sidebar", Text(sidebar));
        var open = Row(view, "Open the sidebar at launch"); Assert.False(open.IsEnabled); Assert.Equal(0.5, open.Opacity);
        var activity = Row(view, "Activity list"); Assert.True(activity.IsEnabled); Click(Button(activity, "Project name")); Sync(); Assert.Equal(ActivityThreadStyle.ProjectName, model.Settings.ActivityThreadStyle);
        Assert.Contains("The project's name, with a folder mark, on a line under the thread's title", Text(activity));
        Click(Button(sidebar, "Column")); Sync(); Assert.True(open.IsEnabled); Assert.Equal(1, open.Opacity);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-124")] // App/Views/Settings/SettingsView.swift:1165-1208
    public void Theme_menu_partitions_all_26_palettes_and_a_selection_writes_through() => WithView(() => new ThemePickerButton(new Binding<AppTheme>(() => ThemeManager.Current, value => ThemeManager.Current = value)), (view, _, _) =>
    {
        var presenter = new RecordingPresenter(); using var presentation = Presentation.Presenter.Override(presenter);
        var button = Assert.Single(Nodes<Button>(view)); Assert.Contains("System", Text(button)); Assert.Single(Nodes<ThemeSwatch>(view));
        Assert.Equal(14, Nodes<ThemeSwatch>(view).Single().Width); Click(button); Sync();
        var menu = Opened(presenter.Calls.Last(x => x.Presented).Popup);
        var expected = new[] { AppTheme.System }.Concat(SwiftEnum<AppTheme>.AllCases.Where(x => x.Scheme() == ThemeVariant.Dark)).Concat(SwiftEnum<AppTheme>.AllCases.Where(x => x.Scheme() == ThemeVariant.Light));
        Assert.Equal(expected.Select(x => x.DisplayName()), Nodes<PopoverItem>(menu).Select(x => Text(x).First()));
        Assert.Equal(new[] { "Dark", "Light" }, Nodes<PopoverSectionHeader>(menu).Select(x => Text(x).Single()));
        var choice = Nodes<PopoverItem>(menu).Single(x => Text(x).Contains("Claude Light")); Click(Assert.Single(Nodes<Button>(choice))); Sync(); Assert.Equal(AppTheme.ClaudeLight, ThemeManager.Current);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-125")] // App/Views/Settings/SettingsView.swift:521-564
    public void Transparency_writes_continuously_and_uses_the_exact_stock_threshold_and_overlay_geometry() => WithView(() => new AppearanceSettingsPage(), (view, model, h) =>
    {
        var control = Assert.Single(Nodes<BackdropOpacitySlider>(view)); Assert.Equal("Window transparency", AutomationProperties.GetName(control));
        var slider = Assert.Single(Nodes<Slider>(control)); Assert.Equal(0, slider.Minimum); Assert.Equal(1, slider.Maximum); Assert.Equal(140, slider.Width);
        var reset = Assert.Single(Nodes<Button>(control)); Assert.Equal(22, reset.Width); Assert.Equal(22, reset.Height); Assert.Equal("Back to the stock look", ToolTip.GetTip(reset)); Assert.False(reset.IsVisible);
        slider.Value = 0.504; Sync(); Assert.Equal(0.504, model.Settings.BackdropOpacity); Assert.False(reset.IsVisible); Assert.Equal(0.504, h.Defaults.Object("backdropOpacity"));
        slider.Value = 0.51; Sync(); Assert.True(reset.IsVisible); Click(reset); Sync(); Assert.Equal(0.5, model.Settings.BackdropOpacity);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-224")] // App/Views/Settings/SettingsView.swift:585-614
    [Trait("Req", "PR-SETTINGS-225")] // App/Views/Settings/SettingsView.swift:646-675
    [Trait("Req", "PR-SETTINGS-226")] // App/Views/Settings/SettingsView.swift:629-634,676-687,772-779
    [Trait("Req", "PR-SETTINGS-231")] // App/Views/Settings/SettingsView.swift:638-641,738-757
    public void Provider_sections_are_all_eager_and_usage_is_absent_for_uninstalled_accounts() => WithView(() => new ProvidersSettingsPage(), (view, _, _) =>
    {
        Assert.Equal(14, Nodes<ProviderSettingsSection>(view).Length);
        foreach (var section in Nodes<ProviderSettingsSection>(view)) Assert.Single(Nodes<ChromeCard>(section));
        Assert.Empty(Nodes<Marrow.App.Views.Usage.PlanLimitsView>(view)); Assert.Empty(Nodes<Marrow.App.Views.Usage.CreditsView>(view));
        Assert.Equal(20, Assert.IsType<StackPanel>(Assert.IsType<ProvidersSettingsPage>(view).Child).Spacing);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-227")] // App/Views/Settings/SettingsView.swift:691-717
    [Trait("Req", "PR-SETTINGS-228")] // App/Views/Settings/SettingsView.swift:785-799
    [Trait("Req", "PR-SETTINGS-229")] // App/Views/Settings/SettingsView.swift:719-727
    public void API_key_eye_only_changes_mask_and_get_key_opens_the_declared_URL() => WithView(() => new ProviderSettingsSection(ProviderKind.Deepseek, Binding<bool>.Constant(true)), (view, _, _) =>
    {
        var field = Assert.Single(Nodes<TextBox>(view)); Assert.Equal(200, field.Width); Assert.Equal(12, field.FontSize); Assert.Equal('\u2022', field.PasswordChar);
        var eye = Nodes<Button>(view).Single(x => Equals(ToolTip.GetTip(x), "Show the API key")); Click(eye); Sync(); Assert.Equal('\0', field.PasswordChar); Assert.Equal("Hide the API key", ToolTip.GetTip(eye)); Click(eye); Sync(); Assert.Equal('\u2022', field.PasswordChar);
        SwiftURL? opened = null; using var browser = Marrow.Services.Port.Workspace.Open.Override(url => { opened = url; return true; });
        Click(Button(view, "Get a DeepSeek key")); Assert.Equal(ProviderKind.Deepseek.InstallURL, opened);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-230")] // App/Views/Settings/SettingsView.swift:732-736,780-783
    public void Binary_path_subtitle_follows_last_resolved_status_not_the_pending_override() => WithView(() => new ProviderSettingsSection(ProviderKind.Codex, Binding<bool>.Constant(true)), (view, model, _) =>
    {
        Seed(model, ProviderKind.Codex, model.Providers.Status(ProviderKind.Codex) with { Executable = SwiftURL.FileURLWithPath("/resolved/codex") }); Sync();
        Assert.Contains("/resolved/codex", Text(Row(view, "Binary path")));
        Assert.Single(Nodes<TextBox>(view)).Text = "  custom-codex  "; Sync(); Assert.Equal("custom-codex", model.Settings.BinaryPath(ProviderKind.Codex)); Assert.Contains("/resolved/codex", Text(Row(view, "Binary path")));
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-232")] // App/Views/Settings/SettingsView.swift:808-816
    [Trait("Req", "PR-SETTINGS-233")] // App/Views/Settings/SettingsView.swift:818-834
    [Trait("Req", "PR-SETTINGS-234")] // App/Views/Settings/SettingsView.swift:836-847
    [Trait("Req", "UI-SETTINGS-068")] // App/Views/Settings/SettingsView.swift:808-816
    public void Commit_editor_keeps_whitespace_and_text_generation_uses_the_source_asset_map() => WithView(() => new SourceControlSettingsPage(), (view, model, _) =>
    {
        var editor = Assert.Single(Nodes<TextBox>(view)); Assert.True(editor.AcceptsReturn); Assert.Equal(90, editor.MinHeight); editor.Text = " \nKeep this\n "; Sync(); Assert.Equal(" \nKeep this\n ", model.Settings.CommitInstructions);
        model.Settings.TextGeneration = TextGenerationChoice.Claude; Sync(); Assert.Contains(Nodes<SwiftImage>(view), image => image.AssetName == "claude");
        model.Settings.TextGeneration = TextGenerationChoice.Codex; Sync(); Assert.Contains(Nodes<SwiftImage>(view), image => image.AssetName == "codex");
        Assert.Contains(Storage.WorktreesDirectory, Text(Row(view, "Worktrees"))); Assert.Equal("Reveal", Text(Button(Row(view, "Worktrees"), "Reveal")).Single());
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-235")] // App/Views/Settings/SettingsView.swift:854-885
    [Trait("Req", "UI-SETTINGS-053")] // App/Views/Settings/SettingsView.swift:851-885
    public void Delete_all_uses_singular_confirmation_and_cancels_without_deleting() => WithView(() => new ArchiveDeleteAllButton(), (view, model, _) =>
    {
        var presenter = new RecordingPresenter(); using var presentation = Presentation.Presenter.Override(presenter);
        Click(Button(view, "Delete all")); Sync(); var popup = presenter.Calls.Last(x => x.Presented).Popup; var content = Opened(popup);
        Assert.Contains("Delete the archived thread?", Text(content)); Assert.Contains("Its history will be removed. Files in your project stay as they are.", Text(content)); Assert.Contains("Delete thread", Text(content)); Assert.Single(model.ArchivedThreads);
        var state = Field<MainActorModel>(view, "state"); state.GetType().GetProperty("IsPresented")!.SetValue(state, false); Sync(); Assert.Single(model.ArchivedThreads);
        Click(Button(view, "Delete all")); Sync(); content = Opened(presenter.Calls.Last(x => x.Presented).Popup); Click(Button(content, "Delete thread")); Sync(); Assert.Empty(model.ArchivedThreads);
    }, beforeModel: h => { var project = new Project("One", h.NewFolder("one")); h.WriteLibrary([project], [new ChatThread(project.Id, ProviderKind.Codex, null, null, RuntimeMode.Supervised) with { IsArchived = true, Title = "Archived" }]); });

    [Fact]
    [Trait("Req", "PR-SETTINGS-236")] // App/Views/Settings/SettingsView.swift:888-935
    [Trait("Req", "UI-SETTINGS-054")] // App/Views/Settings/SettingsView.swift:896-934
    public void Archived_rows_restore_immediately_and_delete_only_after_the_shared_dialog_confirms() => WithView(() => new ArchiveSettingsPage(), (view, model, _) =>
    {
        Assert.Equal(new[] { "Archived", "Another" }, Nodes<ChromeRow>(view).Select(x => Text(x).First())); Assert.Single(Nodes<ChromeRowDivider>(view)); Assert.Contains("One", Text(Row(view, "Archived")));
        Click(Button(Row(view, "Archived"), "Delete")); Sync(); Assert.Equal(2, model.ArchivedThreads.Count);
        var dialog = Field<Avalonia.Controls.Window>(view, "confirmation"); Assert.Equal("Delete \"Archived\"?", dialog.Title); Assert.Contains("Its history will be removed. Files in your project stay as they are.", Text((Control)dialog.Content!)); dialog.Close(); Sync(); Assert.Equal(2, model.ArchivedThreads.Count);
        Click(Button(Row(view, "Archived"), "Delete")); Sync(); dialog = Field<Avalonia.Controls.Window>(view, "confirmation"); Click(Button((Control)dialog.Content!, "Delete thread")); Sync(); Assert.Single(model.ArchivedThreads);
        Click(Button(Row(view, "Another"), "Restore")); Sync(); Assert.Empty(model.ArchivedThreads);
    }, beforeModel: h => { var project = new Project("One", h.NewFolder("one")); h.WriteLibrary([project], new[] { "Archived", "Another" }.Select(title => new ChatThread(project.Id, ProviderKind.Codex, null, null, RuntimeMode.Supervised) with { IsArchived = true, Title = title })); });

    [Fact]
    [Trait("Req", "PR-SETTINGS-237")] // App/Views/Settings/SettingsView.swift:938-965
    [Trait("Req", "PR-SETTINGS-238")] // App/Views/Settings/SettingsView.swift:968-1005
    [Trait("Req", "PR-SETTINGS-239")] // App/Views/Settings/SettingsView.swift:985-987
    [Trait("Req", "PR-SETTINGS-248")] // App/Views/Settings/SettingsView.swift:946-963,1060-1121
    [Trait("Req", "UI-SETTINGS-072")] // App/Views/Settings/SettingsView.swift:950-960,1104-1121
    [Trait("Req", "UI-UPDATE-023")] // App/Views/Settings/SettingsView.swift:991-995
    [Trait("Req", "UI-UPDATE-024")] // App/Views/Settings/SettingsView.swift:997-1002
    [Trait("Req", "UI-UPDATE-029")] // App/Views/Settings/SettingsView.swift:954-956
    public void About_keeps_the_empty_update_slot_and_truthful_product_and_attribution_content() => WithView(() => new AboutSettingsPage(), (view, _, _) =>
    {
        var stack = Assert.IsType<StackPanel>(Assert.IsType<AboutSettingsPage>(view).Child); Assert.Equal(20, stack.Spacing); Assert.Equal(3, stack.Children.Count); Assert.IsType<AboutSoftwareUpdateSection>(stack.Children[0]);
        Assert.Contains("Marrow", Text(view)); Assert.Contains("A native home for your coding agents, built in C# with Avalonia.", Text(view));
        var licence = Assert.Single(Nodes<TextBlock>(stack.Children[1]), text => text.Text == "Marrow is licensed under the GNU AGPL v3.0 with attribution terms; "); // :996-1002, OD-0012 F1
        Assert.Equal(12, licence.FontSize); Assert.Equal(new Thickness(16, 11, 10, 11), licence.Margin);
        var appRows = Assert.Single(Nodes<StackPanel>(stack.Children[1]), panel => panel.Children.Contains(licence)); Assert.IsType<ChromeRowDivider>(appRows.Children[appRows.Children.IndexOf(licence) - 1]);
        var terminal = Row(stack.Children[2], "Terminal"); Assert.Empty(Nodes<CreditLink>(terminal)); // :953-956; unknown Windows component is not fabricated.
        var creditRows = Assert.Single(Nodes<StackPanel>(stack.Children[2]), panel => panel.Children.Contains(terminal)); Assert.IsType<ChromeRowDivider>(creditRows.Children[creditRows.Children.IndexOf(terminal) + 1]);
        Assert.Contains("Based on Droppy Code by Jordy Spruit (Droppy), https://getdroppycode.app", Text(view));
        Assert.Contains("The slideshow from the first launch: Hydra, the effort slider, panels and themes.", Text(view));
        Assert.Equal(56, Assert.Single(Nodes<Image>(stack.Children[1])).Width);
        var droppy = Assert.Single(Nodes<MadeByDroppyRow>(view)); var mark = Assert.Single(Nodes<SwiftImage>(droppy), image => image.AssetName is not null); Assert.Equal("droppy-logo", mark.AssetName); Assert.Equal(40, mark.Width); Assert.Single(Nodes<SwiftImage>(droppy), image => image.SymbolName == "arrow.up.right");
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-249")] // App/Views/Settings/SettingsView.swift:1009-1057
    [Trait("Req", "UI-SETTINGS-060")] // App/Views/Settings/SettingsView.swift:1007-1057
    public void Licences_are_read_once_and_the_three_markdown_blocks_use_fixed_popover_geometry() => WithView(() => new LicensesRow(), (view, _, _) =>
    {
        var presenter = new RecordingPresenter(); using var presentation = Presentation.Presenter.Override(presenter); Click(Button(view, "Show")); Sync(); var content = Opened(presenter.Calls.Last(x => x.Presented).Popup);
        var scroll = Assert.Single(Nodes<ScrollViewer>(content)); Assert.Equal(480, scroll.Width); Assert.Equal(440, scroll.Height);
        var blocks = Assert.Single(Nodes<StackPanel>(content), x => x.Spacing == 18); Assert.Equal(new Thickness(18), blocks.Margin); Assert.Equal(3, blocks.Children.Count);
        Assert.Equal(new[] { "Marrow", "Third-party notices", "Trademarks" }, blocks.Children.Select(x => Text(x).First()));
        foreach (var markdown in Nodes<MarkdownView>(content)) Assert.Equal(12, MarkdownView.GetMarkdownPointSize(markdown)); Assert.Equal(3, Nodes<MarkdownView>(content).Length);
        var reader = typeof(LicensesRow).GetMethod("Text", BindingFlags.Static | BindingFlags.NonPublic)!; Assert.Equal("The THIRD_PARTY_NOTICES file is missing from this copy of the app.", reader.Invoke(null, ["THIRD_PARTY_NOTICES"]));
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-250")] // App/Views/Settings/SettingsView.swift:109-134
    public void General_settings_persist_before_closing_without_commit_or_cancel_buttons() => WithView(() => new GeneralSettingsPage(), (view, model, h) =>
    {
        model.Settings.ShowReasoning = true; Sync(); Assert.True(h.Defaults.Bool("showReasoning")); Assert.DoesNotContain(Text(view), label => label is "Apply" or "OK" or "Cancel" or "Done");
    });

    // STOP App.Views.Settings.SettingsView #3
    // Kind: dependency. Swift: SettingsView.swift:280,251-253. Records: PR-SETTINGS-027; PR-SETTINGS-028 (MCP branch).
    // Missing: MCPSettingsPage(query) is MARROW_STUB in the assigned base. Needed: MCP unit implementation.
    // Part 4 also assigns PR-SETTINGS-252: the shell remains the owner only of the query/routing, not server deletion.
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
    [Trait("Req", "UI-SETTINGS-016")] // App/Views/Settings/SettingsView.swift:1130-1154
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
    [Trait("Req", "UI-THEME-026")] // App/Views/Settings/SettingsView.swift:358-364
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
    [Trait("Req", "UI-UPDATE-033")] // App/Views/Settings/SettingsView.swift:77,148-164
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
    [Trait("Req", "UI-STORE-001")] // App/Views/Settings/SettingsView.swift:836-842
    [Trait("Req", "UI-STORE-002")] // App/Views/Settings/SettingsView.swift:839-840
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
