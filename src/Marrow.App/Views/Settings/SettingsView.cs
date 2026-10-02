using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Media;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.App.Port;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Marrow.UI.Common;
using Marrow.UI.Sound;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Theme;
using Marrow.Core.Support;
using Marrow.UI.Markdown;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.IO;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;

namespace Marrow.App.Views.Settings;

// Source: DroppyCode/App/Views/Settings/SettingsView.swift (B3 52bc792), :1-1210.
// Skeleton (c100 group 4): every type the file declares, private ones included, with its inputs; SettingsPage's cases
// and raw values and SettingsNavigation's stored property are complete; every view body, computed property and helper
// is left to the implementing unit (MARROW_STUB), as are the views' private members.
// Forms (ED-095): a SwiftUI View is a Decorator whose constructor builds `body`; `@Environment(AppModel.self)` is read
// from the environment, not passed in; `enum SettingsPage: String` carries [RawValue] on every case (R-ENUM-1) and its
// computed members are C# 14 extension members of SettingsPageExtensions (R-NAME-4); `@MainActor @Observable` is
// MainActorModel (R-OBS-6); Color is SwiftColor; URL is SwiftURL. Swift `private` top-level types are `internal`.

/// <summary>:4-80 <c>enum SettingsPage: String, CaseIterable, Identifiable</c>.</summary>
public enum SettingsPage
{
    [RawValue("general")] General, // :5
    [RawValue("hydra")] Hydra, // :7
    [RawValue("appearance")] Appearance, // :8
    [RawValue("providers")] Providers, // :9
    [RawValue("mcp")] Mcp, // :10
    [RawValue("models")] Models, // :11
    [RawValue("sourceControl")] SourceControl, // :12
    [RawValue("shortcuts")] Shortcuts, // :13
    [RawValue("archive")] Archive, // :14
    [RawValue("about")] About, // :15
}

/// <summary>:17-79 the computed members of <c>SettingsPage</c>.</summary>
public static class SettingsPageExtensions
{
    extension(SettingsPage page)
    {
        // :17 var id: String { rawValue }
        public string Id => SwiftEnum<SettingsPage>.RawValue(page);

        // :19-32 var title: String
        public string Title => page switch
        {
            SettingsPage.General => "General", SettingsPage.Hydra => "Hydra", SettingsPage.Appearance => "Appearance",
            SettingsPage.Providers => "Providers", SettingsPage.Mcp => "MCP", SettingsPage.Models => "Models",
            SettingsPage.SourceControl => "Source control", SettingsPage.Shortcuts => "Shortcuts", SettingsPage.Archive => "Archive", SettingsPage.About => "About",
        };

        // :34-49 var symbol: String
        public string Symbol => page switch
        {
            SettingsPage.General => "gear", SettingsPage.Appearance => "paintpalette", SettingsPage.Providers => "cpu",
            SettingsPage.Mcp => "puzzlepiece.extension", SettingsPage.Models => "slider.horizontal.3",
            SettingsPage.Hydra => "point.3.connected.trianglepath.dotted", SettingsPage.SourceControl => "arrow.triangle.branch",
            SettingsPage.Shortcuts => "command", SettingsPage.Archive => "archivebox", SettingsPage.About => "info.circle",
        };

        // :51-64 var tint: Color
        public SwiftColor Tint => page switch
        {
            SettingsPage.General => Chrome.Gray, SettingsPage.Appearance => new(0.85, 0.44, 0.36), SettingsPage.Providers => Chrome.Blue,
            SettingsPage.Mcp => new(0.95, 0.55, 0.20), SettingsPage.Models => new(0.686, 0.322, 0.871), SettingsPage.Hydra => new(0.188, 0.690, 0.780),
            SettingsPage.SourceControl => new(0.345, 0.337, 0.839), SettingsPage.Shortcuts => new(0.32, 0.48, 0.93),
            SettingsPage.Archive => Chrome.Orange, SettingsPage.About => new(0.204, 0.780, 0.349),
        };

        // :66-79 var keywords: [String]
        public ValueList<string> Keywords => new(page switch
        {
            SettingsPage.General => new[] { "permissions", "worktree", "reasoning", "thinking", "notifications", "token", "tokens", "activity", "usage", "panel", "limits", "credits", "heatmap", "daily", "weekly", "cumulative", "settle", "settled", "finish", "finished", "done", "sound", "chime", "icon", "icons", "emoji", "symbol", "symbols", "project mark", "compact", "concise", "anti-slop", "slop", "verbose", "comments", "replies" },
            SettingsPage.Appearance => new[] { "appearance", "theme", "transparency", "transparent", "opacity", "glass", "dark", "light", "accent", "tint", "catppuccin", "dracula", "tokyo", "nord", "gruvbox", "solarized", "github", "claude", "codex", "cursor", "matrix", "text", "size", "font", "zoom", "wallpaper", "sidebar" },
            SettingsPage.Models => new[] { "model", "effort", "reasoning", "fast", "slider", "picker" },
            SettingsPage.Hydra => new[] { "hydra", "heads", "subagents", "sub-agents", "agents", "team", "orchestrator", "worker", "pair", "pairs", "parallel", "delegate", "queue" },
            SettingsPage.Providers => new[] { "codex", "claude", "cursor", "opencode", "opencode zen", "opencode go", "zen", "grok", "deepseek", "meta", "muse", "spark", "zai", "z.ai", "glm", "coding plan", "devin", "cognition", "antigravity", "agy", "google", "gemini", "copilot", "github", "command code", "commandcode", "cmd", "binary", "path", "sign in", "login", "api key", "usage", "limits", "limit", "plan", "quota", "credits", "balance" },
            SettingsPage.Mcp => new[] { "mcp", "model context protocol", "servers", "tools", "connect", "integrations", "github", "playwright", "notion", "linear", "slack", "figma", "supabase", "stripe", "resend", "email", "context7", "sentry", "postgres", "browser", "netlify", "deploy", "hosting" },
            SettingsPage.SourceControl => new[] { "git", "commit", "pull request", "titles", "text generation" },
            SettingsPage.Shortcuts => new[] { "keyboard", "keys" }, SettingsPage.Archive => new[] { "archived", "restore" }, SettingsPage.About => new[] { "version", "license" },
        });
    }
}

/// <summary>:84-89 <c>@MainActor @Observable final class SettingsNavigation</c>: a page asked for from outside the
/// window.</summary>
public sealed class SettingsNavigation : MainActorModel
{
    // :87 static let shared = SettingsNavigation()
    public static SettingsNavigation Shared { get; } = new();

    // :88 var requestedPage: SettingsPage?
    private SettingsPage? requestedPage;

    public SettingsPage? RequestedPage
    {
        get => Read(requestedPage);
        set => Set(ref requestedPage, value);
    }
}

/// <summary>:91-294 <c>struct SettingsView: View</c>: the Settings window's content.</summary>
public sealed class SettingsView : Decorator
{
    // :93-98
    private sealed class ViewState : MainActorModel
    {
        private SettingsPage page = SettingsPage.General;
        private string search = "", modelSearch = "", mcpSearch = "";
        public SettingsPage Page { get => Read(page); set => Set(ref page, value); }
        public string Search { get => Read(search); set => Set(ref search, value); }
        public string ModelSearch { get => Read(modelSearch); set => Set(ref modelSearch, value); }
        public string McpSearch { get => Read(mcpSearch); set => Set(ref mcpSearch, value); }
    }
    private readonly ViewState state = new();
    private readonly ChromeScrollModel scrollChrome = new();
    private readonly SettingsScrollTracker scrollTracker = new();
    private readonly StackPanel rows = new() { Spacing = 1, Margin = new Thickness(Chrome.ListInset, 12, Chrome.ListInset, 8) }; // :147,179-181
    private readonly Control sidebar;
    private readonly Border detail = new();
    private readonly Border frame;
    private Control? pageStack;
    private SettingsPage? builtPage;
    private readonly SidebarSearchField search;
    private readonly WindowBackdrop backdrop;
    private Control? riseChrome;
    private Control? risePage;

    // PORT-ONLY (OD-0074, ED-249): the parts the Settings window's open (Port/SettingsRise.cs) fades and raises.
    internal WindowBackdrop RiseBackdrop => backdrop;
    internal Panel? RiseSheet => detail.Child as Panel; // DetailSheetModifier: wash, tint, content
    internal SidebarSearchField RiseSearch => search;
    internal Controls RiseSidebarRows => rows.Children;
    internal Control? RiseChrome => riseChrome; // the chrome row, when the page has controls in it
    internal Control? RisePage => risePage;

    // :100-106
    private ValueList<SettingsPage> VisiblePages
    {
        get
        {
            var query = FoundationText.Trim(state.Search, includeNewlines: true);
            return new(SwiftEnum<SettingsPage>.AllCases.Where(page => query.Length == 0 || SwiftString.LocalizedCaseInsensitiveContains(page.Title, query) || page.Keywords.Any(word => SwiftString.LocalizedCaseInsensitiveContains(word, query))));
        }
    }

