using Marrow.UI.Port.Armilla;

namespace Marrow.UI.Tests.Port.Armilla;

// AS-8 "done when": the motion budget, at most 8 moving marks per view, counted by what actually moves (an Astur's mark and a
// small Working ring never count, idle row marks never count), given out per list from the Board down, never splitting one list,
// and a hidden list costs nothing (2.5 "Motion budget", revised r5).
public sealed class ArmillaMotionBudgetTests
{
    [Theory]
    [InlineData(ArmillaState.Thinking, 20, ArmillaKind.Row, true)]
    [InlineData(ArmillaState.Listening, 24, ArmillaKind.Row, true)]
    [InlineData(ArmillaState.Speaking, 176, ArmillaKind.Live, true)]
    [InlineData(ArmillaState.Working, 20, ArmillaKind.Row, false)]
    [InlineData(ArmillaState.Working, 24, ArmillaKind.Live, false)]
    [InlineData(ArmillaState.Working, 28, ArmillaKind.Row, true)]
    [InlineData(ArmillaState.Working, 64, ArmillaKind.Live, true)]
    [InlineData(ArmillaState.Idle, 20, ArmillaKind.Row, false)]
    [InlineData(ArmillaState.Idle, 160, ArmillaKind.Live, true)]
    [InlineData(ArmillaState.NeedsYou, 24, ArmillaKind.Row, false)]
    [InlineData(ArmillaState.NeedsYou, 24, ArmillaKind.Live, true)]
    [InlineData(ArmillaState.Paused, 160, ArmillaKind.Live, false)]
    [InlineData(ArmillaState.Offline, 160, ArmillaKind.Live, false)]
    [InlineData(ArmillaState.Thinking, 30, ArmillaKind.Still, false)]
    [Trait("Feature", "AS-8")]
    [Trait("Decision", "ED-NEW-dextra-as8-2")]
    public void Only_marks_that_actually_move_count(ArmillaState state, double size, ArmillaKind kind, bool moves) =>
        Assert.Equal(moves, ArmillaMotionBudget.Moves(new ArmillaLook { Size = size, State = state }, kind));

    [Fact]
    [Trait("Feature", "AS-8")]
    [Trait("Decision", "ED-NEW-dextra-as8-2")]
    public void A_live_presence_showing_a_glyph_does_not_count()
    {
        Assert.False(ArmillaMotionBudget.Moves(new ArmillaLook { Size = 160, State = ArmillaState.Idle, Glyph = ArmillaGlyph.Done }, ArmillaKind.Live));
    }

    [Fact]
    [Trait("Feature", "AS-8")]
    [Trait("Decision", "ED-NEW-dextra-as8-2")]
    public void The_budget_goes_board_first_and_a_list_that_does_not_fit_goes_still_as_a_whole()
    {
        var lists = new[]
        {
            new ArmillaListCost(ArmillaListRank.Other, 2),
            new ArmillaListCost(ArmillaListRank.Header, 1),
            new ArmillaListCost(ArmillaListRank.Board, 6),
            new ArmillaListCost(ArmillaListRank.Result, 2),
        };
        // board 6, result 2: 8 used; the header (1) and the sidebar (2) go still
        Assert.Equal(new HashSet<int> { 0, 1 }, ArmillaMotionBudget.Still(lists).ToHashSet());
        // a bigger result list does not fit after the board, and the smaller header then does
        lists[3] = lists[3] with { Moving = 3 };
        Assert.Equal(new HashSet<int> { 0, 3 }, ArmillaMotionBudget.Still(lists).ToHashSet());
    }

    [Fact]
    [Trait("Feature", "AS-8")]
    [Trait("Decision", "ED-NEW-dextra-as8-2")]
    public void A_hidden_list_costs_nothing_and_a_list_with_nothing_moving_never_goes_still()
    {
        var lists = new[]
        {
            new ArmillaListCost(ArmillaListRank.Board, 9, Shown: false),
            new ArmillaListCost(ArmillaListRank.Result, 8),
            new ArmillaListCost(ArmillaListRank.Header, 0),
            new ArmillaListCost(ArmillaListRank.Other, 1),
        };
        Assert.Equal(new HashSet<int> { 3 }, ArmillaMotionBudget.Still(lists).ToHashSet());
        Assert.Empty(ArmillaMotionBudget.Still([new ArmillaListCost(ArmillaListRank.Board, 8)]));
        Assert.Equal(8, ArmillaMotionBudget.PerView);
    }
}
