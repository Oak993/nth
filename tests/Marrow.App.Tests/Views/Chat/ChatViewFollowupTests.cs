using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.App.Views.Terminal;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Avalonia.Threading;

namespace Marrow.App.Tests.Views.Chat;

[Collection("Headless")]
public sealed class ChatViewFollowupTests(HeadlessSession headless)
{
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;
    private static void Call(object owner, string name, params object?[] values) => owner.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(owner, values);
    private static ThreadTimeline Timeline(ChatView view) => Assert.Single(LogicalQuery.SelfAndDescendants<ThreadTimeline>(view));
    private static void Drain(AppModelHarness h)
    {
        h.Main.RunUntilIdle();
        h.Main.OnMain(() => Dispatcher.UIThread.RunJobs());
        h.Main.RunUntilIdle();
    }

    private void WithChat(Action<ChatView, ThreadRuntime, AppModel, AppModelHarness, Avalonia.Controls.Window, ManualFrameSource> check) =>
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            using var cursor = DragHandleView.CursorPlatform.Override(new ChatNoCursor());
            var frames = new ManualFrameSource();
            using var clock = FrameClock.Source.Override(frames);
            var project = new Project("Project", h.NewFolder("project"));
            h.WriteLibrary([project], []);
            var model = h.MakeModel();
            Avalonia.Controls.Window? window = null;
            try
            {
                ChatView view = null!;
                ThreadRuntime runtime = null!;
                h.Main.OnMain(() =>
                {
                    model.Settings.ShowsUsagePanel = false;
                    model.Settings.HydraEnabled = false;
                    runtime = model.Runtime(model.NewThread(project)!.Id);
                    view = new ChatView(runtime);
                    window = new Avalonia.Controls.Window { Content = view, Width = 1400, Height = 900 };
                    AppEnvironment.SetAppModel(window, model);
                    AppEnvironment.SetWindowLiveResize(window, new WindowLiveResize(window));
                    ChatHeadlessFixtures.Track(window);
                    window.Show();
                    window.UpdateLayout();
                });
                Drain(h);
                check(view, runtime, model, h, window!, frames);
            }
            finally
            {
                h.Main.OnMain(() => { window?.Close(); model.Terminals.TerminateAll(); });
                Drain(h);
            }
        });

    [Fact]
    [Trait("Req", "PR-CHAT-158")] // App/Views/Chat/ChatView.swift:98-153,280-288
    [Trait("Req", "PR-CHAT-159")] // App/Views/Chat/ChatView.swift:143-145
    [Trait("Req", "PR-TERM-001")] // App/Views/Chat/ChatView.swift:275-288
    public void The_mounted_column_contains_the_timeline_composer_top_veil_and_chrome_without_a_terminal()
        => WithChat((view, runtime, _, _, _, _) =>
        {
            Assert.NotNull(Timeline(view));
            Assert.Single(LogicalQuery.SelfAndDescendants<ComposerArea>(view));
            Assert.Single(LogicalQuery.SelfAndDescendants<JumpToLatestButton>(view));
            Assert.Single(LogicalQuery.SelfAndDescendants<PaneTopVeil>(view));
            Assert.Single(LogicalQuery.SelfAndDescendants<ChatChromeRow>(view));
            Assert.Empty(LogicalQuery.SelfAndDescendants<TerminalPanel>(view));
            Assert.Equal(runtime.ThreadID, LinkParagraphView.GetMergeRequestTarget(view)!.ChatID); // :292-294
            Assert.Equal(1, view.Opacity); // :283-288 identity transition.
        });

    [Theory]
    [InlineData("subagentDrag")]
    [InlineData("hydraDrag")]
    [InlineData("poppedDrag")]
    [InlineData("usageDrag")]
    [InlineData("auto")]
    [Trait("Req", "PR-CHAT-157")] // App/Views/Chat/ChatView.swift:48-52,112-115,266-269
    public void Any_held_panel_disables_the_mounted_timeline_hit_tests_and_release_restores_them(string name)
        => WithChat((view, _, _, h, _, _) =>
        {
            PanelDragState drag = null!;
            h.Main.OnMain(() =>
            {
                if (name == "auto")
                {
                    var id = Guid.NewGuid();
                    Call(view, "NoteAutoPanelIDs", new Guid[] { id });
                    drag = Field<Dictionary<Guid, PanelDragState>>(view, "autoDrags")[id];
                }
                else drag = Field<PanelDragState>(view, name);
                var layout = Field<PanelScene>(view, "scene").Layout;
                drag.Move(new Size(10, 10), new Point(20, 20), layout);
            });
            Drain(h);
            Assert.False(Timeline(view).IsHitTestVisible);
            Assert.True(Assert.Single(LogicalQuery.SelfAndDescendants<ComposerArea>(view)).IsHitTestVisible);
            h.Main.OnMain(() => drag.Release());
            Drain(h);
            Assert.True(Timeline(view).IsHitTestVisible);
        });

    [Fact]
    [Trait("Req", "PR-CHAT-159")] // App/Views/Chat/ChatView.swift:143-145; UI/Chrome/Chrome.swift:568-579,585-624
    public void The_mounted_top_veil_uses_the_same_scroll_chrome_model_as_the_timeline()
        => WithChat((view, runtime, _, h, _, _) =>
        {
            var veil = Assert.Single(LogicalQuery.SelfAndDescendants<PaneTopVeil>(view));
            Assert.False(veil.IsHitTestVisible);
            h.Main.OnMain(() => typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.IsLoadingHistory))!.SetValue(runtime, true));
            Drain(h); // suppress the empty timeline's source-mandated reset while injecting measured scroll travel.
            h.Main.OnMain(() => Field<ChromeScrollModel>(view, "scrollChrome").Update(44));
            Drain(h);
            var layer = Assert.IsType<Panel>(veil.Child);
            Assert.Equal(Chrome.VeilHeight, layer.Height);
            Assert.Equal(1, layer.Opacity);
            Assert.Equal(2, layer.Children.Count);
        });

    [Fact]
    [Trait("Req", "PR-CHAT-158")] // App/Views/Chat/ChatView.swift:122-140,713-720,747-768
    public void A_usage_dock_reserves_the_mounted_conversation_and_composer_with_a_gliding_offset()
        => WithChat((view, _, model, h, window, frames) =>
        {
            var composer = Assert.Single(LogicalQuery.SelfAndDescendants<ComposerArea>(view));
            Assert.Equal(0, Timeline(view).Margin.Left);
            h.Main.OnMain(() => model.Settings.ShowsUsagePanel = true);
            Drain(h);
            var scene = Field<PanelScene>(view, "scene");
            Assert.True(scene.IsMeasured);
            Assert.True(scene.ShowsUsage);
            Assert.True(scene.Reserve.Leading > 0);
            var timeline = Timeline(view);
            Assert.Equal(scene.Reserve.Leading, timeline.Margin.Left); // final width snaps once.
            Assert.True(ChatView.GetReserveSlideOffset(timeline) < 0);
            Assert.Same(composer, Assert.Single(LogicalQuery.SelfAndDescendants<ComposerArea>(view)));
            h.Main.Advance(TimeSpan.FromSeconds(Chrome.PanelSlide.Duration));
            frames.Advance(TimeSpan.Zero);
            window.UpdateLayout();
            Assert.Equal(0, ChatView.GetReserveSlideOffset(Timeline(view)));
            Assert.Equal(scene.Reserve.Leading, composer.Margin.Left);
        });

    [Fact]
    [Trait("Rule", "R-FILE-1")] // App/Views/Chat/ChatView.swift:130-138 (composer-growth guard, no packet item names this callback).
    public void Composer_growth_ignores_first_measurement_half_a_point_and_live_resize()
        => WithChat((view, _, _, h, _, _) =>
        {
            h.Main.OnMain(() =>
            {
                var state = Field<TimelineScrollState>(view, "scrollState");
                var before = state.AreaGrowthRequest;
                typeof(ChatView).GetField("composerAreaHeight", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(view, 0d);
                Call(view, "NoteComposerAreaHeight", 100d);
                Call(view, "NoteComposerAreaHeight", 100.5d);
                Assert.Equal(before, state.AreaGrowthRequest);
                var resize = AppEnvironment.GetWindowLiveResize(view);
                typeof(WindowLiveResize).GetProperty(nameof(WindowLiveResize.IsActive))!.SetValue(resize, true);
                Call(view, "NoteComposerAreaHeight", 110d);
                Assert.Equal(before, state.AreaGrowthRequest);
                typeof(WindowLiveResize).GetProperty(nameof(WindowLiveResize.IsActive))!.SetValue(resize, false);
                Call(view, "NoteComposerAreaHeight", 111d);
                Assert.Equal(before + 1, state.AreaGrowthRequest);
            });
        });

    [Fact]
    [Trait("Req", "PR-TERM-001")] // App/Views/Chat/ChatView.swift:275-288
    public void The_terminal_flag_inserts_and_removes_the_actual_drawer_with_bottom_move_and_fade()
        => WithChat((view, runtime, _, h, window, frames) =>
        {
            Assert.True(Directory.Exists(Field<string>(view, "directory")), Field<string>(view, "directory"));
            h.Main.OnMain(() => runtime.IsTerminalVisible = true);
            Drain(h);
            window.UpdateLayout();
            var terminal = Assert.Single(LogicalQuery.SelfAndDescendants<TerminalPanel>(view));
            Assert.Equal(0, terminal.Opacity);
            Assert.True(terminal.RenderTransform!.Value.M32 > 0); // move(bottom), its measured height.
            h.Main.Advance(TimeSpan.FromSeconds(Chrome.PanelSlide.Duration)); frames.Advance(TimeSpan.Zero);
            Assert.Equal(1, terminal.Opacity);
            Assert.Equal(0, terminal.RenderTransform!.Value.M32);
            h.Main.OnMain(() => runtime.IsTerminalVisible = false);
            Drain(h);
            Assert.Contains(terminal, LogicalQuery.SelfAndDescendants<TerminalPanel>(view)); // remains through removal frames.
            h.Main.Advance(TimeSpan.FromSeconds(Chrome.PanelSlide.Duration)); frames.Advance(TimeSpan.Zero);
            Assert.Empty(LogicalQuery.SelfAndDescendants<TerminalPanel>(view));
            Assert.Equal(0, terminal.Opacity);
            Assert.True(terminal.RenderTransform!.Value.M32 > 0);
        });

    [Fact]
    [Trait("Rule", "R-FILE-1")] // App/Views/Chat/ChatView.swift:601-607 (shared panel transition; its two scale constants have no packet item).
    public void Floating_panels_grow_from_point_ninety_two_and_leave_at_point_ninety_six()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            var frames = new ManualFrameSource();
            using var clock = FrameClock.Source.Override(frames);
                var transition = (SwiftTransition)typeof(ChatView).GetProperty("PanelTransition", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
                var content = new Border().Transition(transition);
                var host = new Panel();
                Transaction.WithAnimation(Chrome.PanelSlide, () => SwiftTransition.Insert(host, 0, content));
                Assert.Equal(0.92, content.RenderTransform!.Value.M11, 8);
                Assert.Equal(0, content.Opacity);
                h.Main.Advance(TimeSpan.FromSeconds(Chrome.PanelSlide.Duration)); frames.Advance(TimeSpan.Zero);
                Assert.Equal(1, content.RenderTransform!.Value.M11);
                Transaction.WithAnimation(Chrome.PanelSlide, () => SwiftTransition.Remove(host, content));
                h.Main.Advance(TimeSpan.FromSeconds(Chrome.PanelSlide.Duration)); frames.Advance(TimeSpan.Zero);
                Assert.Equal(0.96, content.RenderTransform!.Value.M11, 8);
                Assert.Equal(0, content.Opacity);
                Assert.Empty(host.Children);
        });
    }

    [Fact]
    [Trait("Req", "UI-CHAT-006")] // App/Views/Chat/ChatView.swift:1089-1103
    public void Jump_to_latest_has_the_source_action_zero_height_and_asymmetric_transitions()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            var frames = new ManualFrameSource();
            using var clock = FrameClock.Source.Override(frames);
            TimelineScrollState state = null!; JumpToLatestButton view = null!;
            h.Main.OnMain(() =>
            {
                state = new TimelineScrollState(); view = new JumpToLatestButton(state);
                var window = new Avalonia.Controls.Window { Content = view, Width = 300, Height = 100 };
                ChatHeadlessFixtures.Track(window); window.Show();
                Assert.Empty(LogicalQuery.SelfAndDescendants<Button>(view));
                Transaction.WithAnimation(Chrome.PanelSlide, () => state.ShowsJumpButton = true);
            });
            Drain(h);
            var button = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(view));
            var circle = Assert.Single(LogicalQuery.SelfAndDescendants<ChromeCircleButton>(view)); // :1091 source transition encloses the whole chrome surface.
            var frame = Assert.IsAssignableFrom<Avalonia.Controls.Decorator>(circle.Parent); // :1094-1101 the transition is on the zero-height frame with the 10 points of bottom padding, not on the circle.
            Assert.Equal("Jump to latest", ToolTip.GetTip(button));
            Assert.Equal("arrow.down", Assert.Single(LogicalQuery.SelfAndDescendants<SwiftImage>(button)).SymbolName);
            Assert.Equal(0, view.Height);
            Assert.Equal(0, frame.Opacity); Assert.Equal(1, circle.Opacity);
            Assert.Equal(0.6, frame.RenderTransform!.Value.M11, 8);
            Assert.Equal(10, frame.RenderTransform!.Value.M32, 8);
            h.Main.Advance(TimeSpan.FromSeconds(Chrome.PanelSlide.Duration)); frames.Advance(TimeSpan.Zero);
            h.Main.OnMain(() => ChatGitFixtures.Click(button));
            Assert.Equal(1, state.JumpRequest);
            h.Main.OnMain(() => Transaction.WithAnimation(Chrome.PanelSlide, () => state.ShowsJumpButton = false));
            Drain(h);
            h.Main.Advance(TimeSpan.FromSeconds(Chrome.PanelSlide.Duration)); frames.Advance(TimeSpan.Zero);
            Assert.Equal(0, frame.Opacity);
            Assert.Equal(0.85, frame.RenderTransform!.Value.M11, 8);
            Assert.Equal(6, frame.RenderTransform!.Value.M32, 8);
            Assert.Empty(LogicalQuery.SelfAndDescendants<Button>(view));
        });
    }
}
