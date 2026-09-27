using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input.GestureRecognizers;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Port;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Sound;

namespace Marrow.App.Views.Settings;

// Source: DroppyCode/App/Views/Settings/MCPSettingsPage.swift (B3 52bc792), :1-1141.
// DEVIATION OD-0012 F1: Marrow; OD-0008 B3/B4: Credential Manager / this PC.
public sealed class MCPSettingsPage : Decorator
{
    private string query; // :7
    private readonly StackPanel root = new() { Spacing = Chrome.SectionSpacing }; // :36
    private sealed class ViewState : MainActorModel
    {
        private ValueSet<string> celebrating = new([], SwiftStringComparer.Instance); // :12
        public ValueSet<string> Celebrating { get => Read(celebrating); set => Set(ref celebrating, value); }
    }
    private readonly ViewState view = new();
    private OnChange<IReadOnlyDictionary<string, MCPConnectionState>>? stateChanges;
    private readonly MCPLeadCard lead = new();
    private readonly TextBlock footer = Text("Connected servers reach every provider when a thread starts: the CLIs at launch, the API models through Marrow itself.", 11, Chrome.SecondaryText); // :60-62
    private readonly StackPanel connectedRows = new();
    private readonly Dictionary<string, MCPConnectedRow> connectedControls = new(SwiftStringComparer.Instance);
    private readonly Dictionary<string, ChromeRowDivider> connectedDividers = new(SwiftStringComparer.Instance);
    private ChromeSection? connectedSection;
    private MCPCustomSection? customs;
    private string? customsQuery;
    // PORT-ONLY identity cache for :49-59's ForEach/LazyVGrid. Retaining a card keeps its onChange armed.
    private sealed record CategoryView(ChromeSection Section, Grid Grid, ForEach<MCPCatalogEntry, string> Cards)
    {
        public ValueSet<string> Celebrating { get; set; } = new([], SwiftStringComparer.Instance);
    }
    private readonly Dictionary<MCPCategory, CategoryView> categories = new();
    private TextBlock? deletionReport; // OD-0008 A12 / OD-0012 E6
    // :7,35-63
    public MCPSettingsPage(string query)
    {
        this.query = query; Child = root;
        // :64-74 register before layout tracking, so a newly connected card stays in its grid while celebrating.
        AttachedToVisualTree += (_, _) => stateChanges ??= new OnChange<IReadOnlyDictionary<string, MCPConnectionState>>(() => AppEnvironment.GetAppModel(this).Mcp.States, Celebrate);
        DetachedFromVisualTree += (_, _) => { stateChanges?.Dispose(); stateChanges = null; };
        this.Track(() => (AppEnvironment.GetAppModel(this).Mcp.States, AppEnvironment.GetAppModel(this).Mcp.Customs, view.Celebrating, AppEnvironment.GetAppModel(this).Mcp.CredentialDeletionFailure), _ => Rebuild());
    }
    // :64-74. Intentionally independent of Settings.SettleSound, and each task removes its own ID after 900ms.
    private void Celebrate(IReadOnlyDictionary<string, MCPConnectionState> previous, IReadOnlyDictionary<string, MCPConnectionState> current)
    {
        if (EnvironmentValues.GetAccessibilityReduceMotion(this)) return; // :65 suppresses BOTH sound and celebration
        foreach (var (id, state) in current.Where(pair => pair.Value.IsConnected && !(previous.GetValueOrDefault(pair.Key)?.IsConnected ?? false)))
        {
            Transaction.WithAnimation(Animation.Spring(duration: 0.35), () => view.Celebrating = view.Celebrating.Mutating(ids => ids.Add(id))); // :58,67
            SettleChime.Play(); // :68
            MainActor.Spawn(async () =>
            {
                await SwiftTask.TrySleep(TimeSpan.FromMilliseconds(900)); // :69-70 unstructured task, as in source
                Transaction.WithAnimation(Animation.Spring(duration: 0.35), () => view.Celebrating = view.Celebrating.Mutating(ids => ids.Remove(id))); // :58,71
            });
        }
    }
    // :7,77-85
    public void SetQuery(string value) { query = value; if (GetValue(AppEnvironment.AppModelProperty) is not null) Rebuild(); }
    // :19-63
    private void Rebuild()
    {
        var model = AppEnvironment.GetAppModel(this); var children = new List<Control> { lead };
        var connected = MCPCatalog.Entries.Where(e => !view.Celebrating.Contains(e.Id) && model.Mcp.State(e.Id).IsConnected).OrderBy(e => e.Name, SwiftStringComparer.Instance).ToArray(); // :19-23
        var rows = new List<Control>();
        for (var i = 0; i < connected.Length; i++)
        {
            var entry = connected[i];
            if (i > 0) { if (!connectedDividers.TryGetValue(entry.Id, out var divider)) connectedDividers[entry.Id] = divider = new ChromeRowDivider(); rows.Add(divider); }
            if (!connectedControls.TryGetValue(entry.Id, out var row)) connectedControls[entry.Id] = row = new MCPConnectedRow(entry); rows.Add(row);
        }
        Reconcile(connectedRows, rows); // :40-45 retains the source ForEach identities
        foreach (var gone in connectedControls.Keys.Where(id => !connected.Any(e => SwiftString.Equal(e.Id, id))).ToArray()) { connectedControls.Remove(gone); connectedDividers.Remove(gone); }
        if (connected.Length > 0) { connectedSection ??= new ChromeSection("Connected", () => new ChromeCard(() => connectedRows, lazy: true)); children.Add(connectedSection); } // :38-47
        if (customs is null || !SwiftString.Equal(customsQuery ?? "", query)) { customs = new MCPCustomSection(query); customsQuery = query; }
        children.Add(customs); // :48
        var visibleCategories = new HashSet<MCPCategory>();
        foreach (var group in MCPCatalog.ByCategory)
        {
            var visible = group.Entries.Where(e => (!model.Mcp.State(e.Id).IsConnected || view.Celebrating.Contains(e.Id)) && Matches(e, query)).ToArray(); // :25-33
            if (visible.Length == 0 && !categories.ContainsKey(group.Category)) continue;
            if (!categories.TryGetValue(group.Category, out var category))
            {
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 10, RowSpacing = 10 };
                var cards = new ForEach<MCPCatalogEntry, string>(grid, e => e.Id,
                    e => new MCPServerCard(e, view.Celebrating.Contains(e.Id)).Transition(SwiftTransition.Scale(0.9).Combined(SwiftTransition.Opacity)), // :54
                    (control, e) => ((MCPServerCard)control).UpdateCelebrating(view.Celebrating.Contains(e.Id)));
                // ED-218: the last row's removal must finish before its containing category leaves the tree.
                grid.Children.CollectionChanged += (_, _) =>
                {
                    if (grid.Children.Count == 0 && stateChanges is not null)
                        MainActor.Post(() => { if (stateChanges is not null) Rebuild(); });
                };
                categories[group.Category] = category = new CategoryView(new ChromeSection(group.Category.Title(), () => grid), grid, cards) { Celebrating = view.Celebrating };
            }
            // :58 .animation(_, value: celebrating) also applies when a states-triggered rebuild was already queued.
            var gridAnimation = category.Celebrating.Equals(view.Celebrating) ? Transaction.Current : Animation.Spring(duration: 0.35);
            category.Celebrating = view.Celebrating;
            if (EnvironmentValues.GetAccessibilityReduceMotion(this)) gridAnimation = null;
            Transaction.WithAnimation(gridAnimation, () =>
            {
                category.Cards.Apply(visible); category.Grid.RowDefinitions.Clear();
                // Exiting controls keep their old grid slot while active rows take their new slots and glide (:54-58).
                var active = category.Cards.Controls;
                for (var i = 0; i < active.Count; i++) { Grid.SetColumn(active[i], i % 2); Grid.SetRow(active[i], i / 2); }
                var rowCount = category.Grid.Children.Count == 0 ? 0 : category.Grid.Children.Max(control => Grid.GetRow(control)) + 1;
                for (var i = 0; i < rowCount; i++) category.Grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            });
            if (category.Grid.Children.Count == 0) continue;
            visibleCategories.Add(group.Category);
            children.Add(category.Section); // :49-59
        }
        foreach (var gone in categories.Keys.Where(category => !visibleCategories.Contains(category)).ToArray()) categories.Remove(gone);
        // ED-221 (PR-MCP-228): the page-level report survives a removed custom row, above the footer.
        // :270-273 failed-line style: .subheadline, Chrome.danger, lineLimit(3); next disconnect/deletion clears it.
        if (model.Mcp.CredentialDeletionFailure is { } failure) { deletionReport ??= Text(failure, 11, Chrome.Danger, lines: 3); deletionReport.Text = failure; children.Add(deletionReport); }
        children.Add(footer); Reconcile(root, children); // :60-62 footer remains last
    }
    // PORT-ONLY keyed body reconciliation: unchanged controls stay attached, preserving their onChange/focus.
    private static void Reconcile(Panel panel, IReadOnlyList<Control> children)
    {
        foreach (var gone in panel.Children.Where(control => !children.Contains(control)).ToArray()) panel.Children.Remove(gone);
        for (var i = 0; i < children.Count; i++) { var old = panel.Children.IndexOf(children[i]); if (old < 0) panel.Children.Insert(i, children[i]); else if (old != i) panel.Children.Move(old, i); }
    }
    // :77-85
    internal static bool Matches(MCPCatalogEntry entry, string query)
    {
        var trimmed = query.Trimmed();
        return trimmed.Length == 0 || new[] { entry.Name, entry.Vendor, entry.Summary, entry.Id }.Concat(entry.SampleTools).Any(value => SwiftString.LocalizedCaseInsensitiveContains(value, trimmed));
    }
    // :894-898
    internal static Control McpFieldSurface(Control view) => new Border { Child = view, CornerRadius = new CornerRadius(9), Background = Chrome.Overlay(0.06).Brush(view) };
    // :97-98,111-113,158-162 and the other Text modifiers below.
    internal static TextBlock Text(string text, double size, SwiftColor color, SwiftFont.Weight weight = SwiftFont.Weight.Regular, bool mono = false, int lines = 0)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxLines = lines };
        SwiftFont.System(size, weight, mono ? SwiftFont.Design.Monospaced : SwiftFont.Design.Default).Apply(block); block.Foreground = color.Brush(block); return block;
    }
    // :91-93,172-176,721-725,928-932
    internal static SwiftImage Symbol(string name, double size, SwiftColor color) => SwiftImage.SystemName(name).Font(SwiftFont.System(size)).ForegroundStyle(color);
    // :328-330,342-348. ENGINEERING DECISION: shared capsule recipe from FollowUpEditor/WelcomeView.
    // Native .small metrics remain UNVERIFIED under OD-0001; material is MARROW_DEFERRED ED-180.
    internal static Control ActionButton(string title, Action action, bool prominent = false)
    {
        var label = Text(title, 11, Chrome.PrimaryText);
        if (prominent) return WelcomeView.GlassProminentButton(label, Chrome.CapsuleContentHeight, action);
        var surface = new Border { Child = label, Height = Chrome.CapsuleContentHeight, Padding = new Thickness(Chrome.CapsuleHorizontalPadding, 0) }.Capsule(RoundedCornerStyle.Continuous);
        SwiftGlass.SetEffect(surface, GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Continuous)).Interactive());
        return PlainControls.Button(surface, action);
    }
    // :169-179,543-553
    internal static Button Options(Action action)
    {
        var button = PlainControls.Button(Symbol("ellipsis.circle", 13, Chrome.SecondaryText), action); button.Width = 22; button.Height = 22; ToolTip.SetTip(button, "Server options"); return button;
    }
    // :180-198,554-573,941-948; environment and dismiss are injected by SwiftUI.
    internal static Popup Popover(Control anchor, Binding<bool> presented, Func<Control> content, Control environment) => PlainControls.Popover(anchor, presented, () =>
    {
        var body = content(); if (environment.GetValue(AppEnvironment.AppModelProperty) is { } model) AppEnvironment.SetAppModel(body, model);
        Popovers.SetClosePopover(body, () => presented.Value = false); return body.PresentedChrome();
    });
    // :352-358,534-540
    internal static void Begin(Control owner, MCPCatalogEntry entry)
    {
        var model = AppEnvironment.GetAppModel(owner); if (!entry.NeedsInput) model.Mcp.Connect(entry); else MCPKeyPanel.Shared.Present(entry, model);
    }
    // :1010-1019 Link destination uses the Windows desktop launcher.
    internal static void OpenURL(Control owner, SwiftURL url)
    {
        if (TopLevel.GetTopLevel(owner) is { } top) _ = top.Launcher.LaunchUriAsync(new Uri(url.AbsoluteString, UriKind.Absolute));
    }
}

