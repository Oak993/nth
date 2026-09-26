using System.Collections;
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.App.Views.Sidebar;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Sidebar;

// Source: DroppyCode/App/Views/Sidebar/SidebarView.swift (B3 52bc792), :1-2695. Part 1 of 8: PR-HYDRAMERGE-100,
// PR-SIDEBAR-001 to -006, -009 to -025. Every expected value is transcribed from the B3 line cited on the test or from
// the record, never from the C# implementation. Non-ASCII text is written as escapes (R-STR-9).
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
}