    // :109-134 body
    public SettingsView()
    {
        Width = 780; Height = 580; // :122
        search = new SidebarSearchField(new Binding<string>(() => state.Search, value => state.Search = value), onSubmit: () => { if (VisiblePages.Count > 0) state.Page = VisiblePages[0]; }); // :141-143
        search.Margin = new Thickness(Chrome.ListInset, 14, Chrome.ListInset, 0); // :144-145
        var scroll = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }; // :146,190
        WireScroll(scroll, page: false);
        var column = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") }; // :137
        var spacer = new Border { Height = Chrome.TrafficLightDiameter, Margin = new Thickness(0, Chrome.TrafficLightTop, 0, 0) }; // :138-140
        Grid.SetRow(search, 1); Grid.SetRow(scroll, 2); column.Children.Add(spacer); column.Children.Add(search); column.Children.Add(scroll);
        sidebar = PlainControls.Background(column, new WindowDragArea()); sidebar.Width = 212; // :112,192-193
        detail.Margin = new Thickness(0, Chrome.SheetInset, Chrome.SheetInset, Chrome.SheetInset); // :114-115
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("212,*") }; Grid.SetColumn(detail, 1); grid.Children.Add(sidebar); grid.Children.Add(detail);
        backdrop = new WindowBackdrop(0); // :118
        frame = new Border { Child = new Panel { Children = { backdrop, new WindowChromeProbe(), grid } } }.RoundedRectangle(Chrome.WindowCornerRadius, RoundedCornerStyle.Continuous); frame.ClipToBounds = true; // :119-120
        Child = frame;
        this.Track(() => AppEnvironment.GetAppModel(this).Settings.BackdropOpacity, value => backdrop.Update(value, null));
        this.Track(() => SettingsNavigation.Shared.RequestedPage, requested => // :123-127
        {
            if (requested is not { } page) return;
            state.Page = page;
            SettingsNavigation.Shared.RequestedPage = null;
        });
        this.Track(() => VisiblePages, pages => // :130-133
        {
            if (pages.Count > 0 && !pages.Contains(state.Page)) state.Page = pages[0];
            RebuildSidebar();
        });
        this.Track(() => state.Page, _ => { RebuildSidebar(); RebuildDetail(); });
        this.Track(() => UpdateChecker.Shared.UpdateAvailable, _ => RebuildSidebar());
        this.Track(() => { var model = AppEnvironment.GetAppModel(this); _ = model.Threads; return model.ArchivedThreads.Count; }, count => { if (state.Page == SettingsPage.Archive && (count == 0) != (pageStack is null)) RebuildDetail(); }); // :198 observe the collection behind the computed archive count; the page's ScrollView identity is its page (:231), so only the empty placeholder swap rebuilds.
        rows.Track(() => scrollTracker.IsFrozen, value => rows.IsHitTestVisible = !value); // :182
        DetachedFromVisualTree += (_, _) => scrollTracker.End(); // :117
    }

    // :148-177
    private void RebuildSidebar()
    {
        rows.Children.Clear();
        foreach (var page in VisiblePages)
        {
            var row = new SidebarRow(page.Title, () => state.Page = page, () =>
            {
                var badge = new SidebarIconBadge(() => page == SettingsPage.Hydra
                    ? new HydraMarkImage { Width = Chrome.SymbolSize * 1.15, Height = Chrome.SymbolSize * 1.15 }
                    : new SidebarSymbol(page.Symbol, page == SettingsPage.General ? 1.15 : 1)); // :151-157
                if (page != SettingsPage.About || !UpdateChecker.Shared.UpdateAvailable) return badge;
                return new Panel { Children = { badge, new UpdateAvailableDot { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top } } }; // :159-163
            }, _ => new Border(), isSelected: state.Page == page);
            rows.Children.Add(row);
        }
        if (VisiblePages.Count == 0)
        {
            var noResults = new TextBlock { Text = "No results", Foreground = Chrome.PrimaryText.Opacity(0.92).Brush(this) }.Font(SwiftFont.System(SwiftFont.TextStyle.Body, weight: SwiftFont.Weight.Semibold));
            var note = new TextBlock { Text = "Try a setting or a provider name.", Foreground = Chrome.SecondaryText.Brush(this) }.Font(SwiftFont.System(SwiftFont.TextStyle.Subheadline));
            rows.Children.Add(new StackPanel { Spacing = 6, Margin = new Thickness(8, 14, 8, 0), Children = { noResults, note } }); // :166-177
        }
    }

    // :196-215
    private void RebuildDetail()
    {
        var model = AppEnvironment.GetAppModel(this);
        if (builtPage != state.Page)
        {
            scrollChrome.Update(0); state.ModelSearch = ""; state.McpSearch = ""; builtPage = state.Page; // :211-215
        }
        if (state.Page == SettingsPage.Archive && model.ArchivedThreads.Count == 0)
        {
            var symbol = SwiftImage.SystemName("archivebox").Font(SwiftFont.System(44, SwiftFont.Weight.Light)).ForegroundStyle(Chrome.SecondaryText.Opacity(0.6)); // :201-203
            symbol.Width = 44; symbol.Height = 44; symbol.HorizontalAlignment = HorizontalAlignment.Center; symbol.VerticalAlignment = VerticalAlignment.Center;
            AutomationProperties.SetName(symbol, "No archived threads"); // :204
            detail.Child = symbol.DetailSheet(); pageStack = null; riseChrome = null; risePage = symbol; return;
        }
        Control content = state.Page switch // :276-287
        {
            SettingsPage.General => new GeneralSettingsPage(), SettingsPage.Appearance => new AppearanceSettingsPage(), SettingsPage.Providers => new ProvidersSettingsPage(),
            SettingsPage.Mcp => new MCPSettingsPage(state.McpSearch), SettingsPage.Models => new ModelsSettingsPage(state.ModelSearch), SettingsPage.Hydra => new HydraSettingsPage(),
            SettingsPage.SourceControl => new SourceControlSettingsPage(), SettingsPage.Shortcuts => new ShortcutsSettingsPage(), SettingsPage.Archive => new ArchiveSettingsPage(), SettingsPage.About => new AboutSettingsPage(),
        };
        var hasControls = state.Page is SettingsPage.Providers or SettingsPage.Models or SettingsPage.Mcp or SettingsPage.Archive or SettingsPage.About; // :270-272
        pageStack = new StackPanel { Spacing = Chrome.SectionSpacing, Margin = new Thickness(Chrome.ContentHorizontalPadding + 8, hasControls ? Chrome.ChromeTopPadding + Chrome.CapsuleHeight + 16 : PageTopWithoutControls, Chrome.ContentHorizontalPadding + 8, 24), Children = { content } }; // :220-227
        pageStack.Track(() => scrollTracker.IsFrozen, value => pageStack.IsHitTestVisible = !value);
        var scroll = new ScrollViewer { Template = SettingsScrollTemplate, Content = ClearingCaptionButtons(pageStack, hasControls), VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }; // :228-230
        WireScroll(scroll, page: true);
        var accessories = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var chromeRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10, MinHeight = Chrome.CapsuleHeight, Children = { new ChromeCompactTitle(state.Page.Title, scrollChrome), accessories } }; Grid.SetColumn(accessories, 1);
        var chrome = new StackPanel { Spacing = 10, MinHeight = Chrome.CapsuleHeight, Margin = new Thickness(Chrome.ChromeHorizontalPadding, Chrome.ChromeTopPadding, WindowChrome.CaptionTrailingReserve, 0), VerticalAlignment = VerticalAlignment.Top, Children = { chromeRow } }; // :246-267; trailing: DEVIATION OD-0044, clear of the Windows caption buttons. Grid's star track is Swift's flexible title frame.
        if (state.Page == SettingsPage.Models) accessories.Children.Add(new ChromeSearchField(new Binding<string>(() => state.ModelSearch, value => { state.ModelSearch = value; ((ModelsSettingsPage)content).SetQuery(value); }), "Search models")); // :248-250
        if (state.Page == SettingsPage.Mcp) accessories.Children.Add(new ChromeSearchField(new Binding<string>(() => state.McpSearch, value => { state.McpSearch = value; ((MCPSettingsPage)content).SetQuery(value); }), "Search servers")); // :251-253
        if (state.Page == SettingsPage.Providers) accessories.Children.Add(new ProvidersRefreshButton()); // :254-256
        if (state.Page == SettingsPage.Archive) accessories.Children.Add(new ArchiveDeleteAllButton()); // :257-259
        if (state.Page == SettingsPage.About) accessories.Children.Add(new AboutUpdateChromeAccessory()); // :260-262
        riseChrome = accessories.Children.Count > 0 ? chrome : null; risePage = content; // OD-0074, ED-249
        detail.Child = new Panel { Children = { scroll, new PaneTopVeil(scrollChrome) { VerticalAlignment = VerticalAlignment.Top }, chrome } }.DetailSheet();
    }

    // DEVIATION OD-0044, OD-0046, ED-239: a page's content starts below the Windows caption buttons. The source puts the
    // traffic lights over the sidebar's empty top (:137-140), so its pages that have no chrome controls start 22 below the
    // sheet edge (:225 "Pages without chrome controls start just below the sheet edge; the others clear their capsules").
    // The port's buttons sit at the top right, in the capsules' row (WindowChrome.CaptionTop), over the detail pane, so
    // those pages clear that row as the others do: Chrome.chromeTopPadding + Chrome.capsuleHeight + 16 (:225), the same
    // 60 as a page with controls. The page's own margin keeps the source's 22; the difference is added above it, so the
    // content at rest never runs under the buttons (scrolled content passes under them through the pane's top veil).
    private static Control ClearingCaptionButtons(Control pageStack, bool hasControls) =>
        hasControls ? pageStack : new Border { Padding = new Thickness(0, Chrome.ChromeTopPadding + Chrome.CapsuleHeight + 16 - PageTopWithoutControls, 0, 0), Child = pageStack };

    // :225 the top padding of a page without chrome controls.
    private const double PageTopWithoutControls = 22;

    // :228-241 ScrollView: the unthemed app owns its real vertical content presenter.
    private static readonly FuncControlTemplate<ScrollViewer> SettingsScrollTemplate = new((viewer, scope) =>
    {
        var presenter = new ScrollContentPresenter { Background = Brushes.Transparent, Name = "PART_ContentPresenter", [!ContentPresenter.ContentProperty] = viewer[!ContentControl.ContentProperty] };
        presenter.GestureRecognizers.Add(new Avalonia.Input.GestureRecognizers.ScrollGestureRecognizer { CanVerticallyScroll = true, CanHorizontallyScroll = false });
        return presenter.RegisterInNameScope(scope);
    });
    // :184-189,231-241. Avalonia reports wheel geometry; pointer phases mirror interaction and coasting.
    private void WireScroll(ScrollViewer scroll, bool page)
    {
        scroll.ScrollChanged += (_, e) => { if (e.OffsetDelta.Y == 0) return; scrollTracker.Note(); if (page) scrollChrome.Update(scroll.Offset.Y); };
        scroll.AddHandler(PointerPressedEvent, (_, e) => { if (e.Pointer.Type == PointerType.Touch) scrollTracker.Note(); }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        scroll.AddHandler(PointerReleasedEvent, (_, e) => { if (e.Pointer.Type == PointerType.Touch) scrollTracker.Coast(); }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }
}

/// <summary>:296-305 <c>struct SettingsSwitch: View</c>: a small switch with no label.</summary>
public sealed class SettingsSwitch : Decorator
{
    // :297 @Binding var isOn: Bool; :299-304 Toggle("", isOn: $isOn).labelsHidden().toggleStyle(.switch).controlSize(.small)
    public SettingsSwitch(Binding<bool> isOn) => Child = new SmallSwitch(isOn);
}

/// <summary>
/// ENGINEERING DECISION: Avalonia's own ToggleSwitch needs its Fluent theme's control template
/// (PART_MovingKnobs), which this codebase's headless test host does not register, so it throws
/// KeyNotFoundException the moment it is measured. A small, self-drawn switch avoids any themed
/// template. Track/knob sizes are not given by the Swift source beyond ".controlSize(.small)", so
/// they are chosen to read as a small macOS-style switch, not a source fact.
/// </summary>
internal sealed class SmallSwitch : Control
{
    private const double TrackWidth = 28;
    private const double TrackHeight = 16;
    private const double Inset = 2;
    private const double KnobDiameter = TrackHeight - Inset * 2;

    private readonly Binding<bool> isOn;
    private bool pressed;

    public SmallSwitch(Binding<bool> isOn)
    {
        this.isOn = isOn;
        Width = TrackWidth;
        Height = TrackHeight;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        this.Track(() => isOn.Value, _ => InvalidateVisual());
        KeyDown += (_, e) => { if (e.Key == Key.Space && IsEffectivelyEnabled) { isOn.Value = !isOn.Value; e.Handled = true; InvalidateVisual(); } };
        // :296-305 the native switch acts on a primary press that is released inside it: a secondary or middle press,
        // or a press dragged away before release, changes nothing.
        PointerPressed += (_, e) =>
        {
            if (!IsEffectivelyEnabled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            Focus();
            pressed = true;
            e.Pointer.Capture(this);
            e.Handled = true;
        };
        PointerReleased += (_, e) =>
        {
            if (!pressed) return;
            pressed = false;
            e.Pointer.Capture(null);
            e.Handled = true;
            if (!IsEffectivelyEnabled || !new Rect(Bounds.Size).Contains(e.GetPosition(this))) return;
            isOn.Value = !isOn.Value;
            InvalidateVisual();
        };
        PointerCaptureLost += (_, _) => pressed = false;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new SwitchPeer(this);
    private sealed class SwitchPeer(SmallSwitch owner) : ControlAutomationPeer(owner), IToggleProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.CheckBox;
        public ToggleState ToggleState => owner.isOn.Value ? ToggleState.On : ToggleState.Off;
        public void Toggle() { if (owner.IsEffectivelyEnabled) owner.isOn.Value = !owner.isOn.Value; }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var on = isOn.Value;
        var track = new Rect(0, 0, TrackWidth, TrackHeight);
        var trackBrush = on ? Chrome.AccentNSColor.Brush(this) : Chrome.Overlay(0.2).Brush(this);
        context.DrawRectangle(trackBrush, null, track, TrackHeight / 2, TrackHeight / 2);

        var knobX = on ? TrackWidth - Inset - KnobDiameter : Inset;
        var knob = new Rect(knobX, Inset, KnobDiameter, KnobDiameter);
        context.DrawEllipse(Brushes.White, null, knob);
    }
}

/// <summary>:307-413 <c>private struct GeneralSettingsPage: View</c>.</summary>
internal sealed class GeneralSettingsPage : Decorator
{
    // :310-412 body
    public GeneralSettingsPage()
    {
        var root = new StackPanel { Spacing = Chrome.SectionSpacing }; Child = root;
        this.Track(() => AppEnvironment.GetAppModel(this), model =>
        {
            var s = model.Settings;
            Control Switch(Func<bool> read, Action<bool> write) => new SettingsSwitch(new Binding<bool>(read, write));
            ChromeRow Toggle(string title, string? detail, Func<bool> read, Action<bool> write) => new(title, () => Switch(read, write), detail);
            StackPanel Rows(params Control[] rows)
            {
                var stack = new StackPanel(); for (var i = 0; i < rows.Length; i++) { if (i > 0) stack.Children.Add(new ChromeRowDivider()); stack.Children.Add(rows[i]); } return stack;
            }
            root.Children.Add(new TokenActivitySection()); // :314
            var permission = new Border();
            permission.Track(() => s.DefaultRuntimeMode, mode => permission.Child = new ChromeRow("Permissions", () => new GlassPickerButton<RuntimeMode>(SwiftEnum<RuntimeMode>.AllCases.Select(x => (x, x.Title)).ToArray(), new Binding<RuntimeMode>(() => s.DefaultRuntimeMode, value => s.DefaultRuntimeMode = value), symbol: x => x.Symbol), mode.Summary)); // :323-325
            root.Children.Add(new ChromeSection("New threads", () => new ChromeCard(() => Rows(
                new ChromeRow("Provider and model", () => new Border(), "A new thread follows the one you are in: its provider, model, effort and Hydra pair. With no thread open it starts on the provider you last chose."), // :319-321
                permission,
                new ChromeRow("Workspace", () => new ChromeVisualPicker<WorkspaceMode, Control>( // :327-330; Provider.swift:208-213 titles
                    [(WorkspaceMode.Local, "Local"), (WorkspaceMode.Worktree, "New worktree")],
                    new Binding<WorkspaceMode>(() => s.DefaultWorkspaceMode, value => s.DefaultWorkspaceMode = value), mode => SwiftImage.SystemName(mode == WorkspaceMode.Local ? "folder" : "arrow.triangle.branch")), "Where a new thread makes its changes")))));
            var chime = Toggle("Chime when a turn finishes", "A soft chime, whether or not the thread is in view", () => s.ChimeWhenFinished, value => s.ChimeWhenFinished = value); // :358-360
            var lastChime = s.ChimeWhenFinished;
            chime.Track(() => s.ChimeWhenFinished, value => { if (value && !lastChime) FinishChime.Play(); lastChime = value; }); // :361-364
            root.Children.Add(new ChromeSection("Conversation", () => new ChromeCard(() => Rows(
                Toggle("Show thinking", "The working line opens to the agent's thinking", () => s.ShowReasoning, value => s.ShowReasoning = value), // :336-338
                Toggle("Concise AI replies", "Built-in guidance for shorter replies, useful code comments, and focused work, including Hydra heads. Changes apply on the next message.", () => s.AntiSlopEnabled, value => s.AntiSlopEnabled = value), // :340-342
                new ChromeRow("Working line", () => new ChromeVisualPicker<bool, Control>([(true, "Card"), (false, "Line")], new Binding<bool>(() => s.ShowsWorkingCard, value => s.ShowsWorkingCard = value), card => SwiftImage.SystemName(card ? "rectangle.fill" : "minus")), "A card with the turn's progress bar, or a plain line"), // :344-348
                Toggle("Recent downloads", "The attach button offers recent downloads first", () => s.RecentDownloadsPicker, value => s.RecentDownloadsPicker = value), // :350-352
                Toggle("Notify when a turn finishes", null, () => s.NotifyWhenFinished, value => s.NotifyWhenFinished = value), // :354-356
                chime,
                Toggle("Confirm before deleting threads", null, () => s.ConfirmBeforeDeleting, value => s.ConfirmBeforeDeleting = value), // :366-368
                Toggle("Continue after a usage limit", "When the provider's limit is spent, the chat waits for the reset and then tells the agent to carry on", () => s.AutoContinueAfterLimit, value => s.AutoContinueAfterLimit = value), // :370-372
                Toggle("Show a usage panel", "The plan's limits and credits in a floating panel beside every chat, for the model in use or a pair's two; the usage popover's pop-out button switches it on as well, and it keeps the corner it was last dragged to", () => s.ShowsUsagePanel, value => s.ShowsUsagePanel = value))))); // :374-379
            root.Children.Add(new ChromeSection("Projects", () => new ChromeCard(() => Rows(
                Toggle("Projects", "Activates every project at once unless explicitly overridden", () => s.ProjectsEnabled, value => s.ProjectsEnabled = value), // :384-386
                new ChromeRow("Marks", () => new ProjectMarksRow(), "An emoji or symbol for each project, drawn before the thread's name in the activity list and on the project's own row"))))); // :388-390
            var finish = new Border();
            finish.Track(() => s.ThreadFinishAction, action => finish.Child = new ChromeRow("Done with a thread", () => new ChromeVisualPicker<ThreadFinishAction, Control>(SwiftEnum<ThreadFinishAction>.AllCases.Select(x => (x, x.Title)).ToArray(), new Binding<ThreadFinishAction>(() => s.ThreadFinishAction, value => s.ThreadFinishAction = value), value => SwiftImage.SystemName(value == ThreadFinishAction.Settle ? "checkmark" : "archivebox")), action.Detail)); // :395-399
            var settle = Toggle("Sound when settling", "A small note as the thread settles", () => s.SettleSound, value => s.SettleSound = value); // :401-403
            var lastSettle = s.SettleSound;
            settle.Track(() => s.SettleSound, value => { if (value && !lastSettle) SettleChime.Play(); lastSettle = value; }); // :404-406
            settle.Track(() => s.ThreadFinishAction, value => { settle.IsEnabled = value == ThreadFinishAction.Settle; settle.Opacity = value == ThreadFinishAction.Settle ? 1 : 0.5; }); // :408-409
            root.Children.Add(new ChromeSection("Finished threads", () => new ChromeCard(() => Rows(finish, settle)))); // :393-411
        });
    }
}

/// <summary>:417-458 <c>private struct AppearanceSettingsPage: View</c>.</summary>
internal sealed class AppearanceSettingsPage : Decorator
{
    // :420-457 body
    public AppearanceSettingsPage()
    {
        var host = new Border(); Child = host;
        this.Track(() => AppEnvironment.GetAppModel(this), model =>
        {
            var settings = model.Settings;
            var theme = new ChromeRow("Theme", () => new ThemePickerButton(new Binding<AppTheme>(() => settings.Theme, value => settings.Theme = value))); // :424-426
            theme.Track(() => settings.Theme, value => SettingsRowDetails.Update(theme, value.Detail()));
            var sidebar = new ChromeRow("Sidebar", () => new ChromeVisualPicker<SidebarMode, Control>(SwiftEnum<SidebarMode>.AllCases.Select(mode => (mode, mode.Title)).ToArray(), new Binding<SidebarMode>(() => settings.SidebarMode, value => settings.SidebarMode = value), mode => SwiftImage.SystemName(mode == SidebarMode.Column ? "sidebar.left" : mode == SidebarMode.Floating ? "macwindow.on.rectangle" : "macwindow"))); // :432-436
            sidebar.Track(() => settings.SidebarMode, value => SettingsRowDetails.Update(sidebar, value.Detail));
            var activity = new ChromeRow("Activity list", () => new ChromeVisualPicker<ActivityThreadStyle, Control>(SwiftEnum<ActivityThreadStyle>.AllCases.Select(style => (style, style.Title)).ToArray(), new Binding<ActivityThreadStyle>(() => settings.ActivityThreadStyle, value => settings.ActivityThreadStyle = value), style => SwiftImage.SystemName(style == ActivityThreadStyle.Icon ? "list.bullet" : "text.alignleft"))); // :438-442
            activity.Track(() => settings.ActivityThreadStyle, value => SettingsRowDetails.Update(activity, value.Detail));
            var open = new ChromeRow("Open the sidebar at launch", () => new SettingsSwitch(new Binding<bool>(() => model.Sidebar.StartsOpen, value => model.Sidebar.StartsOpen = value)), "Collapsed otherwise; the list button in the toolbar shows and hides it any time"); // :444-448
            open.Track(() => settings.SidebarMode, mode => { open.IsEnabled = mode != SidebarMode.PanelOnly; open.Opacity = mode == SidebarMode.PanelOnly ? 0.5 : 1; }); // :449-450
            var rows = new StackPanel();
            foreach (var row in new Control[] { theme, new ChromeRow("Transparency", () => new BackdropOpacitySlider(new Binding<double>(() => settings.BackdropOpacity, value => settings.BackdropOpacity = value)), "How much shows through the window"), sidebar, activity, open }) { if (rows.Children.Count > 0) rows.Children.Add(new ChromeRowDivider()); rows.Children.Add(row); }
            host.Child = new ChromeSection("Appearance", () => new StackPanel { Spacing = Chrome.SectionHeaderSpacing, Children = { new ChromeCard(() => rows), new WallpaperCard(), new AppZoomCard(new Binding<long>(() => settings.AppZoom, value => settings.AppZoom = value)) } }); // :452-454
        });
    }
}

// Updates a ChromeRow's own detail slot without replacing its editing control or popover.
internal static class SettingsRowDetails
{
    public static void Update(ChromeRow row, string? detail)
    {
        var labels = (StackPanel)((Grid)((Border)row.Child!).Child!).Children[0];
        if (string.IsNullOrEmpty(detail)) { if (labels.Children.Count > 1) labels.Children.RemoveAt(1); return; }
        if (labels.Children.Count > 1) ((TextBlock)labels.Children[1]).Text = detail;
        else labels.Children.Add(new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap, Foreground = Chrome.SecondaryText.Brush(row) }.Font(SwiftFont.System(11)));
    }
}

// PORT-ONLY mapping of animatable scalar view properties to the existing Swift animation/frame clocks.
internal sealed class SettingsScalarAnimation
{
    private readonly Action<double> apply;
    private IFrameSource? frames;
    private Action<TimeSpan>? pending;
    public double Value { get; private set; }
    public SettingsScalarAnimation(Control owner, double initial, Action<double> apply)
    {
        Value = initial; this.apply = apply; owner.DetachedFromVisualTree += (_, _) => Stop();
    }
    public void Set(double target, Animation? animation)
    {
        Stop(); var from = Value;
        if (animation is null || from == target) { Value = target; apply(target); return; }
        var start = SwiftClock.Now; frames = FrameClock.Source.Current;
        Action<TimeSpan>? tick = null;
        tick = _ =>
        {
            var elapsed = (SwiftClock.Now - start).TotalSeconds; var finished = (SwiftClock.Now - start).Ticks + TimeSpan.TicksPerMillisecond >= SwiftTask.Seconds(animation.Duration).Ticks; // ED-117 (2): frame-clock completion tolerance.
            Value = finished ? target : from + (target - from) * animation.Progress(elapsed); apply(Value);
            if (finished) pending = null; else frames.RequestFrame(tick!);
        };
        pending = tick; apply(from); frames.RequestFrame(tick);
    }
    private void Stop() { if (pending is not null) frames?.CancelFrame(pending); pending = null; frames = null; }
}

/// <summary>:462-517 <c>private struct AppZoomCard: View</c>: the app-wide zoom.</summary>
internal sealed class AppZoomCard : Decorator
{
    // :463 @Binding var percent: Int; :467-516 body
    public AppZoomCard(Binding<long> percent)
    {
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        Button Command(string symbol, string help, Action action, SwiftFont.Weight weight = SwiftFont.Weight.Regular)
        {
            var button = PlainControls.Button(SwiftImage.SystemName(symbol).Font(SwiftFont.System(11, weight)).ForegroundStyle(Chrome.SecondaryText), action); button.Width = 22; button.Height = 22; ToolTip.SetTip(button, help); return button;
        }
        var smaller = Command("textformat.size.smaller", "Zoom out", () => percent.Value = AppZoom.Next((int)percent.Value, increasing: false)); // :475-483
        var label = new TextBlock { MinWidth = 46, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Chrome.PrimaryText.Brush(this) }.Font(SwiftFont.System(11, SwiftFont.Weight.Medium).MonospacedDigit()); // :483-486
        var larger = Command("textformat.size.larger", "Zoom in", () => percent.Value = AppZoom.Next((int)percent.Value, increasing: true)); // :491-499
        var reset = Command("arrow.counterclockwise", "Back to the stock size", () => percent.Value = 100, SwiftFont.Weight.Medium); // :501-510
        controls.Children.Add(smaller); controls.Children.Add(label); controls.Children.Add(larger); controls.Children.Add(reset);
        var row = new ChromeRow("Zoom", () => controls); Child = new ChromeCard(() => row);
        var primed = false; var fade = new SettingsScalarAnimation(reset, 0, value => reset.Opacity = Math.Clamp(value, 0, 1));
        this.Track(() => AppZoom.Normalized((int)percent.Value), value => { label.Text = value + "%"; SettingsRowDetails.Update(row, value == 100 ? "The stock size" : value + "% of the stock size"); smaller.IsEnabled = value > AppZoom.Levels[0]; larger.IsEnabled = value < AppZoom.Levels[^1]; reset.IsEnabled = value != 100; fade.Set(value == 100 ? 0 : 1, primed ? Animation.Snappy(0.2) : null); primed = true; }); // :467-516
    }
}

/// <summary>:521-564 <c>private struct BackdropOpacitySlider: View</c>.</summary>
internal sealed class BackdropOpacitySlider : Decorator
{
    // :522 @Binding var value: Double; :526-563 body
    public BackdropOpacitySlider(Binding<double> value)
    {
        var slider = new Slider { Minimum = 0, Maximum = 1, Width = 140, Value = value.Value }; // :535-537
        var updatingSlider = false;
        var thumb = new SettingsScalarAnimation(slider, value.Value, v => { updatingSlider = true; try { slider.Value = v; } finally { updatingSlider = false; } });
        slider.PropertyChanged += (_, change) => { if (!updatingSlider && change.Property == Slider.ValueProperty && value.Value != slider.Value) value.Value = slider.Value; };
        slider.Track(() => value.Value, v => thumb.Set(v, Transaction.Current));
        var clear = SwiftImage.SystemName("circle.dotted").Font(SwiftFont.System(11)).ForegroundStyle(Chrome.SecondaryText); ToolTip.SetTip(clear, "Clear");
        var solid = SwiftImage.SystemName("circle.fill").Font(SwiftFont.System(11)).ForegroundStyle(Chrome.SecondaryText); ToolTip.SetTip(solid, "Solid");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { clear, slider, solid } }; // :528-539
        // :551-552 .frame(width: 22, height: 22).contentShape(.rect): DEFECT FIX (UI-SETTINGS-018), the whole 22pt square is the hit target; the bare glyph was hit
        // only on its drawn pixels (the centre of the arrow is empty).
        var reset = PlainControls.Button(new Panel { Background = Brushes.Transparent, Children = { SwiftImage.SystemName("arrow.counterclockwise").Font(SwiftFont.System(11, SwiftFont.Weight.Medium)).ForegroundStyle(Chrome.SecondaryText) } }, () => Transaction.WithAnimation(Animation.Snappy(0.2), () => value.Value = 0.5));
        reset.Width = 22; reset.Height = 22; reset.HorizontalAlignment = HorizontalAlignment.Left; reset.VerticalAlignment = VerticalAlignment.Center; reset.RenderTransform = new TranslateTransform(-28, 0); ToolTip.SetTip(reset, "Back to the stock look"); // :540-557
        var primed = false; var shown = false;
        var fade = new SettingsScalarAnimation(reset, 0, v => { var opacity = Math.Clamp(v, 0, 1); reset.Opacity = opacity; reset.RenderTransform = new TranslateTransform(-28 + 6 * (1 - opacity), 0); if (opacity == 0 && !shown) reset.IsVisible = false; });
        reset.Track(() => Math.Abs(value.Value - 0.5) >= 0.005, offStock => { shown = offStock; reset.IsEnabled = offStock; if (offStock) reset.IsVisible = true; fade.Set(offStock ? 1 : 0, primed ? Animation.Snappy(0.2) : null); primed = true; }); // :524,558-562
        // :543 `.overlay(alignment: .leading)`: DEFECT FIX (UI-SETTINGS-018). The reset sat in a plain Panel, so its 22pt height grew the group from 14
        // to 22 and moved the slider 4pt up whenever the reset showed; an overlay takes no layout room (:540-542).
        Child = new LeadingOverlayPanel { Children = { row, reset } }; AutomationProperties.SetName(this, "Window transparency"); // :563
    }
}

