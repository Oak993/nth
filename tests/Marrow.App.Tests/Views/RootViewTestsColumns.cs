using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Common;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views;

// Source: DroppyCode/App/Views/RootView.swift (B3 52bc792), :120-297. PORT-ONLY (ED-NEW-session-hotkeys): every test runs on
// both paths a switch can take: the port's default one, where the chat left fades out for 90 ms (gated at once, hidden after),
// and Reduce Motion's, which flips the columns at once as the source does.
[Collection("Headless")]
public sealed class RootViewTestsColumns(HeadlessSession headless) : RootViewTestsColumnsPaths(headless)
{
    protected override bool Animated => true;
}

[Collection("Headless")]
public sealed class RootViewTestsColumnsReduceMotion(HeadlessSession headless) : RootViewTestsColumnsPaths(headless)
{
    protected override bool Animated => false;
}

public abstract class RootViewTestsColumnsPaths(HeadlessSession headless)
{
    /// <summary>True: the default path (a switch moves); false: Reduce Motion (it flips at once, as the source does).</summary>
    protected abstract bool Animated { get; }

    private ManualFrameSource frameSource = null!;

    // The column the switch left: hidden at once under Reduce Motion; on the default path, leaving (gated, fading) until its
    // move is drawn, then hidden.
    private bool Gone(FrameHostingView host) => Animated ? !host.IsVisible || host.IsLeaving : !host.IsVisible;

    // The one column shown (a column still fading out is drawn but not shown).
    private static FrameHostingView ShownHost(Panel pane) => Hosts(pane).Single(host => host.IsVisible && !host.IsLeaving);

    // The switch's move drawn to its end: time and frames go on together (a test whose timers it would shift does not call it).
    private void FinishMoves(AppModelHarness h)
    {
        for (var frame = 0; frame < 12; frame++)
        {
            h.Main.OnMain(() => Dispatcher.UIThread.RunJobs());
            h.Main.AdvanceMilliseconds(1000.0 / 60);
            h.Main.OnMain(() => frameSource.Advance(TimeSpan.FromMilliseconds(1000.0 / 60)));
        }
    }

    private static Type ColumnsType => typeof(RootView).Assembly.GetType("Marrow.App.Views.ThreadColumns")!;
    private static object Coordinator() => Activator.CreateInstance(ColumnsType.GetNestedType("Coordinator")!)!;
    private static void Call(object target, string name, params object?[] args) => target.GetType().GetMethod(name)!.Invoke(target, args);
    private static FrameHostingView[] Hosts(Panel pane) => pane.Children.Cast<FrameHostingView>().ToArray();
    private static ChatView Chat(FrameHostingView host) => Assert.Single(LogicalQuery.SelfAndDescendants<ChatView>(host));
    private static IReadOnlyList<Guid> Prebuilt(DetailView detail) => (IReadOnlyList<Guid>)typeof(DetailView).GetField("prebuilt", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(detail)!;
    private static IDisposable Idle(Func<double> seconds)
    {
        var property = typeof(DetailView).GetProperty("SecondsSinceLastInput", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(property);
        return ((Ambient<Func<double>>)property.GetValue(null)!).Override(seconds);
    }
    private static void Tick(AppModelHarness h, double milliseconds)
    {
        h.Main.OnMain(() => Dispatcher.UIThread.RunJobs()); // start queued UI-executor tasks before advancing their timers
        h.Main.AdvanceMilliseconds(milliseconds);
        h.Main.OnMain(() => Dispatcher.UIThread.RunJobs());
    }

    private void WithPane(Action<AppModelHarness, AppModel, Guid[], Panel, WindowLiveResize, Avalonia.Controls.Window> check) => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        using var cursor = DragHandleView.CursorPlatform.Override(new ChatNoCursor());
        frameSource = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(frameSource);
        using var motion = Chrome.ReduceMotionPlatform.Override(new RenderingGeniePlatform { AccessibilityDisplayShouldReduceMotion = !Animated });
        var model = h.MakeModel();
        Guid[] ids = [];
        var pane = new Panel();
        var window = new Avalonia.Controls.Window { Content = pane };
        WindowLiveResize resize = null!;
        try
        {
            h.Main.OnMain(() =>
            {
                model.Settings.ShowsUsagePanel = false;
                model.Settings.HydraEnabled = false;
                var project = model.AddProject(new Project("Project", h.NewFolder("project")).Url);
                ids = Enumerable.Range(0, 11).Select(_ => model.NewThread(project)!.Id).ToArray();
                resize = new WindowLiveResize(window);
                window.Show();
                window.UpdateLayout();
            });
            // ThreadRuntime starts a real asynchronous document read. Complete it on the harness loop before
            // binding mounted ChatView callbacks to the UI dispatcher, so idle release has persisted state.
            h.Main.Loop.Run(async () =>
            {
                foreach (var id in ids) await model.Runtime(id).EnsureLoaded();
            }, AppModelHarness.Bound);
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            check(h, model, ids, pane, resize, window);
        }
        finally { h.Main.OnMain(() => { window.Hide(); window.Content = null; Dispatcher.UIThread.RunJobs(); }); }
    });

