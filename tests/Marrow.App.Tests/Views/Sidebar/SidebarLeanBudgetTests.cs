using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Sidebar;
using Marrow.Core.Models;
using Marrow.Testing;
using Marrow.UI.Port;
using Xunit;
using static Marrow.App.Tests.Views.Sidebar.SidebarViewTests;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Sidebar;

// Lean pass L15 (ED-NEW-lean-15): what the sidebar costs with 500 threads in the list. A search keystroke that leaves the results as they
// were, a change the list does not show (a space typed into the empty search field, a keystroke that keeps the same results), one pointer
// move of a dragged row, and the layout manager's LayoutUpdated subscribers. Measured on the headless UI thread with
// GC.GetAllocatedBytesForCurrentThread (the list, its rows and the main loop all run there; AllocationProbe's own thread cannot host the
// window). The numbers are a Debug build's (the test project is built Debug).
[Collection("Headless")]
public sealed class SidebarLeanBudgetTests(HeadlessSession headless)
{
    private const int Warmup = 16;
    private const int Measured = 40;

    // Debug build. Measured on the base (claude/release-integration 0a946c558): 2,310,737 bytes per keystroke (2,310,258 and 2,310,262 on
    // reruns). Limit about 1.5x.
    private const long SearchKeystrokeLimit = 3_450_000;
    // Measured on the base: 20 list passes for 20 changes the list does not show (10 spaces, 10 keystrokes that keep the results). Limit
    // about 1.5x; the fix's target is 0.
    private const int NoOpPassLimit = 30;
    // Measured on the base: 502 subscribers with 500 thread rows (one per row, and two others). Limit about 1.5x.
    private const int LayoutSubscriberLimit = 750;
    // Debug build. Measured on the base: 393,324 bytes per pointer move of a dragged row (393,330 on a rerun). Limit about 1.5x.
    private const long DragMoveLimit = 590_000;

    internal sealed class Rig(AppModelHarness harness, ManualFrameSource frames, AvaloniaWindow window, SidebarView view, AppModel model)
    {
        public AppModelHarness Harness => harness;
        public AvaloniaWindow Window => window;
        public SidebarView View => view;
        public AppModel Model => model;

        public void Tick(int milliseconds)
        {
            harness.Main.AdvanceMilliseconds(milliseconds);
            harness.Main.OnMain(() => { frames.Advance(TimeSpan.FromMilliseconds(milliseconds)); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); });
        }

        public void Type(string text, int then = 16)
        {
            harness.Main.OnMain(() => SearchBox(view).Text = text);
            Tick(then);
        }

