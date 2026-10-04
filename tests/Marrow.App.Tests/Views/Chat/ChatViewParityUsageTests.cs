using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
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

// PR-USAGE-069/070 on the mounted column: the panel's arrival and leaving, and the stored corner.
[Collection("Headless")]
public sealed class ChatViewParityUsageTests(HeadlessSession headless)
{
    private static void Drain(AppModelHarness h, AvaloniaWindow window)
    {
        h.Main.RunUntilIdle();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        h.Main.RunUntilIdle();
    }

    private static double Seen(Control control, Control root)
    {
        var opacity = 1.0;
        for (Control? at = control; at is not null && at != root; at = at.Parent as Control) opacity *= at.Opacity;
        return opacity;
    }

    [Fact]
    [Trait("Req", "PR-USAGE-069")] // App/Views/Chat/ChatView.swift:234-241,601-607 the panel appears and leaves with panelSlide
    public void Turning_the_setting_on_and_off_grows_the_usage_panel_in_and_fades_it_out_on_the_panel_slide()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            h.Main.Loop.Run(() => Marrow.Core.Support.LoginEnvironment.Load(), AppModelHarness.Bound);
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            using var cursor = DragHandleView.CursorPlatform.Override(new ChatNoCursor());
            var frames = new ManualFrameSource();
            using var clock = FrameClock.Source.Override(frames);
            h.WriteLibrary([new Project("Project", h.NewFolder("project"))], []);
            var model = h.MakeModel();
            h.PrepareBootstrap(model);
            AvaloniaWindow window = null!;
            ChatView view = null!;
            h.Main.OnMain(() =>
            {
                model.Settings.ShowsUsagePanel = false;
                model.Settings.HydraEnabled = false;
                view = new ChatView(model.Runtime(model.NewThread(model.Projects[0])!.Id));
                window = new AvaloniaWindow { Content = view, Width = 1400, Height = 900 };
                AppEnvironment.SetAppModel(window, model);
                AppEnvironment.SetWindowLiveResize(window, new WindowLiveResize(window));
                ChatHeadlessFixtures.Track(window);
                window.Show();
            });
            try
            {
                Drain(h, window);
                Assert.Empty(LogicalQuery.SelfAndDescendants<UsageFloatingPanel>(view));
                h.Main.OnMain(() => model.Settings.ShowsUsagePanel = true);
                Drain(h, window);
                var panel = Assert.Single(LogicalQuery.SelfAndDescendants<UsageFloatingPanel>(view));
                Assert.True(Seen(panel, view) < 0.5); // :601-604 insertion: scale 0.92 with opacity, not a cut.
                Assert.Equal(0.92, panel.RenderTransform!.Value.M11, 6);
                h.Main.AdvanceMilliseconds(100); frames.Advance(TimeSpan.Zero); Drain(h, window);
                Assert.InRange(Seen(panel, view), 0.001, 0.999);
                h.Main.Advance(TimeSpan.FromSeconds(Chrome.PanelSlide.Duration)); frames.Advance(TimeSpan.Zero); Drain(h, window);
                Assert.Equal(1, Seen(panel, view), 6);
                h.Main.OnMain(() => model.Settings.ShowsUsagePanel = false);
                Drain(h, window);
                Assert.Same(panel, Assert.Single(LogicalQuery.SelfAndDescendants<UsageFloatingPanel>(view))); // :605 leaving, still drawn.
                h.Main.AdvanceMilliseconds(100); frames.Advance(TimeSpan.Zero); Drain(h, window);
                Assert.InRange(Seen(panel, view), 0.001, 0.999);
                h.Main.Advance(TimeSpan.FromSeconds(Chrome.PanelSlide.Duration)); frames.Advance(TimeSpan.Zero); Drain(h, window);
                Assert.Empty(LogicalQuery.SelfAndDescendants<UsageFloatingPanel>(view));
            }
            finally { h.Main.OnMain(() => { window.Close(); model.Terminals.TerminateAll(); }); Drain(h, window); }
        });
    }

    [Fact]
    [Trait("Req", "PR-USAGE-069")] // App/Views/Chat/ChatView.swift:240 .animation(_, value: scene.usage): the first measurement changes no keyed value
    public void A_column_opened_with_the_setting_on_shows_its_usage_panel_in_place_without_growing_it_in()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            h.Main.Loop.Run(() => Marrow.Core.Support.LoginEnvironment.Load(), AppModelHarness.Bound);
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            using var cursor = DragHandleView.CursorPlatform.Override(new ChatNoCursor());
            var frames = new ManualFrameSource();
            using var clock = FrameClock.Source.Override(frames);
            h.WriteLibrary([new Project("Project", h.NewFolder("project"))], []);
            var model = h.MakeModel();
            h.PrepareBootstrap(model);
            AvaloniaWindow window = null!;
            ChatView view = null!;
            h.Main.OnMain(() =>
            {
                model.Settings.ShowsUsagePanel = true;
                model.Settings.HydraEnabled = false;
                view = new ChatView(model.Runtime(model.NewThread(model.Projects[0])!.Id));
                window = new AvaloniaWindow { Content = view, Width = 1400, Height = 900 };
                AppEnvironment.SetAppModel(window, model);
                AppEnvironment.SetWindowLiveResize(window, new WindowLiveResize(window));
                ChatHeadlessFixtures.Track(window);
                window.Show();
            });
            try
            {
                Drain(h, window);
                var panel = Assert.Single(LogicalQuery.SelfAndDescendants<UsageFloatingPanel>(view));
                Assert.Equal(1, Seen(panel, view), 6); // in place from its first frame: a switch never grows it in.
            }
            finally { h.Main.OnMain(() => { window.Close(); model.Terminals.TerminateAll(); }); Drain(h, window); }
        });
    }

    [Fact]
    [Trait("Req", "PR-USAGE-069")] // App/Views/Chat/ChatView.swift:236 `if let usage = scene.usage, scene.showsUsage`; :930 showsUsage needs a measured pane
    public void Before_the_pane_has_a_size_no_usage_panel_is_placed()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            var model = h.MakeModel();
            h.Main.OnMain(() =>
            {
                model.Settings.ShowsUsagePanel = true;
                var view = new ChatView(model.Runtime(model.NewThread(model.AddProject(new Project("P", h.NewFolder("p")).Url))!.Id));
                // The body's first pass, before any layout has measured the pane (:17 paneSize = .zero).
                typeof(ChatView).GetField("model", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(view, model);
                typeof(ChatView).GetMethod("RefreshInputs", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(view, null);
                var scene = (PanelScene)typeof(ChatView).GetField("scene", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view)!;
                Assert.NotNull(scene.Usage); // the setting is on and the thread is loaded...
                Assert.False(scene.IsMeasured); // ...but the pane has no size yet,
                Assert.Empty(LogicalQuery.SelfAndDescendants<UsageFloatingPanel>(view)); // so nothing is placed.
                Assert.Empty(LogicalQuery.SelfAndDescendants<PlacedPanel>(view));
            });
        });
    }

    // PR-USAGE-074 (ChatView.swift:541-545,574-577): the panel's frame, surface included, glides to the content height; a surface that is swapped in
    // while it glides (the thread's provider changed: another conversation inside the same placed panel) joins the frame where it is, not at its end.
    [Fact]
    [Trait("Rule", "R-TEST-4")] // ChatView.swift:541-545,574-577; ED-278 (4)
    public void A_usage_panel_replaced_in_the_middle_of_its_glide_takes_the_size_the_glide_has_reached()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            h.Main.Loop.Run(() => Marrow.Core.Support.LoginEnvironment.Load(), AppModelHarness.Bound);
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            using var cursor = DragHandleView.CursorPlatform.Override(new ChatNoCursor());
            var frames = new ManualFrameSource();
            using var clock = FrameClock.Source.Override(frames);
            h.WriteLibrary([new Project("Project", h.NewFolder("project"))], []);
            var model = h.MakeModel();
            h.PrepareBootstrap(model);
            AvaloniaWindow window = null!;
            ChatView view = null!;
            Guid threadID = default;
            h.Main.OnMain(() =>
            {
                model.Settings.HydraEnabled = false;
                model.Settings.UsagePanelDock = PanelDockCorner.TopLeading;
                model.Settings.UsagePanelHeight = 500;
                model.Settings.ShowsUsagePanel = true;
                threadID = model.NewThread(model.Projects[0])!.Id;
                view = new ChatView(model.Runtime(threadID));
                window = new AvaloniaWindow { Content = view, Width = 1400, Height = 900 };
                AppEnvironment.SetAppModel(window, model);
                AppEnvironment.SetWindowLiveResize(window, new WindowLiveResize(window));
                ChatHeadlessFixtures.Track(window);
                window.Show();
            });
            try
            {
                Drain(h, window);
                h.Main.Advance(TimeSpan.FromSeconds(1)); frames.Advance(TimeSpan.Zero); Drain(h, window);
                var panel = Assert.Single(LogicalQuery.SelfAndDescendants<UsageFloatingPanel>(view));
                var stored = panel.Bounds.Height;
                Assert.Equal(500, stored, 1);
                h.Main.OnMain(() =>
                {
                    var resizer = (PanelResize)typeof(ChatView).GetMethod("CurrentResizer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                        .Invoke(view, [new FloatingPanelID.Usage()])!;
                    resizer.OnReset(); // the grip's double-click.
                });
                Drain(h, window);
                h.Main.AdvanceMilliseconds(90); frames.Advance(TimeSpan.Zero); Drain(h, window);
                var mid = panel.Bounds.Height;
                Assert.InRange(mid, 150, stored - 20); // on its way down.
                h.Main.OnMain(() => model.UpdateThread(threadID, thread => thread with { Provider = thread.Provider == ProviderKind.Codex ? ProviderKind.Claude : ProviderKind.Codex }));
                Drain(h, window);
                var replaced = Assert.Single(LogicalQuery.SelfAndDescendants<UsageFloatingPanel>(view));
                Assert.NotSame(panel, replaced); // another provider's panel in the same placed panel...
                Assert.Equal(mid, replaced.Bounds.Height, 1); // ...takes the frame where the glide is.
                h.Main.Advance(TimeSpan.FromSeconds(1)); frames.Advance(TimeSpan.Zero); Drain(h, window);
                Assert.True(replaced.Bounds.Height < stored - 50);
            }
            finally { h.Main.OnMain(() => { window.Close(); model.Terminals.TerminateAll(); }); Drain(h, window); }
        });
    }

    [Fact]
    [Trait("Req", "PR-USAGE-070")] // AppSettings.swift:175,787 the corner is stored for the app, unreadable values read as bottom leading
    public void The_usage_corner_survives_a_restart_and_an_unreadable_value_falls_back_to_bottom_leading()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            h.Defaults.Set("usagePanelDock", "nonsense");
            var model = h.MakeModel();
            h.Main.OnMain(() =>
            {
                Assert.Equal(PanelDockCorner.BottomLeading, model.Settings.UsagePanelDock);
                model.Settings.UsagePanelDock = PanelDockCorner.TopTrailing;
            });
            Assert.Equal("topTrailing", h.Defaults.Object("usagePanelDock"));
            var restarted = h.MakeModel();
            Assert.Equal(PanelDockCorner.TopTrailing, h.Main.OnMain(() => restarted.Settings.UsagePanelDock));
        });
    }
}
