// PORT-ONLY: the card opens away from the pointer (OD-0145, ED-NEW-pcctl-20; DESIGN.md 4.1, 10.3 dodge table, 10.4
// "Safety"; critique C12): no shift when nothing protected is under the pointer, the least sideways shift that clears
// every answer control by 12 DIP, the 16 DIP inside the work area, at most half the card's width, and the controls left
// inert when no shift clears them.
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.Core.Tests.Port.PcControl.Pill;

[Trait("Decision", "ED-NEW-pcctl-20")]
public sealed class CardPlacementTests
{
    private static readonly PillArea Work = new(0, 0, 1920, 1032);
    private static readonly CardControl No = new("no", new PillRect(16, 200, 120, 36), CardControlKind.Answer);
    private static readonly CardControl Chat = new("chat", new PillRect(144, 200, 140, 36), CardControlKind.Answer);
    private static readonly CardControl Always = new("always", new PillRect(16, 244, 268, 36), CardControlKind.Always);
    private static readonly CardControl Later = new("later", new PillRect(360, 12, 64, 28), CardControlKind.Free);
    private static readonly CardControl[] Layout = [No, Chat, Always, Later];

    [Fact]
    public void Nothing_protected_under_the_pointer_or_no_pointer_opens_the_card_in_place()
    {
        Assert.Equal(CardShift.None, CardPlacement.Shift(740, 64, 440, Layout, new PillVector(1500, 600), Work));
        Assert.Equal(CardShift.None, CardPlacement.Shift(740, 64, 440, Layout, null, Work));
        // Later under the pointer is harmless.
        Assert.Equal(CardShift.None, CardPlacement.Shift(740, 64, 440, Layout, new PillVector(740 + 390, 64 + 26), Work));
    }

    [Fact]
    public void A_button_under_the_pointer_shifts_the_card_by_the_least_amount_that_clears_every_answer_by_12_DIP()
    {
        var pointer = new PillVector(740 + 16 + 60, 64 + 200 + 18);

        var shift = CardPlacement.Shift(740, 64, 440, Layout, pointer, Work);

        // Left by 76 would put "Allow for this chat" under the pointer, so the card goes right by 76.
        Assert.Equal(76, shift.Dx);
        Assert.Empty(shift.Inert);
        Assert.Equal(pointer.X + CardPlacement.Clearance, 740 + 76 + No.Bounds.X - CardShield.UnderMargin);
    }

    [Fact]
    public void The_card_stays_16_DIP_inside_the_work_area()
    {
        var pointer = new PillVector(16 + 16 + 60, 64 + 200 + 18);

        var shift = CardPlacement.Shift(16, 64, 440, Layout, pointer, Work);

        Assert.Equal(76, shift.Dx);
        Assert.True(16 + shift.Dx >= Work.Left + PillAnchors.SideGap);
    }

    [Fact]
    public void A_shift_over_half_the_card_s_width_is_not_taken_and_the_control_stays_inert_instead()
    {
        var wide = new CardControl("go", new PillRect(10, 100, 220, 36), CardControlKind.Answer);
        var pointer = new PillVector(1000 + 10 + 110, 64 + 118);

        var shift = CardPlacement.Shift(1000, 64, 240, [wide], pointer, Work);

        Assert.Equal(0, shift.Dx);
        Assert.Equal(new[] { "go" }, shift.Inert);
    }
}