internal sealed class MCPLeadCard : Decorator
{
    // :89-122
    public MCPLeadCard()
    {
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { MCPSettingsPage.Text("Connected servers lead", 13, Chrome.PrimaryText, SwiftFont.Weight.Semibold), new MCPHoverInfoButton("About connected servers", Facts) } };
        var copy = new StackPanel { Spacing = 6, Children = { title, MCPSettingsPage.Text("Connect a server here and every thread can use it. Anything set up in a terminal or another app stays just as it is.", 12, Chrome.SecondaryText) } };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 }; row.Children.Add(MCPSettingsPage.Symbol("checkmark.shield.fill", 22, Chrome.Accent)); Grid.SetColumn(copy, 1); row.Children.Add(copy);
        Child = new Border { Padding = new Thickness(16), CornerRadius = new CornerRadius(14), Background = Chrome.PrimaryText.Opacity(0.05).Brush(row), Child = row };
    }
    // :101-107,124-133
    private static Control Facts()
    {
        var stack = new StackPanel { Spacing = 8 };
        foreach (var (symbol, text) in new[] { ("lock.fill", "Keys stay in your Credential Manager"), ("arrow.uturn.backward", "Disconnect to go back to your own setup"), ("sparkles", "Every provider, whatever the model") })
        {
            var icon = MCPSettingsPage.Symbol(symbol, 12, Chrome.Accent); icon.Width = 14;
            stack.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { icon, MCPSettingsPage.Text(text, 12, Chrome.PrimaryText.Opacity(0.9)) } });
        }
        return new Border { Padding = new Thickness(14), Child = stack };
    }
}