    [Fact]
    [Trait("Req", "PR-WIN-063")] // App/Views/RootView.swift:33-43,224-243,266-283
    public void Switching_columns_reuses_the_chat_timeline_and_composer_and_passes_the_hosted_environment() => WithPane((h, model, ids, pane, resize, window) =>
    {
        var coordinator = Coordinator();
        FrameHostingView first = null!;
        ChatView chat = null!;
        ThreadTimeline timeline = null!;
        ComposerView composer = null!;
        h.Main.OnMain(() =>
        {
            Call(coordinator, "Show", ids[0], pane, model, resize);
            window.UpdateLayout();
            first = Assert.Single(Hosts(pane));
            chat = Chat(first);
            timeline = Assert.Single(LogicalQuery.SelfAndDescendants<ThreadTimeline>(chat));
            composer = Assert.Single(LogicalQuery.SelfAndDescendants<ComposerView>(chat));
            Assert.Same(model, AppEnvironment.GetAppModel(chat));
            Assert.Same(resize, AppEnvironment.GetWindowLiveResize(chat));
            Assert.True(RootView.GetChatColumnIsShown(chat));
            Call(coordinator, "Show", ids[1], pane, model, resize);
            Assert.True(Gone(first));
            Assert.Same(pane, first.Parent); // :171-174 hidden, never detached on a switch
            Assert.False(RootView.GetChatColumnIsShown(chat));
            Assert.False(first.IsEffectivelyEnabled); // FrameHostingView's hidden shortcut gate (closed at once on both paths)
            Assert.True(RootView.GetChatColumnIsShown(Chat(ShownHost(pane))));
        });
        if (Animated)
        {
            FinishMoves(h);
            h.Main.OnMain(() =>
            {
                Assert.False(first.IsVisible); // hidden once its fade is drawn
                Assert.False(first.IsLeaving);
                Assert.False(first.IsEffectivelyEnabled);
                Assert.Same(Hosts(pane).Single(host => host != first), Hosts(pane).Single(host => host.IsVisible));
            });
        }
        h.Main.OnMain(() =>
        {
            Call(coordinator, "Show", ids[0], pane, model, resize);
            window.UpdateLayout();
            Assert.Same(first, ShownHost(pane));
            Assert.Same(chat, Chat(first));
            Assert.Same(timeline, Assert.Single(LogicalQuery.SelfAndDescendants<ThreadTimeline>(chat)));
            Assert.Same(composer, Assert.Single(LogicalQuery.SelfAndDescendants<ComposerView>(chat)));
            Assert.True(RootView.GetChatColumnIsShown(chat));
        });
    });

    [Fact]
    [Trait("Req", "PR-WIN-063")] // App/Views/RootView.swift:207-212,224-243,284-294
    public void Nine_columns_are_kept_and_the_oldest_hidden_one_detaches_only_after_500_ms() => WithPane((h, model, ids, pane, resize, _) =>
    {
        var coordinator = Coordinator();
        FrameHostingView oldest = null!;
        h.Main.OnMain(() =>
        {
            Call(coordinator, "Show", ids[0], pane, model, resize);
            oldest = Assert.Single(Hosts(pane));
            foreach (var id in ids.Skip(1).Take(8)) Call(coordinator, "Show", id, pane, model, resize);
            Assert.Equal(9, Hosts(pane).Length); // keptLimit 8 + shown one
            Call(coordinator, "Show", ids[9], pane, model, resize);
            Assert.Equal(10, Hosts(pane).Length); // retired stays attached until the switch is drawn
            Assert.True(Gone(oldest));
        });
        Tick(h, 499);
        h.Main.OnMain(() => Assert.Same(pane, oldest.Parent));
        Tick(h, 1);
        h.Main.OnMain(() => { Assert.Null(oldest.Parent); Assert.Equal(9, Hosts(pane).Length); });
    });

