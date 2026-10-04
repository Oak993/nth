using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Sidebar;
using Marrow.Core.Models;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Xunit;
using static Marrow.App.Tests.Views.Sidebar.SidebarViewTests;

namespace Marrow.App.Tests.Views.Sidebar;

// REVIEW of the H3 sidebar leftovers (cloud/b3-sidebar-leftovers 0aaacaa22), on Windows, 2026-10-04.
// Each test states what the pinned Swift does. The first fails on the merged branch (the port cuts, OD-0076); the others
// pass and close what two surviving mutants showed unproved (the face's selection key, the capsule focus ring) and the
// mid-swap clause of the glyph crossfade.
[Collection("Headless")]
public sealed class SidebarLeftoversReviewTests(HeadlessSession headless)
{
    // UI-SIDEBAR-017/021/022 (the claim of 2df3d6211: "the title's room eases with the hover"). SidebarView.swift:1739-1743
    // keeps ONE ThreadRowFace under the row's identity and :2068 animates its trailing padding in the hover's transaction;
    // a selection change that lands while the padding is on its way (:1721 isSelected re-renders the same face) leaves
    // that animation running. The port keys its kept face on (thread, selected, mergeStage) and builds a new face at the
    // TARGET clearance when any of them changes, so the title's room jumps to its end: a cut.
    [Fact]
    [Trait("Req", "UI-SIDEBAR-017")]
    public void REVIEW_a_selection_change_mid_hover_does_not_cut_the_title_room_to_its_end() => headless.OnUiThread(() =>
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
        double Right() => ((Border)LogicalQuery.SelfAndDescendants<ThreadRowFace>(row).Single().Child!).Padding.Right;
        try
        {
            harness.Main.OnMain(() =>
            {
                row = LiveThreadRow(thread, "Alpha");
                var window = WindowOf(Hosted(model, row));
                window.UpdateLayout();
                window.MouseMove(row.TranslatePoint(new Point(10, 10), window)!.Value);
            });
            Tick(16);
            double before = 0;
            harness.Main.OnMain(() => { before = Right(); Assert.InRange(before, 8 + 18 + 0.01, 8 + 52 - 1); });
            harness.Main.OnMain(() => model.SelectedThreadID = thread.Id); // a click lands while the room is still opening
            harness.Main.OnMain(() => Assert.InRange(Right(), before, 8 + 52 - 1)); // still on its way, not cut to 60
            Tick(300);
            harness.Main.OnMain(() => Assert.Equal(8d + 52, Right()));
        }
        finally { harness.Main.OnMain(() => WindowOf(row).Close()); }
    });

    // PR-SIDEBAR-127 (the claim of 8b9cb13a2: "each fades on its own scalar, so a swap that arrives mid-fade leaves
    // nothing cut"). A mark that is still fading IN when the next swap comes must fade OUT from where it is: its opacity
    // never rises again and it leaves only once clear.
    [Fact]
    [Trait("Req", "PR-SIDEBAR-127")]
    public void REVIEW_a_swap_that_arrives_mid_crossfade_fades_the_half_shown_mark_out_from_where_it_stands() => headless.OnUiThread(() =>
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
            SwiftImage? Star() => LogicalQuery.SelfAndDescendants<SwiftImage>(row).SingleOrDefault(i => i.SymbolName == "star.fill");
            // the mark's own opacity and its wrapper's, up to the badge's 20 pt box
            double Shown(Visual mark) => mark.GetSelfAndVisualAncestors().OfType<Visual>().TakeWhile(v => v is not SidebarIconBadge && !(v is Panel { Width: 20 })).Aggregate(1d, (o, v) => o * v.Opacity);
            harness.Main.OnMain(() => model.UpdateProject(project.Id, p => p with { Icon = new ProjectIcon.Symbol("star.fill") }));
            Tick(32);
            double last = 0;
            harness.Main.OnMain(() => { last = Shown(Star()!); Assert.InRange(last, 0.05, 0.95); });
            harness.Main.OnMain(() => model.UpdateProject(project.Id, p => p with { Icon = new ProjectIcon.Symbol("bolt.fill") }));
            for (var step = 0; step < 12; step++)
            {
                Tick(16);
                harness.Main.OnMain(() =>
                {
                    if (Star() is not { } star) { last = 0; return; }
                    var now = Shown(star);
                    Assert.True(now <= last + 1e-9, $"the leaving mark rose again ({last} then {now})");
                    Assert.True(last - now < 0.5, $"the leaving mark jumped ({last} then {now})");
                    last = now;
                });
            }
            harness.Main.OnMain(() => Assert.Null(Star()));
        }
        finally { harness.Main.OnMain(() => Close(row)); }
    });

    // PR-SIDEBAR-065 through the kept face of 2df3d6211: SidebarView.swift:2045-2051 the title is .medium
    // once the row is selected (:1721 isSelected re-renders the face). The kept face is keyed on the selection; nothing
    // proved that key (a mutant dropping `selected` from it survived the whole Sidebar namespace).
    [Fact]
    [Trait("Req", "PR-SIDEBAR-065")]
    public void REVIEW_a_live_rows_title_turns_medium_when_the_row_is_selected() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Pick me", 0);
        var model = Model(harness, [project], [thread]);
        using var shared = RowGlideAnimator.Shared.Override(new RowGlideAnimator(new FakeGeniePlatform()));
        Control row = null!;
        TextBlock Title() => LogicalQuery.SelfAndDescendants<TextBlock>(row).First(t => t.Text == "Pick me");
        try
        {
            harness.Main.OnMain(() =>
            {
                row = LiveThreadRow(thread);
                WindowOf(Hosted(model, row)).UpdateLayout();
                Assert.Equal(FontWeight.Normal, Title().FontWeight);
            });
            harness.Main.OnMain(() => model.SelectedThreadID = thread.Id);
            harness.Main.OnMain(() =>
            {
                Assert.Equal(FontWeight.Medium, Title().FontWeight); // :2046 isSelected -> .medium
            });
        }
        finally { harness.Main.OnMain(() => WindowOf(row).Close()); }
    });

    // UI-SIDEBAR-030 through 7ab6d128e: the capsule the source's presentedChrome gives a bordered button is the button's
    // shape for its focus ring too (the commit says "for the bezel, the press wash and the focus ring alike"); only the
    // bezel and the wash were asserted (a mutant leaving the ring at the kit's radius survived).
    [Fact]
    [Trait("Req", "UI-SIDEBAR-030")]
    public void REVIEW_under_presentedChrome_the_focus_ring_follows_the_capsule() => headless.OnUiThread(() =>
    {
        var content = new StackPanel { Children = { SystemButtons.Bordered("Cancel", () => { }), SystemButtons.BorderedProminent("Rename", () => { }) } };
        Popovers.PresentedChrome(content);
        var window = new Avalonia.Controls.Window { Content = content, Width = 300, Height = 120 };
        window.Show();
        window.UpdateLayout();
        try
        {
            foreach (var button in LogicalQuery.SelfAndDescendants<Button>(content))
            {
                var bezel = button.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_Bezel");
                var ring = button.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_Focus");
                Assert.Equal(new CornerRadius(bezel.Bounds.Height / 2 + SystemButtons.FocusBand), ring.CornerRadius);
            }
        }
        finally { window.Close(); }
    });
}
