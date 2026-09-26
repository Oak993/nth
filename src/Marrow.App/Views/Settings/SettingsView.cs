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
        var search = new SidebarSearchField(new Binding<string>(() => state.Search, value => state.Search = value), onSubmit: () => { if (VisiblePages.Count > 0) state.Page = VisiblePages[0]; }); // :141-143
        search.Margin = new Thickness(Chrome.ListInset, 14, Chrome.ListInset, 0); // :144-145
        var scroll = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }; // :146,190
        WireScroll(scroll, page: false);
        var column = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") }; // :137
        var spacer = new Border { Height = Chrome.TrafficLightDiameter, Margin = new Thickness(0, Chrome.TrafficLightTop, 0, 0) }; // :138-140
        Grid.SetRow(search, 1); Grid.SetRow(scroll, 2); column.Children.Add(spacer); column.Children.Add(search); column.Children.Add(scroll);
        sidebar = PlainControls.Background(column, new WindowDragArea()); sidebar.Width = 212; // :112,192-193
        detail.Margin = new Thickness(0, Chrome.SheetInset, Chrome.SheetInset, Chrome.SheetInset); // :114-115
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("212,*") }; Grid.SetColumn(detail, 1); grid.Children.Add(sidebar); grid.Children.Add(detail);
        var backdrop = new WindowBackdrop(0); // :118
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
        this.Track(() => AppEnvironment.GetAppModel(this).ArchivedThreads.Count, _ => { if (state.Page == SettingsPage.Archive) RebuildDetail(); });
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
            detail.Child = symbol.DetailSheet(); pageStack = null; return;
        }
        Control content = state.Page switch // :276-287
        {
            SettingsPage.General => new GeneralSettingsPage(), SettingsPage.Appearance => new AppearanceSettingsPage(), SettingsPage.Providers => new ProvidersSettingsPage(),
            SettingsPage.Mcp => new MCPSettingsPage(state.McpSearch), SettingsPage.Models => new ModelsSettingsPage(state.ModelSearch), SettingsPage.Hydra => new HydraSettingsPage(),
            SettingsPage.SourceControl => new SourceControlSettingsPage(), SettingsPage.Shortcuts => new ShortcutsSettingsPage(), SettingsPage.Archive => new ArchiveSettingsPage(), SettingsPage.About => new AboutSettingsPage(),
        };
        var hasControls = state.Page is SettingsPage.Providers or SettingsPage.Models or SettingsPage.Mcp or SettingsPage.Archive or SettingsPage.About; // :270-272
        pageStack = new StackPanel { Spacing = Chrome.SectionSpacing, Margin = new Thickness(Chrome.ContentHorizontalPadding + 8, hasControls ? Chrome.ChromeTopPadding + Chrome.CapsuleHeight + 16 : 22, Chrome.ContentHorizontalPadding + 8, 24), Children = { content } }; // :220-227
        pageStack.Track(() => scrollTracker.IsFrozen, value => pageStack.IsHitTestVisible = !value);
        var scroll = new ScrollViewer { Content = pageStack, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }; // :228-230
        WireScroll(scroll, page: true);
        var chrome = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, MinHeight = Chrome.CapsuleHeight, Margin = new Thickness(Chrome.ChromeHorizontalPadding, Chrome.ChromeTopPadding, Chrome.ChromeHorizontalPadding, 0), VerticalAlignment = VerticalAlignment.Top, Children = { new ChromeCompactTitle(state.Page.Title, scrollChrome) } }; // :246-267
        if (state.Page == SettingsPage.Models) chrome.Children.Add(new ChromeSearchField(new Binding<string>(() => state.ModelSearch, value => { state.ModelSearch = value; ((ModelsSettingsPage)content).SetQuery(value); }), "Search models")); // :248-250
        if (state.Page == SettingsPage.Mcp) chrome.Children.Add(new ChromeSearchField(new Binding<string>(() => state.McpSearch, value => state.McpSearch = value), "Search servers")); // :251-253
        if (state.Page == SettingsPage.Providers) chrome.Children.Add(new ProvidersRefreshButton()); // :254-256
        if (state.Page == SettingsPage.Archive) chrome.Children.Add(new ArchiveDeleteAllButton()); // :257-259
        if (state.Page == SettingsPage.About) chrome.Children.Add(new AboutUpdateChromeAccessory()); // :260-262
        detail.Child = new Panel { Children = { scroll, new PaneTopVeil(scrollChrome) { VerticalAlignment = VerticalAlignment.Top }, chrome } }.DetailSheet();
    }

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

    public SmallSwitch(Binding<bool> isOn)
    {
        this.isOn = isOn;
        Width = TrackWidth;
        Height = TrackHeight;
        Cursor = new Cursor(StandardCursorType.Hand);
        PointerPressed += (_, e) =>
        {
            isOn.Value = !isOn.Value;
            e.Handled = true;
            InvalidateVisual();
        };
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
    public AppearanceSettingsPage() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/SettingsView.swift:420");
}

