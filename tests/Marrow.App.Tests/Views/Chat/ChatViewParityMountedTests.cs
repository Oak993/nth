using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Usage;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Chat;

// Parity closure for ChatView.swift (1.7.0) on the mounted column: real pointer input, the reserve slide, the minimap flag.
[Collection("Headless")]
public sealed class ChatViewParityMountedTests(HeadlessSession headless)
{
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    // Two sent messages in two turns: the rail shows for more than one sent message (ThreadTimeline.swift:180-200).
    private static TimelineEntry User(string id) => new(new TimelineItem(Guid.NewGuid(), new TimelineItem.Content.User(new UserMessage { Text = "prompt " + id }), id));

    private static void Drain(AppModelHarness h, AvaloniaWindow window)
    {
        h.Main.RunUntilIdle();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        h.Main.RunUntilIdle();
    }

    private static ThreadTimeline Timeline(ChatView view) => Assert.Single(LogicalQuery.SelfAndDescendants<ThreadTimeline>(view));

    private void WithColumn(Action<AppModelHarness, AppModel, ThreadRuntime, ChatView, AvaloniaWindow, ManualFrameSource> check,
        double width = 1400, Action<AppModel>? configure = null, bool messages = false) => ChatHeadlessFixtures.Run(headless, () =>
    {
        using var h = new AppModelHarness();
        h.Main.Loop.Run(() => Marrow.Core.Support.LoginEnvironment.Load(), AppModelHarness.Bound);
        using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        using var cursor = DragHandleView.CursorPlatform.Override(new ChatNoCursor());
        var frames = new ManualFrameSource();
        using var clock = FrameClock.Source.Override(frames);
        var directory = ChatGitFixtures.Init(h.NewFolder("repo"));
        h.WriteLibrary([new Project("Project", directory)], []);
        var model = h.MakeModel();
        h.PrepareBootstrap(model); // offline: every CLI path is missing; mounting usage never runs a real provider.
        AvaloniaWindow window = null!;
        ChatView view = null!;
        ThreadRuntime runtime = null!;
        h.Main.OnMain(() =>
        {
            model.Settings.ShowsUsagePanel = false;
            model.Settings.HydraEnabled = false;
            configure?.Invoke(model);
            runtime = model.Runtime(model.NewThread(model.Projects[0])!.Id);
            if (messages)
            {
                typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.IsLoadingHistory))!.SetValue(runtime, false);
                typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.Entries))!.SetValue(runtime, new ValueList<TimelineEntry>([User("a"), User("b")]));
            }
            view = new ChatView(runtime);
            window = new AvaloniaWindow { Content = view, Width = width, Height = 900 };
            AppEnvironment.SetAppModel(window, model);
            AppEnvironment.SetWindowLiveResize(window, new WindowLiveResize(window));
            ChatHeadlessFixtures.Track(window);
            window.Show();
        });
        try { Drain(h, window); check(h, model, runtime, view, window, frames); }
        finally { h.Main.OnMain(() => { window.Close(); model.Terminals.TerminateAll(); }); Drain(h, window); }
    });

    // ---- PR-CHAT-157 + PR-USAGE-070: a real pointer drag of the usage panel ------------------------------------------

    [Fact]
    [Trait("Req", "PR-CHAT-157")] // App/Views/Chat/ChatView.swift:36-52,112-115
    [Trait("Req", "PR-USAGE-070")] // App/Views/Chat/ChatView.swift:492-494,509-519,619-625
    public void Dragging_the_usage_panel_by_its_strip_mutes_the_timeline_and_redocks_live_then_release_restores_hit_tests()
        => WithColumn((h, model, _, view, window, _) =>
        {
            h.Main.OnMain(() => { model.Settings.UsagePanelDock = PanelDockCorner.BottomLeading; model.Settings.ShowsUsagePanel = true; });
            Drain(h, window);
            var panel = Assert.Single(LogicalQuery.SelfAndDescendants<UsageFloatingPanel>(view));
            var handle = LogicalQuery.SelfAndDescendants<PanelDragHandle>(panel).First(x => x.Bounds.Width > 40);
            var start = handle.TranslatePoint(new Point(handle.Bounds.Width / 2, handle.Bounds.Height / 2), window)!.Value;
            Assert.True(Timeline(view).IsHitTestVisible);
            var writes = h.Defaults.Writes.Count(key => key == "usagePanelDock");
            window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
            var docks = new List<PanelDockCorner>();
            // Up and to the right, across both the column's middle lines: the nearest corner follows the pointer (:509-512).
            foreach (var step in Enumerable.Range(1, 12))
            {
                window.MouseMove(start + new Point(step * 90, -step * 55), RawInputModifiers.LeftMouseButton);
                Drain(h, window);
                Assert.False(Timeline(view).IsHitTestVisible); // :115 for the whole length of the drag.
                Assert.True(Assert.Single(LogicalQuery.SelfAndDescendants<Marrow.App.Views.Composer.ComposerArea>(view)).IsHitTestVisible);
                docks.Add(model.Settings.UsagePanelDock);
            }
            Assert.Equal(PanelDockCorner.TopTrailing, docks[^1]); // re-docked while still held (:611 "called as the panel is held").
            Assert.Contains(PanelDockCorner.BottomLeading, docks.Take(1));
            // :620-625 `if next != corner.wrappedValue`: written mid-drag once per change of corner, never on a move that leaves it where it is.
            var changes = docks.Prepend(PanelDockCorner.BottomLeading).Zip(docks, (was, now) => was != now).Count(changed => changed);
            Assert.True(changes >= 2, "the drag crosses more than one corner");
            Assert.Equal(changes, h.Defaults.Writes.Count(key => key == "usagePanelDock") - writes);
            window.MouseUp(start + new Point(12 * 90, -12 * 55), MouseButton.Left, RawInputModifiers.None);
            Drain(h, window);
            Assert.True(Timeline(view).IsHitTestVisible); // the flag clears when every drag state releases.
            Assert.Equal(PanelDockCorner.TopTrailing, model.Settings.UsagePanelDock);
            Assert.Same(panel, Assert.Single(LogicalQuery.SelfAndDescendants<UsageFloatingPanel>(view)));
        });

    // ---- PR-CHAT-158: the rail holds the column's leading edge through the glide; the slide is suppressed when held ---

    [Fact]
    [Trait("Req", "PR-CHAT-158")] // App/Views/Chat/ChatView.swift:122-123,713-720,747-768; ThreadTimeline.swift:203-207
    public void A_trailing_dock_glides_the_rows_on_a_transform_while_the_rail_stays_on_the_columns_leading_edge()
        => WithColumn((h, model, _, view, window, frames) =>
        {
            var timeline = Timeline(view);
            var rail = Assert.Single(LogicalQuery.SelfAndDescendants<TimelineMinimapColumn>(view));
            double RailX() => LogicalQuery.SelfAndDescendants<TimelineMinimapColumn>(view).Single().TranslatePoint(default, view)!.Value.X;
            var resting = RailX();
            h.Main.OnMain(() => { model.Settings.UsagePanelDock = PanelDockCorner.TopTrailing; model.Settings.ShowsUsagePanel = true; });
            Drain(h, window);
            var scene = Field<PanelScene>(view, "scene");
            Assert.True(scene.Reserve.Trailing > 0);
            Assert.Equal(0, scene.Reserve.Leading);
            Assert.Equal(new Thickness(0, 0, scene.Reserve.Trailing, 0), timeline.Margin); // one layout at the final width.
            Assert.Equal(scene.Reserve.Trailing / 2, ChatView.GetReserveSlideOffset(timeline), 6); // starts where it was.
            Assert.Equal(resting, RailX(), 3);
            h.Main.AdvanceMilliseconds(90); frames.Advance(TimeSpan.Zero); Drain(h, window);
            var mid = ChatView.GetReserveSlideOffset(timeline);
            Assert.InRange(mid, 0.001, scene.Reserve.Trailing / 2 - 0.001); // gliding on the transform.
            Assert.Equal(resting, RailX(), 3); // the rail cancels the glide (ThreadTimeline.swift:203-207).
            h.Main.Advance(TimeSpan.FromSeconds(Chrome.PanelSlide.Duration)); frames.Advance(TimeSpan.Zero); Drain(h, window);
            Assert.Equal(0, ChatView.GetReserveSlideOffset(timeline));
            Assert.Equal(resting, RailX(), 3);
        }, messages: true);

    [Theory]
    [InlineData("live")]
    [InlineData("terminal")]
    [InlineData("grip")]
    [Trait("Req", "PR-CHAT-158")] // App/Views/Chat/ChatView.swift:63-65,123,141 slides only when measured and not resizing, held or switching
    public void A_dock_while_the_pane_is_reshaped_or_a_grip_is_held_snaps_with_no_glide(string held)
        => WithColumn((h, model, runtime, view, window, frames) =>
        {
            h.Main.OnMain(() =>
            {
                switch (held)
                {
                    case "live":
                        typeof(WindowLiveResize).GetProperty(nameof(WindowLiveResize.IsActive))!.SetValue(AppEnvironment.GetWindowLiveResize(view), true);
                        break;
                    case "terminal":
                        runtime.IsTerminalResizing = true;
                        break;
                    default:
                        Field<PanelResizeState>(view, "panelResize").Begin(new Size(300, 300));
                        break;
                }
            });
            Drain(h, window);
            h.Main.OnMain(() => { model.Settings.UsagePanelDock = PanelDockCorner.TopTrailing; model.Settings.ShowsUsagePanel = true; });
            Drain(h, window);
            var scene = Field<PanelScene>(view, "scene");
            Assert.True(scene.Reserve.Trailing > 0);
            Assert.Equal(0, ChatView.GetReserveSlideOffset(Timeline(view))); // :762 offset = 0 at once.
            var composer = Assert.Single(LogicalQuery.SelfAndDescendants<Marrow.App.Views.Composer.ComposerArea>(view));
            Assert.Equal(scene.Reserve.Trailing, composer.Margin.Right); // :755 the box follows without a spring.
        });

    // ---- PR-CHAT-160 / UI-CHAT-003: the memoized timeline and the minimap flag the column hands it ----------------------

    [Fact]
    [Trait("Req", "PR-CHAT-160")] // App/Views/Chat/ChatView.swift:97-111; ThreadTimeline.swift:26-35
    public void A_title_effort_or_mode_change_leaves_the_timeline_alone_while_a_handed_value_redraws_it()
        => WithColumn((h, model, runtime, view, window, _) =>
        {
            var timeline = Timeline(view);
            var passes = timeline.DrawPasses; // the passes of the timeline's body: a direct count, whatever it rebuilds or reuses inside one.
            Assert.True(passes > 0);
            h.Main.OnMain(() =>
            {
                model.Rename(runtime.ThreadID, "A new title");
                model.UpdateThread(runtime.ThreadID, t => t with { RuntimeMode = RuntimeMode.FullAccess });
                model.UpdateThread(runtime.ThreadID, t => t with { Effort = t.Effort == "high" ? "low" : "high" });
            });
            Drain(h, window);
            Assert.Same(timeline, Timeline(view));
            Assert.Equal(passes, timeline.DrawPasses); // never drawn again.
            Assert.Contains(LogicalQuery.SelfAndDescendants<TextBlock>(view), t => t.Text == "A new title"); // the chat view itself did follow.
            h.Main.OnMain(() => window.Height = 800); // the column height is one of the eight values.
            Drain(h, window);
            Assert.Same(timeline, Timeline(view));
            Assert.True(timeline.DrawPasses > passes);
        }, messages: true);

    [Theory]
    [InlineData(1400, null, true)] // free width 1400 >= 900.
    [InlineData(899, null, false)] // under 900 with no panel.
    [InlineData(1400, PanelDockCorner.BottomLeading, false)] // a panel docked on the leading side takes the rail's edge.
    [InlineData(1400, PanelDockCorner.TopTrailing, true)] // a trailing panel leaves at least 900 here.
    [Trait("Req", "PR-CHAT-160")] // App/Views/Chat/ChatView.swift:105-109 (the minimap flag is one of the handed values)
    [Trait("Req", "UI-CHAT-003")] // App/Views/Chat/ChatView.swift:108-109
    public void The_column_hands_the_timeline_the_minimap_flag_from_the_docked_sides_and_the_free_width(double width, PanelDockCorner? dock, bool shown)
        => WithColumn((h, model, _, view, window, frames) =>
        {
            // The first measurement may turn the flag off: the rail then fades with panelSlide (ThreadTimeline.swift:197-199).
            h.Main.Advance(TimeSpan.FromSeconds(Chrome.PanelSlide.Duration)); frames.Advance(TimeSpan.Zero); Drain(h, window);
            Assert.Equal(shown, LogicalQuery.SelfAndDescendants<TimelineMinimapColumn>(view).Any());
            var scene = Field<PanelScene>(view, "scene");
            var pane = Field<Size>(view, "paneSize");
            Assert.Equal(shown, !scene.DockedSides.Contains(PanelDockSide.Leading) && pane.Width - scene.Reserve.Leading - scene.Reserve.Trailing >= 900);
        }, width, model =>
        {
            if (dock is { } corner) { model.Settings.UsagePanelDock = corner; model.Settings.ShowsUsagePanel = true; }
        }, messages: true);

    [Fact]
    [Trait("Req", "UI-CHAT-003")] // App/Views/Chat/ChatView.swift:108-109 `paneSize == .zero ||`: shown on the unmeasured first frame.
    public void Before_the_pane_is_measured_the_timeline_is_handed_a_shown_minimap()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            var model = h.MakeModel();
            h.Main.OnMain(() =>
            {
                var runtime = model.Runtime(model.NewThread(model.AddProject(new Project("P", h.NewFolder("p")).Url))!.Id);
                var view = new ChatView(runtime);
                // The body's first pass, before any layout has measured the pane (:17 paneSize = .zero).
                typeof(ChatView).GetField("model", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(view, model);
                typeof(ChatView).GetMethod("RefreshInputs", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, null);
                var timeline = Timeline(view);
                Assert.Equal(default, Field<Size>(view, "paneSize"));
                var inputs = typeof(ThreadTimeline).GetField("inputs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(timeline)!;
                var rest = inputs.GetType().GetField("Rest")!.GetValue(inputs)!; // the eighth element of the eight handed values.
                Assert.True((bool)rest.GetType().GetField("Item1")!.GetValue(rest)!); // ThreadTimeline.swift:22,35 showsMinimap.
            });
        });
    }

    // ---- UI-CHAT-003: the rail fades with the slide of the panel that takes its edge ---------------------------------

    [Fact]
    [Trait("Req", "UI-CHAT-003")] // App/Views/Chat/ChatView.swift:108-109; ThreadTimeline.swift:178-199 .transition(.opacity), .animation(panelSlide, value: showsMinimap)
    public void A_leading_dock_fades_the_rail_out_with_the_panel_slide_and_an_undock_fades_it_back()
        => WithColumn((h, model, _, view, window, frames) =>
        {
            TimelineMinimapColumn[] Rails() => LogicalQuery.SelfAndDescendants<TimelineMinimapColumn>(view).ToArray();
            double Seen(Control rail)
            {
                var opacity = 1.0;
                for (Control? at = rail; at is not null && at != view; at = at.Parent as Control) opacity *= at.Opacity;
                return opacity;
            }
            var rail = Assert.Single(Rails());
            Assert.Equal(1, Seen(rail));
            double X(Control control) => control.TranslatePoint(default, view)!.Value.X;
            var resting = X(rail);
            h.Main.OnMain(() => { model.Settings.UsagePanelDock = PanelDockCorner.BottomLeading; model.Settings.ShowsUsagePanel = true; });
            Drain(h, window);
            var leaving = Assert.Single(Rails()); // still on screen: fading, not cut.
            Assert.True(Seen(leaving) > 0.5);
            Assert.True(Field<PanelScene>(view, "scene").Reserve.Leading > 0);
            Assert.Equal(resting, X(leaving), 3); // it keeps the column's leading edge while it fades (:203-207).
            h.Main.AdvanceMilliseconds(90); frames.Advance(TimeSpan.Zero); Drain(h, window);
            Assert.InRange(Seen(Assert.Single(Rails())), 0.001, 0.999);
            Assert.Equal(resting, X(leaving), 3);
            h.Main.Advance(TimeSpan.FromSeconds(Chrome.PanelSlide.Duration)); frames.Advance(TimeSpan.Zero); Drain(h, window);
            Assert.Empty(Rails());
            h.Main.OnMain(() => model.Settings.ShowsUsagePanel = false);
            Drain(h, window);
            var arriving = Assert.Single(Rails());
            Assert.True(Seen(arriving) < 0.5);
            h.Main.Advance(TimeSpan.FromSeconds(Chrome.PanelSlide.Duration)); frames.Advance(TimeSpan.Zero); Drain(h, window);
            Assert.Equal(1, Seen(Assert.Single(Rails())), 3);
        }, messages: true);

    [Fact]
    [Trait("Req", "UI-CHAT-003")] // ThreadTimeline.swift:197-199 held mid-resize: no fade while the pane is reshaped
    public void While_the_window_is_resized_the_rail_leaves_at_once()
        => WithColumn((h, model, _, view, window, _) =>
        {
            Assert.Single(LogicalQuery.SelfAndDescendants<TimelineMinimapColumn>(view));
            h.Main.OnMain(() =>
            {
                typeof(WindowLiveResize).GetProperty(nameof(WindowLiveResize.IsActive))!.SetValue(AppEnvironment.GetWindowLiveResize(view), true);
                model.Settings.UsagePanelDock = PanelDockCorner.BottomLeading;
                model.Settings.ShowsUsagePanel = true;
            });
            Drain(h, window);
            Assert.Empty(LogicalQuery.SelfAndDescendants<TimelineMinimapColumn>(view));
        }, messages: true);
}
