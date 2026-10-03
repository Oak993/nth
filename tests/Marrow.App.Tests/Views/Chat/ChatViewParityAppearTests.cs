using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Chat;

// The chrome row's two soft-appear controls, the Threads button and the Hydra button (ChatView.swift:1129-1131, 1139-1140;
// Chrome.swift:933-960): each arrives under Animation.softAppear (.smooth(duration: 0.32)) fading in with a 4-point rise, and
// leaves under .easeOut(duration: 0.12) with a plain fade (round 1 review: four mutants of these two curves survived the suite).
// The curves are the port's own Animation values, which their own tests fix; what is proven here is which curve each half uses
// and what it moves. ED-NEW-parity-chatview (2).
[Collection("Headless")]
public sealed class ChatViewParityAppearTests(HeadlessSession headless)
{
    private static (ChatChromeRow Row, AvaloniaWindow Window, ThreadRuntime Runtime) MakeRow(AppModelHarness h, AppModel model, bool sidebarHoldsButtons, bool hydra) => h.Main.OnMain(() =>
    {
        model.Settings.SidebarFloats = false;
        model.Settings.HydraEnabled = hydra;
        if (sidebarHoldsButtons) model.Sidebar.NoteLaidOutWidth(400);
        var runtime = model.Runtime(model.NewThread(model.AddProject(new Project("P", h.NewFolder("p")).Url))!.Id);
        var row = new ChatChromeRow(model, runtime, new ChromeScrollModel(), "T", null, h.Scratch, new GitStatusModel());
        var window = new AvaloniaWindow { Content = row, Width = 1400, Height = 300 };
        ChatHeadlessFixtures.Track(window);
        window.Show();
        return (row, window, runtime);
    });

    private static void Step(AppModelHarness h, AvaloniaWindow window, ManualTimeProvider time, ManualFrameSource frames, int milliseconds)
    {
        time.Advance(TimeSpan.FromMilliseconds(milliseconds));
        frames.Advance(TimeSpan.FromMilliseconds(milliseconds));
        h.Main.RunUntilIdle();
        Dispatcher.UIThread.RunJobs();
        h.Main.OnMain(() => window.UpdateLayout());
        h.Main.RunUntilIdle();
    }

    // How the control is drawn now: its shown opacity (the product along its ancestry up to the row), the rise its transition holds it at, and where it is.
    private static (double Opacity, double Rise, double X) Look<T>(AppModelHarness h, ChatChromeRow row, AvaloniaWindow window) where T : Control => h.Main.OnMain(() =>
    {
        var control = LogicalQuery.SelfAndDescendants<T>(row).Single();
        var opacity = 1.0;
        for (Control? at = control; at is not null && !ReferenceEquals(at, row); at = at.Parent as Control) opacity *= at.Opacity;
        return (opacity, control.RenderTransform?.Value.M32 ?? 0, control.TranslatePoint(default, window)!.Value.X);
    });

