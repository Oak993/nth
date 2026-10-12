using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Window;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Window;

// PORT-ONLY (ED-NEW-session-hotkeys; owner 2026-10-11): hold Ctrl alone for 500 ms and the sidebar numbers its sessions;
// the numbers go the moment Ctrl comes up (owner: "they should not wait for me to click somewhere"), and the number pad's
// 1 to 9 open sessions like the number row's (owner: "with the numpad working as well").
[Collection("Headless")]
public sealed class SessionHotkeysWindowTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "OD-0155")] // ED-NEW-session-hotkeys
    public void Holding_Control_alone_shows_the_numbers_at_500_ms_and_not_before() => headless.OnUiThread(() =>
    {
        using var rig = new SessionHotkeysRig();
        rig.ControlPress();
        rig.Advance(250);
        // Windows repeats a held key: the repeats neither restart nor double the hold.
        rig.Window.KeyPress(Key.LeftCtrl, RawInputModifiers.Control, PhysicalKey.ControlLeft, null);
        rig.Advance(249);
        Assert.False(rig.Shown);
        Assert.Empty(rig.Calls);
        rig.Advance(1);
        Assert.True(rig.Shown);
        Assert.Equal([true], rig.Calls);
        rig.Advance(400); // they stay while Ctrl stays down
        Assert.Equal([true], rig.Calls);
    });

    [Fact]
    [Trait("Decision", "OD-0155")] // ED-NEW-session-hotkeys
    public void A_quick_chord_like_Control_C_never_shows_the_numbers_even_if_Control_stays_down() => headless.OnUiThread(() =>
    {
        using var rig = new SessionHotkeysRig();
        rig.ControlPress();
        rig.Advance(80);
        rig.Window.KeyPress(Key.C, RawInputModifiers.Control, PhysicalKey.C, null);
        rig.Window.KeyRelease(Key.C, RawInputModifiers.Control, PhysicalKey.C, null);
        rig.Advance(1000);
        rig.Window.KeyPress(Key.LeftCtrl, RawInputModifiers.Control, PhysicalKey.ControlLeft, null); // a repeat
        rig.Advance(1000);
        Assert.Empty(rig.Calls);
        rig.ControlRelease();
        rig.ControlPress(); // a fresh hold works again
        rig.Advance(500);
        Assert.Equal([true], rig.Calls);
    });

    [Fact]
    [Trait("Decision", "OD-0155")] // ED-NEW-session-hotkeys
    public void Releasing_Control_hides_the_numbers_on_that_same_key_up_with_no_click_or_move() => headless.OnUiThread(() =>
    {
        using var rig = new SessionHotkeysRig();
        rig.ControlPress();
        rig.Advance(500);
        Assert.True(rig.Shown);
        rig.ControlRelease(); // no RunJobs, no time, no pointer: the key-up itself
        Assert.False(rig.Shown);
        Assert.Equal([true, false], rig.Calls);
    });

    [Fact]
    [Trait("Decision", "OD-0155")] // ED-NEW-session-hotkeys
    public void The_release_is_seen_even_when_the_focused_text_box_and_a_handler_swallow_the_key_up() => headless.OnUiThread(() =>
    {
        using var rig = new SessionHotkeysRig();
        var box = new TextBox();
        rig.Window.Content = box;
        Dispatcher.UIThread.RunJobs();
        box.Focus();
        // A child that takes every key-up for itself, on the way down and on the way up.
        box.AddHandler(InputElement.KeyUpEvent, (_, e) => e.Handled = true, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        rig.Window.AddHandler(InputElement.KeyUpEvent, (_, e) => e.Handled = true, RoutingStrategies.Tunnel);
        rig.ControlPress();
        rig.Advance(500);
        Assert.True(rig.Shown);
        Assert.True(box.IsFocused);
        rig.ControlRelease();
        Assert.False(rig.Shown);
        Assert.Equal([true, false], rig.Calls);
    });

    [Fact]
    [Trait("Decision", "OD-0155")] // ED-NEW-session-hotkeys
    public void A_lost_key_up_is_caught_by_the_key_state_safety_net_within_one_watch_interval() => headless.OnUiThread(() =>
    {
        using var rig = new SessionHotkeysRig();
        rig.ControlPress();
        rig.Advance(500);
        Assert.True(rig.Shown);
        rig.ControlDown = false; // Ctrl came up somewhere this window never heard of
        rig.Advance(ThreadWindow.HoldWatchInterval.TotalMilliseconds);
        Assert.False(rig.Shown);
        Assert.Equal([true, false], rig.Calls);
        // A hold whose release is lost before the delay ends never shows at all.
        rig.ControlPress();
        rig.Advance(100);
        rig.ControlDown = false;
        rig.Advance(500);
        Assert.Equal([true, false], rig.Calls);
    });

    [Fact]
    [Trait("Decision", "OD-0155")] // ED-NEW-session-hotkeys
    public void Another_key_a_press_or_the_window_going_inactive_hides_the_numbers() => headless.OnUiThread(() =>
    {
        using var rig = new SessionHotkeysRig();
        rig.ControlPress();
        rig.Advance(500);
        rig.Window.KeyPress(Key.K, RawInputModifiers.Control, PhysicalKey.K, null);
        Assert.False(rig.Shown);
        Assert.Equal([true, false], rig.Calls);
        rig.ControlRelease();

        rig.ControlPress();
        rig.Advance(500);
        rig.Window.MouseDown(new Avalonia.Point(10, 10), MouseButton.Left, RawInputModifiers.Control);
        Assert.False(rig.Shown);
        rig.Window.MouseUp(new Avalonia.Point(10, 10), MouseButton.Left, RawInputModifiers.Control);
        rig.ControlRelease();

        rig.ControlPress();
        rig.Advance(500);
        Assert.True(rig.Shown);
        AppCommandsRig.Deactivate(rig.Window);
        Assert.False(rig.Shown);
        Assert.Equal([true, false, true, false, true, false], rig.Calls);
    });

    [Fact]
    [Trait("Decision", "OD-0155")] // ED-NEW-session-hotkeys
    public void Control_with_Shift_or_Alt_AltGr_is_not_a_hold() => headless.OnUiThread(() =>
    {
        using var rig = new SessionHotkeysRig();
        rig.ControlPress(RawInputModifiers.Shift);
        rig.Advance(600);
        rig.ControlRelease();
        rig.ControlPress(RawInputModifiers.Alt); // AltGr arrives as Ctrl+Alt
        rig.Advance(600);
        Assert.Empty(rig.Calls);
        rig.ControlRelease();
    });

    [Fact]
    [Trait("Decision", "OD-0155")] // ED-NEW-session-hotkeys
    public void Control_and_a_digit_while_the_numbers_show_selects_and_hides_them() => headless.OnUiThread(() =>
    {
        using var rig = new SessionHotkeysRig();
        var numbers = new List<long>();
        rig.Window.SelectThreadNumber = numbers.Add;
        rig.ControlPress();
        rig.Advance(500);
        rig.Window.KeyPress(Key.NumPad3, RawInputModifiers.Control, PhysicalKey.NumPad3, null);
        Assert.Equal([3L], numbers);
        Assert.Equal([true, false], rig.Calls);
    });

    // ---- the number pad ------------------------------------------------------------------------------------------

    private static List<Key> Passed(AvaloniaWindow window)
    {
        var passed = new List<Key>();
        window.AddHandler(InputElement.KeyDownEvent, (_, e) => passed.Add(e.Key), RoutingStrategies.Tunnel);
        return passed;
    }

    [Fact]
    [Trait("Decision", "OD-0155")] // ED-NEW-session-hotkeys
    public void Control_and_each_number_pad_digit_with_Num_Lock_on_selects_that_thread_number() => headless.OnUiThread(() =>
    {
        var window = new ThreadWindow { Width = 400, Height = 300 };
        window.Show();
        var passed = Passed(window);
        var numbers = new List<long>();
        window.SelectThreadNumber = numbers.Add;
        Key[] pad = [Key.NumPad1, Key.NumPad2, Key.NumPad3, Key.NumPad4, Key.NumPad5, Key.NumPad6, Key.NumPad7, Key.NumPad8, Key.NumPad9];
        PhysicalKey[] physical = [PhysicalKey.NumPad1, PhysicalKey.NumPad2, PhysicalKey.NumPad3, PhysicalKey.NumPad4, PhysicalKey.NumPad5,
            PhysicalKey.NumPad6, PhysicalKey.NumPad7, PhysicalKey.NumPad8, PhysicalKey.NumPad9];
        for (var index = 0; index < pad.Length; index++)
        {
            window.KeyPress(pad[index], RawInputModifiers.Control, physical[index], null);
            window.KeyRelease(pad[index], RawInputModifiers.Control, physical[index], null);
        }
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([1L, 2L, 3L, 4L, 5L, 6L, 7L, 8L, 9L], numbers);
        Assert.Empty(passed);
        window.Close();
    });

    [Fact]
    [Trait("Decision", "OD-0155")] // ED-NEW-session-hotkeys
    public void With_Num_Lock_off_the_pad_keys_type_navigation_keys_and_keep_their_own_chords() => headless.OnUiThread(() =>
    {
        var window = new ThreadWindow { Width = 400, Height = 300 };
        window.Show();
        var passed = Passed(window);
        var numbers = new List<long>();
        window.SelectThreadNumber = numbers.Add;
        // Num Lock off: the pad's 1, 2, 3, 4, 6, 7, 8, 9 are End, Down, PageDown, Left, Right, Home, Up, PageUp.
        (Key Key, PhysicalKey Physical)[] navigation =
        [
            (Key.End, PhysicalKey.NumPad1), (Key.Down, PhysicalKey.NumPad2), (Key.PageDown, PhysicalKey.NumPad3),
            (Key.Left, PhysicalKey.NumPad4), (Key.Right, PhysicalKey.NumPad6), (Key.Home, PhysicalKey.NumPad7),
            (Key.Up, PhysicalKey.NumPad8), (Key.PageUp, PhysicalKey.NumPad9),
        ];
        foreach (var (key, physical) in navigation)
        {
            window.KeyPress(key, RawInputModifiers.Control, physical, null);
            window.KeyRelease(key, RawInputModifiers.Control, physical, null);
        }
        // The pad's 0 is not a thread number, as the row's 0 is not; Shift or Alt with a pad digit is another chord.
        window.KeyPress(Key.NumPad0, RawInputModifiers.Control, PhysicalKey.NumPad0, null);
        window.KeyPress(Key.NumPad2, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.NumPad2, null);
        window.KeyPress(Key.NumPad3, RawInputModifiers.Control | RawInputModifiers.Alt, PhysicalKey.NumPad3, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(numbers);
        Assert.Equal([.. navigation.Select(pair => pair.Key), Key.NumPad0, Key.NumPad2, Key.NumPad3], passed);
        window.Close();
    });
    [Fact]
    [Trait("Decision", "OD-0155")] // ED-NEW-session-hotkeys
    public void A_wheel_turn_hides_the_numbers_and_spends_the_hold_until_Control_is_released() => headless.OnUiThread(() =>
    {
        using var rig = new SessionHotkeysRig();
        rig.ControlPress();
        rig.Advance(500);
        Assert.True(rig.Shown);
        // The sidebar scrolled under the numbers (or Ctrl+wheel): the badges would sit on rows that moved. They go at once.
        rig.Window.MouseWheel(new Avalonia.Point(40, 40), new Avalonia.Vector(0, -1), RawInputModifiers.Control);
        Assert.False(rig.Shown);
        Assert.Equal([true, false], rig.Calls);
        rig.Advance(1000); // still held: the hold is spent, they do not come back
        Assert.False(rig.Shown);
        rig.ControlRelease();
        rig.ControlPress();
        rig.Advance(500);
        Assert.True(rig.Shown); // a fresh hold shows them again
        Assert.Equal([true, false, true], rig.Calls);

        // A wheel turn during the 500 ms wait spends it too.
        rig.ControlRelease();
        rig.ControlPress();
        rig.Advance(200);
        rig.Window.MouseWheel(new Avalonia.Point(40, 40), new Avalonia.Vector(0, 1), RawInputModifiers.Control);
        rig.Advance(600);
        Assert.False(rig.Shown);
    });

    [Fact]
    [Trait("Decision", "OD-0155")] // ED-NEW-session-hotkeys
    public void A_quick_release_and_press_never_leaves_the_safety_net_without_a_handle_or_with_two_loops() => headless.OnUiThread(() =>
    {
        using var rig = new SessionHotkeysRig();
        rig.ControlPress();
        Assert.True(rig.Window.HoldWatchState.Held);
        // Released and pressed again before the cancelled watcher's tail has run: that tail must not clear the new handle.
        rig.Window.KeyRelease(Key.LeftCtrl, RawInputModifiers.None, PhysicalKey.ControlLeft, null);
        rig.Window.KeyPress(Key.LeftCtrl, RawInputModifiers.Control, PhysicalKey.ControlLeft, null);
        Dispatcher.UIThread.RunJobs();
        rig.Advance(60); // every pending tail and one watch interval
        Assert.True(rig.Window.HoldWatchState.Held);
        Assert.Equal(1, rig.Window.HoldWatchState.Loops);
        // Pressed once more (a repeat) and released: the one watcher stops, and none is left behind.
        rig.Window.KeyPress(Key.LeftCtrl, RawInputModifiers.Control, PhysicalKey.ControlLeft, null);
        rig.ControlRelease();
        rig.Advance(120);
        Assert.Equal((false, 0), rig.Window.HoldWatchState);
        // The safety net still catches a lost key-up after all this (one loop, its handle held).
        rig.ControlPress();
        rig.Advance(500);
        Assert.True(rig.Shown);
        rig.ControlDown = false; // the key-up went elsewhere
        rig.Advance(50);
        Assert.False(rig.Shown);
        rig.Advance(60);
        Assert.Equal((false, 0), rig.Window.HoldWatchState);
    });
}