internal sealed class MCPConnectedRow : Decorator
{
    private readonly MCPCatalogEntry entry; // :140
    private sealed class ViewState : MainActorModel
    {
        private bool menu, tools;
        public bool Menu { get => Read(menu); set => Set(ref menu, value); } // :142
        public bool Tools { get => Read(tools); set => Set(ref tools, value); } // :143
    }
    private readonly ViewState view = new();
    // :152-203
    public MCPConnectedRow(MCPCatalogEntry entry) { this.entry = entry; this.Track(() => (AppEnvironment.GetAppModel(this).Mcp.States, AppEnvironment.GetAppModel(this).Mcp.Connections), _ => Build()); }
    // :145-203
    private void Build()
    {
        var model = AppEnvironment.GetAppModel(this); var connection = model.Mcp.Connection(entry.Id);
        var count = model.Mcp.State(entry.Id) is MCPConnectionState.Connected c ? c.ToolCount : connection?.Tools.Count ?? 0;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 12 };
        row.Children.Add(new Border { Width = 26, Child = new MCPIcon(entry,22) { HorizontalAlignment = HorizontalAlignment.Center } });
        var copy = new StackPanel { Spacing = 2, Children = { MCPSettingsPage.Text(entry.Name,13,Chrome.PrimaryText,SwiftFont.Weight.Semibold), MCPSettingsPage.Text(count.ToString(CultureInfo.InvariantCulture)+" tools \U000000B7 "+(connection?.ServerVersion??entry.Vendor),11,Chrome.SecondaryText) } }; // U+00B7 MIDDLE DOT
        Grid.SetColumn(copy,1); row.Children.Add(copy);
        var enabled = new SettingsSwitch(new Binding<bool>(()=>model.Mcp.Connection(entry.Id)?.IsEnabled??true, v=>model.Mcp.SetEnabled(v,entry))); Grid.SetColumn(enabled,2); row.Children.Add(enabled);
        var button = MCPSettingsPage.Options(()=>view.Menu=!view.Menu);
        var menus = new Panel { Children = { button, MCPSettingsPage.Popover(button,new Binding<bool>(()=>view.Menu,v=>view.Menu=v),BuildMenu,this), MCPSettingsPage.Popover(button,new Binding<bool>(()=>view.Tools,v=>view.Tools=v),BuildTools,this) } }; Grid.SetColumn(menus,3); row.Children.Add(menus);
        Child = new Border { Padding = new Thickness(16,11,Chrome.RowControlTrailingPadding,11), Child = row };
    }
    // :180-194
    private Control BuildMenu()
    {
        var model = AppEnvironment.GetAppModel(this);
        return new PopoverMenu(()=>new StackPanel { Children = {
            new PopoverItem("Refresh",()=>model.Mcp.Refresh(entry),symbol:"arrow.clockwise"),
            new PopoverItem("Show tools",()=>{view.Menu=false;view.Tools=true;},symbol:"wrench"),
            new PopoverDivider(), new PopoverItem("Disconnect",()=>model.Mcp.Disconnect(entry),symbol:"xmark.circle",isDestructive:true),
        } });
    }
    // :205-228
    private Control BuildTools()
    {
        var tools = AppEnvironment.GetAppModel(this).Mcp.Connection(entry.Id)?.Tools??ValueList<MCPToolSummary>.Empty;
        return new PopoverMenu(()=>
        {
            var stack = new StackPanel(); stack.Children.Add(new PopoverSectionHeader(entry.Name+" tools"));
            if(tools.Count==0) stack.Children.Add(new PopoverNote("No tools listed yet."));
            foreach(var tool in tools)
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing=8 };
                row.Children.Add(MCPSettingsPage.Text(tool.Name,11,Chrome.PrimaryText,mono:true));
                if(tool.Summary is {Length:>0} summary) { var text=MCPSettingsPage.Text(summary,11,Chrome.SecondaryText,lines:2); Grid.SetColumn(text,1);row.Children.Add(text); }
                stack.Children.Add(new Border { Padding=new Thickness(8,3),Child=row });
            }
            return stack;
        });
    }
}
internal sealed class MCPServerCard : Decorator
{
    private readonly MCPCatalogEntry entry; // :237
    private bool celebrating; // :238
    private OnChange<MCPConnectionState>? stateChanges;
    private sealed class ViewState : MainActorModel
    {
        private bool successPopped;
        public bool SuccessPopped { get => Read(successPopped); set => Set(ref successPopped, value); } // :240
    }
    private readonly ViewState view = new();
    // :249-303
    public MCPServerCard(MCPCatalogEntry entry, bool celebrating = false)
    {
        this.entry = entry; this.celebrating = celebrating; this.Track(() => AppEnvironment.GetAppModel(this).Mcp.States, _ => Build());
        AttachedToVisualTree += (_, _) => stateChanges ??= new OnChange<MCPConnectionState>(() => AppEnvironment.GetAppModel(this).Mcp.State(entry.Id), (_, now) =>
        {
            if (now is MCPConnectionState.NeedsSetup needs) MCPSetupPanel.Shared.Present(entry, needs.Guide, AppEnvironment.GetAppModel(this)); // :284-290
        });
        DetachedFromVisualTree += (_, _) => { stateChanges?.Dispose(); stateChanges = null; };
    }
    // :238,54. Update the ForEach input without replacing the card's identity or its state watcher.
    internal void UpdateCelebrating(bool value) { if (celebrating == value) return; celebrating = value; Build(); }
    // :249-303,305-349,360-375
    private void Build()
    {
        var state = AppEnvironment.GetAppModel(this).Mcp.State(entry.Id); var stack = new StackPanel { Spacing = 8 };
        if (celebrating)
        {
            var image = MCPSettingsPage.Symbol("checkmark.circle.fill", 34, Chrome.Success); var initialScale = view.SuccessPopped ? 1 : 0.5; image.RenderTransform = new ScaleTransform(initialScale, initialScale); image.RenderTransformOrigin = RelativePoint.Center; image.HorizontalAlignment = HorizontalAlignment.Center;
            // :371-374 use the shared frame seam and manual-clock-compatible source spring.
            image.AttachedToVisualTree += (_, _) =>
            {
                if (EnvironmentValues.GetAccessibilityReduceMotion(this) || view.SuccessPopped) return;
                view.SuccessPopped = true; // :371-374 onAppear only pops this card identity once
                var start = SwiftClock.Now;
                var animation = Animation.Spring(duration: 0.5, bounce: 0.4);
                var frames = FrameClock.Source.Current;
                Action<TimeSpan>? frame = null;
                frame = _ =>
                {
                    var elapsed = (SwiftClock.Now - start).TotalSeconds;
                    var complete = elapsed >= animation.Duration; // ED-011: shared spring settling duration, rather than its response
                    var scale = complete ? 1 : 0.5 + 0.5 * animation.Progress(elapsed);
                    image.RenderTransform = new ScaleTransform(scale, scale);
                    if (!complete) frames.RequestFrame(frame!);
                };
                image.DetachedFromVisualTree += (_, _) => frames.CancelFrame(frame!);
                frames.RequestFrame(frame);
            };
            var count = state is MCPConnectionState.Connected c ? c.ToolCount : 0;
            var success = new StackPanel { Spacing = 8, MinHeight = 120, VerticalAlignment = VerticalAlignment.Center, Children = { image, MCPSettingsPage.Text("Connected \U000000B7 " + count.ToString(CultureInfo.InvariantCulture) + " tools", 12, Chrome.PrimaryText, SwiftFont.Weight.Medium) } }; // U+00B7 MIDDLE DOT
            success.Children[1].HorizontalAlignment = HorizontalAlignment.Center; stack.Children.Add(success);
        }
        else
        {
            var name = new StackPanel { Spacing = 1, Children = { MCPSettingsPage.Text(entry.Name, 13, Chrome.PrimaryText, SwiftFont.Weight.Semibold), MCPSettingsPage.Text(entry.Vendor, 11, Chrome.SecondaryText) } };
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10 }; header.Children.Add(new MCPIcon(entry, 26)); Grid.SetColumn(name, 1); header.Children.Add(name);
            var action = Action(state); Grid.SetColumn(action, 2); header.Children.Add(action); stack.Children.Add(header);
            var summary = MCPSettingsPage.Text(entry.Summary, 12, Chrome.SecondaryText, lines: 2);
            // :260 reservesSpace: true. Measure two lines with this exact font, rather than guessing line height.
            var reserved = MCPSettingsPage.Text(" \n ", 12, Chrome.SecondaryText);
            reserved.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            summary.MinHeight = reserved.DesiredSize.Height;
            stack.Children.Add(summary);
            var tools = MCPSettingsPage.Text(entry.SampleTools.Count == 0 ? " " : string.Join(" \U000000B7 ", entry.SampleTools), 10.5, Chrome.SecondaryText.Opacity(0.8), mono: true, lines: 1); tools.Margin = new Thickness(0, 0, 24, 0); tools.TextTrimming = TextTrimming.CharacterEllipsis; tools.TextWrapping = TextWrapping.NoWrap; stack.Children.Add(tools); // U+00B7 MIDDLE DOT
            if (state is MCPConnectionState.Failed failed) stack.Children.Add(MCPSettingsPage.Text(failed.Message, 11, Chrome.Danger, lines: 3));
            if (state is MCPConnectionState.NeedsSetup needs) stack.Children.Add(MCPSettingsPage.Text(needs.Guide.Title, 11, Chrome.Warning, lines: 2));
        }
        var content = new Border { Padding = new Thickness(14), CornerRadius = new CornerRadius(14), Background = Chrome.PrimaryText.Opacity(0.05).Brush(stack), Child = stack }; var layers = new Panel { Children = { content } };
        if (!celebrating) layers.Children.Add(new MCPInfoButton(entry) { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(10) }); Child = layers;
    }
    // :322-349
    private Control Action(MCPConnectionState state) => state switch
    {
        MCPConnectionState.NotConnected => MCPSettingsPage.ActionButton(entry.IsOAuth ? "Sign in" : "Connect", () => MCPSettingsPage.Begin(this, entry)),
        MCPConnectionState.Connecting => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new WorkingSpinner(3), FixedLabel(entry.IsOAuth ? "Waiting" : "Connecting") } },
        MCPConnectionState.Connected => new Border(),
        MCPConnectionState.NeedsSetup n => MCPSettingsPage.ActionButton("Fix it", () => MCPSetupPanel.Shared.Present(entry, n.Guide, AppEnvironment.GetAppModel(this)), true),
        MCPConnectionState.Failed => MCPSettingsPage.ActionButton(entry.IsOAuth ? "Sign in again" : "Try again", () => MCPSettingsPage.Begin(this, entry)),
        _ => throw new UnreachableException(),
    };
    // :334-337 .fixedSize()
    private static TextBlock FixedLabel(string label)
    {
        var text = MCPSettingsPage.Text(label, 11, Chrome.SecondaryText); text.TextWrapping = TextWrapping.NoWrap; return text;
    }
}

