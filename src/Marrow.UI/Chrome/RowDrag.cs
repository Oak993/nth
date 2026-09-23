using Marrow.Core.Port;

namespace Marrow.UI.Chrome;

// Source: DroppyCode/UI/Chrome/RowDrag.swift (B2 1fbf81e), :1-62.
// Records: PR-SIDEBAR-051 (its source_evidence cites RowDrag.swift:41-77 and :36, line numbers that do not exist in
// the 62-line B2 file; the code it quotes is B2 :29-61 and :33, which is what the citations below use).
// Callers: SidebarView.swift:33 and FollowUpQueueTab.swift:35 (`RowDrag<UUID>`), ModelsSettingsPage.swift:9
// (`RowDrag<ModelPin>`).

/// <summary>
/// :3-6. One live grip-drag reorder of a row in a stack. The grabbed row follows the pointer exactly; whenever its
/// centre passes a neighbour's centre the item moves one slot and the neighbour slides across, with the row's own
/// slot shift folded into <see cref="Settled"/> so it never jumps under the pointer.
/// </summary>
/// <remarks>
/// :7. <c>struct RowDrag&lt;ID: Hashable&gt;</c> is an immutable sealed record (R-VAL-1, ED-077); the memberwise
/// <c>RowDrag(id:)</c> and <c>RowDrag()</c> are object initializers. Hashable maps to <c>IEquatable&lt;TID&gt;</c>
/// (R-PROTO-1). ENGINEERING DECISION: <c>var id: ID?</c> is a real optional (R-COND-1, <c>T?</c> is <c>T?</c>), and
/// C# makes <c>TID?</c> a <c>Nullable&lt;TID&gt;</c> only under a <c>struct</c> constraint; without it a default
/// <c>RowDrag&lt;Guid&gt;</c> would hold Guid.Empty instead of nil and <c>guard let id</c> (:36) could not fail.
/// So TID is constrained to value types, which UUID (Guid) satisfies; ModelsSettingsPage.swift:9's
/// <c>RowDrag&lt;ModelPin&gt;</c> needs ModelPin (AppSettings.swift:96, a struct of a ProviderKind and a String)
/// ported as a value type when that App unit comes.
/// </remarks>
public sealed record RowDrag<TID> where TID : struct, IEquatable<TID>
{
    /// <summary>:8. The grabbed row, nil when nothing is grabbed.</summary>
    public TID? Id { get; init; }

    /// <summary>:9-10. The pointer's travel since the grab.</summary>
    public double Translation { get; init; } = 0;

    /// <summary>
    /// :11-13. How far the row's slot has already moved toward the pointer through reorders; subtracting it keeps
    /// the row pinned under the pointer.
    /// </summary>
    public double Settled { get; init; } = 0;

    /// <summary>:15.</summary>
    public double VisualOffset => Translation - Settled;

    /// <summary>
    /// :17-28. Walks the grabbed row across every neighbour whose centre it has passed, so a fast flick crosses
    /// several rows in one step. <paramref name="move"/> swaps the item with the neighbour (before it for
    /// <c>placeAfter: false</c>, after it otherwise) and returns whether the order changed; it runs inside the
    /// caller's slide animation, and the slot compensation is folded into <see cref="Settled"/> here in the same
    /// transaction. Returns whether anything moved.
    /// The compensation must be applied here, not by <paramref name="move"/>: the Swift method mutates a copy of the
    /// caller's <c>@State</c> value, so a write to the state from inside <c>move</c> would be invisible to the loop.
    /// pastCentre: how much further than the neighbour's centre, as a share of its slot, the row's centre must
    /// travel before the two swap; 0 swaps the moment it crosses the centre, and the follow-up queue uses a quarter
    /// so a row held over the middle of a neighbour pairs with it instead.
    /// </summary>
    /// <remarks>
    /// :29-35. A <c>mutating</c> method returns the new value (R-VAL-1) together with Swift's Bool result. Swift's
    /// <c>move</c> has no default and follows <c>pastCentre: CGFloat = 0</c>, so it moves before pastCentre and
    /// call sites name their arguments (ED-095). <c>move: (_ neighbour: ID, _ placeAfter: Bool) -&gt; Bool</c> is a
    /// <c>Func&lt;TID, bool, bool&gt;</c> taking (neighbour, placeAfter). CGFloat is double.
    /// A refused move breaks the walk without advancing the index or adding the compensation (port_impacts.md
    /// entry 154).
    /// </remarks>
    public (RowDrag<TID> Next, bool Moved) Settle(
        ValueList<TID> order, // :30
        ValueMap<TID, double> heights, // :31
        double fallbackHeight, // :32
        Func<TID, bool, bool> move, // :34
        double pastCentre = 0) // :33
    {
        // :36. `guard let id, let index = order.firstIndex(of: id) else { return false }`; firstIndex(of:) compares
        // with Hashable's ==, which IndexOf does through EqualityComparer<TID>.Default (IEquatable<TID>).
        if (Id is not { } id) return (this, false);
        var index = order.IndexOf(id);
        if (index < 0) return (this, false);
        var own = heights.TryGetValue(id, out var ownHeight) ? ownHeight : fallbackHeight; // :37
        var settled = Settled; // the mutated `settled` of the Swift copy
        var current = index; // :38
        var moved = false; // :39
        while (true) // :40
        {
            var offset = Translation - settled; // :41, `visualOffset` (:15) over the settled written so far
            if (offset > 0 && current + 1 < order.Count) // :42
            {
                var next = order[current + 1]; // :43
                var slot = heights.TryGetValue(next, out var nextHeight) ? nextHeight : fallbackHeight; // :44
                // :45. Strict `>`; `move` runs only when the threshold passes, and a refusal breaks before :46-48.
                if (!(offset > (own + slot) / 2 + slot * pastCentre) || !move(next, true)) break;
                settled += slot; // :46
                current += 1; // :47
                moved = true; // :48
            }
            else if (offset < 0 && current > 0) // :49
            {
                var previous = order[current - 1]; // :50
                var slot = heights.TryGetValue(previous, out var previousHeight) ? previousHeight : fallbackHeight; // :51
                // :52
                if (!(-offset > (own + slot) / 2 + slot * pastCentre) || !move(previous, false)) break;
                settled -= slot; // :53
                current -= 1; // :54
                moved = true; // :55
            }
            else // :56-58
            {
                break;
            }
        }
        return (this with { Settled = settled }, moved); // :60
    }
}