        public void OnMain(Action body) => harness.Main.OnMain(body);
    }

    // `count` threads titled "Thread 000" on, spread over five expanded projects, each a minute older than the last.
    internal static void Run(int count, Action<Rig> body)
    {
        using var harness = new AppModelHarness();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        var projects = Enumerable.Range(0, 5).Select(index => NewProject(harness, "Project " + index)).ToList();
        var threads = Enumerable.Range(0, count).Select(index => NewThread(projects[index % projects.Count], "Thread " + index.ToString("000", System.Globalization.CultureInfo.InvariantCulture), count - index)).ToList();
        var model = Model(harness, projects, threads);
        SidebarView view = null!;
        harness.Main.OnMain(() => view = Attached(model));
        var rig = new Rig(harness, frames, WindowOf(view), view, model);
        try
        {
            rig.Tick(1600); // every row's delayed insertion fade has finished
            body(rig);
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    }

    internal static int ListPasses(SidebarView view) => (int)Field(view, "listPasses")!;

    internal static List<SidebarThreadRow> ThreadRows(SidebarView view) => LogicalQuery.SelfAndDescendants<SidebarThreadRow>(view).ToList();

    // The layout manager's own LayoutUpdated event: one subscriber per control that listens to LayoutUpdated (Avalonia's Layoutable
    // subscribes itself to its root's manager for its first handler).
    internal static int LayoutUpdatedSubscribers(TopLevel top)
    {
        object? manager = null;
        for (var type = top.GetType(); manager is null && type is not null; type = type.BaseType)
            if (type.GetProperty("LayoutManager", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) is { } property)
                manager = property.GetValue(top);
        Assert.NotNull(manager);
        for (var type = manager.GetType(); type is not null; type = type.BaseType)
            if (type.GetField("LayoutUpdated", BindingFlags.Instance | BindingFlags.NonPublic) is { } field)
                return ((Delegate?)field.GetValue(manager))?.GetInvocationList().Length ?? 0;
        throw new InvalidOperationException("no LayoutUpdated field on " + manager.GetType());
    }

    private static void Log(string line)
    {
        if (Environment.GetEnvironmentVariable("MARROW_LEAN_LOG") is { Length: > 0 } path) File.AppendAllText(path, line + Environment.NewLine);
    }

    internal static long BytesPerSearchKeystroke(int count)
    {
        long bytes = 0;
        Run(count, rig =>
        {
            // Every query below matches every thread, so each keystroke keeps the same results (typing on through a title that still matches).
            string[] cycle = ["th", "thr", "thre", "threa", "thread", "threa", "thre", "thr"];
            rig.Type("t", then: 1600);
            for (var i = 0; i < Warmup; i++) rig.Type(cycle[i % cycle.Length]);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < Measured; i++) rig.Type(cycle[(Warmup + i) % cycle.Length]);
            bytes = (GC.GetAllocatedBytesForCurrentThread() - before) / Measured;
            Assert.Equal(count, ThreadRows(rig.View).Count); // the results still show every thread
        });
        return bytes;
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-15")]
    public void A_search_keystroke_with_500_threads_stays_within_its_byte_budget() => headless.OnUiThread(() =>
    {
        var bytes = BytesPerSearchKeystroke(500);
        Log($"L15 search keystroke bytes={bytes}");
        Assert.True(bytes <= SearchKeystrokeLimit, $"a search keystroke with 500 threads costs {bytes} bytes (limit {SearchKeystrokeLimit})");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lean-15")]
    public void A_change_the_list_does_not_show_applies_no_list_pass() => headless.OnUiThread(() =>
    {
        var passes = 0;
        Run(500, rig =>
        {
            Assert.Equal(500, ThreadRows(rig.View).Count);
            // Spaces typed into the empty field: the query trims to "" and the list stays as it is.
            var start = ListPasses(rig.View);
            for (var i = 0; i < 10; i++) rig.Type(i % 2 == 0 ? " " : "  ");
            var list = ListPasses(rig.View) - start;
            rig.Type("", then: 16);
            // A keystroke that keeps the same results.
            rig.Type("thread", then: 1600);
            start = ListPasses(rig.View);
            for (var i = 0; i < 10; i++) rig.Type(i % 2 == 0 ? "threa" : "thread");
            var search = ListPasses(rig.View) - start;
            passes = list + search;
            Log($"L15 no-op passes list={list} search={search}");
            Assert.Equal(500, ThreadRows(rig.View).Count);
        });
        Assert.True(passes <= NoOpPassLimit, $"20 changes the list does not show applied {passes} list passes (limit {NoOpPassLimit})");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lean-15")]
    public void Five_hundred_thread_rows_stay_within_the_LayoutUpdated_subscriber_budget() => headless.OnUiThread(() =>
    {
        var subscribers = 0;
        Run(500, rig =>
        {
            Assert.Equal(500, ThreadRows(rig.View).Count);
            subscribers = LayoutUpdatedSubscribers(rig.Window);
            Log($"L15 LayoutUpdated subscribers={subscribers}");
        });
        Assert.True(subscribers <= LayoutSubscriberLimit, $"500 thread rows leave {subscribers} LayoutUpdated subscribers (limit {LayoutSubscriberLimit})");
    });

    internal static long BytesPerDragMove(int count)
    {
        long bytes = 0;
        Run(count, rig =>
        {
            var first = ListPanel(rig.View).Children.First(child => LogicalQuery.SelfAndDescendants<SidebarThreadRow>(child).Any());
            var start = first.TranslatePoint(new Point(40, 10), rig.Window)!.Value;
            // Moves of 5 and 6 points: the row follows the pointer and never crosses a neighbour's centre, so nothing swaps.
            void Move(int i)
            {
                rig.OnMain(() => rig.Window.MouseMove(new Point(start.X, start.Y + 5 + (i % 2)), RawInputModifiers.LeftMouseButton));
                rig.Tick(16);
            }
            rig.OnMain(() => rig.Window.MouseDown(start, MouseButton.Left));
            for (var i = 0; i < Warmup; i++) Move(i);
            Assert.NotNull(Drag(rig.View).Id);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < Measured; i++) Move(Warmup + i);
            bytes = (GC.GetAllocatedBytesForCurrentThread() - before) / Measured;
            rig.OnMain(() => rig.Window.MouseUp(new Point(start.X, start.Y + 5), MouseButton.Left));
            rig.Tick(600);
            Assert.Null(Drag(rig.View).Id);
        });
        return bytes;
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-15")]
    public void A_drag_move_with_500_threads_stays_within_its_byte_budget() => headless.OnUiThread(() =>
    {
        var bytes = BytesPerDragMove(500);
        Log($"L15 drag move bytes={bytes}");
        Assert.True(bytes <= DragMoveLimit, $"a drag move with 500 threads costs {bytes} bytes (limit {DragMoveLimit})");
    });
}