internal sealed class MCPCustomSection : Decorator
{
    private sealed class ViewState : MainActorModel
    {
        private bool adding; // :385
        public bool Adding { get => Read(adding); set => Set(ref adding, value); }
    }
    private readonly ViewState state = new();
    // :387-415
    public MCPCustomSection(string query)
    {
        this.Track(() => AppEnvironment.GetAppModel(this).Mcp.Customs, customs =>
        {
            var visible = customs.Where(c => MCPSettingsPage.Matches(c.CatalogEntry(), query)).ToArray();
            if (visible.Length == 0 && query.Trimmed().Length > 0) { Child = null; return; }
            var stack = new StackPanel();
            if (visible.Length == 0)
            {
                var note = MCPSettingsPage.Text("Servers of your own: a command that runs on this PC, or a remote address, with the variables they need.", 12, Chrome.SecondaryText); note.Margin = new Thickness(16, 11); stack.Children.Add(note);
            }
            else for (var i = 0; i < visible.Length; i++) { if (i > 0) stack.Children.Add(new ChromeRowDivider()); stack.Children.Add(new MCPCustomRow(visible[i])); }
            stack.Children.Add(new ChromeRowDivider()); // :403,409
            // :417-436 the whole row is the rectangular hit area.
            var addRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 12 };
            var addGlyph = MCPSettingsPage.Symbol("plus.circle", 13, Chrome.Accent); addGlyph.HorizontalAlignment = HorizontalAlignment.Center;
            addRow.Children.Add(new Border { Width = 26, Child = addGlyph });
            var addText = MCPSettingsPage.Text("Add server", 13, Chrome.PrimaryText, SwiftFont.Weight.Medium); Grid.SetColumn(addText, 1); addRow.Children.Add(addText);
            var spacer = new Border { MinWidth = 12 }; Grid.SetColumn(spacer, 2); addRow.Children.Add(spacer);
            var add = PlainControls.Button(new Border { Background = Brushes.Transparent, Padding = new Thickness(16, 11, Chrome.RowControlTrailingPadding, 11), Child = addRow }, () => state.Adding = true);
            stack.Children.Add(new Panel { Children = { add, MCPSettingsPage.Popover(add, new Binding<bool>(() => state.Adding, v => state.Adding = v), () => new MCPCustomEditor(null), this) } });
            Child = new ChromeSection("Custom", () => new ChromeCard(() => stack, lazy: true));
        });
    }
}