/// <summary>SwiftUI <c>.overlay(alignment: .leading)</c> (SettingsView.swift:543-559): the panel is exactly the first child's size; the later
/// children are laid against its leading edge, centred vertically, at their own size, and may spill past it.</summary>
internal sealed class LeadingOverlayPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count == 0) return default;
        Children[0].Measure(availableSize);
        for (var i = 1; i < Children.Count; i++) Children[i].Measure(Size.Infinity);
        return Children[0].DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count == 0) return finalSize;
        Children[0].Arrange(new Rect(finalSize));
        for (var i = 1; i < Children.Count; i++)
        {
            var size = Children[i].DesiredSize;
            Children[i].Arrange(new Rect(0, (finalSize.Height - size.Height) / 2, size.Width, size.Height));
        }
        return finalSize;
    }
}

/// <summary>:566-580 <c>private struct ProvidersRefreshButton: View</c>.</summary>
internal sealed class ProvidersRefreshButton : Decorator
{
    // :569-579 body
    public ProvidersRefreshButton()
    {
        var holder = new Border(); Child = new ChromeCapsule(() => holder); // :569-570
        this.Track(() => AppEnvironment.GetAppModel(this).Providers.IsRefreshing, refreshing => holder.Child = new ChromeIconButton("arrow.clockwise", refreshing ? "Checking providers\u2026" : "Check providers again", () => MainActor.Spawn(async () => await AppEnvironment.GetAppModel(this).Providers.RefreshEverything()), isEnabled: !refreshing)); // :571-579
    }
}

