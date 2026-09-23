using Marrow.Core.Port;
using Marrow.UI.Chrome;

namespace Marrow.UI.Tests.Chrome;

// Source: DroppyCode/UI/Chrome/RowDrag.swift (B2 1fbf81e), :1-62.
// Every expected value below is worked by hand from RowDrag.swift:29-61 (settle) and :15 (visualOffset), with
// the sidebar's call from SidebarView.swift:782-801 (fallbackHeight Chrome.rowHeight + 1 = 29, no pastCentre)
// and port_impacts.md entry 154 (the move callback returns Bool; a refused move stops the walk without
// advancing the index or adding the compensation). Heights are chosen so every threshold is exact in binary.
//
// Port shape used here (IMPLEMENTING.md R-VAL-1, ED-095):
// - RowDrag<TID> is immutable; Swift's memberwise init is an object initializer over Id, Translation, Settled.
// - The mutating settle returns the new value together with Swift's Bool, read here by deconstruction:
//   var (next, moved) = drag.Settle(...).
// - Swift's `move` has no default and follows `pastCentre: CGFloat = 0`, so it moves before pastCentre and every
//   call names its arguments (ED-095).
// - The UUID ids of SidebarView.swift:33 (`RowDrag<UUID>`) are Guid, and `ID?` is a real optional: a default
//   RowDrag has no id, which is not the same as Guid.Empty.
public sealed class RowDragTests
{
    private const double SidebarFallback = 29; // App/Views/Sidebar/SidebarView.swift:782, Chrome.rowHeight + 1