    [Fact]
    [Trait("Req", "PR-WIN-063")] // App/Views/RootView.swift:250-263
    public void A_prebuilt_column_is_evicted_before_visited_columns_and_is_built_once_per_request() => WithPane((h, model, ids, pane, resize, _) =>
    {
        var coordinator = Coordinator();
        FrameHostingView ahead = null!;
        h.Main.OnMain(() =>
        {
            Call(coordinator, "Show", ids[0], pane, model, resize);
            Call(coordinator, "Prebuild", new[] { ids[10] }, pane, model, resize);
            ahead = Hosts(pane).Single(host => !host.IsVisible);
            Assert.False(RootView.GetChatColumnIsShown(Chat(ahead)));
            foreach (var id in ids.Skip(1).Take(8)) Call(coordinator, "Show", id, pane, model, resize);
        });
        Tick(h, 500);
        h.Main.OnMain(() =>
        {
            Assert.Null(ahead.Parent);
            Call(coordinator, "Prebuild", new[] { ids[10] }, pane, model, resize);
            Assert.Equal(9, Hosts(pane).Length); // requested still contains it: no rebuilding
            Call(coordinator, "Prebuild", Array.Empty<Guid>(), pane, model, resize);
            Call(coordinator, "Prebuild", new[] { ids[10] }, pane, model, resize);
            Assert.Equal(10, Hosts(pane).Length); // a fresh request may build it again
            Assert.DoesNotContain(ahead, Hosts(pane));
        });
    });

    [Fact]
    [Trait("Req", "PR-WIN-063")] // App/Views/RootView.swift:207-209,227-234,266-271
    public void Hidden_columns_keep_their_built_size_until_shown_again() => WithPane((h, model, ids, pane, resize, window) =>
    {
        var coordinator = Coordinator();
        h.Main.OnMain(() =>
        {
            pane.Width = 600; pane.Height = 400;
            window.UpdateLayout();
            Call(coordinator, "Show", ids[0], pane, model, resize);
            window.UpdateLayout();
            var first = Assert.Single(Hosts(pane));
            var original = first.Bounds.Size;
            Assert.Equal(new Size(600, 400), original);
            Call(coordinator, "Show", ids[1], pane, model, resize);
            pane.Width = 700; pane.Height = 500;
            window.UpdateLayout();
            Assert.Equal(original, first.Bounds.Size);
            Assert.Equal(new Size(700, 500), ShownHost(pane).Bounds.Size);
            Call(coordinator, "Show", ids[0], pane, model, resize);
            window.UpdateLayout();
            Assert.Equal(new Size(700, 500), first.Bounds.Size);
        });
    });

    [Fact]
    [Trait("Req", "PR-WIN-063")] // App/Views/RootView.swift:193-198,247-249
    public void Updating_drops_hidden_running_missing_or_released_threads_and_filters_prebuilds() => WithPane((h, model, ids, _, resize, window) =>
    {
        Control columns = null!;
        h.Main.OnMain(() =>
        {
            columns = (Control)Activator.CreateInstance(ColumnsType, ids[0], new[] { ids[1] }, new HashSet<Guid>(), model, resize, false)!;
            window.Content = columns;
            window.UpdateLayout();
            var hosts = LogicalQuery.SelfAndDescendants<FrameHostingView>(columns).ToArray();
            Assert.Equal(2, hosts.Length);
            Call(columns, "Update", ids[0], new[] { ids[2], Guid.NewGuid() }, new HashSet<Guid> { ids[1], ids[2] }, model, resize);
            Assert.Equal(2, LogicalQuery.SelfAndDescendants<FrameHostingView>(columns).Count()); // hidden running retired, no filtered prebuild
        });
        Tick(h, 500);
        h.Main.OnMain(() =>
        {
            Assert.Single(LogicalQuery.SelfAndDescendants<FrameHostingView>(columns));
            Call(columns, "Update", ids[0], new[] { ids[3] }, new HashSet<Guid> { ids[0] }, model, resize);
            Assert.Equal(2, LogicalQuery.SelfAndDescendants<FrameHostingView>(columns).Count()); // shown may run
            Assert.True(model.ReleaseIdleRuntime(ids[3]));
            Call(columns, "Update", ids[0], Array.Empty<Guid>(), new HashSet<Guid>(), model, resize);
        });
        Tick(h, 500);
        h.Main.OnMain(() => Assert.Single(LogicalQuery.SelfAndDescendants<FrameHostingView>(columns)));
    });

