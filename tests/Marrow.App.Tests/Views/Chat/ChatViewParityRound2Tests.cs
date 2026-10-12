using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.App.Views.Terminal;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Chat;

// The ChatView parity unit, review round 2: the proofs the review found missing (a curve no test pinned, a path no test drove).
[Collection("Headless")]
public sealed class ChatViewParityRound2Tests(HeadlessSession headless)
{
    private static void Drain(AppModelHarness h, AvaloniaWindow window)
    {
        h.Main.RunUntilIdle();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        h.Main.RunUntilIdle();
    }

    private static void Advance(AppModelHarness h, AvaloniaWindow window, ManualFrameSource frames, double seconds)
    {
        h.Main.Advance(TimeSpan.FromSeconds(seconds)); frames.Advance(TimeSpan.Zero); Drain(h, window);
    }

    // A chat column on a manual clock: no usage panel, no Hydra, the terminal closed; `configure` runs before the view is built.
    private void WithColumn(Action<AppModelHarness, AppModel, ThreadRuntime, ChatView, AvaloniaWindow, ManualFrameSource> check,
        double width = 1400, double height = 900, Action<AppModel, ThreadRuntime>? configure = null, bool reduceMotion = false) => ChatHeadlessFixtures.Run(headless, () =>
    {
        using var motion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = reduceMotion });
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
        ThreadRuntime runtime = null!;
        h.Main.OnMain(() =>
        {
            model.Settings.ShowsUsagePanel = false;
            model.Settings.HydraEnabled = false;
            runtime = model.Runtime(model.NewThread(model.Projects[0])!.Id);
            configure?.Invoke(model, runtime);
            view = new ChatView(runtime);
            window = new AvaloniaWindow { Content = view, Width = width, Height = height };
            AppEnvironment.SetAppModel(window, model);
            AppEnvironment.SetWindowLiveResize(window, new WindowLiveResize(window));
            ChatHeadlessFixtures.Track(window);
            window.Show();
        });
        try { Drain(h, window); check(h, model, runtime, view, window, frames); }
        finally { h.Main.OnMain(() => { window.Close(); model.Terminals.TerminateAll(); }); Drain(h, window); }
    });

    // PR-TERM-001, the Reduce Motion clause: ChatView.swift:290 `.animation(switching ? nil : Chrome.panelSlide, value: runtime.isTerminalVisible)`,
    // and Chrome.panelSlide is `.easeOut(duration: 0.18)` when the system reports Reduce Motion (Chrome.swift:100-104). The drawer's move and fade and
    // the thread view's push both follow that curve, sampled on it, and are settled at 0.18 s, both ways. (Mutant M25 of round 2, the drawer's
    // transaction on a fixed spring(0.32, 0.9), survived every terminal test before this one.)
    [Fact]
    [Trait("Req", "PR-TERM-001")] // App/Views/Chat/ChatView.swift:276-279,290; UI/Chrome/Chrome.swift:100-104
    public void Under_Reduce_Motion_the_drawer_and_the_push_run_on_the_0_18_second_ease_out_both_ways()
    {
        WithColumn((h, _, runtime, view, window, frames) =>
        {
            var curve = Animation.EaseOut(duration: 0.18);
            Assert.Equal(curve, Chrome.PanelSlide);
            var composer = Assert.Single(LogicalQuery.SelfAndDescendants<ComposerArea>(view));
            double ComposerBottom() => composer.TranslatePoint(new Point(0, composer.Bounds.Height), view)!.Value.Y;
            var resting = ComposerBottom();
            h.Main.OnMain(() => runtime.IsTerminalVisible = true);
            Drain(h, window);
            var terminal = Assert.Single(LogicalQuery.SelfAndDescendants<TerminalPanel>(view));
            double TerminalTop() => terminal.TranslatePoint(default, view)!.Value.Y;
            var height = terminal.Bounds.Height;
            Assert.True(height > 100);
            Assert.Equal(resting, ComposerBottom(), tolerance: 1); // the first frame: nothing has moved.
            Assert.Equal(resting, TerminalTop(), tolerance: 1);
            Assert.Equal(0, terminal.Opacity, tolerance: 0.001);
            void Sample(bool opening)
            {
                var elapsed = 0.0;
                foreach (var at in new[] { 0.03, 0.06, 0.1, 0.15 })
                {
                    Advance(h, window, frames, at - elapsed); elapsed = at;
                    var progress = curve.Progress(at);
                    var shown = opening ? progress : 1 - progress; // how much of the drawer is up.
                    Assert.True(Math.Abs(resting - ComposerBottom() - shown * height) <= 1,
                        $"at {at:0.00} s the chat box is {resting - ComposerBottom():0.0} pt up; the ease-out puts it at {shown * height:0.0}");
                    Assert.Equal(ComposerBottom(), TerminalTop(), tolerance: 1); // the drawer's top on the chat box's bottom.
                    Assert.Equal(shown, terminal.Opacity, tolerance: 0.01);
                }
                Advance(h, window, frames, 0.18 - elapsed); // settled at 0.18 s.
            }
            Sample(opening: true);
            Assert.Equal(resting - height, ComposerBottom(), tolerance: 1);
            Assert.Equal(resting - height, TerminalTop(), tolerance: 1);
            Assert.Equal(1, terminal.Opacity);
            h.Main.OnMain(() => runtime.IsTerminalVisible = false);
            Drain(h, window);
            Assert.Equal(resting - height, ComposerBottom(), tolerance: 1); // the first frame of the close.
            Sample(opening: false);
            Assert.Empty(LogicalQuery.SelfAndDescendants<TerminalPanel>(view));
            Assert.Equal(resting, ComposerBottom(), tolerance: 1);
        }, reduceMotion: true);
    }

    // PR-CHAT-172 / PR-CHAT-173 (the round 1 fix, ED-278 (1)). ChatView.swift:1179 `.animation(Chrome.panelSlide, value: sidebarVisible)`:
    // the controls glide to their new places on panelSlide itself (.spring(response: 0.32, dampingFraction: 0.9)). The round 1 tests only ask that a
    // control is somewhere between its two places 64 ms in; here the New thread circle is sampled on the curve (mutants M7b, a glide on
    // .easeOut(duration: 0.32), and M20, a linear glide, survived the ChatView suite before this test). Written by the round 2 review.
    [Theory]
    [InlineData(false)] // Panel only (ED-NEW-sidebarmodes-1): the Threads button leaves and the row closes up.
    [InlineData(true)] // back to Column: the Threads button arrives and the row opens.
    [Trait("Req", "PR-CHAT-172")] // App/Views/Chat/ChatView.swift:1179 .animation(Chrome.panelSlide, value: sidebarVisible)
    [Trait("Req", "PR-CHAT-173")] // App/Views/Chat/ChatView.swift:1125-1131,1179
    public void The_row_glides_on_the_panel_slide_curve_itself(bool arrives)
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var motion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform()); // the spring, whatever the machine's setting.
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero));
            using var timeScope = SwiftClock.Provider.Override(time);
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var (row, window) = h.Main.OnMain(() =>
            {
                model.Settings.SidebarMode = arrives ? SidebarMode.PanelOnly : SidebarMode.Column;
                model.Settings.HydraEnabled = false;
                var runtime = model.Runtime(model.NewThread(model.AddProject(new Project("P", h.NewFolder("p")).Url))!.Id);
                var built = new ChatChromeRow(model, runtime, new ChromeScrollModel(), "T", null, h.Scratch, new GitStatusModel());
                var shown = new AvaloniaWindow { Content = built, Width = 1400, Height = 300 };
                ChatHeadlessFixtures.Track(shown);
                shown.Show();
                return (built, shown);
            });
            void Step(int milliseconds)
            {
                time.Advance(TimeSpan.FromMilliseconds(milliseconds));
                frames.Advance(TimeSpan.FromMilliseconds(milliseconds));
                h.Main.RunUntilIdle();
                Dispatcher.UIThread.RunJobs();
                h.Main.OnMain(() => window.UpdateLayout());
                h.Main.RunUntilIdle();
            }
            double NewThreadX() => h.Main.OnMain(() => LogicalQuery.SelfAndDescendants<ChromeCircleButton>(row).Single(control =>
                (ToolTip.GetTip(LogicalQuery.SelfAndDescendants<Button>(control).First()) as string ?? "").StartsWith("New thread", StringComparison.Ordinal))
                .TranslatePoint(default, window)!.Value.X);
            Step(0);
            Step(500);
            var from = NewThreadX();
            var to = from + (arrives ? 42 : -42);
            h.Main.OnMain(() => model.Settings.SidebarMode = arrives ? SidebarMode.Column : SidebarMode.PanelOnly);
            Step(0);
            Assert.Equal(from, NewThreadX(), 0.5); // the first frame: where it was.
            var elapsed = 0;
            foreach (var at in new[] { 32, 64, 112 })
            {
                Step(at - elapsed); elapsed = at;
                var progress = Chrome.PanelSlide.Progress(at / 1000.0);
                Assert.InRange(progress, 0.05, 0.95);
                Assert.True(Math.Abs(from + (to - from) * progress - NewThreadX()) <= 0.75,
                    $"at {at} ms the circle is at {NewThreadX():0.00}; panelSlide puts it at {from + (to - from) * progress:0.00} (from {from:0.0} to {to:0.0})");
            }
            Step(600);
            Assert.Equal(to, NewThreadX(), 0.5);
            h.Main.OnMain(() => window.Close());
        });
    }

    // UI-USAGE-041 on a mounted usage panel, by the pointer: the grips sit on the panel's free edges for its corner (a bottom-docked panel's height
    // grip on its top edge, a trailing-docked panel's width grip on its left edge), a held drag changes only the live values and writes the settings
    // on release, a trailing panel grows leftwards and a bottom one upwards, the width is the shared panel width fitted to the pane, the height is
    // floored at 94 points (PanelScene.UsageMinimumHeight, ChatView.swift:527 `HydraPanel.stripHeight + 44`), and a double-click clears the stored
    // height inside Chrome.panelSlide. (The round 2 review: the only UI-USAGE-041 test never double-clicked nor dragged below the floor.)
    [Theory]
    [InlineData(PanelDockCorner.BottomLeading)]
    [InlineData(PanelDockCorner.TopTrailing)]
    [Trait("Req", "UI-USAGE-041")] // App/Views/Chat/ChatView.swift:527,554-590; SubagentPanel.swift (PanelResizeGrips, the free edges)
    public void The_usage_panels_grips_sit_on_its_free_edges_resize_on_release_floor_at_94_and_reset_on_a_double_click(PanelDockCorner corner)
        => WithColumn((h, model, _, view, window, frames) =>
        {
            Advance(h, window, frames, 1);
            var panel = Assert.Single(LogicalQuery.SelfAndDescendants<Marrow.App.Views.Usage.UsageFloatingPanel>(view));
            Rect Frame(Control control) => new(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);
            var box = Frame(panel);
            // The panel's own grips: the handles at its frame (the window's other panels and the drawer's handle are elsewhere).
            var handles = LogicalQuery.SelfAndDescendants<PanelDragHandle>(view).Where(x => box.Inflate(8).Contains(Frame(x).Center)
                && (ToolTip.GetTip(x) as string ?? "").StartsWith("Drag to resize", StringComparison.Ordinal)).ToArray();
            Assert.Equal(3, handles.Length); // the width, height and corner grips (SubagentPanel.swift:594-615).
            var heightGrip = Assert.Single(handles, x => double.IsNaN(x.Width) && !double.IsNaN(x.Height));
            var widthGrip = Assert.Single(handles, x => !double.IsNaN(x.Width) && double.IsNaN(x.Height));
            var up = !corner.IsTop; // a bottom-docked panel grows upwards...
            var left = corner.Side == PanelDockSide.Trailing; // ...a trailing one leftwards.
            // On the free edges, inside the panel: a 6 pt grip's centre is 3 pt in from the edge.
            Assert.Equal(up ? box.Top + 3 : box.Bottom - 3, Frame(heightGrip).Center.Y, tolerance: 0.5); // the free horizontal edge.
            Assert.Equal(left ? box.Left + 3 : box.Right - 3, Frame(widthGrip).Center.X, tolerance: 0.5); // the free vertical edge.
            Assert.Equal(box.Width, Frame(heightGrip).Width, tolerance: 0.5);
            Assert.Equal(box.Height, Frame(widthGrip).Height, tolerance: 0.5);
            var storedSize = model.Settings.PanelSize;
            Assert.Null(model.Settings.UsagePanelHeight);
            // The width grip, held: 60 points outwards.
            var at = Frame(widthGrip).Center;
            var outwards = left ? -60 : 60;
            window.MouseDown(at, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None);
            foreach (var step in new[] { 20, 40, 60 })
            {
                window.MouseMove(at + new Point(left ? -step : step, 0), Avalonia.Input.RawInputModifiers.LeftMouseButton);
                Drain(h, window);
            }
            Advance(h, window, frames, 1);
            Assert.Equal(storedSize, model.Settings.PanelSize); // held: nothing written...
            Assert.Equal(box.Width + 60, panel.Bounds.Width, tolerance: 6); // ...the live width grows (shown stepped to 12 points, :595-598).
            window.MouseUp(at + new Point(outwards, 0), Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None);
            Drain(h, window);
            Assert.Equal(box.Width + 60, Assert.IsType<Size>(model.Settings.PanelSize).Width, 0.5); // written on release, exact.
            Advance(h, window, frames, 1);
            box = Frame(panel);
            // The height grip, held: 600 points inwards, far past the floor.
            at = Frame(heightGrip).Center;
            var inwards = up ? 600 : -600;
            window.MouseDown(at, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None);
            foreach (var step in new[] { 200, 400, 600 })
            {
                window.MouseMove(at + new Point(0, up ? step : -step), Avalonia.Input.RawInputModifiers.LeftMouseButton);
                Drain(h, window);
            }
            Advance(h, window, frames, 1);
            Assert.Null(model.Settings.UsagePanelHeight); // held: nothing written...
            Assert.Equal(94, PanelScene.UsageMinimumHeight);
            Assert.Equal(94, panel.Bounds.Height, 0.5); // ...the live height stops at the floor.
            window.MouseUp(at + new Point(0, inwards), Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None);
            Drain(h, window);
            Assert.Equal(94, model.Settings.UsagePanelHeight);
            Advance(h, window, frames, 1);
            Assert.Equal(94, panel.Bounds.Height, 0.5);
            // A double-click on the height grip: the stored height is cleared inside panelSlide.
            Animation? during = null;
            var tracker = h.Main.OnMain(() => ObservationTracking.Track(() => model.Settings.UsagePanelHeight, () => during = Transaction.Current).Tracker);
            at = Frame(heightGrip).Center;
            window.MouseDown(at, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None);
            window.MouseUp(at, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None);
            window.MouseDown(at, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None);
            window.MouseUp(at, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None);
            Drain(h, window);
            Assert.Null(model.Settings.UsagePanelHeight);
            Assert.Equal<Animation?>(Chrome.PanelSlide, during);
            GC.KeepAlive(tracker);
        }, configure: (model, _) => { model.Settings.UsagePanelDock = corner; model.Settings.ShowsUsagePanel = true; });

    // ED-278 (4), the surface's presented size (ChatView.SizeSurface): SubagentPanel.swift:299,340-354 frames the panel's scrim and
    // its glass shape explicitly at the panel's size, so when the pane changes size under a mounted helper panel the inner glass follows every size
    // the placement presents and ends at the new one. (Mutant M5d of round 2, the inner framing deleted, survived the ChatView suite.)
    [Fact]
    [Trait("Rule", "R-TEST-4")] // App/Views/Chat/SubagentPanel.swift:299,340-354; ChatView.swift:541-545; ED-278 (4)
    public void A_helper_panels_inner_glass_follows_the_panels_size_when_the_pane_changes_under_it()
        => WithColumn((h, model, runtime, view, window, frames) =>
        {
            Advance(h, window, frames, 1);
            var panel = Assert.Single(LogicalQuery.SelfAndDescendants<SubagentPanel>(view));
            // SubagentPanel.swift:345-355 the clipped glass and the shadow-bearing scrim under it are siblings (PR-HYDRA-229).
            var surface = Assert.IsType<Grid>(panel.Child);
            // ED-358: the scrim's shadow is its SwiftShadow.Caster, drawn first, under it.
            Assert.Equal(3, surface.Children.Count);
            var caster = Assert.IsType<Marrow.UI.Port.SwiftShadow.Caster>(surface.Children[0]);
            var scrim = Assert.IsType<Border>(surface.Children[1]);
            var shape = Assert.IsType<Border>(surface.Children[2]);
            Assert.Same(scrim, caster.Source);
            void Matches()
            {
                Assert.Equal(panel.Bounds.Width, scrim.Bounds.Width, 0.5);
                Assert.Equal(panel.Bounds.Height, scrim.Bounds.Height, 0.5);
                Assert.Equal(panel.Bounds.Width, shape.Bounds.Width, 0.5);
                Assert.Equal(panel.Bounds.Height, shape.Bounds.Height, 0.5);
            }
            Matches();
            var before = panel.Bounds.Size;
            Assert.Equal(500, before.Height, 0.5); // min(520, max(300, 1000 * 0.5)): SubagentPanel.swift:165-170.
            h.Main.OnMain(() => window.Height = 800);
            Drain(h, window);
            var elapsed = 0.0;
            foreach (var at in new[] { 0.03, 0.08, 0.15 })
            {
                Advance(h, window, frames, at - elapsed); elapsed = at;
                Matches();
            }
            Advance(h, window, frames, 1);
            Assert.Equal(400, panel.Bounds.Height, 0.5); // the pane is 800 tall: 800 * 0.5.
            Matches();
        }, width: 1600, height: 1000, configure: (model, runtime) =>
        {
            var parent = model.Thread(runtime.ThreadID)!;
            model.InsertThread(new ChatThread(parent.ProjectID, parent.Provider, parent.Model, parent.Effort, parent.RuntimeMode, parent.FastMode)
            {
                ParentThreadID = parent.Id,
                IsInPanel = true,
                Title = "Helper",
                HasCustomTitle = true,
            });
        });
}
