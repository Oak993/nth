using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
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

// ONE ThreadRowFace per row, updated in place (lead ruling 2026-10-04, from SidebarView.swift:1739-1743; ED-291 (1)).
// SwiftUI keeps the face under the row's identity and re-reads its inputs, so a thread, selection or merge-stage change that
// lands while one of the face's animated properties is on its way (the trailing padding :2068, the title's weight :2041 and
// opacity :2042, the project line's glyph :2055) leaves it on its way: the value drawn right after the change is the value
// drawn right before it, it keeps easing to its end, and the face under the row is the same object. Each test starts one
// property moving in the transaction SwiftUI would use, changes one input with NO transaction, and reads the property
// before, right after and along the rest of the way. UI-SIDEBAR-020 (the settle pop) is not touched here; OD-0105 settles it by the Swift source (SidebarSettleBounceTests).
[Collection("Headless")]
public sealed class SidebarFaceInPlaceTests(HeadlessSession headless)
{
    // UI-SIDEBAR-017/021/022 (the row's face, the title's room and its accessories): a property on its way is not cut by the change
    // of another input. One test per property so each carries the record that states that property.
    [Theory]
    [Trait("Req", "UI-SIDEBAR-017")]
    [Trait("Req", "UI-SIDEBAR-021")]
    [Trait("Req", "UI-SIDEBAR-022")]
    [InlineData(FaceInput.Thread)]
    [InlineData(FaceInput.Selection)]
    [InlineData(FaceInput.MergeStage)]
    public void A_change_of_an_input_mid_animation_leaves_the_trailing_padding_on_its_way_in_the_same_face(FaceInput input) => headless.OnUiThread(() => AssertStaysOnItsWay(FaceProperty.Padding, input));

    // PR-SIDEBAR-065 :2041 the title's weight (medium when selected or unread).
    [Theory]
    [Trait("Req", "PR-SIDEBAR-065")]
    [InlineData(FaceInput.Thread)]
    [InlineData(FaceInput.Selection)]
    [InlineData(FaceInput.MergeStage)]
    public void A_change_of_an_input_mid_animation_leaves_the_titles_weight_on_its_way_in_the_same_face(FaceInput input) => headless.OnUiThread(() => AssertStaysOnItsWay(FaceProperty.Weight, input));

    // PR-SIDEBAR-065 :2042 the title's opacity (1 selected, 0.92 otherwise). The selection is this property's own input: its mid-animation
    // change is the next test.
    [Theory]
    [Trait("Req", "PR-SIDEBAR-065")]
    [InlineData(FaceInput.Thread)]
    [InlineData(FaceInput.MergeStage)]
    public void A_change_of_an_input_mid_animation_leaves_the_titles_opacity_on_its_way_in_the_same_face(FaceInput input) => headless.OnUiThread(() => AssertStaysOnItsWay(FaceProperty.Opacity, input));

    // PR-SIDEBAR-070 / UI-SIDEBAR-019 :2055 the project line's mark (folder, or the branch for a worktree).
    [Theory]
    [Trait("Req", "PR-SIDEBAR-070")]
    [Trait("Req", "UI-SIDEBAR-019")]
    [InlineData(FaceInput.Thread)]
    [InlineData(FaceInput.Selection)]
    [InlineData(FaceInput.MergeStage)]
    public void A_change_of_an_input_mid_animation_leaves_the_project_lines_mark_on_its_way_in_the_same_face(FaceInput input) => headless.OnUiThread(() => AssertStaysOnItsWay(FaceProperty.Glyph, input));

    private static void AssertStaysOnItsWay(FaceProperty property, FaceInput input)
    {
        using var rig = new ThreadFaceRig();
        rig.Start(property);
        rig.Tick(16);
        ThreadRowFace face = null!;
        double before = 0, end = 0;
        rig.Harness.Main.OnMain(() =>
        {
            face = rig.Face();
            before = rig.Read(property);
            end = rig.End(property);
            Assert.True(before != end && Math.Abs(before - end) > 0.01, $"{property} should be on its way ({before} of {end})");
        });
        rig.Change(input); // no transaction: a click, an edit of the record, a merge moving to its next stage
        rig.Harness.Main.OnMain(() =>
        {
            rig.AssertTold(input);
            Assert.Same(face, rig.Face()); // one face under the row, never rebuilt for an input
            Assert.Equal(before, rig.Read(property)); // not cut: the value drawn is the value that was drawn
        });
        rig.Tick(16);
        rig.Harness.Main.OnMain(() =>
        {
            var now = rig.Read(property);
            Assert.NotEqual(end, now); // still on its way
            Assert.True(Math.Abs(end - now) < Math.Abs(end - before), $"{property} should have moved towards {end} ({before} then {now})");
        });
        rig.Tick(2000);
        rig.Harness.Main.OnMain(() => Assert.Equal(end, rig.Read(property))); // and it arrives
    }