    [Fact]
    [Trait("Req", "PR-WIN-063")] // App/Views/RootView.swift:130-154,179
    public void Detail_prebuild_waits_1500_ms_then_for_one_second_of_input_idle_and_spaces_builds_400_ms_apart() => WithPane((h, model, ids, _, resize, window) =>
    {
        var idleSeconds = 0.5;
        using var idle = Idle(() => idleSeconds);
        var detail = new DetailView();
        Guid[] candidates = [];
        h.Main.OnMain(() =>
        {
            model.SelectedThreadID = ids[4];
            candidates = model.Neighbors(ids[4]).Concat(model.SidebarThreads.Take(6).Select(thread => thread.Id))
                .Where(id => id != ids[4]).Distinct().ToArray();
            AppEnvironment.SetAppModel(detail, model);
            AppEnvironment.SetWindowLiveResize(detail, resize);
            window.Content = detail;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        });
        Tick(h, 1499);
        h.Main.OnMain(() => Assert.Empty(Prebuilt(detail)));
        Tick(h, 1);
        h.Main.OnMain(() => Assert.Empty(Prebuilt(detail))); // recent input defers the first build
        idleSeconds = 1;
        Tick(h, 299);
        h.Main.OnMain(() => Assert.Empty(Prebuilt(detail)));
        Tick(h, 1);
        h.Main.OnMain(() => Assert.Equal(candidates.Take(1), Prebuilt(detail)));
        Tick(h, 399);
        h.Main.OnMain(() => Assert.Single(Prebuilt(detail)));
        Tick(h, 1);
        h.Main.OnMain(() => Assert.Equal(candidates.Take(2), Prebuilt(detail)));
        // Source candidates contain duplicates and the selected id: each still consumes a 400-ms sleep.
        for (var step = 0; step < 8; step++) Tick(h, 400);
        h.Main.OnMain(() => Assert.Equal(candidates, Prebuilt(detail)));
    });

    [Fact]
    [Trait("Req", "PR-WIN-063")] // App/Views/RootView.swift:133-153
    public void Changing_selection_cancels_the_old_prebuild_and_the_launch_top_rows_are_requested_only_once() => WithPane((h, model, ids, _, resize, window) =>
    {
        using var idle = Idle(() => 2);
        var detail = new DetailView();
        h.Main.OnMain(() =>
        {
            model.SelectedThreadID = ids[4];
            AppEnvironment.SetAppModel(detail, model);
            AppEnvironment.SetWindowLiveResize(detail, resize);
            window.Content = detail;
            Dispatcher.UIThread.RunJobs();
        });
        Tick(h, 1500);
        h.Main.OnMain(() => Assert.Single(Prebuilt(detail)));
        h.Main.OnMain(() => model.SelectedThreadID = ids[8]);
        h.Main.OnMain(() => { Dispatcher.UIThread.RunJobs(); Assert.Empty(Prebuilt(detail)); });
        Tick(h, 1499);
        h.Main.OnMain(() => Assert.Empty(Prebuilt(detail)));
        Tick(h, 1);
        h.Main.OnMain(() => Assert.Equal(model.Neighbors(ids[8]).Take(1), Prebuilt(detail)));
        for (var step = 0; step < 4; step++) Tick(h, 400);
        h.Main.OnMain(() =>
        {
            Assert.Equal(model.Neighbors(ids[8]), Prebuilt(detail)); // launch rows already requested for the first selection
            typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.Phase))!.SetValue(model.Runtime(ids[7]), RuntimePhase.Starting);
            model.SelectedThreadID = ids[7];
        });
        h.Main.OnMain(() => Dispatcher.UIThread.RunJobs());
        Tick(h, 1500);
        h.Main.OnMain(() => Assert.Empty(Prebuilt(detail))); // :150 running selection stops the entire prebuild task
    });

    [Fact]
    [Trait("Req", "PR-WIN-063")] // App/Views/RootView.swift:131-133,196-197
    public void A_newly_prebuilt_hidden_column_is_dropped_when_its_runtime_starts_streaming() => WithPane((h, model, ids, _, resize, window) =>
    {
        using var idle = Idle(() => 2);
        var detail = new DetailView();
        Guid neighbor = default;
        h.Main.OnMain(() =>
        {
            neighbor = model.Neighbors(ids[4])[0];
            Assert.True(model.ReleaseIdleRuntime(neighbor));
            model.SelectedThreadID = ids[4];
            AppEnvironment.SetAppModel(detail, model);
            AppEnvironment.SetWindowLiveResize(detail, resize);
            window.Content = detail;
            Dispatcher.UIThread.RunJobs();
        });
        Tick(h, 1500);
        FrameHostingView hidden = null!;
        h.Main.OnMain(() =>
        {
            Assert.Equal(neighbor, Assert.Single(Prebuilt(detail)));
            hidden = LogicalQuery.SelfAndDescendants<FrameHostingView>(detail).Single(host => !host.IsVisible);
            typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.Phase))!.SetValue(model.Runtime(neighbor), RuntimePhase.Starting);
        });
        // Freeze additional prebuilds without changing the streaming runtime or the source's retirement deadline.
        using var busy = Idle(() => 0);
        Tick(h, 500);
        h.Main.OnMain(() => Assert.Null(hidden.Parent));
    });
}
