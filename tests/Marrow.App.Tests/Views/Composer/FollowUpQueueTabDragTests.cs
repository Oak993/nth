using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;
using static Marrow.App.Tests.Fixtures.FollowUpQueueTabRig;

namespace Marrow.App.Tests.Views.Composer;

// Reordering the queue by its grips: swaps, pairing, unlinking, and how the held row and its target look.
// Source: DroppyCode/App/Views/Composer/FollowUpQueueTab.swift (B3 52bc7926) :201-296, :464-484.
// Rows are 32 tall here, so a neighbour's slot is 32: the held row pairs while its travel is within a quarter slot (8) of
// 32, and swaps once it passes (32 + 32) / 2 + 0.25 * 32 = 40.
[Collection("Headless")]
public sealed class FollowUpQueueTabDragTests(HeadlessSession headless)
{
    private static FollowUpPrompt[] Plain(params string[] texts) => texts.Select(text => Prompt(text)).ToArray();

    /// <summary>Presses the left button on a row's grip and returns where it was pressed.</summary>
    private static Point Press(FollowUpTabRun run, FollowUpRow row)
    {
        var start = Center(Grip(row), run.Window);
        run.Step(() => run.Window.MouseDown(start, MouseButton.Left, RawInputModifiers.None));
        return start;
    }

    /// <summary>Moves the held pointer to this travel from where it was pressed.</summary>
    private static void Drag(FollowUpTabRun run, Point start, double dy, double dx = 0) =>
        run.Step(() => run.Window.MouseMove(start + new Vector(dx, dy), RawInputModifiers.LeftMouseButton));

    private static void Release(FollowUpTabRun run, Point start, double dy, double dx = 0) =>
        run.Step(() => run.Window.MouseUp(start + new Vector(dx, dy), MouseButton.Left, RawInputModifiers.None));

    /// <summary>The row's drawn surface (capsule included) in window coordinates, transforms applied.</summary>
    private static Rect Surface(FollowUpTabRun run, FollowUpRow row) => RectIn((Visual)row.Child!, run.Window);

    private static bool ShowsPlainLink(FollowUpRow row) =>
        Leading(row) is SwiftImage { SymbolName: "link" } glyph
        && AutomationProperties.GetAccessibilityView(glyph) == AccessibilityView.Raw;

