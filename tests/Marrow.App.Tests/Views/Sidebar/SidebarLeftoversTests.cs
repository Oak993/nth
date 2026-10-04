using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.App.Views.Sidebar;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Views.Sidebar;

// Source: DroppyCode/App/Views/Sidebar/SidebarView.swift (B3 52bc792). The records the SidebarView parity unit left
// open, closed or narrowed by the leftovers pass of 2026-10-04 (branch cloud/b3-sidebar-leftovers). Two rulings made
// them provable: OD-0098 (where no reference video frame shows a state, the pinned Swift source is the proof, so each
// test below reads its expected values from the Swift lines it cites) and OD-0076 (every change is transitioned; where
// the Mac snaps the port eases in the Mac's motion language, hover about 0.1 s). Continuous corners are ED-275's.
// Same class as SidebarViewTests (partial) so these tests share its private reflection and fixture helpers.
public sealed partial class SidebarViewTests
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
        var checker = harness.Main.OnMain(() => new UpdateChecker(ReleaseFeed.Running("1.0.0"), [], UpdateChecker.DefaultFeed));
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
}