    // The arrival: faded out and 4 points low on the first frame, then Opacity = p and Rise = 4 (1 - p) on the softAppear curve, whole at its end.
    private void AssertArrives<T>(bool sidebarHoldsButtons, bool hydra, Action<AppModel> trigger) where T : Control
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero));
            using var timeScope = SwiftClock.Provider.Override(time);
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var (row, window, _) = MakeRow(h, model, sidebarHoldsButtons, hydra);
            Step(h, window, time, frames, 0);
            Assert.Empty(LogicalQuery.SelfAndDescendants<T>(row));
            h.Main.OnMain(() => trigger(model));
            Step(h, window, time, frames, 0);
            var first = Look<T>(h, row, window);
            Assert.Equal(0, first.Opacity, 3); // faded out: not a cut to full.
            Assert.Equal(4, first.Rise, 3); // the rise: .offset(y: 4) in the modifier's inactive state (Chrome.swift:938-941).
            Assert.Equal(Animation.Smooth(0.32), Animation.SoftAppear); // Chrome.swift:960 `.smooth(duration: 0.32)`.
            var elapsed = 0;
            foreach (var at in new[] { 40, 100, 180 })
            {
                Step(h, window, time, frames, at - elapsed); elapsed = at;
                var progress = Animation.SoftAppear.Progress(at / 1000.0);
                var seen = Look<T>(h, row, window);
                Assert.InRange(progress, 0.02, 0.98); // each sample lies on the curve's slope, where a different curve would not agree.
                Assert.Equal(progress, seen.Opacity, 2);
                Assert.Equal(4 * (1 - progress), seen.Rise, 2);
            }
            Step(h, window, time, frames, 300);
            var done = Look<T>(h, row, window);
            Assert.Equal(1, done.Opacity, 3);
            Assert.Equal(0, done.Rise, 3);
        });
    }

    // The departure: a plain fade on .easeOut(duration: 0.12) with no rise, and the control is gone when it ends.
    private void AssertLeaves<T>(bool sidebarHoldsButtons, bool hydra, Action<AppModel> trigger) where T : Control
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero));
            using var timeScope = SwiftClock.Provider.Override(time);
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var (row, window, _) = MakeRow(h, model, sidebarHoldsButtons, hydra);
            Step(h, window, time, frames, 0);
            Step(h, window, time, frames, 400);
            var settled = Look<T>(h, row, window);
            Assert.Equal(1, settled.Opacity, 3);
            Assert.Equal(0, settled.Rise, 3);
            Assert.True(settled.X > 0);
            h.Main.OnMain(() => trigger(model));
            Step(h, window, time, frames, 0);
            var first = Look<T>(h, row, window);
            Assert.Equal(1, first.Opacity, 3); // still whole on the first frame.
            var elapsed = 0;
            foreach (var at in new[] { 30, 60, 90 })
            {
                Step(h, window, time, frames, at - elapsed); elapsed = at;
                var progress = Animation.EaseOut(0.12).Progress(at / 1000.0);
                var seen = Look<T>(h, row, window);
                Assert.InRange(progress, 0.02, 0.98);
                Assert.Equal(1 - progress, seen.Opacity, 2);
                Assert.Equal(0, seen.Rise, 3); // the removal is `.opacity` alone: it never sinks.
                Assert.Equal(settled.X, seen.X, 0.5); // it fades where it stood: a removed view keeps no slot in the stack, and does not drift with it.
            }
            Step(h, window, time, frames, 130 - elapsed); // 0.13 s: the 0.12 s fade has ended and the control has left the row.
            Assert.Empty(LogicalQuery.SelfAndDescendants<T>(row));
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // ChatView.swift:1129-1131; Chrome.swift:938-960 (PR-CHAT-173 and UI-CHAT-074, the arrival half); ED-NEW-parity-chatview
    public void The_Threads_button_arrives_on_the_soft_appear_curve_fading_in_with_a_4_point_rise() =>
        AssertArrives<ThreadsButton>(sidebarHoldsButtons: true, hydra: false, model => model.Sidebar.NoteLaidOutWidth(0));

    [Fact]
    [Trait("Rule", "R-TEST-4")] // ChatView.swift:1129-1131; Chrome.swift:948-956 (PR-CHAT-173 and UI-CHAT-074, the departure half); ED-NEW-parity-chatview
    public void The_Threads_button_leaves_on_an_ease_out_fade_of_0_12_seconds_without_sinking() =>
        AssertLeaves<ThreadsButton>(sidebarHoldsButtons: false, hydra: false, model => model.Sidebar.NoteLaidOutWidth(400));

    [Fact]
    [Trait("Rule", "R-TEST-4")] // ChatView.swift:1138-1140; Chrome.swift:938-960 (the Hydra slot of PR-CHAT-171, the arrival half); ED-NEW-parity-chatview
    public void The_Hydra_button_arrives_on_the_soft_appear_curve_fading_in_with_a_4_point_rise() =>
        AssertArrives<HydraButton>(sidebarHoldsButtons: true, hydra: false, model => model.Settings.HydraEnabled = true);

    [Fact]
    [Trait("Rule", "R-TEST-4")] // ChatView.swift:1138-1140; Chrome.swift:948-956 (the Hydra slot of PR-CHAT-171, the departure half); ED-NEW-parity-chatview
    public void The_Hydra_button_leaves_on_an_ease_out_fade_of_0_12_seconds_without_sinking() =>
        AssertLeaves<HydraButton>(sidebarHoldsButtons: true, hydra: true, model => model.Settings.HydraEnabled = false);

    // The toggle capsule (ChatView.swift:1164-1172): the buttons keep their identity across a refresh, and the "on" circle of each fades in and out
    // where its flag changes: the terminal's on the panel slide (the column's `.animation(Chrome.panelSlide, value: runtime.isTerminalVisible)`, :290),
    // the Changes button's, which the Mac snaps, on the hover curve (OD-0076).
    [Fact]
    [Trait("Rule", "R-TEST-4")] // ChatView.swift:290,1164-1172; Chrome.swift:344-350; OD-0076; ED-NEW-parity-chatview (5)
    public void The_toggle_capsules_on_circles_fade_in_and_out_on_the_panel_slide_and_the_hover_curve_while_the_buttons_keep_their_identity()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero));
            using var timeScope = SwiftClock.Provider.Override(time);
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var (row, window, runtime) = MakeRow(h, model, sidebarHoldsButtons: false, hydra: false);
            Step(h, window, time, frames, 0);
            Control Button(string tip) => h.Main.OnMain(() => LogicalQuery.SelfAndDescendants<ChromeIconButton>(row)
                .Single(button => ((string?)ToolTip.GetTip(LogicalQuery.SelfAndDescendants<Button>(button).Single()) ?? "").StartsWith(tip, StringComparison.Ordinal)));
            // The circle's shown opacity (the product along its ancestry up to the button), or null with no circle.
            double? Circle(Control button) => h.Main.OnMain(() =>
            {
                var circle = LogicalQuery.SelfAndDescendants<Avalonia.Controls.Shapes.Ellipse>(button).SingleOrDefault();
                if (circle is null) return (double?)null;
                var opacity = 1.0;
                for (Control? at = circle; at is not null && !ReferenceEquals(at, button); at = at.Parent as Control) opacity *= at.Opacity;
                return opacity;
            });
            var terminal = Button("Terminal");
            var changes = Button("Changes");
            Assert.Null(Circle(terminal));
            Assert.Null(Circle(changes));
            Assert.Equal(Animation.EaseOut(duration: 0.1), Chrome.Hover); // Chrome.swift's hover curve: `.easeOut(duration: 0.1)`.
            void Fade(Control button, Action<bool> set, Animation curve, bool on)
            {
                h.Main.OnMain(() => set(on));
                Step(h, window, time, frames, 0);
                Assert.Equal(on ? 0 : 1, Circle(button)!.Value, 3); // the first frame: the circle has not started to move, no cut.
                var elapsed = 0;
                foreach (var at in new[] { 30, 70, 120 })
                {
                    Step(h, window, time, frames, at - elapsed); elapsed = at;
                    var progress = curve.Progress(at / 1000.0);
                    if (progress >= 0.999) { if (on) Assert.Equal(1, Circle(button)!.Value, 2); else Assert.Null(Circle(button)); continue; }
                    Assert.InRange(progress, 0.02, 0.98);
                    Assert.Equal(on ? progress : 1 - progress, Circle(button)!.Value, 2);
                }
                Step(h, window, time, frames, 600 - elapsed);
                if (on) Assert.Equal(1, Circle(button)!.Value, 3); else Assert.Null(Circle(button));
            }
            Fade(terminal, on => runtime.IsTerminalVisible = on, Chrome.PanelSlide, on: true);
            Assert.Same(terminal, Button("Terminal")); // the same button, kept across the refresh.
            Assert.Same(changes, Button("Changes"));
            Fade(terminal, on => runtime.IsTerminalVisible = on, Chrome.PanelSlide, on: false);
            Fade(changes, on => runtime.IsDiffVisible = on, Chrome.Hover, on: true);
            Fade(changes, on => runtime.IsDiffVisible = on, Chrome.Hover, on: false);
            Assert.Same(terminal, Button("Terminal"));
            Assert.Same(changes, Button("Changes"));
        });
    }
}
