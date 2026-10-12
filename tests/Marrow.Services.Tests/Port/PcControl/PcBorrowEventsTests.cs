using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Port.PcControl.Native;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-18; DESIGN.md 2.7 steps 4 to 7). The pure parts of a borrow's input:
// what the hook sees for each SendInput entry, the ups a borrow owes, the held input and its replay.
[Trait("Decision", "ED-NEW-pcctl-18")]
public sealed class PcBorrowEventsTests
{
    [Fact]
    public void A_click_is_a_move_a_down_and_an_up_to_the_hook()
    {
        var click = PcInputPlan.Click(new PcPoint(10, 10), new PcRect(0, 0, 100, 100), PcMouseButton.Right, 1, [], vk => vk);

        Assert.Equal(
            new[] { PcLedgerEvent.Move(), PcLedgerEvent.Button(2, true), PcLedgerEvent.Button(2, false) },
            PcBorrowEvents.Ledger([click]));
    }

    [Fact]
    public void A_unicode_character_is_a_packet_key_and_a_wheel_is_one_wheel_event()
    {
        var text = PcInputPlan.Text("h", vk => vk)[0];
        var wheel = PcInputPlan.Wheel(new PcPoint(1, 1), new PcRect(0, 0, 10, 10), PcScrollDirection.Left, 2);

        Assert.Equal(
            new[] { PcLedgerEvent.Key(PcBorrowEvents.Packet, true), PcLedgerEvent.Key(PcBorrowEvents.Packet, false), PcLedgerEvent.Move(), PcLedgerEvent.Wheel() },
            PcBorrowEvents.Ledger([text, wheel]));
    }

    [Fact]
    public void X_buttons_are_four_and_five()
    {
        var x2 = PcInputEvent.Mouse(0, 0, 2, PcBorrowEvents.XDOWN);
        var x1 = PcInputEvent.Mouse(0, 0, 1, PcBorrowEvents.XUP);

        Assert.Equal(PcLedgerEvent.Button(5, true), Assert.Single(PcBorrowEvents.Ledger(x2)));
        Assert.Equal(PcLedgerEvent.Button(4, false), Assert.Single(PcBorrowEvents.Ledger(x1)));
    }

    [Fact]
    public void Held_keys_replay_with_their_own_codes_and_extended_flag()
    {
        var right = new PcRawInput(0, PcRawKind.Key, true, 0x27, false, 0) { Scan = 0x4D, Extended = true };
        var up = new PcRawInput(0, PcRawKind.Key, false, 0x41, false, 0);

        Assert.Equal(PcInputEvent.Key(0x27, 0x4D, PcInputPlan.KEY_EXTENDED), PcBorrowEvents.Replay(right, vk => 0));
        Assert.Equal(PcInputEvent.Key(0x41, 0x99, PcInputPlan.KEY_UP), PcBorrowEvents.Replay(up, vk => 0x99));
        Assert.Null(PcBorrowEvents.Replay(up with { Code = 0 }, vk => 0));
    }

    [Fact]
    public void Held_buttons_and_wheels_replay_at_the_cursor_and_moves_do_not_replay()
    {
        PcInputEvent? Replay(PcRawKind kind, bool down, ushort code, int delta = 0) =>
            PcBorrowEvents.Replay(new PcRawInput(0, kind, down, code, false, 0) { Delta = delta, Moved = new PcPoint(3, 3) }, vk => vk);

        Assert.Equal(PcInputEvent.Mouse(0, 0, 0, PcInputPlan.LEFTDOWN), Replay(PcRawKind.Button, true, 1));
        Assert.Equal(PcInputEvent.Mouse(0, 0, 0, PcInputPlan.MIDDLEUP), Replay(PcRawKind.Button, false, 3));
        Assert.Equal(PcInputEvent.Mouse(0, 0, 2, PcBorrowEvents.XDOWN), Replay(PcRawKind.Button, true, 5));
        Assert.Equal(PcInputEvent.Mouse(0, 0, -120, PcInputPlan.WHEEL), Replay(PcRawKind.Wheel, false, 0, -120));
        Assert.Equal(PcInputEvent.Mouse(0, 0, 240, PcInputPlan.HWHEEL), Replay(PcRawKind.Wheel, false, 1, 240));
        Assert.Null(Replay(PcRawKind.Move, false, 0));
    }

    [Fact]
    public void The_ups_owed_are_buttons_first_then_keys_in_reverse()
    {
        var pressed = new PcPressed();
        pressed.Track([PcInputEvent.Key(0x11, 0x1D, 0), PcInputEvent.Key(0x10, 0x2A, 0), PcInputEvent.Mouse(0, 0, 0, PcInputPlan.LEFTDOWN), PcInputEvent.Key(0x41, 0, PcInputPlan.KEY_UNICODE)]);
        pressed.Track([PcInputEvent.Key(0x10, 0x2A, PcInputPlan.KEY_UP)]);

        Assert.True(pressed.Any);
        var ups = pressed.TakeUps();

        Assert.Equal(new[] { PcInputEvent.Mouse(0, 0, 0, PcInputPlan.LEFTUP), PcInputEvent.Key(0x11, 0x1D, PcInputPlan.KEY_UP) }, ups);
        Assert.False(pressed.Any);
        Assert.Empty(pressed.TakeUps());
    }

    [Fact]
    public void A_complete_click_owes_nothing()
    {
        var pressed = new PcPressed();
        pressed.Track(PcInputPlan.Click(new PcPoint(1, 1), new PcRect(0, 0, 10, 10), PcMouseButton.Left, 2, [0x11], vk => vk));

        Assert.False(pressed.Any);
    }

    [Fact]
    public void Held_input_sums_moves_keeps_the_rest_in_order_and_zeroes()
    {
        var held = new PcHeldInput();
        held.Add(new PcRawInput(0, PcRawKind.Move, false, 0, false, 0) { Moved = new PcPoint(4, -1) });
        held.Add(new PcRawInput(1, PcRawKind.Key, true, 0x41, false, 0));
        held.Add(new PcRawInput(2, PcRawKind.Move, false, 0, false, 0) { Moved = new PcPoint(1, 1) });
        held.Add(new PcRawInput(3, PcRawKind.Key, false, 0x41, false, 0));
        held.Drop();

        Assert.Equal(5, held.Received);
        Assert.Equal(new PcPoint(5, 0), held.Delta);
        Assert.Equal(1, held.Keys);
        Assert.Equal(new long[] { 1, 3 }, held.Take().Select(e => e.TimeMs));
        Assert.Empty(held.Take());
        Assert.Equal(new PcPoint(5, 0), held.TakeDelta());
        Assert.Equal(new PcPoint(0, 0), held.Delta);

        held.Add(new PcRawInput(4, PcRawKind.Key, true, 0x42, false, 0));
        held.Zero();
        Assert.Empty(held.Take());
    }
}