    private static readonly Guid A = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = new("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid C = new("00000000-0000-0000-0000-00000000000c");
    private static readonly Guid D = new("00000000-0000-0000-0000-00000000000d");
    private static readonly Guid E = new("00000000-0000-0000-0000-00000000000e");
    private static readonly Guid G = new("00000000-0000-0000-0000-000000000009");

    private static ValueMap<Guid, double> Heights(params (Guid Id, double Height)[] pairs) =>
        new(pairs.Select(p => KeyValuePair.Create(p.Id, p.Height)));

    [Fact]
    [Trait("Req", "PR-SIDEBAR-051")] // UI/Chrome/RowDrag.swift:8-15
    public void A_new_drag_has_no_id_and_zero_translation_settled_and_visual_offset()
    {
        var drag = new RowDrag<Guid>();

        Assert.Null(drag.Id);
        Assert.Equal(0.0, drag.Translation);
        Assert.Equal(0.0, drag.Settled);
        Assert.Equal(0.0, drag.VisualOffset);
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-051")] // UI/Chrome/RowDrag.swift:15
    public void Visual_offset_is_translation_minus_settled()
    {
        var drag = new RowDrag<Guid> { Id = G, Translation = 12.5, Settled = 40 };

        Assert.Equal(-27.5, drag.VisualOffset);
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-051")] // UI/Chrome/RowDrag.swift:37, :42-48
    public void The_swap_needs_strictly_more_than_half_the_two_slots_and_equality_does_not_swap()
    {
        // own = fallback 29 (G has no height), C = 40: threshold (29 + 40) / 2 + 40 * 0 = 34.5.
        ValueList<Guid> order = new[] { A, G, C };
        var heights = Heights((A, 30), (C, 40));
        var calls = new List<(Guid Neighbour, bool PlaceAfter)>();

        var atThreshold = new RowDrag<Guid> { Id = G, Translation = 34.5 };
        var (unchanged, movedAtThreshold) = atThreshold.Settle(
            order: order, heights: heights, fallbackHeight: SidebarFallback,
            move: (neighbour, placeAfter) => { calls.Add((neighbour, placeAfter)); return true; });

        Assert.False(movedAtThreshold);
        Assert.Empty(calls);
        Assert.Equal(0.0, unchanged.Settled);
        Assert.Equal(34.5, unchanged.VisualOffset);

        var past = new RowDrag<Guid> { Id = G, Translation = 34.75 };
        var (next, moved) = past.Settle(
            order: order, heights: heights, fallbackHeight: SidebarFallback,
            move: (neighbour, placeAfter) => { calls.Add((neighbour, placeAfter)); return true; });

        Assert.True(moved);
        Assert.Equal(new[] { (C, true) }, calls);
        Assert.Equal(40.0, next.Settled); // the neighbour's slot, not the grabbed row's
        Assert.Equal(-5.25, next.VisualOffset);
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-051")] // UI/Chrome/RowDrag.swift:40-48
    public void A_fast_flick_down_crosses_several_rows_in_one_call_and_folds_each_slot_into_settled()
    {
        // G own 20; B 30; C has no height (fallback 29); D 50. Translation 100.
        // 100 > (20+30)/2 = 25: swap B, settled 30. 70 > (20+29)/2 = 24.5: swap C, settled 59.
        // 41 > (20+50)/2 = 35: swap D, settled 109. Offset -9: the upward test reads order[3] of the order it was
        // given (C, fallback 29), 9 > (20+29)/2 = 24.5 fails: stop.
        ValueList<Guid> order = new[] { A, G, B, C, D };
        var heights = Heights((A, 10), (G, 20), (B, 30), (D, 50));
        var calls = new List<(Guid Neighbour, bool PlaceAfter)>();
        var drag = new RowDrag<Guid> { Id = G, Translation = 100 };

        var (next, moved) = drag.Settle(
            order: order, heights: heights, fallbackHeight: SidebarFallback,
            move: (neighbour, placeAfter) => { calls.Add((neighbour, placeAfter)); return true; });

        Assert.True(moved);
        Assert.Equal(new[] { (B, true), (C, true), (D, true) }, calls);
        Assert.Equal(109.0, next.Settled);
        Assert.Equal(-9.0, next.VisualOffset);
        Assert.Equal(100.0, next.Translation);
        Assert.Equal(G, next.Id);
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-051")] // UI/Chrome/RowDrag.swift:49-55
    public void A_fast_flick_up_crosses_several_rows_placing_before_each_and_subtracts_each_slot()
    {
        // G own 20; C 50; B has no height (fallback 29); A 30. Translation -100.
        // 100 > (20+50)/2 = 35: swap C, settled -50. 50 > (20+29)/2 = 24.5: swap B, settled -79.
        // 21 > (20+30)/2 = 25 fails: stop.
        ValueList<Guid> order = new[] { A, B, C, G, D };
        var heights = Heights((A, 30), (C, 50), (G, 20), (D, 10));
        var calls = new List<(Guid Neighbour, bool PlaceAfter)>();
        var drag = new RowDrag<Guid> { Id = G, Translation = -100 };

        var (next, moved) = drag.Settle(
            order: order, heights: heights, fallbackHeight: SidebarFallback,
            move: (neighbour, placeAfter) => { calls.Add((neighbour, placeAfter)); return true; });

        Assert.True(moved);
        Assert.Equal(new[] { (C, false), (B, false) }, calls);
        Assert.Equal(-79.0, next.Settled);
        Assert.Equal(-21.0, next.VisualOffset);
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-051")] // UI/Chrome/RowDrag.swift:42, :49, :56-57
    public void The_walk_stops_at_either_end_of_the_order()
    {
        ValueList<Guid> order = new[] { G, B, C };
        var calls = new List<(Guid Neighbour, bool PlaceAfter)>();

        // First row dragged up: `current > 0` fails, nothing moves.
        var up = new RowDrag<Guid> { Id = G, Translation = -1000 };
        var (upNext, upMoved) = up.Settle(
            order: order, heights: ValueMap<Guid, double>.Empty, fallbackHeight: SidebarFallback,
            move: (neighbour, placeAfter) => { calls.Add((neighbour, placeAfter)); return true; });
        Assert.False(upMoved);
        Assert.Empty(calls);
        Assert.Equal(0.0, upNext.Settled);

        // Dragged down past everything: B and C at 29 each, then `current + 1 < order.count` fails.
        var down = new RowDrag<Guid> { Id = G, Translation = 1000 };
        var (downNext, downMoved) = down.Settle(
            order: order, heights: ValueMap<Guid, double>.Empty, fallbackHeight: SidebarFallback,
            move: (neighbour, placeAfter) => { calls.Add((neighbour, placeAfter)); return true; });
        Assert.True(downMoved);
        Assert.Equal(new[] { (B, true), (C, true) }, calls);
        Assert.Equal(58.0, downNext.Settled);
        Assert.Equal(942.0, downNext.VisualOffset);
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-051")] // UI/Chrome/RowDrag.swift:30, :43, :50
    public void The_walk_indexes_the_order_it_was_given_so_a_reversal_names_the_row_at_the_original_position()
    {
        // `order` is a let parameter the move callback cannot change, and `current` indexes it as given.
        // G own 10, B 100. Translation 60 > (10+100)/2 = 55: swap B, settled 100, current 1, offset -40.
        // Upward: previous = order[0], which is G itself; slot 10, 40 > (10+10)/2 = 10, so move(G, false) is
        // asked. Refusing it stops the walk with settled still 100.
        ValueList<Guid> order = new[] { G, B, C };
        var heights = Heights((G, 10), (B, 100));
        var calls = new List<(Guid Neighbour, bool PlaceAfter)>();
        var drag = new RowDrag<Guid> { Id = G, Translation = 60 };

        var (next, moved) = drag.Settle(
            order: order, heights: heights, fallbackHeight: SidebarFallback,
            move: (neighbour, placeAfter) => { calls.Add((neighbour, placeAfter)); return neighbour != G; });

        Assert.True(moved);
        Assert.Equal(new[] { (B, true), (G, false) }, calls);
        Assert.Equal(100.0, next.Settled);
        Assert.Equal(-40.0, next.VisualOffset);
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-051")] // UI/Chrome/RowDrag.swift:45, :52
    public void A_refused_move_stops_the_walk_without_advancing_or_compensating_and_keeps_earlier_swaps()
    {
        // All 29. B accepted (settled 29, offset 71); C refused: the walk breaks there, once, and settled stays 29.
        ValueList<Guid> order = new[] { G, B, C, D };
        var calls = new List<(Guid Neighbour, bool PlaceAfter)>();
        var drag = new RowDrag<Guid> { Id = G, Translation = 100 };

        var (next, moved) = drag.Settle(
            order: order, heights: ValueMap<Guid, double>.Empty, fallbackHeight: SidebarFallback,
            move: (neighbour, placeAfter) => { calls.Add((neighbour, placeAfter)); return neighbour != C; });

        Assert.True(moved);
        Assert.Equal(new[] { (B, true), (C, true) }, calls);
        Assert.Equal(29.0, next.Settled);
        Assert.Equal(71.0, next.VisualOffset);
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-051")] // UI/Chrome/RowDrag.swift:52, :60
    public void A_refused_first_move_upward_returns_false_and_leaves_settled_alone()
    {
        ValueList<Guid> order = new[] { A, B, G };
        var calls = new List<(Guid Neighbour, bool PlaceAfter)>();
        var drag = new RowDrag<Guid> { Id = G, Translation = -100, Settled = 0 };

        var (next, moved) = drag.Settle(
            order: order, heights: ValueMap<Guid, double>.Empty, fallbackHeight: SidebarFallback,
            move: (neighbour, placeAfter) => { calls.Add((neighbour, placeAfter)); return false; });

        Assert.False(moved);
        Assert.Equal(new[] { (B, false) }, calls);
        Assert.Equal(0.0, next.Settled);
        Assert.Equal(-100.0, next.VisualOffset);
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-051")] // UI/Chrome/RowDrag.swift:36
    public void A_grabbed_id_missing_from_the_order_returns_false_and_never_calls_move()
    {
        ValueList<Guid> order = new[] { A, B, C };
        var calls = 0;
        var drag = new RowDrag<Guid> { Id = E, Translation = 1000 };

        var (next, moved) = drag.Settle(
            order: order, heights: ValueMap<Guid, double>.Empty, fallbackHeight: SidebarFallback,
            move: (_, _) => { calls++; return true; });

        Assert.False(moved);
        Assert.Equal(0, calls);
        Assert.Equal(0.0, next.Settled);
        Assert.Equal(1000.0, next.VisualOffset);
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-051")] // UI/Chrome/RowDrag.swift:36
    public void A_drag_with_no_id_returns_false_even_when_the_order_holds_the_all_zero_uuid()
    {
        // `guard let id` fails on nil before any lookup. Guid.Empty is a valid UUID, so an id that defaulted to
        // it instead of nil would be found at index 0 and walked down.
        ValueList<Guid> order = new[] { Guid.Empty, A, B };
        var calls = 0;
        var drag = new RowDrag<Guid> { Translation = 1000 };

        var (next, moved) = drag.Settle(
            order: order, heights: ValueMap<Guid, double>.Empty, fallbackHeight: SidebarFallback,
            move: (_, _) => { calls++; return true; });

        Assert.False(moved);
        Assert.Equal(0, calls);
        Assert.Equal(0.0, next.Settled);
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-051")] // UI/Chrome/RowDrag.swift:41-42, :49, :56-57
    public void No_offset_moves_nothing()
    {
        ValueList<Guid> order = new[] { A, G, B };
        var calls = 0;
        var drag = new RowDrag<Guid> { Id = G, Translation = 29, Settled = 29 };

        var (next, moved) = drag.Settle(
            order: order, heights: ValueMap<Guid, double>.Empty, fallbackHeight: SidebarFallback,
            move: (_, _) => { calls++; return true; });

        Assert.False(moved);
        Assert.Equal(0, calls);
        Assert.Equal(29.0, next.Settled);
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-051")] // UI/Chrome/RowDrag.swift:33, :45, :52
    public void Past_centre_defaults_to_zero_and_adds_its_share_of_the_neighbour_slot_in_both_directions()
    {
        // Fallback 32 for every row. pastCentre 0 (the default the sidebar uses): threshold 32.
        // pastCentre 0.25: threshold 32 + 32 * 0.25 = 40, still strict.
        ValueList<Guid> order = new[] { A, G, B };
        var heights = ValueMap<Guid, double>.Empty;
        var calls = new List<(Guid Neighbour, bool PlaceAfter)>();
        Func<Guid, bool, bool> log = (neighbour, placeAfter) => { calls.Add((neighbour, placeAfter)); return true; };

        var (byDefault, movedByDefault) = new RowDrag<Guid> { Id = G, Translation = 36 }.Settle(
            order: order, heights: heights, fallbackHeight: 32, move: log);
        Assert.True(movedByDefault);
        Assert.Equal(32.0, byDefault.Settled);

        calls.Clear();
        var (held, movedHeld) = new RowDrag<Guid> { Id = G, Translation = 40 }.Settle(
            order: order, heights: heights, fallbackHeight: 32, move: log, pastCentre: 0.25);
        Assert.False(movedHeld);
        Assert.Empty(calls);
        Assert.Equal(0.0, held.Settled);

        var (down, movedDown) = new RowDrag<Guid> { Id = G, Translation = 40.5 }.Settle(
            order: order, heights: heights, fallbackHeight: 32, move: log, pastCentre: 0.25);
        Assert.True(movedDown);
        Assert.Equal(new[] { (B, true) }, calls);
        Assert.Equal(32.0, down.Settled);

        calls.Clear();
        var (heldUp, movedHeldUp) = new RowDrag<Guid> { Id = G, Translation = -40 }.Settle(
            order: order, heights: heights, fallbackHeight: 32, move: log, pastCentre: 0.25);
        Assert.False(movedHeldUp);
        Assert.Empty(calls);
        Assert.Equal(0.0, heldUp.Settled);

        var (up, movedUp) = new RowDrag<Guid> { Id = G, Translation = -40.5 }.Settle(
            order: order, heights: heights, fallbackHeight: 32, move: log, pastCentre: 0.25);
        Assert.True(movedUp);
        Assert.Equal(new[] { (A, false) }, calls);
        Assert.Equal(-32.0, up.Settled);
        Assert.Equal(-8.5, up.VisualOffset);
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-051")] // UI/Chrome/RowDrag.swift:37, :44-46, :51-53
    public void Past_centre_scales_the_neighbour_slot_not_the_grabbed_row_or_the_average_of_the_two()
    {
        // G own 20, both neighbours 40, pastCentre 0.25.
        // Swift :45 / :52: (own + slot) / 2 + slot * pastCentre = (20 + 40) / 2 + 40 * 0.25 = 30 + 10 = 40.
        // Scaling own instead gives 30 + 20 * 0.25 = 35; scaling the average gives 30 + 30 * 0.25 = 37.5.
        // Translation 39 lies above both wrong thresholds and below the right one, so it must not swap.
        ValueList<Guid> order = new[] { A, G, B };
        var heights = Heights((A, 40), (G, 20), (B, 40));
        var calls = new List<(Guid Neighbour, bool PlaceAfter)>();
        Func<Guid, bool, bool> log = (neighbour, placeAfter) => { calls.Add((neighbour, placeAfter)); return true; };

        var (heldDown, movedHeldDown) = new RowDrag<Guid> { Id = G, Translation = 39 }.Settle(
            order: order, heights: heights, fallbackHeight: SidebarFallback, move: log, pastCentre: 0.25);
        Assert.False(movedHeldDown);
        Assert.Empty(calls);
        Assert.Equal(0.0, heldDown.Settled);
        Assert.Equal(39.0, heldDown.VisualOffset);

        // 40.5 > 40: move(B, true) once, settled += 40, visual 0.5; then B was the last row, so the walk stops.
        var (down, movedDown) = new RowDrag<Guid> { Id = G, Translation = 40.5 }.Settle(
            order: order, heights: heights, fallbackHeight: SidebarFallback, move: log, pastCentre: 0.25);
        Assert.True(movedDown);
        Assert.Equal(new[] { (B, true) }, calls);
        Assert.Equal(40.0, down.Settled);
        Assert.Equal(0.5, down.VisualOffset);

        calls.Clear();
        var (heldUp, movedHeldUp) = new RowDrag<Guid> { Id = G, Translation = -39 }.Settle(
            order: order, heights: heights, fallbackHeight: SidebarFallback, move: log, pastCentre: 0.25);
        Assert.False(movedHeldUp);
        Assert.Empty(calls);
        Assert.Equal(0.0, heldUp.Settled);
        Assert.Equal(-39.0, heldUp.VisualOffset);

        // -40.5: 40.5 > 40, move(A, false) once, settled -= 40, visual -0.5; then A was the first row, so it stops.
        var (up, movedUp) = new RowDrag<Guid> { Id = G, Translation = -40.5 }.Settle(
            order: order, heights: heights, fallbackHeight: SidebarFallback, move: log, pastCentre: 0.25);
        Assert.True(movedUp);
        Assert.Equal(new[] { (A, false) }, calls);
        Assert.Equal(-40.0, up.Settled);
        Assert.Equal(-0.5, up.VisualOffset);
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-051")] // UI/Chrome/RowDrag.swift:17-27, :36
    public void Settle_leaves_the_original_value_alone_and_the_next_delivery_continues_from_the_returned_settled()
    {
        // The compensation lives in the value settle returns, never in anything the move callback touches.
        var drag = new RowDrag<Guid> { Id = G, Translation = 30 };
        var calls = new List<(Guid Neighbour, bool PlaceAfter)>();
        Func<Guid, bool, bool> log = (neighbour, placeAfter) => { calls.Add((neighbour, placeAfter)); return true; };

        // First delivery: 30 > 29, swap B; settled 29, visual 1.
        var (first, firstMoved) = drag.Settle(
            order: new ValueList<Guid>(new[] { G, B, C }), heights: ValueMap<Guid, double>.Empty,
            fallbackHeight: SidebarFallback, move: log);
        Assert.True(firstMoved);
        Assert.Equal(0.0, drag.Settled);
        Assert.Equal(30.0, drag.VisualOffset);
        Assert.Equal(29.0, first.Settled);
        Assert.Equal(1.0, first.VisualOffset);

        // The caller's model now reads B, G, C. Next delivery: translation 60, offset 31 > 29, swap C; settled 58.
        var (second, secondMoved) = (first with { Translation = 60 }).Settle(
            order: new ValueList<Guid>(new[] { B, G, C }), heights: ValueMap<Guid, double>.Empty,
            fallbackHeight: SidebarFallback, move: log);
        Assert.True(secondMoved);
        Assert.Equal(new[] { (B, true), (C, true) }, calls);
        Assert.Equal(58.0, second.Settled);
        Assert.Equal(2.0, second.VisualOffset);
    }
}