    // UI-SIDEBAR-017 / PR-SIDEBAR-065: the selection IS the input of the weight and the opacity; when it is taken back inside an
    // animation while the title is on its way up, the title turns round from where it stands (a retarget, not a restart).
    [Theory]
    [Trait("Req", "UI-SIDEBAR-017")]
    [Trait("Req", "PR-SIDEBAR-065")]
    [InlineData(FaceProperty.Weight)]
    [InlineData(FaceProperty.Opacity)]
    public void A_selection_taken_back_mid_animation_turns_the_title_round_from_where_it_stands(FaceProperty property) => headless.OnUiThread(() =>
    {
        using var rig = new ThreadFaceRig();
        rig.Harness.Main.OnMain(() => Transaction.WithAnimation(Chrome.Hover, () => rig.Model.SelectedThreadID = rig.Thread.Id));
        rig.Tick(16);
        double before = 0, start = 0, end = 0;
        rig.Harness.Main.OnMain(() =>
        {
            before = rig.Read(property);
            end = rig.End(property);
            start = property == FaceProperty.Weight ? (int)FontWeight.Normal : Math.Round(rig.FullOpacityByte() * 0.92);
            Assert.True(before > start + 0.5 && before < end - 0.5, $"{property} should be on its way up ({start} {before} {end})");
        });
        var face = rig.Face();
        rig.Harness.Main.OnMain(() => Transaction.WithAnimation(Chrome.Hover, () => rig.Model.SelectedThreadID = null));
        rig.Harness.Main.OnMain(() =>
        {
            Assert.Same(face, rig.Face());
            Assert.Equal(before, rig.Read(property)); // neither snapped back to the start nor on to the end
        });
        rig.Tick(16);
        rig.Harness.Main.OnMain(() => Assert.InRange(rig.Read(property), start, before - 0.5)); // now on its way back down
        rig.Tick(2000);
        rig.Harness.Main.OnMain(() => Assert.InRange(rig.Read(property), start - 1.5, start + 1.5)); // an alpha byte rounds
    });

    // The thread itself changes shape (an open thread settles, :2020 isSettled): the face's content is rebuilt INSIDE the same
    // face and the animated values go on from where they stand.
    [Theory]
    [Trait("Req", "UI-SIDEBAR-017")]
    [Trait("Req", "PR-SIDEBAR-065")]
    [InlineData(FaceProperty.Weight)]
    [InlineData(FaceProperty.Opacity)]
    public void A_thread_that_settles_mid_animation_rebuilds_the_content_in_the_same_face_and_carries_the_titles_look_on(FaceProperty property) => headless.OnUiThread(() => AssertSettleCarries(property));

    [Fact]
    [Trait("Req", "UI-SIDEBAR-017")]
    public void A_thread_that_settles_mid_animation_rebuilds_the_content_in_the_same_face_and_carries_the_padding_on() => headless.OnUiThread(() => AssertSettleCarries(FaceProperty.Padding));

    private static void AssertSettleCarries(FaceProperty property)
    {
        using var rig = new ThreadFaceRig();
        rig.Start(property);
        rig.Tick(16);
        ThreadRowFace face = null!;
        double before = 0;
        rig.Harness.Main.OnMain(() => { face = rig.Face(); before = rig.Read(property); });
        rig.Harness.Main.OnMain(() => Transaction.WithAnimation(Chrome.PanelSlide, () => rig.Model.UpdateThread(rig.Thread.Id, t => t with { IsSettled = true })));
        rig.Harness.Main.OnMain(() =>
        {
            Assert.Same(face, rig.Face());
            Assert.Equal(12d, rig.Title().FontSize); // the settled layout is drawn (:2048)
            Assert.Equal(before, rig.Read(property)); // the new content starts where the old one stood
        });
        rig.Tick(16);
        rig.Harness.Main.OnMain(() => Assert.NotEqual(before, rig.Read(property))); // and moves on to the settled values
    }