internal sealed class MCPCustomRow : Decorator
{
    private readonly MCPCustomServer server; // :449
    private sealed class ViewState : MainActorModel
    {
        private bool menu, editing, confirming;
        public bool Menu { get => Read(menu); set => Set(ref menu, value); } // :451
        public bool Editing { get => Read(editing); set => Set(ref editing, value); } // :452
        public bool Confirming { get => Read(confirming); set => Set(ref confirming, value); } // OD-0012 E1
    }
    private readonly ViewState view = new();
    private Control? DeleteConfirmation { get; set; }
    // :463-480
    public MCPCustomRow(MCPCustomServer server) { this.server = server; this.Track(() => (AppEnvironment.GetAppModel(this).Mcp.States, AppEnvironment.GetAppModel(this).Mcp.Connections), _ => Build()); }
    // :463-529
    private void Build()
    {
        var model = AppEnvironment.GetAppModel(this); var entry = server.CatalogEntry(); var state = model.Mcp.State(server.Id); var connection = model.Mcp.Connection(server.Id);
        var count = state is MCPConnectionState.Connected c ? c.ToolCount : connection?.Tools.Count ?? 0;
        var status = state switch
        {
            MCPConnectionState.Connected => MCPSettingsPage.Text(count.ToString(CultureInfo.InvariantCulture) + " tools \U000000B7 " + (connection?.ServerVersion ?? entry.Vendor), 11, Chrome.SecondaryText), // U+00B7 MIDDLE DOT
            MCPConnectionState.Failed f => MCPSettingsPage.Text(f.Message, 11, Chrome.Danger, lines: 3),
            _ => MCPSettingsPage.Text(server.Summary, 11, Chrome.SecondaryText),
        };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto"), ColumnSpacing = 12 };
        row.Children.Add(new Border { Width = 26, Child = new MCPIcon(entry, 22) { HorizontalAlignment = HorizontalAlignment.Center } });
        var copy = new StackPanel { Spacing = 2, Children = { MCPSettingsPage.Text(server.Name, 13, Chrome.PrimaryText, SwiftFont.Weight.Semibold), status } }; Grid.SetColumn(copy, 1); row.Children.Add(copy);
        var spacer = new Border { MinWidth = 12 }; Grid.SetColumn(spacer, 2); row.Children.Add(spacer);
        Control action = state switch
        {
            MCPConnectionState.NotConnected => MCPSettingsPage.ActionButton("Connect", () => MCPSettingsPage.Begin(this, entry)),
            MCPConnectionState.Connecting => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new WorkingSpinner(3), ConnectingLabel() } },
            MCPConnectionState.Connected => new SettingsSwitch(new Binding<bool>(() => model.Mcp.Connection(server.Id)?.IsEnabled ?? true, v => model.Mcp.SetEnabled(v, entry))),
            MCPConnectionState.NeedsSetup n => MCPSettingsPage.ActionButton("Fix it", () => MCPSetupPanel.Shared.Present(entry, n.Guide, model), true),
            MCPConnectionState.Failed => MCPSettingsPage.ActionButton("Try again", () => MCPSettingsPage.Begin(this, entry)),
            _ => throw new UnreachableException(),
        };
        Grid.SetColumn(action, 3); row.Children.Add(action); var menu = MCPSettingsPage.Options(() => view.Menu = !view.Menu);
        var menus = new Panel { Children = { menu,
            MCPSettingsPage.Popover(menu, new Binding<bool>(() => view.Menu, v => view.Menu = v), BuildMenu, this),
            MCPSettingsPage.Popover(menu, new Binding<bool>(() => view.Editing, v => view.Editing = v), () => new MCPCustomEditor(server), this),
            MCPSettingsPage.Popover(menu, new Binding<bool>(() => view.Confirming, v => view.Confirming = v), () => DeleteConfirmation!, this),
        } }; Grid.SetColumn(menus, 4); row.Children.Add(menus);
        Child = new Border { Padding = new Thickness(16, 11, Chrome.RowControlTrailingPadding, 11), Child = row };
    }
    // :508-515 .font(.system(.subheadline)) and .fixedSize().
    private TextBlock ConnectingLabel()
    {
        var label = new TextBlock { Text = "Connecting", Foreground = Chrome.SecondaryText.Brush(this), TextWrapping = TextWrapping.NoWrap };
        SwiftFont.System(SwiftFont.TextStyle.Subheadline).Apply(label);
        return label;
    }
    // :554-568
    private Control BuildMenu()
    {
        var model = AppEnvironment.GetAppModel(this);
        return new PopoverMenu(() => new StackPanel { Children = {
            new PopoverItem("Edit", () => { view.Menu = false; view.Editing = true; }, symbol: "pencil"),
            new PopoverItem("Disconnect", () => model.Mcp.Disconnect(server.CatalogEntry()), symbol: "xmark.circle", isEnabled: model.Mcp.State(server.Id).IsConnected, isDestructive: true),
            new PopoverDivider(), new PopoverItem("Delete", Delete, symbol: "trash", isDestructive: true),
        } });
    }
    // :564-566. DEVIATION OD-0008 A1, OD-0012 E1: gate on confirmBeforeDeleting.
    private void Delete()
    {
        var model = AppEnvironment.GetAppModel(this);
        if (!model.Settings.ConfirmBeforeDeleting) { model.Mcp.DeleteCustom(server); return; }
        DeleteConfirmation = new PopoverMenu(() => new StackPanel { Children = {
            new PopoverSectionHeader("Delete this server?"),
            new PopoverNote("\U0000201C" + server.Name + "\U0000201D and the values it keeps in Credential Manager will be removed."), // U+201C/201D DOUBLE QUOTATION MARKS
            new PopoverDivider(), new PopoverItem("Delete server", () => { model.Mcp.DeleteCustom(server); view.Confirming = false; }, symbol: "trash", isDestructive: true),
        } });
        Popovers.SetClosePopover(DeleteConfirmation, () => view.Confirming = false); view.Confirming = true;
    }
}