    [Fact]
    [Trait("Req", "PR-COMPOSER-161")] // FollowUpQueueTab.swift:229-248,260-282: swap past the far quarter, pair over the middle
    public void Past_a_neighbours_far_quarter_the_two_swap_and_over_its_middle_they_pair()
    {
        RunTab(headless, Plain("r1", "r2", "r3"), run =>
        {
            var start = Press(run, run.Rows[0]);

            // Over the middle of r2: it is lit as the pair target, the order is unchanged.
            Drag(run, start, 32);
            Assert.Equal(["r1", "r2", "r3"], Texts(run.Runtime));
            Assert.True(ShowsPlainLink(run.Rows[1]), "the target shows the link in place of its number");
            Assert.True(ShowsPlainLink(run.Rows[0]), "so does the held row");
            Assert.False(ShowsPlainLink(run.Rows[2]));

            // Still inside the quarter either side of r2's centre line (32 +- 8): no swap yet, past its middle but not its far quarter.
            Drag(run, start, 38);
            Assert.Equal(["r1", "r2", "r3"], Texts(run.Runtime));
            Assert.True(ShowsPlainLink(run.Rows[1]));

            // Past r2's far quarter: the two swap, and r3 is not a target.
            Drag(run, start, 45);
            Assert.Equal(["r2", "r1", "r3"], Texts(run.Runtime));
            Assert.All(run.Rows, row => Assert.False(ShowsPlainLink(row)));

            // Letting go there pairs nothing.
            Release(run, start, 45);
            Assert.All(run.Runtime.FollowUps, prompt => Assert.Null(prompt.BundleID));
            Assert.Equal(["r2", "r1", "r3"], Texts(run.Runtime));
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-161")] // FollowUpQueueTab.swift:239-245 a fast flick crosses several rows in one step
    public void A_fast_flick_crosses_several_rows_in_one_step()
    {
        RunTab(headless, Plain("r1", "r2", "r3"), run =>
        {
            var start = Press(run, run.Rows[0]);
            Drag(run, start, 120);
            Assert.Equal(["r2", "r3", "r1"], Texts(run.Runtime));
            Release(run, start, 120);
            Assert.Equal(["r2", "r3", "r1"], Texts(run.Runtime));
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-161")] // FollowUpQueueTab.swift:228 sideways travel plays no part; upward works as downward
    public void Sideways_travel_plays_no_part_and_the_swap_works_upwards_too()
    {
        RunTab(headless, Plain("r1", "r2", "r3"), run =>
        {
            var start = Press(run, run.Rows[0]);
            Drag(run, start, 32, dx: 300);
            Assert.Equal(["r1", "r2", "r3"], Texts(run.Runtime));
            Assert.True(ShowsPlainLink(run.Rows[1]));
            Drag(run, start, 45, dx: -200);
            Assert.Equal(["r2", "r1", "r3"], Texts(run.Runtime));
            Release(run, start, 45);
        });

        RunTab(headless, Plain("r1", "r2", "r3"), run =>
        {
            var start = Press(run, run.Rows[2]);
            Drag(run, start, -32);
            Assert.True(ShowsPlainLink(run.Rows[1]), "r2 is the pair target from below");
            Drag(run, start, -45);
            Assert.Equal(["r1", "r3", "r2"], Texts(run.Runtime));
            Release(run, start, -45);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-161")] // FollowUpQueueTab.swift:15 the held row follows the pointer exactly, across swaps
    public void The_held_row_follows_the_pointer_exactly_before_and_after_a_swap()
    {
        RunTab(headless, Plain("r1", "r2", "r3"), run =>
        {
            var held = run.Rows[0];
            var home = Surface(run, held).Center;
            var start = Press(run, held);

            Drag(run, start, 10);
            Assert.Equal(home.Y + 10, Surface(run, held).Center.Y, 0.5);
            Assert.Equal(home.X, Surface(run, held).Center.X, 0.5);

            // Past the swap the slot moves with it: the row stays under the pointer while the list flows.
            Drag(run, start, 45);
            Assert.Equal(["r2", "r1", "r3"], Texts(run.Runtime));
            Assert.Same(held, run.Rows[1]); // the same control: the grip under the pointer was never torn down
            Assert.Equal(home.Y + 45, Surface(run, held).Center.Y, 0.5);
            Release(run, start, 45);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-161")] // FollowUpQueueTab.swift:284-289 letting go over a pair target bundles them on a spring
    public void Letting_go_over_the_pair_target_bundles_the_two_on_a_spring()
    {
        RunTab(headless, Plain("r1", "r2", "r3"), run =>
        {
            var seen = new List<Animation?>();
            Tracked<ValueList<FollowUpPrompt>>? probe = null;
            run.Step(() => probe = new Tracked<ValueList<FollowUpPrompt>>(() => run.Runtime.FollowUps, _ => seen.Add(Transaction.Current)));

            var start = Press(run, run.Rows[0]);
            Drag(run, start, 32);
            seen.Clear();
            Release(run, start, 32);

            var prompts = run.Runtime.FollowUps;
            Assert.Equal(["r2", "r1", "r3"], Texts(run.Runtime)); // the held prompt joins after the target
            Assert.NotNull(prompts[0].BundleID);
            Assert.Equal(prompts[0].BundleID, prompts[1].BundleID);
            Assert.Null(prompts[2].BundleID);
            Assert.Equal(Animation.Spring(response: 0.36, dampingFraction: 0.78), seen.Last());

            // The pair reads as one: a number on the head, the unlink glyph on the second, no stray target left lit.
            Assert.IsType<TextBlock>(Leading(run.Rows[0]));
            Assert.IsType<Button>(Leading(run.Rows[1]));
            Assert.False(ShowsPlainLink(run.Rows[2]));
            run.Step(() => probe!.Dispose());
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-162")] // FollowUpQueueTab.swift:250-258 more than 60 percent of its height out of its slot unlinks a row
    public void Pulling_a_linked_row_more_than_60_percent_of_its_height_out_of_its_slot_unlinks_it()
    {
        var bundle = Guid.NewGuid();
        RunTab(headless, [Prompt("a", bundle), Prompt("b", bundle), Prompt("c")], run =>
        {
            var start = Press(run, run.Rows[1]);

            // 60 percent of 32 is 19.2: 18 up still holds the pair.
            Drag(run, start, -18);
            Assert.Equal(run.Runtime.FollowUps[0].BundleID, run.Runtime.FollowUps[1].BundleID);
            Assert.NotNull(run.Runtime.FollowUps[1].BundleID);

            // 20 up, and the row leaves at once (a lone partner is released with it).
            Drag(run, start, -20);
            Assert.All(run.Runtime.FollowUps, prompt => Assert.Null(prompt.BundleID));
            Assert.Equal(["a", "b", "c"], Texts(run.Runtime));
            Release(run, start, -20);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-162")] // FollowUpQueueTab.swift:253-258 hovering the neighbour's middle again pairs them back on release
    public void Hovering_the_neighbours_middle_again_pairs_them_back_on_release()
    {
        var bundle = Guid.NewGuid();
        RunTab(headless, [Prompt("a", bundle), Prompt("b", bundle)], run =>
        {
            var start = Press(run, run.Rows[1]);
            Drag(run, start, -20);
            Assert.All(run.Runtime.FollowUps, prompt => Assert.Null(prompt.BundleID));

            Drag(run, start, -32);
            Assert.True(ShowsPlainLink(run.Rows[0]), "a is lit as the target again");
            Release(run, start, -32);

            Assert.NotNull(run.Runtime.FollowUps[0].BundleID);
            Assert.Equal(run.Runtime.FollowUps[0].BundleID, run.Runtime.FollowUps[1].BundleID);
            Assert.Equal(["a", "b"], Texts(run.Runtime));
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-164")] // FollowUpQueueTab.swift:118,464-484
    [Trait("Req", "UI-COMPOSER-049")]
    public void A_lifted_row_rides_on_a_capsule_with_a_shadow_and_a_pair_target_is_lit_with_an_accent_capsule_and_frame()
    {
        RunTab(headless, Plain("r1", "r2", "r3"), run =>
        {
            var held = run.Rows[0];
            var target = run.Rows[1];
            var bystander = run.Rows[2];
            var heldHome = Surface(run, held);
            var targetHome = Surface(run, target);
            var bystanderHome = Surface(run, bystander);

            // At rest: no capsule, no shadow, no scale.
            Assert.Null(((Border)held.Child!).Background);
            Assert.Equal(default, ((Border)held.Child!).BoxShadow);
            Assert.Null(held.LiftShadow);

            var start = Press(run, held);
            Drag(run, start, 10); // lifted, not yet over a neighbour's middle
            var lifted = (Border)held.Child!;
            Assert.Equal(Effective(Chrome.Overlay(0.12).Brush(run.Tab)), Effective(lifted.Background));
            Assert.True(lifted.CornerRadius.TopLeft >= 16, "a capsule");
            // ED-NEW-glass-g3 (was the capsule's BoxShadow of Blur (10 - 0.5) / 0.288675 at the colour's full 0.28, outside the box only): the
            // row draws the capsule's own coverage under it, radius 10 (sigma 10, ED-327), y 4, black 0.28 times the fill's alpha (26 of 255): 7.
            Assert.Equal(default, lifted.BoxShadow);
            Assert.Equal((10d, 4d), (FollowUpRow.LiftShadowRadius, FollowUpRow.LiftShadowY));
            Assert.Equal(Color.FromArgb((byte)(0.28 * 255), 0, 0, 0), held.LiftShadow);
            Assert.Equal(Color.FromArgb(7, 0, 0, 0), held.DrawnLiftShadow);
            Assert.Equal(1, held.ZIndex);
            Assert.Equal(1.01, Surface(run, held).Width / heldHome.Width, 0.0005); // grows to 1.01
            Assert.Equal(heldHome.Center.X, Surface(run, held).Center.X, 0.5); // no indent yet
            Assert.Equal(heldHome.Center.Y + 10, Surface(run, held).Center.Y, 0.5);
            Assert.Equal(1.0, Surface(run, bystander).Width / bystanderHome.Width, 0.0005);

            // Over r2's middle: the held row indents 14 to the right; the target is lit.
            Drag(run, start, 32);
            Assert.Equal(heldHome.Center.X + 14, Surface(run, held).Center.X, 0.5);
            Assert.Equal(heldHome.Center.Y + 32, Surface(run, held).Center.Y, 0.5);

            var lit = (Border)target.Child!;
            Assert.Equal(Effective(Chrome.Accent.Opacity(0.18).Brush(run.Tab)), Effective(lit.Background));
            Assert.True(lit.CornerRadius.TopLeft >= 16, "a capsule");
            Assert.Equal(default, lit.BoxShadow);
            Assert.Null(target.LiftShadow); // the pair target's capsule casts none (:466-472)
            Assert.Equal(1.008, Surface(run, target).Width / targetHome.Width, 0.0005);
            Assert.Equal(targetHome.Center, Surface(run, target).Center);
            var frame = target.GetVisualDescendants().OfType<Border>().Single(border => border.IsVisible && border.BorderBrush is not null);
            Assert.Equal(new Thickness(1.5), frame.BorderThickness);
            Assert.Equal(Effective(Chrome.Accent.Opacity(0.65).Brush(run.Tab)), Effective(frame.BorderBrush));
            Assert.Equal(32, target.Bounds.Height); // the frame is a stroke over the row, not size added to it

            // The link takes the place of the numbers on both rows, as a plain, non-interactive glyph hidden from screen readers.
            Assert.True(ShowsPlainLink(held));
            Assert.True(ShowsPlainLink(target));
            Assert.Equal(14, Leading(target).Bounds.Width);

            // Let go: everything settles back.
            Release(run, start, 32);
            foreach (var row in run.Rows)
            {
                Assert.Null(((Border)row.Child!).Background);
                Assert.Equal(default, ((Border)row.Child!).BoxShadow);
                Assert.Null(row.LiftShadow); // ED-NEW-glass-g3: the row draws the held capsule's shadow itself, and only while held
                Assert.Equal(default, row.DrawnLiftShadow);
                Assert.Equal(0, row.ZIndex);
            }
            Assert.Equal(1.0, Surface(run, run.Rows[0]).Width / heldHome.Width, 0.0005);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-164")] // FollowUpQueueTab.swift:107 the rule is off for a lifted row
    public void A_lifted_row_has_no_rule_under_it()
    {
        RunTab(headless, Plain("r1", "r2"), run =>
        {
            var first = run.Rows[0];
            Assert.True(RuleOf(first).IsVisible);
            var start = Press(run, first);
            Drag(run, start, 5);
            Assert.False(RuleOf(first).IsVisible);
            Release(run, start, 5);
            Assert.True(RuleOf(first).IsVisible);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-161")] // FollowUpQueueTab.swift:157-163 a row that leaves mid-drag ends the drag cleanly
    public void A_row_deleted_while_held_ends_the_drag_and_leaves_nothing_lifted()
    {
        RunTab(headless, Plain("r1", "r2", "r3"), run =>
        {
            var start = Press(run, run.Rows[0]);
            Drag(run, start, 10);
            run.Step(() => run.Runtime.RemoveFollowUp(run.Runtime.FollowUps[0].Id));
            run.Settle();

            Assert.Equal(["r2", "r3"], Texts(run.Runtime));
            foreach (var row in run.Rows)
            {
                Assert.Null(((Border)row.Child!).Background);
                Assert.Equal(default, ((Border)row.Child!).BoxShadow);
                Assert.Null(row.LiftShadow); // ED-NEW-glass-g3: the row draws the held capsule's shadow itself, and only while held
                Assert.Equal(default, row.DrawnLiftShadow);
            }
            Release(run, start, 10);
            Assert.Equal(["r2", "r3"], Texts(run.Runtime));
        });
    }
}