    // The glyph: a crossfade in the transaction, a cut without one; a second swap while the first is half through fades the
    // half-shown mark out from where it is.
    [Fact]
    [Trait("Req", "PR-SIDEBAR-070")]
    public void A_worktree_change_crossfades_the_project_lines_mark_and_a_second_swap_mid_fade_fades_the_half_shown_one_from_where_it_stands() => headless.OnUiThread(() =>
    {
        using var rig = new ThreadFaceRig();
        IEnumerable<SwiftImage> Marks() => LogicalQuery.SelfAndDescendants<SwiftImage>(rig.Face());
        rig.Start(FaceProperty.Glyph);
        rig.Tick(16);
        double halfShown = 0;
        rig.Harness.Main.OnMain(() =>
        {
            Assert.Equal("arrow.triangle.branch,folder", string.Join(",", Marks().Select(i => i.SymbolName).Order())); // both drawn while it fades
            halfShown = Marks().Single(i => i.SymbolName == "arrow.triangle.branch").Opacity;
            Assert.InRange(halfShown, 0.05, 0.95);
            Assert.InRange(Marks().Single(i => i.SymbolName == "folder").Opacity, 0.05, 0.95);
        });
        rig.Harness.Main.OnMain(() => Transaction.WithAnimation(Chrome.Hover, () => rig.Model.UpdateThread(rig.Thread.Id, t => t with { WorktreePath = null })));
        rig.Harness.Main.OnMain(() =>
        {
            // the branch fades out from halfShown; the folder that was leaving is replaced by a new one that rises from 0
            Assert.Equal(halfShown, Marks().First(i => i.SymbolName == "arrow.triangle.branch").Opacity);
        });
        rig.Tick(16);
        rig.Harness.Main.OnMain(() => Assert.True(Marks().First(i => i.SymbolName == "arrow.triangle.branch").Opacity < halfShown));
        rig.Tick(2000);
        rig.Harness.Main.OnMain(() =>
        {
            var only = Assert.Single(Marks());
            Assert.Equal("folder", only.SymbolName);
            Assert.Equal(1d, only.Opacity);
        });
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-070")]
    public void A_worktree_change_in_no_transaction_replaces_the_mark_at_once() => headless.OnUiThread(() =>
    {
        using var rig = new ThreadFaceRig();
        rig.Harness.Main.OnMain(() => rig.Model.UpdateThread(rig.Thread.Id, t => t with { WorktreePath = "C:\\wt" }));
        rig.Harness.Main.OnMain(() =>
        {
            var only = Assert.Single(LogicalQuery.SelfAndDescendants<SwiftImage>(rig.Face()));
            Assert.Equal("arrow.triangle.branch", only.SymbolName);
            Assert.Equal(1d, only.Opacity);
        });
    });

    // The leading badge reads the thread it was built with (:2152-2166); the face builds it again exactly when what it
    // reads changes, in the same face, and keeps it while only the title (or anything else the badge does not read) changes.
    [Fact]
    [Trait("Req", "PR-SIDEBAR-068")]
    public void The_badge_of_a_compact_row_is_built_again_only_when_what_it_shows_changes_and_inside_the_same_face() => headless.OnUiThread(() =>
    {
        using var rig = new ThreadFaceRig(projectName: null);
        ThreadRowFace face = null!;
        Control badge = null!;
        Control Leading() => LogicalQuery.SelfAndDescendants<ThreadBadge>(rig.Face()).Single();
        rig.Harness.Main.OnMain(() => { face = rig.Face(); badge = Leading(); Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<SwiftImage>(badge), i => i.SymbolName == "pin.fill"); });
        rig.Harness.Main.OnMain(() => rig.Model.UpdateThread(rig.Thread.Id, t => t with { Title = "Renamed" }));
        rig.Harness.Main.OnMain(() =>
        {
            Assert.Same(face, rig.Face());
            Assert.Same(badge, Leading()); // nothing the badge reads changed: the same badge
            Assert.Equal("Renamed", LogicalQuery.SelfAndDescendants<TextBlock>(rig.Face()).First().Text);
        });
        rig.Harness.Main.OnMain(() => rig.Model.UpdateThread(rig.Thread.Id, t => t with { IsPinned = true }));
        rig.Harness.Main.OnMain(() =>
        {
            Assert.Same(face, rig.Face());
            Assert.NotSame(badge, Leading()); // pinned: the pin shows (:2159-2161)
            Assert.Contains(LogicalQuery.SelfAndDescendants<SwiftImage>(Leading()), i => i.SymbolName == "pin.fill");
        });
    });

    // PR-SIDEBAR-065 :2041 (lead ruling 2026-10-04: in-between weights resolve to heavier Inter faces, measured, so weight changes
    // crossfade Regular and Medium). The faces drawn in every frame of the title turning medium: only Regular and Medium copies of
    // the text, a lone Regular at the start, both stacked (Regular under Medium) in between, a lone Medium at rest.
    [Fact]
    [Trait("Req", "PR-SIDEBAR-065")]
    public void The_title_turning_medium_draws_only_a_regular_and_a_medium_copy_stacked_and_one_copy_at_rest() => headless.OnUiThread(() =>
    {
        using var rig = new ThreadFaceRig();
        rig.Harness.Main.OnMain(() => Assert.Equal(FontWeight.Normal, Assert.Single(rig.Titles()).FontWeight));
        rig.Start(FaceProperty.Weight);
        var frames = new List<string>();
        for (var step = 0; step < 16; step++)
        {
            rig.Tick(8);
            rig.Harness.Main.OnMain(() =>
            {
                var titles = rig.Titles();
                frames.Add(string.Join("+", titles.Select(t => (int)t.FontWeight)));
                Assert.All(titles, t => Assert.Contains((int)t.FontWeight, new[] { 400, 500 }));
                Assert.All(titles, t => Assert.Equal("A long title that wants all the room it can get", t.Text)); // the same text in both copies
                if (titles.Count == 2)
                {
                    Assert.Equal(FontWeight.Normal, titles[0].FontWeight); // Medium over Regular
                    Assert.Equal(FontWeight.Medium, titles[1].FontWeight);
                    Assert.Equal(titles[0].Bounds, titles[1].Bounds); // stacked exactly
                    Assert.Equal(titles[0].FontSize, titles[1].FontSize);
                }
            });
        }
        var firstAtRest = frames.IndexOf("500");
        Assert.True(firstAtRest > 1, string.Join(", ", frames)); // both drawn while it changes
        Assert.All(frames.Take(firstAtRest), frame => Assert.Equal("400+500", frame)); // from the first frame to the last one before rest
        Assert.All(frames.Skip(firstAtRest), frame => Assert.Equal("500", frame)); // at rest one copy, the Medium one, for good
    });

    // The copies' opacities run along the curve the weight used: Chrome.hover (.easeOut 0.1 s), Medium at the curve's value, Regular at its complement.
    [Fact]
    [Trait("Req", "PR-SIDEBAR-065")]
    public void The_regular_and_medium_copies_opacities_follow_the_hover_curve_frame_by_frame() => headless.OnUiThread(() =>
    {
        using var rig = new ThreadFaceRig();
        rig.Start(FaceProperty.Weight);
        for (var step = 1; step <= 16; step++)
        {
            rig.Tick(8);
            var seconds = step * 0.008;
            rig.Harness.Main.OnMain(() =>
            {
                var share = seconds >= 0.1 ? 1d : Chrome.Hover.Progress(seconds);
                var titles = rig.Titles();
                var medium = titles.Single(t => t.FontWeight == FontWeight.Medium);
                Assert.Equal(share, medium.Opacity, 6);
                if (share < 1) Assert.Equal(1 - share, titles.Single(t => t.FontWeight == FontWeight.Normal).Opacity, 6);
                else Assert.Single(titles);
            });
        }
    });

    // A reversal mid-crossfade (the title read again, unread -> read, inside an animation) turns round from the opacities it stands at:
    // nothing cuts, no frame draws a face outside Regular and Medium, and it ends on a lone Regular copy.
    [Fact]
    [Trait("Req", "PR-SIDEBAR-065")]
    public void A_weight_change_reversed_mid_crossfade_turns_round_from_the_current_opacities() => headless.OnUiThread(() =>
    {
        using var rig = new ThreadFaceRig();
        rig.Start(FaceProperty.Weight);
        rig.Tick(24);
        double mediumBefore = 0, regularBefore = 0;
        rig.Harness.Main.OnMain(() =>
        {
            var titles = rig.Titles();
            Assert.Equal(2, titles.Count);
            regularBefore = titles[0].Opacity;
            mediumBefore = titles[1].Opacity;
            Assert.InRange(mediumBefore, 0.05, 0.95);
        });
        rig.Harness.Main.OnMain(() => Transaction.WithAnimation(Chrome.Hover, () => rig.Model.UpdateThread(rig.Thread.Id, t => t with { HasUnread = false })));
        rig.Harness.Main.OnMain(() =>
        {
            var titles = rig.Titles();
            Assert.Equal(2, titles.Count);
            Assert.Equal(regularBefore, titles[0].Opacity); // not cut: the same opacities as a moment ago
            Assert.Equal(mediumBefore, titles[1].Opacity);
        });
        var last = mediumBefore;
        for (var step = 0; step < 16; step++)
        {
            rig.Tick(8);
            rig.Harness.Main.OnMain(() =>
            {
                var titles = rig.Titles();
                Assert.All(titles, t => Assert.Contains((int)t.FontWeight, new[] { 400, 500 }));
                var medium = titles.FirstOrDefault(t => t.FontWeight == FontWeight.Medium)?.Opacity ?? 0;
                Assert.True(medium <= last + 1e-9, $"the Medium copy rose again ({last} then {medium})"); // turning back, never forward
                if (titles.Count == 2) Assert.Equal(1d, titles[0].Opacity + titles[1].Opacity, 6);
                last = medium;
            });
        }
        rig.Harness.Main.OnMain(() =>
        {
            var only = Assert.Single(rig.Titles());
            Assert.Equal(FontWeight.Normal, only.FontWeight);
            Assert.Equal(1d, only.Opacity);
        });
    });
    // ED-NEW-perf-motion-11 (the lead's ruling on finding 5): the title's room follows a hover in and out on additive runs, as SwiftUI's
    // animations add: a hover out mid-glide keeps the glide in flight and adds the way back on top, so the room turns round at its
    // speed. Before, the way back replaced the glide from the value reached, at rest.
    [Fact]
    [Trait("Decision", "ED-NEW-perfmotion-11")]
    public void A_hover_out_mid_glide_turns_the_titles_room_round_at_its_speed() => headless.OnUiThread(() =>
    {
        using var rig = new ThreadFaceRig();
        double start = 0;
        rig.Harness.Main.OnMain(() => start = rig.Padding());
        var from = start - Chrome.RowHorizontalPadding;
        var to = from == 52 ? 18 : 52;
        var hover = Chrome.Hover;
        rig.Harness.Main.OnMain(() => rig.Face().SetTrailingClearance(to, hover));
        rig.Tick(16);
        rig.Tick(16);
        double reached = 0;
        rig.Harness.Main.OnMain(() => reached = rig.Padding());
        Assert.Equal(start + (to - from) * hover.Progress(0.032), reached, 3);
        rig.Harness.Main.OnMain(() => rig.Face().SetTrailingClearance(from, hover));
        rig.Harness.Main.OnMain(() => Assert.Equal(reached, rig.Padding(), 9)); // no jump at the hover out
        rig.Tick(16);
        // The first run carries on and the way back is added on top: start + (to - from) (p(48 ms) - p(16 ms)). The replacing run gave
        // reached + (start - reached) p(16 ms).
        var additive = start + (to - from) * (hover.Progress(0.048) - hover.Progress(0.016));
        var replacing = reached + (start - reached) * hover.Progress(0.016);
        Assert.True(Math.Abs(additive - replacing) > 0.5, $"the two rules differ here ({additive} and {replacing})");
        rig.Harness.Main.OnMain(() => Assert.Equal(additive, rig.Padding(), 3));
        rig.Tick(500);
        rig.Harness.Main.OnMain(() => Assert.Equal(start, rig.Padding(), 9));
    });
}