/// <summary>:462-517 <c>private struct AppZoomCard: View</c>: the app-wide zoom.</summary>
internal sealed class AppZoomCard : Decorator
{
    // :463 @Binding var percent: Int; :467-516 body
    public AppZoomCard(Binding<long> percent) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/SettingsView.swift:467");
}

/// <summary>:521-564 <c>private struct BackdropOpacitySlider: View</c>.</summary>
internal sealed class BackdropOpacitySlider : Decorator
{
    // :522 @Binding var value: Double; :526-563 body
    public BackdropOpacitySlider(Binding<double> value) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/SettingsView.swift:526");
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
            new Binding<bool>(() => state.Expansion.TryGetValue(provider, out var value) ? value : AppEnvironment.GetAppModel(this).Providers.Status(provider).ProviderStatusAuth is ProviderStatus.Auth.SignedIn,
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
        Child = new StackPanel { Spacing = Chrome.SectionHeaderSpacing, Children = { new CollapsibleProviderHeader(provider, isExpanded, "provider settings"), reveal } }; // :646-647
        this.Track(() => isExpanded.Value, expanded => // :764-766
        {
            reveal.Fraction = expanded ? 1 : 0; reveal.IsEnabled = expanded; reveal.IsHitTestVisible = expanded;
            AutomationProperties.SetAccessibilityView(reveal, expanded ? AccessibilityView.Content : AccessibilityView.Raw);
        });
        this.Track(() => AppEnvironment.GetAppModel(this), model =>
        {
            state.BinaryPath = model.Settings.BinaryPath(provider); if (provider.AcceptsAPIKey) state.ApiKey = model.Settings.ApiKeyInput(provider); // :768-771
            var statusRow = new Grid { ColumnDefinitions = new ColumnDefinitions("22,*,Auto"), ColumnSpacing = 12, Margin = new Thickness(16, 11, Chrome.RowControlTrailingPadding, 11) }; // :650,653,673-675
            var icon = new ProviderIcon { Provider = provider, Size = 18 }; statusRow.Children.Add(icon); // :651
            var labels = new StackPanel { Spacing = 2 }; Grid.SetColumn(labels, 1); statusRow.Children.Add(labels); // :654
            var toggle = new SettingsSwitch(new Binding<bool>(() => model.Settings.IsEnabled(provider), value => model.Settings.SetEnabled(value, provider))); Grid.SetColumn(toggle, 2); statusRow.Children.Add(toggle); // :668-671
            cardRows.Children.Add(statusRow);
            var usage = new StackPanel { Spacing = 14, Margin = new Thickness(16, 12) }; // :676-687
            var usageSlot = new StackPanel(); cardRows.Children.Add(usageSlot);
            statusRow.Track(() => model.Providers.Status(provider), status =>
            {
                labels.Children.Clear(); labels.Children.Add(new TextBlock { Text = status.IsChecking ? "Checking\u2026" : status.Summary }.Font(SwiftFont.System(13))); // :655-656
                var detailText = !provider.IsAPIKeyBased && status.Version is { } version ? "Version " + version : provider.IsAPIKeyBased && status.ApiKeyConfigured && provider.ApiHost is { } host ? "Native API \u00b7 " + host : null; // :657-664
                if (detailText is not null) labels.Children.Add(new TextBlock { Text = detailText, Foreground = Chrome.SecondaryText.Brush(this) }.Font(SwiftFont.System(11)));
            });
            bool ShowsUsage() { var status = model.Providers.Status(provider); return (PlanLimitsReader.ExposesLimits(provider) || CreditsReader.ExposesCredits(provider)) && status.IsInstalled && status.ProviderStatusAuth is not ProviderStatus.Auth.SignedOut; } // :629-634
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
                var eye = PlainControls.Button(SwiftImage.SystemName("eye").Font(SwiftFont.System(12)).ForegroundStyle(Chrome.SecondaryText), () => state.ShowsAPIKey = !state.ShowsAPIKey); // :708-715
                eye.Track(() => state.ShowsAPIKey, shown => { keyField.PasswordChar = shown ? '\0' : '\u2022'; eye.Content = SwiftImage.SystemName(shown ? "eye.slash" : "eye").Font(SwiftFont.System(12)).ForegroundStyle(Chrome.SecondaryText); ToolTip.SetTip(eye, shown ? "Hide the API key" : "Show the API key"); }); // :711,716
                // DEVIATION OD-0012 G2: Credential Manager replaces Keychain.
                var keyDetail = provider.IsAPIKeyBased ? "Stored in your Credential Manager. Get one at " + (provider.ApiKeySource ?? "the provider dashboard") + "." : "Instead of `" + provider.LoginCommand + "`: a Studio key from " + (provider.ApiKeySource ?? "the provider dashboard") + ", stored in your Credential Manager and handed to the CLI."; // :693-695
                cardRows.Children.Add(new ChromeRow(provider.IsAPIKeyBased ? "API key" : "API key (optional)", () => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { keyField, eye } }, keyDetail)); // :691-717
                keyField.Track(() => StoredKey(model.Settings), value => { if (!SwiftString.Equal(state.ApiKey, value)) state.ApiKey = value; }); // :796-799
            }
            if (!provider.IsAPIKeyBased)
            {
                cardRows.Children.Add(new ChromeRowDivider());
                var field = PlainControls.TextField(provider.ExecutableName, new Binding<string>(() => state.BinaryPath, value => { state.BinaryPath = value; if (!SwiftString.Equal(value, model.Settings.BinaryPath(provider))) model.Settings.SetBinaryPath(value, provider); })); // :732-735,780-783
                field.Width = 200; SwiftFont.System(12, design: SwiftFont.Design.Monospaced).Apply(field);
                var row = new ChromeRow("Binary path", () => field, model.Providers.Status(provider).Executable?.Path); cardRows.Children.Add(row); // :732-736
            }
            var installSlot = new StackPanel(); cardRows.Children.Add(installSlot);
            installSlot.Track(() => model.Providers.Status(provider), status =>
            {
                installSlot.Children.Clear();
                if (provider.IsAPIKeyBased && !status.IsInstalled) { installSlot.Children.Add(new ChromeRowDivider()); installSlot.Children.Add(new ChromeRow("Get a key", () => Link("Get a " + provider.DisplayName + " key", provider.InstallURL), provider.DisplayName + " needs an API key. Nothing to install.")); } // :719-727
                if (provider.IsAPIKeyBased) return;
                if (!status.IsInstalled) { installSlot.Children.Add(new ChromeRowDivider()); installSlot.Children.Add(new ChromeRow("Install", () => Link("Get " + provider.DisplayName, provider.InstallURL), provider.DisplayName + " was not found on your PATH.")); } // :738-745
                else if (status.ProviderStatusAuth is not ProviderStatus.Auth.SignedIn && !status.IsChecking) { installSlot.Children.Add(new ChromeRowDivider()); installSlot.Children.Add(new ChromeRow("Sign in", () => PlainControls.Button(new TextBlock { Text = "Login", Height = Chrome.RowButtonLabelHeight }, () => Workspace.OpenTerminal(provider.LoginCommand)), "Click here to login again.")); } // :638-641,747-756
            });
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
            _ => "",
        };
    }

    // :722-726,741-745
    private static Control Link(string title, SwiftURL url) => PlainControls.Button(new TextBlock { Text = title, Height = Chrome.RowButtonLabelHeight }, () => Marrow.Services.Port.Workspace.Open.Current(url));
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
                new GlassPickerButton<TextGenerationChoice>(SwiftEnum<TextGenerationChoice>.AllCases.Select(choice => (choice, choice.Title)).ToArray(), new Binding<TextGenerationChoice>(() => settings.TextGeneration, value => settings.TextGeneration = value)),
                "Automatic uses the thread's own provider. Claude uses Haiku, Codex your default model.")))); // :808-816
            var editor = PlainControls.TextField("", new Binding<string>(() => settings.CommitInstructions, value => settings.CommitInstructions = value)); editor.AcceptsReturn = true; editor.MinHeight = 90; SwiftFont.System(SwiftFont.TextStyle.Body).Apply(editor); // :821-824
            var placeholder = new TextBlock { Text = "For example: use conventional commit prefixes", Margin = new Thickness(5, 0), Foreground = Chrome.SecondaryText.Brush(this), IsHitTestVisible = false }.Font(SwiftFont.System(SwiftFont.TextStyle.Body)); // :825-830
            placeholder.Track(() => settings.CommitInstructions.Length == 0, value => placeholder.IsVisible = value);
            sections.Children.Add(new ChromeSection("Commit message instructions", () => new ChromeCard(() => new Border { Padding = new Thickness(12), Child = new Panel { Children = { editor, placeholder } } }))); // :818-834
            sections.Children.Add(new ChromeSection("Worktrees and pull requests", () => new ChromeCard(() => new StackPanel { Children =
            {
                new ChromeRow("Worktrees", () => PlainControls.Button(new TextBlock { Text = "Reveal" }, () => Workspace.RevealInFileExplorer(Storage.WorktreesDirectory)), Storage.WorktreesDirectory), // :838-840
                new ChromeRowDivider(), // :842
                new ChromeRow("Pull requests", () => new Border(), "Opened with the GitHub CLI (gh) or GitLab CLI (glab) from your PATH."), // :843-845
            } })));
        });
    }
}

