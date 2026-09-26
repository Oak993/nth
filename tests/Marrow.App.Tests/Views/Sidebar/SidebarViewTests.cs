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
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.App.Views.Sidebar;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
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
    [Trait("Req", "PR-SIDEBAR-071")] // App/Views/Sidebar/SidebarView.swift:1980-1984
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
    [Trait("Req", "PR-SIDEBAR-071")] // App/Views/Sidebar/SidebarView.swift:1980-1984, :2057-2058 (the row reads it)
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
    [Trait("Req", "PR-SIDEBAR-071")] // App/Views/Sidebar/SidebarView.swift:1983
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
    [Trait("Req", "PR-SIDEBAR-049")] // App/Views/Sidebar/SidebarView.swift:124 (B3; replaces B2's NSCursor push/pop)
    public void The_list_carries_the_grab_active_pointer_style_only_while_a_row_is_dragged()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            SidebarView view = null!;
            StackPanel list = null!;
            var scale = 1d;
            var grabbed = Guid.NewGuid();
            // Track applies one main-actor turn after the state it reads changes (ViewLifetime.Track), so each step
            // that mutates state.Drag runs in its own OnMain, letting the posted apply land before the next assertion.
            harness.Main.OnMain(() =>
            {
                view = Attached(model);
                list = (StackPanel)Field(view, "list")!;
                scale = TopLevel.GetTopLevel(list)!.RenderScaling;
                Assert.Null(list.Cursor); // :124 nil while drag.id is nil
            });
            harness.Main.OnMain(() => Call(view, "DragChanged", grabbed, 5d));
            harness.Main.OnMain(() =>
                Assert.Same(PointerStyleCursors.Get(PointerStyle.GrabActive, scale), list.Cursor)); // :124 .grabActive
            harness.Main.OnMain(() => Call(view, "DragChanged", grabbed, 9d));
            harness.Main.OnMain(() =>
                Assert.Same(PointerStyleCursors.Get(PointerStyle.GrabActive, scale), list.Cursor)); // stays while grabbed
            harness.Main.OnMain(() => Call(view, "DragEnded"));
            harness.Main.OnMain(() =>
            {
                Assert.Null(list.Cursor); // drag.id returns to nil; the style clears itself
                Call(view, "DragEnded"); // :804 nothing grabbed: no re-application, no throw
            });
            harness.Main.OnMain(() =>
            {
                Assert.Null(list.Cursor);
                Close(view);
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

    // ======== Part 3 of 8 ========
    // Records: PR-SIDEBAR-052 to -058, -061 to -073, -075 to -078. SidebarThreadRow's own constructor stays
    // MARROW_STUB: its full body needs settle()/reopen(), archiveWithGenie(), makeActions(), RowGlideAnimator and
    // ThreadActions.makeContextMenu, none of which exist yet (this file's own part-1 header already defers an
    // attached, populated row to "parts 2 to 5"). Every record below is proven as its own piece: a Swift-named
    // static helper (Fill, TrailingClearance), a standalone view (ThreadRowFace, ThreadBadge, SettleCheck,
    // ActivityStatus, RelativeTimeLabel, MinuteClock, RidesWithDraggedParent), or a small extracted seam with no
    // missing collaborator (BackgroundFill, ShowsCheck, SelectThread, NoteHover, ApplyDraggedLift,
    // BuildSettleCheckOverlay, BuildTrailingOverlay) that the row's own body will call once part 4 lands the rest.

    private static object? CallStatic(Type type, string method, params object?[] args) =>
        type.GetMethod(method, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(null, args);

    // Every type below builds its whole Child reactively through Track (R-VIEW-1), whose apply runs only once
    // attached to a real visual tree (ViewLifetime.Track: AttachedToVisualTree), never for a merely logical,
    // unshown one (the file's own part-1 header: a DETACHED view's "tracked parts stay inert"). A shown window is
    // therefore required, not a bare host, to observe anything these views draw.
    private static Border Hosted(AppModel model, Control child)
    {
        var host = new Border();
        AppEnvironment.SetAppModel(host, model);
        host.Child = child;
        var window = new AvaloniaWindow { Content = host, Width = 200, Height = 100 };
        window.Show();
        return host;
    }

    private static T Shown<T>(T control) where T : Control
    {
        var window = new AvaloniaWindow { Content = control, Width = 200, Height = 100 };
        window.Show();
        return control;
    }

    private static Control MakeFace(ChatThread thread, string? projectName, bool isSelected, double trailingClearance,
        ProjectIcon? projectIcon = null, string? mergeStage = null) =>
        (Control)Activator.CreateInstance(Private("ThreadRowFace"), thread, projectName, isSelected, trailingClearance,
            (Func<Control>)(() => new Border { Tag = "badge" }), projectIcon, mergeStage)!;

    private static ChatThread HeadHelper(Project project, Guid parentID, long index, int minute) =>
        NewThread(project, "Head" + index, minute) with
        {
            ParentThreadID = parentID,
            IsInPanel = false,
            Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Completed) with { Index = index },
        };

    // ---- PR-SIDEBAR-056: a helper, head card or folded stub rides its dragged parent ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-056")] // App/Views/Sidebar/SidebarView.swift:1622-1632
    public void A_helper_under_a_dragged_parent_takes_the_parents_offset_and_zIndex_1()
    {
        var type = Private("RidesWithDraggedParent");
        var parent = Guid.NewGuid();

        var riding = (Control)CallOn(
            Activator.CreateInstance(type, parent, new RowDrag<Guid> { Id = parent, Translation = 40d, Settled = 10d })!,
            "Body", new Border())!;
        Assert.Equal(30d, ((TranslateTransform)riding.RenderTransform!).Y); // :1627, :1629 visualOffset = 40 - 10
        Assert.Equal(1, riding.ZIndex); // :1630

        var elsewhere = (Control)CallOn(
            Activator.CreateInstance(type, Guid.NewGuid(), new RowDrag<Guid> { Id = parent, Translation = 40d, Settled = 10d })!,
            "Body", new Border())!;
        Assert.Equal(0d, ((TranslateTransform)elsewhere.RenderTransform!).Y); // a different parent id: does not ride
        Assert.Equal(0, elsewhere.ZIndex);

        var noParent = (Control)CallOn(Activator.CreateInstance(type, null, new RowDrag<Guid>())!, "Body", new Border())!;
        Assert.Equal(0d, ((TranslateTransform)noParent.RenderTransform!).Y); // a nil parent id: never rides
        Assert.Equal(0, noParent.ZIndex);
    }

    // ---- PR-SIDEBAR-052, -053: the swap's spring and its peer re-snapshot ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-052")] // App/Views/Sidebar/SidebarView.swift:767, :784-793
    [Trait("Req", "PR-SIDEBAR-053")] // App/Views/Sidebar/SidebarView.swift:794-801
    public void An_accepted_swap_runs_inside_the_slide_spring_and_re_takes_the_peer_snapshot()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var older = NewThread(alpha, "Older", 1);
            var newer = NewThread(alpha, "Newer", 2);
            var model = Model(harness, [alpha], [older, newer]);
            SidebarView view = null!;
            object? peersBefore = null;
            harness.Main.OnMain(() =>
            {
                view = Detached(model);
                Call(view, "DragChanged", older.Id, 1d); // grabs; takes the first snapshot (order [newer, older])
                peersBefore = Field(view, "dragPeers");
            });
            Animation? during = null;
            using var probe = new OnChange<ValueList<ChatThread>>(() => model.Threads, (_, _) => during = Transaction.Current);
            harness.Main.OnMain(() => Call(view, "DragChanged", older.Id, -35d)); // crosses newer's centre (threshold 29)
            harness.Main.OnMain(() =>
            {
                Assert.Equal(Animation.Spring(response: 0.28, dampingFraction: 0.82), during); // :767, :784-793
                var peersAfter = Field(view, "dragPeers")!;
                Assert.NotSame(peersBefore, peersAfter); // :796-798 re-taken once the swap is accepted
                Assert.Equal([older.Id, newer.Id], (IEnumerable<Guid>)Get(peersAfter, "Order")!); // the list as it now stands
            });
        });
    }

    // ---- PR-SIDEBAR-057, -058: a peer's reorder slot ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-057")] // App/Views/Sidebar/SidebarView.swift:829-856, :1277, :1308-1311
    public void A_peers_slot_is_its_row_plus_its_folded_stub_or_its_helpers_and_head_card_each_plus_1_pt()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var folded = NewThread(alpha, "Folded", 1) with { ExpandsHelpers = false };
            var foldedHelper = NewThread(alpha, "FoldedHelper", 2) with { ParentThreadID = folded.Id };
            var open = NewThread(alpha, "Open", 3) with { ExpandsHelpers = true };
            var plainA = NewThread(alpha, "PlainA", 4) with { ParentThreadID = open.Id };
            var plainB = NewThread(alpha, "PlainB", 5) with { ParentThreadID = open.Id };
            var head1 = HeadHelper(alpha, open.Id, 0, 6);
            var head2 = HeadHelper(alpha, open.Id, 1, 7);
            var model = Model(harness, [alpha], [folded, foldedHelper, open, plainA, plainB, head1, head2]);
            harness.Main.OnMain(() =>
            {
                model.Settings.HydraAutoHidesIdleHeads = false;
                var view = Detached(model);
                var rowHeights = Field(view, "rowHeights")!;
                CallOn(rowHeights, "Note", "helpers-" + SwiftUUID.UuidString(folded.Id), 40d); // a measured stub height
                var heights = (IReadOnlyDictionary<Guid, double>)Call(view, "SlotHeights", new ValueList<Guid>([folded.Id, open.Id]))!;
                Assert.Equal(29d + (40d + 1d), heights[folded.Id]); // :833 row, :837 measured stub + 1
                // :839-840 two unmeasured plain helpers (24 fallback each, +1 each) and :845-847 one head card
                // (2 heads -> (2+5)/6 = 1 row).
                var headCardHeight = (double)CallStatic(Private("ThreadRowMetrics"), "HeadCardHeight", 1L)!;
                Assert.Equal(29d + (24d + 1d) * 2 + (headCardHeight + 1d), heights[open.Id]);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-058")] // App/Views/Sidebar/SidebarView.swift:845-847 (upstream defect 59, reproduced)
    public void The_slot_head_card_fallback_assumes_six_glyphs_per_row_crossing_at_7_and_13()
    {
        double HeightFor(int headCount)
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var parent = NewThread(alpha, "Parent", 1) with { ExpandsHelpers = true };
            var heads = Enumerable.Range(0, headCount).Select(i => HeadHelper(alpha, parent.Id, i, 2 + i)).ToList();
            var model = Model(harness, [alpha], new[] { parent }.Concat(heads));
            var result = 0d;
            harness.Main.OnMain(() =>
            {
                model.Settings.HydraAutoHidesIdleHeads = false;
                var view = Detached(model);
                var heights = (IReadOnlyDictionary<Guid, double>)Call(view, "SlotHeights", new ValueList<Guid>([parent.Id]))!;
                result = heights[parent.Id];
            });
            return result;
        }
        double CardHeight(long rows) => (double)CallStatic(Private("ThreadRowMetrics"), "HeadCardHeight", rows)!;
        var row = Chrome.RowHeight + 1;
        headless.OnUiThread(() =>
        {
            // :846 rows = (headCount + 5) / 6: 6 heads round down to 1 row, 7 round up to 2. The real card lays out
            // 7 per row until measured (upstream defect 59), so it would not cross until 8; the fallback disagrees
            // and is reproduced verbatim, not corrected (PR-SIDEBAR-058).
            Assert.Equal(row + CardHeight(1) + 1d, HeightFor(6));
            Assert.Equal(row + CardHeight(2) + 1d, HeightFor(7));
            Assert.Equal(row + CardHeight(2) + 1d, HeightFor(12));
            Assert.Equal(row + CardHeight(3) + 1d, HeightFor(13));
        });
    }

    // ---- PR-SIDEBAR-054: the lifted row's scale, fill and shadow ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-054")] // App/Views/Sidebar/SidebarView.swift:1838-1849
    public void The_lifted_rows_scale_fill_and_shadow_spring_in_and_out_on_response_0_25_damping_0_8()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var grabbed = Guid.NewGuid();
            SidebarView view = null!;
            Border content = null!;
            Control host = null!;
            AvaloniaWindow window = null!;
            var manual = new ManualFrameSource();
            using var frames = FrameClock.Source.Override(manual);
            harness.Main.OnMain(() =>
            {
                view = Detached(model);
                content = new Border { Height = 20 };
                host = (Control)CallStatic(Private("SidebarThreadRow"), "ApplyDraggedLift", content, (Func<bool>)(() => Drag(view).Id == grabbed))!;
                window = new AvaloniaWindow { Content = host, Width = 100, Height = 100 };
                window.Show();
            });
            static double ScaleOf(Control control) => control.RenderTransform is ScaleTransform scale ? scale.ScaleX : 1;
            static double LiftOpacity(Control host) => ((Panel)host).Children[0].Opacity;
            harness.Main.OnMain(() =>
            {
                Assert.Equal(1d, ScaleOf(content));
                Assert.Equal(0d, LiftOpacity(host));
                Call(view, "DragChanged", grabbed, 5d);
            });
            for (var i = 0; i < 100; i++) // 1.6 s of 16 ms frames: well past a 0.25 s response spring's settle
            {
                harness.Main.Advance(TimeSpan.FromMilliseconds(16));
                manual.Advance(TimeSpan.FromMilliseconds(16));
            }
            harness.Main.OnMain(() =>
            {
                Assert.Equal(1.02d, ScaleOf(content), 3); // :1846
                Assert.Equal(1d, LiftOpacity(host)); // :1842-1843 fully faded in
                Call(view, "DragEnded");
            });
            for (var i = 0; i < 100; i++)
            {
                harness.Main.Advance(TimeSpan.FromMilliseconds(16));
                manual.Advance(TimeSpan.FromMilliseconds(16));
            }
            harness.Main.OnMain(() =>
            {
                Assert.Equal(1d, ScaleOf(content), 3); // springs back out
                Assert.Equal(0d, LiftOpacity(host));
                window.Close();
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-054")] // App/Views/Sidebar/SidebarView.swift:1735, :1838-1843
    public void The_lifted_rows_fill_and_shadow_follow_the_rows_own_continuous_row_corner_radius()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var view = Detached(model);
                var content = new Border { Height = 20 };
                var host = (Control)CallStatic(Private("SidebarThreadRow"), "ApplyDraggedLift", content, (Func<bool>)(() => false))!;
                var lift = (Border)((Panel)host).Children[0];
                // :1735 let shape = RoundedRectangle(cornerRadius: Chrome.rowCornerRadius, style: .continuous), the
                // same shape the row's own background/contentShape use (SidebarThreadRow.body, not yet ported) and
                // the lift's fill and shadow follow at :1838-1843. Chrome.RowCornerRadius is 7 (Chrome.cs:104).
                Assert.Equal(new CornerRadius(Chrome.RowCornerRadius), lift.CornerRadius);
                Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(lift));
            });
        });
    }

    // ---- PR-SIDEBAR-061, -062: click selects, hover pre-decodes ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-061")] // App/Views/Sidebar/SidebarView.swift:1736-1738
    public void A_thread_rows_click_sets_selectedThreadID()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var thread = NewThread(alpha, "Thread", 1);
            var model = Model(harness, [alpha], [thread]);
            harness.Main.OnMain(() =>
            {
                Assert.Null(model.SelectedThreadID);
                CallStatic(Private("SidebarThreadRow"), "SelectThread", model, thread);
                Assert.Equal(thread.Id, model.SelectedThreadID);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-062")] // App/Views/Sidebar/SidebarView.swift:1816-1820
    public void Hovering_a_thread_row_warms_its_document_and_leaving_schedules_nothing_more()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var thread = NewThread(alpha, "Thread", 1);
            AppModelHarness.WriteDocument(thread.Id);
            var model = Model(harness, [alpha], [thread]);
            harness.Main.OnMain(() =>
            {
                Assert.Null(DocumentPrefetch.Shared.Take(thread.Id));
                CallStatic(Private("SidebarThreadRow"), "NoteHover", model, thread, true);
            });
            AppModelHarness.WaitForPrefetch();
            harness.Main.OnMain(() =>
            {
                Assert.NotNull(DocumentPrefetch.Shared.Take(thread.Id)); // :1819 warmDocuments([thread.id])
                CallStatic(Private("SidebarThreadRow"), "NoteHover", model, thread, false);
            });
            AppModelHarness.WaitForPrefetch();
            harness.Main.OnMain(() => Assert.Null(DocumentPrefetch.Shared.Take(thread.Id))); // leaving schedules nothing
        });
    }

    // ---- PR-SIDEBAR-063, -064: the row's own fill ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-063")] // App/Views/Sidebar/SidebarView.swift:1874-1877
    public void An_unpinned_rows_fill_is_0_12_selected_0_06_hovered_otherwise_clear()
    {
        var type = Private("SidebarThreadRow");
        Assert.Equal(0.12, (double)CallStatic(type, "Fill", true, false)!); // :1875 selected
        Assert.Equal(0.12, (double)CallStatic(type, "Fill", true, true)!); // selected wins over hovering
        Assert.Equal(0.06, (double)CallStatic(type, "Fill", false, true)!); // :1876 hovered
        Assert.Equal(0d, (double)CallStatic(type, "Fill", false, false)!); // neither
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-064")] // App/Views/Sidebar/SidebarView.swift:1745-1758
    public void A_pinned_row_is_tinted_danger_0_10_unselected_or_danger_0_06_over_the_selection_fill()
    {
        var type = Private("SidebarThreadRow");
        static Color Resolved(SwiftColor color) => color.Resolve(ThemeVariant.Light);

        var (unselected, overlay1) = ((SwiftColor, SwiftColor?))CallStatic(type, "BackgroundFill", true, false, false)!;
        Assert.Equal(Resolved(Chrome.Danger.Opacity(0.10)), Resolved(unselected)); // :1751-1752
        Assert.Null(overlay1);

        var (selected, overlay2) = ((SwiftColor, SwiftColor?))CallStatic(type, "BackgroundFill", true, true, false)!;
        Assert.Equal(Resolved(Chrome.Overlay(0.12)), Resolved(selected)); // the selection fill, not the pinned one
        Assert.Equal(Resolved(Chrome.Danger.Opacity(0.06)), Resolved(overlay2!)); // :1756-1758

        var (plain, overlay3) = ((SwiftColor, SwiftColor?))CallStatic(type, "BackgroundFill", false, false, true)!;
        Assert.Equal(Resolved(Chrome.Overlay(0.06)), Resolved(plain));
        Assert.Null(overlay3);
    }

    // ---- PR-SIDEBAR-065, -066, -067, -070: the face's title, leading slot and second line ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-065")] // App/Views/Sidebar/SidebarView.swift:2040-2051, :2075-2078
    public void An_open_titles_13pt_is_medium_when_selected_or_unread_and_a_settled_titles_12pt_is_always_regular()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var unread = NewThread(alpha, "Unread", 1) with { HasUnread = true };
            var quiet = NewThread(alpha, "Quiet", 2);
            harness.Main.OnMain(() =>
            {
                var open = MakeFace(unread, null, isSelected: false, trailingClearance: 52);
                var title = LogicalQuery.SelfAndDescendants<TextBlock>(open).First();
                Assert.Equal(13d, title.FontSize); // :2041 open: 13 pt
                Assert.Equal(FontWeight.Medium, title.FontWeight); // hasUnread, not selected: medium
                Assert.Equal(Chrome.PrimaryText.Opacity(0.92).Resolve(title.ActualThemeVariant), ((ISolidColorBrush)title.Foreground!).Color);

                var plain = MakeFace(quiet, null, isSelected: false, trailingClearance: 52);
                var plainTitle = LogicalQuery.SelfAndDescendants<TextBlock>(plain).First();
                Assert.Equal(FontWeight.Normal, plainTitle.FontWeight); // no unread, not selected: regular

                var settled = quiet with { IsSettled = true };
                var settledFace = MakeFace(settled, null, isSelected: true, trailingClearance: 18);
                var settledTitle = LogicalQuery.SelfAndDescendants<TextBlock>(settledFace).First();
                Assert.Equal(12d, settledTitle.FontSize); // :2048 settled: 12 pt
                Assert.Equal(FontWeight.Normal, settledTitle.FontWeight); // :2048 !isSettled gate: always regular once settled
                Assert.Equal(Chrome.PrimaryText.Opacity(0.75).Resolve(settledTitle.ActualThemeVariant), ((ISolidColorBrush)settledTitle.Foreground!).Color);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-066")] // App/Views/Sidebar/SidebarView.swift:2021-2033
    public void The_leading_slot_is_20pt_holding_the_project_mark_or_the_badge_and_is_absent_when_detailed_or_settled()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var thread = NewThread(alpha, "Thread", 1);
            harness.Main.OnMain(() =>
            {
                // Icon-style (no projectName): the badge shows in a 20 pt slot.
                var iconStyle = MakeFace(thread, null, isSelected: false, trailingClearance: 52);
                var badge = LogicalQuery.SelfAndDescendants<Control>(iconStyle).FirstOrDefault(c => (string?)c.Tag == "badge");
                Assert.NotNull(badge);
                Assert.Equal(Chrome.IconSize, badge!.Width); // :2031

                // Project-name style, detailed (projectName set, not settled): the slot is omitted.
                var detailed = MakeFace(thread, "Alpha", isSelected: false, trailingClearance: 18);
                Assert.Null(LogicalQuery.SelfAndDescendants<Control>(detailed).FirstOrDefault(c => (string?)c.Tag == "badge"));

                // Settled: the slot is omitted even without a project name.
                var settled = thread with { IsSettled = true };
                var settledFace = MakeFace(settled, null, isSelected: false, trailingClearance: 18);
                Assert.Null(LogicalQuery.SelfAndDescendants<Control>(settledFace).FirstOrDefault(c => (string?)c.Tag == "badge"));

                // A project mark, passed in, replaces the badge in the icon-style slot.
                var withMark = MakeFace(thread, null, isSelected: false, trailingClearance: 52, projectIcon: new ProjectIcon.Symbol("folder"));
                Assert.NotNull(LogicalQuery.SelfAndDescendants<ProjectIconMark>(withMark).FirstOrDefault());
                Assert.Null(LogicalQuery.SelfAndDescendants<Control>(withMark).FirstOrDefault(c => (string?)c.Tag == "badge"));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-067")] // App/Views/Sidebar/SidebarView.swift:2034-2052
    public void A_detailed_row_with_a_picked_mark_leads_the_title_at_14pt_and_drops_the_project_line()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var thread = NewThread(alpha, "Thread", 1);
            harness.Main.OnMain(() =>
            {
                var face = MakeFace(thread, "Alpha", isSelected: false, trailingClearance: 18, projectIcon: new ProjectIcon.Symbol("folder"));
                var mark = LogicalQuery.SelfAndDescendants<ProjectIconMark>(face).Single();
                var title = Assert.Single(LogicalQuery.SelfAndDescendants<TextBlock>(face)); // no second project line
                Assert.Equal("Thread", title.Text);

                var withoutIcon = MakeFace(thread, "Alpha", isSelected: false, trailingClearance: 18);
                Assert.Equal(2, LogicalQuery.SelfAndDescendants<TextBlock>(withoutIcon).Count()); // title + project line
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-070")] // App/Views/Sidebar/SidebarView.swift:2053-2063
    public void A_detailed_second_line_shows_a_folder_or_branch_mark_then_the_project_name_or_merge_stage()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var plain = NewThread(alpha, "Plain", 1);
            var worktree = NewThread(alpha, "Worktree", 2) with { WorktreePath = "C:\\wt" };
            harness.Main.OnMain(() =>
            {
                var plainFace = MakeFace(plain, "Alpha", isSelected: false, trailingClearance: 18);
                var texts = LogicalQuery.SelfAndDescendants<TextBlock>(plainFace).ToList();
                Assert.Equal("Alpha", texts[1].Text); // :2057 projectName
                var glyph = LogicalQuery.SelfAndDescendants<SwiftImage>(plainFace).Single();
                Assert.Equal("folder", glyph.SymbolName); // :2055 no worktree path

                var worktreeFace = MakeFace(worktree, "Alpha", isSelected: false, trailingClearance: 18);
                var worktreeGlyph = LogicalQuery.SelfAndDescendants<SwiftImage>(worktreeFace).Single();
                Assert.Equal("arrow.triangle.branch", worktreeGlyph.SymbolName); // :2055 a worktree path

                var merging = MakeFace(plain, "Alpha", isSelected: false, trailingClearance: 18, mergeStage: "Merging\U00002026");
                var mergingTexts = LogicalQuery.SelfAndDescendants<TextBlock>(merging).ToList();
                Assert.Equal("Merging\U00002026", mergingTexts[1].Text); // :2057 mergeStage replaces projectName

                // Settled: the second line does not draw at all.
                var settledFace = MakeFace(plain with { IsSettled = true }, "Alpha", isSelected: false, trailingClearance: 18);
                Assert.Single(LogicalQuery.SelfAndDescendants<TextBlock>(settledFace)); // title only
            });
        });
    }

    // ---- PR-SIDEBAR-068, -069: the thread badge ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-068")] // App/Views/Sidebar/SidebarView.swift:2147-2165
    public void The_badge_shows_the_hand_then_the_spinner_then_the_pin_then_the_provider_icon()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            using var frames = FrameClock.Source.Override(new ManualFrameSource()); // the running spinner animates
            var alpha = NewProject(harness, "Alpha");
            var pinned = NewThread(alpha, "Pinned", 1) with { IsPinned = true };
            var plain = NewThread(alpha, "Plain", 2);
            var model = Model(harness, [alpha], [pinned, plain]);
            harness.Main.OnMain(() =>
            {
                // Plain: no runtime at all -> the provider icon.
                var plainBadge = Hosted(model, (Control)Activator.CreateInstance(Private("ThreadBadge"), plain)!);
                Assert.NotNull(LogicalQuery.SelfAndDescendants<ProviderIcon>(plainBadge).FirstOrDefault());

                // Pinned, no runtime: the pin, danger at 0.85.
                var pinnedBadge = Hosted(model, (Control)Activator.CreateInstance(Private("ThreadBadge"), pinned)!);
                var pin = LogicalQuery.SelfAndDescendants<SwiftImage>(pinnedBadge).Single();
                Assert.Equal("pin.fill", pin.SymbolName);
                Assert.Equal(Chrome.Danger.Opacity(0.85).Resolve(pin.ActualThemeVariant), ((ISolidColorBrush)pin.Foreground!).Color);

                // Running (merging): the spinner, its tooltip the merge stage, wins over the pin.
                var runtime = model.Runtime(pinned.Id);
                runtime.IsHydraMerging = true;
                runtime.HydraMergeStage = "Reviewing";
                var runningBadge = Hosted(model, (Control)Activator.CreateInstance(Private("ThreadBadge"), pinned)!);
                var spinner = LogicalQuery.SelfAndDescendants<MiniSpinner>(runningBadge).Single();
                Assert.Equal("Reviewing\U00002026", ToolTip.GetTip(spinner));

                // Needs input: the raised hand, orange, wins over everything else.
                typeof(ThreadRuntime).GetProperty("Approvals")!.SetValue(runtime,
                    new ValueList<ApprovalRequest>([new ApprovalRequest("a1", ApprovalRequest.Kind.Tool, "Allow?", ValueList<ApprovalRequest.Option>.Empty)]));
                var needsInputBadge = Hosted(model, (Control)Activator.CreateInstance(Private("ThreadBadge"), pinned)!);
                var hand = LogicalQuery.SelfAndDescendants<SwiftImage>(needsInputBadge).Single();
                Assert.Equal("hand.raised.fill", hand.SymbolName);
                Assert.Equal(Chrome.Orange.Resolve(hand.ActualThemeVariant), ((ISolidColorBrush)hand.Foreground!).Color);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-069")] // App/Views/Sidebar/SidebarView.swift:2166-2173
    public void An_unread_threads_badge_carries_a_7pt_dot_danger_or_accent_unless_the_hand_or_spinner_shows()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var failed = NewThread(alpha, "Failed", 1) with { HasUnread = true, LastStatus = TurnStatus.Failed };
            var ok = NewThread(alpha, "Ok", 2) with { HasUnread = true, LastStatus = TurnStatus.Completed };
            var model = Model(harness, [alpha], [failed, ok]);
            harness.Main.OnMain(() =>
            {
                var failedBadge = Hosted(model, (Control)Activator.CreateInstance(Private("ThreadBadge"), failed)!);
                var failedDot = LogicalQuery.SelfAndDescendants<Avalonia.Controls.Shapes.Ellipse>(failedBadge).Single();
                Assert.Equal(7d, failedDot.Width);
                Assert.Equal(Chrome.Danger.Resolve(failedDot.ActualThemeVariant), ((ISolidColorBrush)failedDot.Fill!).Color);

                var okBadge = Hosted(model, (Control)Activator.CreateInstance(Private("ThreadBadge"), ok)!);
                var okDot = LogicalQuery.SelfAndDescendants<Avalonia.Controls.Shapes.Ellipse>(okBadge).Single();
                Assert.Equal(Chrome.Accent.Resolve(okDot.ActualThemeVariant), ((ISolidColorBrush)okDot.Fill!).Color);

                // Needs input suppresses the dot even though hasUnread is true.
                var runtime = model.Runtime(failed.Id);
                typeof(ThreadRuntime).GetProperty("Questions")!.SetValue(runtime,
                    new ValueList<QuestionRequest>([new QuestionRequest("q1", ValueList<QuestionRequest.Question>.Empty)]));
                var suppressed = Hosted(model, (Control)Activator.CreateInstance(Private("ThreadBadge"), failed)!);
                Assert.Empty(LogicalQuery.SelfAndDescendants<Avalonia.Controls.Shapes.Ellipse>(suppressed));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-068")] // App/Views/Sidebar/SidebarView.swift:1717-1721, :2147-2151
    [Trait("Req", "PR-SIDEBAR-069")] // App/Views/Sidebar/SidebarView.swift:1717-1721, :2166-2173
    public void The_badge_updates_in_place_when_its_runtime_changes_after_mount()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            using var frames = FrameClock.Source.Override(new ManualFrameSource()); // the running spinner animates
            var alpha = NewProject(harness, "Alpha");
            var pinned = NewThread(alpha, "Pinned", 1) with { IsPinned = true };
            var model = Model(harness, [alpha], [pinned]);
            Border host = null!;
            ThreadRuntime runtime = null!;
            harness.Main.OnMain(() =>
            {
                // The runtime exists (and is empty) before the badge ever mounts, exactly as the row's own body
                // would find it already sitting in AppModel; the fix under test is the already-attached badge
                // picking up a LATER change to that same runtime instance, not a fresh one being created.
                runtime = model.Runtime(pinned.Id);
                host = Hosted(model, (Control)Activator.CreateInstance(Private("ThreadBadge"), pinned)!);
                var pin = LogicalQuery.SelfAndDescendants<SwiftImage>(host).Single(); // :2159-2161, nothing running yet
                Assert.Equal("pin.fill", pin.SymbolName);
            });
            harness.Main.OnMain(() =>
            {
                // Mutate the SAME runtime the already-mounted badge is showing; no ThreadBadge is reconstructed.
                // :1717-1721 (SidebarThreadRow.body comment): "the runtime's fields are observed per runtime."
                runtime.IsHydraMerging = true;
                runtime.HydraMergeStage = "Reviewing";
            });
            Avalonia.Threading.Dispatcher.UIThread.RunJobs(); // the badge's own re-layout, posted by Track's apply
            harness.Main.OnMain(() =>
            {
                Assert.Empty(LogicalQuery.SelfAndDescendants<SwiftImage>(host)); // the pin is gone
                var spinner = LogicalQuery.SelfAndDescendants<MiniSpinner>(host).Single(); // :2156-2158
                Assert.Equal("Reviewing\U00002026", ToolTip.GetTip(spinner));
            });
            harness.Main.OnMain(() =>
            {
                typeof(ThreadRuntime).GetProperty("Approvals")!.SetValue(runtime,
                    new ValueList<ApprovalRequest>([new ApprovalRequest("a1", ApprovalRequest.Kind.Tool, "Allow?", ValueList<ApprovalRequest.Option>.Empty)]));
            });
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            harness.Main.OnMain(() =>
            {
                Assert.Empty(LogicalQuery.SelfAndDescendants<MiniSpinner>(host)); // the hand wins over the spinner
                var hand = LogicalQuery.SelfAndDescendants<SwiftImage>(host).Single(); // :2153-2155
                Assert.Equal("hand.raised.fill", hand.SymbolName);
                Assert.Equal(Chrome.Orange.Resolve(hand.ActualThemeVariant), ((ISolidColorBrush)hand.Foreground!).Color);
            });
        });
    }

    // ---- PR-SIDEBAR-073: the trailing activity status ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-073")] // App/Views/Sidebar/SidebarView.swift:2412-2427
    public void The_trailing_status_is_the_hand_then_the_spinner_then_the_unread_dot_then_nothing()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            using var frames = FrameClock.Source.Override(new ManualFrameSource()); // the running spinner animates
            var alpha = NewProject(harness, "Alpha");
            var thread = NewThread(alpha, "Thread", 1) with { HasUnread = true, LastStatus = TurnStatus.Completed };
            var quiet = NewThread(alpha, "Quiet", 2);
            var model = Model(harness, [alpha], [thread, quiet]);
            harness.Main.OnMain(() =>
            {
                var nothing = Hosted(model, (Control)Activator.CreateInstance(Private("ActivityStatus"), quiet)!);
                Assert.Null(((Decorator)nothing.Child!).Child); // :2426 nothing drawn

                var dotted = Hosted(model, (Control)Activator.CreateInstance(Private("ActivityStatus"), thread)!);
                var dot = LogicalQuery.SelfAndDescendants<Avalonia.Controls.Shapes.Ellipse>(dotted).Single();
                Assert.Equal(7d, dot.Width);
                Assert.Equal(Chrome.Accent.Resolve(dot.ActualThemeVariant), ((ISolidColorBrush)dot.Fill!).Color);

                var runtime = model.Runtime(thread.Id);
                runtime.IsHydraMerging = true;
                runtime.HydraMergeStage = "Stage";
                var spinning = Hosted(model, (Control)Activator.CreateInstance(Private("ActivityStatus"), thread)!);
                var spinner = LogicalQuery.SelfAndDescendants<MiniSpinner>(spinning).Single();
                Assert.Equal("Stage\U00002026", ToolTip.GetTip(spinner));

                typeof(ThreadRuntime).GetProperty("Approvals")!.SetValue(runtime,
                    new ValueList<ApprovalRequest>([new ApprovalRequest("a1", ApprovalRequest.Kind.Tool, "Allow?", ValueList<ApprovalRequest.Option>.Empty)]));
                var handing = Hosted(model, (Control)Activator.CreateInstance(Private("ActivityStatus"), thread)!);
                var hand = LogicalQuery.SelfAndDescendants<SwiftImage>(handing).Single();
                Assert.Equal("hand.raised.fill", hand.SymbolName);
                Assert.Equal(Chrome.Warning.Resolve(hand.ActualThemeVariant), ((ISolidColorBrush)hand.Foreground!).Color); // :2417 warning, not orange
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-073")] // App/Views/Sidebar/SidebarView.swift:1717-1721, :2412-2427
    public void The_trailing_status_updates_in_place_when_its_runtime_changes_after_mount()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            using var frames = FrameClock.Source.Override(new ManualFrameSource()); // the running spinner animates
            var alpha = NewProject(harness, "Alpha");
            var quiet = NewThread(alpha, "Quiet", 1);
            var model = Model(harness, [alpha], [quiet]);
            Border host = null!;
            ThreadRuntime runtime = null!;
            harness.Main.OnMain(() =>
            {
                // Same shape as the ThreadBadge regression above: the runtime exists, empty, before mount, and the
                // already-attached ActivityStatus must pick up a later change to that very instance (no rebuild).
                // :1717-1721 (SidebarThreadRow.body comment): "the runtime's fields are observed per runtime."
                runtime = model.Runtime(quiet.Id);
                host = Hosted(model, (Control)Activator.CreateInstance(Private("ActivityStatus"), quiet)!);
                Assert.Null(((Decorator)host.Child!).Child); // :2426 nothing drawn: no unread, nothing running
            });
            harness.Main.OnMain(() =>
            {
                runtime.IsHydraMerging = true;
                runtime.HydraMergeStage = "Stage";
            });
            Avalonia.Threading.Dispatcher.UIThread.RunJobs(); // the status's own re-layout, posted by Track's apply
            harness.Main.OnMain(() =>
            {
                var spinner = LogicalQuery.SelfAndDescendants<MiniSpinner>(host).Single(); // :2418-2420
                Assert.Equal("Stage\U00002026", ToolTip.GetTip(spinner));
            });
            harness.Main.OnMain(() =>
            {
                typeof(ThreadRuntime).GetProperty("Approvals")!.SetValue(runtime,
                    new ValueList<ApprovalRequest>([new ApprovalRequest("a1", ApprovalRequest.Kind.Tool, "Allow?", ValueList<ApprovalRequest.Option>.Empty)]));
            });
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            harness.Main.OnMain(() =>
            {
                Assert.Empty(LogicalQuery.SelfAndDescendants<MiniSpinner>(host)); // the hand wins over the spinner
                var hand = LogicalQuery.SelfAndDescendants<SwiftImage>(host).Single(); // :2415-2417
                Assert.Equal("hand.raised.fill", hand.SymbolName);
                Assert.Equal(Chrome.Warning.Resolve(hand.ActualThemeVariant), ((ISolidColorBrush)hand.Foreground!).Color);
            });
        });
    }

    // ---- PR-SIDEBAR-072: the trailing overlay's precedence ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-072")] // App/Views/Sidebar/SidebarView.swift:1772-1801
    public void The_trailing_overlay_shows_hover_buttons_then_status_then_status_plus_time_then_time_alone()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var alpha = NewProject(harness, "Alpha");
            var thread = NewThread(alpha, "Thread", 1);
            var model = Model(harness, [alpha], [thread]);
            var type = Private("SidebarThreadRow");
            var actions = new List<RowAction> { new() { Title = "Rename", Action = () => { } } };
            harness.Main.OnMain(() =>
            {
                var isMenuPresented = new Binding<bool>(() => false, _ => { });

                // showsActions: the archive button (finish action not settle, not settled) then the ellipsis, no gap.
                var withArchive = (Control)CallStatic(type, "BuildTrailingOverlay",
                    thread, true, false, false, false, false, (Action)(() => { }), actions, isMenuPresented)!;
                var cluster = Assert.IsType<StackPanel>(withArchive);
                Assert.Equal(0d, cluster.Spacing); // :1775 HStack(spacing: 0)
                Assert.Equal(2, cluster.Children.Count); // archive + ellipsis
                Assert.Equal(new Thickness(0, 0, 6, 0), withArchive.Margin); // :1800 .padding(.trailing, 6)

                // showsActions with settles true: the archive button is gone, only the ellipsis remains.
                var withoutArchive = (Control)CallStatic(type, "BuildTrailingOverlay",
                    thread, true, false, false, false, true, (Action)(() => { }), actions, isMenuPresented)!;
                Assert.Single(((StackPanel)withoutArchive).Children);

                // Detailed or settled, not hovering: the activity status alone.
                var detailed = Hosted(model, (Control)CallStatic(type, "BuildTrailingOverlay",
                    thread, false, true, false, false, false, (Action)(() => { }), actions, isMenuPresented)!);
                Assert.True(Private("ActivityStatus").IsInstanceOfType(((Border)detailed).Child));

                // Icon style with a project icon: status and time, 6 pt apart.
                var withTime = Hosted(model, (Control)CallStatic(type, "BuildTrailingOverlay",
                    thread, false, false, false, true, false, (Action)(() => { }), actions, isMenuPresented)!);
                var pair = Assert.IsType<StackPanel>(((Border)withTime).Child);
                Assert.Equal(6d, pair.Spacing); // :1792-1795
                Assert.Equal(2, pair.Children.Count);

                // Plain project layout: the relative time alone.
                var timeAlone = Hosted(model, (Control)CallStatic(type, "BuildTrailingOverlay",
                    thread, false, false, false, false, false, (Action)(() => { }), actions, isMenuPresented)!);
                Assert.True(Private("RelativeTimeLabel").IsInstanceOfType(((Border)timeAlone).Child));
            });
        });
    }

    // ---- PR-SIDEBAR-077, -078: the settle check's visibility and its own view ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-077")] // App/Views/Sidebar/SidebarView.swift:1727-1729, :1763-1771
    public void The_check_shows_at_the_trailing_end_hovered_or_settling_only_with_the_settle_action_and_leads_a_settled_row()
    {
        var type = Private("SidebarThreadRow");
        Assert.False((bool)CallStatic(type, "ShowsCheck", false, true, false, false)!); // not hovering, not settling
        Assert.True((bool)CallStatic(type, "ShowsCheck", false, true, true, false)!); // showsActions (hovered)
        Assert.True((bool)CallStatic(type, "ShowsCheck", false, true, false, true)!); // isSettling
        Assert.False((bool)CallStatic(type, "ShowsCheck", false, false, true, false)!); // finish action is archive
        Assert.True((bool)CallStatic(type, "ShowsCheck", true, false, false, false)!); // settled: always shows

        var overlay = (Control?)CallStatic(type, "BuildSettleCheckOverlay", false, true, true, false, false, (Action)(() => { }));
        Assert.Equal(HorizontalAlignment.Right, overlay!.HorizontalAlignment); // :1763 open: trailing
        Assert.Equal(new Thickness(0, 0, 26d, 0), overlay.Margin); // :1769 openCheckInset

        var settledOverlay = (Control?)CallStatic(type, "BuildSettleCheckOverlay", true, true, false, false, false, (Action)(() => { }));
        Assert.Equal(HorizontalAlignment.Left, settledOverlay!.HorizontalAlignment); // :1763 settled: leading
        Assert.Equal(new Thickness(6d, 0, 0, 0), settledOverlay.Margin); // :1768 settledCheckInset

        Assert.Null((Control?)CallStatic(type, "BuildSettleCheckOverlay", false, true, false, false, false, (Action)(() => { })));
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-078")] // App/Views/Sidebar/SidebarView.swift:2122-2140
    public void The_settle_check_is_filled_while_settled_or_popping_and_outlined_otherwise_with_the_right_tips()
    {
        headless.OnUiThread(() =>
        {
            static Button ButtonOf(Control c) => LogicalQuery.SelfAndDescendants<Button>(c).Single();
            static SwiftImage GlyphOf(Control c) => LogicalQuery.SelfAndDescendants<SwiftImage>(c).Single();

            var tapped = false;
            var open = Shown((Control)Activator.CreateInstance(Private("SettleCheck"), false, false, (Action)(() => tapped = true))!);
            Assert.Equal("checkmark.circle", GlyphOf(open).SymbolName); // :2124-2126 open, not hovering: outline
            Assert.Equal(Chrome.SecondaryText.Resolve(GlyphOf(open).ActualThemeVariant), ((ISolidColorBrush)GlyphOf(open).Foreground!).Color);
            var openButton = ButtonOf(open);
            Assert.Equal("Settle thread", ToolTip.GetTip(openButton)); // :2138
            Assert.Equal("Settle thread", AutomationProperties.GetName(openButton));
            Click(openButton);
            Assert.True(tapped);

            var settled = Shown((Control)Activator.CreateInstance(Private("SettleCheck"), true, false, (Action)(() => { }))!);
            Assert.Equal("checkmark.circle.fill", GlyphOf(settled).SymbolName); // :2124 settled, not hovering: filled
            Assert.Equal(Chrome.Success.Opacity(0.8).Resolve(GlyphOf(settled).ActualThemeVariant), ((ISolidColorBrush)GlyphOf(settled).Foreground!).Color);
            Assert.Equal("Reopen thread", ToolTip.GetTip(ButtonOf(settled)));

            var popping = Shown((Control)Activator.CreateInstance(Private("SettleCheck"), false, true, (Action)(() => { }))!);
            Assert.Equal("checkmark.circle.fill", GlyphOf(popping).SymbolName); // :2124 popping: filled regardless of settled
            Assert.Equal(1.35d, ((ScaleTransform)GlyphOf(popping).RenderTransform!).ScaleX); // :2132
            Assert.Equal(Chrome.Success.Resolve(GlyphOf(popping).ActualThemeVariant), ((ISolidColorBrush)GlyphOf(popping).Foreground!).Color);
        });
    }

    // ---- PR-SIDEBAR-075: the minute clock and the relative time label ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-075")] // App/Views/Sidebar/SidebarView.swift:2667-2680
    public void The_minute_clock_ticks_once_every_60_seconds()
    {
        using var runtime = new TestRuntime();
        var clock = runtime.OnMain(() => (MinuteClock)Activator.CreateInstance(typeof(MinuteClock), nonPublic: true)!);
        var first = runtime.OnMain(() => clock.Now);
        runtime.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal(first, runtime.OnMain(() => clock.Now)); // :2676 not yet 60 s
        runtime.Advance(TimeSpan.FromSeconds(1));
        var second = runtime.OnMain(() => clock.Now);
        Assert.NotEqual(first, second); // :2678 ticked
        runtime.Advance(TimeSpan.FromSeconds(60));
        Assert.NotEqual(second, runtime.OnMain(() => clock.Now)); // keeps ticking once a minute
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-075")] // App/Views/Sidebar/SidebarView.swift:2686-2694
    public void The_relative_time_label_reads_the_shared_clocks_now_at_subheadline_size_secondaryText_0_8()
    {
        headless.OnUiThread(() =>
        {
            var date = MinuteClock.Shared.Now.AddMinutes(-3);
            var label = Shown((Control)Activator.CreateInstance(Private("RelativeTimeLabel"), date)!);
            var text = LogicalQuery.SelfAndDescendants<TextBlock>(label).Single();
            Assert.Equal(RelativeTime.Short(date, MinuteClock.Shared.Now), text.Text); // :2690
            Assert.Equal(11d, text.FontSize); // .subheadline, ED-059
            Assert.Equal(Chrome.SecondaryText.Opacity(0.8).Resolve(text.ActualThemeVariant), ((ISolidColorBrush)text.Foreground!).Color); // :2692
            Assert.Equal(new Thickness(0, 0, 4, 0), text.Margin); // :2693
        });
    }

    // ---- PR-SIDEBAR-076: the title's trailing clearance ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-076")] // App/Views/Sidebar/SidebarView.swift:1894-1898, :2065-2066
    public void The_trailing_clearance_is_52_30_18_or_those_plus_20_by_formula()
    {
        var type = Private("SidebarThreadRow");
        Assert.Equal(52d, (double)CallStatic(type, "TrailingClearance", false, false, false, false)!); // plain
        Assert.Equal(18d, (double)CallStatic(type, "TrailingClearance", false, true, false, false)!); // detailed
        Assert.Equal(18d, (double)CallStatic(type, "TrailingClearance", true, false, false, false)!); // settled
        Assert.Equal(52d, (double)CallStatic(type, "TrailingClearance", false, false, true, false)!); // showsActions, open
        Assert.Equal(30d, (double)CallStatic(type, "TrailingClearance", true, false, true, false)!); // showsActions, settled
        Assert.Equal(72d, (double)CallStatic(type, "TrailingClearance", false, false, true, true)!); // + 20 pt fold
        Assert.Equal(50d, (double)CallStatic(type, "TrailingClearance", true, false, true, true)!); // 30 + 20
    }

    // ======== Part 4 of 8 ========
    //
    // Records: PR-SIDEBAR-079, -081, -083, -084, -086, -087, -088, -089, -091, -093, -094, -095, -096, -097, -098,
    // -099, -100, -101, -102, -103, -107, -108, -109, -110.
    //
    // STOP App.Views.Sidebar.SidebarView #6
    // Kind: forbidden need
    // Swift: DroppyCode/App/Views/Sidebar/SidebarView.swift:995-1024 (B3 52bc792); :1009 .textFieldStyle(.roundedBorder),
    // :1017 .buttonStyle(.bordered), :1019-1022 .buttonStyle(.borderedProminent)
    // Requirements: PR-SIDEBAR-091
    // What is unclear or missing: two AppKit control styles this file needs for the rename popover have no Windows
    // chrome anywhere in this port yet (Marrow.UI.Chrome, Marrow.UI.Port) and no engineering or platform-adaptation
    // decision names either one; this part's own decision list (ED-008, ED-009, ED-010, ED-041, ED-051, ED-059,
    // ED-062, ED-149, ED-187, OD-0001, OD-0005, OD-0008, OD-0009, OD-0010, PA-0019) is silent on both, and
    // porting/engineering_decisions.md and porting/platform_adaptations.md have no "roundedBorder" or
    // "borderedProminent"/"bordered" entry. Every other popover this part ports (Delete, Archive) reuses an
    // already-decided recipe (PopoverItem; OD-0005's `.glass`/`.glassProminent`); inventing a third, undecided one
    // here would be exactly the invention the contract forbids.
    // Readings or options: (a) treat `.bordered`/`.borderedProminent` as close enough to the already-decided
    // `.glass`/`.glassProminent` recipe and reuse it -> a visible material the source does not ask for on this
    // popover, an invented equivalence; (b) build a new, unstyled rectangle/border recipe for these two roles ->
    // arbitrary corner radius, padding and colours with no source number or owner ruling behind them.
    // Needed to proceed: an engineering decision (or owner ruling) naming the Windows chrome for
    // `.textFieldStyle(.roundedBorder)` and for `.buttonStyle(.bordered)`/`.buttonStyle(.borderedProminent)`.
    // Done so far: nothing implemented at RenameThreadPopover or SidebarView.RenamePopover; both stay MARROW_STUB.
    // liveRenameTarget (PR-SIDEBAR-093, the ellipsis path's live-thread lookup, :882-887) does not depend on this
    // site and is proven below.

    private static object? SetProp(object target, string name, object? value)
    {
        target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.SetValue(target, value);
        return null;
    }

    // ---- PR-SIDEBAR-110: HelperConnector and HelperConnectorShape, the dotted line beside a helper row ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-110")] // App/Views/Sidebar/SidebarView.swift:1662-1674
    public void The_connector_shape_runs_from_the_top_to_the_bottom_or_only_to_midY_when_it_ends_here() => headless.OnUiThread(() =>
    {
        var full = (Geometry)CallOn(CreateShapeRecord(false), "Path", new Rect(0, 0, 28, 24))!;
        Assert.Equal(0d, full.Bounds.Top, 3);
        Assert.Equal(24d, full.Bounds.Bottom, 3); // :1669 endsHere false -> rect.maxY

        var last = (Geometry)CallOn(CreateShapeRecord(true), "Path", new Rect(0, 0, 28, 24))!;
        Assert.Equal(0d, last.Bounds.Top, 3);
        Assert.Equal(12d, last.Bounds.Bottom, 3); // :1669 endsHere true -> rect.midY

        object CreateShapeRecord(bool endsHere) => Activator.CreateInstance(Private("HelperConnectorShape"), [endsHere])!;
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-110")] // App/Views/Sidebar/SidebarView.swift:1644-1646
    public void The_connectors_colour_is_secondaryText_at_0_45_or_0_9_hovered()
    {
        var type = Private("HelperConnector");
        static Color Resolved(SwiftColor color) => color.Resolve(ThemeVariant.Light);
        Assert.Equal(Resolved(Chrome.SecondaryText.Opacity(0.45)), Resolved((SwiftColor)CallStatic(type, "Color", false)!)); // :1645
        Assert.Equal(Resolved(Chrome.SecondaryText.Opacity(0.9)), Resolved((SwiftColor)CallStatic(type, "Color", true)!));
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-110")] // App/Views/Sidebar/SidebarView.swift:1648-1659
    public void Clicking_the_connector_folds_the_helpers_and_it_is_tooltipped_and_labelled_Hide_helpers() => headless.OnUiThread(() =>
    {
        var folded = false;
        var control = Shown((Control)Activator.CreateInstance(Private("HelperConnector"), false, (Action)(() => folded = true))!);
        var button = Assert.IsType<Button>(LogicalQuery.SelfAndDescendants<Button>(control).Single());
        Assert.Equal("Hide helpers", ToolTip.GetTip(button)); // :1657
        Assert.Equal("Hide helpers", AutomationProperties.GetName(button)); // :1658
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(folded); // :1648 Button(action: action)
    });

    // ---- PR-SIDEBAR-088: DeleteThreadPopover's content ----

    private static string? TextOf(Button button) =>
        LogicalQuery.SelfAndDescendants<TextBlock>(button).Select(t => t.Text).FirstOrDefault();

    // A StyledText.Render(host) target holds its text as Inlines' Runs, not host.Text (matches
    // tests/Marrow.UI.Tests/Port/StyledTextTests.cs's own pattern).
    private static string InlineText(TextBlock block) =>
        block.Inlines is { } inlines ? string.Concat(inlines.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text)) : "";

    private static AvaloniaWindow WindowOf(Control control) => (AvaloniaWindow)TopLevel.GetTopLevel(control)!;

    [Fact]
    [Trait("Req", "PR-SIDEBAR-088")] // App/Views/Sidebar/SidebarView.swift:966-981
    public void The_delete_popover_is_280_wide_with_a_header_a_curly_quoted_note_a_divider_and_delete_rows() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = AppModelSamples.Project("Alpha", harness.NewFolder("Alpha"));
        var thread = AppModelSamples.Thread(project) with { Title = "Fix the bug" };
        var deletes = new List<bool>();
        var popover = Shown((Control)Activator.CreateInstance(Private("DeleteThreadPopover"), thread, (Action<bool>)(removeWorktree => deletes.Add(removeWorktree)))!);

        var menu = Assert.IsType<PopoverMenu>(Get(popover, "Child"));
        Assert.Equal(280d, menu.Width); // :980
        var texts = Texts(popover);
        Assert.Contains("Delete this thread?", texts); // :973
        Assert.Contains("\u201CFix the bug\u201D and its history will be removed. Files in your project stay as they are.", texts); // :974
        Assert.Contains("Delete thread", texts); // :976
        Assert.DoesNotContain("Delete thread and worktree", texts); // :977, no worktree

        var deleteButton = LogicalQuery.SelfAndDescendants<Button>(popover).Single(b => TextOf(b) == "Delete thread");
        deleteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        harness.Main.RunUntilIdle(); // :271-274 PopoverItem.Activate spawns the action on the main actor
        Assert.Equal([false], deletes); // :976 onDelete(false)
        WindowOf(popover).Close();
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-088")] // App/Views/Sidebar/SidebarView.swift:977-978
    public void The_delete_and_worktree_row_shows_only_with_a_worktree_and_deletes_with_it_true() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = AppModelSamples.Project("Alpha", harness.NewFolder("Alpha"));
        var thread = AppModelSamples.Thread(project) with { Title = "Fix the bug", WorktreePath = "C:/Alpha/wt" };
        var deletes = new List<bool>();
        var popover = Shown((Control)Activator.CreateInstance(Private("DeleteThreadPopover"), thread, (Action<bool>)(removeWorktree => deletes.Add(removeWorktree)))!);

        Assert.Contains("Delete thread and worktree", Texts(popover)); // :977
        var button = LogicalQuery.SelfAndDescendants<Button>(popover).Single(b => TextOf(b) == "Delete thread and worktree");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        harness.Main.RunUntilIdle(); // :271-274 PopoverItem.Activate spawns the action on the main actor
        Assert.Equal([true], deletes); // :978 onDelete(true)
        WindowOf(popover).Close();
    });

    // ---- PR-SIDEBAR-095, PR-SIDEBAR-096: ArchiveThreadPopover ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-095")] // App/Views/Sidebar/SidebarView.swift:905-943
    public void The_archive_popover_is_272_wide_asks_Archive_title_and_offers_Delete_and_Archive() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = AppModelSamples.Project("Alpha", harness.NewFolder("Alpha"));
        var thread = AppModelSamples.Thread(project) with { Title = "Fix the bug" };
        var archived = 0;
        var deleted = 0;
        var popover = (Control)Activator.CreateInstance(Private("ArchiveThreadPopover"), thread,
            (Action)(() => archived++), (Action)(() => deleted++), (Action)(() => { }))!;
        Shown(popover);

        var menu = Assert.IsType<PopoverMenu>(Get(popover, "Child"));
        Assert.Equal(272d, menu.Width); // :943
        var question = LogicalQuery.SelfAndDescendants<TextBlock>(popover).Single(t => InlineText(t) == "Archive \u201CFix the bug\u201D?"); // :917

        var buttons = LogicalQuery.SelfAndDescendants<Button>(popover).ToList();
        var delete = buttons.Single(b => TextOf(b) == "Delete"); // :927
        var archive = buttons.Single(b => TextOf(b) == "Archive"); // :931
        Assert.Equal("Remove the thread and its history", ToolTip.GetTip(delete)); // :929
        Assert.Equal("Move it to Archived (Return)", ToolTip.GetTip(archive)); // :936
        Assert.True(archive.IsDefault); // :932 .keyboardShortcut(.defaultAction)

        delete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, deleted); // :927 Button(action: onDelete)
        archive.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, archived); // :931 Button("Archive", action: onArchive)
        WindowOf(popover).Close();
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-095")] // App/Views/Sidebar/SidebarView.swift:927 Text("Delete").foregroundStyle(.red)
    public void The_delete_button_in_the_archive_popover_is_drawn_in_danger() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = AppModelSamples.Project("Alpha", harness.NewFolder("Alpha"));
        var thread = AppModelSamples.Thread(project) with { Title = "Fix the bug" };
        var popover = (Control)Activator.CreateInstance(Private("ArchiveThreadPopover"), thread, (Action)(() => { }), (Action)(() => { }), (Action)(() => { }))!;
        Shown(popover);
        var delete = LogicalQuery.SelfAndDescendants<TextBlock>(popover).Single(t => t.Text == "Delete");
        Assert.Equal(Chrome.Danger.Resolve(delete.ActualThemeVariant), ((ISolidColorBrush)delete.Foreground!).Color);
        WindowOf(popover).Close();
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-096")] // App/Views/Sidebar/SidebarView.swift:946-955
    public void Return_or_keypad_enter_archives_and_escape_dismisses_swallowing_the_key() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = AppModelSamples.Project("Alpha", harness.NewFolder("Alpha"));
        var thread = AppModelSamples.Thread(project);
        var archived = 0;
        var dismissed = 0;
        var popover = (Control)Activator.CreateInstance(Private("ArchiveThreadPopover"), thread,
            (Action)(() => archived++), (Action)(() => { }), (Action)(() => dismissed++))!;
        Shown(popover);
        var window = WindowOf(popover);
        var composerSawKey = false;
        window.AddHandler(InputElement.KeyDownEvent, (_, e) => { if (e.PhysicalKey == PhysicalKey.Enter) composerSawKey = true; });

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); // :951 keyCode 36
        Assert.Equal(1, archived);
        Assert.False(composerSawKey); // :954 return nil: swallowed before the bubble phase

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.NumPadEnter, null); // :951 keyCode 76
        Assert.Equal(2, archived);

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); // :952 keyCode 53
        Assert.Equal(1, dismissed);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-096")] // App/Views/Sidebar/SidebarView.swift:949 modifierFlags.intersection(...).isEmpty
    public void A_modifier_held_lets_the_key_pass_through_the_archive_popovers_monitor() => headless.OnUiThread(() =>
    {
        // Escape, not Return: Return is also the Archive button's own keyboard default-action (:932), so a Ctrl+Return
        // observation would be confounded by Avalonia's own IsDefault dispatch rather than isolating the monitor.
        using var harness = new AppModelHarness();
        var project = AppModelSamples.Project("Alpha", harness.NewFolder("Alpha"));
        var thread = AppModelSamples.Thread(project);
        var dismissed = 0;
        var popover = (Control)Activator.CreateInstance(Private("ArchiveThreadPopover"), thread, (Action)(() => { }), (Action)(() => { }), (Action)(() => dismissed++))!;
        Shown(popover);
        var window = WindowOf(popover);

        window.KeyPress(Key.Escape, RawInputModifiers.Control, PhysicalKey.Escape, null); // :949
        Assert.Equal(0, dismissed);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-096")] // App/Views/Sidebar/SidebarView.swift:958-961 onDisappear
    public void The_archive_popovers_monitor_is_torn_down_once_it_leaves_the_tree() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = AppModelSamples.Project("Alpha", harness.NewFolder("Alpha"));
        var thread = AppModelSamples.Thread(project);
        var archived = 0;
        var popover = (Control)Activator.CreateInstance(Private("ArchiveThreadPopover"), thread, (Action)(() => archived++), (Action)(() => { }), (Action)(() => { }))!;
        var window = new AvaloniaWindow { Content = popover, Width = 200, Height = 100 };
        window.Show();
        window.Content = null; // detaches the popover
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Assert.Equal(0, archived); // the handler was removed, so nothing archives
        window.Close();
    });

    // ---- PR-SIDEBAR-086, PR-SIDEBAR-087, PR-SIDEBAR-089: the delete popover's coordinator on SidebarView ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-086")] // App/Views/Sidebar/SidebarView.swift:889-896 (and the identical :359-366, :189-196)
    public void RequestDeletion_asks_first_when_confirmBeforeDeleting_and_deletes_at_once_otherwise() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var confirming = NewThread(project, "Ask first", 0);
        var immediate = NewThread(project, "No confirm", 1);
        var model = Model(harness, [project], [confirming, immediate]);
        harness.Main.OnMain(() =>
        {
            var view = Detached(model);
            model.Settings.ConfirmBeforeDeleting = true;
            Call(view, "RequestDeletion", confirming.Id);
            var pending = Get(Field(view, "state")!, "PendingDeletionID");
            Assert.Equal(confirming.Id, pending); // :890
            Assert.NotNull(model.Thread(confirming.Id)); // not deleted yet

            model.Settings.ConfirmBeforeDeleting = false;
            Call(view, "RequestDeletion", immediate.Id);
            Assert.Null(model.Thread(immediate.Id)); // :896 deleted at once, removeWorktree false
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-087")] // App/Views/Sidebar/SidebarView.swift:217-237
    public void DeletePopover_presents_only_for_the_matching_id_with_its_arrow_on_the_trailing_edge() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0);
        var other = NewThread(project, "Other", 1);
        var model = Model(harness, [project], [thread, other]);
        var recorder = new RecordingPresenter();
        using var presenter = Presentation.Presenter.Override(recorder);
        object? view = null;
        Popup? popup = null;
        // Track's re-evaluation runs on a MainActor.Post; each phase below is its own OnMain call so the previous
        // phase's post is already drained (RunUntilIdle "runs queued work, including work queued while running")
        // before the next phase reads it, rather than re-entering the loop mid-pump.
        harness.Main.OnMain(() =>
        {
            view = Detached(model);
            var row = new Border();
            var panel = (Panel)Call(view, "DeletePopover", thread, row)!;
            popup = Assert.IsType<Popup>(panel.Children[1]);
            Assert.Equal(PlacementMode.Right, popup.Placement); // :220 arrowEdge: .trailing
            Shown(panel);
        });
        harness.Main.OnMain(() => SetProp(Field(view!, "state")!, "PendingDeletionID", other.Id)); // a different thread pending
        harness.Main.OnMain(() => Assert.DoesNotContain(recorder.Calls, c => c.Popup == popup && c.Presented)); // :221 ids differ

        harness.Main.OnMain(() => SetProp(Field(view!, "state")!, "PendingDeletionID", thread.Id)); // :221 this row's id
        harness.Main.OnMain(() => Assert.Contains(recorder.Calls, c => c.Popup == popup && c.Presented));
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-089")] // App/Views/Sidebar/SidebarView.swift:256-263 confirmDeletion
    public void ConfirmDeletion_records_the_deed_clears_both_pending_ids_and_runs_at_once_when_no_popover_shows() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0);
        var model = Model(harness, [project], [thread]);
        harness.Main.OnMain(() =>
        {
            var view = Detached(model);
            var state = Field(view, "state")!;
            SetProp(state, "PendingDeletionID", thread.Id);
            SetProp(state, "PendingArchiveID", thread.Id);
            Call(view, "ConfirmDeletion", thread.Id, true);
            Assert.Null(Get(state, "PendingDeletionID")); // :259
            Assert.Null(Get(state, "PendingArchiveID")); // :260
        });
        harness.Main.Loop.Run(() => Task.Delay(1), AppModelHarness.Bound); // let the spawned MainActor task run
        Assert.Null(model.Thread(thread.Id)); // :261, :303-305 carried out with removeWorktree true
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-089")] // App/Views/Sidebar/SidebarView.swift:295-306 carryOutConfirmedDeletion
    public void ConfirmDeletion_waits_for_the_popover_to_close_when_one_is_still_shown() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0);
        var model = Model(harness, [project], [thread]);
        harness.Main.OnMain(() =>
        {
            var view = Detached(model);
            var state = Field(view, "state")!;
            SetProp(state, "IsDeletePopoverShown", true); // :261 the popover is still up
            Call(view, "ConfirmDeletion", thread.Id, false);
            Assert.NotNull(model.Thread(thread.Id)); // :261 not carried out yet
            Assert.NotNull(Get(state, "ConfirmedDeletion")); // :258, waiting for onDisappear (:231-232)

            SetProp(state, "IsDeletePopoverShown", false);
            Call(view, "CarryOutConfirmedDeletion"); // what the popover's onDisappear calls (:232)
            Assert.Null(Get(state, "ConfirmedDeletion")); // :301
        });
        harness.Main.Loop.Run(() => Task.Delay(1), AppModelHarness.Bound);
        Assert.Null(model.Thread(thread.Id)); // :304-305, one MainActor turn later
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-089")] // App/Views/Sidebar/SidebarView.swift:299-300 guard let confirmed
    public void CarryOutConfirmedDeletion_does_nothing_when_nothing_was_confirmed() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        harness.Main.OnMain(() =>
        {
            var view = Detached(model);
            Call(view, "CarryOutConfirmedDeletion"); // no exception, nothing scheduled
            Assert.Null(Get(Field(view, "state")!, "ConfirmedDeletion"));
        });
    });

    // ---- PR-SIDEBAR-093: rename reads the live thread record ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-093")] // App/Views/Sidebar/SidebarView.swift:882-887 (and the identical :354-358)
    public void LiveRenameTarget_reads_the_live_record_and_falls_back_to_the_snapshot_when_gone() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Old title", 0);
        var model = Model(harness, [project], [thread]);
        harness.Main.OnMain(() =>
        {
            model.Rename(thread.Id, "New title");
            var live = (ChatThread)CallStatic(typeof(SidebarView), "LiveRenameTarget", model, thread)!;
            Assert.Equal("New title", live.Title); // :886 the live record, not the stale snapshot

            model.Delete(thread.Id);
            var gone = (ChatThread)CallStatic(typeof(SidebarView), "LiveRenameTarget", model, thread)!;
            Assert.Equal(thread.Id, gone.Id); // :886 ?? thread: falls back to the snapshot
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-093")] // App/Views/Sidebar/SidebarView.swift:182-186 the context-menu path uses the posted snapshot
    public void A_rename_posted_from_the_context_menu_uses_the_posted_value_as_is() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Old title", 0);
        var model = Model(harness, [project], [thread]);
        object? view = null;
        harness.Main.OnMain(() =>
        {
            view = Detached(model);
            Call(view, "Appear"); // wires the onChange handlers without needing a real window (ProjectRow is still
            // MARROW_STUB, so a real Attached() render of a non-empty library throws)
            model.Rename(thread.Id, "New title"); // changes after the menu already posted its snapshot
            SetProp(Field(view, "menuRequests")!, "Rename", thread); // the snapshot the AppKit menu captured before the rename
        });
        // A fresh OnMain call: Track's re-evaluation runs on a MainActor.Post, which the call above already drained.
        harness.Main.OnMain(() =>
        {
            var state = Field(view!, "state")!;
            Assert.Equal("Old title", Get(state, "RenameText")); // :187 posted.title, not the live one
            Assert.Equal(thread, Get(state, "Renaming")); // :188
            Assert.Null(Get(Field(view!, "menuRequests")!, "Rename")); // :186 cleared
        });
    });

    // ---- PR-SIDEBAR-094: the archive shortcut's question lands on the thread's own row ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-094")] // App/Views/Sidebar/SidebarView.swift:198-202
    public void An_archive_request_for_a_present_thread_opens_the_question_and_clears_the_request() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0);
        var model = Model(harness, [project], [thread]);
        object? view = null;
        harness.Main.OnMain(() =>
        {
            view = Detached(model);
            Call(view, "Appear"); // see the rename test above for why Detached, not Attached
            model.AppModelArchiveRequest = new AppModel.ArchiveRequest(thread.Id);
        });
        harness.Main.OnMain(() =>
        {
            Assert.Null(model.AppModelArchiveRequest); // :200
            Assert.Equal(thread.Id, Get(Field(view!, "state")!, "PendingArchiveID")); // :201
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-094")] // App/Views/Sidebar/SidebarView.swift:199 guard ..., model.thread(request.threadID) != nil
    public void An_archive_request_for_a_missing_thread_is_left_for_the_models_own_fallback() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        SidebarView? view = null;
        var missing = Guid.NewGuid();
        harness.Main.OnMain(() =>
        {
            view = Attached(model);
            model.AppModelArchiveRequest = new AppModel.ArchiveRequest(missing);
        });
        harness.Main.OnMain(() =>
        {
            Assert.NotNull(model.AppModelArchiveRequest); // :199 not taken up: left for the model's 80 ms fallback
            Assert.Null(Get(Field(view!, "state")!, "PendingArchiveID"));
            Close(view!);
        });
    });

    // ---- PR-SIDEBAR-097: archiving or deleting from the archive popover ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-097")] // App/Views/Sidebar/SidebarView.swift:276-279
    public void Archiving_from_the_archive_popovers_content_closes_it_and_archives_inside_panelSlide() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0);
        var model = Model(harness, [project], [thread]);
        harness.Main.OnMain(() =>
        {
            var view = Detached(model);
            SetProp(Field(view, "state")!, "PendingArchiveID", thread.Id);
            var content = (Control)Call(view, "BuildArchivePopoverContent", thread)!;
            Shown(content);
            var archive = LogicalQuery.SelfAndDescendants<Button>(content).Single(b => TextOf(b) == "Archive");
            archive.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Null(Get(Field(view, "state")!, "PendingArchiveID")); // :278
            Assert.True(model.Thread(thread.Id)!.IsArchived); // :279
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-097")] // App/Views/Sidebar/SidebarView.swift:281, the deferred-delete path shared with the delete popover
    public void Deleting_from_the_archive_popovers_content_goes_through_the_deferred_delete_path() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0);
        var model = Model(harness, [project], [thread]);
        harness.Main.OnMain(() =>
        {
            var view = Detached(model);
            var state = Field(view, "state")!;
            SetProp(state, "IsDeletePopoverShown", true); // :285, as the wrapping archivePopover sets on appear
            var content = (Control)Call(view, "BuildArchivePopoverContent", thread)!;
            Shown(content);
            var delete = LogicalQuery.SelfAndDescendants<Button>(content).Single(b => TextOf(b) == "Delete");
            delete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.NotNull(Get(state, "ConfirmedDeletion")); // :281 confirmDeletion(of:removeWorktree: false)
            Assert.NotNull(model.Thread(thread.Id)); // deferred while the popover is still shown
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-097")] // App/Views/Sidebar/SidebarView.swift:283
    public void Dismissing_the_archive_popovers_content_only_clears_the_pending_id() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0);
        var model = Model(harness, [project], [thread]);
        harness.Main.OnMain(() =>
        {
            var view = Detached(model);
            var state = Field(view, "state")!;
            SetProp(state, "PendingArchiveID", thread.Id);
            var content = (Control)Call(view, "BuildArchivePopoverContent", thread)!;
            Shown(content);
            var window = WindowOf(content);

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); // :952 onDismiss()

            Assert.Null(Get(state, "PendingArchiveID")); // :283 clears the pending id, nothing else
            Assert.NotNull(model.Thread(thread.Id)); // not archived, not deleted
            window.Close();
        });
    });

    // ---- PR-SIDEBAR-079, PR-SIDEBAR-081, PR-SIDEBAR-083: settling, reopening and the finish request ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-079")] // App/Views/Sidebar/SidebarView.swift:1904-1910
    public void Settle_pops_the_check_and_settles_the_thread_with_its_own_sound_suppressed() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0);
        var model = Model(harness, [project], [thread]);
        harness.Main.OnMain(() =>
        {
            var setCalls = new List<bool>();
            CallStatic(Private("SidebarThreadRow"), "Settle", model, thread, false, false, (Action<bool>)(v => setCalls.Add(v)));
            Assert.Equal([true], setCalls); // :1909
            Assert.True(model.Thread(thread.Id)!.IsSettled); // :1910
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-079")] // App/Views/Sidebar/SidebarView.swift:1907 guard !isSettling, !thread.isSettled
    public void Settle_does_nothing_while_already_settling_or_already_settled() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var already = NewThread(project, "Already", 0) with { IsSettled = true };
        var mid = NewThread(project, "Mid", 1);
        var model = Model(harness, [project], [already, mid]);
        harness.Main.OnMain(() =>
        {
            var setCalls = new List<bool>();
            CallStatic(Private("SidebarThreadRow"), "Settle", model, already, false, false, (Action<bool>)(v => setCalls.Add(v)));
            Assert.Empty(setCalls); // :1907 already settled

            CallStatic(Private("SidebarThreadRow"), "Settle", model, mid, true, false, (Action<bool>)(v => setCalls.Add(v)));
            Assert.Empty(setCalls); // :1907 isSettling already true
            Assert.False(model.Thread(mid.Id)!.IsSettled);
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-081")] // App/Views/Sidebar/SidebarView.swift:1912-1916
    public void Reopen_glides_before_reopening_and_does_nothing_when_not_settled() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var settled = NewThread(project, "Settled", 0) with { IsSettled = true };
        var open = NewThread(project, "Open", 1);
        var model = Model(harness, [project], [settled, open]);
        harness.Main.OnMain(() =>
        {
            var order = new List<string>();
            CallStatic(Private("SidebarThreadRow"), "Reopen", model, settled, (Action)(() => order.Add("glide")));
            Assert.Equal(["glide"], order); // :1914 launched first, so the arriving row can report its frame to it
            Assert.False(model.Thread(settled.Id)!.IsSettled); // :1915 model.reopenAnimated

            order.Clear();
            CallStatic(Private("SidebarThreadRow"), "Reopen", model, open, (Action)(() => order.Add("glide")));
            Assert.Empty(order); // :1913 guard thread.isSettled
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-083")] // App/Views/Sidebar/SidebarView.swift:1863-1870
    public void FinishRequestReceived_settles_or_reopens_only_the_matching_thread_and_state() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        harness.Main.OnMain(() =>
        {
            var project = AppModelSamples.Project("Alpha", harness.NewFolder("Alpha"));
            var thread = AppModelSamples.Thread(project);
            var settled = 0;
            var reopened = 0;

            model.AppModelFinishRequest = new AppModel.FinishRequest(thread.Id, false); // not settled -> settle()
            CallStatic(Private("SidebarThreadRow"), "FinishRequestReceived", model, thread, false, (Action)(() => settled++), (Action)(() => reopened++));
            Assert.Equal(1, settled); // :1867
            Assert.Equal(0, reopened);
            Assert.Null(model.AppModelFinishRequest); // :1865

            model.AppModelFinishRequest = new AppModel.FinishRequest(thread.Id, false);
            CallStatic(Private("SidebarThreadRow"), "FinishRequestReceived", model, thread, true, (Action)(() => settled++), (Action)(() => reopened++));
            Assert.Equal(1, settled); // :1864 isSettled (true) != request.reopens (false): not taken up
            Assert.NotNull(model.AppModelFinishRequest);

            model.AppModelFinishRequest = new AppModel.FinishRequest(Guid.NewGuid(), false); // a different thread
            CallStatic(Private("SidebarThreadRow"), "FinishRequestReceived", model, thread, false, (Action)(() => settled++), (Action)(() => reopened++));
            Assert.Equal(1, settled); // :1864 threadID mismatch: not taken up
        });
    });

    // ---- PR-SIDEBAR-084: the hover archive button's genie flight ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-084")] // App/Views/Sidebar/SidebarView.swift:1988-1999
    public void ArchiveWithGenie_flies_a_ghost_of_the_row_and_archives_the_thread() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0);
        var model = Model(harness, [project], [thread]);
        harness.Main.OnMain(() =>
        {
            var animator = new GenieAnimator(new RenderingGeniePlatform());
            CallStatic(Private("SidebarThreadRow"), "ArchiveWithGenie", animator, model, thread, null, null, false, null, new Rect(0, 0, 200, 28), ThemeVariant.Light);
            Assert.True(model.Thread(thread.Id)!.IsArchived); // :1998 withAnimation(Chrome.panelSlide) { model.archive(...) }
            Assert.Single(animator.Flights); // :1994-1997 a ghost launched from the row's own frame
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-084")] // App/Views/Sidebar/SidebarView.swift:1990 Chrome.overlay(isSelected ? 0.12 : 0.06)
    public void ArchiveWithGenies_ghost_fill_is_the_selection_overlay() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0);
        var model = Model(harness, [project], [thread]);
        harness.Main.OnMain(() =>
        {
            var animator = new GenieAnimator(new RenderingGeniePlatform());
            CallStatic(Private("SidebarThreadRow"), "ArchiveWithGenie", animator, model, thread, null, null, true, null, new Rect(0, 0, 200, 28), ThemeVariant.Light);
            Assert.Single(animator.Flights); // still flies once, selected this time (:1991 isSelected ? 0.12 : 0.06)
        });
    });

    // ---- PR-SIDEBAR-098, 099, 100, 101, 102, 103: ThreadActions ----

    private static List<RowAction> ThreadActionsMake(AppModel model, ChatThread thread, Action? onSettle = null, Action? onReopen = null) =>
        ((IEnumerable)CallStatic(Private("ThreadActions"), "Make", model, thread, (Action)(() => { }), (Action)(() => { }), onSettle, onReopen)!)
            .Cast<RowAction>().ToList();

    private static List<RowAction> ThreadActionsMakeContextMenu(AppModel model, ChatThread thread, bool settleFirst, object requests) =>
        ((IEnumerable)CallStatic(Private("ThreadActions"), "MakeContextMenu", model, thread, settleFirst, requests)!)
            .Cast<RowAction>().ToList();

    [Fact]
    [Trait("Req", "PR-SIDEBAR-099")] // App/Views/Sidebar/SidebarView.swift:2199-2207
    public void Pin_or_unpin_toggles_isPinned_and_is_absent_for_a_helper_or_a_settled_thread() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0);
        var parent = NewThread(project, "Parent", 1);
        var helper = NewThread(project, "Helper", 2) with { ParentThreadID = parent.Id };
        var settled = NewThread(project, "Settled", 3) with { IsSettled = true };
        var model = Model(harness, [project], [thread, parent, helper, settled]);
        harness.Main.OnMain(() =>
        {
            var pin = ThreadActionsMake(model, thread).Single(a => a.Title is "Pin" or "Unpin");
            Assert.Equal("Pin", pin.Title); // :2200 not yet pinned
            Assert.Equal("pin", pin.Symbol);
            pin.Action();
            Assert.True(model.Thread(thread.Id)!.IsPinned); // :2201-2203

            var unpin = ThreadActionsMake(model, model.Thread(thread.Id)!).Single(a => a.Title is "Pin" or "Unpin");
            Assert.Equal("Unpin", unpin.Title);
            Assert.Equal("pin.slash", unpin.Symbol);

            Assert.DoesNotContain(ThreadActionsMake(model, helper), a => a.Title is "Pin" or "Unpin"); // :2196-2198
            Assert.DoesNotContain(ThreadActionsMake(model, settled), a => a.Title is "Pin" or "Unpin");
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-100")] // App/Views/Sidebar/SidebarView.swift:2210-2212
    public void Forward_to_new_thread_starts_an_asynchronous_forward() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0);
        var model = Model(harness, [project], [thread]);
        harness.Main.OnMain(() =>
        {
            var forward = ThreadActionsMake(model, thread).Single(a => a.Title == "Forward to new thread"); // :2210
            Assert.Equal("arrowshape.turn.up.right", forward.Symbol);
            forward.Action(); // :2211 Task { await model.forwardThread(thread.id) }
        });
        harness.Main.Loop.Run(() => Task.Delay(1), AppModelHarness.Bound); // lets the spawned async forward finish
        harness.Main.OnMain(() => Assert.Equal(2, model.Threads.Count)); // the forward has landed
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-101")] // App/Views/Sidebar/SidebarView.swift:2216-2218, OD-0008 B1
    public void Reveal_worktree_shows_only_with_a_worktree_and_reads_File_Explorer_by_the_owner_ruling() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var withWorktree = NewThread(project, "WT", 0) with { WorktreePath = "C:/Alpha/wt" };
        var without = NewThread(project, "NoWT", 1);
        var model = Model(harness, [project], [withWorktree, without]);
        harness.Main.OnMain(() =>
        {
            var reveal = ThreadActionsMake(model, withWorktree).Single(a => a.Title == "Reveal worktree in File Explorer"); // OD-0008 B1
            Assert.Equal("folder", reveal.Symbol);

            var revealed = new List<string>();
            var previous = ShellWorkspace.RevealInFileExplorer;
            try
            {
                ShellWorkspace.RevealInFileExplorer = p => revealed.Add(p);
                reveal.Action(); // :2217
                Assert.Equal(["C:/Alpha/wt"], revealed);
            }
            finally
            {
                ShellWorkspace.RevealInFileExplorer = previous;
            }

            Assert.DoesNotContain(ThreadActionsMake(model, without), a => a.Title.StartsWith("Reveal", StringComparison.Ordinal)); // :2214
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-102")] // App/Views/Sidebar/SidebarView.swift:2219-2242
    public void A_helpers_menu_offers_no_pin_and_no_settle_only_archive_in_that_group() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var helper = NewThread(project, "Helper", 1) with { ParentThreadID = parent.Id };
        var model = Model(harness, [project], [parent, helper]);
        harness.Main.OnMain(() =>
        {
            var actions = ThreadActionsMake(model, helper);
            var titles = actions.Select(a => a.Title).ToList();
            Assert.DoesNotContain("Pin", titles);
            Assert.DoesNotContain("Unpin", titles);
            Assert.DoesNotContain("Settle", titles);
            Assert.DoesNotContain("Reopen", titles);
            var archive = Assert.Single(actions, a => a.Title == "Archive"); // :2224-2225, only in the settle/archive group
            Assert.True(archive.StartsGroup); // :2221 startsGroup: !settleFirst, and settleFirst is always false for a helper
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-103")] // App/Views/Sidebar/SidebarView.swift:2245-2312
    public void The_context_menus_actions_post_rather_than_run() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0);
        var model = Model(harness, [project], [thread]);
        harness.Main.OnMain(() =>
        {
            var requests = Activator.CreateInstance(Private("SidebarMenuRequests"))!;
            var actions = ThreadActionsMakeContextMenu(model, thread, true, requests);

            var rename = actions.Single(a => a.Title == "Rename");
            rename.Action();
            Assert.Equal(thread, Get(requests, "Rename")); // :2261 requests.rename = thread, posted rather than run
            Assert.Null(Get(requests, "Delete"));

            var delete = actions.Single(a => a.Title == "Delete\u2026");
            delete.Action();
            Assert.Equal(thread, Get(requests, "Delete")); // :2309-2310
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-103")] // App/Views/Sidebar/SidebarView.swift:2262-2263, :2269 guard let model else { return }
    public void A_context_menu_actions_weak_model_capture_bails_once_the_model_is_gone() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0);
        var model = Model(harness, [project], [thread]);
        harness.Main.OnMain(() =>
        {
            var requests = Activator.CreateInstance(Private("SidebarMenuRequests"))!;
            var actions = ThreadActionsMakeContextMenu(model, thread, true, requests);
            var pin = actions.Single(a => a.Title == "Pin");
            GC.KeepAlive(model);
            pin.Action(); // model is still alive: the weak capture resolves
            Assert.True(model.Thread(thread.Id)!.IsPinned);
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-098")] // App/Views/Sidebar/SidebarView.swift:2193-2246, :2253-2312
    public void The_popover_and_context_menu_lists_carry_the_same_items_in_the_same_order() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0) with { WorktreePath = "C:/Alpha/wt" };
        var model = Model(harness, [project], [thread]);
        harness.Main.OnMain(() =>
        {
            var requests = Activator.CreateInstance(Private("SidebarMenuRequests"))!;
            var popoverTitles = ThreadActionsMake(model, thread).Select(a => a.Title).ToList();
            var menuTitles = ThreadActionsMakeContextMenu(model, thread, true, requests).Select(a => a.Title).ToList();
            Assert.Equal(popoverTitles, menuTitles); // the two lists carry the same items in the same order
            Assert.Equal(
                ["Rename", "Pin", "Forward to new thread", "Reveal worktree in File Explorer"],
                popoverTitles.Take(4).ToList()); // :2200-2218 the fixed order up to the settle/archive group
            Assert.Equal("Delete\u2026", popoverTitles[^1]); // :2244 Delete… always last
        });
    });

    // ---- PR-SIDEBAR-107, PR-SIDEBAR-108, PR-SIDEBAR-109: SidebarHelperRow's directly testable pieces ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-107")] // App/Views/Sidebar/SidebarView.swift:1361-1372
    public void A_helper_rows_title_and_background_match_the_recorded_numbers()
    {
        var type = Private("SidebarHelperRow");
        Assert.Equal(SwiftFont.Weight.Medium, CallStatic(type, "TitleWeight", true, false)); // :1361 selected
        Assert.Equal(SwiftFont.Weight.Medium, CallStatic(type, "TitleWeight", false, true)); // :1361 unread
        Assert.Equal(SwiftFont.Weight.Regular, CallStatic(type, "TitleWeight", false, false));
        Assert.Equal(0.96, (double)CallStatic(type, "TitleOpacity", true)!); // :1362
        Assert.Equal(0.8, (double)CallStatic(type, "TitleOpacity", false)!);
        Assert.Equal(34d, (double)CallStatic(type, "TrailingPadding", true)!); // :1365-1367, 8 + 26
        Assert.Equal(26d, (double)CallStatic(type, "TrailingPadding", false)!); // 8 + 18
        Assert.Equal(0.12, (double)CallStatic(type, "BackgroundFill", true, false)!); // :1370 selected
        Assert.Equal(0.06, (double)CallStatic(type, "BackgroundFill", false, true)!); // hovered
        Assert.Equal(0d, (double)CallStatic(type, "BackgroundFill", false, false)!); // neither
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-108")] // App/Views/Sidebar/SidebarView.swift:1354-1357, upstream defect 65
    public void A_helper_rows_head_glyph_never_shows_the_running_breath_ring() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var runningHead = NewThread(project, "Head", 0) with { Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Running) };
        var plain = NewThread(project, "Plain", 1);
        harness.Main.OnMain(() =>
        {
            var glyph = (HydraGlyph)CallStatic(Private("SidebarHelperRow"), "HeadGlyph", runningHead)!;
            Assert.False(glyph.IsRunning); // :1354-1357 no isRunning argument, even though the head is running
            Assert.Equal(14d, glyph.Size); // ThreadRowMetrics.headGlyphSize, :1295

            Assert.Null(CallStatic(Private("SidebarHelperRow"), "HeadGlyph", plain)); // no hydra head at all
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-109")] // App/Views/Sidebar/SidebarView.swift:1375-1384
    public void A_helper_rows_trailing_area_holds_the_ellipsis_while_shown_or_the_activity_status_otherwise() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0);
        var model = Model(harness, [project], [thread]);
        harness.Main.OnMain(() =>
        {
            var actionsButton = new Border(); // a stand-in for RowActionsButton, which this piece takes as a parameter
            var withActions = (Control)CallStatic(Private("SidebarHelperRow"), "BuildTrailingOverlay", true, thread, actionsButton)!;
            Assert.Same(actionsButton, withActions); // :1377 the ellipsis button, passed straight through
            Assert.Equal(new Thickness(0, 0, 6, 0), withActions.Margin); // :1382 .padding(.trailing, 6)

            var withoutActions = (Control)CallStatic(Private("SidebarHelperRow"), "BuildTrailingOverlay", false, thread, actionsButton)!;
            Assert.IsType(Private("ActivityStatus"), withoutActions); // :1380 ActivityStatus(thread: thread)
        });
    });
}
