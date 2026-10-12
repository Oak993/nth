using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Port;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

using Marrow.App.Views.Tour;

namespace Marrow.App.Views.Palette;

// Source: DroppyCode/App/Views/Palette/CommandPalette.swift (B3 52bc792), :1-211.
// Records: PR-TERM-071, PR-WIN-159, PR-WIN-160, PR-WIN-161, PR-WIN-162, PR-WIN-163, PR-WIN-164, PR-WIN-165,
// PR-WIN-166, PR-WIN-167, PR-WIN-168, PR-WIN-169, PR-WIN-170, PR-WIN-171, PR-WIN-172, PR-WIN-173, PR-WIN-174,
// PR-WIN-175, PR-WIN-176, PR-WIN-177, PR-WIN-178, PR-WIN-179, UI-GIT-025, UI-TERM-033, UI-WIN-028, UI-WIN-029,
// UI-WIN-030, UI-WIN-031, UI-WIN-032, UI-WIN-033.
// Forms (ED-095): a SwiftUI View is a Decorator whose constructor builds `body`; `@Environment(AppModel.self)` is
// read from the environment, not passed in, so the view takes no inputs. As NewThreadPrompt.cs does, the whole
// build is deferred to attach time through `this.Track(() => AppEnvironment.GetAppModel(this), Build)`, because the
// environment attached property is not yet set on `this` while its own constructor runs.
// `@State private var query/selection/items` (:8-12), all read by body, live on a private ViewState : MainActorModel
// (R-VIEW-2). `private func row(_:isSelected:)` (:110-147) becomes a nested Decorator (Row), not a function,
// because ForEach (R-VIEW-8) needs one persistent control per surviving row id whose isSelected can change (hover,
// arrow keys) without the row being rebuilt; PR-WIN-178 requires exactly that: rows are recomputed only when the
// five-field ResultsKey changes, never on selection or hover. `@Environment(\.colorScheme)` (:6) reads
// PlainControls.IsDark(ActualThemeVariant) (R-VIEW-6).
using Chrome = Marrow.UI.Chrome.Chrome; // ED-095: the Swift type Chrome, not this file's own namespace

/// <summary>:3-211 <c>struct CommandPalette: View</c>: search threads, projects and actions.</summary>
public sealed class CommandPalette : Decorator
{
    /// <summary>:23-31 <c>private struct Item: Identifiable</c>. Internal, not private (R-NAME-6): the file-scoped
    /// CommandPaletteRow below (kept file-scoped, as DownloadRow is, so a Row control is only ever reached from a
    /// test through the generic Decorator/Control it downcasts to, per Swift's own file-private) needs it.</summary>
    internal sealed record Item(
        string Id, // :24
        string Title, // :25
        string? Subtitle, // :26
        ProjectIcon? Icon, // :27
        string Symbol, // :28
        string? Shortcut, // :29
        Action Perform); // :30

    /// <summary>:8-13 the view's own state, all read by body (R-VIEW-2).</summary>
    private sealed class ViewState : MainActorModel
    {
        private string query = ""; // :8
        public string Query { get => Read(query); set => Set(ref query, value); }

        private int selection; // :9
        public int Selection { get => Read(selection); set => Set(ref selection, value); }

        // :10-12 "The rows for the query as the library stands: built when either changes, not on every arrow
        // key or selection change." Kept for Run()'s bounds check and the down-arrow clamp; the visible rows
        // themselves are refreshed directly by RefreshRows(), never through a Track on this property (see the
        // ENGINEERING DECISION on RecomputeItems below).
        private ValueList<Item> items = ValueList<Item>.Empty;
        public ValueList<Item> Items { get => Read(items); set => Set(ref items, value); }
    }

    private readonly ViewState state = new();
    private TextBox queryField = null!;
    private readonly MiddleTruncatingTextBlock noMatchesText = new(); // :66-76
    private readonly StackPanel rowsPanel = new() { Orientation = Orientation.Vertical, Spacing = 2 }; // :78 VStack(spacing: 2)
    private readonly Panel resultsSlot = new();
    private readonly ForEach<(Item Item, int Index, bool Selected), string> rows;

    /// <summary>Test-only surface (R-NAME-6): the field itself is private, but tests need to type into it and read
    /// its state, exactly as NewThreadPromptTests reaches other App-layer views.</summary>
    internal TextBox QueryField => queryField;

    /// <summary>Test-only: the current selection index.</summary>
    internal int Selection => state.Selection;

