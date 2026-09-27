using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Views.Chat;

[Collection("Headless")]
public sealed class SubagentPanelSweepTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "PR-HYDRA-226")] // App/Views/Chat/SubagentPanel.swift:530-547; UI/Chrome/Chrome.swift:99-104
    public void Drag_is_immediate_release_animates_and_live_resize_cancels_settling(bool reducedMotion)
    {
        headless.OnUiThread(() =>
        {
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-09-27T00:00:00Z"));
            using var timeScope = SwiftClock.Provider.Override(time);
            using var motionScope = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = reducedMotion });
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            runtime.OnMain(() =>
            {
                var content = new Border();
                var drag = new PanelDragState();
                var rest = new Point(20, 72);
                var layout = new SubagentPanelLayout { Pane = new Size(1000, 800), ComposerAreaHeight = 100 };
                var placed = new PlacedPanel(drag, rest, new Size(400, 300), content);
                var window = new Avalonia.Controls.Window { Content = placed };
                var resize = new WindowLiveResize(window);
                AppEnvironment.SetWindowLiveResize(placed, resize);
                EnvironmentValues.SetAccessibilityReduceMotion(placed, reducedMotion);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                try
                {
                    drag.Move(new Size(100, 80), rest, layout);
                    Dispatcher.UIThread.RunJobs();
                    var wrapper = Assert.IsAssignableFrom<Control>(placed.Child);
                    Assert.Equal(120, Assert.IsType<TranslateTransform>(wrapper.RenderTransform).X);
                    Assert.Equal(152, Assert.IsType<TranslateTransform>(wrapper.RenderTransform).Y);
                    Assert.Equal(0, frames.Pending);
                    drag.Release();
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(120, Assert.IsType<TranslateTransform>(wrapper.RenderTransform).X);
                    Assert.Equal(1, frames.Pending);
                    time.Advance(TimeSpan.FromSeconds(0.09));
                    frames.Advance(TimeSpan.FromSeconds(0.09));
                    var progress = (reducedMotion ? Animation.EaseOut(0.18) : Animation.Spring(0.32, 0.9)).Progress(0.09);
                    Assert.Equal(120 + (20 - 120) * progress, Assert.IsType<TranslateTransform>(wrapper.RenderTransform).X, 8);
                    ResizeMessage(resize, 0x0231);
                    ResizeMessage(resize, 0x0214);
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(20, Assert.IsType<TranslateTransform>(wrapper.RenderTransform).X);
                    Assert.Equal(0, frames.Pending);
                    Assert.Same(content, Assert.IsAssignableFrom<Decorator>(wrapper).Child);
                    // Swift animates both origin and size as its immutable placement inputs change.
                    var update = typeof(PlacedPanel).GetMethod("UpdatePlacement");
                    Assert.NotNull(update);
                    update.Invoke(placed, [new Point(50, 90), new Size(450, 350), false]);
                    Assert.Equal(50, Assert.IsType<TranslateTransform>(wrapper.RenderTransform).X);
                    Assert.Equal(450, wrapper.Width);
                    Assert.Equal(0, frames.Pending);
                    ResizeMessage(resize, 0x0232);
                    Dispatcher.UIThread.RunJobs();
                    update.Invoke(placed, [new Point(70, 100), new Size(500, 400), false]);
                    Assert.Equal(50, Assert.IsType<TranslateTransform>(wrapper.RenderTransform).X);
                    Assert.Equal(450, wrapper.Width);
                    Assert.Equal(1, frames.Pending);
                    time.Advance(TimeSpan.FromSeconds(1));
                    frames.Advance(TimeSpan.FromSeconds(1));
                    Assert.Equal(70, Assert.IsType<TranslateTransform>(wrapper.RenderTransform).X);
                    Assert.Equal(500, wrapper.Width);
                    Assert.Equal(400, wrapper.Height);
                    update.Invoke(placed, [new Point(90, 120), new Size(520, 420), true]);
                    Assert.Equal(90, Assert.IsType<TranslateTransform>(wrapper.RenderTransform).X);
                    Assert.Equal(520, wrapper.Width);
                    Assert.Equal(0, frames.Pending);
                }
                finally { window.Close(); }
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-230")] // App/Views/Chat/SubagentPanel.swift:271-297,314-326
    public void Head_details_switch_live_helpers_keep_transcripts_and_running_close_requires_confirmation()
    {
        headless.OnUiThread(() =>
        {
            using var h = new AppModelHarness(withRuntime: false);
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            var project = AppModelSamples.Project("Panel", h.NewFolder("panel"));
            var thread = AppModelSamples.Thread(project) with { Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Running) };
            h.WriteLibrary([project], [thread]);
            AppModelHarness.WriteDocument(thread.Id);
            Marrow.Services.Store.DocumentPrefetch.Shared.Warm([thread.Id], urgent: true)?.Completion.GetAwaiter().GetResult();
            var manager = new WindowManager();
            using var shared = WindowManager.OverrideShared(manager);
            var model = manager.Model;
            model.Settings.HydraShowsHeadDetails = false;
            var runtime = model.Runtime(thread.Id);
            var closed = 0;
            var panel = MakePanel(thread, project, () => closed++);
            AppEnvironment.SetAppModel(panel, model);
            var window = new Avalonia.Controls.Window { Content = panel };
            AppEnvironment.SetWindowLiveResize(panel, new WindowLiveResize(window));
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                Assert.Single(Descendants<HydraHeadProgress>(panel));
                Assert.Single(Descendants<ComposerArea>(panel));
                model.Settings.HydraShowsHeadDetails = true;
                Dispatcher.UIThread.RunJobs();
                Assert.Empty(Descendants<HydraHeadProgress>(panel));
                Assert.Single(Descendants<ThreadTimeline>(panel));
                Assert.Single(Descendants<ComposerArea>(panel));
                var close = Descendants<ChromeCircleButton>(panel).Single(button => Equals(ToolTip.GetTip(button), "Close this chat"));
                typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.Phase))!.SetValue(runtime, RuntimePhase.Running);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("Close this chat and stop its turn", ToolTip.GetTip(close));
                Descendants<Button>(close).Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(0, closed);
                Assert.True(panel.GetLogicalDescendants().OfType<Avalonia.Controls.Primitives.Popup>().Single().IsOpen);
                typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.Phase))!.SetValue(runtime, RuntimePhase.Idle);
                Dispatcher.UIThread.RunJobs();
                Descendants<Button>(close).Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(1, closed);
                // A helper without Hydra remains a full conversation even with head details off.
                model.Settings.HydraShowsHeadDetails = false;
                var helper = MakePanel(thread with { Hydra = null }, project, () => { });
                Assert.Empty(Descendants<HydraHeadProgress>(helper));
                Assert.Single(Descendants<ThreadTimeline>(helper));
                Assert.Single(Descendants<ComposerArea>(helper));
            }
            finally { window.Close(); runtime.CancelPersistence(); }
        });
    }

    private static IEnumerable<T> Descendants<T>(Control root) => root.GetSelfAndLogicalDescendants().OfType<T>();

    // STOP App.Views.Chat.SubagentPanel #1
    // Kind: missing dependency. Swift: App/Views/Chat/SubagentPanel.swift:294-297.
    // Records: PR-HYDRA-230. JumpToLatestButton still throws MARROW_STUB in the excluded ChatView.cs.
    // Needed: its ChatView unit implementation before an end-to-end public panel can be constructed.
    // These tests substitute only that unrelated control; transcript/composer/close behavior stays real.
    private static SubagentPanel MakePanel(ChatThread thread, Project project, Action close)
    {
        var constructor = typeof(SubagentPanel).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .SingleOrDefault(value => value.GetParameters().Length == 8);
        Assert.NotNull(constructor);
        return (SubagentPanel)constructor.Invoke([thread, new Size(400, 500), project.Path, project.Name,
            (Action<Size>)(_ => { }), (Action)(() => { }), close, (Func<TimelineScrollState, Control>)(_ => new Border())]);
    }

    private static void ResizeMessage(WindowLiveResize resize, uint message)
    {
        var method = typeof(WindowLiveResize).GetMethod("WndProc", BindingFlags.Instance | BindingFlags.NonPublic)!;
        method.Invoke(resize, [(nint)0, message, (nint)0, (nint)0, false]);
    }
}
