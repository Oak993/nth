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
    [Trait("Req", "PR-HYDRA-226")] // App/Views/Chat/SubagentPanel.swift:329,498-523,530-547; UI/Chrome/Chrome.swift:99-104
    public void Resizable_placement_keeps_conversation_and_grips_at_the_presented_size(bool reducedMotion)
    {
        headless.OnUiThread(() =>
        {
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            var time = new ManualTimeProvider(new DateTimeOffset(2026, 9, 27, 0, 0, 0, TimeSpan.Zero));
            using var timeScope = SwiftClock.Provider.Override(time);
            using var motionScope = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = reducedMotion });
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var content = new Border();
            var resize = new PanelResize
            {
                Corner = PanelDockCorner.TopLeading,
                OnResize = (_, _) => { }, OnResizeEnd = () => { }, OnReset = () => { },
            };
            var placed = new PlacedPanel(new PanelDragState(), new Point(20, 72), new Size(400, 300), content, resize);
            // Measure the source's fractional animation frame, before platform pixel rounding.
            var window = new Avalonia.Controls.Window { Content = placed, UseLayoutRounding = false };
            AppEnvironment.SetWindowLiveResize(placed, new WindowLiveResize(window));
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                var placement = Assert.IsAssignableFrom<Decorator>(placed.Child);
                var overlay = Assert.IsType<Panel>(placement.Child);
                var grips = Assert.Single(overlay.Children.OfType<PanelResizeGrips>());
                CheckSize(400, 300);
                placed.UpdatePlacement(new Point(50, 90), new Size(450, 350), true);
                CheckSize(450, 350);
                Assert.Equal(0, frames.Pending);
                placed.UpdatePlacement(new Point(70, 100), new Size(500, 400));
                CheckSize(450, 350);
                Assert.Equal(1, frames.Pending);
                time.Advance(TimeSpan.FromSeconds(0.09));
                frames.Advance(TimeSpan.FromSeconds(0.09));
                var progress = (reducedMotion ? Animation.EaseOut(0.18) : Animation.Spring(response: 0.32, dampingFraction: 0.9)).Progress(0.09);
                CheckSize(450 + 50 * progress, 350 + 50 * progress);
                time.Advance(TimeSpan.FromSeconds(1));
                frames.Advance(TimeSpan.FromSeconds(1));
                CheckSize(500, 400);
                Assert.Equal(0, frames.Pending);

                void CheckSize(double width, double height)
                {
                    window.UpdateLayout();
                    Assert.Same(content, overlay.Children[0]);
                    Assert.Equal(width, content.Width, 8);
                    Assert.Equal(height, content.Height, 8);
                    Assert.Equal(width, overlay.Bounds.Width, 8);
                    Assert.Equal(height, overlay.Bounds.Height, 8);
                    Assert.Equal(width, grips.Bounds.Width, 8);
                    Assert.Equal(height, grips.Bounds.Height, 8);
                }
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "PR-HYDRA-226")] // App/Views/Chat/SubagentPanel.swift:530-547; UI/Chrome/Chrome.swift:99-104
    public void Drag_is_immediate_release_animates_and_live_resize_cancels_settling(bool reducedMotion)
    {
        headless.OnUiThread(() =>
        {
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            var time = new ManualTimeProvider(new DateTimeOffset(2026, 9, 27, 0, 0, 0, TimeSpan.Zero));
            using var timeScope = SwiftClock.Provider.Override(time);
            using var motionScope = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = reducedMotion });
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
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
                    var progress = (reducedMotion ? Animation.EaseOut(0.18) : Animation.Spring(response: 0.32, dampingFraction: 0.9)).Progress(0.09);
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
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-230")] // App/Views/Chat/SubagentPanel.swift:271-297,314-326
    [Trait("Req", "UI-HYDRA-067")] // App/Views/Chat/SubagentPanel.swift:314-326 the close mark: xmark, padded 12 top and 14 trailing, the two tooltips, confirm while running
    public void Head_details_switch_live_helpers_keep_transcripts_and_running_close_requires_confirmation()
    {
        headless.OnUiThread(() =>
        {
            using var h = new AppModelHarness(withRuntime: false);
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            // Headless has no native popup backend; observe the same presentation request through the
            // established IPopoverPresenter harness (HeadlessFixtures), retaining the true/open assertion.
            var presenter = new RecordingPresenter();
            using var presentation = Presentation.Presenter.Override(presenter);
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
            var window = new Avalonia.Controls.Window { Content = new Avalonia.Controls.Primitives.VisualLayerManager { EnableOverlayLayer = true, Child = panel } };
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
                // :315-322 ChromeCircleButton(symbol: "xmark") padded Chrome.chromeTopPadding (12) on top and
                // Chrome.chromeHorizontalPadding (14) at the trailing edge.
                Assert.Contains(Descendants<Marrow.UI.Port.SwiftImage>(close), image => image.SymbolName == "xmark");
                Assert.Equal(new Thickness(0, 12, 14, 0), close.Margin);
                typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.Phase))!.SetValue(runtime, RuntimePhase.Running);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("Close this chat and stop its turn", ToolTip.GetTip(close));
                Descendants<Button>(close).Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(0, closed);
                Assert.True(Assert.Single(presenter.Calls).Presented);
                var confirmation = presenter.Calls.Single().Popup; // :323 arrowEdge: .bottom: the confirmation opens below the close mark
                Assert.Equal(Avalonia.Controls.PlacementMode.Bottom, confirmation.Placement);
                Assert.Same(close, confirmation.PlacementTarget);
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
            finally
            {
                window.Close();
                runtime.CancelPersistence();
                // ComposerView.swift:216-218,1140-1154: closing cancels the view's prepare, but its
                // shared build survives. Join it before the fixture deletes this directory so a later
                // test's two-build limit cannot wait on work left behind by this panel.
                var preparation = new FileIndex().Prepare(project.Path);
                HeadlessSession.RunDispatcherUntil(preparation, AppModelHarness.Bound);
                preparation.GetAwaiter().GetResult();
            }
        });
    }

    // PR-HYDRA-230 `.overlay(alignment: .top) { JumpToLatestButton }` on the composer (SubagentPanel.swift:292-297): with the REAL control the
    // circle floats 10 points above the chat box's top edge, centred on it. (The seam factory only captures the panel's scroll state.)
    [Fact]
    [Trait("Req", "PR-HYDRA-230")] // App/Views/Chat/SubagentPanel.swift:292-297 with ChatView.swift:1089-1103
    [Trait("Rule", "R-TEST-4")] // partial proof of PR-HYDRA-230: the jump button's place; the other clauses are asserted by the tests beside it
    public void Jump_to_latest_floats_above_the_chat_box()
    {
        headless.OnUiThread(() =>
        {
            using var h = new AppModelHarness();
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var project = AppModelSamples.Project("Panel", h.NewFolder("panel"));
            var thread = AppModelSamples.Thread(project);
            h.WriteLibrary([project], [thread]);
            AppModelHarness.WriteDocument(thread.Id);
            Marrow.Services.Store.DocumentPrefetch.Shared.Warm([thread.Id], urgent: true)?.Completion.GetAwaiter().GetResult();
            var manager = new WindowManager();
            using var shared = WindowManager.OverrideShared(manager);
            TimelineScrollState state = null!;
            var panel = new SubagentPanel(thread, new Size(400, 500), project.Path, project.Name, _ => { }, () => { }, () => { },
                scrollState => { state = scrollState; return new JumpToLatestButton(scrollState); });
            AppEnvironment.SetAppModel(panel, manager.Model);
            var window = new Avalonia.Controls.Window { Width = 400, Height = 500, UseLayoutRounding = false, Content = new Avalonia.Controls.Primitives.VisualLayerManager { EnableOverlayLayer = true, Child = panel } };
            AppEnvironment.SetWindowLiveResize(panel, new WindowLiveResize(window));
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                state.ShowsJumpButton = true;
                Dispatcher.UIThread.RunJobs();
                h.Main.Advance(TimeSpan.FromSeconds(Chrome.PanelSlide.Duration)); frames.Advance(TimeSpan.FromSeconds(Chrome.PanelSlide.Duration));
                Dispatcher.UIThread.RunJobs();
                var circle = Descendants<Button>(panel).Single(button => Equals(ToolTip.GetTip(button), "Jump to latest")); // the circle chrome's own button
                var composer = Descendants<ComposerArea>(panel).Single();
                Rect Where(Control control) => new(control.TranslatePoint(default, panel) ?? default, control.Bounds.Size);
                Assert.Equal(Where(composer).Top - 10, Where(circle).Bottom, 1); // the zero-height button sits on the composer's top edge; its circle rides 10 above
                Assert.Equal(Where(composer).Center.X, Where(circle).Center.X, 1);
            }
            finally
            {
                window.Close();
                h.Main.OnMain(() => manager.Model.Runtime(thread.Id).CancelPersistence());
                var preparation = new FileIndex().Prepare(project.Path);
                HeadlessSession.RunDispatcherUntil(preparation, AppModelHarness.Bound);
                preparation.GetAwaiter().GetResult();
            }
        });
    }

    // The arguments the panel hands its children (SubagentPanel.swift:268-327): the transcript is built without a rewind and without a rail at the
    // panel's height; the chat box is the compact, non-focus-taking one with no changes tab; the strip's handle is "Drag to move" at the handle height.
    [Fact]
    [Trait("Req", "PR-HYDRA-230")] // App/Views/Chat/SubagentPanel.swift:268-327
    [Trait("Rule", "R-TEST-4")] // partial proof of PR-HYDRA-230: the panel's arguments to its children (private fields read by reflection, nothing public observes them)
    public void Panel_arguments_reach_their_children()
    {
        headless.OnUiThread(() =>
        {
            using var h = new AppModelHarness(withRuntime: false);
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            using var frameScope = FrameClock.Source.Override(new ManualFrameSource());
            var project = AppModelSamples.Project("Panel", h.NewFolder("panel"));
            var thread = AppModelSamples.Thread(project);
            h.WriteLibrary([project], [thread]);
            AppModelHarness.WriteDocument(thread.Id);
            Marrow.Services.Store.DocumentPrefetch.Shared.Warm([thread.Id], urgent: true)?.Completion.GetAwaiter().GetResult();
            var manager = new WindowManager();
            using var shared = WindowManager.OverrideShared(manager);
            var panel = new SubagentPanel(thread, new Size(400, 500), project.Path, project.Name, _ => { }, () => { }, () => { });
            AppEnvironment.SetAppModel(panel, manager.Model);
            var window = new Avalonia.Controls.Window { Width = 400, Height = 500, Content = new Avalonia.Controls.Primitives.VisualLayerManager { EnableOverlayLayer = true, Child = panel } };
            AppEnvironment.SetWindowLiveResize(panel, new WindowLiveResize(window));
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
                var timeline = Assert.Single(Descendants<ThreadTimeline>(panel));
                var inputs = typeof(ThreadTimeline).GetField("inputs", instance)!.GetValue(timeline)!;
                Assert.False((bool)inputs.GetType().GetField("Item6")!.GetValue(inputs)!); // supportsRewind: false
                Assert.Equal(500d, (double)inputs.GetType().GetField("Item7")!.GetValue(inputs)!); // columnHeight: size.height
                var rest = inputs.GetType().GetField("Rest")!.GetValue(inputs)!; // the eighth element of the eight-tuple
                Assert.False((bool)rest.GetType().GetField("Item1")!.GetValue(rest)!); // showsMinimap: false
                var composerView = Assert.Single(Descendants<ComposerView>(panel));
                Assert.True((bool)typeof(ComposerView).GetField("compactModelChip", instance)!.GetValue(composerView)!);
                Assert.False((bool)typeof(ComposerView).GetField("takesFocusOnAppear", instance)!.GetValue(composerView)!);
                var slot = Assert.Single(Descendants<TabSlot>(panel));
                Assert.False((bool)typeof(TabSlot).GetField("showsChanges", instance)!.GetValue(slot)!);
                var handle = Descendants<PanelDragHandle>(panel).Single();
                Assert.Equal("Drag to move", ToolTip.GetTip(handle));
                Assert.Equal("Drag to move", Avalonia.Automation.AutomationProperties.GetName(handle));
                Assert.Equal(Chrome.ChromeTopPadding + Chrome.CapsuleHeight + 6, handle.Height);
                Assert.Equal(12, Descendants<SubagentWorkingBox>(panel).Single().Margin.Top); // .padding(.top, Chrome.chromeTopPadding)
            }
            finally
            {
                window.Close();
                manager.Model.Runtime(thread.Id).CancelPersistence();
                var preparation = new FileIndex().Prepare(project.Path);
                HeadlessSession.RunDispatcherUntil(preparation, AppModelHarness.Bound);
                preparation.GetAwaiter().GetResult();
            }
        });
    }

    private static IEnumerable<T> Descendants<T>(Control root) => root.GetSelfAndLogicalDescendants().OfType<T>();

    // The public constructor: the real JumpToLatestButton (ChatView.cs:1155-1190) is mounted, not a stand-in (the earlier STOP #1 is resolved).
    private static SubagentPanel MakePanel(ChatThread thread, Project project, Action close) =>
        new(thread, new Size(400, 500), project.Path, project.Name, _ => { }, () => { }, close);

    private static void ResizeMessage(WindowLiveResize resize, uint message)
    {
        var method = typeof(WindowLiveResize).GetMethod("WndProc", BindingFlags.Instance | BindingFlags.NonPublic)!;
        method.Invoke(resize, [(nint)0, message, (nint)0, (nint)0, false]);
    }
}