    /// <summary>Test-only: how many rows the cached results hold.</summary>
    internal int ResultCount => state.Items.Count;

    /// <summary>Test-only: the panel showing either the row list or the no-matches line.</summary>
    internal Panel ResultsSlot => resultsSlot;

    /// <summary>Test-only: the row list itself (its Children are the row controls, keyed to row-list order).</summary>
    internal StackPanel RowsPanel => rowsPanel;

    /// <summary>Test-only: the "Nothing matches" line.</summary>
    internal MiddleTruncatingTextBlock NoMatchesText => noMatchesText;

    // :33-108 body
    public CommandPalette()
    {
        rows = new ForEach<(Item Item, int Index, bool Selected), string>(
            rowsPanel,
            entry => entry.Item.Id,
            entry => new CommandPaletteRow(entry.Item, entry.Index, entry.Selected, ActivateRow, HoverRow),
            (control, entry) => ((CommandPaletteRow)control).Configure(entry.Item, entry.Index, entry.Selected));
        this.Track(() => AppEnvironment.GetAppModel(this), Build);
    }

    private void Build(AppModel model)
    {
        // :35-41 the dimming scrim: "A dark window needs more of a wash than a light one to push the palette
        // forward; a single fixed value read as nothing at all in dark themes."
        var scrim = new Border { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        var scrimFill = new AnimatedColor(scrim);
        scrim.Background = scrimFill.Brush;
        void DrawScrim() => scrimFill.Set(
            PlainControls.IsDark(scrim.ActualThemeVariant) ? SwiftColor.Black.Opacity(0.24) : SwiftColor.Black.Opacity(0.08), null); // :37
        scrim.ActualThemeVariantChanged += (_, _) => DrawScrim();
        DrawScrim();
        scrim.PointerPressed += (_, _) => Close(); // :40 onTapGesture
        AutomationProperties.SetAccessibilityView(scrim, AccessibilityView.Raw); // :41 accessibilityHidden(true)

        // :43-49 the search row.
        var magnifier = SwiftImage.SystemName("magnifyingglass").ForegroundStyle(Chrome.SecondaryText); // :44-45
        var textField = PlainControls.TextField(
            "Search threads, projects and actions", // :46
            new Binding<string>(() => state.Query, value => state.Query = value));
        SwiftFont.System(SwiftFont.TextStyle.Title3).Apply(textField); // :48
        textField.HorizontalAlignment = HorizontalAlignment.Stretch;
        textField.VerticalAlignment = VerticalAlignment.Center;
        queryField = textField;

        // :43 HStack(spacing: 10). ENGINEERING DECISION: a two-column Grid ("Auto,*"), the ChromeRow convention for a
        // flexible child (see CommandPaletteRow), not a horizontal StackPanel: a StackPanel never stretches a main-axis
        // child, so the text box stayed as wide as its placeholder and a long query grew it past the 600-point panel
        // instead of scrolling inside it. A SwiftUI TextField fills the room the magnifier leaves.
        var searchRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        searchRow.Children.Add(magnifier);
        searchRow.Children.Add(textField);
        Grid.SetColumn(magnifier, 0);
        Grid.SetColumn(textField, 1);
        textField.Margin = new Thickness(10, 0, 0, 0); // :43 spacing: 10
        magnifier.VerticalAlignment = VerticalAlignment.Center;
        searchRow.Margin = new Thickness(18, 15, 18, 15); // :64-65

        // :50-62. SwiftUI answers onSubmit/onKeyPress before the field's own key handling, so these are taken on
        // the way down (Tunnel), ahead of the TextBox's own handler (same convention as Chrome.cs's
        // SidebarSearchField). All four are consumed unconditionally, as the source's own `return .handled` is.
        searchRow.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (!ReferenceEquals(e.Source, textField)) return;
            switch (e.Key)
            {
                case Key.Enter: Run(); e.Handled = true; break; // :50 onSubmit
                case Key.Up: state.Selection = Math.Max(0, state.Selection - 1); e.Handled = true; break; // :51-54
                case Key.Down: // :55-58
                    state.Selection = Math.Min(Math.Max(0, state.Items.Count - 1), state.Selection + 1);
                    e.Handled = true;
                    break;
                case Key.Escape: Close(); e.Handled = true; break; // :59-62
            }
        }, RoutingStrategies.Tunnel);