/// <summary>:585-615 <c>private struct ProvidersSettingsPage: View</c>.</summary>
internal sealed class ProvidersSettingsPage : Decorator
{
    // :587-590
    private sealed class ViewState : MainActorModel
    {
        private ValueMap<ProviderKind, bool> expansion = ValueMap<ProviderKind, bool>.Empty;
        public ValueMap<ProviderKind, bool> Expansion { get => Read(expansion); set => Set(ref expansion, value); }
    }
    // :592-614 body
    public ProvidersSettingsPage()
    {
        var state = new ViewState();
        var stack = new StackPanel { Spacing = Chrome.SectionSpacing }; Child = stack; // :598-600
        var sections = new ForEach<ProviderKind, ProviderKind>(stack, provider => provider, provider => new ProviderSettingsSection(provider,
            new Binding<bool>(() => { if (state.Expansion.TryGetValue(provider, out var value)) return value; var registry = AppEnvironment.GetAppModel(this).Providers; _ = registry.Statuses; return registry.Status(provider).ProviderStatusAuth is ProviderStatus.Auth.SignedIn; },
                value => state.Expansion = state.Expansion.Mutating(map => map[provider] = value))), (_, _) => { }); // :605-610
        this.Track(() => AppEnvironment.GetAppModel(this).Providers.Statuses, _ =>
        {
            bool Configured(ProviderKind provider) => AppEnvironment.GetAppModel(this).Providers.Status(provider).ProviderStatusAuth is ProviderStatus.Auth.SignedIn; // :593-597
            sections.Apply(SwiftEnum<ProviderKind>.AllCases.Where(Configured).Concat(SwiftEnum<ProviderKind>.AllCases.Where(provider => !Configured(provider))).ToArray()); // :604
        });
    }
}