/// <summary>:877-909 <c>private struct ArchiveDeleteAllButton: View</c>.</summary>
internal sealed class ArchiveDeleteAllButton : Decorator
{
    // :881-908 body
    public ArchiveDeleteAllButton() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/SettingsView.swift:881");
}

/// <summary>:912-959 <c>private struct ArchiveSettingsPage: View</c>.</summary>
internal sealed class ArchiveSettingsPage : Decorator
{
    // :919-958 body
    public ArchiveSettingsPage() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/SettingsView.swift:919");
}

/// <summary>:961-1028 <c>private struct AboutSettingsPage: View</c>.</summary>
internal sealed class AboutSettingsPage : Decorator
{
    // :965-989 body
    public AboutSettingsPage() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/SettingsView.swift:965");
}

/// <summary>:1032-1080 <c>private struct LicensesRow: View</c>: the license, notices and trademark policy.</summary>
internal sealed class LicensesRow : Decorator
{
    // :1041-1067 body
    public LicensesRow() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/SettingsView.swift:1041");

    // :1077-1079 private static func text(of:) -> String
    private static string Text(string of) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/SettingsView.swift:1077");
}

/// <summary>:1083-1125 <c>private struct MadeByDroppyRow: View</c>.</summary>
internal sealed class MadeByDroppyRow : Decorator
{
    // :1084 let url: URL; :1087-1124 body
    public MadeByDroppyRow(SwiftURL url) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/SettingsView.swift:1087");
}

/// <summary>:1127-1144 <c>private struct CreditLink: View</c>.</summary>
internal sealed class CreditLink : Decorator
{
    // :1128-1129 let title, let url; :1131-1143 body
    public CreditLink(string title, SwiftURL url) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/SettingsView.swift:1131");
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
        Child = new Panel { Children = { button, popup } }; // :1136
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
    public ThemePickerButton(Binding<AppTheme> selection) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/SettingsView.swift:1191");
}
