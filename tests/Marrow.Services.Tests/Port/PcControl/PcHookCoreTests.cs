using Marrow.Services.Port.PcControl;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-17; DESIGN.md 2.7 "Hold the user's input", critiques C2, C3, C17).
// What the hook callbacks decide, with no hook installed: physical held state, the pair-aware swallow, the auto-repeat
// abort, which key codes are recorded at all, swallowed move deltas, wheels and modifiers.
[Trait("Decision", "ED-NEW-pcctl-17")]
public sealed class PcHookCoreTests
{
    private const uint A = 0x41;
    private const uint B = 0x42;
    private const uint LeftCtrl = 0xA2;
    private const uint LeftShift = 0xA0;

    [Fact]
    public void Outside_a_borrow_nothing_is_swallowed_and_a_plain_key_has_no_code()
    {
        var core = new PcHookCore();

        var down = core.Key(A, 30, isDown: true, injected: false, extra: 0, time: 1, out var swallow);

        Assert.False(swallow);
        Assert.Equal(0, down.Code);
        Assert.Equal(0, down.Scan);
        Assert.Equal(1, down.HeldKeys);
        Assert.False(down.Repeat);
    }

    [Fact]
    public void Esc_watched_keys_and_modifiers_keep_their_codes()
    {
        var core = new PcHookCore();
        core.Watch(key => key == 0x20);

        Assert.Equal(0x1B, core.Key(0x1B, 1, true, false, 0, 1, out _).Code);
        Assert.Equal(0x20, core.Key(0x20, 57, true, false, 0, 2, out _).Code);
        Assert.Equal(LeftCtrl, core.Key(LeftCtrl, 29, true, false, 0, 3, out _).Code);
        Assert.Equal(0, core.Key(B, 48, true, false, 0, 4, out _).Code);
    }

    [Fact]
    public void A_tagged_injected_key_keeps_its_code_and_tag_and_never_counts_as_held()
    {
        var core = new PcHookCore();

        var record = core.Key(A, 30, true, injected: true, extra: 0xBEEF, time: 1, out var swallow);

        Assert.False(swallow);
        Assert.Equal(A, record.Code);
        Assert.Equal(0xBEEFUL, record.Tag);
        Assert.Equal(0, record.HeldKeys);
    }

    [Fact]
    public void A_hold_swallows_keys_pressed_during_it_down_and_up_with_their_scan_codes()
    {
        var core = new PcHookCore();
        core.BeginSwallow();

        var down = core.Key(A, 30, true, false, 0, 1, out var downSwallowed);
        var up = core.Key(A, 30, false, false, 0, 2, out var upSwallowed);

        Assert.True(downSwallowed);
        Assert.True(upSwallowed);
        Assert.True(down.Swallowed);
        Assert.Equal(A, down.Code);
        Assert.Equal(30, down.Scan);
        Assert.Equal(0, up.HeldKeys);
    }

    [Fact]
    public void A_key_held_before_the_hold_lets_its_up_through_and_its_repeat_asks_to_abort()
    {
        var core = new PcHookCore();
        core.Key(LeftShift, 42, true, false, 0, 1, out _);
        core.BeginSwallow();

        var repeat = core.Key(LeftShift, 42, true, false, 0, 2, out var repeatSwallowed);
        Assert.True(repeat.Repeat);
        Assert.False(repeatSwallowed);
        Assert.True(core.AbortRequested);

        core.Key(LeftShift, 42, false, false, 0, 3, out var upSwallowed);
        Assert.False(upSwallowed);

        core.EndSwallow();
        Assert.False(core.AbortRequested);
        Assert.False(core.Swallowing);
    }

    [Fact]
    public void A_hold_swallows_untagged_injected_input_but_never_a_tagged_event()
    {
        var core = new PcHookCore();
        core.BeginSwallow();

        core.Key(A, 30, true, injected: true, extra: 0, time: 1, out var untagged);
        core.Key(B, 48, true, injected: true, extra: 0x51, time: 2, out var tagged);
        core.Mouse(PcRawKind.Button, 1, true, 5, 5, 0, injected: true, extra: 0x51, time: 3, out var taggedClick);

        Assert.True(untagged);
        Assert.False(tagged);
        Assert.False(taggedClick);
    }

    [Fact]
    public void A_swallowed_move_measures_from_the_last_position_the_cursor_reached()
    {
        var core = new PcHookCore();
        core.Mouse(PcRawKind.Move, 0, false, 100, 100, 0, false, 0, 1, out _);
        core.BeginSwallow();

        var first = core.Mouse(PcRawKind.Move, 0, false, 105, 102, 0, false, 0, 2, out var swallowed);
        var second = core.Mouse(PcRawKind.Move, 0, false, 103, 101, 0, false, 0, 3, out _);
        core.EndSwallow();
        var after = core.Mouse(PcRawKind.Move, 0, false, 110, 110, 0, false, 0, 4, out var delivered);

        Assert.True(swallowed);
        Assert.Equal((5, 2), (first.Dx, first.Dy));
        Assert.Equal((3, 1), (second.Dx, second.Dy));
        Assert.False(delivered);
        Assert.Equal((0, 0), (after.Dx, after.Dy));
    }

    [Fact]
    public void Buttons_are_held_physically_and_swallowed_in_pairs()
    {
        var core = new PcHookCore();
        var right = core.Mouse(PcRawKind.Button, 2, true, 0, 0, 0, false, 0, 1, out _);
        Assert.Equal(2, right.HeldButtons);
        core.BeginSwallow();

        var rightUp = core.Mouse(PcRawKind.Button, 2, false, 0, 0, 0, false, 0, 2, out var rightUpSwallowed);
        var left = core.Mouse(PcRawKind.Button, 1, true, 0, 0, 0, false, 0, 3, out var leftSwallowed);
        var leftUp = core.Mouse(PcRawKind.Button, 1, false, 0, 0, 0, false, 0, 4, out var leftUpSwallowed);

        Assert.False(rightUpSwallowed);
        Assert.Equal(0, rightUp.HeldButtons);
        Assert.True(leftSwallowed);
        Assert.Equal(1, left.HeldButtons);
        Assert.Equal(1, left.Code);
        Assert.True(leftUpSwallowed);
        Assert.Equal(0, leftUp.HeldButtons);
    }

    [Fact]
    public void A_wheel_records_its_delta_and_which_wheel()
    {
        var core = new PcHookCore();

        var vertical = core.Mouse(PcRawKind.Wheel, 0, false, 0, 0, -120, false, 0, 1, out _);
        var horizontal = core.Mouse(PcRawKind.Wheel, 1, false, 0, 0, 240, false, 0, 2, out _);

        Assert.Equal(-120, vertical.Dx);
        Assert.Equal(0, vertical.Code);
        Assert.Equal(240, horizontal.Dx);
        Assert.Equal(1, horizontal.Code);
        Assert.Equal(0, horizontal.HeldButtons);
    }

    [Fact]
    public void Modifiers_are_read_from_the_physical_keys()
    {
        var core = new PcHookCore();

        core.Key(LeftCtrl, 29, true, false, 0, 1, out _);
        Assert.Equal(2, core.Modifiers());
        var shifted = core.Key(LeftShift, 42, true, false, 0, 2, out _);
        Assert.Equal(6, shifted.Modifiers);
        core.Key(0xA4, 56, true, injected: true, extra: 0, time: 3, out _);
        Assert.Equal(6, core.Modifiers());
        core.Key(LeftCtrl, 29, false, false, 0, 4, out _);
        Assert.Equal(4, core.Modifiers());
    }
}
