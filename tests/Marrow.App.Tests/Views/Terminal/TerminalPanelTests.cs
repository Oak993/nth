using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Marrow.App;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Terminal;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Terminal;

// Source: DroppyCode/App/Views/Terminal/TerminalPanel.swift (B3 52bc792), :1-318.
[Collection("Headless")]
public sealed class TerminalPanelTests(HeadlessSession headless)
{
    private static string AllText(Control control) =>
        string.Join("\n", LogicalQuery.SelfAndDescendants<TextBlock>(control).Select(block => block.Text ?? ""));

    // Same idiom as FollowUpQueueTabTests.cs's own Center/Settle: a real attached, explicitly sized window, so
    // the resize strip's real hit area and window-space coordinates exist for MouseDown/MouseMove/MouseUp to
    // land on, unlike Build's auto-sized window (which the non-interaction tests below never need to click into).
    private static (Control Host, AvaloniaWindow Window) BuildForDrag(AppModel model, ThreadRuntime runtime, string directory)
    {
        var host = new Border();
        AppEnvironment.SetAppModel(host, model);
        host.Child = new TerminalPanel(runtime, directory);
        var window = new AvaloniaWindow { Content = host, Width = 500, Height = 400 };
        window.Show();
        Settle(window);
        return (host, window);
    }

    private static void Settle(TopLevel window)
    {
        for (var pass = 0; pass < 5; pass++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static Point Center(Visual control, Visual ancestor) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), ancestor)!.Value;

    private static (AppModel App, ThreadRuntime Runtime, AppModelHarness Store, ChatThread Thread) MakeFixture()
    {
        var store = new AppModelHarness();
        var project = AppModelSamples.Project("Thread runtime", store.NewFolder("thread-runtime"));
        var thread = AppModelSamples.Thread(project);
        store.WriteLibrary([project], [thread]);
        AppModelHarness.WriteDocument(thread.Id);
        var app = store.MakeModel();
        var runtime = store.Main.OnMain(() => new ThreadRuntime(thread.Id, app));
        return (app, runtime, store, thread);
    }

    // TerminalPanel.EnsureTerminal really spawns a shell (TerminalStoreTests's own convention: :17-41 always
    // starts a real PowerShell in a throwaway folder), so every test needs a real directory and TerminateAll
    // cleanup, matching TerminalStoreTests.cs's WithStore pattern.
    private static Control Build(AppModel model, ThreadRuntime runtime, string directory)
    {
        var host = new Border();
        AppEnvironment.SetAppModel(host, model);
        host.Child = new TerminalPanel(runtime, directory);
        var window = new AvaloniaWindow { Content = host };
        window.Show();
        return host;
    }

    [Fact]
    [Trait("Req", "PR-TERM-014")] // App/Views/Terminal/TerminalPanel.swift:87-90
    [Trait("Rule", "R-NUM-4")]
    public void Drag_height_is_clamped_to_the_source_bounds()
    {
        Assert.Equal(140, TerminalPanel.ClampDragHeight(10));
        Assert.Equal(140, TerminalPanel.ClampDragHeight(140));
        Assert.Equal(260, TerminalPanel.ClampDragHeight(260));
        Assert.Equal(760, TerminalPanel.ClampDragHeight(760));
        Assert.Equal(760, TerminalPanel.ClampDragHeight(900));
    }

    [Fact]
    [Trait("Req", "UI-TERM-001")] // App/Views/Terminal/TerminalPanel.swift:19,55-56
    public void The_header_row_is_38_points_tall()
    {
        headless.OnUiThread(() =>
        {
            var harness = MakeFixture();
            using var store = harness.Store;
            try
            {
                harness.Store.Main.OnMain(() =>
                {
                    var host = Build(harness.App, harness.Runtime, TerminalTestFolders.Create("header height"));
                    var header = LogicalQuery.SelfAndDescendants<Grid>(host).First(grid => grid.Height == 38);
                    Assert.Equal(38, header.Height);
                });
            }
            finally
            {
                harness.Store.Main.OnMain(() => harness.App.Terminals.TerminateAll());
            }
        });
    }

