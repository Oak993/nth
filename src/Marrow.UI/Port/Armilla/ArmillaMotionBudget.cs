// PORT-ONLY: Dextra AS-8 (DESIGN.md rev 5, 2.5 "Motion budget"; the canvas's budget(), work/design/assistant/index.html lines
// 3062-3095; ED-NEW-dextra-as8-2). No Swift twin.
// At most 8 moving marks per view. Only marks that actually move count: Thinking, Listening and Speaking at any size, Working
// at 28 px and up (the small form works still), and Dextra's live presence at rest or needing you. An Astur's mark never moves,
// and a working card's shimmer counts as one. The budget is given out per list, the Board first, then the Result list, the
// home header and the sidebar; a list that does not fit goes still as a whole, so one list never mixes moving and still marks.
// A list that is not shown costs nothing.
namespace Marrow.UI.Port.Armilla;

/// <summary>The lists a view hands the budget to, in the order it gives it out (the canvas's LIST_RANK).</summary>
public enum ArmillaListRank { Board = 0, Result = 1, Header = 2, Other = 3 }

/// <summary>One list as the budget sees it: its rank, how many of its marks move now, whether it is shown.</summary>
public sealed record ArmillaListCost(ArmillaListRank Rank, int Moving, bool Shown = true);

public static class ArmillaMotionBudget
{
    /// <summary>The most moving marks one view shows at once.</summary>
    public const int PerView = 8;

    /// <summary>Whether a mark with this look moves (the canvas's MOVES).</summary>
    public static bool Moves(ArmillaLook look, ArmillaKind kind)
    {
        ArgumentNullException.ThrowIfNull(look);
        if (kind == ArmillaKind.Still) return false;
        if (look.State is ArmillaState.Thinking or ArmillaState.Listening or ArmillaState.Speaking) return true;
        if (look.State == ArmillaState.Working) return look.Size >= 28;
        return kind == ArmillaKind.Live && look.State is ArmillaState.Idle or ArmillaState.NeedsYou && look.Glyph == ArmillaGlyph.None;
    }

    /// <summary>
    /// Which lists go still: the indices into <paramref name="lists"/> whose cost does not fit what is left after the lists
    /// ranked before them. Lists of one rank keep their order.
    /// </summary>
    public static IReadOnlySet<int> Still(IReadOnlyList<ArmillaListCost> lists, int budget = PerView)
    {
        ArgumentNullException.ThrowIfNull(lists);
        var order = Enumerable.Range(0, lists.Count).OrderBy(i => (int)lists[i].Rank).ThenBy(i => i);
        var left = budget;
        var still = new HashSet<int>();
        foreach (var i in order)
        {
            var cost = lists[i].Shown ? Math.Max(0, lists[i].Moving) : 0;
            if (cost > left) still.Add(i);
            else left -= cost;
        }
        return still;
    }
}
