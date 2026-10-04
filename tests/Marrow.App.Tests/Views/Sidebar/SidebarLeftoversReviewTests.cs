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

    // Round 2 (c62d754a9, ONE face updated in place). The lead's question (a): does the project name hold its place while the
    // folder and branch marks crossfade? SidebarView.swift:2054-2058 `HStack(spacing: 4) { Image(systemName: ...); Text(...) }`
    // puts the name 4 pt after the mark's frame. In the port both marks are the same 12 pt square (the Lucide marks of
    // OD-0010), held in one Panel while they crossfade, so the name has nothing to move for: it must not move in any frame,
    // in either direction. (Measured headless: the name stays at (24, 26) in the window on every frame.)
    [Theory]
    [Trait("Req", "PR-SIDEBAR-070")]
    [Trait("Req", "UI-SIDEBAR-019")]
    [InlineData(false)] // folder -> branch
    [InlineData(true)] // branch -> folder
    public void The_project_name_holds_its_place_in_every_frame_while_the_folder_and_branch_marks_crossfade(bool back) => headless.OnUiThread(() =>
    {
        using var rig = new ThreadFaceRig();
        Point Name() => rig.Label().TranslatePoint(default, rig.Window)!.Value;
        if (back)
        {
            rig.Harness.Main.OnMain(() => rig.Model.UpdateThread(rig.Thread.Id, t => t with { WorktreePath = "C:\\wt" })); // at once
            rig.Tick(16);
        }
        var start = rig.Harness.Main.OnMain(() => { rig.Window.UpdateLayout(); return Name(); });
        rig.Harness.Main.OnMain(() => Transaction.WithAnimation(Chrome.Hover, () =>
            rig.Model.UpdateThread(rig.Thread.Id, t => t with { WorktreePath = back ? null : "C:\\wt" })));
        var sawBoth = false;
        for (var step = 0; step < 14; step++)
        {
            rig.Tick(16);
            rig.Harness.Main.OnMain(() =>
            {
                rig.Window.UpdateLayout();
                sawBoth |= LogicalQuery.SelfAndDescendants<SwiftImage>(rig.Face()).Count() == 2;
                var now = Name();
                Assert.True(Math.Abs(now.X - start.X) < 0.01 && Math.Abs(now.Y - start.Y) < 0.01, $"the name moved from {start} to {now} on frame {step}");
            });
        }
        Assert.True(sawBoth, "the marks crossfaded (both drawn on some frame)");
    });

    // The lead's question (b), answered headless: the face eases the title's weight 400 -> 500 as a NUMBER (ApplyWeight
    // rounds it to an int FontWeight), but the embedded Inter is static faces, so nothing in between exists to draw.
    // Avalonia's matching of an in-between weight against fonts:Inter#Inter (FontManager.TryGetGlyphTypeface, the face
    // the TextBlock is drawn with) does not take the nearer face: on this ease it gave 413 -> Inter Bold (700) and
    // 425..498 -> Inter SemiBold (600); a standalone probe gave Bold for 410/440/451/460/490 and Medium only at 450, and
    // its 192-dpi renders of those weights matched neither the 400 nor the 500 render. So for most of the 0.1 s the title
    // is drawn one or two weights PAST the source's .medium (:2041), then drops back to Medium. Whatever the Mac draws
    // between the two (SF is a variable font; the pinned source cannot say), it never goes past .medium. FAILS on c62d754a9.
    [Fact]
    [Trait("Req", "PR-SIDEBAR-065")]
    public void REVIEW_the_title_easing_from_regular_to_medium_is_never_drawn_in_a_face_outside_regular_and_medium() => headless.OnUiThread(() =>
    {
        using var rig = new ThreadFaceRig();
        var drawn = new List<string>();
        void Sample() => rig.Harness.Main.OnMain(() =>
        {
            var title = rig.Title();
            Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(title.FontFamily, title.FontStyle, title.FontWeight), out var face));
            drawn.Add((int)title.FontWeight + "->" + (int)face!.Weight);
        });
        Sample(); // 400 -> Inter Regular
        rig.Start(FaceProperty.Weight); // :2041 an unread thread's title turns medium, in Chrome.hover
        for (var step = 0; step < 16; step++) { rig.Tick(8); Sample(); }
        Assert.Equal("400->400", drawn[0]);
        Assert.Equal("500->500", drawn[^1]); // at rest on Inter Medium: the reads are the right ones
        Assert.All(drawn, entry => Assert.True(entry.EndsWith("->400", StringComparison.Ordinal) || entry.EndsWith("->500", StringComparison.Ordinal), string.Join(", ", drawn)));
    });
}
