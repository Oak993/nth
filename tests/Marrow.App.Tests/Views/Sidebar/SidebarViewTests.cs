using System.Collections;
using System.Globalization;
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
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.App.Views.Sidebar;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Sidebar;

// Source: DroppyCode/App/Views/Sidebar/SidebarView.swift (B3 52bc792), :1-2695. Part 1 of 8: PR-HYDRAMERGE-100,
// PR-SIDEBAR-001 to -006, -009 to -025. Part 2 of 8: PR-SIDEBAR-026, -028 to -050. Every expected value is
// transcribed from the B3 line cited on the test or from the record, never from the C# implementation. Non-ASCII text
// is written as escapes (R-STR-9).
//
// How the view is driven. Marrow.App has no InternalsVisibleTo (ED-119), so the file's private types and members are
// reached by reflection, as NewThreadPromptTests does. A view is either ATTACHED (in a shown headless window: its
// tracked parts run, one main turn after a change) or DETACHED (under a host that carries the AppModel, with no
// window: the environment reaches it through the logical tree, its tracked parts stay inert, and its builders and
// handlers can be called directly). Until the row views of parts 2 to 5 exist (ProjectRow, SidebarThreadRow and the
// helper rows are still MARROW_STUB), an attached view is only ever given an empty library; everything that needs
// projects or threads is exercised on a detached view.
//
// STOP App.Views.Sidebar.SidebarView #1
// Kind: unverified
// Swift: DroppyCode/App/Views/Sidebar/SidebarView.swift:737-751 (B3 52bc792); :743 `project.name.lowercased().contains(needle)`
// and :745 `titles[$0.id]?.contains(needle) ?? $0.title.lowercased().contains(needle)`
// Requirements: PR-SIDEBAR-005 (and PR-SIDEBAR-043 of part 2)
// What is unclear or missing: the search field's Return runs searchResults even when the trimmed query is empty
// (:86-90), so `contains` is asked about an empty needle. Two Swift overloads can answer: Foundation's
// StringProtocol.contains (range(of:) != nil, which finds no range for "", so false) and the standard library's
// Collection.contains from _StringProcessing (an empty collection is always contained, so true). Which one the
// macOS 26 build binds for `String.contains(String)` with AppKit imported is not settled by the source, and the two
// give different observable results.
// Readings or options: (a) Foundation's -> Return in an empty (or whitespace-only) field selects nothing;
// (b) the standard library's -> it selects the first thread of the first project that has one.
// Needed to proceed: an oracle item (a macOS run of `"abc".contains("")` in an AppKit target) or a recorded decision.
// Done so far: Return and the arrows are tested for non-empty queries only; the empty-needle case of the matching
// helper is left unwritten (a MARROW_STUB naming this STOP), nothing guessed.
//
// STOP App.Views.Sidebar.SidebarView #2
// Kind: missing dependency
// Swift: DroppyCode/App/Views/Sidebar/SidebarView.swift:168 (B3 52bc792) `action: { WindowManager.shared.showSettings() }`
// Requirements: PR-SIDEBAR-013
// What is unclear or missing: the Settings row's action is written as the Swift writes it, but WindowManager.Shared
// and ShowSettings are still MARROW_STUB in the skeleton (unit App.Window.WindowManager), so clicking the row cannot
// be observed yet.
// Readings or options: none; the call is the source's.
// Needed to proceed: unit App.Window.WindowManager.
// Done so far: the row's title, glyph and update dot are tested; the click's effect is not.
//
// STOP App.Views.Sidebar.SidebarView #3
// Kind: platform gap
// Swift: DroppyCode/App/Views/Sidebar/SidebarView.swift:117-119 (B3 52bc792) `.animation(Chrome.panelSlide, value: ...)`
// on the LazyVStack
// Requirements: PR-SIDEBAR-019
// What is unclear or missing: SwiftUI animates the positions of the rows that stay while a row enters or leaves ("the
// space opens or closes on a spring"). The port's UI layer has no layout-animation mechanism (UI/Port has transitions
// for inserted and removed children only, SwiftAnimation.cs), so the surviving rows' slide cannot be reproduced
// here without a new foundation piece.
// Readings or options: (a) a foundation panel that animates its children's arranged offsets under the current
// Transaction -> the source's motion; (b) no positional motion -> rows jump while the entering and leaving ones fade.
// Needed to proceed: a decision (ED) and, for (a), a UI/Port unit.
// Done so far: the list applies each rebuild inside Chrome.panelSlide when the thread count, the projects' expansion
// or the settled fold changed (the entering and leaving fades ride it); no test can observe it before the row views
// exist, so PR-SIDEBAR-019 is not claimed.
//
// STOP App.Views.Sidebar.SidebarView #4
// Kind: conflict
// Swift: DroppyCode/App/AppModel.swift:525-527 (B3 52bc792) `let path = url.standardizedFileURL.path` and
// `if let existing = projects.first(where: { $0.path == path }) { return existing }`, reached from
// DroppyCode/App/Views/Sidebar/SidebarView.swift:509 `for url in panel.urls { added = model.addProject(at: url) }`
// Requirements: PR-SIDEBAR-012
// What is unclear or missing: the port's AppModel.AddProject(SwiftURL) (src/Marrow.App/AppModel.cs:544) passes
// SwiftURL.Path to SwiftPath.StandardizedFileURLPath. For the file URL a Windows folder gives ("file:///C:/..."),
// SwiftURL.Path is "/C:/...", which SwiftPath.IsAbsolute rejects, so the path is resolved against the process's
// current directory (seen here: "<test bin>\C:/Users/.../alpha"). A folder chosen in the panel therefore becomes a
// project with a wrong path, and a folder added by any other route (a library written by the app, AddProjectPath)
// never matches it, so it is added twice. SidebarView.AddProject calls the model exactly as :509-510 do.
// Readings or options: (a) AppModel.AddProject reads the URL's file-system path ("C:/..." without the leading
// slash, as swift-foundation's URL.path does on Windows) -> the source's duplicate check; (b) the panel seam hands the
// model a URL whose Path already has that form -> a non-standard file URL.
// Needed to proceed: a fix in unit App.AppModel (reading (a)), or a recorded decision.
// Done so far: the dedupe test adds the folder through this row twice, so both reads agree and the source's rule is
// observable; no test asserts the stored path of a chosen folder.
//
// STOP App.Views.Sidebar.SidebarView #5
// Kind: platform gap
// Swift: DroppyCode/App/Views/Sidebar/SidebarView.swift:124 (B3 52bc792) `.pointerStyle(drag.id != nil ? .grabActive : nil)`
// Requirements: PR-SIDEBAR-049 (B3 replaced B2's `NSCursor.closedHand.push()` at :775 and `NSCursor.pop()` at :805
// with this pointer style)
// What is unclear or missing: while a row is grabbed the list shows the closed-hand ("grab active") pointer; nil
// leaves the rows' own pointers. Windows has no closed-hand system cursor (IDC_HAND is the pointing hand) and no
// decision maps `.grabActive` (FollowUpQueueTab.swift:384 and ModelsSettingsPage.swift:184 use it too, with
// `.grabIdle`).
// Readings or options: (a) a bundled closed-hand cursor image -> the source's look; (b) a stock cursor such as
// StandardCursorType.SizeAll or DragMove -> a different shape; (c) no pointer change -> the cursor stays as it was.
// Needed to proceed: a decision (ED or PA) mapping `.grabActive` and `.grabIdle` for the three sites.
// Done so far: the grab, the peers snapshot and the release are implemented and tested; the list's pointer style is
// left unwritten.
//
// STOP App.Views.Sidebar.SidebarView #6
// Kind: unverified
// Swift: DroppyCode/App/Views/Sidebar/SidebarView.swift:743, :745 (B3 52bc792) `.lowercased().contains(needle)`
// Requirements: PR-SIDEBAR-043
// What is unclear or missing: ED-009 asks the search fixture to cover a combining-mark case. Which `contains` the
// source binds (STOP #1) also decides how a combining mark matches: the standard library's Collection.contains walks
// Characters under canonical equivalence (a needle "e" does not match inside "e" + U+0301, and a precomposed U+00E9
// needle matches a decomposed title), while the port's `Contains` (part 1) is an ordinal substring test (the
// opposite answer in both cases). Lowercasing itself is settled (O-07, per-scalar) and is pinned below with U+0130,
// U+0131 and the final sigma, where every reading agrees.
// Readings or options: (a) Character-wise canonical matching -> the answers above; (b) ordinal UTF-16 matching -> the
// opposite answers; (c) Foundation's `range(of:)` without options -> its own rules.
// Needed to proceed: the same oracle item or decision STOP #1 needs.
// Done so far: no test asserts either reading for combining marks; part 1's matcher is unchanged.

[Collection("Headless")]
public sealed class SidebarViewTests(HeadlessSession headless)
{
    private static readonly Assembly AppAssembly = typeof(SidebarView).Assembly;
    private static readonly DateTimeOffset Base = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    // ---- reflection over the file's private declarations ----

    private static Type Private(string name) => AppAssembly.GetType("Marrow.App.Views.Sidebar." + name, throwOnError: true)!;