internal sealed class MCPCustomEditor : Decorator
{
    private const double MCPCustomEditorWidth = 380; // :624
    private const double SectionSpacing = 12; // :625
    private const double OuterPadding = 16; // :626
    private const double VariableRowHeight = 34; // :627
    private readonly MCPCustomServer? server; // :585
    // :587-594. R-VIEW-2 / R-MODEL-3: state read by the form is observable and lists have value semantics.
    private sealed class ViewState : MainActorModel
    {
        private string name = "", command = "", argsText = "", url = "";
        private MCPCustomServer.Kind kind;
        private ValueList<VariableDraft> variables = ValueList<VariableDraft>.Empty;
        public string Name { get => Read(name); set => Set(ref name, value); }
        public string Command { get => Read(command); set => Set(ref command, value); }
        public string ArgsText { get => Read(argsText); set => Set(ref argsText, value); }
        public string Url { get => Read(url); set => Set(ref url, value); }
        public MCPCustomServer.Kind Kind { get => Read(kind); set => Set(ref kind, value); }
        public ValueList<VariableDraft> Variables { get => Read(variables); set => Set(ref variables, value); }
    }
    private readonly ViewState state = new();
    private string name { get => state.Name; set => state.Name = value; } // :587
    private string command { get => state.Command; set => state.Command = value; } // :589
    private string argsText { get => state.ArgsText; set => state.ArgsText = value; } // :590
    private string url { get => state.Url; set => state.Url = value; } // :591
    private MCPCustomServer.Kind kind { get => state.Kind; set => state.Kind = value; } // :588
    private ValueList<VariableDraft> variables { get => state.Variables; set => state.Variables = value; } // :592
    private readonly Dictionary<Guid, Grid> variableRows = new(); // :796 ForEach identity
    private StackPanel? variablesHost;
    private ForEach<VariableDraft, Guid>? variableForEach;
    private Control? nameSection, kindSection;
    private string? savedID; // :594
    private readonly Grid root = new() { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = SectionSpacing }; // :630-656
    private readonly StackPanel form = new() { Spacing = SectionSpacing }; // :633
    private readonly StackPanel footer = new() { Spacing = 10 }; // :690
    private TextBox? nameField;
    // :614-622,629-677
    public MCPCustomEditor(MCPCustomServer? server)
    {
        this.server = server; name = server?.Name ?? ""; kind = server?.ServerKind ?? MCPCustomServer.Kind.Stdio;
        command = server?.Command ?? ""; argsText = string.Join("\n", server?.Args ?? ValueList<string>.Empty); url = server?.Url ?? "";
        variables = new ValueList<VariableDraft>((server?.Variables ?? ValueList<MCPCustomServer.Variable>.Empty).Select(v => new VariableDraft(v))); Width = MCPCustomEditorWidth;
        var scroll = new ScrollViewer { Content = form, Template = EditorScrollTemplate, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; Grid.SetRow(scroll, 1); root.Children.Add(scroll); Grid.SetRow(footer, 2); root.Children.Add(footer);
        Child = new Border { Padding = new Thickness(OuterPadding), Child = root }; RebuildForm(); BuildFooter(null);
        AttachedToVisualTree += (_, _) =>
        {
            if (server is null) Avalonia.Threading.Dispatcher.UIThread.Post(() => nameField?.Focus()); // :663-665
            else
            {
                var model = AppEnvironment.GetAppModel(this);
                for (var i = 0; i < variables.Count; i++) if (model.Mcp.StoredValue(server.Id, variables[i].Key, variables[i].IsSecret) is { } stored) variables = variables.SetItem(i, variables[i] with { Value = stored }); // :666-674
            }
        };
    }
    // :601-612
    private sealed record VariableDraft
    {
        public Guid Id { get; } = Guid.NewGuid(); // :602
        public string Key { get; init; } = ""; // :603
        public string Value { get; init; } = ""; // :604
        public bool IsSecret { get; init; } = false; // :605
        public VariableDraft() { } // :607
        public VariableDraft(MCPCustomServer.Variable variable) { Key = variable.Key; IsSecret = variable.IsSecret; } // :608-611
    }
    // :682-685
    private void ResizeForm() => Height = Math.Min(560, (kind == MCPCustomServer.Kind.Stdio ? 450 : 320) + variables.Count * VariableRowHeight);
    // :635-653,842-844
    private void SetKind(MCPCustomServer.Kind value) { kind = value; RebuildForm(); }
    // :629-658,706-839
    private void RebuildForm()
    {
        ResizeForm(); if (root.Children.Count > 2) root.Children.RemoveAt(2); var header = BuildHeader(); Grid.SetRow(header, 0); root.Children.Add(header);
        variablesHost?.Children.Clear(); form.Children.Clear(); // :796 retain each variable's controls by its ID
        if (nameSection is null) { nameField = EditorField("My server", () => name, v => name = v, name: true); nameSection = Field("Name", MCPSettingsPage.McpFieldSurface(nameField)); }
        form.Children.Add(nameSection);
        kindSection ??= Field("Kind", new GlassPickerButton<MCPCustomServer.Kind>(new[] { (MCPCustomServer.Kind.Stdio, "stdio \U00002014 a command on this PC"), (MCPCustomServer.Kind.Http, "http \U00002014 a remote server") }, new Binding<MCPCustomServer.Kind>(() => kind, SetKind), symbol: v => v == MCPCustomServer.Kind.Stdio ? "terminal" : "network")); form.Children.Add(kindSection); // U+2014 EM DASH
        if (kind == MCPCustomServer.Kind.Stdio)
        {
            form.Children.Add(Field("Command", MCPSettingsPage.McpFieldSurface(EditorField("npx", () => command, v => command = v))));
            var args = EditorField("", () => argsText, v => argsText = v); args.AcceptsReturn = true; args.TextWrapping = TextWrapping.Wrap; args.Height = 66; args.Padding = new Thickness(6, 4);
            form.Children.Add(Field("Arguments", new StackPanel { Children = { MCPSettingsPage.McpFieldSurface(args), MCPSettingsPage.Text("One argument per line; empty lines are dropped.", 10.5, Chrome.SecondaryText) } }));
        }
        else form.Children.Add(Field("URL", MCPSettingsPage.McpFieldSurface(EditorField("https://\U00002026", () => url, v => url = v)))); // U+2026 HORIZONTAL ELLIPSIS
        var rows = new StackPanel { Spacing = 5 }; variablesHost = rows;
        // :796 all row inputs read the state binding by ID; ForEach preserves attached controls on append/remove.
        variableForEach = new ForEach<VariableDraft, Guid>(rows, v => v.Id, VariableRow, (_, _) => { }); variableForEach.Apply(variables);
        var addText = kind == MCPCustomServer.Kind.Stdio ? "Add environment variable" : "Add header";
        var add = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { MCPSettingsPage.Symbol("plus", 12, Chrome.Accent).Font(SwiftFont.System(12, SwiftFont.Weight.Medium)), MCPSettingsPage.Text(addText, 12, Chrome.Accent, SwiftFont.Weight.Medium) } };
        rows.Children.Add(PlainControls.Button(add, () => { variables = variables.Appending(new VariableDraft()); variableForEach.Apply(variables); ResizeForm(); }));
        rows.Children.Add(MCPSettingsPage.Text("Locked values are kept in the Credential Manager.", 10.5, Chrome.SecondaryText)); form.Children.Add(Field(kind == MCPCustomServer.Kind.Stdio ? "Environment" : "Headers", rows));
    }
    // :706-729
    private Control BuildHeader()
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10 }; row.Children.Add(new MCPIcon(Draft.CatalogEntry(), 26));
        var copy = new StackPanel { Spacing = 1, Children = { MCPSettingsPage.Text(server?.Name ?? "New server", 14, Chrome.PrimaryText, SwiftFont.Weight.Semibold), MCPSettingsPage.Text("Custom \U000000B7 " + (kind == MCPCustomServer.Kind.Stdio ? "stdio" : "http"), 11, Chrome.SecondaryText) } }; Grid.SetColumn(copy, 1); row.Children.Add(copy); // U+00B7 MIDDLE DOT
        var close = PlainControls.Button(MCPSettingsPage.Symbol("xmark", 11, Chrome.SecondaryText).Font(SwiftFont.System(11, SwiftFont.Weight.Semibold)), () => Popovers.GetClosePopover(this)?.Invoke()); close.Width = 24; close.Height = 24; AutomationProperties.SetName(close, "Close"); Grid.SetColumn(close, 2); row.Children.Add(close); return row;
    }
    // :747-753
    private static Control Field(string label, Control content) => new StackPanel { Spacing = 5, Children = { MCPSettingsPage.Text(label, 11, Chrome.SecondaryText, SwiftFont.Weight.Semibold), content } };
    // :732-742,756-774
    private static TextBox EditorField(string prompt, Func<string> read, Action<string> write, bool secret = false, bool name = false)
    {
        var box = PlainControls.TextField(prompt, new Binding<string>(read, write)); if (secret) box.PasswordChar = '\u2022'; // U+2022 BULLET
        box.Template = EditorFieldTemplate; box.FocusAdorner = null; box.Padding = new Thickness(10, 7);
        SwiftFont.System(name ? 15 : 12, name ? SwiftFont.Weight.Semibold : SwiftFont.Weight.Regular, name ? SwiftFont.Design.Default : SwiftFont.Design.Monospaced).Apply(box); box.Foreground = Chrome.PrimaryText.Brush(box); return box;
    }
    // :756-774. Local plain template carries padding and SecureField masking to the actual presenter.
    // Mirrors PlainControls' plain field; the shared template currently omits these properties.
    private static readonly FuncControlTemplate<TextBox> EditorFieldTemplate = new((box, scope) =>
    {
        var prompt = new TextBlock
        {
            IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center,
            [!TextBlock.TextProperty] = box[!TextBox.PlaceholderTextProperty],
            [!TextBlock.ForegroundProperty] = box[!TextBox.PlaceholderForegroundProperty],
        };
        var presenter = new TextPresenter
        {
            Name = "PART_TextPresenter", VerticalAlignment = VerticalAlignment.Center,
            [!TextPresenter.TextProperty] = box[!TextBox.TextProperty],
            [!TextPresenter.PasswordCharProperty] = box[!TextBox.PasswordCharProperty],
            [!TextPresenter.RevealPasswordProperty] = box[!TextBox.RevealPasswordProperty],
            [!TextPresenter.TextWrappingProperty] = box[!TextBox.TextWrappingProperty],
        }.RegisterInNameScope(scope);
        void ShowPrompt() => prompt.IsVisible = string.IsNullOrEmpty(box.Text);
        box.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) ShowPrompt(); };
        ShowPrompt();
        Control text = new Panel { Children = { prompt, presenter } };
        if (box.AcceptsReturn) text = new ScrollViewer { Name = "PART_ScrollViewer", Template = EditorScrollTemplate, Content = text, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }.RegisterInNameScope(scope); // :781-787 TextEditor
        return new Border { [!Border.PaddingProperty] = box[!TextBox.PaddingProperty], Child = text };
    });
    // :632-655,659-662,781-787. Same content-presenter / gesture recipe as SidebarView's ListScrollTemplate.
    // The app deliberately has no theme to supply templates. Native scroll-indicator appearance is UNVERIFIED OD-0001.
    private static readonly FuncControlTemplate<ScrollViewer> EditorScrollTemplate = new((viewer, scope) =>
    {
        var presenter = new ScrollContentPresenter { Name = "PART_ContentPresenter", [!ContentPresenter.ContentProperty] = viewer[!ContentControl.ContentProperty] };
        presenter.GestureRecognizers.Add(new ScrollGestureRecognizer { CanVerticallyScroll = true, CanHorizontallyScroll = false });
        return presenter.RegisterInNameScope(scope);
    });
    // :794-840
    private Control VariableRow(VariableDraft variable)
    {
        if (variableRows.TryGetValue(variable.Id, out var existing)) return existing;
        int Index() => Array.FindIndex(variables.ToArray(), v => v.Id == variable.Id);
        void Edit(Func<VariableDraft, VariableDraft> update) { var index = Index(); if (index >= 0) variables = variables.SetItem(index, update(variables[index])); }
        var key = EditorField("KEY", () => variables[Index()].Key, v => Edit(old => old with { Key = v }));
        var value = EditorField("value", () => variables[Index()].Value, v => Edit(old => old with { Value = v }), variable.IsSecret);
        var toggle = PlainControls.Button(MCPSettingsPage.Symbol(variable.IsSecret ? "lock.fill" : "lock.open", 12, variable.IsSecret ? Chrome.Accent : Chrome.SecondaryText).Font(SwiftFont.System(12, SwiftFont.Weight.Medium)), () => Edit(old => old with { IsSecret = !old.IsSecret })); ToolTip.SetTip(toggle, variable.IsSecret ? "Kept in the Credential Manager" : "Stored in mcp.json; click to keep it in the Credential Manager instead");
        var remove = PlainControls.Button(MCPSettingsPage.Symbol("minus.circle", 12, Chrome.SecondaryText).Font(SwiftFont.System(12, SwiftFont.Weight.Medium)), () => { variables = variables.Mutating(list => list.RemoveAll(v => v.Id == variable.Id)); variableForEach!.Apply(variables); ResizeForm(); variableRows.Remove(variable.Id); }); ToolTip.SetTip(remove, "Remove this variable"); toggle.Width = toggle.Height = remove.Width = remove.Height = 24;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,Auto,Auto"), ColumnSpacing = 6 };
        foreach (var (control, column) in new[] { (MCPSettingsPage.McpFieldSurface(key), 0), (MCPSettingsPage.McpFieldSurface(value), 1), ((Control)toggle, 2), ((Control)remove, 3) }) { Grid.SetColumn(control, column); row.Children.Add(control); }
        var locked = variable.IsSecret;
        void RenderSecret(bool isSecret)
        {
            if (locked == isSecret) return;
            locked = isSecret;
            var replacement = MCPSettingsPage.McpFieldSurface(EditorField("value", () => variables[Index()].Value, v => Edit(old => old with { Value = v }), isSecret));
            Grid.SetColumn(replacement, 1); row.Children.RemoveAt(1); row.Children.Insert(1, replacement);
            toggle.Content = MCPSettingsPage.Symbol(isSecret ? "lock.fill" : "lock.open", 12, isSecret ? Chrome.Accent : Chrome.SecondaryText).Font(SwiftFont.System(12, SwiftFont.Weight.Medium));
            ToolTip.SetTip(toggle, isSecret ? "Kept in the Credential Manager" : "Stored in mcp.json; click to keep it in the Credential Manager instead");
        }
        row.Track(() => variables[Index()].IsSecret, RenderSecret);
        toggle.Click += (_, _) => RenderSecret(variables[Index()].IsSecret); // :804-814 immediate branch swap on activation
        variableRows.Add(variable.Id, row); return row;
    }
    // :845-862
    private MCPCustomServer Draft
    {
        get
        {
            var kept = new ValueList<MCPCustomServer.Variable>(variables.Select(v => new MCPCustomServer.Variable(FoundationText.Trim(v.Key), v.IsSecret)).Where(v => v.Key.Length > 0));
            return new MCPCustomServer(server?.Id ?? savedID ?? MCPCustomServer.MakeID(name), name, kind)
            {
                Command = command.Trimmed(), Url = url.Trimmed(), Args = new ValueList<string>(argsText.Split("\n").Where(v => FoundationText.Trim(v).Length > 0)),
                Env = kind == MCPCustomServer.Kind.Stdio ? kept : ValueList<MCPCustomServer.Variable>.Empty,
                Headers = kind == MCPCustomServer.Kind.Http ? kept : ValueList<MCPCustomServer.Variable>.Empty,
            };
        }
    }
    // :687-704
    private void BuildFooter(string? error)
    {
        footer.Children.Clear(); if (error is not null) footer.Children.Add(MCPSettingsPage.Text(error, 11, Chrome.Danger, lines: 3));
        var save = MCPSettingsPage.ActionButton(server is null ? "Add server" : "Save", Save, true); save.HorizontalAlignment = HorizontalAlignment.Right; footer.Children.Add(save);
    }
    // :864-889
    private void Save()
    {
        var model = AppEnvironment.GetAppModel(this); var draft = Draft; var wasConnected = server is not null && model.Mcp.State(server.Id).IsConnected;
        if (model.Mcp.SaveCustom(draft, server?.Id ?? savedID) is { } failure) { BuildFooter(failure.Message); return; } savedID = draft.Id;
        var keptValues = variables.Select(v => (Key: FoundationText.Trim(v.Key), v.Value, v.IsSecret)).Where(v => v.Key.Length > 0).ToArray();
        if (!model.Mcp.SaveCustomValues(keptValues, draft.Id, server?.CatalogEntry())) { BuildFooter("The Credential Manager could not save a value. Keep this editor open and try again."); return; }
        if (wasConnected && model.Mcp.Entry(draft.Id) is { } entry) model.Mcp.Refresh(entry); Popovers.GetClosePopover(this)?.Invoke();
    }
}

