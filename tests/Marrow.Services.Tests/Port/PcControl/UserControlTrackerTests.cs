using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-19; DESIGN.md 2.9 "The user's control"). Whose control a write is
// aimed at: focused and typed into within 2 s, or pressed on now; another control of the same window is free; keys and
// text for the app's own focus wait while the user types in the window; with the hooks blind, a focused control in front
// counts as the user's until their input is 2 s old.
[Trait("Decision", "ED-NEW-pcctl-19")]
public sealed class UserControlTrackerTests
{
    private const nint Window = 0x1a;
    private const nint Field = 0x1a1;
    private static readonly PcUiNode Edit = new("1", "edit", "Subject") { Bounds = new PcRect(10, 10, 200, 20), States = PcStates.Focused };
    private static readonly PcUiNode Stock = Edit with { NativeWindow = Field };

    [Fact]
    public void A_focused_control_typed_into_within_two_seconds_is_the_users_until_then()
    {
        var check = UserControlTracker.Check(Typed(1000), 2500, new PcWriteTarget(Window, Edit, true, Field, true));

        Assert.Equal(PcUserHold.Typing, check.Hold);
        Assert.Equal(1000 + UserControlTracker.TypingMs, check.AgainMs);
        Assert.False(UserControlTracker.Check(Typed(1000), 1000 + UserControlTracker.TypingMs, new PcWriteTarget(Window, Edit, true, Field, true)).Users);
    }

    [Fact]
    public void Another_control_of_the_same_window_is_not_the_users()
    {
        Assert.False(UserControlTracker.Check(Typed(1000), 1500, new PcWriteTarget(Window, Edit, true, Field, false)).Users);
        Assert.False(UserControlTracker.Check(Typed(1000, focus: 0x1a2), 1500, new PcWriteTarget(Window, Stock, true, 0x1a2, false)).Users);
        Assert.True(UserControlTracker.Check(Typed(1000), 1500, new PcWriteTarget(Window, Stock, true, Field, false)).Users);
        // The focus moved to another of the window's controls since the key.
        Assert.False(UserControlTracker.Check(Typed(1000), 1500, new PcWriteTarget(Window, Edit, true, 0x1a9, true)).Users);
    }

    [Fact]
    public void Keys_into_another_window_or_none_at_all_leave_the_control_free()
    {
        Assert.False(UserControlTracker.Check(Typed(1000, window: 0x2b), 1500, new PcWriteTarget(Window, Edit, true, Field, true)).Users);
        Assert.False(UserControlTracker.Check(new PcPhysicalSnapshot { Healthy = true }, 1500, new PcWriteTarget(Window, Edit, true, Field, true)).Users);
    }

    [Fact]
    public void Keys_and_text_for_the_apps_own_focus_wait_while_the_user_types_in_the_window()
    {
        Assert.Equal(PcUserHold.Typing, UserControlTracker.Check(Typed(1000), 1500, new PcWriteTarget(Window, null, true, 0x1a9, true)).Hold);
    }

    [Fact]
    public void A_pointer_pressed_on_the_control_makes_it_the_users_while_held()
    {
        var held = new PcPhysicalSnapshot { Healthy = true, HeldButtons = 1, PressedWindow = Window, PressedPoint = new PcPoint(50, 15) };
        var idle = new PcWriteTarget(Window, Edit with { States = PcStates.None }, false, 0, false);

        var check = UserControlTracker.Check(held, 5000, idle);

        Assert.Equal(PcUserHold.Pressed, check.Hold);
        Assert.Equal(5000 + UserControlTracker.PollMs, check.AgainMs);
        Assert.False(UserControlTracker.Check(held with { PressedPoint = new PcPoint(500, 500) }, 5000, idle).Users);
        Assert.False(UserControlTracker.Check(held with { HeldButtons = 0 }, 5000, idle).Users);
        Assert.False(UserControlTracker.Check(held with { PressedWindow = 0x2b }, 5000, idle).Users);
    }

    [Fact]
    public void While_the_hooks_are_blind_a_focused_control_in_front_is_the_users_until_its_input_is_two_seconds_old()
    {
        var blind = new PcPhysicalSnapshot { Healthy = false };
        var front = new PcWriteTarget(Window, Edit, true, Field, true);

        Assert.Equal(PcUserHold.Unseen, UserControlTracker.Check(blind, 1000, front).Hold);
        Assert.False(UserControlTracker.Check(blind, 1000, front with { InFront = false }).Users);
        Assert.False(UserControlTracker.Check(blind, 1000, front with { Focused = false }).Users);
        Assert.True(UserControlTracker.Check(blind, 1000, front with { Element = Stock, Focused = false }).Users);
        Assert.True(UserControlTracker.Check(blind with { LastUnseenMs = 500 }, 1000, front).Users);
        Assert.False(UserControlTracker.Check(blind with { LastUnseenMs = 500 }, 500 + UserControlTracker.TypingMs, front).Users);
    }

    private static PcPhysicalSnapshot Typed(long at, nint window = Window, nint focus = Field) =>
        new() { Healthy = true, KeyWindow = window, KeyFocus = focus, KeyWindowMs = at, LastKeyMs = at };
}
