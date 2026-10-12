using System.Collections;
using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.App.Views.Sidebar;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Theme;
using Xunit;
using static Marrow.App.Tests.Views.Sidebar.SidebarViewTests;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Sidebar;

// Source: DroppyCode/App/Views/Sidebar/SidebarView.swift (B3 52bc792). The records the SidebarView parity unit left
// open, closed or narrowed by the leftovers pass of 2026-10-04 (branch cloud/b3-sidebar-leftovers). Two rulings made
// them provable: OD-0098 (where no reference video frame shows a state, the pinned Swift source is the proof, so each
// test below reads its expected values from the Swift lines it cites) and OD-0076 (every change is transitioned; where
// the Mac snaps the port eases in the Mac's motion language, hover about 0.1 s). Continuous corners are ED-275's.
// A class of its own (TraceabilityTests: one class per file); it shares SidebarViewTests' reflection and fixture helpers (internal static).
[Collection("Headless")]
public sealed class SidebarLeftoversTests(HeadlessSession headless)
{
    // ---- PR-SIDEBAR-013, UI-SIDEBAR-043, UI-UPDATE-010: the update dot comes and goes on Chrome.hover, not by a cut ----

    private static AvailableUpdate WaitingUpdate() => new()
    {
        Version = "999.0.0",
        Tag = "v999.0.0",
        DownloadURL = SwiftURL.Create("https://example.com/Marrow.zip")!,
        Notes = "",
        PageURL = SwiftURL.Create("https://example.com/releases/v999.0.0")!,
    };

    private static void SetUpdate(UpdateChecker checker, AvailableUpdate? update) =>
        typeof(UpdateChecker).GetProperty("Update")!.SetValue(checker, update);