internal sealed class MCPInfoButton : Decorator
{
    // :905-909
    public MCPInfoButton(MCPCatalogEntry entry) => Child = new MCPHoverInfoButton("About " + entry.Name, () => new MCPInfoPopover(entry));
}

internal sealed class MCPHoverInfoButton : Decorator
{
    private sealed class ViewState : MainActorModel
    {
        private bool presented, button, popover;
        public bool Presented { get => Read(presented); set => Set(ref presented, value); } // :918
        public bool Button { get => Read(button); set => Set(ref button, value); } // :919
        public bool Popover { get => Read(popover); set => Set(ref popover, value); } // :920
    }
    private readonly ViewState state = new();
    private TaskHandle? settle; // :921
    private bool IsPresented => state.Presented;
    // :923-949
    public MCPHoverInfoButton(string label, Func<Control> content)
    {
        var image = MCPSettingsPage.Symbol("info.circle", 12, Chrome.SecondaryText.Opacity(0.55)).Font(SwiftFont.System(12, SwiftFont.Weight.Medium));
        var button = PlainControls.Button(image, () => { settle?.Cancel(); state.Presented = !state.Presented; }); button.Width = 22; button.Height = 22; button.Focusable = false; AutomationProperties.SetName(button, label);
        button.OnHover(over => { state.Button = over; Reconsider(); });
        image.Track(() => state.Button || state.Presented, full => image.ForegroundStyle(Chrome.SecondaryText.Opacity(full ? 1 : 0.55)));
        var popup = MCPSettingsPage.Popover(button, new Binding<bool>(() => state.Presented, value => state.Presented = value), () => content().OnHover(over => { state.Popover = over; Reconsider(); }), this);
        Child = new Panel { Children = { button, popup } };
    }
    // :953-961
    private void Reconsider()
    {
        settle?.Cancel(); var wanted = state.Button || state.Popover; if (wanted == state.Presented) return;
        settle = MainActor.Spawn(async () => { await SwiftTask.TrySleep(TimeSpan.FromMilliseconds(wanted ? 220 : 320)); if (SwiftTask.IsCancelled || (state.Button || state.Popover) != wanted) return; state.Presented = wanted; });
    }
}

