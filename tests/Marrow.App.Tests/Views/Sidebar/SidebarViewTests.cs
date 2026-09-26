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
    [Trait("Req", "UI-HYDRAMERGE-030")] // App/Views/Sidebar/SidebarView.swift:1980-1984 (part 6: the same stage line, absent state)
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
    [Trait("Req", "UI-HYDRAMERGE-030")] // App/Views/Sidebar/SidebarView.swift:1980-1984 (part 6: the stage words, present state)
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
    [Trait("Req", "UI-HYDRAMERGE-030")] // App/Views/Sidebar/SidebarView.swift:1983 (part 6: the fallback words, single ellipsis)
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
    [Trait("Req", "UI-SIDEBAR-001")] // App/Views/Sidebar/SidebarView.swift:74-80 (part 6: the same spacer, as a screen-contract record)
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
    [Trait("Req", "UI-SIDEBAR-042")] // App/Views/Sidebar/SidebarView.swift:165-167 (part 7: the same row's action, as
    // a screen-contract record)
    [Trait("Req", "UI-SIDEBAR-044")] // App/Views/Sidebar/SidebarView.swift:500-512 (part 7: the same dialog, as a
    // screen-contract record)
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
    [Trait("Req", "UI-SIDEBAR-044")] // App/Views/Sidebar/SidebarView.swift:507-511 (part 7: confirm adds every chosen
    // folder as a project and starts a thread in the last, as a screen-contract record)
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
    [Trait("Req", "UI-SIDEBAR-044")] // App/Views/Sidebar/SidebarView.swift:507 (part 7: cancel does nothing at all,
    // as a screen-contract record)
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
    [Trait("Req", "UI-SIDEBAR-043")] // App/Views/Sidebar/SidebarView.swift:168-175 (part 7: the same gear glyph, as a
    // screen-contract record)
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
    [Trait("Req", "PR-UPDATE-080")] // App/Views/Sidebar/SidebarView.swift:168-175 (part 6: this file's half of the record; the
    // About row's own placement is App/Views/Settings/SettingsView.swift:159-163, a different unit)
    [Trait("Req", "UI-SIDEBAR-043")] // App/Views/Sidebar/SidebarView.swift:170-174; App/Views/Settings/AboutUpdateViews.swift:526-534
    // (part 7: the same update dot, as a screen-contract record; OD-0008 C6, OD-0012 I1 keep the dot hidden until the
    // owner sets a Windows release feed)
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
    [Trait("Req", "UI-SIDEBAR-059")] // App/Views/Sidebar/SidebarView.swift:317-326 (part 7: the same arms, as a
    // screen-contract record)
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
    [Trait("Req", "UI-SIDEBAR-059")] // App/Views/Sidebar/SidebarView.swift:1063-1076 (part 7: the same timing, as a
    // screen-contract record)
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
    [Trait("Req", "UI-SIDEBAR-006")] // App/Views/Sidebar/SidebarView.swift:517-519, :331-332 (part 6: the same gap item, as a screen-contract record)
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
    [Trait("Req", "UI-SIDEBAR-007")] // App/Views/Sidebar/SidebarView.swift:533-537, :1281 (part 6: the same gap item, as a screen-contract record)
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
    [Trait("Req", "UI-SIDEBAR-007")] // App/Views/Sidebar/SidebarView.swift:536-537 (part 6: the gap item's absence state)
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
    [Trait("Req", "UI-SIDEBAR-058")] // App/Views/Sidebar/SidebarView.swift:703-712 (part 7: the gap, project-row and
    // thread-row sequence, and hasHelpers, as a screen-contract record; the project row's own inertness-but-live-
    // accessories and the thread rows' project form are proven separately below, since ThreadRow (:870) stays
    // MARROW_STUB -- STOP #8, SidebarThreadRow's own constructor)
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
    [Trait("Req", "UI-SIDEBAR-041")] // App/Views/Sidebar/SidebarView.swift:692-703 (part 7: the same empty state, as a
    // screen-contract record)
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
    [Trait("Req", "UI-SIDEBAR-019")] // App/Views/Sidebar/SidebarView.swift:2053 (part 6: the project line's absence with a picked icon)
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
    [Trait("Req", "UI-SIDEBAR-019")] // App/Views/Sidebar/SidebarView.swift:2053-2063 (part 6: the same project line, as a screen-contract record)
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
    [Trait("Req", "UI-SIDEBAR-018")] // App/Views/Sidebar/SidebarView.swift:2143-2179 (part 6: the same badge, as a screen-contract record)
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
    [Trait("Req", "UI-SIDEBAR-018")] // App/Views/Sidebar/SidebarView.swift:2143-2179 (part 6: the badge's unread-dot state)
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
    [Trait("Req", "UI-SIDEBAR-024")] // App/Views/Sidebar/SidebarView.swift:2408-2427 (part 7: the same states, as a
    // screen-contract record)
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
    [Trait("Req", "UI-SIDEBAR-020")] // App/Views/Sidebar/SidebarView.swift:2114-2142 (part 6: the same check, as a screen-contract record)
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
    [Trait("Req", "UI-SIDEBAR-023")] // App/Views/Sidebar/SidebarView.swift:2686-2695 (part 7: the same label, as a
    // screen-contract record; the B3 colour is Chrome.secondaryText at 0.8, ED-062's 0.7 label colour further scaled)
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
    // STOP App.Views.Sidebar.SidebarView #6 (extended part 7: UI-SIDEBAR-028, -029, -030 name the same site under
    // the B3 packet's own record ids)
    // Kind: forbidden need
    // Swift: DroppyCode/App/Views/Sidebar/SidebarView.swift:995-1024 (B3 52bc792); :1009 .textFieldStyle(.roundedBorder),
    // :1017 .buttonStyle(.bordered), :1019-1022 .buttonStyle(.borderedProminent)
    // Requirements: PR-SIDEBAR-091; UI-SIDEBAR-028 (the rename popover as a whole), UI-SIDEBAR-029 (the Title field;
    // its own site is otherwise unchanged from B1/B2, see this part's b2_change note, so only the field's chrome is
    // blocked, not its text or behaviour), UI-SIDEBAR-030 (the Cancel/Rename buttons)
    // What is unclear or missing: two AppKit control styles this file needs for the rename popover have no Windows
    // chrome anywhere in this port yet (Marrow.UI.Chrome, Marrow.UI.Port) and no engineering or platform-adaptation
    // decision names either one; this part's own decision list (ED-008, ED-009, ED-010, ED-041, ED-051, ED-059,
    // ED-062, ED-149, ED-187, OD-0001, OD-0005, OD-0008, OD-0009, OD-0010, PA-0019) is silent on both, and
    // porting/engineering_decisions.md and porting/platform_adaptations.md have no "roundedBorder" or
    // "borderedProminent"/"bordered" entry. Every other popover this part ports (Delete, Archive) reuses an
    // already-decided recipe (PopoverItem; OD-0005's `.glass`/`.glassProminent`); inventing a third, undecided one
    // here would be exactly the invention the contract forbids. Part 7's own decision list (ED-193 to ED-206) is
    // silent on both too; nothing has changed since part 4.
    // Readings or options: (a) treat `.bordered`/`.borderedProminent` as close enough to the already-decided
    // `.glass`/`.glassProminent` recipe and reuse it -> a visible material the source does not ask for on this
    // popover, an invented equivalence; (b) build a new, unstyled rectangle/border recipe for these two roles ->
    // arbitrary corner radius, padding and colours with no source number or owner ruling behind them.
    // Needed to proceed: an engineering decision (or owner ruling) naming the Windows chrome for
    // `.textFieldStyle(.roundedBorder)` and for `.buttonStyle(.bordered)`/`.buttonStyle(.borderedProminent)`.
    // Done so far: nothing implemented at RenameThreadPopover or SidebarView.RenamePopover; both stay MARROW_STUB.
    // liveRenameTarget (PR-SIDEBAR-093, the ellipsis path's live-thread lookup, :882-887) does not depend on this
    // site and is proven below. Nothing is claimed for UI-SIDEBAR-028, -029 or -030, and no test names them.

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
    [Trait("Req", "UI-SIDEBAR-034")] // App/Views/Sidebar/SidebarView.swift:1636-1660 (part 7: the same connector, as a
    // screen-contract record; the B3 colour is Chrome.secondaryText at 0.45/0.9, ED-062's 0.7 label colour further
    // scaled to 0.315/0.63)
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

    [Fact]
    [Trait("Req", "PR-SIDEBAR-110")] // App/Views/Sidebar/SidebarView.swift:1653 .contentShape(.rect)
    public void The_connectors_whole_rectangle_is_clickable_not_just_the_drawn_dash() => headless.OnUiThread(() =>
    {
        // RaiseEvent(ClickEvent) above bypasses hit-testing entirely; this exercises a real pointer click at a point
        // that is nowhere near the drawn dash (REVIEW FIX, part 4: IsHitTestVisible alone did not make the empty
        // space clickable; only Background = Transparent does). A real pointer move onto the button also fires its
        // .onHover (:1656), which animates through the frame clock (WallpaperCardFixtures.cs's own pattern), so this
        // binds a manual frame source the way that suite does.
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var folded = false;
        var control = (Control)Activator.CreateInstance(Private("HelperConnector"), false, (Action)(() => folded = true))!;
        control.Width = ThreadRowMetrics.ConnectorWidth; // :1291, 28
        control.Height = ThreadRowMetrics.HelperHeight; // :1277, 24
        Shown(control);
        var window = WindowOf(control);
        window.UpdateLayout();

        // The stroke sits at x = ConnectorLineX (18): a vertical line down the middle-right and a horizontal tick at
        // midY (12) from x=18 to the right edge. (2, 2) is near the top-left corner, clear of both dash segments.
        var point = At(control, 2, 2, window);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Assert.True(folded);
        window.Close();
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
    [Trait("Req", "UI-GIT-057")] // App/Views/Sidebar/SidebarView.swift:965-983; App/AppModel.swift:943-966 (part 6: the same
    // popover, as a screen-contract record)
    [Trait("Req", "UI-SIDEBAR-026")] // App/Views/Sidebar/SidebarView.swift:966-981 (part 7: the same popover, as a
    // screen-contract record)
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
    [Trait("Req", "UI-GIT-057")] // App/Views/Sidebar/SidebarView.swift:977-978 (part 6: the worktree row's own state)
    [Trait("Req", "UI-SIDEBAR-027")] // App/Views/Sidebar/SidebarView.swift:977-980 (part 7: the same rows, as a
    // screen-contract record)
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

    // ArchiveThreadPopover now takes the anchor it resolves its window-scoped key handler from (REVIEW FIX, part 4:
    // see the constructor's own comment). These two helpers host a throwaway anchor alongside the popover in one
    // window -- a degenerate stand-in for ArchivePopover's real `Panel { row, popup }`, adequate for every test below
    // except Through_a_real_popup_..., which deliberately keeps the anchor and the popover in separate top-levels.
    private static Control ShownArchivePopover(ChatThread thread, Action onArchive, Action onDelete, Action onDismiss)
    {
        var anchor = new Border();
        var popover = (Control)Activator.CreateInstance(Private("ArchiveThreadPopover"), thread, anchor, onArchive, onDelete, onDismiss)!;
        new AvaloniaWindow { Content = new Panel { Children = { anchor, popover } }, Width = 200, Height = 100 }.Show();
        return popover;
    }

    private static Control ShownWithAnchor(Control anchor, Control content)
    {
        new AvaloniaWindow { Content = new Panel { Children = { anchor, content } }, Width = 200, Height = 100 }.Show();
        return content;
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-095")] // App/Views/Sidebar/SidebarView.swift:905-943
    [Trait("Req", "UI-SIDEBAR-031")] // App/Views/Sidebar/SidebarView.swift:904-964 (part 7: the same popover, as a
    // screen-contract record; OD-0005 governs the glass material, reproduced as an Acrylic-class Windows material)
    public void The_archive_popover_is_272_wide_asks_Archive_title_and_offers_Delete_and_Archive() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = AppModelSamples.Project("Alpha", harness.NewFolder("Alpha"));
        var thread = AppModelSamples.Thread(project) with { Title = "Fix the bug" };
        var archived = 0;
        var deleted = 0;
        var popover = ShownArchivePopover(thread, (Action)(() => archived++), (Action)(() => deleted++), (Action)(() => { }));

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
    [Trait("Req", "UI-SIDEBAR-032")] // App/Views/Sidebar/SidebarView.swift:927-940 (part 7: the same buttons, as a
    // screen-contract record)
    public void The_delete_button_in_the_archive_popover_is_drawn_in_danger() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = AppModelSamples.Project("Alpha", harness.NewFolder("Alpha"));
        var thread = AppModelSamples.Thread(project) with { Title = "Fix the bug" };
        var popover = ShownArchivePopover(thread, (Action)(() => { }), (Action)(() => { }), (Action)(() => { }));
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
        var popover = ShownArchivePopover(thread, (Action)(() => archived++), (Action)(() => { }), (Action)(() => dismissed++));
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
        var popover = ShownArchivePopover(thread, (Action)(() => { }), (Action)(() => { }), (Action)(() => dismissed++));
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
        var anchor = new Border();
        var popover = (Control)Activator.CreateInstance(Private("ArchiveThreadPopover"), thread, anchor, (Action)(() => archived++), (Action)(() => { }), (Action)(() => { }))!;
        var panel = new Panel { Children = { anchor, popover } };
        var window = new AvaloniaWindow { Content = panel, Width = 200, Height = 100 };
        window.Show();
        panel.Children.Remove(popover); // detaches the popover; the anchor (and its window) stay
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Assert.Equal(0, archived); // the handler was removed, so nothing archives
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-096")] // App/Views/Sidebar/SidebarView.swift:944-955, the doc comment at :900-904: "even
    // when the keys land on the chat window behind it"
    public void A_key_landing_on_the_anchors_window_still_archives_and_dismisses_when_the_popover_lives_in_its_own_top_level() => headless.OnUiThread(() =>
    {
        // Avalonia headless cannot open a real Popup at all (ViewLifetime.cs's own comment on IPopoverPresenter:
        // "headless tests record instead ... Avalonia 12.1.2 headless cannot open a Popup in a theme-less window"),
        // so a real Popup can't be exercised here either. This reproduces the shape a real Popup's separate PopupRoot
        // gives it instead: the anchor and the popover content sit in two distinct top-levels (two real Windows), and
        // only the anchor's window receives the key -- exactly AttachmentPreviewPanel.cs:162-172's "the popup's own
        // content is a separate top-level" scenario. Before the REVIEW FIX (resolving TopLevel.GetTopLevel(this)
        // instead of the anchor), the handler would attach to the popover's own window and this key would never
        // reach it.
        using var harness = new AppModelHarness();
        var project = AppModelSamples.Project("Alpha", harness.NewFolder("Alpha"));
        var thread = AppModelSamples.Thread(project);
        var archived = 0;
        var dismissed = 0;

        var anchor = new Border();
        var anchorWindow = new AvaloniaWindow { Content = anchor, Width = 200, Height = 100 };
        anchorWindow.Show();

        var popover = (Control)Activator.CreateInstance(Private("ArchiveThreadPopover"), thread, anchor,
            (Action)(() => archived++), (Action)(() => { }), (Action)(() => dismissed++))!;
        var popoverWindow = new AvaloniaWindow { Content = popover, Width = 272, Height = 100 };
        popoverWindow.Show(); // a distinct top-level, standing in for the real PopupRoot

        anchorWindow.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); // :951 keyCode 36
        Assert.Equal(1, archived); // the handler was resolved from the anchor's window, not the popover's own

        anchorWindow.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); // :952 keyCode 53
        Assert.Equal(1, dismissed);

        popoverWindow.Close();
        anchorWindow.Close();
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
            var anchor = new Border();
            var content = (Control)Call(view, "BuildArchivePopoverContent", thread, anchor)!;
            ShownWithAnchor(anchor, content);
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
            var anchor = new Border();
            var content = (Control)Call(view, "BuildArchivePopoverContent", thread, anchor)!;
            ShownWithAnchor(anchor, content);
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
            var anchor = new Border();
            var content = (Control)Call(view, "BuildArchivePopoverContent", thread, anchor)!;
            ShownWithAnchor(anchor, content);
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
            // REVIEW FIX (part 4): a helper's list is Rename, Forward, Archive, Delete… = 4, never 5. :2204's Pin
            // guard and :2225-2226's helper branch both gate solely on isHelper, so a helper's count does not depend
            // on isSettled or worktreePath either; this is the list's true minimum (isHelper is the only branch that
            // ever drops the list below 2 items in the settle/archive group).
            Assert.Equal(4, actions.Count);
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
    [Trait("Req", "UI-SIDEBAR-025")] // App/Views/Sidebar/SidebarView.swift:2192-2314 (part 7: the same menu items, as a
    // screen-contract record: order, symbols, Delete… in danger, and the settle/archive swap by threadFinishAction)
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
            // REVIEW FIX (part 4): this non-helper, unsettled, worktree-carrying thread's list is Rename, Pin,
            // Forward, Reveal, [Archive, Settle] (both, :2242, since !isHelper), Delete… = 7 items, counted
            // straight off :2201-2244 (and the identical :2259-2311) -- not 8: nothing in the builder appends an
            // 8th item.
            Assert.Equal(7, popoverTitles.Count);
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

    // ==== Part 5 of 8: PR-SIDEBAR-111, -113 to -120, -123, -125, -154, -155, -159 ====
    //
    // Records: PR-SIDEBAR-111, -113, -114, -115, -116, -117, -118, -119, -120, -123, -125, -154, -155, -159 (14 of
    // this part's 24). PR-SIDEBAR-121, -122, -126, -127, -128, -129, -130, -133, -134, -135 are the STOP below.
    //
    // STOP App.Views.Sidebar.SidebarView #7 (narrowed part 6: ProjectRow's own constructor is now built, along with
    // contextMenuActions, actions(isActive:) and ProjectPairMenuItems; see ProjectRow's own header comment in
    // SidebarView.cs for why PR-SIDEBAR-133, the live AppKit-style context menu, is a separate, still-open gap)
    // Kind: missing dependency
    // Swift: DroppyCode/App/Views/Sidebar/SidebarView.swift:1436-1548 HeadCardsRow's own constructor; :1138-1161
    // ProjectRow's own <c>.contextMenu</c> attachment; :1820-1833, :1933-1978 SidebarThreadRow.windowFrame/glide
    // (B3 52bc792)
    // Requirements: PR-SIDEBAR-121, PR-SIDEBAR-122 (HeadCardsRow's glyph buttons, hover, selection and the card's own
    // animation/fold); PR-SIDEBAR-130 (the "Remove project" item's own confirmation, gated behind the same missing
    // live menu); PR-SIDEBAR-133 (the AppKit context menu itself, live on a right-click); PR-SIDEBAR-134,
    // PR-SIDEBAR-135 (the row's frame report and the reopen/settle glide it launches)
    // What is unclear or missing: HeadCardsRow's own constructor and SidebarThreadRow.windowFrame/glide stay
    // MARROW_STUB, unchanged from part 5 (HeadCardsRow needs a geometry-observed width: PR-SIDEBAR-117's and
    // PR-SIDEBAR-118's PerRow/RowGap are proven as static pieces below, but the live SizeChanged wiring, the
    // per-glyph hover scale and selection ring, and the card's own opacity/animation are still unassembled;
    // SidebarThreadRow.windowFrame and .glide are part of that row's own still-MARROW_STUB constructor). New this
    // part: ProjectRow's own constructor, contextMenuActions, actions(isActive:) and ProjectPairMenuItems are built
    // and proven (UI-SIDEBAR-008, -009, -010, -014, -015, -016), but SwiftUI's <c>.contextMenu</c> modifier
    // re-evaluates isActive, hasOverride and the pairs snapshot fresh every time the menu opens (:1138-1161), and no
    // ED or platform-adaptation entry names a Windows/Avalonia foundation for that per-open refresh
    // (porting/engineering_decisions.md and porting/platform_adaptations.md have no ".contextMenu" or "ContextMenu"
    // entry). A menu attached once, at construction time, would go stale the first time isActive or hasOverride
    // changed without the row itself being rebuilt: an invented, silently wrong behaviour. PR-SIDEBAR-130's own
    // confirmation popover (OD-0008 A9, OD-0012 E2 wording; SidebarMenuRequests would need a ProjectRemoval request
    // field, as it already carries Rename and Delete) is reachable only once that same menu exists, so it is gated
    // behind this same STOP, not a separate one.
    // Readings or options: for the context menu, (a) build a menu fixed at construction time -> goes stale, an
    // invented behaviour the contract forbids more than an honest gap does; (b) invent a rebuild-on-open hook with no
    // ED/PA backing -> the same objection. For HeadCardsRow and SidebarThreadRow's own remaining pieces: none
    // attempted; a partial assembly risked an untested or wrong behaviour standing in for a hard feature.
    // Needed to proceed: an engineering or platform-adaptation decision naming the Windows/Avalonia foundation for
    // SwiftUI's <c>.contextMenu</c> re-evaluation-on-open (then PR-SIDEBAR-130's request field and popover follow);
    // separately, HeadCardsRow's own constructor assembly and SidebarThreadRow's own remaining collaborators.
    // Done so far: HeadCardsRow.Usable, PerRow, RowGap, Chunk, Capped, Visible, CardRows and ChevronTooltip are
    // proven directly (PR-SIDEBAR-117, -118, -119, -120, part 5); ProjectRow's own body, contextMenuActions,
    // actions(isActive:), BuildContextMenuItems and ProjectPairMenuItems are proven directly (part 6, below); nothing
    // is claimed for PR-SIDEBAR-121, -122, -130, -133, -134 or -135, and no test names them.

    private static IReadOnlyList<object> Enumerate(object valueList) => ((IEnumerable)valueList).Cast<object>().ToList();

    // ---- PR-SIDEBAR-111: the connector shape's exact extent, including the horizontal tick ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-111")] // App/Views/Sidebar/SidebarView.swift:1662-1676, :1292 connectorLineX = 18
    public void The_connector_shape_runs_down_x_18_and_ticks_to_2pt_short_of_the_trailing_edge() => headless.OnUiThread(() =>
    {
        object CreateShapeRecord(bool endsHere) => Activator.CreateInstance(Private("HelperConnectorShape"), [endsHere])!;

        var last = (Geometry)CallOn(CreateShapeRecord(true), "Path", new Rect(0, 0, 28, 24))!;
        Assert.Equal(18d, last.Bounds.Left, 3); // :1668, :1670 both segments start at x = connectorLineX (18)
        Assert.Equal(26d, last.Bounds.Right, 3); // :1671 rect.maxX - 2 = 26 in a 28 pt column
        Assert.Equal(0d, last.Bounds.Top, 3); // :1668 rect.minY
        Assert.Equal(12d, last.Bounds.Bottom, 3); // :1669 endsHere -> rect.midY

        var full = (Geometry)CallOn(CreateShapeRecord(false), "Path", new Rect(0, 0, 28, 24))!;
        Assert.Equal(18d, full.Bounds.Left, 3);
        Assert.Equal(26d, full.Bounds.Right, 3);
        Assert.Equal(0d, full.Bounds.Top, 3);
        Assert.Equal(24d, full.Bounds.Bottom, 3); // :1669 not endsHere -> rect.maxY
    });

    // ---- PR-SIDEBAR-113, PR-SIDEBAR-114, PR-SIDEBAR-115: HelperStubRow, the folded line under a parent ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-113")] // App/Views/Sidebar/SidebarView.swift:1564-1620
    [Trait("Req", "UI-SIDEBAR-035")] // App/Views/Sidebar/SidebarView.swift:1552-1618 (part 7: the same row, as a
    // screen-contract record)
    public void A_folded_stub_counts_its_helpers_and_says_head_only_when_every_folded_thread_is_a_head() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var helperA = NewThread(project, "Helper A", 1) with { ParentThreadID = parent.Id };
        var helperB = NewThread(project, "Helper B", 2) with { ParentThreadID = parent.Id };
        var headA = HeadHelper(project, parent.Id, 0, 3);
        var headB = HeadHelper(project, parent.Id, 1, 4);
        var model = Model(harness, [project], [parent, helperA, helperB, headA, headB]);
        harness.Main.OnMain(() =>
        {
            var type = Private("HelperStubRow");

            var one = (Control)Activator.CreateInstance(type, parent, new[] { helperA }, (Action)(() => { }))!;
            Hosted(model, one);
            var oneButton = LogicalQuery.SelfAndDescendants<Button>(one).Single();
            var oneLabel = LogicalQuery.SelfAndDescendants<TextBlock>(oneButton).Single();
            Assert.Equal("1 helper", oneLabel.Text); // :1591 count == 1, mixed/no-head -> "helper"
            Assert.Equal("Show the helper", ToolTip.GetTip(oneButton)); // :1614
            Assert.Equal("1 folded helper", AutomationProperties.GetName(oneButton)); // :1615

            // A mix of a helper and a head still reads "helpers" (any mix says helpers).
            var mixed = (Control)Activator.CreateInstance(type, parent, new[] { helperA, headA }, (Action)(() => { }))!;
            Hosted(model, mixed);
            var mixedLabel = LogicalQuery.SelfAndDescendants<TextBlock>(LogicalQuery.SelfAndDescendants<Button>(mixed).Single()).Single();
            Assert.Equal("2 helpers", mixedLabel.Text);

            // Every folded thread a head -> "head"/"heads".
            var heads = (Control)Activator.CreateInstance(type, parent, new[] { headA, headB }, (Action)(() => { }))!;
            Hosted(model, heads);
            var headsButton = LogicalQuery.SelfAndDescendants<Button>(heads).Single();
            var headsLabel = LogicalQuery.SelfAndDescendants<TextBlock>(headsButton).Single();
            Assert.Equal("2 heads", headsLabel.Text);
            Assert.Equal("Show 2 heads", ToolTip.GetTip(headsButton));
            Assert.Equal("2 folded heads", AutomationProperties.GetName(headsButton));
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-114")] // App/Views/Sidebar/SidebarView.swift:1567-1571, :1594-1597
    public void The_folded_stub_shows_a_mini_spinner_only_while_a_folded_helper_is_running_or_merging() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource()); // the running spinner animates
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var helperA = NewThread(project, "Helper A", 1) with { ParentThreadID = parent.Id };
        var helperB = NewThread(project, "Helper B", 2) with { ParentThreadID = parent.Id };
        var model = Model(harness, [project], [parent, helperA, helperB]);

        // Each case is its own harness.Main.OnMain call, as the ActivityStatus regression above does: OnMain pumps
        // the manual loop to idle on return, which is what actually runs Tracked<T>'s MainActor.Post(Update)
        // (ViewSupport.cs:71) after the runtime mutation, not Dispatcher.UIThread.RunJobs (a different queue).
        harness.Main.OnMain(() =>
        {
            var quiet = (Control)Activator.CreateInstance(Private("HelperStubRow"), parent, new[] { helperA, helperB }, (Action)(() => { }))!;
            Hosted(model, quiet);
            Assert.Empty(LogicalQuery.SelfAndDescendants<MiniSpinner>(quiet)); // no live runtime for either helper: no spinner
            WindowOf(quiet).Close(); // closed before the next case opens another, rather than left to pile up
        });

        // ThreadRuntime.IsRunning (:887) is computed from Phase, whose setter is private; set it through
        // reflection as AppModelHydraTests.SetRuntimeProperty does. :1573
        harness.Main.OnMain(() => typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.Phase))!.SetValue(model.Runtime(helperB.Id), RuntimePhase.Running));
        harness.Main.OnMain(() =>
        {
            var working = (Control)Activator.CreateInstance(Private("HelperStubRow"), parent, new[] { helperA, helperB }, (Action)(() => { }))!;
            Hosted(model, working);
            Assert.Single(LogicalQuery.SelfAndDescendants<MiniSpinner>(working)); // :1594-1596
            WindowOf(working).Close();
        });

        harness.Main.OnMain(() =>
        {
            typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.Phase))!.SetValue(model.Runtime(helperB.Id), RuntimePhase.Idle);
            model.Runtime(helperA.Id).IsHydraMerging = true; // :1573 isHydraMerging also counts
        });
        harness.Main.OnMain(() =>
        {
            var merging = (Control)Activator.CreateInstance(Private("HelperStubRow"), parent, new[] { helperA, helperB }, (Action)(() => { }))!;
            Hosted(model, merging);
            Assert.Single(LogicalQuery.SelfAndDescendants<MiniSpinner>(merging));
            WindowOf(merging).Close();
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-115")] // App/Views/Sidebar/SidebarView.swift:1583, :1606-1609
    public void Clicking_the_folded_stub_anywhere_including_the_connector_unfolds_the_helpers() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var helperA = NewThread(project, "Helper A", 1) with { ParentThreadID = parent.Id };
        var model = Model(harness, [project], [parent, helperA]);
        harness.Main.OnMain(() =>
        {
            var unfolded = false;
            var row = (Control)Activator.CreateInstance(Private("HelperStubRow"), parent, new[] { helperA }, (Action)(() => unfolded = true))!;
            Hosted(model, row);
            var button = LogicalQuery.SelfAndDescendants<Button>(row).Single(); // :1583 the whole HStack is one Button
            Click(button);
            Assert.True(unfolded);
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-115")] // App/Views/Sidebar/SidebarView.swift:1603, :1605-1606 (REVIEW FIX, part 5)
    public void Clicking_the_stub_rows_empty_trailing_region_past_the_label_still_unfolds_the_helpers() => headless.OnUiThread(() =>
    {
        // :1603 .frame(maxWidth: .infinity, alignment: .leading) is on the labelled HStack itself, before :1605-1606's
        // .background and .contentShape, so the hover fill and hit area cover the row's whole remaining width, not
        // just the label's own text. Click(button) above (and PR-SIDEBAR-115's own synthetic click) bypasses hit-
        // testing entirely; this uses a real pointer click far past where the chevron/label content ends, in a
        // window wide enough that the gap cannot be closed by the label's own text (REVIEW FIX: a horizontally
        // oriented StackPanel arranges every child at its own DesiredSize along the stack axis --
        // HorizontalAlignment.Stretch on a stacked child is a no-op there -- so `shape` sat at only its own content
        // width instead of the row's remaining width; fixed with a Grid Auto,* column, this file's own established
        // idiom for a fixed leading column plus a flexible trailing one, at ThreadRowFace's `row`, :2022).
        using var frames = FrameClock.Source.Override(new ManualFrameSource()); // a real pointer move fires .onHover (:1611-1613)
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var helperA = NewThread(project, "Helper A", 1) with { ParentThreadID = parent.Id };
        var model = Model(harness, [project], [parent, helperA]);
        harness.Main.OnMain(() =>
        {
            var unfolded = false;
            var row = (Control)Activator.CreateInstance(Private("HelperStubRow"), parent, new[] { helperA }, (Action)(() => unfolded = true))!;
            var host = new Border();
            AppEnvironment.SetAppModel(host, model);
            host.Child = row;
            // :1584 connectorWidth is 28; a 400pt window leaves 372pt of remaining width, far past any plausible
            // chevron+label content width, so a click near the far trailing edge only lands on live content if the
            // row's remaining width was actually handed to `shape`.
            var window = new AvaloniaWindow { Content = host, Width = 400, Height = 100 };
            window.Show();
            window.UpdateLayout();
            var button = LogicalQuery.SelfAndDescendants<Button>(row).Single();
            var point = At(button, 350, 50, window);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Assert.True(unfolded);
            window.Close();
        });
    });

    // ---- PR-SIDEBAR-116: the thread row's fold chevron, 46 pt from the trailing edge ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-116")] // App/Views/Sidebar/SidebarView.swift:1805-1806
    public void The_fold_overlay_is_absent_unless_showsFold_and_onToggleFold_are_both_set()
    {
        var type = Private("SidebarThreadRow");
        Assert.Null(CallStatic(type, "BuildFoldOverlay", false, (Action)(() => { }), false)); // :1805 !showsFold
        Assert.Null(CallStatic(type, "BuildFoldOverlay", true, null, false)); // :1805 onToggleFold nil
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-116")] // App/Views/Sidebar/SidebarView.swift:1804-1814, :1288 foldInset = 46
    [Trait("Req", "UI-SIDEBAR-022")] // App/Views/Sidebar/SidebarView.swift:1803-1814 (part 6: the same chevron, as a screen-contract record)
    public void The_fold_chevron_points_right_folded_and_down_unfolded_46pt_in_tooltipped_and_labelled() => headless.OnUiThread(() =>
    {
        var type = Private("SidebarThreadRow");
        var toggled = false;
        var folded = (Control)CallStatic(type, "BuildFoldOverlay", true, (Action)(() => toggled = true), true)!;
        Shown(folded);
        Assert.Equal(new Thickness(0, 0, 46, 0), folded.Margin); // :1811, ThreadRowMetrics.foldInset
        Assert.Equal(HorizontalAlignment.Right, folded.HorizontalAlignment); // :1804 .overlay(alignment: .trailing)
        var button = Assert.IsType<Button>(folded);
        Assert.Equal("Show helpers", ToolTip.GetTip(button)); // :1812
        Assert.Equal("Show helpers", AutomationProperties.GetName(button)); // :1813
        Click(button);
        Assert.True(toggled); // :1807 Button(action: onToggleFold)

        var unfolded = (Control)CallStatic(type, "BuildFoldOverlay", true, (Action)(() => { }), false)!;
        Assert.Equal("Hide helpers", ToolTip.GetTip((Button)unfolded));
        Assert.Equal("Hide helpers", AutomationProperties.GetName((Button)unfolded));
    });

    // ---- PR-SIDEBAR-117, PR-SIDEBAR-118, PR-SIDEBAR-119, PR-SIDEBAR-120: HeadCardsRow's directly testable geometry ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-117")] // App/Views/Sidebar/SidebarView.swift:1439-1451
    public void A_head_cards_glyphs_per_row_defaults_to_seven_before_measurement_then_floors_the_fit()
    {
        var type = Private("HeadCardsRow");
        Assert.Equal(7L, CallStatic(type, "PerRow", 0d)); // :1451 width == 0 -> 7
        // At the sidebar's own 210 pt minimum, the file's own worked example (:1439-1441): cardInner = 210-28-12 =
        // 170; usable = 170-3 = 167; perRow = floor((167+22-14)/22) = floor(175/22) = 7.
        Assert.Equal(7L, CallStatic(type, "PerRow", 210d));
        // A narrow card clamps to at least one glyph per row even when the floor would be zero.
        Assert.Equal(1L, CallStatic(type, "PerRow", 28d + 12d + 3d + 1d)); // usable = 1, floor((1+8)/22) = 0 -> max(1, 0)
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-118")] // App/Views/Sidebar/SidebarView.swift:1456-1458
    public void A_head_cards_row_gap_stretches_past_8pt_until_the_last_badge_sits_on_the_padding()
    {
        var type = Private("HeadCardsRow");
        Assert.Equal(8d, (double)CallStatic(type, "RowGap", 0d, 7L)!); // :1458 width <= 0 -> tightGap
        Assert.Equal(8d, (double)CallStatic(type, "RowGap", 210d, 1L)!); // :1458 perRow <= 1 -> tightGap
        // width 210, perRow 7 (see PR-SIDEBAR-117): usable 167; rowGap = max(8, (167 - 7*14) / 6) = max(8, 69/6) = 11.5.
        Assert.Equal(11.5, (double)CallStatic(type, "RowGap", 210d, 7L)!, 3);
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-119")] // App/Views/Sidebar/SidebarView.swift:1436-1443, :1514-1526
    public void A_head_card_caps_at_twelve_heads_and_the_chevron_row_reads_the_right_tooltip() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var heads = Enumerable.Range(0, 15).Select(i => NewThread(project, "Head" + i, i)).ToList();
        var type = Private("HeadCardsRow");

        Assert.False((bool)CallStatic(type, "Capped", 12L)!); // :1442 not more than 12
        Assert.True((bool)CallStatic(type, "Capped", 13L)!);
        Assert.Equal(2L, CallStatic(type, "CardRows", 2L, false)); // :1461 rows.count, no chevron row
        Assert.Equal(3L, CallStatic(type, "CardRows", 2L, true)); // :1461 + 1 chevron row

        var collapsed = Enumerate(CallStatic(type, "Visible", heads, false)!); // :1443
        Assert.Equal(12, collapsed.Count);
        Assert.Equal(heads.Take(12).Select(h => h.Id), collapsed.Cast<ChatThread>().Select(h => h.Id));
        Assert.Equal(15, Enumerate(CallStatic(type, "Visible", heads, true)!).Count); // showsAllHeads -> every head

        Assert.Equal("Show all 17 heads", CallStatic(type, "ChevronTooltip", false, 17L)); // :1526
        Assert.Equal("Show fewer heads", CallStatic(type, "ChevronTooltip", true, 17L));
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-120")] // App/Views/Sidebar/SidebarView.swift:1305-1311, :1530-1540
    public void A_head_cards_height_is_12pt_padding_plus_14pt_per_row_plus_6pt_between_rows()
    {
        double CardHeight(long rows) => (double)CallStatic(Private("ThreadRowMetrics"), "HeadCardHeight", rows)!;
        Assert.Equal(26d, CardHeight(1)); // :1310 12 + 14*1 + 6*0
        Assert.Equal(46d, CardHeight(2)); // 12 + 28 + 6
        Assert.Equal(66d, CardHeight(3)); // 12 + 42 + 12
        Assert.Equal(26d, CardHeight(0)); // :1309 max(1, rows) clamps to 1
    }

    // ---- PR-SIDEBAR-123: SettledHeader ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-123")] // App/Views/Sidebar/SidebarView.swift:2363-2406
    [Trait("Req", "UI-SIDEBAR-040")] // App/Views/Sidebar/SidebarView.swift:2363-2406 (part 7: the same header, as a
    // screen-contract record; the B3 chevron is 10 pt, .caption2, ED-059 (a fixed 9 pt at B1))
    public void The_settled_header_shows_its_count_toggles_the_fold_and_rotates_the_chevron() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0);
        var model = Model(harness, [project], [thread]);
        harness.Main.OnMain(() => model.Settings.SettledCollapsed = false); // :2369 start expanded (default true, AppSettings.cs:1087)

        Button button = null!;
        harness.Main.OnMain(() =>
        {
            var header = (Control)Activator.CreateInstance(Private("SettledHeader"), 3L, true)!;
            Hosted(model, header);
            button = LogicalQuery.SelfAndDescendants<Button>(header).Single(); // SettledHeader's Child is the Button
            var texts = LogicalQuery.SelfAndDescendants<TextBlock>(button).ToList();
            Assert.Equal("Settled", texts[0].Text); // :2380
            Assert.Equal("3", texts[1].Text); // :2384, a bare decimal
            Assert.Equal(0.8, texts[1].Opacity); // :2387
            Assert.Equal("Hide settled threads", ToolTip.GetTip(button)); // :2401 expanded
            Assert.Equal("Settled, 3 threads", AutomationProperties.GetName(button)); // :2402, always plural
            Assert.Equal("Expanded", AutomationProperties.GetItemStatus(button)); // :2403
            Click(button); // :2370-2373
        });

        // A separate harness.Main.OnMain call, as the ActivityStatus regression above does: OnMain pumps the manual
        // loop to idle on return, which is what runs Tracked<T>'s MainActor.Post(Update) (ViewSupport.cs:71) after
        // the click's mutation, not Dispatcher.UIThread.RunJobs (a different queue).
        harness.Main.OnMain(() =>
        {
            Assert.True(model.Settings.SettledCollapsed);
            Assert.Equal("Show settled threads", ToolTip.GetTip(button)); // :2401 collapsed
            Assert.Equal("Collapsed", AutomationProperties.GetItemStatus(button));
        });
    });

    // ---- PR-SIDEBAR-125: ActivityHeader ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-125")] // App/Views/Sidebar/SidebarView.swift:2345-2358
    [Trait("Req", "UI-SIDEBAR-039")] // App/Views/Sidebar/SidebarView.swift:2345-2361 (part 7: the same header, as a
    // screen-contract record)
    public void The_activity_header_is_the_verbatim_title_with_2_or_14pt_of_top_padding() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0);
        var model = Model(harness, [project], [thread]);
        harness.Main.OnMain(() =>
        {
            var first = (Control)Activator.CreateInstance(Private("ActivityHeader"), "Today", true)!;
            Hosted(model, first);
            var firstText = LogicalQuery.SelfAndDescendants<TextBlock>(first).Single();
            Assert.Equal("Today", firstText.Text); // :2350, verbatim
            Assert.Equal(new Thickness(8, 2, 8, 4), firstText.Margin); // :2353-2355 isFirst -> 2 pt top

            var later = (Control)Activator.CreateInstance(Private("ActivityHeader"), "Yesterday", false)!;
            Hosted(model, later);
            var laterText = LogicalQuery.SelfAndDescendants<TextBlock>(later).Single();
            Assert.Equal(new Thickness(8, 14, 8, 4), laterText.Margin); // not first -> 14 pt top
        });
    });

    // ---- PR-SIDEBAR-154: PlacementCache and SidebarItemCache skip rebuilding when their key is unchanged ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-154")] // App/Views/Sidebar/SidebarView.swift:2485-2536 PlacementCache
    public void PlacementCache_hands_back_the_same_placements_only_when_no_fingerprint_field_changed() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var threadA = NewThread(project, "A", 0);
        var threadB = NewThread(project, "B", 1);
        var cache = Activator.CreateInstance(Private("PlacementCache"))!;

        var first = Enumerate(CallOn(cache, "Placements", (object)new[] { threadA, threadB })!);
        var second = Enumerate(CallOn(cache, "Placements", (object)new[] { threadA, threadB })!); // :2531 identical fingerprint array
        Assert.Same(first[0], second[0]); // hands back the previously built array untouched, no rebuild
        Assert.Same(first[1], second[1]);

        // A title change alone does not move the fingerprint (:2487-2503 lists no title).
        var renamedA = threadA with { Title = "Renamed" };
        var third = Enumerate(CallOn(cache, "Placements", (object)new[] { renamedA, threadB })!);
        Assert.Same(second[0], third[0]);
        Assert.Same(second[1], third[1]);

        // A field the fingerprint does read (isPinned) changes the array, so the whole cache rebuilds (:2531-2533,
        // compared as one array, not per element).
        var pinnedA = renamedA with { IsPinned = !renamedA.IsPinned };
        var fourth = Enumerate(CallOn(cache, "Placements", (object)new[] { pinnedA, threadB })!);
        Assert.NotSame(third[0], fourth[0]);
        Assert.NotSame(third[1], fourth[1]);
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-154")] // App/Views/Sidebar/SidebarView.swift:2589-2659 SidebarItemCache.helpers(for:group:)
    public void SidebarItemCache_rebuilds_the_helper_grouping_only_when_placements_change() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var threadA = NewThread(project, "A", 0);
        var threadB = NewThread(project, "B", 1);
        var placementCache = Activator.CreateInstance(Private("PlacementCache"))!;
        var itemCache = Activator.CreateInstance(Private("SidebarItemCache"))!;
        var placementsA = CallOn(placementCache, "Placements", (object)new[] { threadA })!;
        var placementsAB = CallOn(placementCache, "Placements", (object)new[] { threadA, threadB })!;

        var calls = 0;
        ValueMap<Guid, ValueList<ChatThread>> Group()
        {
            calls++;
            return new ValueMap<Guid, ValueList<ChatThread>>(new Dictionary<Guid, ValueList<ChatThread>>());
        }

        CallOn(itemCache, "Helpers", placementsA, (Func<ValueMap<Guid, ValueList<ChatThread>>>)Group);
        CallOn(itemCache, "Helpers", placementsA, (Func<ValueMap<Guid, ValueList<ChatThread>>>)Group); // :2629 identical placements
        Assert.Equal(1, calls); // group() ran once only

        CallOn(itemCache, "Helpers", placementsAB, (Func<ValueMap<Guid, ValueList<ChatThread>>>)Group); // :2629 a different array
        Assert.Equal(2, calls);
    });

    // ---- PR-SIDEBAR-155: SidebarThreadRow and SidebarHelperRow compare value inputs only ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-155")] // App/Views/Sidebar/SidebarView.swift:1882-1891 SidebarThreadRow.==
    public void SidebarThreadRows_equatable_compares_value_inputs_only_ignoring_closures() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var threadA = NewThread(project, "A", 0);
        var type = Private("SidebarThreadRow");
        bool Eq(ChatThread l, string? lp, bool ld, bool lf, ChatThread r, string? rp, bool rd, bool rf) =>
            (bool)CallStatic(type, "RowEquals", l, lp, ld, lf, r, rp, rd, rf)!;

        Assert.True(Eq(threadA, "Alpha", false, true, threadA, "Alpha", false, true));
        Assert.False(Eq(threadA, "Alpha", false, true, threadA with { Title = "Renamed" }, "Alpha", false, true)); // :1884
        Assert.False(Eq(threadA, "Alpha", false, true, threadA with { UpdatedAt = threadA.UpdatedAt.AddMinutes(1) }, "Alpha", false, true)); // :1885
        Assert.False(Eq(threadA, "Alpha", false, true, threadA with { IsPinned = !threadA.IsPinned }, "Alpha", false, true)); // :1886
        Assert.False(Eq(threadA, "Alpha", false, true, threadA with { IsSettled = !threadA.IsSettled }, "Alpha", false, true)); // :1887
        Assert.False(Eq(threadA, "Alpha", false, true, threadA, "Beta", false, true)); // :1888 projectName
        Assert.False(Eq(threadA, "Alpha", false, true, threadA, "Alpha", true, true)); // :1889 isDragged
        Assert.False(Eq(threadA, "Alpha", false, true, threadA, "Alpha", false, false)); // :1890 onToggleFold nil-ness
        Assert.True(Eq(threadA, null, false, true, threadA, null, false, true)); // both project names nil
        Assert.False(Eq(threadA, null, false, true, threadA, "Alpha", false, true)); // one nil, one not
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-155")] // App/Views/Sidebar/SidebarView.swift:1413-1420 SidebarHelperRow.==
    public void SidebarHelperRows_equatable_compares_value_inputs_only() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var threadA = NewThread(project, "A", 0);
        var type = Private("SidebarHelperRow");
        bool Eq(ChatThread l, bool li, ChatThread r, bool ri) => (bool)CallStatic(type, "RowEquals", l, li, r, ri)!;

        Assert.True(Eq(threadA, true, threadA, true));
        Assert.False(Eq(threadA, true, threadA, false)); // :1419 isLast
        Assert.False(Eq(threadA, true, threadA with { Title = "Renamed" }, true)); // :1415
        Assert.False(Eq(threadA, true, threadA with { UpdatedAt = threadA.UpdatedAt.AddMinutes(1) }, true)); // :1416
        Assert.False(Eq(threadA, true, threadA with { IsPinned = !threadA.IsPinned }, true)); // :1417
        Assert.False(Eq(threadA, true, threadA with { IsSettled = !threadA.IsSettled }, true)); // :1418
    });

    // ---- PR-SIDEBAR-159: "Rename" is always the thread menu's first item ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-159")] // App/Views/Sidebar/SidebarView.swift:2201, :2251-2259
    public void Rename_is_always_the_first_item_with_symbol_pencil_in_every_thread_menu() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var lead = NewThread(project, "Lead", 0);
        var parent = NewThread(project, "Parent", 1);
        var helper = NewThread(project, "Helper", 2) with { ParentThreadID = parent.Id };
        var settled = NewThread(project, "Settled", 3) with { IsSettled = true };
        var pinned = NewThread(project, "Pinned", 4) with { IsPinned = true };
        var model = Model(harness, [project], [lead, parent, helper, settled, pinned]);
        harness.Main.OnMain(() =>
        {
            var requests = Activator.CreateInstance(Private("SidebarMenuRequests"))!;
            foreach (var thread in new[] { lead, helper, settled, pinned })
            {
                var made = ThreadActionsMake(model, thread); // :2193-2246
                Assert.Equal("Rename", made[0].Title); // :2200, unconditional, first
                Assert.Equal("pencil", made[0].Symbol);

                var menu = ThreadActionsMakeContextMenu(model, thread, false, requests); // :2253-2312
                Assert.Equal("Rename", menu[0].Title); // :2259-2262
                Assert.Equal("pencil", menu[0].Symbol);
            }
        });
    });

    // ======== Part 6 of 8 ========
    //
    // Records: PR-SIDEBAR-160, -161, -162, -163, PR-UPDATE-080, UI-GIT-057, UI-HYDRAMERGE-030, UI-SIDEBAR-001, -004,
    // -005, -006, -007, -008, -009, -010, -014, -015, -016, -017, -018, -019, -020, -021, -022 (24 of this part's
    // records). PR-UPDATE-080, UI-GIT-057, UI-HYDRAMERGE-030, UI-SIDEBAR-001, -006, -007, -018, -019, -020, -022 are
    // proven by adding this part's own [Trait] to a test parts 1-5 already wrote for the identical evidence (the
    // code and the record describe the same site; grep the id to find the added trait, e.g. UI-SIDEBAR-001 on
    // The_window_column_opens_with_a_14_point_clearance..., PR-SIDEBAR-023's own test). ProjectRow's own constructor
    // (UI-SIDEBAR-008, -009, -010), contextMenuActions, actions(isActive:), BuildContextMenuItems and
    // ProjectPairMenuItems (UI-SIDEBAR-014, -015, -016) are new this part (SidebarView.cs, ProjectRow's own header
    // comment there explains what PR-SIDEBAR-133 still needs). UI-SIDEBAR-017 is the STOP below.
    //
    // STOP App.Views.Sidebar.SidebarView #8
    // Kind: missing dependency
    // Swift: DroppyCode/App/Views/Sidebar/SidebarView.swift:1682-1872, :2006-2080 SidebarThreadRow's own constructor
    // (B3 52bc792)
    // Requirements: UI-SIDEBAR-017 (the thread row as a whole: its selection, hover, pinned, dragged, settling and
    // hidden-under-glide states, and its own right-click context menu)
    // What is unclear or missing: SidebarThreadRow's own constructor stays MARROW_STUB (SidebarView.cs:2094,
    // unchanged since the file's own skeleton). Every piece this part's UI-SIDEBAR-018 to -022 prove (ThreadBadge,
    // ThreadRowFace's project line, SettleCheck, BuildTrailingOverlay's archive button, BuildFoldOverlay) is one of
    // the row's own collaborators; assembling the row itself additionally needs the drag-styled cluster state
    // (isSelected/isHovering/isDragging/isSettling), RowGlideAnimator's own frame reporting (windowFrame/.glide,
    // named by STOP #7 above, a different unit's own remaining piece: RowGlide.cs) and, for the row's own
    // right-click, the exact same missing Avalonia-ContextMenu-per-open-refresh foundation named in ProjectRow's own
    // header comment (SidebarView.cs) and STOP #7 above: no ED or platform-adaptation entry names it, and
    // RowActionMenuButtons and ThreadActions.MakeContextMenu already exist for a caller that does not yet exist.
    // Readings or options: none attempted; a partial assembly of the row risked an untested or wrong behaviour
    // standing in for a hard feature, which the contract forbids more than an honest gap does.
    // Needed to proceed: RowGlideAnimator's own frame-reporting wiring (a different unit's remaining piece, named at
    // STOP #7); an engineering or platform-adaptation decision naming the Windows/Avalonia foundation for a
    // per-open-refreshed context menu (STOP #7's own need, shared by this row).
    // Done so far: every one of the row's own extractable static and standalone pieces that this part's records ask
    // for is proven directly (ThreadBadge, ThreadRowFace, SettleCheck, BuildTrailingOverlay, BuildFoldOverlay,
    // MergeStage, below and in earlier parts); nothing is claimed for the row's own constructor, and no test names it.

    // ---- helpers for ProjectRow (part 6) ----

    private static Control ShownProjectRow(AppModel model, Project project, long count, bool togglesExpansion = true)
    {
        var row = (Control)Activator.CreateInstance(Private("ProjectRow"), project, count, togglesExpansion)!;
        row.VerticalAlignment = VerticalAlignment.Top; // the row's own 28pt height, not the host's full stretch
        var host = new Border { Child = row };
        AppEnvironment.SetAppModel(host, model);
        var window = new AvaloniaWindow { Content = host, Width = 320, Height = 100 };
        window.Show();
        window.UpdateLayout();
        return row;
    }

    private static ValueList<RowAction> ProjectRowContextMenuActions(Project project, bool isActive, bool hasOverride, AppModel model) =>
        (ValueList<RowAction>)CallStatic(Private("ProjectRow"), "ContextMenuActions", project, isActive, hasOverride, model)!;

    private static ValueList<RowAction> ProjectRowActions(AppModel model, Project project, bool isActive) =>
        (ValueList<RowAction>)CallStatic(Private("ProjectRow"), "Actions", model, project, isActive)!;

    private static object ProjectPairChoices(params (Guid Id, string Title)[] pairs)
    {
        var choiceType = Private("ProjectPairChoice");
        var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(choiceType))!;
        foreach (var (id, title) in pairs)
        {
            var choice = Activator.CreateInstance(choiceType)!;
            SetProp(choice, "Id", id);
            SetProp(choice, "Title", title);
            list.Add(choice);
        }
        return list;
    }

    private static IReadOnlyList<Control> ProjectRowBuildContextMenuItems(Project project, bool isActive, bool hasOverride, AppModel model, object pairs) =>
        (IReadOnlyList<Control>)CallStatic(Private("ProjectRow"), "BuildContextMenuItems", project, isActive, hasOverride, model, pairs)!;

    // ---- PR-SIDEBAR-160: the Archive item ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-160")] // App/Views/Sidebar/SidebarView.swift:2219-2226
    public void Archive_is_always_offered_and_starts_a_group_only_when_settle_is_not_first() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0);
        var helper = NewThread(project, "Helper", 1) with { ParentThreadID = thread.Id };
        var model = Model(harness, [project], [thread, helper]);
        harness.Main.OnMain(() =>
        {
            model.Settings.ThreadFinishAction = ThreadFinishAction.Archive; // settleFirst false for a non-helper
            var archive = ThreadActionsMake(model, thread).Single(a => a.Title == "Archive"); // :2221-2223
            Assert.Equal("archivebox", archive.Symbol);
            Assert.True(archive.StartsGroup); // :2224 !settleFirst
            archive.Action(); // :2223 model.archive(thread.id)
            Assert.True(model.Thread(thread.Id)!.IsArchived);

            model.Settings.ThreadFinishAction = ThreadFinishAction.Settle; // settleFirst true for this non-helper
            var archiveNotFirst = ThreadActionsMake(model, model.Thread(thread.Id)!).Single(a => a.Title == "Archive");
            Assert.False(archiveNotFirst.StartsGroup);

            // A helper always gets Archive (it has no place of its own to settle into: :2219-2220), and settleFirst
            // is false for a helper regardless of the setting, so its Archive item always starts the group.
            var helperArchive = ThreadActionsMake(model, helper).Single(a => a.Title == "Archive");
            Assert.True(helperArchive.StartsGroup); // :2224-2225
        });
    });

    // ---- PR-SIDEBAR-161: the Settle/Reopen item ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-161")] // App/Views/Sidebar/SidebarView.swift:2227-2242
    public void Settle_or_Reopen_is_offered_to_non_helpers_only_and_prefers_the_rows_own_handler() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var open = NewThread(project, "Open", 0);
        var settled = NewThread(project, "Settled", 1) with { IsSettled = true };
        var helper = NewThread(project, "Helper", 2) with { ParentThreadID = open.Id };
        var model = Model(harness, [project], [open, settled, helper]);
        harness.Main.OnMain(() =>
        {
            Assert.DoesNotContain(ThreadActionsMake(model, helper), a => a.Title is "Settle" or "Reopen"); // :2224-2226

            model.Settings.ThreadFinishAction = ThreadFinishAction.Settle; // settleFirst true for a non-helper
            var settle = ThreadActionsMake(model, open).Single(a => a.Title is "Settle" or "Reopen"); // :2228-2231
            Assert.Equal("Settle", settle.Title);
            Assert.Equal("checkmark.circle", settle.Symbol);
            Assert.True(settle.StartsGroup); // :2230 settleFirst

            var reopenItem = ThreadActionsMake(model, settled).Single(a => a.Title is "Settle" or "Reopen");
            Assert.Equal("Reopen", reopenItem.Title);
            Assert.Equal("arrow.uturn.backward", reopenItem.Symbol);

            model.Settings.ThreadFinishAction = ThreadFinishAction.Archive; // settleFirst false now
            var settleNotFirst = ThreadActionsMake(model, open).Single(a => a.Title is "Settle" or "Reopen");
            Assert.False(settleNotFirst.StartsGroup); // :2230

            // The row's own handler, when supplied, flies the glide; without it the model moves the thread directly
            // (:2233-2240).
            var handlerCalled = false;
            var withHandler = ThreadActionsMake(model, open, onSettle: () => handlerCalled = true).Single(a => a.Title == "Settle");
            withHandler.Action();
            Assert.True(handlerCalled);
            Assert.False(model.Thread(open.Id)!.IsSettled); // the handler ran instead of the model call

            var withoutHandler = ThreadActionsMake(model, open).Single(a => a.Title == "Settle");
            withoutHandler.Action(); // :2239 model.settleAnimated(thread.id)
            Assert.True(model.Thread(open.Id)!.IsSettled);

            var reopenHandlerCalled = false;
            var settledWithHandler = ThreadActionsMake(model, settled, onReopen: () => reopenHandlerCalled = true).Single(a => a.Title == "Reopen");
            settledWithHandler.Action();
            Assert.True(reopenHandlerCalled);
            Assert.True(model.Thread(settled.Id)!.IsSettled); // the handler ran instead of the model call

            var settledWithoutHandler = ThreadActionsMake(model, settled).Single(a => a.Title == "Reopen");
            settledWithoutHandler.Action(); // :2236 model.reopenAnimated(thread.id)
            Assert.False(model.Thread(settled.Id)!.IsSettled);
        });
    });

    // ---- PR-SIDEBAR-162: settle/archive swap by the finish-action setting ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-162")] // App/Views/Sidebar/SidebarView.swift:2219-2221, :2225-2227, :2242
    public void Settle_and_archive_swap_places_by_the_finish_action_and_a_helper_gets_archive_alone() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0);
        var helper = NewThread(project, "Helper", 1) with { ParentThreadID = thread.Id };
        var model = Model(harness, [project], [thread, helper]);
        harness.Main.OnMain(() =>
        {
            model.Settings.ThreadFinishAction = ThreadFinishAction.Settle;
            var helperGroup = ThreadActionsMake(model, helper).Where(a => a.Title is "Settle" or "Reopen" or "Archive").ToList();
            Assert.Equal(["Archive"], helperGroup.Select(a => a.Title)); // :2224-2226, no Settle/Reopen at all
            Assert.True(helperGroup[0].StartsGroup);

            var settleFirst = ThreadActionsMake(model, thread).Where(a => a.Title is "Settle" or "Archive").ToList();
            Assert.Equal(["Settle", "Archive"], settleFirst.Select(a => a.Title)); // :2242
            Assert.True(settleFirst[0].StartsGroup);
            Assert.False(settleFirst[1].StartsGroup);

            model.Settings.ThreadFinishAction = ThreadFinishAction.Archive;
            var archiveFirst = ThreadActionsMake(model, thread).Where(a => a.Title is "Settle" or "Archive").ToList();
            Assert.Equal(["Archive", "Settle"], archiveFirst.Select(a => a.Title));
            Assert.True(archiveFirst[0].StartsGroup);
            Assert.False(archiveFirst[1].StartsGroup);
        });
    });

    // ---- PR-SIDEBAR-163: the Delete… item ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-163")] // App/Views/Sidebar/SidebarView.swift:2244-2245, :2309-2312
    public void Delete_is_always_the_last_item_destructive_symbol_trash_and_one_ellipsis_character() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0);
        var helper = NewThread(project, "Helper", 1) with { ParentThreadID = thread.Id };
        var settled = NewThread(project, "Settled", 2) with { IsSettled = true };
        var model = Model(harness, [project], [thread, helper, settled]);
        harness.Main.OnMain(() =>
        {
            foreach (var candidate in new[] { thread, helper, settled })
            {
                var delete = ThreadActionsMake(model, candidate)[^1]; // :2244-2245, unconditional, last
                Assert.Equal("Delete\u2026", delete.Title); // one U+2026, not three periods
                Assert.Equal(new byte[] { 0x44, 0x65, 0x6C, 0x65, 0x74, 0x65, 0xE2, 0x80, 0xA6 }, System.Text.Encoding.UTF8.GetBytes(delete.Title));
                Assert.Equal("trash", delete.Symbol);
                Assert.True(delete.IsDestructive);
                Assert.False(delete.StartsGroup); // sits directly under the settle/archive group, no separator of its own
                Assert.Null(delete.Confirms); // the confirmation is the popover, not this flag
            }

            var deleted = false;
            var madeDirect = ((IEnumerable)CallStatic(Private("ThreadActions"), "Make", model, thread,
                (Action)(() => { }), (Action)(() => deleted = true), null, null)!).Cast<RowAction>().ToList();
            madeDirect[^1].Action(); // :2245 onDelete()
            Assert.True(deleted);

            var requests = Activator.CreateInstance(Private("SidebarMenuRequests"))!;
            var contextDelete = ThreadActionsMakeContextMenu(model, thread, false, requests)[^1];
            Assert.Equal("Delete\u2026", contextDelete.Title); // :2308-2311
            contextDelete.Action();
            Assert.Equal(thread, Get(requests, "Delete")); // :2309-2310 requests.delete = thread
        });
    });

    // ---- UI-SIDEBAR-004: the activity-view bell's colours ----

    [Fact]
    [Trait("Req", "UI-SIDEBAR-004")] // App/Views/Sidebar/SidebarView.swift:2316-2343
    public void The_bell_glyph_and_background_colours_follow_the_on_off_and_hovered_while_off_states()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var manual = new ManualFrameSource();
            using var frames = FrameClock.Source.Override(manual);
            var model = harness.MakeModel();
            SidebarView view = null!;
            harness.Main.OnMain(() => view = Attached(model));
            var window = WindowOf(view);
            var glyph = LogicalQuery.SelfAndDescendants<SwiftImage>(view).Single(image => image.SymbolName == "bell"); // :2325-2326
            var circle = LogicalQuery.SelfAndDescendants<Avalonia.Controls.Shapes.Ellipse>(view).Single(e => e.Width == 30); // :2329-2332
            Color GlyphColor() => ((ISolidColorBrush)glyph.Foreground!).Color;
            Color FillColor() => ((ISolidColorBrush)circle.Fill!).Color;
            var button = ButtonNamed(view, "Activity view");
            var inside = button.TranslatePoint(new Point(15, 15), window)!.Value;
            var away = new Point(1, 1);
            void Settle() { for (var i = 0; i < 20; i++) { harness.Main.Advance(TimeSpan.FromMilliseconds(16)); manual.Advance(TimeSpan.FromMilliseconds(16)); } }

            harness.Main.OnMain(() =>
            {
                // Off, not hovering: secondaryText glyph, Color.clear background, set with no animation at construction (:2327, :2331).
                Assert.Equal(Chrome.SecondaryText.Resolve(glyph.ActualThemeVariant), GlyphColor());
                Assert.Equal(SwiftColor.Clear.Resolve(circle.ActualThemeVariant), FillColor());
            });

            harness.Main.OnMain(() => window.MouseMove(inside)); // pointer input reaches MainActorModel.Set; needs the main actor
            Settle();
            harness.Main.OnMain(() =>
            {
                // Hovered, still off: the label colour's own 0.08 overlay, glyph unchanged (:2331).
                Assert.Equal(Chrome.SecondaryText.Resolve(glyph.ActualThemeVariant), GlyphColor());
                Assert.Equal(Chrome.Overlay(0.08).Resolve(circle.ActualThemeVariant), FillColor());
            });

            harness.Main.OnMain(() => window.MouseMove(away));
            Settle();
            harness.Main.OnMain(() => Assert.Equal(SwiftColor.Clear.Resolve(circle.ActualThemeVariant), FillColor()));

            harness.Main.OnMain(() => Click(button)); // on
            Settle();
            harness.Main.OnMain(() =>
            {
                // On: accent glyph, accent at 0.16 background (:2327, :2331).
                Assert.Equal(Chrome.Accent.Resolve(glyph.ActualThemeVariant), GlyphColor());
                Assert.Equal(Chrome.Accent.Opacity(0.16).Resolve(circle.ActualThemeVariant), FillColor());
            });
            Close(view);
        });
    }

    // ---- UI-SIDEBAR-005: the scrolling list's own geometry and its list-wide drag gesture ----

    [Fact]
    [Trait("Req", "UI-SIDEBAR-005")] // App/Views/Sidebar/SidebarView.swift:101-148
    public void The_list_has_no_scroll_indicators_10_12_10_8_padding_and_1pt_spacing()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            using var frames = FrameClock.Source.Override(new ManualFrameSource()); // the row's own insertion transition (:117-119) needs a bound clock
            var alpha = NewProject(harness, "Alpha") with { IsExpanded = false };
            var model = Model(harness, [alpha], []);
            SidebarView view = null!;
            harness.Main.OnMain(() => view = Attached(model));

            var scroll = LogicalQuery.SelfAndDescendants<ScrollViewer>(view).Single();
            Assert.Equal(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility); // :102, :139 .scrollIndicators(.never)
            Assert.Equal(ScrollBarVisibility.Hidden, scroll.VerticalScrollBarVisibility);

            var list = ListStack(view);
            Assert.Equal(Orientation.Vertical, list.Orientation); // :103 LazyVStack(alignment: .leading, spacing: 1)
            Assert.Equal(1d, list.Spacing);
            Assert.Equal(new Thickness(Chrome.ListInset, 12, Chrome.ListInset, 8), list.Margin); // :120-122
            Assert.Null(list.Background); // rows supply their own fills

            // The list's own DragGesture, attached ahead of every row's own click, and its query-empty gating
            // (:123-137, :129, :776) are exercised through real presses on a bare (non-SidebarRow) row by the
            // reorder tests above (part 2), so this test does not repeat that interaction here.
            Close(view);
        });
    }

    // ---- UI-SIDEBAR-021: the archive button's tooltip and click ----

    [Fact]
    [Trait("Req", "UI-SIDEBAR-021")] // App/Views/Sidebar/SidebarView.swift:1775-1784
    public void The_archive_button_is_tooltipped_Archive_thread_and_its_click_runs_the_supplied_action() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Thread", 0);
        var type = Private("SidebarThreadRow");
        var actions = new List<RowAction> { new() { Title = "Rename", Action = () => { } } };
        var isMenuPresented = new Binding<bool>(() => false, _ => { });
        var archived = false;
        var overlay = Shown((Control)CallStatic(type, "BuildTrailingOverlay",
            thread, true, false, false, false, false, (Action)(() => archived = true), actions, isMenuPresented)!);
        var archive = LogicalQuery.SelfAndDescendants<Button>(overlay).First(); // :1785, ahead of the ellipsis
        Assert.Equal("Archive thread", ToolTip.GetTip(archive)); // :1783
        Click(archive);
        Assert.True(archived); // :1777-1782 Button(action: archiveAction)
    });

    // ---- UI-SIDEBAR-008, -009, -010: ProjectRow's own body ----

    [Fact]
    [Trait("Req", "UI-SIDEBAR-008")] // App/Views/Sidebar/SidebarView.swift:1078-1166; UI/Chrome/Chrome.swift:681-746
    public void The_project_row_toggles_expansion_unless_disabled_and_dims_to_0_55_while_inactive() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var model = Model(harness, [project], []);
        Control row = null!;
        harness.Main.OnMain(() =>
        {
            row = ShownProjectRow(model, project, 0);
            Assert.Equal(28d, row.Bounds.Height); // :1099 accessoryWidth into SidebarRow's own Chrome.rowHeight row
            Assert.Equal(1d, row.Opacity); // :1135, active by default
            Assert.True(model.Project(project.Id)!.IsExpanded); // Project.IsExpanded defaults true (Library.cs:86)
            Click(ButtonNamed(row, "Alpha")); // :1100-1105, :738 the button's own automation name is the title
            Assert.False(model.Project(project.Id)!.IsExpanded);
        });

        harness.Main.OnMain(() =>
        {
            var neverToggles = ShownProjectRow(model, project, 0, togglesExpansion: false);
            Click(ButtonNamed(neverToggles, "Alpha")); // :1101 guard togglesExpansion else return
            Assert.False(model.Project(project.Id)!.IsExpanded); // unchanged
        });

        harness.Main.OnMain(() => model.Settings.SetProjectActive(false, project.Id));
        harness.Main.OnMain(() => Assert.Equal(0.55d, row.Opacity)); // :1135, a later pass, not a rebuild
        harness.Main.OnMain(() => model.Settings.SetProjectActive(true, project.Id));
        harness.Main.OnMain(() => Assert.Equal(1d, row.Opacity));
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-009")] // App/Views/Sidebar/SidebarView.swift:1127-1132, :521-523
    public void The_collapsed_count_label_shows_only_while_collapsed_with_a_positive_count() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha") with { IsExpanded = false };
        var model = Model(harness, [project], []);
        harness.Main.OnMain(() =>
        {
            var row = ShownProjectRow(model, project, 3);
            var count = LogicalQuery.SelfAndDescendants<TextBlock>(row).Single(t => t.Text == "3"); // :1128, a bare decimal
            Assert.Equal(Chrome.SecondaryText.Resolve(count.ActualThemeVariant), ((ISolidColorBrush)count.Foreground!).Color); // :1130
            var wrapper = Assert.IsType<Border>(count.Parent); // SidebarRow (Chrome.cs) sets its own Margin on the returned
            Assert.Equal(new Thickness(0, 0, 4, 0), wrapper.Padding); // control unconditionally, so :1131's own padding nests as this Padding
        });

        // ProjectRow's own accessory reads the model's live project (this constructor's own header comment), not the
        // constructor argument, once attached; a row built after the model is expanded reflects that, not the
        // snapshot passed in.
        harness.Main.OnMain(() => model.UpdateProject(project.Id, p => p with { IsExpanded = true }));
        harness.Main.OnMain(() =>
        {
            var expandedRow = ShownProjectRow(model, project, 3);
            Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<TextBlock>(expandedRow), t => t.Text == "3"); // :1127 !isExpanded
        });

        harness.Main.OnMain(() => model.UpdateProject(project.Id, p => p with { IsExpanded = false }));
        harness.Main.OnMain(() =>
        {
            var zeroRow = ShownProjectRow(model, project, 0);
            Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<TextBlock>(zeroRow), t => t.Text == "0"); // :1127 count > 0
        });
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-010")] // App/Views/Sidebar/SidebarView.swift:1119-1125
    public void The_plus_button_shows_only_while_hovered_and_creates_a_thread_with_an_untruncated_tooltip() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource()); // the hover fill's own AnimatedColor needs a bound clock
        var project = NewProject(harness, "Alpha Beta Gamma Delta Epsilon"); // long enough to show a truncated tooltip if one happened
        var model = Model(harness, [project], []);
        Control row = null!;
        AvaloniaWindow window = null!;
        harness.Main.OnMain(() =>
        {
            row = ShownProjectRow(model, project, 0);
            window = WindowOf(row);
            Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<Button>(row),
                b => ToolTip.GetTip(b) is string tip && tip.StartsWith("New thread in", StringComparison.Ordinal)); // :1116, not hovering
        });
        harness.Main.OnMain(() => window.MouseMove(row.TranslatePoint(new Point(10, 10), window)!.Value)); // needs the main actor (MainActorModel.Set)
        harness.Main.OnMain(() =>
        {
            var plus = LogicalQuery.SelfAndDescendants<Button>(row)
                .Single(b => ToolTip.GetTip(b) as string == "New thread in Alpha Beta Gamma Delta Epsilon"); // :1125, untruncated
            Assert.Empty(model.ThreadsIn(project));
            Click(plus); // :1119-1123
            Assert.Single(model.ThreadsIn(project)); // :1120 model.newThread(in: project)
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-127")] // App/Views/Sidebar/SidebarView.swift:1085, :1106-1114 (REVIEW FIX, part 6)
    public void The_icon_glyph_follows_the_live_project_icon_without_rebuilding_the_row() => headless.OnUiThread(() =>
    {
        // REVIEW FIX: :1085 `project` is a computed property Swift's body re-reads on every render, so :1106-1114's
        // icon glyph always reflects the live project.icon, changed live from Settings' icon picker
        // (SettingsView.swift:1152). The initial part 6 implementation built the glyph once, inside SidebarRow's
        // own constructor (icon(), Chrome.cs:1079) and SidebarIconBadge's own constructor (glyph(), Chrome.cs:1044),
        // synchronously before this class's own Track was ever installed, so a later icon change never reached the
        // already-built row. Verified this fails (the glyph stays "folder.fill"/no TextBlock) against the pre-fix
        // code and passes against ReplaceGlyph.
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha"); // no icon yet
        var model = Model(harness, [project], []);
        Control row = null!;
        harness.Main.OnMain(() =>
        {
            row = ShownProjectRow(model, project, 0);
            var fallback = LogicalQuery.SelfAndDescendants<SwiftImage>(row).Single(i => i.SymbolName is not null);
            Assert.Equal("folder.fill", fallback.SymbolName); // :1111 SidebarSymbol("folder.fill"), no icon picked
        });

        harness.Main.OnMain(() => model.UpdateProject(project.Id, p => p with { Icon = new ProjectIcon.Symbol("star.fill") })); // :1108-1109, a picked symbol
        harness.Main.OnMain(() =>
        {
            var symbol = LogicalQuery.SelfAndDescendants<SwiftImage>(row).Single(i => i.SymbolName is not null); // same row instance, not a fresh one
            Assert.Equal("star.fill", symbol.SymbolName); // no longer the folder.fill fallback
        });

        harness.Main.OnMain(() => model.UpdateProject(project.Id, p => p with { Icon = new ProjectIcon.Emoji("\U0001F680") })); // :1108-1109, an emoji icon
        harness.Main.OnMain(() =>
        {
            Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<SwiftImage>(row), i => i.SymbolName is not null); // the symbol glyph is gone entirely
            var emoji = LogicalQuery.SelfAndDescendants<TextBlock>(row).Single(t => t.Text == "\U0001F680"); // :18 Text(verbatim: emoji)
            Assert.NotNull(emoji);
        });

        harness.Main.OnMain(() => model.UpdateProject(project.Id, p => p with { Icon = null })); // the icon is cleared again
        harness.Main.OnMain(() =>
        {
            var backToFallback = LogicalQuery.SelfAndDescendants<SwiftImage>(row).Single(i => i.SymbolName is not null);
            Assert.Equal("folder.fill", backToFallback.SymbolName); // :1111, back to the fallback
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-128")] // App/Views/Sidebar/SidebarView.swift:1091, :1115-1133 (REVIEW FIX, part 6)
    public void The_hover_accessory_collapses_the_instant_the_ellipsis_menu_closes_even_while_not_hovering() => headless.OnUiThread(() =>
    {
        // REVIEW FIX: any write to :1091 `isMenuPresented` is a SwiftUI @State change that re-evaluates the whole
        // body, including :1115-1133's accessory branch (`hovering || isMenuPresented`), with or without a
        // concurrent hover change. The initial part 6 implementation only re-invoked the accessory closure from
        // SidebarRow's own hover Track (Chrome.cs:1120-1128), keyed on ITS OWN state.IsHovering; a change to this
        // row's own isMenuPresented alone (e.g. the ellipsis popover closing while the pointer is not back over the
        // row) registered no dependency there, so the ellipsis+plus cluster stayed on screen indefinitely instead
        // of reverting to the collapsed (here, empty: :1127 count == 0) state. Verified this fails (the cluster
        // survives, the DoesNotContain assertions below fail) against the pre-fix code and passes against the added
        // isMenuPresented Track. count is 0 (rather than a positive collapsed count, already covered independently
        // by UI-SIDEBAR-009) so the collapsed branch is the plain EmptyView Border (:1115-1133's own last branch),
        // not a fresh MonospacedDigit TextBlock: this assembly's headless Skia text-shaper (confirmed pre-existing,
        // unrelated to this fix: tests/Marrow.App.Tests/Fixtures/HeadlessFixtures.cs's own WarmFonts/CapHeight
        // gaps) can throw "Could not create glyphTypeface" the first time a fresh glyph run is measured right after
        // an older control carrying the same weight is torn out of the same overlay in the same pass, which the
        // ellipsis/plus cluster's own removal below would otherwise also trigger a second time for that TextBlock.
        using var harness = new AppModelHarness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource()); // the hover fill's own AnimatedColor needs a bound clock
        using var presenter = Presentation.Presenter.Override(new RecordingPresenter()); // Avalonia 12.1.2 headless cannot open a real Popup (Port/ViewLifetime.cs); the ellipsis click below only needs isMenuPresented to flip, not a real popover
        var project = NewProject(harness, "Alpha") with { IsExpanded = false };
        var model = Model(harness, [project], []);
        Control row = null!;
        AvaloniaWindow window = null!;
        harness.Main.OnMain(() =>
        {
            row = ShownProjectRow(model, project, 0); // :1127 count == 0, see this test's own header comment
            window = WindowOf(row);
        });
        harness.Main.OnMain(() => window.MouseMove(row.TranslatePoint(new Point(10, 10), window)!.Value)); // :1116 hovering
        Button ellipsis = null!;
        harness.Main.OnMain(() =>
        {
            ellipsis = LogicalQuery.SelfAndDescendants<Button>(row).Single(b => ToolTip.GetTip(b) as string == "More"); // Popovers.cs :396
            Click(ellipsis); // :391 isPresented.toggle() -> isMenuPresented = true
        });
        harness.Main.OnMain(() => window.MouseMove(new Point(1, 1))); // pointer leaves the row; :1116 hovering false, isMenuPresented still true
        harness.Main.OnMain(() =>
        {
            Assert.Contains(LogicalQuery.SelfAndDescendants<Button>(row), b => ToolTip.GetTip(b) as string == "More"); // :1116 hovering || isMenuPresented, still shown
            Click(ellipsis); // closes the popover the same way a menu item's dismiss() does (Popovers.cs SetClosePopover): isMenuPresented = false, pointer still away
        });
        harness.Main.OnMain(() =>
        {
            Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<Button>(row), b => ToolTip.GetTip(b) as string == "More"); // :1127 the ellipsis must collapse
            Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<Button>(row), b => ToolTip.GetTip(b) as string == "New thread in Alpha"); // :1127 the plus button collapses with it
        });
    });

    // ---- UI-SIDEBAR-014: the project menu's items (ellipsis popover and context menu) ----

    [Fact]
    [Trait("Req", "UI-SIDEBAR-014")] // App/Views/Sidebar/SidebarView.swift:1168-1229
    public void The_project_menu_offers_new_thread_worktree_reveal_remove_and_the_activation_item_in_order() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var model = Model(harness, [project], []);
        harness.Main.OnMain(() =>
        {
            foreach (var made in new[]
            {
                ProjectRowActions(model, project, isActive: true).ToList(),
                ProjectRowContextMenuActions(project, true, false, model).ToList(),
            })
            {
                Assert.Equal(
                    ["New thread", "New thread in worktree", "Reveal in File Explorer", "Remove project", "Deactivate project"],
                    made.Select(a => a.Title));
                Assert.Equal(["square.and.pencil", "square.stack.3d.up", "folder", "trash", "power"], made.Select(a => a.Symbol));
                Assert.True(made[3].IsDestructive); // Remove project, :1183/:1212
                Assert.True(made[2].StartsGroup); // Reveal in File Explorer, :1179/:1211
                Assert.True(made[3].StartsGroup); // Remove project, its own separator
                Assert.True(made[4].StartsGroup); // the activation item, :1188/:1216
            }

            Assert.Equal("Activate project", ProjectRowActions(model, project, isActive: false).Last().Title); // :1189/:1217
            Assert.Equal("Activate project", ProjectRowContextMenuActions(project, false, false, model).Last().Title);

            // hasOverride adds "Follow global setting" last, symbol arrow.uturn.backward, no separator (:1196-1200/:1223-1226).
            var overriddenMenu = ProjectRowContextMenuActions(project, true, true, model);
            Assert.Equal("Follow global setting", overriddenMenu.Last().Title);
            Assert.Equal("arrow.uturn.backward", overriddenMenu.Last().Symbol);
            Assert.False(overriddenMenu.Last().StartsGroup);
            Assert.Equal(6, overriddenMenu.Count);

            // Actions(isActive:) reads the override off the live model (:1223), unlike ContextMenuActions' own param.
            model.Settings.SetProjectActive(false, project.Id); // creates a per-project override
            var isActive = model.Settings.IsProjectActive(project.Id);
            var overriddenActions = ProjectRowActions(model, project, isActive);
            Assert.True(model.Settings.HasProjectActivationOverride(project.Id));
            Assert.Equal("Follow global setting", overriddenActions.Last().Title);

            // The action itself calls through to the model (Reveal in File Explorer and Remove project are exercised
            // structurally above; New thread is exercised directly here as the cheapest observable one).
            Assert.Empty(model.ThreadsIn(project));
            ProjectRowActions(model, project, isActive: true)[0].Action(); // :1207 model.newThread(in: project)
            Assert.Single(model.ThreadsIn(project));
        });
    });

    // ---- UI-SIDEBAR-015, -016: the pair submenu and its items ----

    [Fact]
    [Trait("Req", "UI-SIDEBAR-015")] // App/Views/Sidebar/SidebarView.swift:1140-1165, :1149-1161
    public void The_start_new_chats_on_submenu_appears_only_when_there_is_a_hydra_pair_to_choose() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var model = Model(harness, [project], []);
        harness.Main.OnMain(() =>
        {
            var none = ProjectRowBuildContextMenuItems(project, true, false, model, ProjectPairChoices());
            Assert.DoesNotContain(none, c => c is MenuItem item && item.Header is StackPanel panel
                && panel.Children.OfType<TextBlock>().Any(t => t.Text == "Start new chats on")); // :1154 pairsSnapshot.isEmpty

            var withPairs = ProjectRowBuildContextMenuItems(project, true, false, model, ProjectPairChoices((Guid.NewGuid(), "Claude, GPT")));
            Assert.IsType<Separator>(withPairs[^2]); // :1155
            var submenu = Assert.IsType<MenuItem>(withPairs[^1]); // :1156-1160
            var header = Assert.IsType<StackPanel>(submenu.Header);
            Assert.Contains(header.Children, c => c is SwiftImage img && img.SymbolName == "point.3.connected.trianglepath.dotted"); // OD-0010
            Assert.Contains(header.Children, c => c is TextBlock t && t.Text == "Start new chats on"); // :1159
            Assert.Equal(3, submenu.Items.Count); // :1157 ProjectPairMenuItems: the clearing item, a divider, one pair
        });
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-016")] // App/Views/Sidebar/SidebarView.swift:1241-1270
    public void The_pair_submenu_lists_the_clearing_item_then_each_pair_with_a_checkmark_on_the_current_choice() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var model = Model(harness, [project], []);
        var firstID = Guid.NewGuid();
        var secondID = Guid.NewGuid();
        harness.Main.OnMain(() =>
        {
            var pairs = ProjectPairChoices((firstID, "Claude, GPT"), (secondID, "Gemini, Grok"));

            var unselected = Activator.CreateInstance(Private("ProjectPairMenuItems"), project.Id, (Guid?)null, pairs, model)!;
            var items = (IReadOnlyList<Control>)Get(unselected, "Items")!;
            Assert.Equal(4, items.Count); // the clearing item, a divider, then one item per pair (:1248-1267)

            var clearing = Assert.IsType<MenuItem>(items[0]);
            var clearingHeader = Assert.IsType<StackPanel>(clearing.Header); // :1250-1255, selectedID nil -> checked
            Assert.Contains(clearingHeader.Children, c => c is SwiftImage img && img.SymbolName == "checkmark");
            Assert.Contains(clearingHeader.Children, c => c is TextBlock t && t.Text == "The chat's last pair");

            Assert.IsType<Separator>(items[1]); // :1257

            var first = Assert.IsType<MenuItem>(items[2]);
            var firstText = Assert.IsType<TextBlock>(first.Header); // :1254, not selected: plain Text
            Assert.Equal("Claude, GPT", firstText.Text);

            var second = Assert.IsType<MenuItem>(items[3]);
            Assert.Equal("Gemini, Grok", Assert.IsType<TextBlock>(second.Header).Text);

            ((MenuItem)items[2]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); // :1259-1260
            Assert.Equal(firstID, model.Project(project.Id)!.HydraPairID);

            var selectedSecond = Activator.CreateInstance(Private("ProjectPairMenuItems"), project.Id, (Guid?)secondID, pairs, model)!;
            var selectedItems = (IReadOnlyList<Control>)Get(selectedSecond, "Items")!;
            var secondSelected = Assert.IsType<MenuItem>(selectedItems[3]);
            var secondHeader = Assert.IsType<StackPanel>(secondSelected.Header); // :1262-1266, selectedID == pair.id -> checked
            Assert.Contains(secondHeader.Children, c => c is SwiftImage img && img.SymbolName == "checkmark");
            Assert.Contains(secondHeader.Children, c => c is TextBlock t && t.Text == "Gemini, Grok");

            ((MenuItem)selectedItems[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); // :1248-1249, clears
            Assert.Null(model.Project(project.Id)!.HydraPairID);
        });
    });

    // ======== Part 7 of 8 ========
    //
    // Records: UI-SIDEBAR-023, -024, -025, -026, -027, -031, -032, -034, -035, -039, -040, -041, -042, -043, -044,
    // -059 (16 of this part's 24) are proven by adding this part's own [Trait] to a test parts 1-6 already wrote for
    // the identical evidence (grep the id to find the added trait; the sites are RelativeTimeLabel, ActivityStatus,
    // ThreadActions.Make/MakeContextMenu, DeleteThreadPopover, ArchiveThreadPopover, HelperConnector, HelperStubRow,
    // ActivityHeader, SettledHeader, NoResults, the Add project/Settings rows and folder chooser, and the sidebarRow
    // transition). UI-SIDEBAR-058's own gap/project-row/thread-row sequence is proven the same way; the project
    // row's own inertness-but-live-accessories in a search group is new below, and the thread rows' project form and
    // disabled reordering stay open under STOP #8 (SidebarThreadRow's own constructor, unchanged from part 6).
    // SidebarHelperRow's own constructor (UI-SIDEBAR-033) and HeadCardsRow's own constructor (UI-SIDEBAR-036, -037,
    // -038; the same site as PR-SIDEBAR-121, -122, now also proven except their own context menus) are new this
    // part, assembled from pieces that were still missing when STOP #7 was written (RowGlideAnimator.Shared.IsHiding
    // and the per-row hover/menu @State pattern, both landed by parts 5 and 6). Each row's own right-click
    // .contextMenu is left out: STOP #7 below is narrowed to name it explicitly for these two rows, rather than
    // resolved, since the same missing per-open-refresh ED/PA foundation still blocks it. UI-SIDEBAR-028, -029, -030
    // extend STOP #6 (part 4): the rename popover's TextField/Button chrome is still undecided.

    [Fact]
    [Trait("Req", "UI-SIDEBAR-033")] // App/Views/Sidebar/SidebarView.swift:1317-1425
    public void A_helper_row_shows_its_head_glyph_selects_on_click_and_swaps_its_overlay_on_hover() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource()); // the hover fill's own AnimatedColor needs a bound clock
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var head = HeadHelper(project, parent.Id, 0, 1) with { HasUnread = true };
        var model = Model(harness, [project], [parent, head]);
        Control row = null!;
        AvaloniaWindow window = null!;
        Panel overlayHost = null!;
        Button selectButton = null!;
        harness.Main.OnMain(() =>
        {
            var requests = Activator.CreateInstance(Private("SidebarMenuRequests"))!;
            row = (Control)Activator.CreateInstance(Private("SidebarHelperRow"),
                head, true, requests, (Action)(() => { }), (Action)(() => { }), (Action)(() => { }))!;
            row.VerticalAlignment = VerticalAlignment.Top; // the row's own 24pt height, not the host's full stretch
            var host = Hosted(model, row);
            window = WindowOf(host);
            window.UpdateLayout();

            var grid = Assert.IsType<Grid>(Get(row, "Child"));
            var connector = grid.Children[0];
            Assert.Equal(ThreadRowMetrics.ConnectorWidth, connector.Width); // :1339, this row's own connector column
            overlayHost = Assert.IsType<Panel>(grid.Children[1]);
            selectButton = Assert.IsType<Button>(overlayHost.Children[0]);

            Assert.NotEmpty(LogicalQuery.SelfAndDescendants<HydraGlyph>(selectButton)); // :1354-1357 a head keeps its glyph
            var title = LogicalQuery.SelfAndDescendants<TextBlock>(selectButton).Single(t => t.Text == head.Title); // :1359
            Assert.Equal(FontWeight.Medium, title.FontWeight); // :1361, hasUnread
            Assert.Equal(Chrome.PrimaryText.Opacity(0.8).Resolve(title.ActualThemeVariant), ((ISolidColorBrush)title.Foreground!).Color); // :1362, not selected

            Assert.IsType(Private("ActivityStatus"), overlayHost.Children[1]); // :1380, not hovering, no menu open

            Assert.Null(model.SelectedThreadID);
            Click(selectButton); // :1348-1350
            Assert.Equal(head.Id, model.SelectedThreadID);
        });
        harness.Main.OnMain(() => window.MouseMove(selectButton.TranslatePoint(new Point(10, 10), window)!.Value)); // :1401-1404 .onHover, pumps the posted Track update on return
        harness.Main.OnMain(() =>
        {
            Assert.IsType<RowActionsButton>(overlayHost.Children[1]); // :1376 showsActions -> the ellipsis
            window.Close();
        });
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-033")] // App/Views/Sidebar/SidebarView.swift:1348-1409
    public void A_selected_helper_rows_title_is_medium_weight_at_0_96_with_the_selection_fill() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var helper = NewThread(project, "Helper", 1) with { ParentThreadID = parent.Id };
        var model = Model(harness, [project], [parent, helper]);
        harness.Main.OnMain(() =>
        {
            model.SelectedThreadID = helper.Id; // :1340 model.isSelected(thread.id)
            var requests = Activator.CreateInstance(Private("SidebarMenuRequests"))!;
            var row = (Control)Activator.CreateInstance(Private("SidebarHelperRow"),
                helper, false, requests, (Action)(() => { }), (Action)(() => { }), (Action)(() => { }))!;
            var host = Hosted(model, row);
            WindowOf(host).UpdateLayout();

            var grid = Assert.IsType<Grid>(Get(row, "Child"));
            var overlayHost = Assert.IsType<Panel>(grid.Children[1]);
            var selectButton = Assert.IsType<Button>(overlayHost.Children[0]);
            var title = LogicalQuery.SelfAndDescendants<TextBlock>(selectButton).Single(t => t.Text == helper.Title);
            Assert.Equal(FontWeight.Medium, title.FontWeight); // :1361, isSelected
            Assert.Equal(Chrome.PrimaryText.Opacity(0.96).Resolve(title.ActualThemeVariant), ((ISolidColorBrush)title.Foreground!).Color); // :1362
            Assert.Empty(LogicalQuery.SelfAndDescendants<HydraGlyph>(selectButton)); // :1354, not a head
            WindowOf(host).Close();
        });
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-036")] // App/Views/Sidebar/SidebarView.swift:1427-1548
    [Trait("Req", "UI-SIDEBAR-037")] // App/Views/Sidebar/SidebarView.swift:1472-1507
    [Trait("Req", "PR-SIDEBAR-121")] // App/Views/Sidebar/SidebarView.swift:1472-1507 (B2 id, same site; the right-click
    // .contextMenu at :1490-1500 is not attached -- STOP #7 below)
    [Trait("Req", "PR-SIDEBAR-122")] // App/Views/Sidebar/SidebarView.swift:1440-1551
    public void A_head_card_selects_a_head_on_click_and_rings_the_selected_glyph() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        // :1010 OrderedHeads sorts newest createdAt first among finished heads, so headA (the one selected below)
        // is given the later minute to land at index 0.
        var headB = HeadHelper(project, parent.Id, 1, 1);
        var headA = HeadHelper(project, parent.Id, 0, 2);
        var model = Model(harness, [project], [parent, headA, headB]);
        harness.Main.OnMain(() => model.SelectedThreadID = headA.Id); // :1479 model.isSelected(head.id)
        harness.Main.OnMain(() =>
        {
            var requests = Activator.CreateInstance(Private("SidebarMenuRequests"))!;
            var card = (Control)Activator.CreateInstance(Private("HeadCardsRow"),
                parent, new[] { headA, headB }, requests, (Action)(() => { }))!;
            var host = Hosted(model, card);
            var window = WindowOf(host);
            window.UpdateLayout(); // :1544 onGeometryChange(width) fires from the real layout pass

            var buttons = LogicalQuery.SelfAndDescendants<Button>(card)
                .Where(b => LogicalQuery.SelfAndDescendants<HydraGlyph>(b).Any()).ToList();
            Assert.Equal(2, buttons.Count); // :1466-1509 one glyph button per head

            var selectedGlyph = LogicalQuery.SelfAndDescendants<HydraGlyph>(buttons[0]).Single();
            var selectedHost = (Panel)selectedGlyph.Parent!;
            Assert.Equal(2, selectedHost.Children.Count); // :1479-1483 the disc sits behind the selected head's glyph
            var disc = Assert.IsType<Avalonia.Controls.Shapes.Ellipse>(selectedHost.Children[0]);
            Assert.Equal(ThreadRowMetrics.HeadGlyphSize + 6, disc.Width); // :1481 padding(-3) each side
            Assert.Equal(Chrome.Overlay(0.14).Resolve(disc.ActualThemeVariant), ((ISolidColorBrush)disc.Fill!).Color);

            var unselectedGlyph = LogicalQuery.SelfAndDescendants<HydraGlyph>(buttons[1]).Single();
            Assert.Single(((Panel)unselectedGlyph.Parent!).Children); // :1479, headB is not selected: no disc

            Assert.Equal(headA.Id, model.SelectedThreadID);
            Click(buttons[1]); // :1473-1475
            Assert.Equal(headB.Id, model.SelectedThreadID);
            window.Close();
        });
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-037")] // App/Views/Sidebar/SidebarView.swift:1484-1499
    public void A_head_glyph_scales_1_18_on_hover_and_carries_the_dotted_tooltip_and_comma_label() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual); // the hover scale animates (Chrome.hover)
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var head = HeadHelper(project, parent.Id, 0, 1);
        var model = Model(harness, [project], [parent, head]);
        AvaloniaWindow window = null!;
        Control card = null!;
        // The card's own width arrives through onGeometryChange (:1544) and can settle in more than one layout
        // pass, rebuilding the row's controls each time (ApplyEntries' own diff does not reach inside this card);
        // re-querying by the glyph's own HydraGlyph, rather than holding a captured Button/ScaleTransform, survives
        // that rebuild the way ChevronOf() does below for the chevron.
        Button HeadButton() => (Button)LogicalQuery.SelfAndDescendants<HydraGlyph>(card).Single().Parent!.Parent!;
        ScaleTransform Scale() => (ScaleTransform)((Panel)LogicalQuery.SelfAndDescendants<HydraGlyph>(card).Single().Parent!).RenderTransform!;
        harness.Main.OnMain(() =>
        {
            var requests = Activator.CreateInstance(Private("SidebarMenuRequests"))!;
            card = (Control)Activator.CreateInstance(Private("HeadCardsRow"),
                parent, new[] { head }, requests, (Action)(() => { }))!;
            card.VerticalAlignment = VerticalAlignment.Top; // the card's own measured height, not the host's full stretch
            var host = Hosted(model, card);
            window = WindowOf(host);
            window.UpdateLayout();

            var button = HeadButton();
            Assert.Equal($"{head.Hydra!.Persona.Name} · {head.Hydra!.Task}", ToolTip.GetTip(button)); // :1490
            Assert.Equal($"{head.Hydra!.Persona.Name}, {head.Hydra!.Task}", AutomationProperties.GetName(button)); // :1499
            Assert.Equal(1d, Scale().ScaleX);
        });
        harness.Main.OnMain(() => window.MouseMove(HeadButton().TranslatePoint(new Point(7, 7), window)!.Value)); // :1486-1488 .onHover, pumps the posted Track update on return
        for (var i = 0; i < 20; i++) { harness.Main.Advance(TimeSpan.FromMilliseconds(16)); manual.Advance(TimeSpan.FromMilliseconds(16)); }
        harness.Main.OnMain(() =>
        {
            Assert.Equal(1.18, Scale().ScaleX, 3); // :1484
            window.Close();
        });
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-038")] // App/Views/Sidebar/SidebarView.swift:1514-1526
    public void The_head_cards_chevron_toggles_all_heads_with_the_right_glyph_and_tooltip_and_snappy_animation() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var heads = Enumerable.Range(0, 13).Select(i => HeadHelper(project, parent.Id, i, i + 1)).ToList();
        var model = Model(harness, [project, ], heads.Prepend(parent).ToList());
        AvaloniaWindow window = null!;
        Control card = null!;
        Button ChevronOf() => LogicalQuery.SelfAndDescendants<Button>(card)
            .Single(b => LogicalQuery.SelfAndDescendants<SwiftImage>(b).Any(i => i.SymbolName?.StartsWith("chevron", StringComparison.Ordinal) == true));
        harness.Main.OnMain(() =>
        {
            var requests = Activator.CreateInstance(Private("SidebarMenuRequests"))!;
            card = (Control)Activator.CreateInstance(Private("HeadCardsRow"),
                parent, heads, requests, (Action)(() => { }))!;
            var host = Hosted(model, card);
            window = WindowOf(host);
            window.UpdateLayout();

            var chevron = ChevronOf();
            var glyph = LogicalQuery.SelfAndDescendants<SwiftImage>(chevron).Single();
            Assert.Equal("chevron.down", glyph.SymbolName); // :1516, collapsed: 13 > 12
            Assert.Equal("Show all 13 heads", ToolTip.GetTip(chevron)); // :1526

            Click(chevron); // :1515-1516, .snappy(duration: 0.18)
        });
        // A separate harness.Main.OnMain call, as SettledHeader's own regression does: OnMain pumps the manual loop
        // to idle on return, which is what runs Tracked<T>'s MainActor.Post(Update) after the click's mutation, not
        // Dispatcher.UIThread.RunJobs (a different queue).
        harness.Main.OnMain(() =>
        {
            var buttons = LogicalQuery.SelfAndDescendants<Button>(card).Where(b => LogicalQuery.SelfAndDescendants<HydraGlyph>(b).Any()).ToList();
            Assert.Equal(13, buttons.Count); // :1443 showsAllHeads -> every head

            var expandedChevron = ChevronOf();
            var expandedGlyph = LogicalQuery.SelfAndDescendants<SwiftImage>(expandedChevron).Single();
            Assert.Equal("chevron.up", expandedGlyph.SymbolName); // :1516
            Assert.Equal("Show fewer heads", ToolTip.GetTip(expandedChevron)); // :1526
            window.Close();
        });
    });

    // ---- UI-SIDEBAR-058: a search group's project row stays inert but keeps its live hover accessories ----

    [Fact]
    [Trait("Req", "UI-SIDEBAR-058")] // App/Views/Sidebar/SidebarView.swift:709 ProjectRow(..., togglesExpansion: false)
    public void A_search_groups_project_row_never_toggles_but_still_shows_its_hover_ellipsis_and_plus() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource()); // the hover fill's own AnimatedColor needs a bound clock
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Fix it", 1);
        var model = Model(harness, [project], [thread]);
        AvaloniaWindow window = null!;
        Control row = null!;
        Button button = null!;
        harness.Main.OnMain(() =>
        {
            var view = Detached(model);
            SearchBox(view).Text = "fix";
            var helpers = Helpers(view);
            var entries = Items(Call(view, "SearchEntries", Call(view, "SearchResults", helpers), helpers));
            var projectEntry = entries.Single(entry => Get(entry, "Value") is ValueTuple<Project, long>);
            row = ((Func<Control>)Get(projectEntry, "Make")!)(); // :709 ProjectRow(result.project, result.count, togglesExpansion: false)
            row.VerticalAlignment = VerticalAlignment.Top; // the row's own 28pt height, not the host's full stretch (ShownProjectRow's own pattern)
            var host = Hosted(model, row);
            window = WindowOf(host);
            window.UpdateLayout();

            var wasExpanded = model.Project(project.Id)!.IsExpanded;
            button = ButtonNamed(row, "Alpha"); // SidebarRow's own title button
            Click(button);
            Assert.Equal(wasExpanded, model.Project(project.Id)!.IsExpanded); // :1101 guard togglesExpansion else return: inert
        });
        harness.Main.OnMain(() => window.MouseMove(row.TranslatePoint(new Point(10, 10), window)!.Value)); // :1116 hovering, pumps the posted Track update on return
        harness.Main.OnMain(() =>
        {
            Assert.NotNull(LogicalQuery.SelfAndDescendants<RowActionsButton>(row).SingleOrDefault()); // :1117-1118, still live
            window.Close();
        });
    });

    // STOP App.Views.Sidebar.SidebarView #7 (narrowed part 7: SidebarHelperRow's and HeadCardsRow's own constructors
    // are now built, along with every other record this part names; ProjectRow's own PR-SIDEBAR-133 stays the
    // longest-standing item, unresolved since part 6)
    // Kind: forbidden need
    // Swift: DroppyCode/App/Views/Sidebar/SidebarView.swift:1138-1161 ProjectRow's own <c>.contextMenu</c>;
    // :1387-1397 SidebarHelperRow's own <c>.contextMenu</c>; :1490-1500 HeadCardsRow's own per-glyph
    // <c>.contextMenu</c> (B3 52bc792)
    // Requirements: PR-SIDEBAR-133 (ProjectRow's live AppKit context menu); PR-SIDEBAR-130 (the "Remove project"
    // confirmation gated behind it); the right-click interaction named in UI-SIDEBAR-033 ("right-click opens the
    // helper's context menu") and in UI-SIDEBAR-037 ("right-click opens the head's context menu with settleFirst
    // false") -- every other interaction, state and geometry each record names is proven above.
    // What is unclear or missing: SwiftUI's <c>.contextMenu</c> modifier re-evaluates its content fresh every time
    // the menu opens (all three sites cited above), and no ED or platform-adaptation entry names a Windows/Avalonia
    // foundation for that per-open refresh (porting/engineering_decisions.md and porting/platform_adaptations.md
    // have no ".contextMenu" or "ContextMenu" entry). A menu attached once, at construction time, would go stale the
    // first time the underlying snapshot changed without the row itself being rebuilt: an invented, silently wrong
    // behaviour. This part built SidebarHelperRow's and HeadCardsRow's own constructors in full otherwise, once
    // RowGlideAnimator.Shared.IsHiding(_:) and the per-row hover/menu @State pattern (both named missing when this
    // STOP was opened at part 5) were confirmed already in the tree (HelperStubRow, part 5; ProjectRow, part 6).
    // Readings or options: (a) build a menu fixed at construction time -> goes stale, an invented behaviour the
    // contract forbids more than an honest gap does; (b) invent a rebuild-on-open hook with no ED/PA backing -> the
    // same objection.
    // Needed to proceed: an engineering or platform-adaptation decision naming the Windows/Avalonia foundation for
    // SwiftUI's <c>.contextMenu</c> re-evaluation-on-open (then PR-SIDEBAR-130's request field and popover follow).
    // Done so far: ProjectRow's own body, contextMenuActions, actions(isActive:), BuildContextMenuItems and
    // ProjectPairMenuItems are proven directly (part 6); SidebarHelperRow's own constructor and HeadCardsRow's own
    // constructor are proven directly above, including their ellipsis-popover/click-to-select/hover-scale/selection-
    // ring/chevron pieces; nothing is claimed for PR-SIDEBAR-133 or -130, or for the right-click interaction alone
    // of UI-SIDEBAR-033/-037, and no test names them.
}