        // :66-76 "Never a blank box: a query that matches nothing says so rather than leaving the field alone
        // above an empty panel."
        SwiftFont.Body.Apply(noMatchesText); // :70
        noMatchesText.Foreground = Chrome.SecondaryText.Brush(noMatchesText); // :71
        noMatchesText.MaxLines = 1; // :72
        noMatchesText.HorizontalAlignment = HorizontalAlignment.Stretch;
        noMatchesText.Margin = new Thickness(18, 0, 18, 16); // :74-76

        resultsSlot.Children.Add(noMatchesText);
        rowsPanel.Margin = new Thickness(6, 0, 6, 6); // :92-93

        var content = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 }; // :42 VStack(spacing: 0)
        content.Children.Add(searchRow);
        content.Children.Add(resultsSlot);

        // :96 .frame(width: 600). The transparent fill makes the panel's blank bands (the 18-point side insets and 15-point top
        // inset of the search row, the gaps and insets around the rows) take a press, so only the scrim dismisses (UI-WIN-029).
        var panel = new Border { Child = content, Width = 600, Background = Brushes.Transparent };
        SwiftGlass.SetEffect(panel, GlassEffect.Regular(GlassShape.RoundedRectangle(24, RoundedCornerStyle.Continuous))); // :97
        panel.HorizontalAlignment = HorizontalAlignment.Center; // :34 ZStack(alignment: .top)
        panel.VerticalAlignment = VerticalAlignment.Top;
        panel.Margin = new Thickness(0, 72, 0, 0); // :98 .padding(.top, 72)

        Child = new Panel { Children = { scrim, panel } };

        // ENGINEERING DECISION: these three observers are constructed directly with Tracked<T> rather than
        // through the this.Track(...) extension (R-VIEW-1's usual idiom). That extension arms itself on the
        // control's NEXT AttachedToVisualTree event; Build() itself already runs from inside that event for
        // `this` (see the constructor), so by this point `this` is already attached and would never fire a
        // second time. Disposed together on detach, exactly as this.Track's own wrapper would.

        // :101 .onChange(of: query) { selection = 0 }. Fires once immediately (Tracked's own initial evaluation,
        // ENGINEERING DECISION: harmless, Selection already starts at 0) then on every later query edit.
        var queryTracker = new Tracked<string>(() => state.Query, _ => state.Selection = 0);

        // :102-107 .onChange(of: ResultsKey(...), initial: true) { _, _ in items = results }. ENGINEERING DECISION:
        // RefreshRows() is called directly here, not through a Tracked read of state.Items, because ValueList<Item>
        // is Equatable (R-OBS-3) and an ordinary Set would silently skip the UI refresh when two different queries
        // both land on an empty result (their empty lists compare equal); calling it inline reproduces SwiftUI's
        // @State write, which always re-renders regardless of value equality.
        var resultsTracker = new Tracked<(string Query, ValueList<ChatThread> Threads, ValueList<Project> Projects, Guid? SelectedThreadID, string FinishActionTitle)>(
            () => (state.Query, model.Threads, model.Projects, model.SelectedThreadID, model.FinishActionTitle),
            _ =>
            {
                var computed = BuildResults(model, state.Query);
                state.Items = new ValueList<Item>(computed);
                RefreshRows(computed);
            });

        // :51-58 the down-arrow clamp and Run()'s bounds check read state.Items directly; the visible rows'
        // highlight still needs a pass whenever only the selection changes (arrow keys, hover, click).
        var selectionTracker = new Tracked<int>(() => state.Selection, selection => RefreshRows(state.Items, selection));

        DetachedFromVisualTree += (_, _) =>
        {
            queryTracker.Dispose();
            resultsTracker.Dispose();
            selectionTracker.Dispose();
        };

        // :100 .onAppear { isFocused = true }. Build() itself runs at attach time (see the constructor), so this
        // is simply focusing the field once everything above is in the tree.
        textField.Focus();
    }

    private void RefreshRows(IReadOnlyList<Item> items) => RefreshRows(items, state.Selection);

    private void RefreshRows(IReadOnlyList<Item> items, int selection)
    {
        if (items.Count == 0) // :66 if items.isEmpty
        {
            // :69. U+201C LEFT / U+201D RIGHT DOUBLE QUOTATION MARK, built from their code points (R-STR-9) rather
            // than a literal non-ASCII character in source.
            noMatchesText.FullText = "Nothing matches " + (char)0x201C + state.Query + (char)0x201D;
            rows.Apply([]);
            if (!ReferenceEquals(resultsSlot.Children.Count > 0 ? resultsSlot.Children[0] : null, noMatchesText))
            {
                resultsSlot.Children.Clear();
                resultsSlot.Children.Add(noMatchesText);
            }
            return;
        }
        var entries = new List<(Item Item, int Index, bool Selected)>(items.Count);
        for (var index = 0; index < items.Count; index++) entries.Add((items[index], index, index == selection)); // :79-90
        rows.Apply(entries);
        if (!ReferenceEquals(resultsSlot.Children.Count > 0 ? resultsSlot.Children[0] : null, rowsPanel))
        {
            resultsSlot.Children.Clear();
            resultsSlot.Children.Add(rowsPanel);
        }
    }

    /// <summary>:81-85 <c>.onHover { hovering in if hovering { selection = index } }</c>: entering only; leaving a
    /// row never clears the selection.</summary>
    private void HoverRow(int index) => state.Selection = index;

    /// <summary>:86-89 <c>.onTapGesture { selection = index; run(items) }</c>.</summary>
    private void ActivateRow(int index)
    {
        state.Selection = index;
        Run();
    }

    /// <summary>:201-206 <c>private func run(_ items: [Item])</c>.</summary>
    private void Run()
    {
        var items = state.Items;
        if (state.Selection < 0 || state.Selection >= items.Count) return; // :202
        var item = items[state.Selection]; // :203
        Close(); // :204
        item.Perform(); // :205
    }

    /// <summary>:208-210 <c>private func close()</c>.</summary>
    private void Close() => AppEnvironment.GetAppModel(this).IsCommandPalettePresented = false;

    /// <summary>:149-199 <c>private var results: [Item]</c>.</summary>
    private static IReadOnlyList<Item> BuildResults(AppModel model, string rawQuery)
    {
        var needle = SwiftString.Lowercased(FoundationText.Trim(rawQuery)); // :150

        // :151-163. "Chords come from the shortcut store, so the column never shows a key the user has remapped
        // away, and shows nothing at all once a chord is cleared."
        var actions = new List<Item>
        {
            new("new-thread", "New thread", null, null, "square.and.pencil", ShortcutStore.Label(AppShortcut.NewThread), () => model.NewThread()), // :154-156
            new("new-worktree", "New thread in worktree", null, null, "arrow.triangle.branch", ShortcutStore.Label(AppShortcut.NewWorktreeThread), () => model.NewThread(workspace: WorkspaceMode.Worktree)), // :157-159
            new("add-project", "Add project", null, null, "folder.badge.plus", ShortcutStore.Label(AppShortcut.AddProject), () => model.ChooseProjectFolder()), // :160-162
        };
        if (model.SelectedThreadID is { } threadID) // :164
        {
            var runtime = model.Runtime(threadID); // :165, side effect: creates the runtime if it did not exist
            actions.Add(new Item("terminal", "Toggle terminal", null, null, "terminal", ShortcutStore.Label(AppShortcut.ToggleTerminal), // :166-168
                () => runtime.IsTerminalVisible = !runtime.IsTerminalVisible));
            actions.Add(new Item("changes", "Toggle changes", null, null, "plusminus", ShortcutStore.Label(AppShortcut.ToggleChanges), // :169-171
                () => runtime.ToggleChanges())); // web W4: the Changes pane
            actions.Add(new Item("finish", model.FinishActionTitle, null, null, model.FinishActionSymbol, ShortcutStore.Label(AppShortcut.FinishThread), // :172-174
                () => model.FinishSelectedThread()));
        }
        actions.Add(new Item("settings", "Settings", null, null, "gearshape", ShortcutStore.Label(AppShortcut.OpenSettings), // :176-178
            () => WindowManager.Shared.ShowSettings()));
        // DEVIATION OD-0012 G5, placed by OD-0053: the Help menu's "Welcome tour" (AppCommands.swift:111-113) has no menu
        // bar to live in, so it is a palette action right after Settings: the sparkles symbol, no chord (the source gives
        // the command none), presenting the tour whether or not it was seen (PR-WIN-115, PR-UPDATE-092).
        actions.Add(new Item("welcome-tour", "Welcome tour", null, null, "sparkles", null, () => Views.Tour.Tour.Present(model)));

        var matchingActions = needle.Length == 0 // :180
            ? actions
            : actions.Where(action => SwiftString.Contains(SwiftString.Lowercased(action.Title), needle)).ToList(); // :180 canonical, not ordinal (R-STR-2): needle and title are both outside text

        var threads = model.Threads // :181-189
            .Where(thread => !thread.IsArchived && !thread.IsInPanel && // :182
                (needle.Length == 0 || SwiftString.Contains(SwiftString.Lowercased(thread.Title), needle))) // canonical (R-STR-2)
            .OrderByDescending(thread => thread.UpdatedAt) // :183, stable (R-COLL-1)
            .Take(needle.Length == 0 ? 6 : 10) // :184
            .Select(thread => new Item(
                SwiftUUID.UuidString(thread.Id), thread.Title, model.Project(thread.ProjectID)?.Name, null, "bubble.left", null, // :185-188
                () => model.SelectedThreadID = thread.Id))
            .ToList();

        var projects = needle.Length == 0 // :190-196
            ? []
            : model.Projects
                .Where(project => SwiftString.Contains(SwiftString.Lowercased(project.Name), needle)) // :191 canonical, not ordinal (R-STR-2)
                .Select(project => new Item(
                    SwiftUUID.UuidString(project.Id), "New thread in " + project.Name, project.Path, project.Icon, "folder", null, // :192-195
                    () => model.NewThread(project)))
                .ToList();

        IEnumerable<Item> ordered = needle.Length == 0 // :197
            ? threads.Concat(matchingActions)
            : matchingActions.Concat(threads).Concat(projects);
        return ordered.Take(12).ToList(); // :198
    }

}

