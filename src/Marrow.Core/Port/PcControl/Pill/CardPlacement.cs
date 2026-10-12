// PORT-ONLY: Tactus pill v2, the card opens away from the pointer (OD-0145, ED-NEW-pcctl-20; DESIGN.md 4.1, 10.3 dodge
// table, 10.4 "Safety"; critique C12). When an answer control (a button or a chip) of a card about to open would land
// under the pointer (within 4 DIP), the pill and card shift sideways by the least amount that clears every such control by
// 12 DIP, at most half the card's width, with the card kept 16 DIP inside the work area. When no shift clears them, the
// card opens in place and the controls under the pointer stay inert until the pointer has left them (CardShield). The
// shift lasts until the card collapses. Pure.
namespace Marrow.Core.Port.PcControl.Pill;

/// <summary>The sideways shift of an opening card, and the controls left under the pointer when no shift clears them.</summary>
public sealed record CardShift(double Dx, IReadOnlyList<string> Inert)
{
    public static CardShift None { get; } = new(0, []);
}

public static class CardPlacement
{
    /// <summary>A shift leaves this much between the pointer and the control it clears.</summary>
    public const double Clearance = 12;

    /// <summary>
    /// The shift for a card whose top-left would be at (<paramref name="left"/>, <paramref name="top"/>) in the work area's
    /// DIP, with <paramref name="layout"/> in the card's own DIP, for the pointer at <paramref name="pointer"/> (null: the
    /// pointer is on another monitor or unknown).
    /// </summary>
    public static CardShift Shift(double left, double top, double width, IReadOnlyList<CardControl> layout, PillVector? pointer, PillArea work)
    {
        if (pointer is not { } p) return CardShift.None;
        var guarded = layout.Where(c => c.Kind != CardControlKind.Free)
            .Select(c => (c.Id, Rect: new PillRect(left + c.Bounds.X, top + c.Bounds.Y, c.Bounds.Width, c.Bounds.Height).Inflate(CardShield.UnderMargin)))
            .ToArray();
        var under = guarded.Where(c => Inside(c.Rect, 0, p)).ToArray();
        if (under.Length == 0) return CardShift.None;
        // Each candidate puts one control's edge 12 DIP past the pointer; the least that clears all and fits wins.
        var candidates = under.SelectMany(c => new[] { p.X - Clearance - c.Rect.Right, p.X + Clearance - c.Rect.X })
            .OrderBy(Math.Abs)
            .ThenBy(dx => dx);
        foreach (var dx in candidates)
        {
            if (Math.Abs(dx) > width / 2) break;
            if (left + dx < work.Left + PillAnchors.SideGap || left + dx + width > work.Right - PillAnchors.SideGap) continue;
            if (!guarded.Any(c => Inside(c.Rect, dx, p))) return new CardShift(dx, []);
        }
        return new CardShift(0, under.Select(c => c.Id).ToArray());
    }

    private static bool Inside(PillRect rect, double dx, PillVector point) =>
        point.X >= rect.X + dx && point.X <= rect.Right + dx && point.Y >= rect.Y && point.Y <= rect.Bottom;
}