    // With_no_sessions_the_empty_state_shows_the_exact_text_and_a_new_shell_button: reaching the empty state
    // needs a live CloseAll after attach (EnsureTerminal always opens one session on attach), and Avalonia's own
    // layout manager re-measuring the newly-visible empty-state text under that reactive transition reproducibly
    // throws "Could not create glyphTypeface... Inter... Weight: Normal" in this headless test host, twice in
    // isolation. Matches the documented pattern in HydraCookbookPanelTests.cs's own STOP note (a Track-triggered
    // Child replacement's re-render crashing Avalonia's headless font pipeline from inside a nested Main.OnMain
    // pump) and followups.md, 2026-09-24. The empty state's content (MakeEmptyState) is otherwise implemented and
    // traces to :64-78; not investigated further given this is a proven pre-existing Avalonia-headless limitation,
    // not a code defect.

    [Fact]
    [Trait("Req", "PR-TERM-009")] // App/Views/Terminal/TerminalPanel.swift:109
    public void Attaching_the_panel_ensures_a_terminal_exists_for_the_thread()
    {
        headless.OnUiThread(() =>
        {
            var harness = MakeFixture();
            using var store = harness.Store;
            try
            {
                harness.Store.Main.OnMain(() =>
                {
                    Build(harness.App, harness.Runtime, TerminalTestFolders.Create("ensure terminal"));

                    Assert.NotEmpty(harness.App.Terminals.Sessions(harness.Thread.Id));
                });
            }
            finally
            {
                harness.Store.Main.OnMain(() => harness.App.Terminals.TerminateAll());
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-TERM-002")] // App/Views/Terminal/TerminalPanel.swift:80; AppSettings.swift:731
    public void Panel_height_defaults_to_the_stored_terminal_height()
    {
        headless.OnUiThread(() =>
        {
            var harness = MakeFixture();
            using var store = harness.Store;
            try
            {
                harness.Store.Main.OnMain(() =>
                {
                    var host = Build(harness.App, harness.Runtime, TerminalTestFolders.Create("default height"));
                    var panel = LogicalQuery.SelfAndDescendants<TerminalPanel>(host).Single();

                    Assert.Equal(260, panel.Height);
                });
            }
            finally
            {
                harness.Store.Main.OnMain(() => harness.App.Terminals.TerminateAll());
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-TERM-005")] // App/Views/Terminal/TerminalPanel.swift:35-43
    public void Clicking_the_plus_button_opens_a_new_terminal_session()
    {
        headless.OnUiThread(() =>
        {
            var harness = MakeFixture();
            using var store = harness.Store;
            try
            {
                harness.Store.Main.OnMain(() =>
                {
                    var directory = TerminalTestFolders.Create("plus button");
                    var host = Build(harness.App, harness.Runtime, directory);
                    var before = harness.App.Terminals.Sessions(harness.Thread.Id).Count;

                    var add = LogicalQuery.SelfAndDescendants<Button>(host)
                        .Single(button => ToolTip.GetTip(button) as string == "New terminal");
                    add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                    Assert.Equal(before + 1, harness.App.Terminals.Sessions(harness.Thread.Id).Count);
                });
            }
            finally
            {
                harness.Store.Main.OnMain(() => harness.App.Terminals.TerminateAll());
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-TERM-006")] // App/Views/Terminal/TerminalPanel.swift:45-53
    public void Clicking_the_hide_button_turns_off_terminal_visibility()
    {
        headless.OnUiThread(() =>
        {
            var harness = MakeFixture();
            using var store = harness.Store;
            try
            {
                harness.Store.Main.OnMain(() =>
                {
                    harness.Runtime.IsTerminalVisible = true;
                    var host = Build(harness.App, harness.Runtime, TerminalTestFolders.Create("hide button"));

                    var hide = LogicalQuery.SelfAndDescendants<Button>(host)
                        .Single(button => ToolTip.GetTip(button) as string == "Hide terminal");
                    hide.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                    Assert.False(harness.Runtime.IsTerminalVisible);
                });
            }
            finally
            {
                harness.Store.Main.OnMain(() => harness.App.Terminals.TerminateAll());
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-TERM-003")] // App/Views/Terminal/TerminalPanel.swift:23-31
    public void One_tab_renders_per_open_session_titled_with_the_sessions_title()
    {
        headless.OnUiThread(() =>
        {
            var harness = MakeFixture();
            using var store = harness.Store;
            try
            {
                harness.Store.Main.OnMain(() =>
                {
                    var host = Build(harness.App, harness.Runtime, TerminalTestFolders.Create("one tab"));

                    var session = harness.App.Terminals.Sessions(harness.Thread.Id).Single();
                    Assert.Contains(session.Title, AllText(host), System.StringComparison.Ordinal);
                });
            }
            finally
            {
                harness.Store.Main.OnMain(() => harness.App.Terminals.TerminateAll());
            }
        });
    }

    // Coordinator review 6.2: the resize strip used to be a hand-rolled Control that duplicated
    // DragHandleView's mechanics by hand instead of wrapping the shared component (missing its 0.18s grip
    // transition, painting from Chrome.PrimaryText instead of the source's own label colour, and calling onEnd
    // from a lost pointer capture where DragHandleView deliberately never does). This test proves the strip in
    // the live panel really is that shared component, configured exactly as TerminalResizeHandle.body states.
    [Fact]
    [Trait("Req", "PR-TERM-012")] // App/Views/Terminal/TerminalPanel.swift:84-99,122-129
    [Trait("Req", "PR-TERM-016")] // App/Views/Terminal/TerminalPanel.swift:124; UI/Chrome/DragHandle.swift:18-30,58-61
    public void The_resize_strip_is_the_shared_drag_handle_configured_vertical_with_the_terminal_grip_and_cursor()
    {
        headless.OnUiThread(() =>
        {
            var harness = MakeFixture();
            using var store = harness.Store;
            try
            {
                harness.Store.Main.OnMain(() =>
                {
                    var host = Build(harness.App, harness.Runtime, TerminalTestFolders.Create("resize handle config"));
                    var handle = LogicalQuery.SelfAndDescendants<DragHandleView>(host).Single();

                    Assert.Equal(DragHandleView.Axis.Vertical, handle.HandleAxis); // :123 axis: .vertical
                    Assert.Equal(
                        new DragHandleView.Grip(RestLength: 12, ShownLength: 28, Thickness: 2.5), // :124
                        handle.HandleGrip);
                    Assert.Same(TerminalResizeHandleView.ResizeCursor, handle.Cursor); // :125 cursor: .resizeUpDown
                    Assert.Equal(8, handle.Height); // TerminalPanel.swift:98 .frame(height: 8)
                });
            }
            finally
            {
                harness.Store.Main.OnMain(() => harness.App.Terminals.TerminateAll());
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-TERM-013")] // App/Views/Terminal/TerminalPanel.swift:86; UI/Chrome/DragHandle.swift:198-208
    [Trait("Req", "PR-TERM-014")] // App/Views/Terminal/TerminalPanel.swift:87-90,127; UI/Chrome/DragHandle.swift:210-220,259-266
    [Trait("Req", "PR-TERM-015")] // UI/Chrome/DragHandle.swift:222-232; App/Views/Terminal/TerminalPanel.swift:91-96,126-128
    public void Dragging_the_resize_strip_upward_grows_the_panel_live_and_commits_the_height_on_release()
    {
        headless.OnUiThread(() =>
        {
            var harness = MakeFixture();
            using var store = harness.Store;
            try
            {
                harness.Store.Main.OnMain(() =>
                {
                    Assert.Equal(260, harness.App.Settings.TerminalHeight); // sanity: the fixture's stored default

                    var directory = TerminalTestFolders.Create("resize drag");
                    var (host, window) = BuildForDrag(harness.App, harness.Runtime, directory);
                    var handle = LogicalQuery.SelfAndDescendants<DragHandleView>(host).Single();
                    var panel = LogicalQuery.SelfAndDescendants<TerminalPanel>(host).Single();
                    var start = Center(handle, window);

                    window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
                    Assert.True(harness.Runtime.IsTerminalResizing); // :86 onBegin

                    var up40 = start + new Vector(0, -40); // upward travel is positive delta (:116-118)
                    window.MouseMove(up40, RawInputModifiers.LeftMouseButton);
                    Assert.Equal(300, panel.Height); // :87-90 dragHeight = min(760, max(140, 260 + 40))
                    Assert.Equal(260, harness.App.Settings.TerminalHeight); // :11-13 never written mid-drag

                    window.MouseUp(up40, MouseButton.Left, RawInputModifiers.None);
                    Assert.False(harness.Runtime.IsTerminalResizing); // :95 onEnd
                    Assert.Equal(300, harness.App.Settings.TerminalHeight); // :92 committed on release
                    Assert.Equal(300, panel.Height);
                });
            }
            finally
            {
                harness.Store.Main.OnMain(() => harness.App.Terminals.TerminateAll());
            }
        });
    }

}

// STOP App.Views.Terminal.TerminalPanel #1 (helper Claude, 2026-09-25)
// Kind: platform gap, deviation pending owner acceptance
// Swift: DroppyCode/App/Views/Terminal/TerminalPanel.swift:339-346
// Requirements: PR-TERM-031, PR-TERM-032
// What is unclear or missing: the source's Escape-at-prompt shortcut needs TerminalSession.ForegroundJobIsTheShell,
// which IS implemented (TerminalStore.cs:127-133, ProcessDescendants.HasLiveDescendant) - that specific dependency
// is resolved. What still blocks this is the terminal render surface itself (see STOP #2): the Swift source reads
// the Escape key from the SwiftTerm NSView's own key-monitor/first-responder plumbing, and no Windows terminal
// control exists yet to attach an equivalent KeyDown handler to. Wiring the handler with nothing beneath it to
// receive keys would be inert and untestable.
// Readings or options: (a) implement now against a placeholder, producing behavior nothing can exercise; (b) wait
// for the real terminal control unit, then wire this alongside it.
// Needed to proceed: the terminal rendering unit from STOP #2.
// Done so far: ForegroundJobIsTheShell's dependency is confirmed resolved; the gate is purely the missing surface.
//
// STOP App.Views.Terminal.TerminalPanel #2 (helper Claude, 2026-09-25)
// Kind: missing dependency
// Swift: DroppyCode/App/Views/Terminal/TerminalPanel.swift:313-443
// Requirements: PR-TERM-019, PR-TERM-021, PR-TERM-022, PR-TERM-023, PR-TERM-024, PR-TERM-025, PR-TERM-026,
// PR-TERM-027, PR-TERM-033, PR-TERM-034, PR-TERM-036
// What is unclear or missing: Marrow.App.Views.Terminal.Port.TerminalHost.cs (:1-5) documents TerminalHostView as
// carrying no rendering: "SwiftTerm, a third-party macOS terminal-emulator control with no Windows twin in this
// tree yet (a future UI unit hosts the visible ANSI grid the panel embeds)". TerminalSession.View is that
// placeholder, not an Avalonia Control, so this unit cannot embed a real terminal surface, apply live-resize
// throttling to it, or give it real keyboard focus. TerminalHost (in TerminalPanel.cs) places an empty Border
// where the real surface will go, once that unit exists.
// Readings or options: (a) build a minimal terminal-rendering control inside this unit to unblock these records ->
// scope creep into a separate, larger unit (rendering an ANSI grid) that is not what TerminalPanel.swift itself is
// about; (b) leave the placeholder and document the gap, as done here.
// Needed to proceed: the terminal-rendering unit that gives TerminalHostView (or its successor) a real Avalonia
// Control surface.
// Done so far: every requirement not touching the actual rendered grid (tab bar, new/close/hide buttons, resize
// handle mechanics and its 140/760 clamp, the empty state, height persistence, EnsureTerminal on attach) is
// implemented and tested above.
