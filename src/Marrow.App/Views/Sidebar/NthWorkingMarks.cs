// PORT-ONLY: Calculi WU6 (ED-NEW-calculi-wu6-1; DESIGN.md sections 9 and 12; supersedes OD-0136 amendment 2, step 1 option B here). The working mark of
// the sidebar's thread rows and helper rows: the quiet working beads at 12 DIP in place of the nth tile-trace spinner, every running row
// animated, each on its own phase. A ghost carried as a picture holds the beads' still pose, drawn with ordinary rendering (NthStillSpinner is retired).
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.UI.Port.Nth.Beads;

namespace Marrow.App.Views.Sidebar;

/// <summary>PORT-ONLY. Builds the working marks (see the file note).</summary>
internal static class NthWorkingMarks
{
    /// <summary>The mark's n box in a row: 12 DIP, drawn flat (section 9).</summary>
    public const double Size = 12;

    /// <summary>The moving mark: the quiet beads of one thread's row (Pedes, or Tripus while helpers or a merge work). It keeps MiniSpinner's
    /// accessible name ("Working", PR-THEME-171), so it is a content element. The key (the thread's id) seeds the pulse: rows differ in direction
    /// and tempo, so several at once do not move together.</summary>
    public static NthBeadsSlot Working(string key, bool hydra = false)
    {
        var mark = new NthBeadsSlot(NthBeadsPlace.SidebarRow, key, hydra ? NthBeadsState.Hydra : null) { VerticalAlignment = VerticalAlignment.Center };
        return mark;
    }

    /// <summary>The still mark a thread row's ghost carries while it glides or genies as a picture: the beads' pose, with no movement and no
    /// composition visual, so it also shows in a snapshot.</summary>
    public static NthBeadsStill Still(bool hydra = false)
    {
        var mark = new NthBeadsStill { Width = Size, Height = Size, GlyphSize = Size, Hub = hydra ? NthBeadsHub.Tripus : NthBeadsHub.Pedes, VerticalAlignment = VerticalAlignment.Center };
        Avalonia.Automation.AutomationProperties.SetName(mark, NthBeadsSites.Name);
        return mark;
    }
}
