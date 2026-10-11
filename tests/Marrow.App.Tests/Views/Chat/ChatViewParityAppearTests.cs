using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.Services.Git;
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
// and what it moves. ED-278 (2).
[Collection("Headless")]
public sealed class ChatViewParityAppearTests(HeadlessSession headless)
{
    private static (ChatChromeRow Row, AvaloniaWindow Window, ThreadRuntime Runtime) MakeRow(AppModelHarness h, AppModel model, bool sidebarHoldsButtons, bool hydra, bool panelOnly = false) => h.Main.OnMain(() =>
    {
        model.Settings.SidebarMode = panelOnly ? SidebarMode.PanelOnly : SidebarMode.Column;
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
    private void AssertArrives<T>(bool sidebarHoldsButtons, bool hydra, Action<AppModel> trigger, bool panelOnly = false) where T : Control
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
            var (row, window, _) = MakeRow(h, model, sidebarHoldsButtons, hydra, panelOnly);
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
    private void AssertLeaves<T>(bool sidebarHoldsButtons, bool hydra, Action<AppModel> trigger, bool panelOnly = false) where T : Control
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
            var (row, window, _) = MakeRow(h, model, sidebarHoldsButtons, hydra, panelOnly);
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
    [Trait("Rule", "R-TEST-4")] // ChatView.swift:1129-1131; Chrome.swift:938-960 (PR-CHAT-173 and UI-CHAT-074, the arrival half); ED-278
    public void The_Threads_button_arrives_on_the_soft_appear_curve_fading_in_with_a_4_point_rise() =>
        AssertArrives<ThreadsButton>(sidebarHoldsButtons: true, hydra: false, model => model.Settings.SidebarMode = SidebarMode.Column, panelOnly: true); // ED-NEW-sidebarmodes-1: it comes back with Column, not with the window buttons

    [Fact]
    [Trait("Rule", "R-TEST-4")] // ChatView.swift:1129-1131; Chrome.swift:948-956 (PR-CHAT-173 and UI-CHAT-074, the departure half); ED-278
    public void The_Threads_button_leaves_on_an_ease_out_fade_of_0_12_seconds_without_sinking() =>
        AssertLeaves<ThreadsButton>(sidebarHoldsButtons: false, hydra: false, model => model.Settings.SidebarMode = SidebarMode.PanelOnly); // ED-NEW-sidebarmodes-1: it leaves for Panel only

    [Fact]
    [Trait("Rule", "R-TEST-4")] // ChatView.swift:1138-1140; Chrome.swift:938-960 (the Hydra slot of PR-CHAT-171, the arrival half); ED-278
    public void The_Hydra_button_arrives_on_the_soft_appear_curve_fading_in_with_a_4_point_rise() =>
        AssertArrives<HydraButton>(sidebarHoldsButtons: true, hydra: false, model => model.Settings.HydraEnabled = true);

    [Fact]
    [Trait("Rule", "R-TEST-4")] // ChatView.swift:1138-1140; Chrome.swift:948-956 (the Hydra slot of PR-CHAT-171, the departure half); ED-278
    public void The_Hydra_button_leaves_on_an_ease_out_fade_of_0_12_seconds_without_sinking() =>
        AssertLeaves<HydraButton>(sidebarHoldsButtons: true, hydra: true, model => model.Settings.HydraEnabled = false);

    // The row's glide starts from where the controls were drawn: a row that has not been laid out yet has drawn nothing, so a change of the window
    // buttons' owner before its first layout puts the controls at their places at once (they do not sweep in from the row's start).
    [Fact]
    [Trait("Rule", "R-TEST-4")] // ChatView.swift:1179; ED-278 (1)
    public void A_row_that_has_not_been_laid_out_yet_places_its_controls_at_once_when_the_window_buttons_move()
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
            var (row, window) = h.Main.OnMain(() =>
            {
                model.Settings.SidebarFloats = false;
                model.Settings.HydraEnabled = false;
                model.Sidebar.NoteLaidOutWidth(400);
                var runtime = model.Runtime(model.NewThread(model.AddProject(new Project("P", h.NewFolder("p")).Url))!.Id);
                var built = new ChatChromeRow(model, runtime, new ChromeScrollModel(), "T", null, h.Scratch, new GitStatusModel());
                model.Sidebar.NoteLaidOutWidth(0); // the buttons leave the sidebar before anything is drawn.
                return (built, new AvaloniaWindow { Content = built, Width = 1400, Height = 300 });
            });
            h.Main.RunUntilIdle();
            h.Main.OnMain(() => { ChatHeadlessFixtures.Track(window); window.Show(); });
            Step(h, window, time, frames, 0);
            double NewThreadX() => h.Main.OnMain(() => LogicalQuery.SelfAndDescendants<ChromeCircleButton>(row).Single(control =>
                (ToolTip.GetTip(LogicalQuery.SelfAndDescendants<Button>(control).First()) as string ?? "").StartsWith("New thread", StringComparison.Ordinal))
                .TranslatePoint(default, window)!.Value.X);
            var first = NewThreadX();
            Assert.Equal(14 + 42, first, 1); // after the Threads button's slot, on the very first frame.
            Step(h, window, time, frames, 500);
            Assert.Equal(first, NewThreadX(), 1);
        });
    }

    // The toggle capsule (ChatView.swift:1164-1172): the buttons keep their identity across a refresh, and the "on" circle of each fades in and out
    // where its flag changes: the terminal's on the panel slide (the column's `.animation(Chrome.panelSlide, value: runtime.isTerminalVisible)`, :290),
    // the Changes button's, which the Mac snaps, on the hover curve (OD-0076).
    [Fact]
    [Trait("Rule", "R-TEST-4")] // ChatView.swift:290,1164-1172; Chrome.swift:344-350; OD-0076; ED-278 (5)
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

    // The kept toggle buttons (ED-278 (5)): a button turned on by ChromeIconButton.Update keeps its "on" circle when the pointer
    // moves onto it and off it again. Chrome.swift:370-378 redraws the label with the button's current isActive and the new hover state; a hover
    // that redrew it with the isActive the button was built with would drop the circle (mutant M10d of round 2 survived the ChatView suite).
    [Fact]
    [Trait("Rule", "R-TEST-4")] // UI/Chrome/Chrome.swift:344-350,370-378; ChatView.swift:1164-1172; ED-278 (5)
    public void A_toggle_button_turned_on_keeps_its_on_circle_while_the_pointer_hovers_it_and_leaves()
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
            bool HasCircle(Control button) => h.Main.OnMain(() =>
                LogicalQuery.SelfAndDescendants<Avalonia.Controls.Shapes.Ellipse>(button).SingleOrDefault() is { } circle && circle.Opacity > 0.99);
            void Hover(Control button, bool on)
            {
                h.Main.OnMain(() =>
                {
                    var at = on ? button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value : new Point(5, 250);
                    Avalonia.Headless.HeadlessWindowExtensions.MouseMove(window, at, Avalonia.Input.RawInputModifiers.None);
                });
                Step(h, window, time, frames, 0);
                Step(h, window, time, frames, 300);
            }
            void Check(string tip, Action turnOn)
            {
                var button = Button(tip);
                h.Main.OnMain(turnOn);
                Step(h, window, time, frames, 0);
                Step(h, window, time, frames, 600);
                Assert.Same(button, Button(tip)); // kept and updated, not rebuilt.
                Assert.True(HasCircle(button), $"{tip}: on");
                Hover(button, on: true);
                Assert.True(HasCircle(button), $"{tip}: the pointer on it dropped the circle");
                Hover(button, on: false);
                Assert.True(HasCircle(button), $"{tip}: the pointer leaving it dropped the circle");
            }
            Check("Terminal", () => runtime.IsTerminalVisible = true);
            Check("Changes", () => runtime.IsDiffVisible = true);
        });
    }

    // PR-CHAT-171 whole (ChatView.swift:1106-1174): the row is ONE glass pass (GlassEffectContainer(spacing: 2): every piece of glass in it merges in that
    // container), a centre-aligned stack of spacing 10 in the fixed order Threads (PR-CHAT-173), New thread, Hydra (only when the thread record resolves, Hydra is
    // on and the thread leads a team of its own; it arrives and leaves on soft appear), the permissions menu (only when the thread record resolves), the branch
    // menu (only in a repository), the title, then a trailing stack of spacing 8 of two capsules: [Open in, a divider and Scripts with a project, a divider and
    // Git actions in a repository] and [Terminal, a divider, Changes]. The row is on the Mac's frame at C 9.000 in the project-and-no-repository state
    // (ChatViewParityChromeTests.The_row_lays_out_at_the_positions_measured_on_the_Mac). No other control appears and the order never varies.
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [Trait("Req", "PR-CHAT-171")] // App/Views/Chat/ChatView.swift:1106-1174
    public void The_chrome_row_is_one_glass_pass_holding_its_slots_in_the_fixed_order_in_every_project_and_repository_state(bool withProject, bool repository)
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            using var frameScope = FrameClock.Source.Override(new ManualFrameSource());
            h.Main.OnMain(() =>
            {
                model.Settings.SidebarFloats = false;
                model.Settings.HydraEnabled = true;
                var project = model.AddProject(new Project("P", h.NewFolder("project")).Url);
                var thread = model.NewThread(project)!;
                var git = new GitStatusModel();
                typeof(GitStatusModel).GetProperty(nameof(GitStatusModel.IsRepository))!.SetValue(git, repository);
                string Kind(Control slot) => slot switch
                {
                    ThreadsButton => "threads",
                    HydraButton => "hydra",
                    PermissionMenu => "permissions",
                    BranchMenu => "branch",
                    ChatChromeTitle => "title",
                    ChromeCircleButton => "new",
                    StackPanel trailing when trailing.Spacing == 8 => "capsules",
                    _ => LogicalQuery.SelfAndDescendants<Control>(slot).Select(c => c switch { ThreadsButton => "threads", HydraButton => "hydra", _ => null }).FirstOrDefault(k => k is not null) ?? slot.GetType().Name,
                };
                List<string> Slots(ChatChromeRow row)
                {
                    var stack = Assert.IsType<ChatChromeRowStack>(LogicalQuery.SelfAndDescendants<StackPanel>(row).First());
                    Assert.Equal(10, stack.Spacing); // :1124 HStack(alignment: .center, spacing: 10)
                    Assert.Equal(Avalonia.Layout.VerticalAlignment.Center, stack.VerticalAlignment);
                    return stack.Children.Select(Kind).ToList();
                }
                ChatChromeRow Build(ThreadRuntime runtime)
                {
                    var row = new ChatChromeRow(model, runtime, new ChromeScrollModel(), "T", withProject ? project : null, h.Scratch, git);
                    var window = new AvaloniaWindow { Content = row, Width = 1400, Height = 300 };
                    ChatHeadlessFixtures.Track(window);
                    window.Show();
                    return row;
                }
                var expected = new List<string> { "threads", "new", "hydra", "permissions" };
                if (repository) expected.Add("branch");
                expected.AddRange(["title", "capsules"]);

                // The whole row, a thread that leads a team of its own with Hydra on: all eight slots.
                var row = Build(model.Runtime(thread.Id));
                Assert.Equal(expected, Slots(row));
                var stack = Assert.IsType<ChatChromeRowStack>(LogicalQuery.SelfAndDescendants<StackPanel>(row).First());
                var trailing = (StackPanel)stack.Children[^1];
                Assert.Equal(2, trailing.Children.Count);
                Assert.All(trailing.Children, capsule => Assert.IsType<ChromeCapsule>(capsule));
                var first = LogicalQuery.SelfAndDescendants<StackPanel>(trailing.Children[0]).Single(panel => panel.Children.FirstOrDefault() is OpenInMenu).Children;
                var wanted = new List<string> { "open" };
                if (withProject) wanted.AddRange(["divider", "scripts"]);
                if (repository) wanted.AddRange(["divider", "git"]);
                Assert.Equal(wanted, first.Select(part => part switch { OpenInMenu => "open", ChromeDivider => "divider", ScriptsMenu => "scripts", GitActionsMenu => "git", _ => part.GetType().Name }).ToList());
                var second = LogicalQuery.SelfAndDescendants<StackPanel>(trailing.Children[1]).Single(panel => panel.Children.OfType<ChromeIconButton>().Any()).Children;
                Assert.Equal(["icon", "divider", "icon"], second.Select(part => part switch { ChromeIconButton => "icon", ChromeDivider => "divider", _ => part.GetType().Name }));

                // The glass: one container of spacing 2 around the whole row, and every piece of glass in the row is merged in it (one material pass).
                var container = Assert.Single(LogicalQuery.SelfAndDescendants<GlassEffectContainer>(row));
                Assert.Equal(2, container.Spacing); // :1123 GlassEffectContainer(spacing: 2)
                Assert.Equal(2, SwiftGlass.GetContainerSpacing(container));
                Assert.Contains(container.GetLogicalDescendants(), descendant => ReferenceEquals(descendant, stack));
                var glass = LogicalQuery.SelfAndDescendants<Control>(row).Where(control => SwiftGlass.GetEffect(control) is not null && control.GetVisualParent() is not null).ToArray(); // drawn glass: a button's template part that is not in the visual tree draws nothing
                Assert.True(glass.Length >= 6, "the circles and the two capsules are glass: " + glass.Length);
                Assert.All(glass, piece => Assert.Same(container, piece.GetVisualAncestors().OfType<Control>().First(ancestor => SwiftGlass.GetContainerSpacing(ancestor) is not null)));
                Assert.Single(row.GetVisualDescendants().OfType<Control>(), control => SwiftGlass.GetContainerSpacing(control) is not null); // no second container

                // Hydra off in Settings: no Hydra slot; the rest unchanged.
                model.Settings.HydraEnabled = false;
                Assert.Equal(expected.Where(slot => slot != "hydra").ToList(), Slots(Build(model.Runtime(thread.Id))));
                model.Settings.HydraEnabled = true;
                // A helper leads no team of its own: no Hydra slot, the permissions menu stays.
                var helper = model.SpawnSubagent(thread.Id, "Review", "Inspect the change.")!;
                Assert.Equal(expected.Where(slot => slot != "hydra").ToList(), Slots(Build(model.Runtime(helper.Id))));
                // A thread record that does not resolve: neither the Hydra nor the permissions slot.
                Assert.Equal(expected.Where(slot => slot is not ("hydra" or "permissions")).ToList(), Slots(Build(model.Runtime(Guid.NewGuid()))));
            });
        });
        // Hydra arrives and leaves on soft appear (:1138-1140), as ChatViewParityAppearTests proves for the control itself.
        AssertArrives<HydraButton>(sidebarHoldsButtons: true, hydra: false, model => model.Settings.HydraEnabled = true);
        AssertLeaves<HydraButton>(sidebarHoldsButtons: true, hydra: true, model => model.Settings.HydraEnabled = false);
    }
}
