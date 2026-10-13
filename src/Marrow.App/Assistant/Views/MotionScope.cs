// PORT-ONLY: Dextra AS-8 (DESIGN.md rev 5, 2.5 "Motion budget"; the canvas's budget(), work/design/assistant/index.html lines
// 3062-3095; ED-NEW-dextra-as8-5). No Swift twin.
// The motion budget of one view (a window): at most 8 moving marks, counted by what actually moves (a working card's shimmer counts
// as one; an Astur's mark, a small working ring and an idle mark never count), given out per list (the Board first, then the
// Result list, the home header, the sidebar). A list that does not fit goes still as a whole, so one list never mixes moving and
// still marks. A list that is not shown costs nothing. Lists recount when what they hold changes; the scope sets each one's calm.
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.UI.Port.Armilla;

namespace Marrow.App.Assistant.Views;

public sealed class MotionScope
{
    private static readonly ConditionalWeakTable<TopLevel, MotionScope> scopes = new();
    private readonly List<Entry> lists = [];
    private bool queued;

    /// <summary>The scope of the window <paramref name="top"/>.</summary>
    public static MotionScope For(TopLevel top) => scopes.GetValue(top, static _ => new MotionScope());

    public sealed class Entry
    {
        internal Entry(MotionScope scope, ArmillaListRank rank, Func<int> moving, Func<bool> shown, Action<bool> calm)
        {
            Scope = scope;
            Rank = rank;
            Moving = moving;
            Shown = shown;
            Calm = calm;
        }

        internal MotionScope Scope { get; }

        public ArmillaListRank Rank { get; }

        internal Func<int> Moving { get; }

        internal Func<bool> Shown { get; }

        internal Action<bool> Calm { get; }

        /// <summary>Whether the list is still now (set by the scope).</summary>
        public bool IsCalm { get; internal set; }

        /// <summary>The list's marks changed what they cost: recount (once per dispatcher turn).</summary>
        public void Changed() => Scope.Queue();

        public void Leave() => Scope.Remove(this);
    }

    public IReadOnlyList<Entry> Lists => lists;

    public Entry Join(ArmillaListRank rank, Func<int> moving, Func<bool> shown, Action<bool> calm)
    {
        var entry = new Entry(this, rank, moving, shown, calm);
        lists.Add(entry);
        Recount();
        return entry;
    }

    private void Remove(Entry entry)
    {
        if (lists.Remove(entry)) Queue();
    }

    private void Queue()
    {
        if (queued) return;
        queued = true;
        Dispatcher.UIThread.Post(() => { queued = false; Recount(); }, DispatcherPriority.Background);
    }

    /// <summary>Gives the budget out now.</summary>
    public void Recount()
    {
        var costs = lists.Select(l => new ArmillaListCost(l.Rank, l.Moving(), l.Shown())).ToList();
        var still = ArmillaMotionBudget.Still(costs);
        for (var i = 0; i < lists.Count; i++)
        {
            var calm = still.Contains(i);
            if (lists[i].IsCalm == calm) continue;
            lists[i].IsCalm = calm;
            lists[i].Calm(calm);
        }
    }
}