/// <summary>:617-824 <c>private struct ProviderSettingsSection: View</c>: one provider's settings.</summary>
internal sealed class ProviderSettingsSection : Decorator
{
    private readonly ProviderKind provider;
    private TaskHandle? keyCheck; // :627
    // :624-626
    private sealed class ViewState : MainActorModel
    {
        private string binaryPath = "", apiKey = "";
        private bool showsAPIKey;
        public string BinaryPath { get => Read(binaryPath); set => Set(ref binaryPath, value); }
        public string ApiKey { get => Read(apiKey); set => Set(ref apiKey, value); }
        public bool ShowsAPIKey { get => Read(showsAPIKey); set => Set(ref showsAPIKey, value); }
    }
    // :619 let provider, :622 @Binding var isExpanded; :644-823 body
    public ProviderSettingsSection(ProviderKind provider, Binding<bool> isExpanded)
    {
        this.provider = provider;
        DetachedFromVisualTree += (_, _) => keyCheck?.Cancel(); // :627,788-794 debounce belongs to this section
        var state = new ViewState();
        var cardRows = new StackPanel();
        var reveal = new ChromeRevealLayout(0) { ClipToBounds = true, Children = { new ChromeCard(() => cardRows) } }; // :648-649,763
        var motion = new SettingsScalarAnimation(reveal, 0, value => reveal.Fraction = value);
        Child = new StackPanel { Spacing = Chrome.SectionHeaderSpacing, Children = { new CollapsibleProviderHeader(provider, isExpanded, "provider settings"), reveal } }; // :646-647
        this.Track(() => isExpanded.Value, expanded => // :764-766
        {
            motion.Set(expanded ? 1 : 0, Transaction.Current); reveal.IsEnabled = expanded; reveal.IsHitTestVisible = expanded;
            AutomationProperties.SetAccessibilityView(reveal, expanded ? AccessibilityView.Content : AccessibilityView.Raw);
        });
        this.Track(() => AppEnvironment.GetAppModel(this), model =>
        {
            state.BinaryPath = model.Settings.BinaryPath(provider); if (provider.AcceptsAPIKey) state.ApiKey = model.Settings.ApiKeyInput(provider); // :768-771
            var statusRow = new Grid { ColumnDefinitions = new ColumnDefinitions("22,*,Auto"), ColumnSpacing = 12, Margin = new Thickness(16, 11, Chrome.RowControlTrailingPadding, 11) }; // :650,653,673-675
            var icon = new ProviderIcon { Provider = provider, Size = 18 }; icon.SetValue(Avalonia.Controls.Documents.TextElement.ForegroundProperty, Chrome.PrimaryText.Brush(icon)); statusRow.Children.Add(icon); // :651-652 .foregroundStyle(Chrome.primaryText)
            var labels = new StackPanel { Spacing = 2 }; Grid.SetColumn(labels, 1); statusRow.Children.Add(labels); // :654
            var toggle = new SettingsSwitch(new Binding<bool>(() => model.Settings.IsEnabled(provider), value => model.Settings.SetEnabled(value, provider))); Grid.SetColumn(toggle, 2); statusRow.Children.Add(toggle); // :668-671
            cardRows.Children.Add(statusRow);
            var usage = new StackPanel { Spacing = 14, Margin = new Thickness(16, 12) }; // :676-687
            var usageSlot = new StackPanel(); cardRows.Children.Add(usageSlot);
            statusRow.Track(() => { _ = model.Providers.Statuses; return model.Providers.Status(provider); }, status =>
            {
                labels.Children.Clear(); labels.Children.Add(new TextBlock { Text = status.IsChecking ? "Checking\u2026" : status.Summary, Foreground = Chrome.PrimaryText.Brush(this) }.Font(SwiftFont.System(13))); // :655-656
                var detailText = !provider.IsAPIKeyBased && status.Version is { } version ? "Version " + version : provider.IsAPIKeyBased && status.ApiKeyConfigured && provider.ApiHost is { } host ? "Native API \u00b7 " + host : null; // :657-664
                if (detailText is not null) labels.Children.Add(new TextBlock { Text = detailText, Foreground = Chrome.SecondaryText.Brush(this) }.Font(SwiftFont.System(11)));
            });
            bool ShowsUsage() { _ = model.Providers.Statuses; var status = model.Providers.Status(provider); return (PlanLimitsReader.ExposesLimits(provider) || CreditsReader.ExposesCredits(provider)) && status.IsInstalled && status.ProviderStatusAuth is not ProviderStatus.Auth.SignedOut; } // :629-634
            usageSlot.Track(ShowsUsage, shows =>
            {
                usageSlot.Children.Clear();
                if (!shows) return;
                usage.Children.Clear();
                if (PlanLimitsReader.ExposesLimits(provider)) usage.Children.Add(new Marrow.App.Views.Usage.PlanLimitsView(provider)); // :679-680
                if (CreditsReader.ExposesCredits(provider)) usage.Children.Add(new Marrow.App.Views.Usage.CreditsView(provider)); // :682-683
                usageSlot.Children.Add(new ChromeRowDivider()); usageSlot.Children.Add(usage);
                model.Providers.RefreshPlanLimits(provider); model.Providers.RefreshCredits(provider); // :772-779
            });
            if (provider.AcceptsAPIKey)
            {
                cardRows.Children.Add(new ChromeRowDivider()); // :689-690
                var keyField = PlainControls.TextField("sk-\u2026", new Binding<string>(() => state.ApiKey, value =>
                {
                    state.ApiKey = value;
                    if (SwiftString.Equal(value, model.Settings.ApiKeyInput(provider))) return; // :786
                    model.Settings.SetAPIKeyInput(value, provider); keyCheck?.Cancel(); // :787-788
                    keyCheck = MainActor.Spawn(async () => { await SwiftTask.TrySleep(TimeSpan.FromMilliseconds(600)); if (SwiftTask.IsCancelled) return; await model.Providers.Refresh(provider); await model.Providers.LoadCatalog(provider, force: true); }); // :789-794
                }));
                keyField.PasswordChar = '\u2022'; keyField.Width = 200; SwiftFont.System(12, design: SwiftFont.Design.Monospaced).Apply(keyField); // :700-707
                MaskedPresenter(keyField); // SecureField must mask its actual text presenter, not just the box property.
                var eyeLabel = new Border { Background = Brushes.Transparent, Child = SwiftImage.SystemName("eye").Font(SwiftFont.System(12)).ForegroundStyle(Chrome.SecondaryText) }; // :708-717: the plain button's label frame is the hit surface.
                var eye = PlainControls.Button(eyeLabel, () => state.ShowsAPIKey = !state.ShowsAPIKey); // :708-715
                eye.Track(() => state.ShowsAPIKey, shown => { keyField.PasswordChar = shown ? '\0' : '\u2022'; eyeLabel.Child = SwiftImage.SystemName(shown ? "eye.slash" : "eye").Font(SwiftFont.System(12)).ForegroundStyle(Chrome.SecondaryText); ToolTip.SetTip(eye, shown ? "Hide the API key" : "Show the API key"); }); // :711,716
                // DEVIATION OD-0012 G2: Credential Manager replaces Keychain.
                var keyDetail = provider.IsAPIKeyBased ? "Stored in your Credential Manager. Get one at " + (provider.ApiKeySource ?? "the provider dashboard") + "." : "Instead of `" + provider.LoginCommand + "`: a Studio key from " + (provider.ApiKeySource ?? "the provider dashboard") + ", stored in your Credential Manager and handed to the CLI."; // :693-695
                var keyControls = new StackPanel { Children = { new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { keyField, eye } } } };
                keyControls.Track(() => { _ = StoredKey(model.Settings); return model.Settings.APIKeyRefusal(provider); }, refusal =>
                {
                    if (keyControls.Children.Count > 1) keyControls.Children.RemoveAt(1);
                    if (refusal is not null) keyControls.Children.Add(new TextBlock { Text = refusal, TextWrapping = TextWrapping.Wrap }); // DEVIATION OD-0012 E5: exact line beneath the field; no unspecified type/colour tokens invented.
                });
                cardRows.Children.Add(new ChromeRow(provider.IsAPIKeyBased ? "API key" : "API key (optional)", () => keyControls, keyDetail)); // :691-717
                keyField.Track(() => StoredKey(model.Settings), value => { if (!SwiftString.Equal(state.ApiKey, value)) state.ApiKey = value; }); // :796-799
            }
            if (!provider.IsAPIKeyBased)
            {
                cardRows.Children.Add(new ChromeRowDivider());
                var field = PlainControls.TextField(provider.ExecutableName, new Binding<string>(() => state.BinaryPath, value => { state.BinaryPath = value; if (!SwiftString.Equal(value, model.Settings.BinaryPath(provider))) model.Settings.SetBinaryPath(value, provider); })); // :732-735,780-783
                field.Width = 200; SwiftFont.System(12, design: SwiftFont.Design.Monospaced).Apply(field);
                var row = new ChromeRow("Binary path", () => field); row.Track(() => { _ = model.Providers.Statuses; return model.Providers.Status(provider).Executable?.Path; }, value => SettingsRowDetails.Update(row, value)); cardRows.Children.Add(row); // :732-736
            }
            var installSlot = new StackPanel(); cardRows.Children.Add(installSlot);
            installSlot.Track(() => { _ = model.Providers.Statuses; return model.Providers.Status(provider); }, status =>
            {
                installSlot.Children.Clear();
                if (provider.IsAPIKeyBased && !status.IsInstalled) { installSlot.Children.Add(new ChromeRowDivider()); installSlot.Children.Add(new ChromeRow("Get a key", () => Link(provider.GetKeyLinkText, provider.InstallURL), provider.DisplayName + " needs an API key. Nothing to install.")); } // :719-727
                if (provider.IsAPIKeyBased) return;
                if (!status.IsInstalled) { installSlot.Children.Add(new ChromeRowDivider()); installSlot.Children.Add(new ChromeRow("Install", () => Link("Get " + provider.DisplayName, provider.InstallURL), provider.DisplayName + " was not found on your PATH.")); } // :738-745
                else if (status.ProviderStatusAuth is not ProviderStatus.Auth.SignedIn && !status.IsChecking) { installSlot.Children.Add(new ChromeRowDivider()); installSlot.Children.Add(new ChromeRow("Sign in", () => PlainControls.Button(new TextBlock { Text = "Login", Height = Chrome.RowButtonLabelHeight }, () => Workspace.OpenTerminal(provider.LoginCommand)).ChromeGlassCapsule(), "Click here to login again.")); } // :638-641,747-756
            });
        });
    }

    // SecureField's native masking and editing presenter, retaining the shared field's binding and prompt.
    // The source's OS-defined roundedBorder appearance remains STOP #6 rather than guessed geometry.
    private static void MaskedPresenter(TextBox box)
    {
        box.Template = new FuncControlTemplate<TextBox>((owner, scope) =>
        {
            var prompt = new TextBlock { IsHitTestVisible = false, [!TextBlock.TextProperty] = owner[!TextBox.PlaceholderTextProperty], [!TextBlock.ForegroundProperty] = owner[!TextBox.PlaceholderForegroundProperty] };
            var presenter = new TextPresenter
            {
                Name = "PART_TextPresenter", [!TextPresenter.TextProperty] = owner[!TextBox.TextProperty],
                [!TextPresenter.PasswordCharProperty] = owner[!TextBox.PasswordCharProperty],
                [!TextPresenter.SelectionStartProperty] = owner[!TextBox.SelectionStartProperty], [!TextPresenter.SelectionEndProperty] = owner[!TextBox.SelectionEndProperty],
                [!TextPresenter.CaretIndexProperty] = owner[!TextBox.CaretIndexProperty],
            }.RegisterInNameScope(scope);
            void ShowPrompt() => prompt.IsVisible = string.IsNullOrEmpty(owner.Text);
            owner.PropertyChanged += (_, change) => { if (change.Property == TextBox.TextProperty) ShowPrompt(); }; ShowPrompt();
            // :698-707: an empty SecureField still receives pointer focus and text input.
            return new Panel { Background = Brushes.Transparent, Children = { prompt, presenter } };
        });
    }

    // :796-799. ApiKeyInput currently reads backing fields; observe the corresponding stored properties too.
    private string StoredKey(AppSettings settings)
    {
        _ = settings.ApiKeyInput(provider);
        return provider switch
        {
            ProviderKind.Deepseek => settings.DeepseekAPIKeyInput, ProviderKind.Meta => settings.MetaAPIKeyInput,
            ProviderKind.Zai => settings.ZaiAPIKeyInput, ProviderKind.Commandcode => settings.CommandcodeAPIKeyInput,
            ProviderKind.OpenRouter => settings.OpenRouterAPIKeyInput, // PORT-ONLY (ED-234)
            _ => "",
        };
    }

    // :722-726,741-745
    private static Control Link(string title, SwiftURL url) => PlainControls.Button(new TextBlock { Text = title, Height = Chrome.RowButtonLabelHeight }, () => Marrow.Services.Port.Workspace.Open.Current(url)).ChromeGlassCapsule();
}