/// <summary>
/// :110-147 <c>private func row(_ item: Item, isSelected: Bool) -> some View</c>, ported as a persistent Decorator
/// rather than a function, so ForEach (R-VIEW-8) can update its isSelected in place without rebuilding the row
/// (PR-WIN-178: rows are recomputed only when the ResultsKey changes, never on selection or hover). File-scoped
/// (not public), the way DownloadRow is in DownloadsPopover.cs: a test reaches one only through the generic
/// Decorator/Control it downcasts to, matching Swift's own file-private <c>row</c>.
/// </summary>
file sealed class CommandPaletteRow : Decorator
{
    private const double IconWidth = 18; // :120 .frame(width: 18)

    private readonly Panel iconSlot = new() { Width = IconWidth, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock titleBlock = new() { MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis }; // :122-123 .lineLimit(1), default .tail truncation (R-STR convention: Chrome.cs:1395, ChatBadge.cs:55)
    private readonly MiddleTruncatingTextBlock subtitleBlock = new() { MaxLines = 1, IsVisible = false }; // :124-130
    private readonly TextBlock chordBlock = new() { IsVisible = false }; // :133-137
    private readonly Border background;
    private readonly AnimatedColor fill;
    private readonly Action<int> onActivate;
    private readonly Action<int> onHover;

    private CommandPalette.Item item = null!;

    public int Index { get; private set; }

    public bool IsSelected { get; private set; }

    public CommandPaletteRow(CommandPalette.Item item, int index, bool isSelected, Action<int> onActivate, Action<int> onHover)
    {
        this.onActivate = onActivate;
        this.onHover = onHover;

        SwiftFont.Default.Apply(titleBlock); // SwiftUI's implicit default, no explicit .font at :122 (ED-290 (1): the face's own line box)
        SwiftFont.Caption.Apply(subtitleBlock); // :126
        subtitleBlock.Foreground = Marrow.UI.Chrome.Chrome.SecondaryText.Brush(subtitleBlock); // :127
        SwiftFont.Caption.Apply(chordBlock); // :135
        chordBlock.Foreground = Marrow.UI.Chrome.Chrome.SecondaryText.Brush(chordBlock); // :136

        var textStack = new StackPanel // :121 VStack(alignment: .leading, spacing: 1)
        {
            Orientation = Orientation.Vertical,
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center,
        };
        textStack.Children.Add(titleBlock);
        textStack.Children.Add(subtitleBlock);

        // :111 HStack(spacing: 12): icon column (18pt), the text block, a Spacer(minLength: 8), then the chord.
        // ENGINEERING DECISION: a two-column-of-flex Grid ("Auto,*,Auto"), the ChromeRow/ChromeSection convention
        // for Spacer(minLength:) already established elsewhere (Chrome.cs:1039, :1066; TokenActivitySection.cs's
        // own BuildHeader), not the fixed-width Control this file used before review: a horizontal StackPanel
        // never stretches a main-axis child regardless of HorizontalAlignment, so that Control never actually
        // expanded and the chord sat a fixed 8px after the text instead of at the row's trailing edge - visible
        // on nearly every row here, since almost every action carries a shortcut (PR-WIN-171/172/173). The Star
        // column also gives titleBlock/subtitleBlock a real bounded width, which is what lets their MaxLines(1)
        // truncation (above, and :124-130) actually clip rather than only ever measuring their full text.
        // ENGINEERING DECISION (ED-279, UI-WIN-032, UI-WIN-033): the spacing is derived from the Swift's stack, whose
        // Spacer(minLength: 8) at :132 is outside `if let shortcut`. HStack(spacing: 12) puts 12 between each pair of adjacent
        // subviews and a Spacer is a subview, so the text block is followed by 12, the Spacer (at least 8) and, only when a chord
        // follows, another 12: 12 + 8 = 20 on every row, 12 + 8 + 12 = 32 before a chord. The 20 is the text block's trailing margin
        // (it stays when the chord is hidden); the chord's own left margin is the second 12.
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        textStack.HorizontalAlignment = HorizontalAlignment.Stretch; // Grid, unlike StackPanel, honors Stretch along this axis
        textStack.Margin = new Thickness(12, 0, 20, 0); // :111 icon -> text spacing (12); text -> Spacer spacing (12) + :132 Spacer(minLength: 8), with or without a chord
        chordBlock.Margin = new Thickness(12, 0, 0, 0); // :111 Spacer -> chord spacing (12)
        Grid.SetColumn(iconSlot, 0);
        Grid.SetColumn(textStack, 1);
        Grid.SetColumn(chordBlock, 2);
        row.Children.Add(iconSlot);
        row.Children.Add(textStack);
        row.Children.Add(chordBlock);
        foreach (var part in row.Children) if (part is Control control) control.VerticalAlignment = VerticalAlignment.Center;
        row.SetValue(TextElement.ForegroundProperty, Marrow.UI.Chrome.Chrome.PrimaryText.Brush(row)); // title's ambient colour

        background = new Border { Child = row, Padding = new Thickness(12, 8) } // :139-140
            .RoundedRectangle(12, RoundedCornerStyle.Continuous); // :141
        fill = new AnimatedColor(background);
        background.Background = fill.Brush;

        background.PointerEntered += (_, _) => this.onHover(Index); // :81-85, entering only
        background.PointerPressed += (_, _) => this.onActivate(Index); // :86-89

        Child = background;
        Configure(item, index, isSelected);
    }

    /// <summary>Called for every ForEach pass, new row or surviving one (R-VIEW-8): item, index and isSelected may
    /// all have changed.</summary>
    public void Configure(CommandPalette.Item newItem, int index, bool selected)
    {
        item = newItem;
        Index = index;
        IsSelected = selected;

        titleBlock.Text = item.Title; // :122

        if (item.Subtitle is { } subtitle) // :124-129
        {
            subtitleBlock.FullText = subtitle;
            subtitleBlock.IsVisible = true;
        }
        else subtitleBlock.IsVisible = false;

        iconSlot.Children.Clear();
        if (item.Icon is { } icon) iconSlot.Children.Add(new ProjectIconMark(icon, 13)); // :113-114
        else iconSlot.Children.Add(SwiftImage.SystemName(item.Symbol).ForegroundStyle(selected ? Marrow.UI.Chrome.Chrome.Accent : SwiftColor.Secondary)); // :116-118

        if (item.Shortcut is { } shortcut) // :133-137
        {
            chordBlock.Text = shortcut;
            chordBlock.IsVisible = true;
        }
        else chordBlock.IsVisible = false;

        fill.Set(selected ? Marrow.UI.Chrome.Chrome.Accent.Opacity(0.16) : SwiftColor.Clear, null); // :141

        // :143-146 "A tap gesture on its own is invisible to VoiceOver: the row reads as one element that says it
        // can be run, and says which one Return will run."
        AutomationProperties.SetName(this, item.Subtitle is { } named ? item.Title + ", " + named : item.Title);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer(CommandPaletteRow owner) : ControlAutomationPeer(owner), ISelectionItemProvider, IInvokeProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button; // :146 .isButton
        protected override IReadOnlyList<AutomationPeer>? GetChildrenCore() => []; // :145 children: .combine

        bool ISelectionItemProvider.IsSelected => ((CommandPaletteRow)Owner).IsSelected; // :146 .isSelected
        ISelectionProvider? ISelectionItemProvider.SelectionContainer => null;
        void ISelectionItemProvider.AddToSelection() { }
        void ISelectionItemProvider.RemoveFromSelection() { }
        void ISelectionItemProvider.Select() { }
        void IInvokeProvider.Invoke() => ((CommandPaletteRow)Owner).onActivate(((CommandPaletteRow)Owner).Index);
    }
}