internal sealed class MCPInfoPopover : Decorator
{
    private const double MCPInfoPopoverWidth = 340; // :971
    // :973-1024,1039-1064
    public MCPInfoPopover(MCPCatalogEntry entry)
    {
        var stack = new StackPanel { Spacing = 14 };
        stack.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { new MCPIcon(entry, 28), new StackPanel { Spacing = 1, Children = { MCPSettingsPage.Text(entry.Name, 14, Chrome.PrimaryText, SwiftFont.Weight.Semibold), MCPSettingsPage.Text(entry.Vendor + " \U000000B7 " + entry.Category.Title(), 11, Chrome.SecondaryText) } } } }); // U+00B7 MIDDLE DOT
        stack.Children.Add(MCPSettingsPage.Text(entry.Summary, 12, Chrome.PrimaryText.Opacity(0.9)));
        var connection = entry.Transport switch
        {
            MCPTransport.Stdio s => "Runs on this PC through " + s.Command + " (" + (s.Args.FirstOrDefault(v => !SwiftString.HasPrefix(v, "-")) ?? s.Command) + ").",
            MCPTransport.Http h => "Remote server at " + Host(h.Url) + ".",
            MCPTransport.Oauth o => "Remote server at " + Host(entry.Fill(o.Url, ValueMap<string, string>.Empty)) + ". You sign in once in your browser.",
            _ => throw new UnreachableException(),
        };
        var required = entry.Fields.Where(f => f.IsRequired).Select(f => f.Label).ToList(); var optional = entry.Fields.Where(f => !f.IsRequired).Select(f => f.Label).ToArray(); string needs;
        if (required.Count == 0 && optional.Length == 0) needs = entry.IsOAuth ? "Only your sign-in." : "Nothing \U00002014 press Connect."; // U+2014 EM DASH
        else { if (optional.Length > 0) required.Add("optional: " + SwiftString.Lowercased(string.Join(", ", optional))); needs = string.Join("; ", required) + "."; }
        stack.Children.Add(new StackPanel { Spacing = 8, Children = { Detail("Connects", connection), Detail("Needs", needs) } });
        if (entry.SampleTools.Count > 0) stack.Children.Add(new StackPanel { Spacing = 6, Children = { MCPSettingsPage.Text("Tools", 11, Chrome.SecondaryText, SwiftFont.Weight.Semibold), new FlowChips(entry.SampleTools) } });
        if (entry.Fields.FirstOrDefault(f => !f.IsRequired) is { } field) stack.Children.Add(PlainControls.Button(MCPSettingsPage.Text("Change the " + SwiftString.Lowercased(field.Label) + "\U00002026", 12, Chrome.Accent, SwiftFont.Weight.Medium), () => MCPKeyPanel.Shared.Present(entry, AppEnvironment.GetAppModel(this)))); // U+2026 HORIZONTAL ELLIPSIS
        if (entry.DocsURL.Length > 0 && SwiftURL.Create(entry.DocsURL) is { } docs) stack.Children.Add(PlainControls.Button(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { MCPSettingsPage.Text("Read the docs", 12, Chrome.Accent, SwiftFont.Weight.Medium), MCPSettingsPage.Symbol("arrow.up.right", 10, Chrome.Accent).Font(SwiftFont.System(10, SwiftFont.Weight.Bold)) } }, () => MCPSettingsPage.OpenURL(this, docs)));
        Child = new Border { Padding = new Thickness(16), Width = MCPInfoPopoverWidth, Child = stack };
    }
    // :1026-1036
    private static Control Detail(string label, string value)
    {
        var title = MCPSettingsPage.Text(label, 11, Chrome.SecondaryText, SwiftFont.Weight.Semibold); title.Width = 58;
        var detail = MCPSettingsPage.Text(value, 11.5, Chrome.PrimaryText.Opacity(0.9));
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 }; row.Children.Add(title); Grid.SetColumn(detail, 1); row.Children.Add(detail); return row;
    }
    // :1062-1064
    private static string Host(string of) => SwiftURL.Create(of)?.Host ?? of;
}

internal sealed class FlowChips : Decorator
{
    // :1071-1084
    public FlowChips(IReadOnlyList<string> items)
    {
        var flow = new FlowLayout(); foreach (var item in items) flow.Children.Add(new Border { Padding = new Thickness(7, 3), CornerRadius = new CornerRadius(6), Background = Chrome.Overlay(0.08).Brush(flow), Child = MCPSettingsPage.Text(item, 10.5, Chrome.PrimaryText.Opacity(0.85), mono: true) }); Child = flow;
    }
}
internal sealed class FlowLayout : Panel
{
    public double Spacing { get; init; } = 6; // :1089
    // :1091-1106
    protected override Size MeasureOverride(Size availableSize)
    {
        double x = 0, y = 0, rowHeight = 0, maxX = 0;
        foreach (var child in Children) { child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); var size = child.DesiredSize; if (x > 0 && x + size.Width > availableSize.Width) { x = 0; y += rowHeight + Spacing; rowHeight = 0; } x += size.Width + Spacing; rowHeight = Math.Max(rowHeight, size.Height); maxX = Math.Max(maxX, x - Spacing); }
        return new Size(double.IsPositiveInfinity(availableSize.Width) ? maxX : availableSize.Width, y + rowHeight);
    }
    // :1108-1121
    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0, y = 0, rowHeight = 0;
        foreach (var child in Children) { var size = child.DesiredSize; if (x > 0 && x + size.Width > finalSize.Width) { x = 0; y += rowHeight + Spacing; rowHeight = 0; } child.Arrange(new Rect(new Point(x, y), size)); x += size.Width + Spacing; rowHeight = Math.Max(rowHeight, size.Height); } return finalSize;
    }
}
public sealed class MCPIcon : Decorator
{
    // :1125-1140
    public MCPIcon(MCPCatalogEntry entry, double size = 20)
    {
        var image = entry.Asset.Length == 0 ? SwiftImage.SystemName("server.rack") : SwiftImage.Named(entry.Asset);
        var color = entry.Asset.Length == 0 || entry.IsMonochrome ? Chrome.PrimaryText : SwiftColor.Rgb(entry.ColorValue);
        image.Resizable().RenderingMode(ImageRenderingMode.Template).AspectRatio(ContentMode.Fit).ForegroundStyle(color); image.Width = size; image.Height = size; Child = image;
    }
}