/// <summary>:826-872 <c>private struct SourceControlSettingsPage: View</c>.</summary>
internal sealed class SourceControlSettingsPage : Decorator
{
    // :829-871 body
    public SourceControlSettingsPage()
    {
        var sections = new StackPanel { Spacing = Chrome.SectionSpacing }; Child = sections;
        this.Track(() => AppEnvironment.GetAppModel(this), model =>
        {
            var settings = model.Settings;
            sections.Children.Add(new ChromeSection("Text generation", () => new ChromeCard(() => new ChromeRow("Thread titles and commit messages", () =>
                new GlassPickerButton<TextGenerationChoice>(SwiftEnum<TextGenerationChoice>.AllCases.Select(choice => (choice, choice.Title)).ToArray(), new Binding<TextGenerationChoice>(() => settings.TextGeneration, value => settings.TextGeneration = value), asset: choice => choice == TextGenerationChoice.Claude ? "claude" : choice == TextGenerationChoice.Codex ? "codex" : null),
                "Automatic uses the thread's own provider. Claude uses Haiku, Codex your default model.")))); // :808-816
            var editor = PlainControls.TextField("", new Binding<string>(() => settings.CommitInstructions, value => settings.CommitInstructions = value)); editor.AcceptsReturn = true; editor.TextWrapping = TextWrapping.Wrap; editor.MinHeight = 90; SwiftFont.System(SwiftFont.TextStyle.Body).Apply(editor); // :821-824
            editor.Template = new FuncControlTemplate<TextBox>((owner, scope) => new ScrollViewer // :821-824. Plain transparent multiline editor, with its native caret/selection and scrolling.
            {
                Template = new FuncControlTemplate<ScrollViewer>((scroll, names) => new ScrollContentPresenter
                {
                    Name = "PART_ContentPresenter", CanHorizontallyScroll = false, CanVerticallyScroll = true,
                    [!ContentPresenter.ContentProperty] = scroll[!ContentControl.ContentProperty],
                    Background = Brushes.Transparent,
                }.RegisterInNameScope(names)),
                Name = "PART_ScrollViewer", Background = Brushes.Transparent, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
                Content = new TextPresenter
                {
                    Name = "PART_TextPresenter", [!TextPresenter.TextProperty] = owner[!TextBox.TextProperty], [!TextPresenter.TextWrappingProperty] = owner[!TextBox.TextWrappingProperty],
                    [!TextPresenter.CaretIndexProperty] = owner[!TextBox.CaretIndexProperty], [!TextPresenter.SelectionStartProperty] = owner[!TextBox.SelectionStartProperty], [!TextPresenter.SelectionEndProperty] = owner[!TextBox.SelectionEndProperty],
                }.RegisterInNameScope(scope),
            }.RegisterInNameScope(scope));
            var placeholder = new TextBlock { Text = "For example: use conventional commit prefixes", HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(5, 0), Foreground = Chrome.SecondaryText.Brush(this), IsHitTestVisible = false }.Font(SwiftFont.System(SwiftFont.TextStyle.Body)); // :820 ZStack(alignment:.topLeading), :825-830.
            placeholder.Track(() => settings.CommitInstructions.Length == 0, value => placeholder.IsVisible = value);
            sections.Children.Add(new ChromeSection("Commit message instructions", () => new ChromeCard(() => new Border { Padding = new Thickness(12), Child = new Panel { Children = { editor, placeholder } } }))); // :818-834
            sections.Children.Add(new ChromeSection("Worktrees and pull requests", () => new ChromeCard(() => new StackPanel { Children =
            {
                new ChromeRow("Worktrees", () => PlainControls.Button(new TextBlock { Text = "Reveal" }, () => Workspace.RevealInFileExplorer(Storage.WorktreesDirectory)).ChromeGlassCapsule(), Storage.WorktreesDirectory), // :838-840
                new ChromeRowDivider(), // :842
                new ChromeRow("Pull requests", () => new Border(), "Opened with the GitHub CLI (gh) or GitLab CLI (glab) from your PATH."), // :843-845
            } })));
        });
    }
}