    [Theory]
    [Trait("Req", "PR-SIDEBAR-013")] // SidebarView.swift:170-174 `if UpdateChecker.shared.updateAvailable { UpdateAvailableDot() }` (no animation: the Mac cuts)
    [Trait("Req", "UI-SIDEBAR-043")] // SidebarView.swift:168-175
    [Trait("Req", "UI-UPDATE-010")] // "present only while updateAvailable is true; disappears immediately when the running version catches up"
    [InlineData(false)]
    [InlineData(true)]
    // DEVIATION OD-0076 (ENGINEERING DECISION, the motion language of the rule: "controls that appear fade and scale in",
    // hover about 0.1 s): the source's `if` cuts the dot in and out; the port fades it and scales it from 0.8 over
    // Chrome.hover (0.1 s ease-out, the clear button's of ED-280 (4)). "Immediately" in UI-UPDATE-010 stays true of the
    // dot's CONDITION: the fade starts in the frame the version catches up and the dot is gone 0.1 s later. Reduce
    // Motion stills the transition (the dot is taken at once), OD-0076's last sentence.
    public void The_update_dot_fades_and_scales_in_and_out_on_chrome_hover_as_the_availability_changes(bool reduceMotion) => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var reduction = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = reduceMotion });
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        var checker = harness.Main.OnMain(() => new UpdateChecker(ReleaseFeed.Running("1.0.0"), [], ""));
        void Tick(int ms)
        {
            harness.Main.AdvanceMilliseconds(ms);
            harness.Main.OnMain(() => manual.Advance(TimeSpan.FromMilliseconds(ms)));
        }
        UpdateAvailableDot? Dot(SidebarView view) => LogicalQuery.SelfAndDescendants<UpdateAvailableDot>(RowNamed(view, "Settings")).SingleOrDefault();
        static double Shown(Visual visual) => visual.GetSelfAndVisualAncestors().OfType<Visual>().TakeWhile(v => v is not SidebarRow).Aggregate(1d, (o, v) => o * v.Opacity);
        static double Scale(UpdateAvailableDot dot) => dot.RenderTransform is ScaleTransform scale ? scale.ScaleX : 1;
        using var shared = UpdateChecker.OverrideShared(checker);
        SidebarView view = null!;
        try
        {
            harness.Main.OnMain(() => view = Attached(model));
            harness.Main.OnMain(() => Assert.Null(Dot(view)));
            harness.Main.OnMain(() => SetUpdate(checker, WaitingUpdate()));
            Tick(16);
            if (reduceMotion)
            {
                harness.Main.OnMain(() => { var dot = Dot(view)!; Assert.Equal(1d, Shown(dot)); Assert.Equal(1d, Scale(dot)); }); // stilled: taken at once
                harness.Main.OnMain(() => SetUpdate(checker, null));
                Tick(16);
                harness.Main.OnMain(() => Assert.Null(Dot(view)));
                return;
            }
            double last = 0;
            harness.Main.OnMain(() =>
            {
                var dot = Dot(view)!; // present from the frame the version is behind
                last = Shown(dot);
                Assert.InRange(last, 0.0, 0.5); // 16 ms of 100: started, not cut in
                Assert.InRange(Scale(dot), 0.8, 0.9);
                // the scale turns about the circle's own centre: the 7 pt box's centre moved by :172's offset (AboutUpdateViews.swift:531)
                var circle = Assert.IsType<Avalonia.Controls.Shapes.Ellipse>(dot.Child);
                var offset = Assert.IsType<TranslateTransform>(circle.RenderTransform);
                Assert.Equal(RelativeUnit.Absolute, dot.RenderTransformOrigin.Unit);
                Assert.Equal(3.5 + offset.X, dot.RenderTransformOrigin.Point.X, 6);
                Assert.Equal(3.5 + offset.Y, dot.RenderTransformOrigin.Point.Y, 6);
            });
            for (var step = 0; step < 4; step++)
            {
                Tick(16);
                harness.Main.OnMain(() => { var now = Shown(Dot(view)!); Assert.True(now >= last, $"fade in is monotonic ({last} then {now})"); Assert.True(now - last < 0.5, "no single-frame jump"); last = now; });
            }
            Tick(100);
            harness.Main.OnMain(() => { var dot = Dot(view)!; Assert.Equal(1d, Shown(dot)); Assert.Equal(1d, Scale(dot)); });
            // The running version catches up: the condition flips now, the dot leaves over the same 0.1 s.
            harness.Main.OnMain(() => SetUpdate(checker, null));
            Tick(16);
            harness.Main.OnMain(() =>
            {
                var dot = Dot(view);
                Assert.NotNull(dot); // still on screen, fading
                last = Shown(dot!);
                Assert.InRange(last, 0.5, 0.999);
            });
            for (var step = 0; step < 3; step++)
            {
                Tick(16);
                harness.Main.OnMain(() => { var dot = Dot(view); if (dot is null) return; var now = Shown(dot); Assert.True(now <= last, $"fade out is monotonic ({last} then {now})"); last = now; });
            }
            Tick(100);
            harness.Main.OnMain(() => Assert.Null(Dot(view))); // gone once faded
            // and it can come back after leaving
            harness.Main.OnMain(() => SetUpdate(checker, WaitingUpdate()));
            Tick(150);
            harness.Main.OnMain(() => { var dot = Assert.IsType<UpdateAvailableDot>(Dot(view)); Assert.Equal(1d, Shown(dot)); });
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    });

    // ---- PR-SIDEBAR-129, UI-SIDEBAR-008: the dim of an inactive project's row is eased, not cut ----

    // Opacity a control is drawn at between the row that carries the dimming and itself (a product of the chain).
    private static double ChainOpacity(Visual visual, Visual stopAt) =>
        visual.GetSelfAndVisualAncestors().OfType<Visual>().TakeWhile(v => !ReferenceEquals(v, stopAt)).Aggregate(1d, (o, v) => o * v.Opacity) * stopAt.Opacity;

    [Theory]
    [Trait("Req", "PR-SIDEBAR-129")] // SidebarView.swift:1096-1097 isActive, :1136 `.opacity(isActive ? 1 : 0.55)` (no animation: the Mac snaps)
    [Trait("Req", "UI-SIDEBAR-008")] // SidebarView.swift:1078-1166 ("inactive (0.55 opacity)")
    [InlineData(false)]
    [InlineData(true)]
    // DEVIATION OD-0076 (ENGINEERING DECISION: the dim is a state change, so it takes the hover curve, the Mac's quick
    // state curve (Chrome.hover, easeOut 0.1 s); OD-0076 names "hover about 0.1-0.13 s" for sites the Mac snaps and no
    // other curve is recorded for this one). The values 1 and 0.55 and what they apply to are the source's; Reduce Motion
    // takes the target at once. The row's first render shows it as it stands, with no fade.
    public void An_inactive_projects_row_eases_to_0_55_and_back_on_chrome_hover(bool reduceMotion) => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var reduction = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = reduceMotion });
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var model = Model(harness, [project], []);
        void Tick(int ms)
        {
            harness.Main.AdvanceMilliseconds(ms);
            harness.Main.OnMain(() => manual.Advance(TimeSpan.FromMilliseconds(ms)));
        }
        Control row = null!;
        try
        {
            harness.Main.OnMain(() => row = ShownProjectRow(model, project, 0));
            harness.Main.OnMain(() => Assert.Equal(1d, row.Opacity)); // active at first, as it stands
            harness.Main.OnMain(() => model.Settings.SetProjectActive(false, project.Id));
            if (reduceMotion)
            {
                Tick(16);
                harness.Main.OnMain(() => Assert.Equal(0.55d, row.Opacity));
                return;
            }
            Tick(16);
            double last = 1;
            harness.Main.OnMain(() => { last = row.Opacity; Assert.InRange(last, 0.56, 0.99); }); // under way, not cut
            for (var step = 0; step < 4; step++)
            {
                Tick(16);
                harness.Main.OnMain(() => { Assert.True(row.Opacity <= last && row.Opacity >= 0.55, $"monotonic towards 0.55 ({last} then {row.Opacity})"); Assert.True(last - row.Opacity < 0.3, "no single-frame jump"); last = row.Opacity; });
            }
            Tick(100);
            harness.Main.OnMain(() => Assert.Equal(0.55d, row.Opacity)); // ends exactly on the source's value
            harness.Main.OnMain(() => model.Settings.SetProjectActive(true, project.Id));
            Tick(16);
            harness.Main.OnMain(() => Assert.InRange(row.Opacity, 0.56, 0.99)); // and back up the same way
            Tick(150);
            harness.Main.OnMain(() => Assert.Equal(1d, row.Opacity));
            // the dimming sits on the whole row (icon, title, accessories), not on a part of it
            harness.Main.OnMain(() => { var badge = LogicalQuery.SelfAndDescendants<SidebarIconBadge>(row).Single(); Assert.Equal(1d, badge.Opacity); });
        }
        finally { harness.Main.OnMain(() => Close(row)); }
    });

    // ---- PR-SIDEBAR-127: the leading mark's swap is a crossfade ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-127")] // SidebarView.swift:1106-1114 `if let icon = project.icon { ProjectIconMark } else { SidebarSymbol("folder.fill") }` (a branch swap with no animation: the Mac cuts)
    // DEVIATION OD-0076 (content that swaps crossfades): the old mark fades out where it stands while the new one fades in
    // over it on Chrome.hover, and the old one leaves the tree once it is clear. A change of the record that leaves the
    // icon as it was swaps nothing (the old code rebuilt the mark on every change of the project).
    public void A_picked_icon_replaces_the_marks_by_a_crossfade_on_chrome_hover_and_a_change_of_anything_else_leaves_it_alone() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var model = Model(harness, [project], []);
        void Tick(int ms)
        {
            harness.Main.AdvanceMilliseconds(ms);
            harness.Main.OnMain(() => manual.Advance(TimeSpan.FromMilliseconds(ms)));
        }
        Control row = null!;
        try
        {
            harness.Main.OnMain(() => row = ShownProjectRow(model, project, 0));
            double Shown(Visual mark) => ChainOpacity(mark, (Visual)LogicalQuery.SelfAndDescendants<SidebarIconBadge>(row).Single().Child!); // the mark's own and its wrapper's
            SwiftImage Folder() => LogicalQuery.SelfAndDescendants<SwiftImage>(row).Single(i => i.SymbolName == "folder.fill");
            harness.Main.OnMain(() => Assert.Equal(1d, Shown(Folder())));
            var folder = harness.Main.OnMain(Folder);
            // anything but the icon changing: the same mark stays, untouched
            harness.Main.OnMain(() => model.UpdateProject(project.Id, p => p with { IsExpanded = !p.IsExpanded }));
            Tick(16);
            harness.Main.OnMain(() => { Assert.Same(folder, Folder()); Assert.Equal(1d, Shown(folder)); });
            harness.Main.OnMain(() => model.UpdateProject(project.Id, p => p with { Icon = new ProjectIcon.Symbol("star.fill") }));
            Tick(16);
            harness.Main.OnMain(() =>
            {
                var star = LogicalQuery.SelfAndDescendants<SwiftImage>(row).Single(i => i.SymbolName == "star.fill");
                var old = Folder(); // the old mark is still drawn, fading out where it stood
                Assert.Same(folder, old);
                Assert.InRange(Shown(star), 0.01, 0.99);
                Assert.InRange(Shown(old), 0.01, 0.99);
                Assert.True(Math.Abs(Shown(star) + Shown(old) - 1) < 0.05, "a crossfade: what the new one gains the old one loses");
                Assert.Same(star.GetVisualAncestors().OfType<Panel>().First(p => p.Width == 20), old.GetVisualAncestors().OfType<Panel>().First(p => p.Width == 20)); // both centred in the one 20 pt box
            });
            Tick(200);
            harness.Main.OnMain(() =>
            {
                Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<SwiftImage>(row), i => i.SymbolName == "folder.fill"); // gone once clear
                Assert.Equal(1d, Shown(LogicalQuery.SelfAndDescendants<SwiftImage>(row).Single(i => i.SymbolName == "star.fill")));
            });
            // emoji for symbol crosses the kinds
            harness.Main.OnMain(() => model.UpdateProject(project.Id, p => p with { Icon = new ProjectIcon.Emoji("\U0001F680") }));
            Tick(16);
            harness.Main.OnMain(() =>
            {
                Assert.Contains(LogicalQuery.SelfAndDescendants<SwiftImage>(row), i => i.SymbolName == "star.fill");
                Assert.InRange(Shown(LogicalQuery.SelfAndDescendants<TextBlock>(row).Single(t => t.Text == "\U0001F680")), 0.01, 0.99);
            });
            Tick(200);
            harness.Main.OnMain(() => Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<SwiftImage>(row), i => i.SymbolName is not null));
        }
        finally { harness.Main.OnMain(() => Close(row)); }
    });

    // ---- UI-SIDEBAR-020: the settle pop springs (:1909), it is not a cut ----

    [Fact]
    [Trait("Req", "UI-SIDEBAR-020")] // SidebarView.swift:1909 `withAnimation(.spring(response: 0.3, dampingFraction: 0.45)) { isSettling = true }`; :2132 `.scaleEffect(isPopping ? 1.35 : 1)`
    // The pop is the check's scale riding the transaction isSettling is set in: from 1 to 1.35 on a spring of response 0.3
    // and damping fraction 0.45 (it overshoots 1.35 and rings), measured against the curve itself at two instants.
    public void A_check_that_starts_popping_scales_from_1_to_1_35_on_the_settle_spring() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var check = new SettleCheck(isSettled: false, isPopping: false, () => { });
        check.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        check.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        var window = new AvaloniaWindow { Width = 100, Height = 100, Content = check };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        try
        {
            double ScaleNow() => ((ScaleTransform)check.GetLogicalDescendants().OfType<SwiftImage>().First().RenderTransform!).ScaleX; // the outgoing and incoming glyphs share the one transform
            Assert.Equal(1d, ScaleNow());
            var spring = Animation.Spring(response: 0.3, dampingFraction: 0.45);
            Transaction.WithAnimation(spring, () => check.SetPopping(true));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(1d, ScaleNow()); // nothing has elapsed: not cut to 1.35
            double elapsed = 0;
            foreach (var step in new[] { 0.06, 0.07 }) // the second sample is past the first peak
            {
                clock.Advance(TimeSpan.FromSeconds(step)); manual.Advance(TimeSpan.FromSeconds(step));
                elapsed += step;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal(1 + 0.35 * spring.Progress(elapsed), ScaleNow(), 2);
            }
            Assert.NotEqual(1d, ScaleNow(), 2);
            clock.Advance(TimeSpan.FromSeconds(2)); manual.Advance(TimeSpan.FromSeconds(2));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(1.35, ScaleNow(), 3); // rests on the source's value
            // the glyph is the filled green check while popping (:2124, :2128), set in place, once the replace is over
            var glyph = Assert.Single(check.GetLogicalDescendants().OfType<SwiftImage>());
            Assert.Equal("checkmark.circle.fill", glyph.SymbolName);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-020")] // SidebarView.swift:1909 isSettling inside the spring, :1729 showsCheck keeps the check while settling, :2132
    // The open row's own check is kept when the settle begins (the row asks it to pop) instead of being rebuilt with its
    // scale already at 1.35, so the pop is seen and the check keeps its hover.
    public void A_real_rows_open_check_is_kept_and_springs_when_the_row_starts_settling() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Settle me", 0);
        var model = Model(harness, [project], [thread]);
        using var shared = RowGlideAnimator.Shared.Override(new RowGlideAnimator(new FakeGeniePlatform()));
        Control row = null!;
        void Tick(int ms)
        {
            harness.Main.AdvanceMilliseconds(ms);
            harness.Main.OnMain(() => manual.Advance(TimeSpan.FromMilliseconds(ms)));
        }
        try
        {
            harness.Main.OnMain(() =>
            {
                model.Settings.ThreadFinishAction = ThreadFinishAction.Settle;
                row = LiveThreadRow(thread);
                var window = WindowOf(Hosted(model, row));
                window.UpdateLayout();
                window.MouseMove(row.TranslatePoint(new Point(10, 10), window)!.Value);
            });
            Tick(200); // the hover's own fade is done
            SettleCheck before = null!;
            double ScaleNow() => ((ScaleTransform)LogicalQuery.SelfAndDescendants<SwiftImage>(before).Last().RenderTransform!).ScaleX;
            harness.Main.OnMain(() =>
            {
                before = LogicalQuery.SelfAndDescendants<SettleCheck>(row).Single();
                Assert.Equal(1d, ScaleNow());
                // Settle()'s first statement (:1909), without the model change that follows it
                var state = typeof(SidebarView).Assembly.GetType("Marrow.App.Views.Sidebar.SidebarThreadRow")!.GetField("state", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(row)!;
                Transaction.WithAnimation(Animation.Spring(response: 0.3, dampingFraction: 0.45), () => state.GetType().GetProperty("IsSettling")!.SetValue(state, true));
            });
            Tick(16);
            harness.Main.OnMain(() =>
            {
                Assert.Same(before, LogicalQuery.SelfAndDescendants<SettleCheck>(row).Single()); // kept, not rebuilt
                Assert.InRange(ScaleNow(), 1.001, 1.349); // on its way up the spring, not at 1.35
            });
            Tick(2000);
            harness.Main.OnMain(() => Assert.Equal(1.35, ScaleNow(), 3));
        }
        finally { harness.Main.OnMain(() => WindowOf(row).Close()); }
    });

    // ======== The thread row's clauses the parity unit listed as per-clause gaps (OD-0098: the Swift states them) ========

    // ---- UI-SIDEBAR-017: the face's paddings and minimum heights, the hide, the accessibility value ----

    [Theory]
    [Trait("Req", "UI-SIDEBAR-017")] // SidebarView.swift:2066-2071 `.padding(.leading, rowHorizontalPadding + (settled ? settledTitleInset : 0))`, `.padding(.trailing, rowHorizontalPadding + trailingClearance)`, `.padding(.vertical, detailed ? detailedVerticalPadding : 0)`, `.frame(minHeight: settled ? settledHeight : rowHeight)`
    [InlineData(false, null, 52d, 8d, 0d, 8d + 52, 28d)] // an open row in the project layout
    [InlineData(false, "Alpha", 18d, 8d, 7d, 8d + 18, 28d)] // a detailed activity row: 7 pt above and below, minimum 28 (the 48 pt fallback is the list's own estimate)
    [InlineData(true, null, 18d, 8d + 22, 0d, 8d + 18, 24d)] // a settled row: title inset 22, minimum 24
    [InlineData(true, "Alpha", 30d, 8d + 22, 0d, 8d + 30, 24d)] // settled is never detailed
    public void A_thread_rows_face_pads_and_floors_its_height_as_the_source_states(bool settled, string? projectName, double clearance, double left, double vertical, double right, double minHeight) => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Title", 0) with { IsSettled = settled };
        harness.Main.OnMain(() =>
        {
            var face = MakeFace(thread, projectName, isSelected: false, trailingClearance: clearance);
            var frame = Assert.IsType<Border>(((Decorator)face).Child);
            Assert.Equal(new Thickness(left, vertical, right, vertical), frame.Padding);
            Assert.Equal(minHeight, frame.MinHeight);
            Assert.Equal(HorizontalAlignment.Stretch, frame.HorizontalAlignment); // :2071 .frame(maxWidth: .infinity, alignment: .leading)
            // the inner HStack's 8 and the title column's 3 (:2022, :2034)
            var texts = LogicalQuery.SelfAndDescendants<StackPanel>(face).First(panel => panel.Orientation == Avalonia.Layout.Orientation.Vertical);
            Assert.Equal(3d, texts.Spacing);
        });
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-017")] // SidebarView.swift:1871-1872 `.accessibilityAddTraits(isSelected ? [.isSelected, .isButton] : .isButton)`, `.accessibilityValue(Text(isSettled ? "Settled" : ""))`
    public void A_thread_row_is_a_button_selected_when_selected_and_its_value_is_Settled_for_a_settled_row_and_empty_for_an_open_one() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var open = NewThread(project, "Open", 0);
        var settled = NewThread(project, "Done", 1) with { IsSettled = true };
        var model = Model(harness, [project], [open, settled]);
        using var shared = RowGlideAnimator.Shared.Override(new RowGlideAnimator(new FakeGeniePlatform()));
        Control openRow = null!, settledRow = null!;
        try
        {
            harness.Main.OnMain(() =>
            {
                openRow = LiveThreadRow(open); settledRow = LiveThreadRow(settled);
                WindowOf(Hosted(model, openRow)).UpdateLayout(); WindowOf(Hosted(model, settledRow)).UpdateLayout();
                (Avalonia.Automation.Provider.IValueProvider Value, Avalonia.Automation.Provider.ISelectionItemProvider Item) Providers(Control row)
                {
                    var button = LogicalQuery.SelfAndDescendants<Button>(row).First(b => Avalonia.Automation.AutomationProperties.GetName(b) is { } name && name == (row == openRow ? "Open" : "Done"));
                    var peer = Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(button);
                    Assert.IsAssignableFrom<Avalonia.Automation.Peers.ButtonAutomationPeer>(peer); // .isButton
                    return ((Avalonia.Automation.Provider.IValueProvider)peer, (Avalonia.Automation.Provider.ISelectionItemProvider)peer);
                }
                Assert.Equal("", Providers(openRow).Value.Value);
                Assert.Equal("Settled", Providers(settledRow).Value.Value);
                Assert.False(Providers(openRow).Item.IsSelected);
                model.SelectedThreadID = open.Id; // :1721 isSelected -> .isSelected
            });
            harness.Main.OnMain(() =>
            {
                var button = LogicalQuery.SelfAndDescendants<Button>(openRow).First(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Open");
                var peer = Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(button);
                Assert.True(((Avalonia.Automation.Provider.ISelectionItemProvider)peer).IsSelected);
            });
        }
        finally { harness.Main.OnMain(() => { WindowOf(openRow).Close(); WindowOf(settledRow).Close(); }); }
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-017")] // SidebarView.swift:1832-1838 `.opacity(isHidden ? 0 : 1)`, `.animation(isHidden ? nil : .easeOut(duration: 0.12), value: isHidden)`: hidden at once, shown again over 0.12 s
    public void A_row_under_a_glide_is_hidden_at_once_and_returns_over_an_ease_out_of_0_12_seconds() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Row", 0);
        var model = Model(harness, [project], [thread]);
        var animator = new RowGlideAnimator(new FakeGeniePlatform());
        using var shared = RowGlideAnimator.Shared.Override(animator);
        Control row = null!;
        void Tick(int ms)
        {
            harness.Main.AdvanceMilliseconds(ms);
            harness.Main.OnMain(() => { manual.Advance(TimeSpan.FromMilliseconds(ms)); Dispatcher.UIThread.RunJobs(); });
        }
        try
        {
            harness.Main.OnMain(() => { row = LiveThreadRow(thread); WindowOf(Hosted(model, row)).UpdateLayout(); Assert.Equal(1d, row.Opacity); });
            harness.Main.OnMain(() =>
            {
                animator.Launch(thread.Id, RowGlideAnimator.Glide.Direction.Reopen, new Rect(0, 100, 200, 24), new Rect(0, -28, 200, 28), new Rect(0, 0, 200, 400), new FaceSample(200, 24), new FaceSample(200, 28), 0, 0);
                Dispatcher.UIThread.RunJobs();
            });
            harness.Main.OnMain(() => Assert.Equal(0d, row.Opacity)); // hiding is instant
            harness.Main.OnMain(() => animator.Arrive(animator.Glides[0].Id));
            Tick(60); // half of 0.12 s
            var half = Animation.EaseOut(duration: 0.12).Progress(0.06);
            harness.Main.OnMain(() => Assert.Equal(half, row.Opacity, 2));
            Tick(100);
            harness.Main.OnMain(() => Assert.Equal(1d, row.Opacity));
        }
        finally { harness.Main.OnMain(() => WindowOf(row).Close()); }
    });

    // ---- UI-SIDEBAR-018, -024: the badge and the trailing status ----

    [Fact]
    [Trait("Req", "UI-SIDEBAR-018")] // SidebarView.swift:2143-2174, Chrome.swift:651-679: a 20 pt box; symbols fit symbolSize 14 (12.6 for the pin at 0.9); provider 14; spinner cellSize 2.4; the unread dot 7 pt at the top-trailing corner moved (2.5, -2.5)
    public void The_badge_box_is_20_points_with_its_glyphs_at_the_recorded_sizes_and_the_dot_rides_the_top_trailing_corner() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var plain = NewThread(project, "Plain", 0);
        var pinned = NewThread(project, "Pinned", 1) with { IsPinned = true };
        var unread = NewThread(project, "Unread", 2) with { HasUnread = true, LastStatus = TurnStatus.Completed };
        var model = Model(harness, [project], [plain, pinned, unread]);
        harness.Main.OnMain(() =>
        {
            Border HostBadge(ChatThread thread)
            {
                var host = Hosted(model, (Control)Activator.CreateInstance(Private("ThreadBadge"), thread)!);
                host.HorizontalAlignment = HorizontalAlignment.Left; host.VerticalAlignment = VerticalAlignment.Top;
                WindowOf(host).UpdateLayout();
                return host;
            }
            var plainHost = HostBadge(plain);
            var box = LogicalQuery.SelfAndDescendants<SidebarIconBadge>(plainHost).Single();
            var square = Assert.IsType<Panel>(box.Child);
            Assert.Equal(new Size(20, 20), new Size(square.Width, square.Height)); // Chrome.iconSize
            var provider = LogicalQuery.SelfAndDescendants<ProviderIcon>(plainHost).Single();
            Assert.Equal(14d, provider.Size); // :2163 ProviderIcon(size: 14)
            Assert.Empty(LogicalQuery.SelfAndDescendants<Avalonia.Controls.Shapes.Ellipse>(plainHost)); // no unread, no dot
            var pinHost = HostBadge(pinned);
            var pin = LogicalQuery.SelfAndDescendants<SwiftImage>(pinHost).Single();
            Assert.Equal(14d * 0.9, pin.Width, 6); // :2160 SidebarSymbol("pin.fill", scale: 0.9): 12.6 pt
            Assert.Equal(14d * 0.9, pin.Height, 6);
            var unreadHost = HostBadge(unread);
            var dot = LogicalQuery.SelfAndDescendants<Avalonia.Controls.Shapes.Ellipse>(unreadHost).Single();
            var badgeFrame = Frame(LogicalQuery.SelfAndDescendants<SidebarIconBadge>(unreadHost).Single(), unreadHost);
            var dotFrame = Frame(dot, unreadHost);
            Assert.Equal(new Size(7, 7), dotFrame.Size);
            Assert.Equal(badgeFrame.Right + 2.5, dotFrame.Right, 3); // :2170-2171 topTrailing, offset x +2.5
            Assert.Equal(badgeFrame.Top - 2.5, dotFrame.Top, 3); // y -2.5
            // the mark is the quiet working beads at 12 pt (Calculi WU6, ED-NEW-calculi-wu6-1), the same in the status (:2157, :2419)
            var runtime = model.Runtime(plain.Id); runtime.IsHydraMerging = true; runtime.HydraMergeStage = "S";
            var runningHost = HostBadge(plain);
            var spinner = LogicalQuery.SelfAndDescendants<Marrow.UI.Port.Nth.Beads.NthBeadsSlot>(runningHost).Single();
            Assert.Equal(new Size(12, 12), spinner.DesiredSize);
            Assert.Equal("Working", Avalonia.Automation.AutomationProperties.GetName(spinner)); // MiniSpinner's accessible name (PR-THEME-171) is kept
            // the ghost's badge is the same marks with the spinner held still (:2100): the beads' still pose, a plain drawing a snapshot carries
            var ghost = (Control)Activator.CreateInstance(Private("ThreadGhostBadge"), model.Thread(plain.Id)!, model.ExistingRuntime(plain.Id))!;
            WindowOf(Hosted(model, ghost)).UpdateLayout();
            var still = LogicalQuery.SelfAndDescendants<Control>(ghost).Single(c => c.GetType().Name == "NthBeadsStill");
            Assert.Equal(spinner.DesiredSize, still.DesiredSize);
            Assert.Empty(LogicalQuery.SelfAndDescendants<Marrow.UI.Port.Nth.Beads.NthBeadsControl>(ghost)); // no moving visual in a picture
        });
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-024")] // SidebarView.swift:2412-2427: hand .subheadline in Chrome.warning; spinner cellSize 2.4; the unread dot 7x7 padded 4 at the trailing end, danger after a failed turn, accent otherwise
    public void The_trailing_statuss_hand_spinner_and_dot_have_the_recorded_font_size_colour_and_padding() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var failed = NewThread(project, "Failed", 0) with { HasUnread = true, LastStatus = TurnStatus.Failed };
        var done = NewThread(project, "Done", 1) with { HasUnread = true, LastStatus = TurnStatus.Completed };
        var model = Model(harness, [project], [failed, done]);
        harness.Main.OnMain(() =>
        {
            T Status<T>(ChatThread thread) where T : Control => LogicalQuery.SelfAndDescendants<T>(Hosted(model, (Control)Activator.CreateInstance(Private("ActivityStatus"), thread)!)).Single();
            var failedDot = Status<Avalonia.Controls.Shapes.Ellipse>(failed);
            Assert.Equal(new Size(7, 7), new Size(failedDot.Width, failedDot.Height));
            Assert.Equal(new Thickness(0, 0, 4, 0), failedDot.Margin); // :2425 .padding(.trailing, 4)
            Assert.Equal(Chrome.Danger.Resolve(failedDot.ActualThemeVariant), ((ISolidColorBrush)failedDot.Fill!).Color); // :2423 after a failed turn
            var okDot = Status<Avalonia.Controls.Shapes.Ellipse>(done);
            Assert.Equal(Chrome.Accent.Resolve(okDot.ActualThemeVariant), ((ISolidColorBrush)okDot.Fill!).Color);
            var runtime = model.Runtime(done.Id); runtime.IsHydraMerging = true; runtime.HydraMergeStage = "S";
            var spinner = Status<Marrow.UI.Port.Nth.Beads.NthBeadsSlot>(done);
            Assert.Equal(new Size(12, 12), spinner.DesiredSize); // :2419, the quiet beads at 12 pt
            typeof(ThreadRuntime).GetProperty("Approvals")!.SetValue(runtime,
                new ValueList<ApprovalRequest>([new ApprovalRequest("a1", ApprovalRequest.Kind.Tool, "Allow?", ValueList<ApprovalRequest.Option>.Empty)]));
            var hand = Status<SwiftImage>(done);
            Assert.Equal(SwiftFont.Subheadline, SwiftImage.GetFont(hand)); // :2416 .font(.system(.subheadline))
            Assert.Equal(11d, SwiftImage.GetFont(hand)!.PointSize);
            Assert.Equal(Chrome.Warning.Resolve(hand.ActualThemeVariant), ((ISolidColorBrush)hand.Foreground!).Color); // :2417 Chrome.warning (the badge's :2155 is Chrome.orange: two tokens)
        });
    });

    // ---- UI-SIDEBAR-019: the project line ----

    [Fact]
    [Trait("Req", "UI-SIDEBAR-019")] // SidebarView.swift:2053-2063 HStack(spacing: 4) { Image.font(.system(.footnote)); Text.font(.system(.callout)) }, 3 pt under the title (the VStack's spacing), Chrome.secondaryText
    public void The_project_line_is_a_footnote_glyph_4_points_before_a_callout_text_3_points_under_the_title_in_secondary_text() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Title", 0);
        var model = Model(harness, [project], [thread]);
        harness.Main.OnMain(() =>
        {
            var face = MakeFace(thread, "Alpha", isSelected: false, trailingClearance: 18);
            face.HorizontalAlignment = HorizontalAlignment.Left; face.VerticalAlignment = VerticalAlignment.Top; face.Width = 260;
            var host = Hosted(model, face);
            WindowOf(host).UpdateLayout();
            var title = LogicalQuery.SelfAndDescendants<TextBlock>(face).First(t => t.Text == "Title");
            var label = LogicalQuery.SelfAndDescendants<TextBlock>(face).Single(t => t.Text == "Alpha");
            var glyph = LogicalQuery.SelfAndDescendants<SwiftImage>(face).Single();
            Assert.Equal(SwiftFont.Footnote, SwiftImage.GetFont(glyph)); // :2056 (10 pt)
            Assert.Equal(10d, SwiftImage.GetFont(glyph)!.PointSize);
            Assert.Equal(12d, label.FontSize); // :2058 callout
            Assert.Equal(4d, Frame(label, face).Left - Frame(glyph, face).Right, 3); // HStack(spacing: 4)
            Assert.Equal(3d, Frame(glyph, face).Top >= Frame(title, face).Bottom ? Frame(label, face).Top - Frame(title, face).Bottom : 0d, 1); // the VStack's 3 under the title
            var secondary = Chrome.SecondaryText.Resolve(label.ActualThemeVariant);
            Assert.Equal(secondary, ((ISolidColorBrush)label.Foreground!).Color); // :2061
            Assert.Equal(secondary, ((ISolidColorBrush)glyph.Foreground!).Color);
            // a deleted project yields an empty string and leaves the glyph alone on the line (notes of the record)
            var orphan = MakeFace(thread, "", isSelected: false, trailingClearance: 18);
            Assert.Single(LogicalQuery.SelfAndDescendants<SwiftImage>(orphan));
            Assert.Equal("", LogicalQuery.SelfAndDescendants<TextBlock>(orphan).Last().Text);
        });
    });

    // ---- UI-SIDEBAR-023: the time label's tabular digits ----

    [Fact]
    [Trait("Req", "UI-SIDEBAR-023")] // SidebarView.swift:2691 `.font(.system(.subheadline).monospacedDigit())`
    public void The_relative_time_label_draws_tabular_digits() => headless.OnUiThread(() =>
    {
        var label = Shown((Control)Activator.CreateInstance(Private("RelativeTimeLabel"), MinuteClock.Shared.Now.AddMinutes(-3))!);
        var text = LogicalQuery.SelfAndDescendants<TextBlock>(label).Single();
        Assert.Equal(SwiftFont.Subheadline.MonospacedDigit().Resolve().FontFeatures, text.FontFeatures);
        Assert.NotNull(text.FontFeatures);
        Assert.Contains(text.FontFeatures!, feature => feature.Tag == "tnum");
    });

    // ---- UI-SIDEBAR-017, -021, -022: the title's room eases with the hover (the trailing clearance rides the hover's transaction) ----

    [Fact]
    [Trait("Req", "UI-SIDEBAR-017")] // SidebarView.swift:1816-1818 `withAnimation(Chrome.hover) { isHovering = hovering }`; :1739-1743 the face's trailingClearance reads showsActions; :2068 `.padding(.trailing, rowHorizontalPadding + trailingClearance)`
    [Trait("Req", "UI-SIDEBAR-021")] // the clearance the buttons need (the archive button joins the ellipsis)
    [Trait("Req", "UI-SIDEBAR-022")] // "its presence also adds 20 pt to the title's trailing clearance": the fold chevron's 20 eases in with it
    // The padding is a number in the transaction the hover is written in, so the title's room goes from 18 to 52 (a detailed
    // row) over Chrome.hover and the truncation point glides; OD-0076 also names it ("a label whose width changes eases its
    // container's width"). It ends on the source's values and comes back the same way.
    public void A_detailed_rows_title_room_eases_from_18_to_52_on_the_hover_and_back() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "A long title that wants all the room it can get", 0);
        var model = Model(harness, [project], [thread]);
        using var shared = RowGlideAnimator.Shared.Override(new RowGlideAnimator(new FakeGeniePlatform()));
        Control row = null!;
        void Tick(int ms)
        {
            harness.Main.AdvanceMilliseconds(ms);
            harness.Main.OnMain(() => { manual.Advance(TimeSpan.FromMilliseconds(ms)); Dispatcher.UIThread.RunJobs(); });
        }
        double Right() => LogicalQuery.SelfAndDescendants<ThreadRowFace>(row).Single() is { } face ? ((Border)face.Child!).Padding.Right : double.NaN;
        try
        {
            harness.Main.OnMain(() =>
            {
                row = LiveThreadRow(thread, "Alpha");
                var window = WindowOf(Hosted(model, row));
                window.UpdateLayout();
                Assert.Equal(8d + 18, Right()); // :1894-1898 isDetailed, no actions
                window.MouseMove(row.TranslatePoint(new Point(10, 10), window)!.Value);
            });
            Tick(50);
            double last = 0;
            harness.Main.OnMain(() => { last = Right(); Assert.InRange(last, 8 + 18 + 1, 8 + 52 - 1); }); // on its way, not cut
            Tick(30);
            harness.Main.OnMain(() => { Assert.InRange(Right(), last, 8 + 52); last = Right(); });
            Tick(200);
            harness.Main.OnMain(() =>
            {
                Assert.Equal(8d + 52, Right()); // :1894-1895 showsActions, open, no fold
                var window = WindowOf(row);
                window.MouseMove(new Point(window.Bounds.Width + 50, window.Bounds.Height + 50));
            });
            Tick(50);
            harness.Main.OnMain(() => Assert.InRange(Right(), 8 + 18 + 1, 8 + 52 - 1)); // and back the same way
            Tick(300);
            harness.Main.OnMain(() => Assert.Equal(8d + 18, Right()));
        }
        finally { harness.Main.OnMain(() => WindowOf(row).Close()); }
    });

    [Theory]
    [Trait("Req", "UI-SIDEBAR-021")] // SidebarView.swift:1775-1784 HStack(spacing: 0) { archive button (RowAccessoryIcon("archivebox")); RowActionsButton }, :1800 .padding(.trailing, 6), :1894-1895 clearance 52
    [Trait("Req", "UI-SIDEBAR-022")] // SidebarView.swift:1803-1814 RowAccessoryIcon(folded ? "chevron.right" : "chevron.down") .padding(.trailing, foldInset 46); presence adds 20 to the clearance
    [InlineData(true)]
    [InlineData(false)]
    public void On_a_hovered_row_the_archive_button_sits_before_the_ellipsis_and_the_fold_chevron_46_points_from_the_edge(bool folded) => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Row", 0) with { ExpandsHelpers = !folded };
        var model = Model(harness, [project], [thread]);
        using var shared = RowGlideAnimator.Shared.Override(new RowGlideAnimator(new FakeGeniePlatform()));
        Control row = null!;
        try
        {
            harness.Main.OnMain(() =>
            {
                model.Settings.ThreadFinishAction = ThreadFinishAction.Archive; // the archive button, not the settle check (:1776)
                row = LiveThreadRow(thread, fold: () => { });
                var window = WindowOf(Hosted(model, row));
                window.UpdateLayout();
                window.MouseMove(row.TranslatePoint(new Point(10, 10), window)!.Value);
            });
            harness.Main.AdvanceMilliseconds(300);
            harness.Main.OnMain(() => { manual.Advance(TimeSpan.FromMilliseconds(300)); WindowOf(row).UpdateLayout(); });
            harness.Main.OnMain(() =>
            {
                var archive = LogicalQuery.SelfAndDescendants<Button>(row).Single(b => ToolTip.GetTip(b) as string == "Archive thread"); // :1783 .help
                var ellipsis = LogicalQuery.SelfAndDescendants<RowActionsButton>(row).Single();
                var chevron = ButtonNamed(row, folded ? "Show helpers" : "Hide helpers");
                var rowFrame = new Rect(row.Bounds.Size);
                Assert.Equal(new Size(20, 20), archive.Bounds.Size); // RowAccessoryIcon's 20 pt square
                Assert.Equal(new Size(20, 20), chevron.Bounds.Size);
                Assert.Equal(archive.TranslatePoint(new Point(20, 0), row)!.Value.X, ellipsis.TranslatePoint(default, row)!.Value.X, 0.6); // HStack(spacing: 0): the ellipsis follows at once
                Assert.Equal(rowFrame.Right - 6, ellipsis.TranslatePoint(new Point(ellipsis.Bounds.Width, 0), row)!.Value.X, 0.6); // .padding(.trailing, 6)
                Assert.Equal(rowFrame.Right - 46, chevron.TranslatePoint(new Point(20, 0), row)!.Value.X, 0.6); // .padding(.trailing, foldInset)
                var symbol = LogicalQuery.SelfAndDescendants<SwiftImage>(chevron).Single().SymbolName;
                Assert.Equal(folded ? "chevron.right" : "chevron.down", symbol); // :1808
                var face = LogicalQuery.SelfAndDescendants<ThreadRowFace>(row).Single();
                Assert.Equal(8d + 52 + 20, ((Border)face.Child!).Padding.Right); // :1894-1895 the chevron adds 20 to the title's clearance
                Assert.Equal(Chrome.SecondaryText.Resolve(row.ActualThemeVariant), ((ISolidColorBrush)LogicalQuery.SelfAndDescendants<SwiftImage>(archive).Single().Foreground!).Color); // Chrome.secondaryText
            });
        }
        finally { harness.Main.OnMain(() => WindowOf(row).Close()); }
    });

    // ---- ED-275: every row of the list draws Apple's continuous corner at 7 pt (UI-SIDEBAR-008, -017, -035, -036) ----

    [Fact]
    [Trait("Req", "UI-SIDEBAR-008")] // SidebarView.swift:1078-1166 ProjectRow body -> Chrome.swift:722 `RoundedRectangle(cornerRadius: rowCornerRadius, style: .continuous)`: 7 pt
    [Trait("Req", "UI-SIDEBAR-017")] // SidebarView.swift:1735 `let shape = RoundedRectangle(cornerRadius: Chrome.rowCornerRadius, style: .continuous)`
    [Trait("Req", "UI-SIDEBAR-035")] // SidebarView.swift:1580 the folded stub's RoundedRectangle(cornerRadius: rowCornerRadius, style: .continuous), 24 pt high
    [Trait("Req", "UI-SIDEBAR-036")] // SidebarView.swift:1519-1521 the head card's background + clipShape, 7 pt continuous
    // ED-275 (2): a `.continuous` corner is Apple's while 2 * 1.528665 * r <= min(w, h): at r 7 that is 21.4 pt, so the 24 pt
    // stub, the 28 pt rows and the head card are all inside the regime the oracle backs, not in the unresolved band; the
    // Border keeps its CornerRadius (7, the circle of the same radius) and is cut to the curve by Visual.Clip (ED-275 (3b)).
    public void Every_row_shape_of_the_list_is_a_7_point_continuous_corner_in_Apples_regime_and_cut_to_the_curve() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var helper = NewThread(project, "Helper", 1) with { ParentThreadID = parent.Id };
        var folded = NewThread(project, "Folded parent", 2) with { ExpandsHelpers = false };
        var foldedHelper = NewThread(project, "Its helper", 3) with { ParentThreadID = folded.Id };
        var heads = Enumerable.Range(0, 3).Select(i => HeadHelper(project, parent.Id, i, 4 + i)).ToList();
        var settled = NewThread(project, "Settled", 8) with { IsSettled = true, SettledAt = Base.AddHours(1) };
        var model = Model(harness, [project], new[] { parent, helper, folded, foldedHelper, settled }.Concat(heads).ToList());
        SidebarView view = null!;
        try
        {
            harness.Main.OnMain(() =>
            {
                model.Settings.HydraAutoHidesIdleHeads = false;
                model.Settings.SettledCollapsed = false;
                view = Attached(model);
                WindowOf(view).UpdateLayout();
            });
            harness.Main.OnMain(() =>
            {
                var shapes = LogicalQuery.SelfAndDescendants<Border>(view)
                    .Where(b => SwiftShapes.GetCornerStyle(b) == RoundedCornerStyle.Continuous && b.CornerRadius == new CornerRadius(Chrome.RowCornerRadius) && b.Bounds.Height >= 14)
                    .ToList();
                Assert.Equal(7d, Chrome.RowCornerRadius); // Chrome.swift:49
                // a project row, a thread row, a helper row, a folded stub, a head card, a settled row: at least one of each kind drew a shape
                Assert.True(shapes.Count >= 6, $"{shapes.Count} continuous 7 pt shapes");
                foreach (var shape in shapes)
                {
                    Assert.True(SwiftShapes.HasContinuousCorner(shape.Bounds.Size, shape.CornerRadius, RoundedCornerStyle.Continuous),
                        $"a {shape.Bounds.Width}x{shape.Bounds.Height} shape is in Apple's regime at r 7");
                    if (shape.Background is not null && shape.BoxShadow.Count == 0)
                        Assert.NotNull(shape.Clip); // cut to the curve, not left as the circle (ED-275 (3b))
                }
                var stub = shapes.Where(b => Math.Abs(b.Bounds.Height - 24) < 0.5).ToList();
                Assert.NotEmpty(stub); // the 24 pt stub and the settled rows
            });
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    });

    // ---- PR-SIDEBAR-135: the glide's two faces (OD-0098: the values SidebarView.swift:1933-1978 states, against renders) ----

    private static (SidebarSweepPlatform Platform, RowGlideAnimator Animator) GlideFaces(RowGlideAnimator.Glide.Direction direction, Rect list, Rect from,
        ChatThread thread, string? projectName, bool selected, bool hovering)
    {
        var platform = new SidebarSweepPlatform();
        var animator = new RowGlideAnimator(platform);
        CallStatic(Private("SidebarThreadRow"), "Glide", animator, direction, list, from, thread, projectName, null, selected, hovering, null, Avalonia.Styling.ThemeVariant.Dark);
        return (platform, animator);
    }

    private static ThreadRowFace FaceOf(SidebarSweepPlatform platform, int index) => LogicalQuery.SelfAndDescendants<ThreadRowFace>(Assert.IsType<ThemeVariantScope>(platform.Renders[index].Content)).Single();

    [Theory]
    [Trait("Req", "PR-SIDEBAR-135")] // SidebarView.swift:1933-1978: from = windowFrame, bounds = listFrame; arrivalHeight; fallback; departureClearance/arrivalClearance; arriving = thread with isSettled = destination (and, for a settle, unpinned and read)
    //                      direction, thread settled, pinned, unread, project, selected, hovering, from height, arrival height, departure clearance, arrival clearance, fallback y
    [InlineData("Reopen", true, false, false, null, false, true, 24d, 28d, 30d, 52d, 100d - 28d)] // a settled row reopens: out of its 30 pt room into the open row's 52; no project line: 28
    [InlineData("Reopen", true, false, true, "Alpha", false, true, 24d, 48d, 30d, 18d, 100d - 48d)] // with a project name the open row is detailed: 48 pt, clearance 18
    [InlineData("Reopen", true, true, true, "Alpha", true, true, 24d, 48d, 30d, 18d, 100d - 48d)] // selected
    [InlineData("Settle", false, true, true, "Alpha", false, true, 48d, 24d, 52d, 18d, 100d + 400d)] // the kept settle branch: the destination is the 24 pt settled row at the list's bottom edge, arriving unpinned and read
    [InlineData("Settle", false, false, false, null, false, false, 28d, 24d, 52d, 18d, 100d + 400d)]
    public void The_glides_two_faces_have_the_sources_heights_clearances_destination_state_and_fallback(
        string directionName, bool threadSettled, bool pinned, bool unread, string? projectName, bool selected, bool hovering,
        double fromHeight, double arrivalHeight, double departureClearance, double arrivalClearance, double fallbackY) => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "A title", 0) with { IsSettled = threadSettled, IsPinned = pinned, HasUnread = unread };
        var direction = directionName == "Settle" ? RowGlideAnimator.Glide.Direction.Settle : RowGlideAnimator.Glide.Direction.Reopen;
        var list = new Rect(10, 100, 200, 400);
        var from = new Rect(10, 300, 200, fromHeight);
        harness.Main.OnMain(() =>
        {
            var (platform, animator) = GlideFaces(direction, list, from, thread, projectName, selected, hovering);
            var glide = Assert.Single(animator.Glides);
            Assert.Equal(2, platform.Renders.Count);
            Assert.Equal(new Rect(from.X, fallbackY, from.Width, arrivalHeight), glide.To); // :1942-1947 until the real row reports where it is
            Assert.Equal(new Size(from.Width, fromHeight), glide.Departure.Size); // :1956 the row's current size
            Assert.Equal(new Size(from.Width, arrivalHeight), glide.Arrival.Size); // :1960 24 / 48 / 28
            Assert.Equal(8d + departureClearance, ((Border)FaceOf(platform, 0).Child!).Padding.Right); // :1952 as if the buttons were out
            Assert.Equal(8d + arrivalClearance, ((Border)FaceOf(platform, 1).Child!).Padding.Right); // :1953 as if they were not
            var departureTitle = LogicalQuery.SelfAndDescendants<TextBlock>(FaceOf(platform, 0)).First(t => t.Text == "A title");
            var arrivalTitle = LogicalQuery.SelfAndDescendants<TextBlock>(FaceOf(platform, 1)).First(t => t.Text == "A title");
            Assert.Equal(threadSettled ? 12d : 13d, departureTitle.FontSize); // the thread as it stands
            Assert.Equal(direction == RowGlideAnimator.Glide.Direction.Settle ? 12d : 13d, arrivalTitle.FontSize); // :1936 the copy with isSettled = the destination state
            if (direction == RowGlideAnimator.Glide.Direction.Settle) Assert.Equal(FontWeight.Normal, arrivalTitle.FontWeight); // :1938-1939 unread cleared (and a settled title is never medium)
            else Assert.Equal(unread ? FontWeight.Medium : FontWeight.Normal, arrivalTitle.FontWeight);
            Assert.Equal(selected ? 0.12 : hovering ? 0.06 : 0d, glide.DepartureFill); // :1971-1972 fill(isSelected:isHovering:)
            Assert.Equal(selected ? 0.12 : 0d, glide.ArrivalFill); // never hovered on arrival
        });
    });

    [Theory]
    [Trait("Req", "PR-SIDEBAR-135")] // SidebarView.swift:1954-1961 animator.render -> RowGlide.swift:127-134 ImageRenderer(content: face.frame(width:height:alignment: .topLeading).environment(\.colorScheme, colorScheme)), renderer.scale = backing scale (2 at least)
    [InlineData(2d)]
    [InlineData(2.5d)]
    // The real renderer, headless: two flat bitmaps at the face sizes times the scale, drawn from the faces the source builds.
    // The ink of each stays inside the room the source leaves it: the settled departure draws nothing inside its 8 + 22 title
    // inset and stops short of the 8 + 30 clearance; the open, detailed arrival starts at the 8 pt padding and stops short of
    // its 8 + 18 clearance. (Only the bounds are asserted: the headless font of the Linux runner draws other glyph shapes
    // than Windows' Inter, so where inside the room a glyph sits is not.)
    public void The_glides_faces_are_rendered_as_flat_bitmaps_at_the_scale_with_their_ink_inside_the_sources_padding(double scale) => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Reopened thread", 0) with { IsSettled = true };
        var animator = new RowGlideAnimator(new WindowsGeniePlatform());
        var list = new Rect(10, 100, 240, 400);
        var from = new Rect(10, 300, 240, 24);
        harness.Main.OnMain(() =>
        {
            typeof(SidebarView).Assembly.GetType("Marrow.App.Views.Sidebar.SidebarThreadRow")!
                .GetMethod("GlideAtScale", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [animator, RowGlideAnimator.Glide.Direction.Reopen, list, from, thread, "Alpha", null, false, true, null, Avalonia.Styling.ThemeVariant.Dark, scale]);
            var glide = Assert.Single(animator.Glides);
            var effective = Math.Max(2, scale);
            foreach (var (image, size) in new[] { (glide.Departure, new Size(240, 24)), (glide.Arrival, new Size(240, 48)) })
            {
                var bitmap = Assert.IsAssignableFrom<Avalonia.Media.Imaging.Bitmap>(image);
                Assert.Equal(new PixelSize((int)Math.Ceiling(size.Width * effective), (int)Math.Ceiling(size.Height * effective)), bitmap.PixelSize);
            }
            (double Left, double Right) Ink(IImage image, double sizeWidth)
            {
                using var stream = new MemoryStream();
#pragma warning disable CS0618
                ((Avalonia.Media.Imaging.Bitmap)image).Save(stream);
#pragma warning restore CS0618
                stream.Position = 0;
                using var decoded = SkiaSharp.SKBitmap.Decode(stream);
                int left = int.MaxValue, right = -1;
                for (var y = 0; y < decoded.Height; y++)
                    for (var x = 0; x < decoded.Width; x++)
                        if (decoded.GetPixel(x, y).Alpha > 3) { left = Math.Min(left, x); right = Math.Max(right, x); }
                Assert.True(right >= 0, "the face drew something");
                return (left / effective, (right + 1) / effective);
            }
            var departure = Ink(glide.Departure, 240);
            Assert.True(departure.Left >= 8 + 22 - 0.5, $"departure ink starts at {departure.Left}"); // nothing is drawn in the settled title's 8 + 22 inset, :2067
            Assert.True(departure.Right <= 240 - (8 + 30) + 0.5, $"departure ink ends at {departure.Right}"); // :2068 clearance 30
            var arrival = Ink(glide.Arrival, 240);
            Assert.InRange(arrival.Left, 8 - 0.5, 8 + 4); // detailed: the title starts at the 8 pt padding
            Assert.True(arrival.Right <= 240 - (8 + 18) + 0.5, $"arrival ink ends at {arrival.Right}"); // clearance 18
        });
    });

    // ======== The rename popover (PR-SIDEBAR-091, UI-SIDEBAR-028, -029, -030) ========

    private static Control ShownRenameContent(SidebarView view, Guid threadID)
    {
        var content = (Control)Call(view, "BuildRenamePopoverContent", threadID)!; // :248-249 the popover's content, presentedChrome() on it
        content.HorizontalAlignment = HorizontalAlignment.Left; content.VerticalAlignment = VerticalAlignment.Top;
        var window = new AvaloniaWindow { Content = new Border { Child = content, Width = 360, Height = 200, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return content;
    }

    private static Border Bezel(Button button) => button.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_Bezel");

    [Fact]
    [Trait("Req", "PR-SIDEBAR-091")] // SidebarView.swift:1002-1022 VStack(alignment: .leading, spacing: 10) { heading; field; HStack { Spacer(); Cancel; Rename } }.padding(14).frame(width: 300)
    [Trait("Req", "UI-SIDEBAR-028")] // frame width 300; padding 14; spacing 10; the button row right-aligned; heading .system(.body, weight: .semibold) (13 pt)
    [Trait("Req", "UI-SIDEBAR-029")] // the field fills the popover's inner width
    public void The_rename_popovers_column_is_300_wide_padded_14_spaced_10_with_the_field_filling_the_width_and_the_buttons_at_the_right() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Old title", 0);
        var model = Model(harness, [project], [thread]);
        harness.Main.OnMain(() =>
        {
            var view = Detached(model);
            SetProp(Field(view, "state")!, "RenameText", "Old title");
            var content = ShownRenameContent(view, thread.Id);
            try
            {
                var frame = LogicalQuery.SelfAndDescendants<Border>(content).First(b => b.Width == 300);
                Assert.Equal(new Thickness(14), frame.Padding);
                var heading = LogicalQuery.SelfAndDescendants<TextBlock>(content).Single(x => x.Text == "Rename thread");
                Assert.Equal(13d, heading.FontSize); // .body, ED-059
                Assert.Equal(FontWeight.SemiBold, heading.FontWeight);
                var field = Assert.Single(LogicalQuery.SelfAndDescendants<TextBox>(content));
                var cancel = LogicalQuery.SelfAndDescendants<Button>(content).Single(b => TextOf(b) == "Cancel");
                var rename = LogicalQuery.SelfAndDescendants<Button>(content).Single(b => TextOf(b) == "Rename");
                var headingFrame = Frame(heading, content); var fieldFrame = Frame(field, content);
                var cancelFrame = Frame(cancel, content); var renameFrame = Frame(rename, content);
                Assert.Equal(14d, headingFrame.Left, 1); // leading-aligned inside the padding
                Assert.Equal(10d, fieldFrame.Top - headingFrame.Bottom, 1); // spacing 10
                Assert.Equal(14d, fieldFrame.Left, 1);
                Assert.Equal(300d - 28, fieldFrame.Width, 1); // fills the inner width
                Assert.Equal(10d, Math.Min(cancelFrame.Top, renameFrame.Top) - fieldFrame.Bottom, 1); // spacing 10 again
                Assert.Equal(300d - 14, renameFrame.Right, 1); // the Spacer pushes the pair to the trailing edge
                Assert.True(cancelFrame.Right <= renameFrame.Left, "Cancel before Rename");
                Assert.Equal(24d, cancel.Bounds.Height, 1); // .controlSize(.regular): the kit's 24 pt push button (SystemButtons)
                Assert.Equal(24d, rename.Bounds.Height, 1);
            }
            finally { Close(content); }
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-091")] // SidebarView.swift:1023 `.onAppear { isFocused = true }`
    [Trait("Req", "UI-SIDEBAR-029")] // "focused on appearance"
    public void The_title_field_holds_the_focus_as_the_popover_appears() => headless.OnUiThread(() =>
    {
        var popover = ShownRename("Focus me", _ => { }, () => { });
        try
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var field = Assert.Single(LogicalQuery.SelfAndDescendants<TextBox>(popover));
            Assert.True(field.IsFocused);
        }
        finally { WindowOf(popover).Close(); }
    });

    [Theory]
    [Trait("Req", "PR-SIDEBAR-091")] // SidebarView.swift:1017 `.disabled(text.trimmingCharacters(in: .whitespaces).isEmpty)`: the set is spaces and tabs (Zs and U+0009), not newlines
    [Trait("Req", "UI-SIDEBAR-030")]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("\t", false)] // a tab is in .whitespaces
    [InlineData("\u00A0", false)] // a no-break space (Zs) too
    [InlineData("\n", true)] // a newline is not: the disabled test is narrower than the model's .whitespacesAndNewlines (UI-SIDEBAR-028 note)
    [InlineData("\r\n", true)]
    [InlineData(" a ", true)]
    public void Rename_is_disabled_only_while_the_text_trimmed_of_spaces_and_tabs_is_empty(string text, bool enabled) => headless.OnUiThread(() =>
    {
        var popover = ShownRename("Seed", _ => { }, () => { });
        try
        {
            var field = Assert.Single(LogicalQuery.SelfAndDescendants<TextBox>(popover));
            var rename = LogicalQuery.SelfAndDescendants<Button>(popover).Single(b => TextOf(b) == "Rename");
            field.Text = text;
            Assert.Equal(enabled, rename.IsEnabled);
        }
        finally { WindowOf(popover).Close(); }
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-091")] // failure_cases: "Return in the field still calls onRename" with blank text, which the model then rejects (PR-SIDEBAR-092); SidebarView.swift:1009 .onSubmit { onRename(text) }
    public void Return_with_a_blank_title_still_calls_onRename_though_the_button_is_disabled() => headless.OnUiThread(() =>
    {
        var renamed = new List<string>();
        var popover = ShownRename("Old", renamed.Add, () => { });
        try
        {
            var field = Assert.Single(LogicalQuery.SelfAndDescendants<TextBox>(popover));
            field.Text = "   ";
            Assert.False(LogicalQuery.SelfAndDescendants<Button>(popover).Single(b => TextOf(b) == "Rename").IsEnabled);
            field.Focus();
            WindowOf(popover).KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(["   "], renamed);
        }
        finally { WindowOf(popover).Close(); }
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-029")] // SidebarView.swift:183-188 (the context menu posts a snapshot, used as it is), :882-887 and :354-358 (the ellipsis path reads the live thread): the live-versus-snapshot asymmetry
    public void The_ellipsis_path_pre_fills_the_live_title_and_the_context_menu_path_the_posted_snapshot() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var presenter = Presentation.Presenter.Override(new RecordingPresenter()); // headless cannot open a real Popup (Port/ViewLifetime.cs); only the state is read
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Snapshot title", 0);
        var model = Model(harness, [project], [thread]);
        SidebarView view = null!;
        try
        {
            harness.Main.OnMain(() =>
            {
                view = Attached(model);
                model.Rename(thread.Id, "Live title"); // the title changed since the row's snapshot
            });
            harness.Main.OnMain(() =>
            {
                Call(view, "RequestRename", thread); // the ellipsis popover's path with the stale snapshot
                Assert.Equal("Live title", Get(Field(view, "state")!, "RenameText"));
                SetProp(Field(view, "state")!, "Renaming", null);
                var requests = Field(view, "menuRequests")!;
                requests.GetType().GetProperty("Rename")!.SetValue(requests, thread); // the AppKit context menu's path posts the snapshot as it was
            });
            harness.Main.OnMain(() =>
            {
                Assert.Equal("Snapshot title", Get(Field(view, "state")!, "RenameText"));
                Assert.Equal(thread.Id, ((ChatThread)Get(Field(view, "state")!, "Renaming")!).Id);
            });
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-030")] // SidebarView.swift:249 `.presentedChrome()` -> Popovers.swift:10 `.buttonBorderShape(.capsule)`: Cancel (.bordered) and Rename (.borderedProminent) are capsules, "not the stock rounded rectangle"
    [Trait("Req", "PR-SIDEBAR-091")]
    // ENGINEERING DECISION (OD-0098, the framework's documented behaviour): a bordered or prominent button takes the
    // border shape of the environment: a capsule's radius is half the button's height (12 at the kit's 24 pt), the same
    // shape for the press wash and the focus ring. Outside a presented tree the kit's radius 6 stands (the stock look).
    public void Under_presentedChrome_the_rename_buttons_are_capsules_and_outside_it_the_stock_rounded_rectangle() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Old title", 0);
        var model = Model(harness, [project], [thread]);
        harness.Main.OnMain(() =>
        {
            var view = Detached(model);
            SetProp(Field(view, "state")!, "RenameText", "Old title");
            var content = ShownRenameContent(view, thread.Id);
            try
            {
                foreach (var button in LogicalQuery.SelfAndDescendants<Button>(content))
                {
                    var bezel = Bezel(button);
                    Assert.Equal(24d, bezel.Bounds.Height, 1);
                    Assert.Equal(12d, bezel.CornerRadius.TopLeft, 3); // half the height
                    Assert.Equal(new CornerRadius(12), bezel.CornerRadius);
                    var press = button.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_Press");
                    Assert.Equal(new CornerRadius(12), press.CornerRadius);
                }
            }
            finally { Close(content); }
            var stock = ShownRename("Seed", _ => { }, () => { }); // no presentedChrome on this tree
            try
            {
                foreach (var button in LogicalQuery.SelfAndDescendants<Button>(stock))
                    Assert.Equal(new CornerRadius(SystemButtons.Radius), Bezel(button).CornerRadius);
            }
            finally { WindowOf(stock).Close(); }
        });
    });

    // ======== The archive question popover's small controls (UI-SIDEBAR-031, -032) ========

    [Fact]
    [Trait("Req", "UI-SIDEBAR-031")] // SidebarView.swift:923-941 `.controlSize(.small)` on a HStack of .glass and .glassProminent text buttons
    [Trait("Req", "UI-SIDEBAR-032")] // "small controls, 6 pt apart": Delete .glass (red label), Archive .glassProminent
    // SOURCE FACT (SwiftGlass.GlassButtonSmall*, ED-280 (2), the reference runtime: `Show`, a `.glass` text button at
    // `.controlSize(.small)`, 48 x 20 with an 11 pt label and 10.5 pt either side) read for every glass text button at the
    // small size; ENGINEERING DECISION (OD-0098): the prominent style takes the same metrics, a control size fixing the size.
    public void The_archive_popovers_small_glass_buttons_are_20_high_with_an_11_point_label_and_10_5_points_either_side() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Fix the bug", 0);
        var anchor = new Border();
        var popover = (Control)Activator.CreateInstance(Private("ArchiveThreadPopover"), thread, anchor, (Action)(() => { }), (Action)(() => { }), (Action)(() => { }))!;
        var window = new AvaloniaWindow { Content = new Panel { Children = { anchor, popover } }, Width = 400, Height = 300 };
        window.Show();
        window.UpdateLayout();
        try
        {
            foreach (var name in new[] { "Delete", "Archive" })
            {
                var button = LogicalQuery.SelfAndDescendants<Button>(popover).Single(b => TextOf(b) == name);
                var label = LogicalQuery.SelfAndDescendants<TextBlock>(button).Single();
                Assert.Equal(SwiftGlass.GlassButtonSmallHeight, button.Bounds.Height, 1);
                Assert.Equal(11d, label.FontSize);
                var labelFrame = Frame(label, button);
                var frame = LogicalQuery.SelfAndDescendants<Border>(button).First(b => b.Padding.Left > 0);
                Assert.Equal(new Thickness(10.5, 0), frame.Padding); // exact in the tree; the drawn offset is layout-rounded to whole pixels at scale 1
                Assert.InRange(labelFrame.Left, 10, 11);
                Assert.InRange(button.Bounds.Width - labelFrame.Right, 10, 11);
                Assert.Equal(20d, SwiftGlass.GlassButtonSmallHeight);
                Assert.Equal(10.5, SwiftGlass.GlassButtonSmallPadding);
            }
        }
        finally { window.Close(); }
    });

    // ======== The delete question's life in its popup (UI-SIDEBAR-026, -027) ========

    private static Popup DeletePopup(Control root) =>
        LogicalQuery.SelfAndDescendants<Popup>(root).Single(p => PopoverHost.Content(p)?.GetType() == Private("DeleteThreadPopover"));

    [Fact]
    [Trait("Req", "UI-SIDEBAR-026")] // SidebarView.swift:217-237: the binding is built on the thread's id; "clicking away or pressing Escape keeps the thread" (the popup closing is the host's dismissal, :222 `if !isPresented && ...id == id { pendingDeletionID = nil }`)
    public void Dismissing_the_delete_question_through_its_popup_keeps_the_thread_and_lets_it_be_asked_again() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var recorder = new RecordingPresenter();
        using var presented = Presentation.Presenter.Override(recorder);
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Kept", 0);
        var model = Model(harness, [project], [thread]);
        SidebarView view = null!;
        object State() => Field(view, "state")!;
        try
        {
            harness.Main.OnMain(() => { model.Settings.ConfirmBeforeDeleting = true; view = Attached(model); });
            harness.Main.OnMain(() => Call(view, "RequestDeletion", thread.Id));
            Popup popup = null!;
            harness.Main.OnMain(() =>
            {
                popup = DeletePopup(view);
                Assert.Equal(thread.Id, Get(State(), "PendingDeletionID")); // :190 asked
                Assert.True(recorder.Calls.Last(c => c.Popup == popup).Presented);
                RaisePopup(popup, "Opened");
                RaisePopup(popup, "Closed"); // a click away or Escape: the popup's own close
            });
            harness.Main.OnMain(() => { });
            harness.Main.OnMain(() =>
            {
                Assert.Null(Get(State(), "PendingDeletionID")); // :222
                Assert.Null(Get(State(), "ConfirmedDeletion")); // nothing half-confirmed is left behind
                Assert.False((bool)Get(State(), "IsDeletePopoverShown")!);
                Assert.NotNull(model.Thread(thread.Id)); // the thread stays
                Call(view, "RequestDeletion", thread.Id); // and the question can be asked again
            });
            harness.Main.OnMain(() => Assert.Equal(thread.Id, Get(State(), "PendingDeletionID")));
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    });

    [Theory]
    [Trait("Req", "UI-SIDEBAR-027")] // SidebarView.swift:977-980 the rows, :256-263 confirmDeletion, :229-232 onAppear/onDisappear, :295-306 carryOutConfirmedDeletion: the delete runs only once the popover content has disappeared
    [Trait("Req", "UI-SIDEBAR-026")] // "click a row to confirm"
    [InlineData("Delete thread", false)]
    [InlineData("Delete thread and worktree", true)]
    public void A_confirmed_delete_waits_for_the_popup_to_close_and_then_runs_on_the_next_turn(string row, bool removesWorktree) => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var recorder = new RecordingPresenter();
        using var presented = Presentation.Presenter.Override(recorder);
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Gone", 0) with { WorktreePath = harness.NewFolder("wt") };
        var model = Model(harness, [project], [thread]);
        SidebarView view = null!;
        try
        {
            harness.Main.OnMain(() => { model.Settings.ConfirmBeforeDeleting = true; view = Attached(model); });
            harness.Main.OnMain(() => Call(view, "RequestDeletion", thread.Id));
            Popup popup = null!;
            harness.Main.OnMain(() =>
            {
                popup = DeletePopup(view);
                RaisePopup(popup, "Opened"); // :229
                Click(LogicalQuery.SelfAndDescendants<Button>(PopoverHost.Content(popup)!).Single(b => AutomationProperties.GetName(b) == row));
            });
            harness.Main.OnMain(() => { });
            harness.Main.OnMain(() =>
            {
                var confirmed = Get(Field(view, "state")!, "ConfirmedDeletion")!;
                Assert.Equal(removesWorktree, Get(confirmed, "RemoveWorktree"));
                Assert.NotNull(model.Thread(thread.Id)); // the popup is still up: removing the row it hangs on is what crashed upstream (the note of UI-SIDEBAR-027)
            });
            harness.Main.OnMain(() => { });
            harness.Main.OnMain(() =>
            {
                Assert.NotNull(model.Thread(thread.Id)); // however many turns pass
                RaisePopup(popup, "Closed"); // :231-232
                Assert.NotNull(model.Thread(thread.Id)); // not inside the close itself: a Task on the main actor (:303-305)
            });
            harness.Main.OnMain(() => { });
            harness.Main.OnMain(() =>
            {
                Assert.Null(model.Thread(thread.Id)); // now it runs
                Assert.Null(Get(Field(view, "state")!, "ConfirmedDeletion")); // :301
            });
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    });

    // ======== The column's window-drag background (UI-SIDEBAR-001) ========

    [Fact]
    [Trait("Req", "UI-SIDEBAR-001")] // SidebarView.swift:74-80 the Color.clear spacer, :146 `.background { if !inPopover { WindowDragArea() } }`: "pressing it drags the window"
    // The spacer is a clear, hit-through item over the column's WindowDragArea, so a press there lands on the area, which is
    // a caption to the Windows hit test (ED-063) and starts the move loop on the press (ED-200, the shared Chrome component
    // and its own tests); a press on the search field lands on the field, and the popover form has no area at all. The
    // system's move loop itself needs a real window (NATIVE-ONLY) and is not claimed here.
    public void A_press_on_the_clearance_spacer_lands_on_the_columns_drag_area_and_the_popover_form_has_none() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        harness.Main.OnMain(() =>
        {
            var column = Attached(model);
            var popover = Attached(model, inPopover: true);
            try
            {
                var window = WindowOf(column);
                window.UpdateLayout();
                using (window.CaptureRenderedFrame()) { } // a rendered frame: the scene the hit test reads
                var area = Assert.Single(LogicalQuery.SelfAndDescendants<WindowDragArea>(column));
                Assert.Equal(Win32Properties.Win32HitTestValue.Caption, Win32Properties.GetNonClientHitTestResult(area));
                // inside the 14 pt spacer (16..30 pt down) and in the gutter beside the search row's top padding
                var spacerPoint = column.TranslatePoint(new Point(140, 23), window)!.Value;
                var hit = window.InputHitTest(spacerPoint) as Visual;
                Assert.NotNull(hit);
                Assert.True(ReferenceEquals(hit, area) || hit!.GetVisualAncestors().Contains(area), $"the spacer's press lands on {hit!.GetType().Name}");
                // the area covers the whole column (:146 background of the full-height VStack)
                Assert.Equal(column.Bounds.Size, area.Bounds.Size);
                // a press on the search field is the field's
                var fieldPoint = LogicalQuery.SelfAndDescendants<SidebarSearchField>(column).Single().TranslatePoint(new Point(20, 14), window)!.Value;
                var fieldHit = window.InputHitTest(fieldPoint) as Visual;
                Assert.False(ReferenceEquals(fieldHit, area));
                // the popover form: no area, no spacer
                Assert.Empty(LogicalQuery.SelfAndDescendants<WindowDragArea>(popover));
            }
            finally { Close(column); Close(popover); }
        });
    });

    // ======== The list's real scroll wiring (UI-SIDEBAR-005) ========

    [Fact]
    [Trait("Req", "UI-SIDEBAR-005")] // SidebarView.swift:128-148 `.onScrollGeometryChange(for: CGFloat.self, of: { $0.contentOffset.y }) { old, new in if old != new { noteListScroll() } }`, :115 `.allowsHitTesting(!isListScrolling)`, :392-404 the 150 ms settle: "scrolling (hit testing off for the whole list, lifted 150 ms after the last movement)"
    public void A_real_scroll_offset_change_and_a_real_wheel_tick_freeze_the_list_and_an_unchanged_offset_does_not() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var threads = Enumerable.Range(0, 40).Select(i => NewThread(project, "Thread " + i, i)).ToList();
        var model = Model(harness, [project], threads);
        SidebarView view = null!;
        try
        {
            harness.Main.OnMain(() =>
            {
                view = Attached(model);
                WindowOf(view).UpdateLayout();
                var scroll = LogicalQuery.SelfAndDescendants<ScrollViewer>(view).Single();
                Assert.True(scroll.Extent.Height > scroll.Viewport.Height, "the list scrolls");
                Assert.True(ListStack(view).IsHitTestVisible);
                scroll.Offset = scroll.Offset; // nothing moved: no freeze
                Assert.True(ListStack(view).IsHitTestVisible);
                scroll.Offset = new Vector(0, 40); // the content offset moved
            });
            harness.Main.OnMain(() => Assert.False(ListStack(view).IsHitTestVisible)); // :397 isListScrolling = true: the rows take no hover or clicks
            harness.Main.AdvanceMilliseconds(149);
            harness.Main.OnMain(() => Assert.False(ListStack(view).IsHitTestVisible));
            harness.Main.AdvanceMilliseconds(1);
            harness.Main.OnMain(() => Assert.True(ListStack(view).IsHitTestVisible)); // :402 150 ms after the last movement
            var before = 0.0;
            harness.Main.OnMain(() =>
            {
                var window = WindowOf(view);
                var at = view.TranslatePoint(new Point(120, 300), window)!.Value;
                before = LogicalQuery.SelfAndDescendants<ScrollViewer>(view).Single().Offset.Y;
                window.MouseWheel(at, new Vector(0, -1)); // a real wheel tick: it reports no phase (:392-404's comment), only the offset moving
                // ED-NEW-softscroll-1: the list glides (OD-0071): nothing moves in the report's own frame.
                Assert.Equal(before, LogicalQuery.SelfAndDescendants<ScrollViewer>(view).Single().Offset.Y);
            });
            void Frame()
            {
                harness.Main.Advance(TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60));
                harness.Main.OnMain(() => ((ManualFrameSource)FrameClock.Source.Current).Advance(TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60)));
            }
            Frame(); // the first 60 Hz frame starts the glide from rest (ED-NEW-softscroll-2: 4.5 of the notch's 100 points)
            harness.Main.OnMain(() => Assert.InRange(LogicalQuery.SelfAndDescendants<ScrollViewer>(view).Single().Offset.Y, before + 4.4, before + 4.5));
            harness.Main.OnMain(() => Assert.False(ListStack(view).IsHitTestVisible));
            for (var frame = 0; frame < 32; frame++) Frame(); // 0.55 s: the glide has come to rest (at 0.47 s), its last movement under 150 ms ago
            harness.Main.OnMain(() => Assert.Equal(before + 100, LogicalQuery.SelfAndDescendants<ScrollViewer>(view).Single().Offset.Y, 3));
            harness.Main.OnMain(() => Assert.False(ListStack(view).IsHitTestVisible));
            harness.Main.AdvanceMilliseconds(160);
            harness.Main.OnMain(() => Assert.True(ListStack(view).IsHitTestVisible));
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    });

    // ======== The gap between projects, mounted (UI-SIDEBAR-006) ========

    [Fact]
    [Trait("Req", "UI-SIDEBAR-006")] // SidebarView.swift:517-519 a gap item `gap-<projectUUID>` of 10 pt before every project but the first; :321-322 `.opacity` (the gaps "ride the list's own motion"); :113-118
    // The list reacts to a project added or removed (the earlier note's doubt, STOP #11, does not hold): the gap goes with its
    // project's row, on the plain opacity transition and not the row's quick 0.06 s removal fade, and it comes at once with
    // its row, with none of the 0.12 s handover delay the other rows wait out (:1072). REVIEW (2026-10-04): the source does
    // not cut it: a project added or removed changes `model.projects.map(\.isExpanded)` (:118, the array's length), so the
    // pass runs in Chrome.panelSlide as the port's ListMotion has it; no OD-0076 deviation is involved.
    public void A_gap_leaves_with_its_removed_project_and_comes_with_an_added_one_on_the_plain_opacity_transition() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var a = NewProject(harness, "A"); var b = NewProject(harness, "B"); var c = NewProject(harness, "C");
        var model = Model(harness, [a, b, c], []);
        SidebarView view = null!;
        void Tick(int ms) { harness.Main.AdvanceMilliseconds(ms); harness.Main.OnMain(() => { manual.Advance(TimeSpan.FromMilliseconds(ms)); WindowOf(view).UpdateLayout(); Dispatcher.UIThread.RunJobs(); }); }
        List<Control> Gaps() => ListPanel(view).Children.Where(ch => ch is Border { Height: 10 }).ToList();
        try
        {
            harness.Main.OnMain(() => view = Attached(model));
            Tick(1600);
            harness.Main.OnMain(() => Assert.Equal(2, Gaps().Count)); // before B and before C
            harness.Main.OnMain(() => model.RemoveProject(c));
            Tick(30);
            harness.Main.OnMain(() =>
            {
                var leaving = Gaps().OrderBy(g => g.Opacity).First();
                Assert.InRange(leaving.Opacity, 0.5, 0.999); // fading, and not on the row's 0.06 s fade (that row is at about a third)
                var row = ListPanel(view).Children.OfType<Control>().Last();
                Assert.True(row.Opacity < leaving.Opacity);
            });
            Tick(1600);
            harness.Main.OnMain(() => Assert.Single(Gaps())); // gone once faded
            var folder = harness.NewFolder("D");
            harness.Main.OnMain(() => model.AddProject(SwiftURL.FileURLWithPath("/" + folder.Replace('\\', '/')))); // the POSIX-shaped path FileURLWithPath is narrowed to (a Windows "C:\..." traps)
            Tick(30);
            harness.Main.OnMain(() =>
            {
                Assert.Equal(2, Gaps().Count);
                var entering = Gaps().OrderBy(g => g.Opacity).First();
                Assert.InRange(entering.Opacity, 0.001, 0.999); // already coming: no 0.12 s delay
            });
            Tick(1600);
            harness.Main.OnMain(() => Assert.All(Gaps(), g => Assert.Equal(1d, g.Opacity)));
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    });

    // ======== The pair submenu as the Windows menu shows it (UI-SIDEBAR-015, -016) ========

    [Fact]
    [Trait("Req", "UI-SIDEBAR-016")] // SidebarView.swift:1250-1266 `Label(_, systemImage: "checkmark")` for the current choice, plain `Text(title)` for every other item
    [Trait("Req", "UI-SIDEBAR-015")] // SidebarView.swift:1149-1161: a divider, then the submenu "Start new chats on" (a Label with its symbol), only with pairs
    // The context menu is the system menu here (NativeContextMenus): it is built from the MenuItem rows, and a Win32 menu
    // draws text, separators, submenus and check marks, not a Label's image. So the current choice is the row carrying the
    // check mark (ENGINEERING DECISION: the glyph of the label swap is, in a Win32 menu, its check mark), and every other row is
    // plain text; the divider, the submenu and its rows reach the system menu as the source orders them. Opening the
    // native submenu is the system's own and is NATIVE-ONLY.
    public void The_pair_submenu_reaches_the_system_menu_with_its_divider_and_a_check_mark_on_the_current_choice_only() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var project = NewProject(harness, "Alpha");
        var model = Model(harness, [project], []);
        var review = new HydraPair(ProviderKind.Claude) with { Name = "Review pair" };
        var build = new HydraPair(ProviderKind.Claude) with { Name = "Build pair" };
        Control row = null!;
        harness.Main.OnMain(() => { row = ShownProjectRow(model, project, 0); model.Settings.AddHydraPair(review); model.Settings.AddHydraPair(build); });
        harness.Main.OnMain(() => model.SetHydraPair(build.Id, project.Id));
        harness.Main.OnMain(() =>
        {
            PointerRightClick(row);
            var entries = NativeContextMenus.Entries(ContextMenuOf(row).Items);
            Assert.True(entries[^2].IsSeparator); // :1155
            var submenu = entries[^1];
            Assert.Equal("Start new chats on", submenu.Title); // :1159
            Assert.Equal(["The chat's last pair", "", "Review pair", "Build pair"], submenu.Children.Select(c => c.Title));
            Assert.True(submenu.Children[1].IsSeparator);
            Assert.Equal([false, false, false, true], submenu.Children.Select(c => c.IsChecked)); // only the chosen pair
            WindowOf(row).Close();
        });
    });

    // ======== The head card's two heights (UI-SIDEBAR-036) ========

    private static (Control Card, AvaloniaWindow Window, List<ChatThread> Heads, ChatThread Parent, ManualFrameSource Manual, Func<Grid> Grid) MountedCard(
        AppModelHarness harness, AppModel model, ChatThread parent, List<ChatThread> heads, ManualFrameSource manual)
    {
        Control card = null!; AvaloniaWindow window = null!;
        harness.Main.OnMain(() =>
        {
            var requests = Activator.CreateInstance(Private("SidebarMenuRequests"))!;
            card = (Control)Activator.CreateInstance(Private("HeadCardsRow"), parent, heads, requests, (Action)(() => { }))!;
            card.VerticalAlignment = VerticalAlignment.Top;
            window = WindowOf(Hosted(model, card));
            window.UpdateLayout();
        });
        harness.Main.OnMain(() => window.UpdateLayout());
        return (card, window, heads, parent, manual, () => Assert.IsType<Grid>(Get(card, "Child")));
    }

    [Fact]
    [Trait("Req", "UI-SIDEBAR-036")] // SidebarView.swift:1535 `.animation(.snappy(duration: 0.18), value: cardRows)`, :1515-1516 `withAnimation(.snappy(duration: 0.18)) { showsAllHeads.toggle() }`: the row count changes animate on the snappy spring, the connector's height with the card's
    public void The_card_and_the_connector_travel_between_their_two_heights_on_the_snappy_curve() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var heads = Enumerable.Range(0, 15).Select(i => HeadHelper(project, parent.Id, i, i + 1)).ToList();
        var model = Model(harness, [project], heads.Prepend(parent).ToList());
        var perRow = HeadCardsRow.PerRow(200);
        long RowsFor(long n) => (n + perRow - 1) / perRow;
        var before = ThreadRowMetrics.HeadCardHeight(RowsFor(12) + 1);
        var after = ThreadRowMetrics.HeadCardHeight(RowsFor(15) + 1);
        Assert.NotEqual(before, after);
        var (card, window, _, _, _, grid) = MountedCard(harness, model, parent, heads, manual);
        try
        {
            harness.Main.OnMain(() => Click(LogicalQuery.SelfAndDescendants<Button>(card).Single(b => LogicalQuery.SelfAndDescendants<SwiftImage>(b).Any(i => i.SymbolName?.StartsWith("chevron", StringComparison.Ordinal) == true))));
            harness.Main.OnMain(() => { });
            var snappy = Animation.Snappy(duration: 0.18);
            double elapsed = 0;
            foreach (var step in new[] { 0.032, 0.032 })
            {
                harness.Main.AdvanceMilliseconds((int)(step * 1000));
                harness.Main.OnMain(() => manual.Advance(TimeSpan.FromSeconds(step)));
                elapsed += step;
                harness.Main.OnMain(() =>
                {
                    var expected = before + (after - before) * snappy.Progress(elapsed);
                    Assert.NotEqual(after, grid().Children[1].Height); // under way
                    Assert.Equal(expected, grid().Children[0].Height, 0.5); // the connector's column
                    Assert.Equal(expected, grid().Children[1].Height, 0.5); // the card
                });
            }
        }
        finally { harness.Main.OnMain(() => window.Close()); }
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-036")] // SidebarView.swift:1541 `.animation(Chrome.panelSlide, value: heads.map(\.id))` on the whole row, :1535 `.animation(.snappy(duration: 0.18), value: cardRows)` on the card inside it
    // A change of the head set that also changes the row count: the card has its own, nearer animation (snappy) and the
    // connector, outside it, takes the row's (panelSlide): the two heights travel on their own curves to the same end.
    public void When_the_head_set_changes_the_connector_slides_on_panelSlide_while_the_card_keeps_its_snappy() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var heads = Enumerable.Range(0, 13).Select(i => HeadHelper(project, parent.Id, i, i + 1)).ToList();
        var model = Model(harness, [project], heads.Prepend(parent).ToList());
        var perRow = HeadCardsRow.PerRow(200);
        long RowsFor(long n) => (n + perRow - 1) / perRow;
        var fewer = heads.Take(3).ToList();
        var before = ThreadRowMetrics.HeadCardHeight(RowsFor(12) + 1); // capped: 13 > 12, the chevron row
        var after = ThreadRowMetrics.HeadCardHeight(RowsFor(3)); // not capped
        Assert.NotEqual(before, after);
        var (card, window, _, _, _, grid) = MountedCard(harness, model, parent, heads, manual);
        try
        {
            harness.Main.OnMain(() => CallOn(card, "Update", fewer));
            harness.Main.OnMain(() => { });
            harness.Main.AdvanceMilliseconds(48);
            harness.Main.OnMain(() => manual.Advance(TimeSpan.FromMilliseconds(48)));
            harness.Main.OnMain(() =>
            {
                var snappy = before + (after - before) * Animation.Snappy(duration: 0.18).Progress(0.048);
                var slide = before + (after - before) * Chrome.PanelSlide.Progress(0.048);
                Assert.NotEqual(snappy, slide, 0.5); // the two curves differ here, or the test proves nothing
                Assert.Equal(slide, grid().Children[0].Height, 0.5); // the connector, outside the card's own animation
                Assert.Equal(snappy, grid().Children[1].Height, 0.5); // the card
            });
        }
        finally { harness.Main.OnMain(() => window.Close()); }
    });

    // ======== The head glyph (UI-SIDEBAR-037) ========

    [Fact]
    [Trait("Req", "UI-SIDEBAR-037")] // SidebarView.swift:1484-1486 `.scaleEffect(hoveredID == head.id ? 1.18 : 1).animation(Chrome.hover, value: ...)`, :1487-1488 the hover clears `hoveredID` inside withAnimation(Chrome.hover): it eases back the way it came
    public void A_hovered_head_glyph_eases_back_from_1_18_to_1_on_the_hover_curve() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var cursor = DragHandleView.CursorPlatform.Override(new ChatNoCursor());
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var head = HeadHelper(project, parent.Id, 0, 1);
        var model = Model(harness, [project], [parent, head]);
        Control card = null!; AvaloniaWindow window = null!;
        Button HeadButton() => (Button)LogicalQuery.SelfAndDescendants<HydraGlyph>(card).Single().Parent!.Parent!;
        ScaleTransform Scale() => (ScaleTransform)((Panel)LogicalQuery.SelfAndDescendants<HydraGlyph>(card).Single().Parent!).RenderTransform!;
        void Tick(int ms) { harness.Main.AdvanceMilliseconds(ms); harness.Main.OnMain(() => manual.Advance(TimeSpan.FromMilliseconds(ms))); }
        try
        {
            harness.Main.OnMain(() =>
            {
                var requests = Activator.CreateInstance(Private("SidebarMenuRequests"))!;
                card = (Control)Activator.CreateInstance(Private("HeadCardsRow"), parent, new[] { head }, requests, (Action)(() => { }))!;
                card.VerticalAlignment = VerticalAlignment.Top;
                window = WindowOf(Hosted(model, card));
                window.UpdateLayout();
                window.MouseMove(HeadButton().TranslatePoint(new Point(7, 7), window)!.Value);
            });
            Tick(300);
            harness.Main.OnMain(() => { Assert.Equal(1.18, Scale().ScaleX, 3); window.MouseMove(new Point(window.Width - 1, window.Height - 1)); });
            Tick(40);
            harness.Main.OnMain(() => Assert.Equal(1.18 - 0.18 * Animation.EaseOut(0.1).Progress(0.04), Scale().ScaleX, 3)); // part way back, not cut
            Tick(200);
            harness.Main.OnMain(() => Assert.Equal(1d, Scale().ScaleX, 3));
        }
        finally { harness.Main.OnMain(() => window.Close()); }
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-037")] // SidebarView.swift:1296-1301 headBadgeOverhang = 3, :1500-1503 `.padding(.trailing, headBadgeOverhang)` on each row of glyphs: "the outcome badge rides about 3 pt past the frame on the trailing side"
    public void Each_row_of_head_glyphs_trails_the_overhang_of_the_last_marks_badge_with_3_points() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0);
        var heads = Enumerable.Range(0, 3).Select(i => HeadHelper(project, parent.Id, i, i + 1)).ToList();
        var model = Model(harness, [project], heads.Prepend(parent).ToList());
        harness.Main.OnMain(() =>
        {
            var requests = Activator.CreateInstance(Private("SidebarMenuRequests"))!;
            var card = (Control)Activator.CreateInstance(Private("HeadCardsRow"), parent, heads, requests, (Action)(() => { }))!;
            WindowOf(Hosted(model, card)).UpdateLayout();
            try
            {
                Assert.Equal(3d, ThreadRowMetrics.HeadBadgeOverhang);
                var row = LogicalQuery.SelfAndDescendants<StackPanel>(card).First(r => r.Orientation == Avalonia.Layout.Orientation.Horizontal && r.Children.OfType<Button>().Any());
                Assert.Equal(3d, row.Margin.Right);
                Assert.Equal(14d, LogicalQuery.SelfAndDescendants<HydraGlyph>(row).First().Size); // headGlyphSize
            }
            finally { Close(card); }
        });
    });

    // ======== The dragged row's lift, on its curve (UI-SIDEBAR-060) ========

    [Fact]
    [Trait("Req", "UI-SIDEBAR-060")] // SidebarView.swift:1838-1846 `.scaleEffect(isDragged ? 1.02 : 1)`, `.animation(.spring(response: 0.25, dampingFraction: 0.8), value: isDragged)`: "springs in and out on response 0.25 damping 0.8"
    public void The_lift_scales_to_1_02_and_back_along_the_spring_of_response_0_25_and_damping_0_8() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        var grabbed = Guid.NewGuid();
        SidebarView view = null!; Control host = null!; AvaloniaWindow window = null!;
        void Tick(int ms) { harness.Main.AdvanceMilliseconds(ms); harness.Main.OnMain(() => manual.Advance(TimeSpan.FromMilliseconds(ms))); }
        double ScaleOf() => host.RenderTransform is ScaleTransform scale ? scale.ScaleX : 1;
        var spring = Animation.Spring(response: 0.25, dampingFraction: 0.8);
        try
        {
            harness.Main.OnMain(() =>
            {
                view = Detached(model);
                host = (Control)CallStatic(Private("SidebarThreadRow"), "ApplyDraggedLift", new Border { Height = 20 }, (Func<bool>)(() => Drag(view).Id == grabbed))!;
                window = new AvaloniaWindow { Content = host, Width = 100, Height = 100 };
                window.Show();
                Call(view, "DragChanged", grabbed, 5d);
            });
            Tick(60);
            harness.Main.OnMain(() => Assert.Equal(1 + 0.02 * spring.Progress(0.06), ScaleOf(), 4)); // on the way in
            Tick(1000);
            harness.Main.OnMain(() => { Assert.Equal(1.02, ScaleOf(), 4); Call(view, "DragEnded"); });
            Tick(60);
            harness.Main.OnMain(() => Assert.Equal(1.02 - 0.02 * spring.Progress(0.06), ScaleOf(), 4)); // and on the way out
            Tick(1000);
            harness.Main.OnMain(() => Assert.Equal(1d, ScaleOf(), 4));
        }
        finally { harness.Main.OnMain(() => window.Close()); }
    });
}