    private static object? Call(object target, string method, params object?[] args) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);

    private static object? Get(object target, string property) =>
        target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(target);

    private static List<object> Items(object? list) => ((IEnumerable)list!).Cast<object>().ToList();

    private static string IdOf(object item) => (string)Get(item, "Id")!;

    private static object KindOf(object item) => Get(item, "SidebarItemKind")!;

    private static string KindName(object item) => KindOf(item).GetType().Name;

    // projectItems(helpers:) (:515-557) over the view's own placements and helper grouping (:72-73).
    private static List<object> ProjectItems(SidebarView view)
    {
        var helpers = Call(view, "HelpersByParent", Get(view, "ThreadPlacements"));
        return Items(Call(view, "ProjectItems", helpers));
    }

    private static SwiftTransition RowTransition(SidebarView view, object item) => (SwiftTransition)Call(view, "RowTransition", item)!;

    private static string? MergeStage(ThreadRuntime? runtime) =>
        (string?)Private("SidebarThreadRow").GetMethod("MergeStage", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [runtime]);

    // ---- building the view ----

    private static SidebarView Detached(AppModel model, bool inPopover = false, Action? dismiss = null)
    {
        var host = new Border();
        AppEnvironment.SetAppModel(host, model);
        var view = new SidebarView(inPopover, dismiss);
        host.Child = view;
        return view;
    }

    private static SidebarView Attached(AppModel model, bool inPopover = false, Action? dismiss = null)
    {
        var host = new Border();
        AppEnvironment.SetAppModel(host, model);
        var view = new SidebarView(inPopover, dismiss);
        host.Child = view;
        var window = new AvaloniaWindow { Content = host, Width = 280, Height = 640 };
        window.Show();
        window.UpdateLayout();
        return view;
    }

    private static void Close(Control view)
    {
        if (TopLevel.GetTopLevel(view) is AvaloniaWindow window) window.Close();
    }

    private static Rect Frame(Control control, Control relativeTo)
    {
        var origin = control.TranslatePoint(new Point(0, 0), relativeTo)!.Value;
        return new Rect(origin, control.Bounds.Size);
    }

    private static string[] Texts(Control control) =>
        LogicalQuery.SelfAndDescendants<TextBlock>(control).Select(block => block.Text ?? "").ToArray();

    private static TextBox SearchBox(SidebarView view) => LogicalQuery.SelfAndDescendants<TextBox>(view).Single();

    private static Button ButtonNamed(Control root, string name) =>
        LogicalQuery.SelfAndDescendants<Button>(root).Single(button => AutomationProperties.GetName(button) == name);

    private static SidebarRow RowNamed(SidebarView view, string title) =>
        LogicalQuery.SelfAndDescendants<SidebarRow>(view)
            .Single(row => LogicalQuery.SelfAndDescendants<Button>(row).Any(button => AutomationProperties.GetName(button) == title));

    private static bool Press(TextBox box, Key key)
    {
        var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = box };
        box.RaiseEvent(args);
        return args.Handled;
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    // ---- library samples (Library.swift shapes through AppModelSamples) ----

    private static Project NewProject(AppModelHarness harness, string name) => AppModelSamples.Project(name, harness.NewFolder(name));

    private static ChatThread NewThread(Project project, string title, int minute) =>
        AppModelSamples.Thread(project) with { Title = title, CreatedAt = Base.AddMinutes(minute), UpdatedAt = Base.AddMinutes(minute) };

    private static AppModel Model(AppModelHarness harness, IEnumerable<Project> projects, IEnumerable<ChatThread> threads)
    {
        harness.WriteLibrary(projects, threads);
        return harness.MakeModel();
    }

    // ---- PR-HYDRAMERGE-100: the merge's stage words ----

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-100")] // App/Views/Sidebar/SidebarView.swift:1980-1984
    public void Merge_stage_is_nil_without_a_runtime_and_while_no_merge_runs()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var project = NewProject(harness, "Alpha");
            var thread = NewThread(project, "Lead", 0);
            var model = Model(harness, [project], [thread]);
            harness.Main.OnMain(() =>
            {
                Assert.Null(MergeStage(null));
                var runtime = model.Runtime(thread.Id);
                runtime.HydraMergeStage = "Reviewing heads";
                Assert.False(runtime.IsHydraMerging);
                Assert.Null(MergeStage(runtime));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-100")] // App/Views/Sidebar/SidebarView.swift:1980-1984
    public void Merge_stage_is_the_stage_words_followed_by_one_horizontal_ellipsis()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var project = NewProject(harness, "Alpha");
            var thread = NewThread(project, "Lead", 0);
            var model = Model(harness, [project], [thread]);
            harness.Main.OnMain(() =>
            {
                var runtime = model.Runtime(thread.Id);
                runtime.IsHydraMerging = true;
                runtime.HydraMergeStage = "Alpha: Reviewing heads";
                Assert.Equal("Alpha: Reviewing heads\U00002026", MergeStage(runtime)); // U+2026 HORIZONTAL ELLIPSIS
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-100")] // App/Views/Sidebar/SidebarView.swift:1983
    public void Merge_stage_falls_back_to_Merging_with_the_single_ellipsis_character()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var project = NewProject(harness, "Alpha");
            var thread = NewThread(project, "Lead", 0);
            var model = Model(harness, [project], [thread]);
            harness.Main.OnMain(() =>
            {
                var runtime = model.Runtime(thread.Id);
                runtime.IsHydraMerging = true;
                runtime.HydraMergeStage = null;
                var stage = MergeStage(runtime);
                Assert.Equal("Merging\U00002026", stage); // U+2026 HORIZONTAL ELLIPSIS
                Assert.Equal(new byte[] { 0x4D, 0x65, 0x72, 0x67, 0x69, 0x6E, 0x67, 0xE2, 0x80, 0xA6 }, System.Text.Encoding.UTF8.GetBytes(stage!));
            });
        });
    }

    // ---- PR-SIDEBAR-001 / -002 / -014: the column's layout ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-001")] // App/Views/Sidebar/SidebarView.swift:74-80
    public void The_window_column_opens_with_a_14_point_clearance_under_16_points_and_the_popover_has_none()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var column = Attached(model);
                var popover = Attached(model, inPopover: true);
                // :76-79 Color.clear 14 pt tall with 16 pt of top padding, then :99 the search row's 14 pt: 44 pt down.
                Assert.Equal(44d, Frame(LogicalQuery.SelfAndDescendants<SidebarSearchField>(column).Single(), column).Y);
                // Without the clearance the popover's search row sits only its own 12 pt down (:99).
                Assert.Equal(12d, Frame(LogicalQuery.SelfAndDescendants<SidebarSearchField>(popover).Single(), popover).Y);
                var spacers = LogicalQuery.SelfAndDescendants<Border>(column)
                    .Where(border => border.Height == 14 && border.Margin == new Thickness(0, 16, 0, 0) && border.Child is null && border.Background is null)
                    .ToList();
                Assert.Single(spacers);
                Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<Border>(popover),
                    border => border.Height == 14 && border.Margin == new Thickness(0, 16, 0, 0) && border.Child is null);
                Close(column);
                Close(popover);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-002")] // App/Views/Sidebar/SidebarView.swift:82-99
    public void The_search_row_sets_the_field_and_the_bell_6_points_apart_inset_10_on_each_side()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var view = Attached(model);
                var field = Frame(LogicalQuery.SelfAndDescendants<SidebarSearchField>(view).Single(), view);
                var bell = Frame(ButtonNamed(view, "Activity view"), view);
                Assert.Equal(10d, field.X); // :98 .padding(.horizontal, Chrome.listInset)
                Assert.Equal(view.Bounds.Width - 10, bell.Right);
                Assert.Equal(6d, bell.X - field.Right); // :82 HStack(spacing: 6)
                Assert.Equal(30d, bell.Width); // :2328 .frame(width: 30, height: 30)
                Assert.Equal(30d, bell.Height);
                Assert.Equal(field.Y, bell.Y);
                Close(view);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-002")] // App/Views/Sidebar/SidebarView.swift:99
    public void The_search_row_takes_14_points_on_top_in_the_column_and_12_in_the_popover()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var column = Attached(model);
                var popover = Attached(model, inPopover: true);
                Assert.Equal(16d + 14 + 14, Frame(ButtonNamed(column, "Activity view"), column).Y);
                Assert.Equal(12d, Frame(ButtonNamed(popover, "Activity view"), popover).Y);
                Close(column);
                Close(popover);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-014")] // App/Views/Sidebar/SidebarView.swift:164-178
    public void Add_project_and_Settings_stack_1_point_apart_inset_10_with_12_points_below()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var view = Attached(model);
                var add = Frame(RowNamed(view, "Add project"), view);
                var settings = Frame(RowNamed(view, "Settings"), view);
                Assert.Equal(10d, add.X); // :177 .padding(.horizontal, Chrome.listInset)
                Assert.Equal(view.Bounds.Width - 10, add.Right);
                Assert.Equal(10d, settings.X);
                Assert.Equal(1d, settings.Y - add.Bottom); // :164 VStack(spacing: 1)
                Assert.Equal(view.Bounds.Height - 12, settings.Bottom); // :178 .padding(.bottom, 12)
                Close(view);
            });
        });
    }

    // ---- PR-SIDEBAR-003 / -004: the search field ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-003")] // App/Views/Sidebar/SidebarView.swift:85
    public void The_search_field_prompt_is_exactly_Search_threads()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                Assert.Equal("Search threads", SearchBox(view).PlaceholderText);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-004")] // App/Views/Sidebar/SidebarView.swift:61-63, :103-113
    public void A_whitespace_only_query_trims_to_empty_and_keeps_the_normal_list()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            SidebarView view = null!;
            harness.Main.OnMain(() =>
            {
                view = Attached(model);
                // Spaces, a tab, a no-break space (Unicode Zs) and a newline: all in .whitespacesAndNewlines.
                SearchBox(view).Text = " \t\U000000A0\n "; // U+00A0 NO-BREAK SPACE
            });
            harness.Main.OnMain(() =>
            {
                Assert.Equal("", Get(view, "Query"));
                Assert.DoesNotContain("No results", Texts(view));
                // The clear button follows the untrimmed text (Chrome.swift:796).
                Assert.Single(LogicalQuery.SelfAndDescendants<Button>(view), button => AutomationProperties.GetName(button) == "Clear search");
                SearchBox(view).Text = " x ";
            });
            harness.Main.OnMain(() =>
            {
                Assert.Equal("x", Get(view, "Query"));
                // A non-empty query shows the search list, here its empty state (:111-112, :693-703).
                Assert.Contains("No results", Texts(view));
                Close(view);
            });
        });
    }

    // ---- PR-SIDEBAR-005 / -006: Return and the arrows ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-005")] // App/Views/Sidebar/SidebarView.swift:86-90
    public void Return_selects_the_first_thread_of_the_first_search_result()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var beta = NewProject(harness, "Beta");
            var older = NewThread(alpha, "Fix the build", 1);
            var newer = NewThread(alpha, "Fix the docs", 2);
            var other = NewThread(beta, "Fix the tests", 3);
            var model = Model(harness, [alpha, beta], [older, newer, other]);
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                model.SelectedThreadID = null;
                var box = SearchBox(view);
                box.Text = "fix";
                Assert.True(Press(box, Key.Enter));
                // Alpha is the first project; threads(in:) puts the newest first (AppModel.swift:314-332).
                Assert.Equal(newer.Id, model.SelectedThreadID);
                box.Text = "TESTS";
                Press(box, Key.Enter);
                Assert.Equal(other.Id, model.SelectedThreadID);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-005")] // App/Views/Sidebar/SidebarView.swift:87
    public void Return_with_no_result_changes_nothing()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var thread = NewThread(alpha, "Fix the build", 1);
            var model = Model(harness, [alpha], [thread]);
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                model.SelectedThreadID = null;
                var box = SearchBox(view);
                box.Text = "nothing matches this";
                Press(box, Key.Enter);
                Assert.Null(model.SelectedThreadID);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-006")] // App/Views/Sidebar/SidebarView.swift:720-731
    public void Down_with_no_selection_selects_the_first_result_and_Up_the_last()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var beta = NewProject(harness, "Beta");
            var first = NewThread(alpha, "Fix one", 2);
            var second = NewThread(alpha, "Fix two", 1);
            var last = NewThread(beta, "Fix three", 3);
            var model = Model(harness, [alpha, beta], [first, second, last]);
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                var box = SearchBox(view);
                box.Text = "fix";
                model.SelectedThreadID = null;
                Assert.True(Press(box, Key.Down));
                Assert.Equal(first.Id, model.SelectedThreadID);
                model.SelectedThreadID = null;
                Assert.True(Press(box, Key.Up));
                Assert.Equal(last.Id, model.SelectedThreadID);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-006")] // App/Views/Sidebar/SidebarView.swift:729-730
    public void The_arrows_move_by_one_and_clamp_at_both_ends()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var beta = NewProject(harness, "Beta");
            var first = NewThread(alpha, "Fix one", 2);
            var second = NewThread(alpha, "Fix two", 1);
            var last = NewThread(beta, "Fix three", 3);
            var model = Model(harness, [alpha, beta], [first, second, last]);
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                var box = SearchBox(view);
                box.Text = "fix";
                model.SelectedThreadID = first.Id;
                Press(box, Key.Up);
                Assert.Equal(first.Id, model.SelectedThreadID);
                Press(box, Key.Down);
                Assert.Equal(second.Id, model.SelectedThreadID);
                Press(box, Key.Down);
                Assert.Equal(last.Id, model.SelectedThreadID);
                Press(box, Key.Down);
                Assert.Equal(last.Id, model.SelectedThreadID);
                Press(box, Key.Up);
                Assert.Equal(second.Id, model.SelectedThreadID);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-006")] // App/Views/Sidebar/SidebarView.swift:724-727
    public void A_selection_outside_the_results_counts_as_no_selection()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var match = NewThread(alpha, "Fix one", 2);
            var outside = NewThread(alpha, "Write docs", 1);
            var model = Model(harness, [alpha], [match, outside]);
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                var box = SearchBox(view);
                box.Text = "fix";
                model.SelectedThreadID = outside.Id;
                Press(box, Key.Up);
                Assert.Equal(match.Id, model.SelectedThreadID);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-006")] // App/Views/Sidebar/SidebarView.swift:721; UI/Chrome/Chrome.swift:818-822
    public void With_a_whitespace_query_the_arrows_change_nothing_and_are_still_consumed()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var thread = NewThread(alpha, "Fix one", 2);
            var model = Model(harness, [alpha], [thread]);
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                var box = SearchBox(view);
                box.Text = "   ";
                model.SelectedThreadID = null;
                Assert.True(Press(box, Key.Down));
                Assert.True(Press(box, Key.Up));
                Assert.Null(model.SelectedThreadID);
            });
        });
    }

    // ---- PR-SIDEBAR-009 / -010 / -011: the bell ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-009")] // App/Views/Sidebar/SidebarView.swift:93-96; App/AppSettings.swift:271-273
    public void The_bell_flips_sidebarActivityView_and_writes_it_under_its_key()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                Assert.False(model.Settings.SidebarActivityView);
                Click(ButtonNamed(view, "Activity view"));
                Assert.True(model.Settings.SidebarActivityView);
                Assert.Equal(true, harness.Defaults.Object("sidebarActivityView"));
                Click(ButtonNamed(view, "Activity view"));
                Assert.False(model.Settings.SidebarActivityView);
                Assert.Equal(false, harness.Defaults.Object("sidebarActivityView"));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-009")] // App/Views/Sidebar/SidebarView.swift:2322-2323
    public void The_bell_toggles_its_binding_inside_Chrome_panelSlide()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            harness.Main.OnMain(() =>
            {
                var isOn = false;
                Animation? during = null;
                var binding = new Binding<bool>(() => isOn, value => { isOn = value; during = Transaction.Current; });
                var toggle = (Control)Activator.CreateInstance(Private("ActivityViewToggle"), binding)!;
                Click(ButtonNamed(toggle, "Activity view"));
                Assert.True(isOn);
                Assert.Equal(Chrome.PanelSlide, during);
                Click(ButtonNamed(toggle, "Activity view"));
                Assert.False(isOn);
            });
        });
    }

    // :93-96 the binding SidebarView passes the bell: settings.sidebarActivityView, an observed value.
    private static Binding<bool> ActivityViewSetting(AppModel model) =>
        new(() => model.Settings.SidebarActivityView, value => model.Settings.SidebarActivityView = value);

    private static Control AttachedToggle(Binding<bool> binding)
    {
        var toggle = (Control)Activator.CreateInstance(Private("ActivityViewToggle"), binding)!;
        var window = new AvaloniaWindow { Content = new Border { Child = toggle }, Width = 100, Height = 100 };
        window.Show();
        return toggle;
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-010")] // App/Views/Sidebar/SidebarView.swift:2321, :2339; Core/Models/Shortcuts.swift:232
    public void The_bell_tooltip_names_the_action_and_the_default_chord_in_Windows_notation()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            // The bell's fill follows its state along the click's animation, one frame at a time (AnimatedColor).
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var store = harness.Main.OnMain(() => (ShortcutStore)Activator.CreateInstance(typeof(ShortcutStore), nonPublic: true)!);
            using var shortcuts = ShortcutStore.Shared.Override(store);
            var model = harness.MakeModel();
            Control toggle = null!;
            harness.Main.OnMain(() =>
            {
                toggle = AttachedToggle(ActivityViewSetting(model));
                // DEVIATION OD-0008 B7: Option+Command+U reads Ctrl+Alt+U.
                Assert.Equal("Turn on activity view (Ctrl+Alt+U)", ToolTip.GetTip(ButtonNamed(toggle, "Activity view")));
                Click(ButtonNamed(toggle, "Activity view"));
            });
            harness.Main.OnMain(() =>
            {
                Assert.Equal("Turn off activity view (Ctrl+Alt+U)", ToolTip.GetTip(ButtonNamed(toggle, "Activity view")));
                Close(toggle);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-010")] // App/Views/Sidebar/SidebarView.swift:2321, :2339
    public void With_the_chord_cleared_the_bell_tooltip_has_no_parenthetical_and_no_trailing_space()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            // The bell's fill follows its state along the click's animation, one frame at a time (AnimatedColor).
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var store = harness.Main.OnMain(() => (ShortcutStore)Activator.CreateInstance(typeof(ShortcutStore), nonPublic: true)!);
            using var shortcuts = ShortcutStore.Shared.Override(store);
            var model = harness.MakeModel();
            Control toggle = null!;
            harness.Main.OnMain(() =>
            {
                store.Set(null, AppShortcut.ToggleActivityView);
                model.Settings.SidebarActivityView = true;
                toggle = AttachedToggle(ActivityViewSetting(model));
                Assert.Equal("Turn off activity view", ToolTip.GetTip(ButtonNamed(toggle, "Activity view")));
                Click(ButtonNamed(toggle, "Activity view"));
            });
            harness.Main.OnMain(() =>
            {
                Assert.Equal("Turn on activity view", ToolTip.GetTip(ButtonNamed(toggle, "Activity view")));
                Close(toggle);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-011")] // App/Views/Sidebar/SidebarView.swift:2340-2341
    public void The_bell_is_labelled_Activity_view_with_the_value_On_or_Off()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            // The bell's fill follows its state along the click's animation, one frame at a time (AnimatedColor).
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var model = harness.MakeModel();
            Control toggle = null!;
            harness.Main.OnMain(() =>
            {
                toggle = AttachedToggle(ActivityViewSetting(model));
                var button = ButtonNamed(toggle, "Activity view");
                Assert.Equal("Off", AutomationProperties.GetItemStatus(button));
                Click(button);
            });
            harness.Main.OnMain(() =>
            {
                Assert.Equal("On", AutomationProperties.GetItemStatus(ButtonNamed(toggle, "Activity view")));
                Close(toggle);
            });
        });
    }

    // ---- PR-SIDEBAR-012: Add project ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-012")] // App/Views/Sidebar/SidebarView.swift:165-167, :500-511
    public void Add_project_asks_for_folders_only_several_at_once_with_the_exact_prompt_and_message()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var runner = new RecordingOpenPanelRunner(null);
            using var seam = OpenPanel.Runner.Override(runner);
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                Click(ButtonNamed(view, "Add project"));
            });
            var shown = Assert.Single(runner.Shown);
            Assert.True(shown.Directories); // :502
            Assert.False(shown.Files); // :503
            Assert.True(shown.Multiple); // :504
            Assert.Equal("Add project", shown.Prompt); // :505
            Assert.Equal("Choose a folder for your agents to work in.", shown.Message); // :506
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-012")] // App/Views/Sidebar/SidebarView.swift:507-510
    public void Add_project_adds_one_project_per_chosen_folder_and_starts_a_thread_in_the_last()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var first = harness.NewFolder("first");
            var second = harness.NewFolder("second");
            var runner = new RecordingOpenPanelRunner([new Project("x", first).Url, new Project("y", second).Url]);
            using var seam = OpenPanel.Runner.Override(runner);
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                Click(ButtonNamed(view, "Add project"));
                Assert.Equal([Path.GetFileName(first), Path.GetFileName(second)], model.Projects.Select(project => project.Name));
                var thread = Assert.Single(model.Threads);
                Assert.Equal(model.Projects[1].Id, thread.ProjectID);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-012")] // App/Views/Sidebar/SidebarView.swift:507
    public void Cancelling_the_folder_chooser_adds_nothing_and_starts_no_thread()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            using var seam = OpenPanel.Runner.Override(new RecordingOpenPanelRunner(null));
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                Click(ButtonNamed(view, "Add project"));
                Assert.Empty(model.Projects);
                Assert.Empty(model.Threads);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-012")] // App/Views/Sidebar/SidebarView.swift:509-510; App/AppModel.swift:525-527
    public void A_folder_already_added_yields_the_existing_project_and_still_gets_the_new_thread()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var folder = harness.NewFolder("alpha");
            // The folder is added through this same row first, so both passes read its path the same way (STOP #4).
            using var seam = OpenPanel.Runner.Override(new RecordingOpenPanelRunner([new Project("x", folder).Url]));
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                Click(ButtonNamed(view, "Add project"));
                var added = Assert.Single(model.Projects);
                Click(ButtonNamed(view, "Add project"));
                Assert.Equal(added.Id, Assert.Single(model.Projects).Id);
                Assert.Equal(2, model.Threads.Count);
                Assert.All(model.Threads, thread => Assert.Equal(added.Id, thread.ProjectID));
            });
        });
    }

    // ---- PR-SIDEBAR-013: Settings ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-013")] // App/Views/Sidebar/SidebarView.swift:168-169
    public void The_Settings_row_carries_the_gear_at_1_15_scale_and_Add_project_the_plus()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                var gear = LogicalQuery.SelfAndDescendants<SwiftImage>(RowNamed(view, "Settings")).Single();
                Assert.Equal("gear", gear.SymbolName);
                Assert.Equal(Chrome.SymbolSize * 1.15, gear.Width); // UI/Chrome/Chrome.swift:664
                var plus = LogicalQuery.SelfAndDescendants<SwiftImage>(RowNamed(view, "Add project")).Single();
                Assert.Equal("plus", plus.SymbolName);
                Assert.Equal(Chrome.SymbolSize, plus.Width);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-013")] // App/Views/Sidebar/SidebarView.swift:170-174; App/Views/Settings/AboutUpdateViews.swift:526-534
    public void The_update_dot_sits_on_the_gear_only_while_an_update_is_available()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var none = harness.Main.OnMain(() => new UpdateChecker(ReleaseFeed.Running("1.0.0"), [], UpdateChecker.DefaultFeed));
            harness.Defaults.Set("updateChecker_release", AvailableUpdate.EncodeToBytes(new AvailableUpdate
            {
                Version = "2.0.0",
                Tag = "v2.0.0",
                DownloadURL = SwiftURL.Create("https://example.com/Marrow.zip")!,
                Notes = "",
                PageURL = SwiftURL.Create("https://example.com/releases/v2.0.0")!,
            }));
            var waiting = harness.Main.OnMain(() => new UpdateChecker(ReleaseFeed.Running("1.0.0"), [], UpdateChecker.DefaultFeed));
            harness.Main.OnMain(() =>
            {
                Assert.False(none.UpdateAvailable);
                Assert.True(waiting.UpdateAvailable);
                using (UpdateChecker.OverrideShared(none))
                {
                    var view = Attached(model);
                    Assert.Empty(LogicalQuery.SelfAndDescendants<UpdateAvailableDot>(view));
                    Close(view);
                }
                using (UpdateChecker.OverrideShared(waiting))
                {
                    var view = Attached(model);
                    var dot = Assert.Single(LogicalQuery.SelfAndDescendants<UpdateAvailableDot>(RowNamed(view, "Settings")));
                    Assert.Equal(HorizontalAlignment.Right, dot.HorizontalAlignment); // :170 .overlay(alignment: .topTrailing)
                    Assert.Equal(VerticalAlignment.Top, dot.VerticalAlignment);
                    Assert.Contains(LogicalQuery.SelfAndDescendants<Control>(dot), control => AutomationProperties.GetName(control) == "Update available");
                    Close(view);
                }
            });
        });
    }

    // ---- PR-SIDEBAR-015 / -016: drag background and the popover's dismissal ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-015")] // App/Views/Sidebar/SidebarView.swift:180-181
    public void The_window_column_has_a_window_drag_area_behind_it_and_the_popover_has_none()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var column = Detached(model);
                var drag = Assert.Single(LogicalQuery.SelfAndDescendants<WindowDragArea>(column));
                // Behind: the first child of the view's root, under the column (:181 .background).
                var root = Assert.IsAssignableFrom<Panel>(column.Child);
                Assert.Same(drag, root.Children[0]);
                Assert.Equal(2, root.Children.Count);
                Assert.Empty(LogicalQuery.SelfAndDescendants<WindowDragArea>(Detached(model, inPopover: true)));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-016")] // App/Views/Sidebar/SidebarView.swift:182-183
    public void In_the_popover_a_selection_change_calls_dismiss()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var dismissed = 0;
            SidebarView view = null!;
            harness.Main.OnMain(() => view = Attached(model, inPopover: true, dismiss: () => dismissed++));
            harness.Main.OnMain(() => model.SelectedThreadID = Guid.NewGuid());
            Assert.Equal(1, dismissed);
            harness.Main.OnMain(() => model.SelectedThreadID = Guid.NewGuid());
            Assert.Equal(2, dismissed);
            harness.Main.OnMain(() => Close(view));
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-016")] // App/Views/Sidebar/SidebarView.swift:182-183
    public void In_the_column_a_selection_change_dismisses_nothing()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var dismissed = 0;
            SidebarView view = null!;
            harness.Main.OnMain(() => view = Attached(model, inPopover: false, dismiss: () => dismissed++));
            harness.Main.OnMain(() => model.SelectedThreadID = Guid.NewGuid());
            Assert.Equal(0, dismissed);
            harness.Main.OnMain(() => Close(view));
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-016")] // App/Views/Sidebar/SidebarView.swift:183 `dismiss?()`
    public void In_the_floating_panel_without_a_dismiss_closure_a_selection_change_is_harmless()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            SidebarView view = null!;
            harness.Main.OnMain(() => view = Attached(model, inPopover: true, dismiss: null));
            harness.Main.OnMain(() => model.SelectedThreadID = Guid.NewGuid());
            harness.Main.OnMain(() =>
            {
                Assert.NotNull(TopLevel.GetTopLevel(view));
                Close(view);
            });
        });
    }

    // ---- PR-SIDEBAR-017 / -018: the scroll view and its freeze ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-017")] // App/Views/Sidebar/SidebarView.swift:101-124, :139
    public void The_list_is_a_vertical_scroll_view_without_indicators_holding_a_1_point_stack_inset_10_12_8()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var view = Attached(model);
                var scroll = LogicalQuery.SelfAndDescendants<ScrollViewer>(view).Single();
                Assert.Equal(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility); // :102 ScrollView(.vertical)
                Assert.Empty(scroll.GetVisualDescendants().OfType<ScrollBar>()); // :139 .scrollIndicators(.never)
                var stack = Assert.IsType<StackPanel>(scroll.Content);
                Assert.Equal(Orientation.Vertical, stack.Orientation);
                Assert.Equal(1d, stack.Spacing); // :103 LazyVStack(alignment: .leading, spacing: 1)
                Assert.Equal(new Thickness(10, 12, 10, 8), stack.Margin); // :120-122
                // The list sits between the search row (44 + 30) and the bottom rows (28 + 1 + 28 + 12).
                var frame = Frame(scroll, view);
                Assert.Equal(74d, frame.Y);
                Assert.Equal(view.Bounds.Height - 69, frame.Bottom);
                Close(view);
            });
        });
    }

    private static StackPanel ListStack(SidebarView view) =>
        Assert.IsType<StackPanel>(LogicalQuery.SelfAndDescendants<ScrollViewer>(view).Single().Content);

    [Fact]
    [Trait("Req", "PR-SIDEBAR-018")] // App/Views/Sidebar/SidebarView.swift:115, :392-404
    public void A_scroll_movement_freezes_the_list_until_150_ms_after_the_last_one()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            SidebarView view = null!;
            harness.Main.OnMain(() =>
            {
                view = Attached(model);
                Assert.True(ListStack(view).IsHitTestVisible);
                Call(view, "NoteListScroll");
            });
            Assert.False(ListStack(view).IsHitTestVisible);
            harness.Main.AdvanceMilliseconds(100);
            harness.Main.OnMain(() => Call(view, "NoteListScroll")); // re-arms the settle
            harness.Main.AdvanceMilliseconds(149);
            Assert.False(ListStack(view).IsHitTestVisible);
            harness.Main.AdvanceMilliseconds(1);
            Assert.True(ListStack(view).IsHitTestVisible);
            harness.Main.OnMain(() => Close(view));
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-018")] // App/Views/Sidebar/SidebarView.swift:141-145
    public void A_scroll_gesture_ending_lifts_the_freeze_at_once()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            SidebarView view = null!;
            harness.Main.OnMain(() =>
            {
                view = Attached(model);
                Call(view, "NoteListScroll");
            });
            Assert.False(ListStack(view).IsHitTestVisible);
            harness.Main.OnMain(() =>
            {
                var scroll = LogicalQuery.SelfAndDescendants<ScrollViewer>(view).Single();
                scroll.RaiseEvent(new ScrollGestureEndedEventArgs(1));
            });
            Assert.True(ListStack(view).IsHitTestVisible);
            // The cancelled settle task does not come back to change anything.
            harness.Main.AdvanceMilliseconds(200);
            Assert.True(ListStack(view).IsHitTestVisible);
            harness.Main.OnMain(() => Close(view));
        });
    }

    // ---- PR-SIDEBAR-020 / -022: transitions and item ids ----

    private static AppModel RichLibrary(AppModelHarness harness, out (Project Alpha, Project Beta, ChatThread Pinned, ChatThread Parent, ChatThread Helper, ChatThread Head, ChatThread Folded, ChatThread FoldedHelper, ChatThread Settled) s)
    {
        var alpha = NewProject(harness, "Alpha");
        var beta = NewProject(harness, "Beta");
        var pinned = NewThread(alpha, "Pinned", 1) with { IsPinned = true };
        var parent = NewThread(alpha, "Parent", 5) with { ExpandsHelpers = true };
        var helper = NewThread(alpha, "Helper", 6) with { ParentThreadID = parent.Id };
        var head = NewThread(alpha, "Head", 7) with { ParentThreadID = parent.Id, Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Completed) };
        var folded = NewThread(alpha, "Folded", 4);
        var foldedHelper = NewThread(alpha, "Folded helper", 8) with { ParentThreadID = folded.Id };
        var settled = NewThread(alpha, "Settled", 2) with { IsSettled = true, SettledAt = Base.AddHours(1) };
        s = (alpha, beta, pinned, parent, helper, head, folded, foldedHelper, settled);
        var model = Model(harness, [alpha, beta], [pinned, parent, helper, head, folded, foldedHelper, settled]);
        harness.Main.OnMain(() =>
        {
            model.Settings.HydraAutoHidesIdleHeads = false;
            model.Settings.SettledCollapsed = false; // AppSettings.swift:725 starts it folded
        });
        return model;
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-020")] // App/Views/Sidebar/SidebarView.swift:317-326
    public void Settled_rows_the_Settled_header_and_gaps_fade_plainly_and_every_other_row_uses_sidebarRow()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = RichLibrary(harness, out _);
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                var items = ProjectItems(view);
                var kinds = items.Select(KindName).ToList();
                foreach (var kind in new[] { "Gap", "Project", "Thread", "SettledHeader", "Helper", "HeadCards", "HelperStub" })
                    Assert.Contains(kind, kinds);
                Assert.Contains(items, item => KindName(item) == "Thread" && ((ChatThread)Get(KindOf(item), "Value")!).IsSettled);
                foreach (var item in items)
                {
                    var expected = KindName(item) switch
                    {
                        "Gap" or "SettledHeader" => SwiftTransition.Opacity, // :321-322
                        "Thread" => ((ChatThread)Get(KindOf(item), "Value")!).IsSettled ? SwiftTransition.Opacity : SidebarRowTransition, // :319-320
                        _ => SidebarRowTransition, // :323-324
                    };
                    Assert.Equal(expected, RowTransition(view, item));
                }
                // The activity header takes the default arm too (:323-324).
                var header = Activator.CreateInstance(Private("SidebarItem"))!;
                Private("SidebarItem").GetProperty("Id")!.SetValue(header, "day-Today");
                Private("SidebarItem").GetProperty("SidebarItemKind")!.SetValue(header, Activator.CreateInstance(Private("SidebarItem+Kind+Header"), "Today", true));
                Assert.Equal(SidebarRowTransition, RowTransition(view, header));
            });
        });
    }

    // :1064-1076 `.asymmetric(insertion: .opacity.animation(.easeIn(duration: 0.18).delay(0.12)),
    // removal: .opacity.animation(.easeOut(duration: 0.06)))`, built here from the cited values.
    private static SwiftTransition SidebarRowTransition => SwiftTransition.Asymmetric(
        SwiftTransition.Opacity.Animation(Animation.EaseIn(duration: 0.18).Delay(0.12)),
        SwiftTransition.Opacity.Animation(Animation.EaseOut(duration: 0.06)));

    [Fact]
    [Trait("Req", "PR-SIDEBAR-020")] // App/Views/Sidebar/SidebarView.swift:1064-1076
    public void The_sidebarRow_transition_fades_in_over_0_18_after_0_12_and_out_over_0_06()
    {
        var sidebarRow = (SwiftTransition)typeof(SidebarView).Assembly.GetType("Marrow.App.Views.Sidebar.SidebarViewExtensions", throwOnError: true)!
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == "get_SidebarRow" && method.GetParameters().Length == 0)
            .Invoke(null, null)!;
        Assert.Equal(SidebarRowTransition, sidebarRow);
        Assert.NotEqual(SwiftTransition.Asymmetric(
            SwiftTransition.Opacity.Animation(Animation.EaseIn(duration: 0.18)),
            SwiftTransition.Opacity.Animation(Animation.EaseOut(duration: 0.06))), sidebarRow);
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-022")] // App/Views/Sidebar/SidebarView.swift:1053-1055, :1058-1062
    public void A_settled_thread_is_keyed_settled_dash_uppercase_uuid_and_an_open_one_by_the_bare_uuid()
    {
        using var runtime = new Marrow.Testing.TestRuntime(); // ChatThread.init reads the defaults (Library.swift:204-226)
        var project = new Project("Alpha", @"C:\alpha");
        var open = AppModelSamples.Thread(project) with { Id = new Guid("0a1b2c3d-4e5f-6071-8293-a4b5c6d7e8f9") };
        var settled = open with { IsSettled = true };
        var idFor = Private("SidebarItem").GetMethod("IdFor", BindingFlags.Public | BindingFlags.Static)!;
        Assert.Equal("0A1B2C3D-4E5F-6071-8293-A4B5C6D7E8F9", idFor.Invoke(null, [open]));
        Assert.Equal("settled-0A1B2C3D-4E5F-6071-8293-A4B5C6D7E8F9", idFor.Invoke(null, [settled]));
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-022")] // App/Views/Sidebar/SidebarView.swift:550, :1058-1062
    public void Settling_a_thread_swaps_its_item_for_one_with_another_id()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var thread = NewThread(alpha, "Work", 1);
            var model = Model(harness, [alpha], [thread]);
            harness.Main.OnMain(() =>
            {
                model.Settings.SettledCollapsed = false; // AppSettings.swift:725 starts it folded
                var view = Detached(model);
                var uuid = SwiftUUID.UuidString(thread.Id);
                Assert.Contains(uuid, ProjectItems(view).Select(IdOf));
                model.UpdateThread(thread.Id, value => value with { IsSettled = true, SettledAt = Base.AddHours(1) });
                var ids = ProjectItems(view).Select(IdOf).ToList();
                Assert.DoesNotContain(uuid, ids);
                Assert.Contains("settled-" + uuid, ids);
            });
        });
    }

    // ---- PR-SIDEBAR-021: reveal ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-021")] // App/Views/Sidebar/SidebarView.swift:154-157
    public void A_reveal_request_for_a_missing_thread_is_cleared_and_scrolls_nothing()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            SidebarView view = null!;
            harness.Main.OnMain(() => view = Attached(model));
            harness.Main.OnMain(() => model.SidebarRevealThreadID = Guid.NewGuid());
            harness.Main.OnMain(() =>
            {
                Assert.Null(model.SidebarRevealThreadID);
                Assert.Equal(0d, LogicalQuery.SelfAndDescendants<ScrollViewer>(view).Single().Offset.Y);
                Close(view);
            });
        });
    }

    // ---- PR-SIDEBAR-023 / -024 / -025: the project layout ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-023")] // App/Views/Sidebar/SidebarView.swift:517-524
    public void Projects_come_in_library_order_with_a_10_point_gap_item_before_every_project_but_the_first()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha") with { IsExpanded = false };
            var beta = NewProject(harness, "Beta") with { IsExpanded = false };
            var gamma = NewProject(harness, "Gamma") with { IsExpanded = false };
            var model = Model(harness, [alpha, beta, gamma], []);
            harness.Main.OnMain(() =>
            {
                var items = ProjectItems(Detached(model));
                string U(Project project) => SwiftUUID.UuidString(project.Id);
                Assert.Equal(
                    ["project-" + U(alpha), "gap-" + U(beta), "project-" + U(beta), "gap-" + U(gamma), "project-" + U(gamma)],
                    items.Select(IdOf));
                Assert.Equal(["Project", "Gap", "Project", "Gap", "Project"], items.Select(KindName));
                Assert.All(items.Where(item => KindName(item) == "Gap"), gap => Assert.Equal(10d, Get(KindOf(gap), "Value")));
                Assert.Equal([alpha.Id, beta.Id, gamma.Id],
                    items.Where(item => KindName(item) == "Project").Select(item => ((Project)Get(KindOf(item), "Value")!).Id));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-023")] // App/Views/Sidebar/SidebarView.swift:517
    public void With_no_projects_the_project_layout_is_empty()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() => Assert.Empty(ProjectItems(Detached(model))));
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-024")] // App/Views/Sidebar/SidebarView.swift:525-554, :1281
    public void Pinned_open_threads_lead_then_the_others_then_a_4_point_gap_the_Settled_header_and_the_settled_threads()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var older = NewThread(alpha, "Older", 1);
            var newer = NewThread(alpha, "Newer", 3);
            var pinned = NewThread(alpha, "Pinned", 0) with { IsPinned = true };
            var settledEarly = NewThread(alpha, "Settled early", 2) with { IsSettled = true, SettledAt = Base.AddHours(1) };
            var settledLate = NewThread(alpha, "Settled late", 4) with { IsSettled = true, SettledAt = Base.AddHours(2) };
            var model = Model(harness, [alpha], [older, newer, pinned, settledEarly, settledLate]);
            harness.Main.OnMain(() =>
            {
                model.Settings.SettledCollapsed = false; // AppSettings.swift:725 starts it folded
                var items = ProjectItems(Detached(model));
                string U(Guid id) => SwiftUUID.UuidString(id);
                Assert.Equal(
                    [
                        "project-" + U(alpha.Id),
                        U(pinned.Id), U(newer.Id), U(older.Id),
                        "settled-gap-" + U(alpha.Id),
                        "settled-header-" + U(alpha.Id),
                        "settled-" + U(settledLate.Id), "settled-" + U(settledEarly.Id),
                    ],
                    items.Select(IdOf));
                Assert.Equal(4d, Get(KindOf(items[4]), "Value"));
                Assert.Equal(2L, Get(KindOf(items[5]), "Count"));
                Assert.Equal(false, Get(KindOf(items[5]), "IsFirst"));
                // Project layout rows carry no project name, no mark and no reorder peers (:551).
                foreach (var item in items.Where(item => KindName(item) == "Thread"))
                {
                    Assert.Null(Get(KindOf(item), "ProjectName"));
                    Assert.Null(Get(KindOf(item), "ProjectIcon"));
                    Assert.Null(Get(KindOf(item), "Peers"));
                }
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-024")] // App/Views/Sidebar/SidebarView.swift:536-544
    public void A_project_whose_threads_are_all_settled_gets_the_header_but_no_gap()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var settled = NewThread(alpha, "Settled", 2) with { IsSettled = true, SettledAt = Base.AddHours(1) };
            var model = Model(harness, [alpha], [settled]);
            harness.Main.OnMain(() =>
            {
                model.Settings.SettledCollapsed = false; // AppSettings.swift:725 starts it folded
                var items = ProjectItems(Detached(model));
                Assert.Equal(["Project", "SettledHeader", "Thread"], items.Select(KindName));
                Assert.Equal(1L, Get(KindOf(items[1]), "Count"));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-024")] // App/Views/Sidebar/SidebarView.swift:525
    public void A_folded_project_shows_only_its_own_row()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha") with { IsExpanded = false };
            var thread = NewThread(alpha, "Work", 1);
            var model = Model(harness, [alpha], [thread]);
            harness.Main.OnMain(() => Assert.Equal(["project-" + SwiftUUID.UuidString(alpha.Id)], ProjectItems(Detached(model)).Select(IdOf)));
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-024")] // App/Views/Sidebar/SidebarView.swift:548-553
    public void Each_thread_row_is_followed_at_once_by_its_helper_items()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = RichLibrary(harness, out var s);
            harness.Main.OnMain(() =>
            {
                var items = ProjectItems(Detached(model));
                var ids = items.Select(IdOf).ToList();
                string U(Guid id) => SwiftUUID.UuidString(id);
                var parentAt = ids.IndexOf(U(s.Parent.Id));
                Assert.Equal(U(s.Helper.Id), ids[parentAt + 1]);
                Assert.Equal("heads-" + U(s.Parent.Id), ids[parentAt + 2]);
                var foldedAt = ids.IndexOf(U(s.Folded.Id));
                Assert.Equal("helpers-" + U(s.Folded.Id), ids[foldedAt + 1]);
                Assert.Equal(true, Get(KindOf(items[parentAt]), "HasHelpers"));
                Assert.Equal(false, Get(KindOf(items[ids.IndexOf(U(s.Pinned.Id))]), "HasHelpers"));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-025")] // App/Views/Sidebar/SidebarView.swift:529, :545
    public void With_the_settled_section_folded_nothing_below_the_Settled_header_is_built()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var open = NewThread(alpha, "Open", 3);
            var settledA = NewThread(alpha, "Settled A", 1) with { IsSettled = true, SettledAt = Base.AddHours(1), ExpandsHelpers = true };
            var settledB = NewThread(alpha, "Settled B", 2) with { IsSettled = true, SettledAt = Base.AddHours(2) };
            var underSettled = NewThread(alpha, "Under settled", 4) with { ParentThreadID = settledA.Id };
            var model = Model(harness, [alpha], [open, settledA, settledB, underSettled]);
            harness.Main.OnMain(() =>
            {
                model.Settings.SettledCollapsed = true;
                var items = ProjectItems(Detached(model));
                string U(Guid id) => SwiftUUID.UuidString(id);
                Assert.Equal(
                    ["project-" + U(alpha.Id), U(open.Id), "settled-gap-" + U(alpha.Id), "settled-header-" + U(alpha.Id)],
                    items.Select(IdOf));
                Assert.Equal(2L, Get(KindOf(items[3]), "Count")); // the full settled count
            });
        });
    }

    // ======== Part 2 of 8 ========

    private static object? Field(object target, string name) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);

    private static object? CallOn(object target, string method, params object?[] args) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(target, args);

    // helpersByParent(placements:) (:410-422) over the view's own placements (:386-388).
    private static ValueMap<Guid, ValueList<ChatThread>> Helpers(SidebarView view) =>
        (ValueMap<Guid, ValueList<ChatThread>>)Call(view, "HelpersByParent", Get(view, "ThreadPlacements"))!;

    private static ValueList<ChatThread> HelpersOf(SidebarView view, Guid parent) =>
        Helpers(view).TryGetValue(parent, out var own) ? own : ValueList<ChatThread>.Empty;

    // activityItems(helpers:) (:561-605).
    private static List<object> ActivityItems(SidebarView view) => Items(Call(view, "ActivityItems", Helpers(view)));

    // helperItems(under:helpers:) (:458-474).
    private static List<object> HelperItems(SidebarView view, ChatThread under, IReadOnlyList<ChatThread> helpers) =>
        Items(Call(view, "HelperItems", under, helpers));

    private static ChatThread ThreadOf(object item) => (ChatThread)Get(KindOf(item), "Value")!;

    private static IEnumerable<object> ThreadItems(IEnumerable<object> items) => items.Where(item => KindName(item) == "Thread");

    private static string U(Guid id) => SwiftUUID.UuidString(id);

    // The manual clock's start (Marrow.Testing ManualTimeProvider): 2026-09-22 12:00 UTC, a Tuesday.
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private static ChatThread ThreadAt(Project project, string title, DateTimeOffset updatedAt) =>
        AppModelSamples.Thread(project) with { Title = title, CreatedAt = updatedAt, UpdatedAt = updatedAt };

    // Calendar.current, the formatters' locale and zone, pinned for one flow (ED-094: a Gregorian culture per flow).
    private static void InDays(Action body, TimeZoneInfo? zone = null)
    {
        using var zoneScope = SwiftFormatters.CurrentTimeZone.Override(zone ?? TimeZoneInfo.Utc);
        using var localeScope = SwiftFormatters.CurrentLocale.Override("en_US");
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            Assert.Equal(Now, SwiftDate.Now);
            body();
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    private static void Approve(AppModel model, Guid thread) =>
        model.Runtime(thread).Rehearse(new ProviderEvent.Approval(new ApprovalRequest(
            "approval", ApprovalRequest.Kind.Command, "Run it?", ValueList<ApprovalRequest.Option>.Empty)));

    private static void Ask(AppModel model, Guid thread) =>
        model.Runtime(thread).Rehearse(new ProviderEvent.Question(new QuestionRequest("question", ValueList<QuestionRequest.Question>.Empty)));

    private static RowDrag<Guid> Drag(SidebarView view) => (RowDrag<Guid>)Get(Field(view, "state")!, "Drag")!;

    private static void NoteFrame(SidebarView view, Guid id, Rect frame) => CallOn(Field(view, "rowFrames")!, "Note", id, frame);

    private static Guid? ThreadAtPoint(object frames, Point point) => (Guid?)CallOn(frames, "Thread", point);

    // The list's stack (:103), the scroll view's content.
    private static StackPanel ListPanel(SidebarView view) => (StackPanel)LogicalQuery.SelfAndDescendants<ScrollViewer>(view).Single().Content!;

    // ---- PR-SIDEBAR-026: a folded project's count ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-026")] // App/Views/Sidebar/SidebarView.swift:521-524
    public void A_project_item_counts_its_top_level_threads_and_every_helper_under_them_settled_ones_included()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha") with { IsExpanded = false };
            var empty = NewProject(harness, "Empty") with { IsExpanded = false };
            var open = NewThread(alpha, "Open", 1);
            var settled = NewThread(alpha, "Settled", 2) with { IsSettled = true, SettledAt = Base.AddHours(1) };
            var helper = NewThread(alpha, "Helper", 3) with { ParentThreadID = open.Id };
            var head = NewThread(alpha, "Head", 4) with { ParentThreadID = open.Id, Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Completed), IsInPanel = true };
            var underSettled = NewThread(alpha, "Under settled", 5) with { ParentThreadID = settled.Id };
            var archived = NewThread(alpha, "Archived", 6) with { IsArchived = true };
            var panelled = NewThread(alpha, "Panelled helper", 7) with { ParentThreadID = open.Id, IsInPanel = true };
            var model = Model(harness, [alpha, empty], [open, settled, helper, head, underSettled, archived, panelled]);
            harness.Main.OnMain(() =>
            {
                var items = ProjectItems(Detached(model));
                // Two top-level threads (the settled one counts), the helper and the panelled head under the open one
                // (a panelled plain helper is not grouped, :415) and one under the settled one: 2 + 2 + 1.
                Assert.Equal([5L, 0L], items.Where(item => KindName(item) == "Project").Select(item => (long)Get(KindOf(item), "Count")!));
            });
        });
    }

    // ---- PR-SIDEBAR-028 to -033: helpers and heads ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-028")] // App/Views/Sidebar/SidebarView.swift:410-422
    public void Helpers_are_grouped_by_parent_newest_created_first_leaving_out_archived_and_panelled_non_head_threads()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var parent = NewThread(alpha, "Parent", 0);
            var lone = NewThread(alpha, "Lone", 0);
            var first = NewThread(alpha, "First", 1) with { ParentThreadID = parent.Id };
            var third = NewThread(alpha, "Third", 3) with { ParentThreadID = parent.Id };
            var archived = NewThread(alpha, "Archived", 5) with { ParentThreadID = parent.Id, IsArchived = true };
            var panelled = NewThread(alpha, "Panelled", 6) with { ParentThreadID = parent.Id, IsInPanel = true };
            var panelledHead = NewThread(alpha, "Panelled head", 2) with { ParentThreadID = parent.Id, IsInPanel = true, Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Completed) };
            var head = NewThread(alpha, "Head", 4) with { ParentThreadID = parent.Id, Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Completed) };
            var missingParent = Guid.NewGuid();
            var stray = NewThread(alpha, "Stray", 7) with { ParentThreadID = missingParent };
            var model = Model(harness, [alpha], [parent, lone, first, third, archived, panelled, panelledHead, head, stray]);
            harness.Main.OnMain(() =>
            {
                var helpers = Helpers(Detached(model));
                Assert.Equal([head.Id, third.Id, panelledHead.Id, first.Id], helpers[parent.Id].Select(thread => thread.Id));
                // A helper whose parent is shown nowhere is still grouped under that id (:416-417).
                Assert.Equal([stray.Id], helpers[missingParent].Select(thread => thread.Id));
                Assert.False(helpers.ContainsKey(lone.Id));
                Assert.Equal(2, helpers.Count);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-029")] // App/Views/Sidebar/SidebarView.swift:460
    public void A_settled_thread_emits_no_helper_items_and_they_return_when_it_reopens()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var parent = NewThread(alpha, "Parent", 0) with { IsSettled = true, SettledAt = Base.AddHours(1), ExpandsHelpers = true };
            var folded = NewThread(alpha, "Folded", 1) with { IsSettled = true, SettledAt = Base.AddHours(2) };
            var lone = NewThread(alpha, "Lone", 2);
            var helper = NewThread(alpha, "Helper", 3) with { ParentThreadID = parent.Id };
            var head = NewThread(alpha, "Head", 4) with { ParentThreadID = parent.Id, Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Completed) };
            var foldedHelper = NewThread(alpha, "Folded helper", 5) with { ParentThreadID = folded.Id };
            var model = Model(harness, [alpha], [parent, folded, lone, helper, head, foldedHelper]);
            harness.Main.OnMain(() =>
            {
                model.Settings.SettledCollapsed = false; // AppSettings.swift starts it folded
                var view = Detached(model);
                Assert.Empty(HelperItems(view, parent, HelpersOf(view, parent.Id)));
                Assert.Empty(HelperItems(view, folded, HelpersOf(view, folded.Id))); // no folded stub either
                Assert.Empty(HelperItems(view, lone, ValueList<ChatThread>.Empty));
                Assert.DoesNotContain(ProjectItems(view), item => KindName(item) is "Helper" or "HeadCards" or "HelperStub");
                model.UpdateThread(parent.Id, thread => thread with { IsSettled = false, SettledAt = null });
                var ids = ProjectItems(view).Select(IdOf).ToList();
                Assert.Contains(U(helper.Id), ids);
                Assert.Contains("heads-" + U(parent.Id), ids);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-030")] // App/Views/Sidebar/SidebarView.swift:461-463; Core/Models/Library.swift:166-170
    public void A_folded_thread_emits_exactly_one_stub_keyed_helpers_dash_its_uuid()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var parent = NewThread(alpha, "Parent", 0);
            var older = NewThread(alpha, "Older", 1) with { ParentThreadID = parent.Id };
            var head = NewThread(alpha, "Head", 2) with { ParentThreadID = parent.Id, Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Completed) };
            var newer = NewThread(alpha, "Newer", 3) with { ParentThreadID = parent.Id };
            var model = Model(harness, [alpha], [parent, older, head, newer]);
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                Assert.False(parent.ExpandsHelpers); // Library.swift:166, so foldsHelpers (:167-170) is true
                Assert.True(parent.FoldsHelpers);
                var items = HelperItems(view, parent, HelpersOf(view, parent.Id));
                var stub = Assert.Single(items);
                Assert.Equal("helpers-" + U(parent.Id), IdOf(stub));
                Assert.Equal("HelperStub", KindName(stub));
                Assert.Equal(parent.Id, ((ChatThread)Get(KindOf(stub), "Parent")!).Id);
                Assert.Equal([newer.Id, head.Id, older.Id], ((IEnumerable<ChatThread>)Get(KindOf(stub), "Helpers")!).Select(thread => thread.Id));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-031")] // App/Views/Sidebar/SidebarView.swift:464-473
    public void Unfolded_helpers_emit_a_row_each_newest_first_then_one_head_card_keyed_heads_dash_the_parent_uuid()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var parent = NewThread(alpha, "Parent", 0) with { ExpandsHelpers = true };
            var older = NewThread(alpha, "Older", 1) with { ParentThreadID = parent.Id };
            var olderHead = NewThread(alpha, "Older head", 2) with { ParentThreadID = parent.Id, Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Completed) };
            var newer = NewThread(alpha, "Newer", 3) with { ParentThreadID = parent.Id };
            var newerHead = NewThread(alpha, "Newer head", 4) with { ParentThreadID = parent.Id, Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Failed) };
            var model = Model(harness, [alpha], [parent, older, olderHead, newer, newerHead]);
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                var items = HelperItems(view, parent, HelpersOf(view, parent.Id));
                Assert.Equal([U(newer.Id), U(older.Id), "heads-" + U(parent.Id)], items.Select(IdOf));
                Assert.Equal(["Helper", "Helper", "HeadCards"], items.Select(KindName));
                // A card follows, so no helper row ends the connector.
                Assert.Equal([false, false], items.Take(2).Select(item => (bool)Get(KindOf(item), "IsLast")!));
                Assert.Equal(parent.Id, ((ChatThread)Get(KindOf(items[2]), "Parent")!).Id);
                Assert.Equal([newerHead.Id, olderHead.Id], ((IEnumerable<ChatThread>)Get(KindOf(items[2]), "Heads")!).Select(thread => thread.Id));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-031")] // App/Views/Sidebar/SidebarView.swift:467-472
    public void The_last_plain_helper_ends_the_connector_only_when_no_card_follows_and_heads_alone_give_just_the_card()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var plainParent = NewThread(alpha, "Plain parent", 0) with { ExpandsHelpers = true };
            var older = NewThread(alpha, "Older", 1) with { ParentThreadID = plainParent.Id };
            var newer = NewThread(alpha, "Newer", 2) with { ParentThreadID = plainParent.Id };
            var headParent = NewThread(alpha, "Head parent", 3) with { ExpandsHelpers = true };
            var head = NewThread(alpha, "Head", 4) with { ParentThreadID = headParent.Id, Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Completed) };
            var model = Model(harness, [alpha], [plainParent, older, newer, headParent, head]);
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                var plain = HelperItems(view, plainParent, HelpersOf(view, plainParent.Id));
                Assert.Equal([U(newer.Id), U(older.Id)], plain.Select(IdOf));
                Assert.Equal([false, true], plain.Select(item => (bool)Get(KindOf(item), "IsLast")!));
                var card = Assert.Single(HelperItems(view, headParent, HelpersOf(view, headParent.Id)));
                Assert.Equal("heads-" + U(headParent.Id), IdOf(card));
                Assert.Equal("HeadCards", KindName(card));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-032")] // App/Views/Sidebar/SidebarView.swift:452-454, :466; App/AppSettings.swift:606-607, :770
    public void With_hydraAutoHidesIdleHeads_on_the_card_shows_only_while_a_live_head_runs()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var parent = NewThread(alpha, "Parent", 0) with { ExpandsHelpers = true };
            var plain = NewThread(alpha, "Plain", 1) with { ParentThreadID = parent.Id };
            var first = NewThread(alpha, "First head", 2) with { ParentThreadID = parent.Id, Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Completed) };
            var second = NewThread(alpha, "Second head", 3) with { ParentThreadID = parent.Id, Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Stopped) };
            var model = Model(harness, [alpha], [parent, plain, first, second]);
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                Assert.False(model.Settings.HydraAutoHidesIdleHeads); // :770 default false
                var snapshot = HelpersOf(view, parent.Id);
                Assert.Equal(["Helper", "HeadCards"], HelperItems(view, parent, snapshot).Select(KindName));

                model.Settings.HydraAutoHidesIdleHeads = true;
                Assert.Equal(true, harness.Defaults.Object("hydraAutoHidesIdleHeads")); // :607
                var hidden = HelperItems(view, parent, snapshot);
                Assert.Equal([U(plain.Id)], hidden.Select(IdOf));
                Assert.Equal(true, Get(KindOf(hidden[0]), "IsLast")); // no card follows

                // The running check reads the live record (:453): the snapshot still says completed.
                model.UpdateThread(first.Id, thread => thread with { Hydra = thread.Hydra! with { HydraHeadInfoStatus = HydraHeadInfo.Status.Running } });
                Assert.Equal(HydraHeadInfo.Status.Completed, snapshot.Single(thread => thread.Id == first.Id).Hydra!.HydraHeadInfoStatus);
                var shown = HelperItems(view, parent, snapshot);
                Assert.Equal([U(plain.Id), "heads-" + U(parent.Id)], shown.Select(IdOf));
                Assert.Equal(false, Get(KindOf(shown[0]), "IsLast"));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-033")] // App/Views/Sidebar/SidebarView.swift:478-485
    public void Heads_are_ordered_running_first_then_newest_created_first_within_each_block()
    {
        using var runtime = new Marrow.Testing.TestRuntime(); // ChatThread.init reads the defaults (Library.swift:204-226)
        var project = new Project("Alpha", @"C:\alpha");
        ChatThread Head(HydraHeadInfo.Status status, int minute) =>
            AppModelSamples.Thread(project) with { CreatedAt = Base.AddMinutes(minute), Hydra = AppModelSamples.Head(status) };
        var runningOld = Head(HydraHeadInfo.Status.Running, 1);
        var completedOld = Head(HydraHeadInfo.Status.Completed, 2);
        var runningNew = Head(HydraHeadInfo.Status.Running, 3);
        var failedNew = Head(HydraHeadInfo.Status.Failed, 4);
        var stoppedNewest = Head(HydraHeadInfo.Status.Stopped, 5);
        var ordered = SidebarView.OrderedHeads([runningOld, completedOld, runningNew, failedNew, stoppedNewest]);
        Assert.Equal([runningNew.Id, runningOld.Id, stoppedNewest.Id, failedNew.Id, completedOld.Id], ordered.Select(thread => thread.Id));
    }

    // ---- PR-SIDEBAR-034 to -042: the activity layout ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-034")] // App/Views/Sidebar/SidebarView.swift:561-605
    public void Activity_sections_run_attention_pinned_days_settled_and_only_the_first_emitted_header_is_first()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var attention = ThreadAt(alpha, "Attention", Now.AddHours(-1));
            var pinned = ThreadAt(alpha, "Pinned", Now.AddHours(-2)) with { IsPinned = true };
            var today = ThreadAt(alpha, "Today", Now.AddHours(-3));
            var yesterday = ThreadAt(alpha, "Yesterday", Now.AddDays(-1));
            var settled = ThreadAt(alpha, "Settled", Now.AddHours(-4)) with { IsSettled = true, SettledAt = Now.AddHours(-4) };
            var model = Model(harness, [alpha], [attention, pinned, today, yesterday, settled]);
            harness.Main.OnMain(() => InDays(() =>
            {
                model.Settings.SettledCollapsed = false;
                Approve(model, attention.Id);
                var items = ActivityItems(Detached(model));
                Assert.Equal(
                    ["attention", U(attention.Id), "day-Pinned", U(pinned.Id), "day-Today", U(today.Id), "day-Yesterday", U(yesterday.Id), "settled", "settled-" + U(settled.Id)],
                    items.Select(IdOf));
                var headers = items.Where(item => KindName(item) is "Header" or "SettledHeader").ToList();
                Assert.Equal(["Header", "Header", "Header", "Header", "SettledHeader"], headers.Select(KindName));
                Assert.Equal(["Needs attention", "Pinned", "Today", "Yesterday"], headers.Take(4).Select(item => (string)Get(KindOf(item), "Value")!));
                Assert.Equal([true, false, false, false, false], headers.Select(item => (bool)Get(KindOf(item), "IsFirst")!));
                Assert.Equal(1L, Get(KindOf(headers[4]), "Count"));
            }));
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-034")] // App/Views/Sidebar/SidebarView.swift:579, :584, :592
    public void Without_earlier_sections_the_first_header_emitted_is_the_first_and_no_threads_give_no_headers()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var pinned = ThreadAt(alpha, "Pinned", Now.AddHours(-2)) with { IsPinned = true };
            var today = ThreadAt(alpha, "Today", Now.AddHours(-3));
            var model = Model(harness, [alpha], [pinned, today]);
            harness.Main.OnMain(() => InDays(() =>
            {
                var view = Detached(model);
                bool? FirstOf(string id) => ActivityItems(view).Where(item => IdOf(item) == id).Select(item => (bool?)Get(KindOf(item), "IsFirst")).SingleOrDefault();
                Assert.Equal(true, FirstOf("day-Pinned"));
                Assert.Equal(false, FirstOf("day-Today"));
                model.UpdateThread(pinned.Id, thread => thread with { IsPinned = false });
                Assert.Equal(true, FirstOf("day-Today"));
                model.Settings.SettledCollapsed = true;
                model.UpdateThread(pinned.Id, thread => thread with { IsSettled = true, SettledAt = Now.AddHours(-5) });
                model.UpdateThread(today.Id, thread => thread with { IsSettled = true, SettledAt = Now.AddHours(-6) });
                Assert.Equal(["settled"], ActivityItems(view).Select(IdOf));
                Assert.Equal(true, FirstOf("settled"));
            }));
        });
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() => InDays(() => Assert.Empty(ActivityItems(Detached(model)))));
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-035")] // App/Views/Sidebar/SidebarView.swift:562-566
    public void The_activity_layout_leaves_out_archived_and_panelled_threads_and_lifts_a_helper_whose_parent_is_not_shown()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var parent = ThreadAt(alpha, "Parent", Now.AddHours(-1)) with { ExpandsHelpers = true };
            var child = ThreadAt(alpha, "Child", Now.AddHours(-2)) with { ParentThreadID = parent.Id };
            var head = ThreadAt(alpha, "Head", Now.AddHours(-3)) with { ParentThreadID = parent.Id, IsInPanel = true, Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Completed) };
            var archived = ThreadAt(alpha, "Archived", Now.AddHours(-4)) with { IsArchived = true };
            var panelled = ThreadAt(alpha, "Panelled", Now.AddHours(-5)) with { IsInPanel = true };
            var archivedParent = ThreadAt(alpha, "Archived parent", Now.AddHours(-6)) with { IsArchived = true };
            var orphan = ThreadAt(alpha, "Orphan", Now.AddHours(-7)) with { ParentThreadID = archivedParent.Id };
            var stray = ThreadAt(alpha, "Stray", Now.AddHours(-8)) with { ParentThreadID = Guid.NewGuid() };
            var model = Model(harness, [alpha], [parent, child, head, archived, panelled, archivedParent, orphan, stray]);
            harness.Main.OnMain(() => InDays(() =>
            {
                var items = ActivityItems(Detached(model));
                Assert.Equal([parent.Id, orphan.Id, stray.Id], ThreadItems(items).Select(item => ThreadOf(item).Id));
                Assert.Equal(["day-Today", U(parent.Id), U(child.Id), "heads-" + U(parent.Id), U(orphan.Id), U(stray.Id)], items.Select(IdOf));
                // The panelled head is only in its parent's card.
                Assert.Equal([head.Id], ((IEnumerable<ChatThread>)Get(KindOf(items[3]), "Heads")!).Select(thread => thread.Id));
                var everyID = items.Select(IdOf).ToList();
                foreach (var gone in new[] { archived, panelled, archivedParent })
                    Assert.DoesNotContain(everyID, id => id.Contains(U(gone.Id), StringComparison.Ordinal));
            }));
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-036")] // App/Views/Sidebar/SidebarView.swift:568-569, :633-636
    public void Threads_with_pending_approvals_or_questions_head_the_list_unless_settled_or_without_a_live_runtime()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var approval = ThreadAt(alpha, "Approval", Now.AddHours(-3));
            var question = ThreadAt(alpha, "Question", Now.AddHours(-2));
            var noRuntime = ThreadAt(alpha, "No runtime", Now.AddHours(-1));
            var idle = ThreadAt(alpha, "Idle runtime", Now.AddHours(-4));
            var settled = ThreadAt(alpha, "Settled", Now.AddHours(-5)) with { IsSettled = true, SettledAt = Now.AddHours(-5) };
            var model = Model(harness, [alpha], [approval, question, noRuntime, idle, settled]);
            harness.Main.OnMain(() => InDays(() =>
            {
                model.Settings.SettledCollapsed = false;
                Approve(model, approval.Id);
                Ask(model, question.Id);
                Approve(model, settled.Id);
                _ = model.Runtime(idle.Id);
                var items = ActivityItems(Detached(model));
                Assert.Equal(
                    ["attention", U(question.Id), U(approval.Id), "day-Today", U(noRuntime.Id), U(idle.Id), "settled", "settled-" + U(settled.Id)],
                    items.Select(IdOf));
                // The lookup never makes a runtime (:634).
                Assert.Null(model.ExistingRuntime(noRuntime.Id));
            }));
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-037")] // App/Views/Sidebar/SidebarView.swift:644-656, :677-686
    public void Day_groups_read_Today_Yesterday_a_wide_weekday_within_seven_calendar_days_then_month_and_day_then_the_year()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var today = ThreadAt(alpha, "Today", new DateTimeOffset(2026, 9, 22, 8, 0, 0, TimeSpan.Zero));
            var alsoToday = ThreadAt(alpha, "Also today", new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.Zero));
            var yesterday = ThreadAt(alpha, "Yesterday", new DateTimeOffset(2026, 9, 21, 23, 59, 0, TimeSpan.Zero));
            var sunday = ThreadAt(alpha, "Sunday", new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.Zero));
            var wednesday = ThreadAt(alpha, "Six days", new DateTimeOffset(2026, 9, 16, 0, 0, 0, TimeSpan.Zero));
            // 157 hours back, under 7 x 24, but seven calendar days back: not a weekday.
            var sevenDays = ThreadAt(alpha, "Seven days", new DateTimeOffset(2026, 9, 15, 23, 0, 0, TimeSpan.Zero));
            var newYear = ThreadAt(alpha, "New year", new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero));
            var lastYear = ThreadAt(alpha, "Last year", new DateTimeOffset(2025, 12, 31, 10, 0, 0, TimeSpan.Zero));
            var model = Model(harness, [alpha], [lastYear, sevenDays, today, sunday, newYear, yesterday, wednesday, alsoToday]);
            harness.Main.OnMain(() => InDays(() =>
            {
                var items = ActivityItems(Detached(model));
                Assert.Equal(
                    [
                        "day-Today", U(today.Id), U(alsoToday.Id),
                        "day-Yesterday", U(yesterday.Id),
                        "day-Sunday", U(sunday.Id),
                        "day-Wednesday", U(wednesday.Id),
                        "day-September 15", U(sevenDays.Id),
                        "day-January 1", U(newYear.Id),
                        "day-December 31, 2025", U(lastYear.Id),
                    ],
                    items.Select(IdOf));
                Assert.Equal(["Today", "Yesterday", "Sunday", "Wednesday", "September 15", "January 1", "December 31, 2025"],
                    items.Where(item => KindName(item) == "Header").Select(item => (string)Get(KindOf(item), "Value")!));
            }));
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-037")] // App/Views/Sidebar/SidebarView.swift:678-681
    public void A_future_date_takes_the_weekday_branch_and_days_follow_the_local_calendar_day()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var future = ThreadAt(alpha, "Future", new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero));
            // 2026-09-21 16:00 UTC is 2026-09-22 01:00 in Tokyo (UTC+9, no daylight time), where it is then 21:00.
            var late = ThreadAt(alpha, "Late", new DateTimeOffset(2026, 9, 21, 16, 0, 0, TimeSpan.Zero));
            var model = Model(harness, [alpha], [future, late]);
            harness.Main.OnMain(() =>
            {
                InDays(() => Assert.Equal(["day-Friday", U(future.Id), "day-Yesterday", U(late.Id)], ActivityItems(Detached(model)).Select(IdOf)));
                InDays(() => Assert.Equal(["day-Friday", U(future.Id), "day-Today", U(late.Id)], ActivityItems(Detached(model)).Select(IdOf)),
                    TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time"));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-038")] // App/Views/Sidebar/SidebarView.swift:659-675
    public void A_group_puts_threads_without_a_place_for_the_day_first_newest_first_then_placed_ones_by_ascending_order()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var day = new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.Zero);
            var second = ThreadAt(alpha, "Placed second", Now.AddHours(-2)) with { ActivityOrder = 2, ActivityOrderDay = day };
            var first = ThreadAt(alpha, "Placed first", Now.AddHours(-3)) with { ActivityOrder = 1, ActivityOrderDay = day };
            var tiedOlder = ThreadAt(alpha, "Placed tie", Now.AddHours(-4)) with { ActivityOrder = 1, ActivityOrderDay = day };
            var fresh = ThreadAt(alpha, "Fresh", Now.AddMinutes(-30));
            // A place taken on an earlier day no longer counts (:662-663).
            var stale = ThreadAt(alpha, "Stale", Now.AddHours(-1)) with { ActivityOrder = 0.5, ActivityOrderDay = day.AddDays(-1) };
            var model = Model(harness, [alpha], [second, first, tiedOlder, fresh, stale]);
            harness.Main.OnMain(() => InDays(() =>
            {
                var items = ActivityItems(Detached(model));
                Assert.Equal(["day-Today", U(fresh.Id), U(stale.Id), U(first.Id), U(tiedOlder.Id), U(second.Id)], items.Select(IdOf));
            }));
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-039")] // App/Views/Sidebar/SidebarView.swift:576-586
    public void Open_pinned_threads_sit_under_Pinned_above_the_days_unless_they_need_attention()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var pinnedToday = ThreadAt(alpha, "Pinned today", Now.AddHours(-3)) with { IsPinned = true };
            var pinnedYesterday = ThreadAt(alpha, "Pinned yesterday", Now.AddDays(-1)) with { IsPinned = true };
            var pinnedAsking = ThreadAt(alpha, "Pinned asking", Now.AddHours(-2)) with { IsPinned = true };
            var plain = ThreadAt(alpha, "Plain", Now.AddHours(-1));
            var model = Model(harness, [alpha], [pinnedToday, pinnedYesterday, pinnedAsking, plain]);
            harness.Main.OnMain(() => InDays(() =>
            {
                Ask(model, pinnedAsking.Id);
                var items = ActivityItems(Detached(model));
                Assert.Equal(
                    ["attention", U(pinnedAsking.Id), "day-Pinned", U(pinnedToday.Id), U(pinnedYesterday.Id), "day-Today", U(plain.Id)],
                    items.Select(IdOf));
                Assert.Equal("Pinned", Get(KindOf(items[2]), "Value"));
            }));
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-040")] // App/Views/Sidebar/SidebarView.swift:590-603; App/AppModel.swift:387-389
    public void The_Settled_section_closes_the_list_latest_settled_first_with_rows_only_while_unfolded()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var open = ThreadAt(alpha, "Open", Now.AddDays(-3));
            var early = ThreadAt(alpha, "Early", Now.AddHours(-9)) with { IsSettled = true, SettledAt = Now.AddHours(-2) };
            var undated = ThreadAt(alpha, "Undated", Now.AddDays(-2)) with { IsSettled = true };
            var late = ThreadAt(alpha, "Late", Now.AddDays(-9)) with { IsSettled = true, SettledAt = Now.AddHours(-1) };
            var helper = ThreadAt(alpha, "Helper", Now.AddHours(-8)) with { ParentThreadID = early.Id };
            var model = Model(harness, [alpha], [open, early, undated, late, helper]);
            harness.Main.OnMain(() => InDays(() =>
            {
                var view = Detached(model);
                model.Settings.SettledCollapsed = true;
                var folded = ActivityItems(view);
                Assert.Equal("settled", IdOf(folded[^1]));
                Assert.Equal(3L, Get(KindOf(folded[^1]), "Count"));
                Assert.Equal(false, Get(KindOf(folded[^1]), "IsFirst"));
                model.Settings.SettledCollapsed = false;
                var items = ActivityItems(view);
                var at = items.FindIndex(item => IdOf(item) == "settled");
                Assert.Equal(["settled", "settled-" + U(late.Id), "settled-" + U(early.Id), "settled-" + U(undated.Id)], items.Skip(at).Select(IdOf));
                foreach (var row in items.Skip(at + 1))
                {
                    Assert.Null(Get(KindOf(row), "Peers"));
                    Assert.Equal(false, Get(KindOf(row), "HasHelpers"));
                }
            }));
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-041")] // App/Views/Sidebar/SidebarView.swift:595-599, :613-617, :628-631; App/AppSettings.swift:277-278, :724
    public void Activity_rows_carry_the_project_mark_in_the_icon_style_and_the_project_name_in_the_other()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha") with { Icon = new ProjectIcon.Symbol("star") };
            var beta = NewProject(harness, "Beta");
            var gone = NewProject(harness, "Gone");
            var inAlpha = ThreadAt(alpha, "In alpha", Now.AddHours(-1));
            var inBeta = ThreadAt(beta, "In beta", Now.AddHours(-2));
            var orphan = ThreadAt(gone, "Orphan", Now.AddHours(-3));
            var settled = ThreadAt(beta, "Settled", Now.AddHours(-4)) with { IsSettled = true, SettledAt = Now.AddHours(-4) };
            var model = Model(harness, [alpha, beta], [inAlpha, inBeta, orphan, settled]);
            harness.Main.OnMain(() => InDays(() =>
            {
                model.Settings.SettledCollapsed = false;
                var view = Detached(model);
                Assert.Contains(model.Threads, thread => thread.Id == orphan.Id); // its project is gone
                Assert.Equal(ActivityThreadStyle.Icon, model.Settings.ActivityThreadStyle); // :724 default .icon
                var folder = new ProjectIcon.Symbol("folder.fill");
                var icon = ThreadItems(ActivityItems(view)).ToList();
                Assert.Equal([inAlpha.Id, inBeta.Id, orphan.Id, settled.Id], icon.Select(item => ThreadOf(item).Id));
                Assert.All(icon, item => Assert.Null(Get(KindOf(item), "ProjectName")));
                Assert.Equal<ProjectIcon?>([new ProjectIcon.Symbol("star"), folder, folder, folder], icon.Select(item => (ProjectIcon?)Get(KindOf(item), "ProjectIcon")));

                model.Settings.ActivityThreadStyle = ActivityThreadStyle.ProjectName;
                Assert.Equal("projectName", harness.Defaults.Object("activityThreadStyle")); // :278
                var named = ThreadItems(ActivityItems(view)).ToList();
                Assert.Equal(["Alpha", "Beta", "", "Beta"], named.Select(item => (string?)Get(KindOf(item), "ProjectName")));
                Assert.Equal<ProjectIcon?>([new ProjectIcon.Symbol("star"), null, null, null], named.Select(item => (ProjectIcon?)Get(KindOf(item), "ProjectIcon")));
            }));
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-042")] // App/Views/Sidebar/SidebarView.swift:599, :609-617, :813-825
    public void Activity_rows_carry_their_own_section_as_peers_and_settled_rows_none()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var asking = ThreadAt(alpha, "Asking", Now.AddMinutes(-10));
            var pinnedA = ThreadAt(alpha, "Pinned A", Now.AddHours(-1)) with { IsPinned = true };
            var pinnedB = ThreadAt(alpha, "Pinned B", Now.AddHours(-2)) with { IsPinned = true };
            var todayA = ThreadAt(alpha, "Today A", Now.AddHours(-3));
            var todayB = ThreadAt(alpha, "Today B", Now.AddHours(-4));
            var yesterday = ThreadAt(alpha, "Yesterday", Now.AddDays(-1));
            var settled = ThreadAt(alpha, "Settled", Now.AddHours(-5)) with { IsSettled = true, SettledAt = Now.AddHours(-5) };
            var model = Model(harness, [alpha], [asking, pinnedA, pinnedB, todayA, todayB, yesterday, settled]);
            harness.Main.OnMain(() => InDays(() =>
            {
                model.Settings.SettledCollapsed = false;
                Approve(model, asking.Id);
                var view = Detached(model);
                var rows = ThreadItems(ActivityItems(view)).ToDictionary(item => ThreadOf(item).Id, item => (IEnumerable<Guid>?)Get(KindOf(item), "Peers"));
                Assert.Equal([asking.Id], rows[asking.Id]!);
                Assert.Equal([pinnedA.Id, pinnedB.Id], rows[pinnedA.Id]!);
                Assert.Equal([pinnedA.Id, pinnedB.Id], rows[pinnedB.Id]!);
                Assert.Equal([todayA.Id, todayB.Id], rows[todayB.Id]!);
                Assert.Equal([yesterday.Id], rows[yesterday.Id]!);
                Assert.Null(rows[settled.Id]);

                (IEnumerable<Guid> Order, IEnumerable<Guid>? Group) PeersOf(Guid id)
                {
                    var tuple = Call(view, "Peers", id)!;
                    return ((IEnumerable<Guid>)tuple.GetType().GetField("Item1")!.GetValue(tuple)!, (IEnumerable<Guid>?)tuple.GetType().GetField("Item2")!.GetValue(tuple));
                }
                model.Settings.SidebarActivityView = true;
                var today = PeersOf(todayA.Id);
                Assert.Equal([todayA.Id, todayB.Id], today.Order);
                Assert.Equal([todayA.Id, todayB.Id], today.Group!);
                var none = PeersOf(settled.Id);
                Assert.Empty(none.Order);
                Assert.Null(none.Group);
                Assert.Empty(PeersOf(Guid.NewGuid()).Order);
                // The project layout reorders among the project's open top-level threads (:823-824).
                model.Settings.SidebarActivityView = false;
                var project = PeersOf(todayA.Id);
                Assert.Equal(model.ThreadsIn(alpha).Where(thread => !thread.IsSettled).Select(thread => thread.Id), project.Order);
                Assert.Null(project.Group);
                Assert.Empty(PeersOf(Guid.NewGuid()).Order);
            }));
        });
    }

    // ---- PR-SIDEBAR-043 / -044 / -045: search ----

    private static List<object> SearchResults(SidebarView view, string query)
    {
        SearchBox(view).Text = query;
        return Items(Call(view, "SearchResults", Helpers(view)));
    }

    private static string ResultProject(object result) => ((Project)Get(result, "Project")!).Name;

    private static Guid[] ResultThreads(object result) => ((IEnumerable<ChatThread>)Get(result, "Threads")!).Select(thread => thread.Id).ToArray();

    private static long ResultCount(object result) => (long)Get(result, "Count")!;

    [Fact]
    [Trait("Req", "PR-SIDEBAR-043")] // App/Views/Sidebar/SidebarView.swift:737-749
    public void A_project_name_match_returns_its_every_thread_with_helpers_after_their_parent_and_a_title_match_only_matches()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var beta = NewProject(harness, "Beta");
            var gamma = NewProject(harness, "Gamma");
            var one = NewThread(alpha, "One", 1);
            var two = NewThread(alpha, "Two", 2);
            var helper = NewThread(alpha, "Helper", 3) with { ParentThreadID = one.Id };
            var settled = NewThread(alpha, "Three", 4) with { IsSettled = true, SettledAt = Base.AddHours(1) };
            var notes = NewThread(beta, "Alpha notes", 5);
            var other = NewThread(beta, "Other", 6);
            var lonely = NewThread(gamma, "Nothing here", 7);
            var model = Model(harness, [alpha, beta, gamma], [one, two, helper, settled, notes, other, lonely]);
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                // threads(in:) order (open newest first, then settled), each followed by its helpers (:742).
                var byName = SearchResults(view, "ALP");
                Assert.Equal(["Alpha", "Beta"], byName.Select(ResultProject));
                Assert.Equal([two.Id, one.Id, helper.Id, settled.Id], ResultThreads(byName[0]));
                Assert.Equal(4L, ResultCount(byName[0]));
                Assert.Equal([notes.Id], ResultThreads(byName[1]));
                Assert.Equal(2L, ResultCount(byName[1])); // the project's full candidate count

                var byTitle = SearchResults(view, "o");
                Assert.Equal(["Alpha", "Beta", "Gamma"], byTitle.Select(ResultProject));
                Assert.Equal([two.Id, one.Id], ResultThreads(byTitle[0]));
                Assert.Equal(4L, ResultCount(byTitle[0]));
                Assert.Equal([other.Id, notes.Id], ResultThreads(byTitle[1]));
                Assert.Equal([lonely.Id], ResultThreads(byTitle[2]));

                var helperOnly = SearchResults(view, "per");
                Assert.Equal(["Alpha"], helperOnly.Select(ResultProject));
                Assert.Equal([helper.Id], ResultThreads(helperOnly[0]));
                Assert.Empty(SearchResults(view, "zzz"));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-043")] // App/Views/Sidebar/SidebarView.swift:740, :743-745
    [Trait("Decision", "ED-009")]
    public void Search_lowercases_per_scalar_so_dotted_capital_I_dotless_i_and_the_final_sigma_match_as_Swift_does()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var travel = NewProject(harness, "Travel");
            var izmir = NewThread(travel, "\U00000130zmir", 1); // U+0130 LATIN CAPITAL LETTER I WITH DOT ABOVE
            var red = NewThread(travel, "K\U00000131rm\U00000131z\U00000131", 2); // U+0131 LATIN SMALL LETTER DOTLESS I
            var road = NewThread(travel, "\U0000039F\U00000394\U0000039F\U000003A3", 3); // GREEK CAPITAL OMICRON, DELTA, OMICRON, SIGMA
            var model = Model(harness, [travel], [izmir, red, road]);
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                Guid[] Found(string query) => SearchResults(view, query).SelectMany(ResultThreads).ToArray();
                // U+0130 lowercases to "i" + U+0307 COMBINING DOT ABOVE, so "iz" is not in it; the dotted needle is.
                Assert.Empty(Found("iz"));
                Assert.Equal([izmir.Id], Found("\U00000130z"));
                // The .NET APIs ED-009 rules out answer differently: Turkish-tailored lowercasing gives a bare "i" (so
                // "iz" would match) and invariant lowercasing leaves U+0130 alone (so the dotted needle would not).
                Assert.Contains("iz", "\U00000130zmir".ToLower(CultureInfo.GetCultureInfo("tr-TR")), StringComparison.Ordinal);
                Assert.DoesNotContain("i\U00000307z", "\U00000130zmir".ToLowerInvariant(), StringComparison.Ordinal); // U+0307 COMBINING DOT ABOVE
                // No Turkish tailoring: "I" lowercases to "i", never to U+0131.
                Assert.Empty(Found("KIR"));
                Assert.Equal([red.Id], Found("K\U00000131R"));
                // Per scalar, capital sigma is always U+03C3, never the final U+03C2.
                Assert.Equal([road.Id], Found("\U000003B4\U000003BF\U000003C3")); // GREEK SMALL DELTA, OMICRON, SIGMA
                Assert.Empty(Found("\U000003B4\U000003BF\U000003C2")); // GREEK SMALL DELTA, OMICRON, FINAL SIGMA
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-044")] // App/Views/Sidebar/SidebarView.swift:705-714
    public void Search_groups_are_separated_by_a_plain_10_point_spacer_that_is_no_list_item()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var beta = NewProject(harness, "Beta");
            var parent = NewThread(alpha, "Fix the parent", 1);
            var helper = NewThread(alpha, "Fix the helper", 2) with { ParentThreadID = parent.Id };
            var other = NewThread(beta, "Fix the other", 3);
            var model = Model(harness, [alpha, beta], [parent, helper, other]);
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                SearchBox(view).Text = "fix";
                var helpers = Helpers(view);
                var entries = Items(Call(view, "SearchEntries", Call(view, "SearchResults", helpers), helpers));
                string Shape(object entry) => Get(entry, "Value") switch
                {
                    double => "gap",
                    ValueTuple<Project, long> => "project",
                    ValueTuple<ChatThread, bool> => "thread",
                    var value => throw new InvalidOperationException(value?.GetType().Name),
                };
                Assert.Equal(["project", "thread", "thread", "gap", "project", "thread"], entries.Select(Shape));
                var gap = entries[3];
                Assert.Equal(10d, Get(gap, "Value")); // Chrome.groupGap
                Assert.Null(Get(gap, "Target")); // not an item ScrollViewReader can reach, unlike the project layout's gap
                var spacer = Assert.IsType<Border>(((Func<Control>)Get(gap, "Make")!)());
                Assert.Equal(10d, spacer.Height);
                Assert.Null(spacer.Child);
                Assert.Null(spacer.Background);
                var first = (ValueTuple<Project, long>)Get(entries[0], "Value")!;
                Assert.Equal(alpha.Id, first.Item1.Id);
                Assert.Equal(2L, first.Item2);
                var threads = entries.Where(entry => Shape(entry) == "thread").ToList();
                // Threads keyed like the main list (:711), handed hasHelpers (:712).
                Assert.Equal([U(parent.Id), U(helper.Id), U(other.Id)], threads.Select(entry => (string?)Get(entry, "Target")));
                Assert.Equal([true, false, false], threads.Select(entry => ((ValueTuple<ChatThread, bool>)Get(entry, "Value")!).Item2));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-045")] // App/Views/Sidebar/SidebarView.swift:693-703
    public void An_empty_search_shows_No_results_and_the_hint_6_points_apart_with_8_and_14_points_of_padding()
    {
        headless.OnUiThread(() =>
        {
            var stack = Assert.IsType<StackPanel>(typeof(SidebarView).GetMethod("NoResults", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null));
            Assert.Equal(Orientation.Vertical, stack.Orientation);
            Assert.Equal(6d, stack.Spacing);
            Assert.Equal(HorizontalAlignment.Left, stack.HorizontalAlignment);
            Assert.Equal(new Thickness(8, 14, 8, 0), stack.Margin);
            Assert.Equal(2, stack.Children.Count);
            var title = Assert.IsType<TextBlock>(stack.Children[0]);
            var hint = Assert.IsType<TextBlock>(stack.Children[1]);
            Assert.Equal("No results", title.Text);
            Assert.Equal("Try a thread title or a project name.", hint.Text);
            Assert.Equal(13d, title.FontSize); // .body, B1:DroppyCode/UI/Common/ChatZoom.swift:51 (ED-059)
            Assert.Equal(FontWeight.SemiBold, title.FontWeight);
            Assert.Equal(11d, hint.FontSize); // .subheadline, B1:DroppyCode/UI/Common/ChatZoom.swift:53 (ED-059)
            Assert.Equal(FontWeight.Normal, hint.FontWeight);
            Assert.Equal(Chrome.PrimaryText.Opacity(0.92).Resolve(title.ActualThemeVariant), ((ISolidColorBrush)title.Foreground!).Color);
            Assert.Equal(Chrome.SecondaryText.Resolve(hint.ActualThemeVariant), ((ISolidColorBrush)hint.Foreground!).Color);
        });
    }

    // ---- PR-SIDEBAR-046 to -050: the drag ----

    // A stand-in for a row's own button (every row is a plain button): a 28 pt slot that counts its clicks.
    private static Button StandInRow(Action clicked) =>
        PlainControls.Button(new Border { Height = 28, Background = Brushes.Transparent }, clicked);

    private static Point At(Control control, double x, double y, Visual window) => control.TranslatePoint(new Point(x, y), window)!.Value;

    [Fact]
    [Trait("Req", "PR-SIDEBAR-047")] // App/Views/Sidebar/SidebarView.swift:129-137
    public void A_press_moved_less_than_4_points_still_clicks_the_row_and_a_drag_grabs_the_row_under_its_start_without_clicking()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var id = Guid.NewGuid();
            SidebarView view = null!;
            harness.Main.OnMain(() => view = Attached(model));
            harness.Main.OnMain(() =>
            {
                var window = (AvaloniaWindow)TopLevel.GetTopLevel(view)!;
                var clicks = 0;
                var row = StandInRow(() => clicks++);
                ListPanel(view).Children.Add(row);
                window.UpdateLayout();
                NoteFrame(view, id, row.Bounds);
                var start = At(row, 40, 10, window);

                window.MouseDown(start, MouseButton.Left);
                window.MouseMove(new Point(start.X, start.Y + 3), RawInputModifiers.LeftMouseButton);
                window.MouseUp(new Point(start.X, start.Y + 3), MouseButton.Left);
                Assert.Equal(1, clicks);
                Assert.Null(Drag(view).Id);

                window.MouseDown(start, MouseButton.Left);
                window.MouseMove(new Point(start.X, start.Y + 5), RawInputModifiers.LeftMouseButton);
                Assert.Equal(id, Drag(view).Id);
                Assert.Equal(5d, Drag(view).Translation);
                // Only the vertical travel counts, and the row stays grabbed wherever the pointer goes.
                window.MouseMove(new Point(start.X + 30, start.Y + 12), RawInputModifiers.LeftMouseButton);
                Assert.Equal(12d, Drag(view).Translation);
                window.MouseMove(new Point(start.X, start.Y + 200), RawInputModifiers.LeftMouseButton);
                Assert.Equal(id, Drag(view).Id);
                Assert.Equal(200d, Drag(view).Translation);
                window.MouseMove(new Point(start.X, start.Y + 6), RawInputModifiers.LeftMouseButton);
                window.MouseUp(new Point(start.X, start.Y + 6), MouseButton.Left);
                Assert.Equal(1, clicks); // the drag won over the row's click
                Assert.Null(Drag(view).Id);
                Close(view);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-047")] // App/Views/Sidebar/SidebarView.swift:131-132, :339-347
    public void A_drag_from_a_point_no_open_row_reported_grabs_nothing_and_only_open_rows_report_their_frame()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            SidebarView view = null!;
            harness.Main.OnMain(() => view = Attached(model));
            harness.Main.OnMain(() =>
            {
                var window = (AvaloniaWindow)TopLevel.GetTopLevel(view)!;
                var clicks = 0;
                var row = StandInRow(() => clicks++);
                var list = ListPanel(view);
                list.Children.Add(row);
                window.UpdateLayout();
                var start = At(row, 40, 10, window);
                window.MouseDown(start, MouseButton.Left);
                window.MouseMove(new Point(start.X, start.Y + 10), RawInputModifiers.LeftMouseButton);
                Assert.Null(Drag(view).Id);
                window.MouseUp(new Point(start.X, start.Y + 10), MouseButton.Left);
                Assert.Null(Drag(view).Id);
                Assert.Equal(0, clicks); // the gesture took the press, though it moved nothing

                // :341-347: an open row reports its height and frame, a settled one its height alone; a row that
                // leaves the list is forgotten.
                var project = new Project("Alpha", @"C:\alpha");
                var open = AppModelSamples.Thread(project);
                var settled = AppModelSamples.Thread(project) with { IsSettled = true };
                var openRow = (Control)Call(view, "ReportsFrame", new Border { Height = 30 }, U(open.Id), open)!;
                var settledRow = (Control)Call(view, "ReportsFrame", new Border { Height = 24 }, "settled-" + U(settled.Id), settled)!;
                list.Children.Add(openRow);
                list.Children.Add(settledRow);
                window.UpdateLayout();
                var frames = Field(view, "rowFrames")!;
                var heights = (IReadOnlyDictionary<string, double>)Get(Field(view, "rowHeights")!, "Values")!;
                Assert.Equal(30d, heights[U(open.Id)]);
                Assert.Equal(24d, heights["settled-" + U(settled.Id)]);
                var openCentre = openRow.Bounds.Center;
                Assert.Equal(open.Id, ThreadAtPoint(frames, openCentre));
                Assert.Null(ThreadAtPoint(frames, settledRow.Bounds.Center));
                list.Children.Remove(openRow);
                Assert.Null(ThreadAtPoint(frames, openCentre));
                Close(view);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-046")] // App/Views/Sidebar/SidebarView.swift:129, :136
    public void While_the_search_field_holds_a_query_no_drag_is_recognised_and_the_row_still_clicks()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var id = Guid.NewGuid();
            SidebarView view = null!;
            harness.Main.OnMain(() =>
            {
                view = Attached(model);
                SearchBox(view).Text = " x ";
            });
            harness.Main.OnMain(() =>
            {
                var window = (AvaloniaWindow)TopLevel.GetTopLevel(view)!;
                var clicks = 0;
                var row = StandInRow(() => clicks++);
                ListPanel(view).Children.Add(row);
                window.UpdateLayout();
                NoteFrame(view, id, row.Bounds);
                var start = At(row, 40, 10, window);
                window.MouseDown(start, MouseButton.Left);
                window.MouseMove(new Point(start.X + 20, start.Y + 8), RawInputModifiers.LeftMouseButton);
                Assert.Null(Drag(view).Id);
                window.MouseUp(new Point(start.X + 20, start.Y + 8), MouseButton.Left);
                Assert.Null(Drag(view).Id);
                Assert.Equal(1, clicks);
                // The same drag with the field cleared grabs the row.
                SearchBox(view).Text = "";
                window.MouseDown(start, MouseButton.Left);
                window.MouseMove(new Point(start.X, start.Y + 8), RawInputModifiers.LeftMouseButton);
                Assert.Equal(id, Drag(view).Id);
                window.MouseUp(new Point(start.X, start.Y + 8), MouseButton.Left);
                Assert.Equal(1, clicks);
                Close(view);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-048")] // App/Views/Sidebar/SidebarView.swift:2456-2472
    [Trait("Req", "PR-SIDEBAR-047")] // App/Views/Sidebar/SidebarView.swift:2469-2471
    public void A_point_inside_two_frames_finds_one_of_them_and_a_frame_holds_its_min_edges_but_not_its_max_edges()
    {
        var frames = Activator.CreateInstance(Private("RowFrames"), nonPublic: true)!;
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        CallOn(frames, "Note", a, new Rect(0, 0, 100, 28));
        CallOn(frames, "Note", b, new Rect(0, 20, 100, 28));
        CallOn(frames, "Note", c, new Rect(0, 100, 100, 28));
        Assert.Contains(ThreadAtPoint(frames, new Point(50, 24)), new Guid?[] { a, b });
        Assert.Equal(a, ThreadAtPoint(frames, new Point(50, 10)));
        Assert.Equal(b, ThreadAtPoint(frames, new Point(50, 40)));
        // CGRect.contains: minX <= x < maxX and minY <= y < maxY.
        Assert.Equal(c, ThreadAtPoint(frames, new Point(0, 100)));
        Assert.Null(ThreadAtPoint(frames, new Point(50, 128)));
        Assert.Null(ThreadAtPoint(frames, new Point(100, 110)));
        Assert.Null(ThreadAtPoint(frames, new Point(50, 70)));
        CallOn(frames, "Forget", c);
        Assert.Null(ThreadAtPoint(frames, new Point(50, 110)));
        CallOn(frames, "Note", c, new Rect(0, 60, 100, 28));
        Assert.Equal(c, ThreadAtPoint(frames, new Point(50, 70)));
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-049")] // App/Views/Sidebar/SidebarView.swift:771-776, :803-805, :829-868
    public void A_grab_takes_its_peers_and_slot_heights_once_and_a_release_without_a_grab_does_nothing()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var older = NewThread(alpha, "Older", 1);
            var newer = NewThread(alpha, "Newer", 2);
            var settled = NewThread(alpha, "Settled", 3) with { IsSettled = true, SettledAt = Base.AddHours(1) };
            var model = Model(harness, [alpha], [older, newer, settled]);
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                var idle = Drag(view);
                Call(view, "DragEnded");
                Assert.Same(idle, Drag(view)); // :804 nothing grabbed, nothing written
                Assert.Null(Field(view, "dragPeers"));

                Call(view, "DragChanged", older.Id, 10d);
                Assert.Equal(older.Id, Drag(view).Id);
                Assert.Equal(10d, Drag(view).Translation);
                var peers = Field(view, "dragPeers")!;
                Assert.Equal([newer.Id, older.Id], (IEnumerable<Guid>)Get(peers, "Order")!);
                Assert.Null(Get(peers, "Group"));
                // Unmeasured rows: Chrome.rowHeight plus the list's 1 pt spacing (:833).
                var heights = (IReadOnlyDictionary<Guid, double>)Get(peers, "Heights")!;
                Assert.Equal(2, heights.Count);
                Assert.Equal(29d, heights[newer.Id]);
                Assert.Equal(29d, heights[older.Id]);

                Call(view, "DragChanged", older.Id, 12d);
                Assert.Same(peers, Field(view, "dragPeers")); // no swap, no new snapshot
                Assert.Equal(12d, Drag(view).Translation);

                Call(view, "DragEnded");
                Assert.Null(Drag(view).Id);
                Assert.Null(Field(view, "dragPeers"));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-050")] // App/Views/Sidebar/SidebarView.swift:757-762, :777-779; UI/Chrome/RowDrag.swift:15
    public void The_grabbed_row_is_lifted_and_offset_by_the_pointer_travel_at_once_even_inside_an_animation()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var grabbed = Guid.NewGuid();
            var neighbour = Guid.NewGuid();
            SidebarView view = null!;
            Control lifted = null!;
            Control still = null!;
            AvaloniaWindow window = null!;
            harness.Main.OnMain(() =>
            {
                view = Detached(model);
                lifted = (Control)Call(view, "Lifted", grabbed, (Func<bool, Control>)(isDragged => new Border { Height = 28, Tag = isDragged }))!;
                still = (Control)Call(view, "Lifted", neighbour, (Func<bool, Control>)(isDragged => new Border { Height = 28, Tag = isDragged }))!;
                window = new AvaloniaWindow { Content = new StackPanel { Children = { lifted, still } }, Width = 200, Height = 200 };
                window.Show();
            });
            static double OffsetOf(Control control) => control.RenderTransform is TranslateTransform translate ? translate.Y : 0;
            static object? DraggedInput(Control control) => ((Decorator)control).Child!.Tag;
            harness.Main.OnMain(() =>
            {
                Assert.Equal(0d, OffsetOf(lifted));
                Assert.Equal(0, lifted.ZIndex);
                Assert.Equal(false, DraggedInput(lifted));
                Call(view, "DragChanged", grabbed, 30d);
            });
            harness.Main.OnMain(() =>
            {
                Assert.Equal(30d, OffsetOf(lifted));
                Assert.Equal(1, lifted.ZIndex);
                Assert.Equal(true, DraggedInput(lifted)); // the row is built with isDragged (:759)
                Assert.Equal(0d, OffsetOf(still));
                Assert.Equal(0, still.ZIndex);
                Assert.Equal(false, DraggedInput(still));
                Transaction.WithAnimation(Animation.Spring(response: 0.5, dampingFraction: 1), () => Call(view, "DragChanged", grabbed, 45d));
            });
            harness.Main.OnMain(() =>
            {
                Assert.Equal(45d, OffsetOf(lifted)); // no animation: it tracks the pointer one to one
                window.Close();
            });
        });
    }
}