/// <summary>:877-909 <c>private struct ArchiveDeleteAllButton: View</c>.</summary>
internal sealed class ArchiveDeleteAllButton : Decorator
{
    private readonly PresentedState state = new(); // :855
    // :881-908 body
    public ArchiveDeleteAllButton()
    {
        var capsule = new ChromeTextButton("trash", "Delete all", "Delete every archived thread", () =>
        {
            var model = AppEnvironment.GetAppModel(this); if (model.Settings.ConfirmBeforeDeleting) state.IsPresented = true; else model.DeleteArchivedThreads();
        }, isDestructive: true);
        var popup = PlainControls.Popover(capsule, new Binding<bool>(() => state.IsPresented, value => state.IsPresented = value), () =>
        {
            var model = AppEnvironment.GetAppModel(this); var count = model.ArchivedThreads.Count;
            var content = new PopoverMenu(() => new StackPanel { Children =
            {
                new PopoverSectionHeader(count == 1 ? "Delete the archived thread?" : "Delete all archived threads?"),
                new PopoverNote(count == 1 ? "Its history will be removed. Files in your project stay as they are." : $"The histories of all {count} threads will be removed. Files in your projects stay as they are."),
                new PopoverDivider(), new PopoverItem(count == 1 ? "Delete thread" : $"Delete {count} threads", () => model.DeleteArchivedThreads(), symbol: "trash", isDestructive: true),
            } }); Popovers.SetClosePopover(content, () => state.IsPresented = false); return content;
        }); Child = new Panel { Children = { capsule, popup } };
        capsule.Track(() => AppEnvironment.GetAppModel(this).ArchivedThreads.Count > 0, enabled => capsule.IsEnabled = enabled); // :857-885
    }
}

// Source-shaped @State used by the popovers declared in this unit.
internal sealed class PresentedState : MainActorModel
{
    private bool isPresented, isHovering;
    public bool IsPresented { get => Read(isPresented); set => Set(ref isPresented, value); }
    public bool IsHovering { get => Read(isHovering); set => Set(ref isHovering, value); }
}

/// <summary>:912-959 <c>private struct ArchiveSettingsPage: View</c>.</summary>
internal sealed class ArchiveSettingsPage : Decorator
{
    private ChatThread? pendingDeletion; // :893-894
    private TaskHandle? confirmation;
    // :919-958 body
    public ArchiveSettingsPage()
    {
        var rows = new StackPanel(); Child = new ChromeCard(() => rows); // :896
        var entries = new ForEach<ChatThread, Guid>(rows, thread => thread.Id, MakeRow, (row, thread) =>
        {
            var labels = (StackPanel)((Grid)((Border)((ChromeRow)row).Child!).Child!).Children[0]; ((TextBlock)labels.Children[0]).Text = thread.Title;
            SettingsRowDetails.Update((ChromeRow)row, AppEnvironment.GetAppModel(this).Project(thread.ProjectID)?.Name);
        });
        this.Track(() =>
        {
            var model = AppEnvironment.GetAppModel(this); var threads = model.ArchivedThreads;
            foreach (var thread in threads) _ = model.Project(thread.ProjectID)?.Name; // :912-934 each row's detail is read in the observed body
            return threads;
        }, threads =>
        {
            entries.Apply(threads); var items = rows.Children.OfType<ChromeRow>().ToArray(); rows.Children.Clear();
            for (var i = 0; i < items.Length; i++) { if (i > 0) rows.Children.Add(new ChromeRowDivider()); rows.Children.Add(items[i]); }
        });
        DetachedFromVisualTree += (_, _) => confirmation?.Cancel();
    }
    // :898-918
    private Control MakeRow(ChatThread thread)
    {
        var model = AppEnvironment.GetAppModel(this);
        var restore = PlainControls.Button(new TextBlock { Text = "Restore" }.Font(SwiftFont.System(12)), () => model.Unarchive(thread.Id));
        var delete = PlainControls.Button(new TextBlock { Text = "Delete", Foreground = Chrome.Danger.Brush(this) }.Font(SwiftFont.System(12)), () =>
        {
            if (!model.Settings.ConfirmBeforeDeleting) { model.Delete(thread.Id); return; }
            pendingDeletion = thread; PresentConfirmation();
        });
        return new ChromeRow(thread.Title, () => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { restore.ChromeGlassCapsule(), delete.ChromeGlassCapsule() } }, model.Project(thread.ProjectID)?.Name);
    }
    // :920-934, ED-216. The shared confirmation seam owns roles, Cancel and owner-centred modal presentation.
    private void PresentConfirmation()
    {
        if (confirmation is not null) return;
        var question = pendingDeletion is { } thread ? $"Delete \"{thread.Title}\"?" : "Delete the archived thread?";
        var presented = pendingDeletion;
        var model = AppEnvironment.GetAppModel(this);
        var owner = TopLevel.GetTopLevel(this) as Avalonia.Controls.Window;
        confirmation = MainActor.Spawn(async () =>
        {
            try
            {
                await AlertPresentation.Show(new AlertRequest(question,
                    "Its history will be removed. Files in your project stay as they are.",
                    [new AlertButton("Delete thread", () => { if (presented is { } pending) model.Delete(pending.Id); pendingDeletion = null; })
                        { Role = AlertButtonRole.Destructive }], AlertKind.ConfirmationDialog, Owner: owner));
            }
            finally { pendingDeletion = null; confirmation = null; }
        });
    }
}

/// <summary>:961-1028 <c>private struct AboutSettingsPage: View</c>.</summary>
internal sealed class AboutSettingsPage : Decorator
{
    // :965-989 body
    public AboutSettingsPage()
    {
        var card = new ChromeCard(AppCard);
        var credits = new ChromeSection("Credits", () => new ChromeCard(() => new StackPanel { Children =
        {
            new MadeByDroppyRow(SwiftURL.Create("https://getdroppy.app")!), new ChromeRowDivider(),
            new ChromeRow("Working indicators", () => new CreditLink("zeronsh/zeron", SwiftURL.Create("https://github.com/zeronsh/zeron")!), "Ported from Zeron by Wing, MIT License."), new ChromeRowDivider(),
            new ChromeRow("Terminal", () => new Panel(), "Marrow's own terminal, built on Windows ConPTY."), new ChromeRowDivider(), // :953-956. OD-0012 H, OD-0072: no repository link; the terminal is Marrow's own.
            new ChromeRow("Welcome tour", () => new CreditLink("rampatra/TourKit", SwiftURL.Create("https://github.com/rampatra/TourKit")!), "Ported from TourKit by Ram Patra, MIT License."), new ChromeRowDivider(), new LicensesRow(),
        } }));
        Child = new StackPanel { Spacing = Chrome.SectionSpacing, Children = { new AboutSoftwareUpdateSection(), card, credits } }; // :942-965 UPSTREAM DEFECT: empty update slot still reserves the gap.
    }
    // :968-1005
    private Control AppCard()
    {
        var icon = new Image { Source = new Bitmap(AssetLoader.Open(new Uri("avares://Marrow.App/Assets/icon.png"))), Width = 56, Height = 56 };
        var labels = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Children = { new TextBlock { Text = "Marrow" }.Font(SwiftFont.System(15, SwiftFont.Weight.Semibold)), new TextBlock { Text = $"Version {AppInfo.Current.Version} ({AppInfo.Current.Build})", Foreground = Chrome.SecondaryText.Brush(this) }.Font(SwiftFont.System(12)) } };
        return new StackPanel { Children =
        {
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Margin = new Thickness(16), Children = { icon, labels } }, new ChromeRowDivider(),
            new ChromeRow("Welcome tour", () => PlainControls.Button(new TextBlock { Text = "Show" }, () => Marrow.App.Views.Tour.Tour.Present(AppEnvironment.GetAppModel(this))).ChromeGlassCapsule(), "The slideshow from the first launch: Hydra, the effort slider, panels and themes."), new ChromeRowDivider(), new AboutUpdateCheckRow(), new ChromeRowDivider(),
            new TextBlock { Text = "A native home for your coding agents, built in C# with Avalonia.", Margin = new Thickness(16, 11, 10, 11), TextWrapping = TextWrapping.Wrap }.Font(SwiftFont.System(13)),
            new ChromeRowDivider(), // :996
            new TextBlock { Text = "Marrow is licensed under the GNU AGPL v3.0 with attribution terms; the terminal is Marrow's own, built on Windows ConPTY.", Foreground = Chrome.SecondaryText.Brush(this), Margin = new Thickness(16, 11, 10, 11), TextWrapping = TextWrapping.Wrap }.Font(SwiftFont.System(12)), // :997-1002, OD-0012 F1, H (OD-0072).
        } };
    }
}

/// <summary>:1032-1080 <c>private struct LicensesRow: View</c>: the license, notices and trademark policy.</summary>
internal sealed class LicensesRow : Decorator
{
    // :1041-1067 body
    private static readonly (string Title, string Resource)[] files = [("Marrow", "LICENSE"), ("Third-party notices", "THIRD_PARTY_NOTICES"), ("Trademarks", "TRADEMARK")]; // :1013-1017
    private static readonly Lazy<Dictionary<string, string>> texts = new(() => files.ToDictionary(file => file.Resource, file => Read(file.Resource))); // :1046-1056
    public LicensesRow()
    {
        var state = new PresentedState(); var button = PlainControls.Button(new TextBlock { Text = "Show" }, () => state.IsPresented = true);
        var popup = PlainControls.Popover(button, new Binding<bool>(() => state.IsPresented, value => state.IsPresented = value), () =>
        {
            var stack = new StackPanel { Spacing = 18, Margin = new Thickness(18) };
            foreach (var file in files)
            {
                var markdown = new MarkdownView(Text(file.Resource)); MarkdownView.SetMarkdownPointSize(markdown, 12); Avalonia.Controls.Documents.TextElement.SetForeground(markdown, Chrome.SecondaryText.Brush(this));
                stack.Children.Add(new StackPanel { Spacing = 8, Children = { new TextBlock { Text = file.Title }.Font(SwiftFont.System(13, SwiftFont.Weight.Semibold)), markdown } });
            }
            return new ScrollViewer { Width = 480, Height = 440, Content = stack, HorizontalContentAlignment = HorizontalAlignment.Left }.PresentedChrome(); // :1043
        });
        Child = new ChromeRow("Licenses", () => new Panel { Children = { button.ChromeGlassCapsule(), popup } }, "AGPL-3.0 with attribution terms, and the notices for what the app builds on."); // :1019-1042
    }

    // :1077-1079 private static func text(of:) -> String
    private static string Text(string of) => texts.Value[of]; // :1055
    private static string Read(string resource)
    {
        try { return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, resource == "LICENSE" ? resource : resource + ".md")); }
        catch (IOException) { return $"The {resource} file is missing from this copy of the app."; }
        catch (UnauthorizedAccessException) { return $"The {resource} file is missing from this copy of the app."; }
    }
}

/// <summary>:1083-1125 <c>private struct MadeByDroppyRow: View</c>.</summary>
internal sealed class MadeByDroppyRow : Decorator
{
    // :1084 let url: URL; :1087-1124 body
    public MadeByDroppyRow(SwiftURL url)
    {
        var logo = SwiftImage.Named("droppy-logo").Resizable().AspectRatio(ContentMode.Fit); logo.Width = 40; logo.Height = 40; AutomationProperties.SetAccessibilityView(logo, AccessibilityView.Raw); // :1070-1073
        var labels = new StackPanel { Spacing = 2, Children = { new TextBlock { Text = "Made by Droppy", Foreground = Chrome.PrimaryText.Brush(this) }.Font(SwiftFont.System(13, SwiftFont.Weight.Semibold)), new TextBlock { Text = "Based on Droppy Code by Jordy Spruit (Droppy), https://getdroppycode.app", Foreground = Chrome.SecondaryText.Brush(this), TextWrapping = TextWrapping.Wrap }.Font(SwiftFont.System(11)) } }; // :1074-1080 OD-0012 F3
        var trailing = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Opacity = 0.9, Children = { new TextBlock { Text = "getdroppy.app", Foreground = Chrome.Accent.Brush(this) }.Font(SwiftFont.System(12, SwiftFont.Weight.Medium)), SwiftImage.SystemName("arrow.up.right").Font(SwiftFont.System(10, SwiftFont.Weight.Semibold)).ForegroundStyle(Chrome.Accent) } };
        var row = new Grid { Background = Brushes.Transparent, ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12, Margin = new Thickness(16, 12, Chrome.RowControlTrailingPadding + 4, 12) }; labels.VerticalAlignment = VerticalAlignment.Center; trailing.VerticalAlignment = VerticalAlignment.Center; /* :1066 HStack(spacing: 12): each child on the logo's centre line */ Grid.SetColumn(labels, 1); Grid.SetColumn(trailing, 2); row.Children.Add(logo); row.Children.Add(labels); row.Children.Add(trailing); // :1093 .contentShape(.rect): the whole row, including its gaps, is one hit target.
        var hover = new SettingsScalarAnimation(trailing, 0.9, value => trailing.Opacity = value); // :1094-1096
        var button = PlainControls.Button(row, () => Marrow.Services.Port.Workspace.Open.Current(url)); button.OnHover(hovering => hover.Set(hovering ? 1 : 0.9, Chrome.Hover)); ToolTip.SetTip(button, "Open getdroppy.app"); AutomationProperties.SetName(button, "Made by Droppy. Open getdroppy.app"); Child = button; // :1081-1101
    }
}

/// <summary>:1127-1144 <c>private struct CreditLink: View</c>.</summary>
internal sealed class CreditLink : Decorator
{
    // :1128-1129 let title, let url; :1131-1143 body
    public CreditLink(string title, SwiftURL url)
    {
        var label = new TextBlock { Text = title, Foreground = Chrome.Accent.Brush(this) }.Font(SwiftFont.System(12, SwiftFont.Weight.Medium));
        var button = PlainControls.Button(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { label, SwiftImage.SystemName("arrow.up.right").Font(SwiftFont.System(10, SwiftFont.Weight.Semibold)).ForegroundStyle(Chrome.Accent) } }, () => Marrow.Services.Port.Workspace.Open.Current(url)); ToolTip.SetTip(button, url.AbsoluteString); Child = button; // :1107-1120
    }
}

/// <summary>:1148-1178 <c>private struct ProjectMarksRow: View</c>: the per-project marks behind one button.</summary>
internal sealed class ProjectMarksRow : Decorator
{
    // :1153-1170 body
    public ProjectMarksRow()
    {
        var presented = new MarksState();
        var button = PlainControls.Button(new TextBlock { Text = "Edit" }, () => presented.IsPresented = true); // :1131-1133
        ToolTip.SetTip(button, "Choose a mark for each project"); // :1135
        this.Track(() => AppEnvironment.GetAppModel(this).Projects.Count, count => button.IsEnabled = count > 0); // :1134
        var popup = PlainControls.Popover(button, new Binding<bool>(() => presented.IsPresented, value => presented.IsPresented = value), () =>
        {
            var model = AppEnvironment.GetAppModel(this);
            var list = new StackPanel { Width = 400 }; // :1137,1145
            for (var i = 0; i < model.Projects.Count; i++)
            {
                var project = model.Projects[i];
                if (i > 0) list.Children.Add(new ChromeRowDivider()); // :1139
                list.Children.Add(new ChromeRow(project.Name, () => new ProjectIconButton(new Binding<ProjectIcon?>(() => model.Project(project.Id)?.Icon, value => model.UpdateProject(project.Id, old => old with { Icon = value }))), project.Path)); // :1140-1142,1149-1153
            }
            return list;
        });
        // :1132 .buttonStyle(.glass): DEFECT FIX (UI-SETTINGS-016), the Edit button had no glass capsule at all; the same recipe as Reveal and the other
        // .glass buttons of this file. The .controlSize(.small) padding stays an OS-defined metric no record gives (UNVERIFIED, OD-0001).
        Child = new Panel { Children = { button.ChromeGlassCapsule(), popup } }; // :1136
    }

    // :1128
    private sealed class MarksState : MainActorModel
    {
        private bool isPresented;
        public bool IsPresented { get => Read(isPresented); set => Set(ref isPresented, value); }
    }
}

/// <summary>:1182-1232 <c>private struct ThemePickerButton: View</c>: the theme menu.</summary>
internal sealed class ThemePickerButton : Decorator
{
    // :1183 @Binding var selection: AppTheme; :1191-1225 body
    public ThemePickerButton(Binding<AppTheme> selection)
    {
        var state = new PresentedState(); var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Height = Chrome.CapsuleContentHeight, Margin = new Thickness(Chrome.CapsuleHorizontalPadding, 0) };
        void Update()
        {
            var theme = selection.Value; label.Children.Clear(); var swatch = theme.Swatch(); swatch.Width = 14; swatch.Height = 14; swatch.VerticalAlignment = VerticalAlignment.Center; /* :1172 HStack: each child on the centre line */ label.Children.Add(swatch); label.Children.Add(new TextBlock { Text = theme.DisplayName(), MaxLines = 1, VerticalAlignment = VerticalAlignment.Center }.Font(SwiftFont.System(12.5, SwiftFont.Weight.Medium))); var chevron = SwiftImage.SystemName("chevron.down").Font(SwiftFont.System(8, SwiftFont.Weight.Bold)).ForegroundStyle(Chrome.SecondaryText); chevron.VerticalAlignment = VerticalAlignment.Center; label.Children.Add(chevron);
        }
        var button = PlainControls.Button(label, () => state.IsPresented = !state.IsPresented);
        // :1185 .contentShape(Capsule(style: .continuous)): the hit-test shape covers the whole padded frame (not just painted content) and survives label replacement.
        button.Template = new FuncControlTemplate<Button>((owner, scope) => new Border
        {
            Background = Brushes.Transparent,
            Child = new ContentPresenter { Name = "PART_ContentPresenter", [!ContentPresenter.ContentProperty] = owner[!ContentControl.ContentProperty] }.RegisterInNameScope(scope),
        }.Capsule(RoundedCornerStyle.Continuous));
        var foreground = new AnimatedColor(label); Avalonia.Controls.Documents.TextElement.SetForeground(label, foreground.Brush);
        label.Track(() => state.IsHovering || state.IsPresented, full => foreground.Set(Chrome.PrimaryText.Opacity(full ? 1 : 0.92), Transaction.Current)); // :1188
        button.OnHover(value => Transaction.WithAnimation(Chrome.Hover, () => state.IsHovering = value));
        var popup = PlainControls.Popover(button, new Binding<bool>(() => state.IsPresented, value => state.IsPresented = value), () =>
        {
            var items = new StackPanel();
            Control Item(AppTheme theme) => new PopoverItem(theme.DisplayName(), () => { selection.Value = theme; Update(); }, leading: theme.Swatch(), isChecked: theme == selection.Value);
            items.Children.Add(Item(AppTheme.System));
            foreach (var scheme in new[] { ("Dark", Avalonia.Styling.ThemeVariant.Dark), ("Light", Avalonia.Styling.ThemeVariant.Light) }) { items.Children.Add(new PopoverSectionHeader(scheme.Item1)); foreach (var theme in SwiftEnum<AppTheme>.AllCases.Where(theme => theme.Scheme() == scheme.Item2)) items.Children.Add(Item(theme)); }
            var menu = new PopoverMenu(() => items); Popovers.SetClosePopover(menu, () => state.IsPresented = false); return menu;
        });
        Child = new Panel { Children = { button.ChromeGlassCapsule(), popup } }; this.Track(() => selection.Value, _ => Update());
        void ThemeChanged()
        {
            if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) { Update(); return; }
            Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (TopLevel.GetTopLevel(this) is not null) Update(); });
        }
        AttachedToVisualTree += (_, _) => ThemeManager.DidChange += ThemeChanged; DetachedFromVisualTree += (_, _) => ThemeManager.DidChange -= ThemeChanged; // :1171-1208
    }

    // :1186 .fixedSize(): retain the content's dimensions even in a stretching parent.
    protected override Size MeasureOverride(Size availableSize) => PlainControls.MeasureFixed(this, Child, availableSize);
}
