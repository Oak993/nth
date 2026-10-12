using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Window;

// Source: DroppyCode/App/Window/ThreadWindow.swift (B2 1fbf81e), :1-65.
// Key codes follow ED-099 (1)-(2): the macOS codes of the Swift become Set-1 scan codes (18..25 the digits 1 to 9 as
// 0x02..0x0A, 48 Tab 0x0F, 53 Esc 0x01, 13 W 0x11), and Command becomes Control (ED-099 (3)). The headless keyboard
// delivers them as Avalonia physical keys, which is what the window reads.
// Every probe is a tunnel handler on the window, which runs after the window's own class handler, so it is reached
// only when the window returned the event unconsumed ("follows its normal route").
[Collection("Headless")]
public sealed class ThreadWindowTests(HeadlessSession headless)
{
    // ThreadWindow.swift:23 the number row, in digit order; ED-099 (2): 1 0x02 ... 9 0x0A.
    private static readonly (PhysicalKey Physical, Key Key, long Number)[] Digits =
    [
        (PhysicalKey.Digit1, Key.D1, 1), (PhysicalKey.Digit2, Key.D2, 2), (PhysicalKey.Digit3, Key.D3, 3),
        (PhysicalKey.Digit4, Key.D4, 4), (PhysicalKey.Digit5, Key.D5, 5), (PhysicalKey.Digit6, Key.D6, 6),
        (PhysicalKey.Digit7, Key.D7, 7), (PhysicalKey.Digit8, Key.D8, 8), (PhysicalKey.Digit9, Key.D9, 9),
    ];

    private static T Show<T>(T window) where T : AvaloniaWindow
    {
        window.Width = 400;
        window.Height = 300;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static List<Key> Passed(AvaloniaWindow window)
    {
        var passed = new List<Key>();
        window.AddHandler(InputElement.KeyDownEvent, (_, e) => passed.Add(e.Key), RoutingStrategies.Tunnel);
        return passed;
    }

    private static void Press(AvaloniaWindow window, Key key, PhysicalKey physical, RawInputModifiers modifiers)
    {
        window.KeyPress(key, modifiers, physical, null);
        if (window.IsVisible) window.KeyRelease(key, modifiers, physical, null); // a closed window takes no more input
        Dispatcher.UIThread.RunJobs();
    }

    // ShortcutStore.swift's `private init()` reads UserDefaults once; the runtime's in-memory defaults are empty, so
    // every chord starts at its default (Shortcuts.swift:222-223 Control-Tab and Control-Shift-Tab).
    private static void WithShortcuts(Action<ShortcutStore> body)
    {
        using var runtime = new TestRuntime();
        using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var store = (ShortcutStore)Activator.CreateInstance(typeof(ShortcutStore), nonPublic: true)!;
        using var shared = ShortcutStore.Shared.Override(store);
        body(store);
    }

    // ---- PR-WIN-154: Command-1 to Command-9 (Ctrl on Windows) --------------------------------------------------

    [Fact]
    [Trait("Req", "PR-WIN-154")] // App/Window/ThreadWindow.swift:8-23
    public void Control_and_each_number_row_digit_selects_that_thread_number_and_swallows_the_event() => headless.OnUiThread(() =>
    {
        var window = Show(new ThreadWindow());
        var passed = Passed(window);
        var numbers = new List<long>();
        window.SelectThreadNumber = numbers.Add;
        foreach (var (physical, key, _) in Digits) Press(window, key, physical, RawInputModifiers.Control);
        Assert.Equal([1L, 2L, 3L, 4L, 5L, 6L, 7L, 8L, 9L], numbers);
        Assert.Empty(passed);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-154")] // App/Window/ThreadWindow.swift:23
    public void The_key_code_crossover_keeps_digit_5_as_5_and_digit_6_as_6() => headless.OnUiThread(() =>
    {
        var window = Show(new ThreadWindow());
        var numbers = new List<long>();
        window.SelectThreadNumber = numbers.Add;
        Press(window, Key.D6, PhysicalKey.Digit6, RawInputModifiers.Control);
        Press(window, Key.D5, PhysicalKey.Digit5, RawInputModifiers.Control);
        Assert.Equal([6L, 5L], numbers);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-154")] // App/Window/ThreadWindow.swift:13
    public void Any_modifier_besides_Control_returns_the_event_unchanged() => headless.OnUiThread(() =>
    {
        var window = Show(new ThreadWindow());
        var passed = Passed(window);
        var numbers = new List<long>();
        window.SelectThreadNumber = numbers.Add;
        Press(window, Key.D1, PhysicalKey.Digit1, RawInputModifiers.None);
        Press(window, Key.D2, PhysicalKey.Digit2, RawInputModifiers.Control | RawInputModifiers.Shift);
        Press(window, Key.D3, PhysicalKey.Digit3, RawInputModifiers.Control | RawInputModifiers.Alt);
        Press(window, Key.D4, PhysicalKey.Digit4, RawInputModifiers.Alt);
        Press(window, Key.D5, PhysicalKey.Digit5, RawInputModifiers.Shift);
        Assert.Empty(numbers);
        Assert.Equal([Key.D1, Key.D2, Key.D3, Key.D4, Key.D5], passed);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-154")] // App/Window/ThreadWindow.swift:13
    public void The_Windows_key_is_masked_out_like_fn_so_Control_Windows_digit_still_selects() => headless.OnUiThread(() =>
    {
        // Shortcuts.swift:6 allowedModifiers leaves fn and Caps Lock out; ED-099 (3) masks the Windows key the same way.
        var window = Show(new ThreadWindow());
        var passed = Passed(window);
        var numbers = new List<long>();
        window.SelectThreadNumber = numbers.Add;
        Press(window, Key.D7, PhysicalKey.Digit7, RawInputModifiers.Control | RawInputModifiers.Meta);
        Assert.Equal([7L], numbers);
        Assert.Empty(passed);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-154")] // App/Window/ThreadWindow.swift:14, :23
    public void A_key_outside_the_nine_number_row_digits_is_returned_unchanged() => headless.OnUiThread(() =>
    {
        // The map holds 18-26 and 28 but not 29 (digit 0). PORT-ONLY (ED-NEW-session-hotkeys, owner 2026-10-11): the number
        // pad's 1 to 9 now select as well (SessionHotkeysWindowTests); its 0, and its 1 with Num Lock off (End), still pass.
        var window = Show(new ThreadWindow());
        var passed = Passed(window);
        var numbers = new List<long>();
        window.SelectThreadNumber = numbers.Add;
        Press(window, Key.D0, PhysicalKey.Digit0, RawInputModifiers.Control);
        Press(window, Key.NumPad0, PhysicalKey.NumPad0, RawInputModifiers.Control);
        Press(window, Key.End, PhysicalKey.NumPad1, RawInputModifiers.Control);
        Press(window, Key.A, PhysicalKey.A, RawInputModifiers.Control);
        Assert.Empty(numbers);
        Assert.Equal([Key.D0, Key.NumPad0, Key.End, Key.A], passed);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-154")] // App/Window/ThreadWindow.swift:12
    public void With_a_sheet_attached_the_digit_chord_is_returned_unchanged() => headless.OnUiThread(() =>
    {
        var window = Show(new ThreadWindow());
        var passed = Passed(window);
        var numbers = new List<long>();
        window.SelectThreadNumber = numbers.Add;
        window.AttachedSheet = new AvaloniaWindow();
        Press(window, Key.D1, PhysicalKey.Digit1, RawInputModifiers.Control);
        Assert.Empty(numbers);
        Assert.Equal([Key.D1], passed);
        window.AttachedSheet = null;
        Press(window, Key.D1, PhysicalKey.Digit1, RawInputModifiers.Control);
        Assert.Equal([1L], numbers);
        Assert.Equal([Key.D1], passed);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-154")] // App/Window/ThreadWindow.swift:8-19
    public void Without_the_digit_closure_the_chord_is_returned_unchanged() => headless.OnUiThread(() =>
    {
        var never = Show(new ThreadWindow());
        var neverPassed = Passed(never);
        Press(never, Key.D1, PhysicalKey.Digit1, RawInputModifiers.Control);
        Assert.Equal([Key.D1], neverPassed);
        never.Close();

        // Installed, then cleared: the monitor stays but `let selectThreadNumber` fails (:14).
        var cleared = Show(new ThreadWindow());
        var clearedPassed = Passed(cleared);
        var numbers = new List<long>();
        cleared.SelectThreadNumber = numbers.Add;
        cleared.SelectThreadNumber = null;
        Press(cleared, Key.D2, PhysicalKey.Digit2, RawInputModifiers.Control);
        Assert.Empty(numbers);
        Assert.Equal([Key.D2], clearedPassed);
        cleared.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-154")] // App/Window/ThreadWindow.swift:12
    public void A_digit_chord_in_another_window_reaches_only_that_window() => headless.OnUiThread(() =>
    {
        var first = Show(new ThreadWindow());
        var second = Show(new ThreadWindow());
        var firstNumbers = new List<long>();
        var secondNumbers = new List<long>();
        first.SelectThreadNumber = firstNumbers.Add;
        second.SelectThreadNumber = secondNumbers.Add;
        Press(second, Key.D8, PhysicalKey.Digit8, RawInputModifiers.Control);
        Assert.Empty(firstNumbers);
        Assert.Equal([8L], secondNumbers);
        first.Close();
        second.Close();
    });

    // ---- PR-WIN-155: the monitor is installed once -------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-WIN-155")] // App/Window/ThreadWindow.swift:8-20
    public void Assigning_the_digit_closure_again_installs_no_second_monitor_so_the_chord_fires_once() => headless.OnUiThread(() =>
    {
        var window = Show(new ThreadWindow());
        var passed = Passed(window);
        var calls = new List<string>();
        window.SelectThreadNumber = n => calls.Add($"first {n}");
        window.SelectThreadNumber = n => calls.Add($"second {n}");
        window.SelectThreadNumber = n => calls.Add($"third {n}");
        Press(window, Key.D3, PhysicalKey.Digit3, RawInputModifiers.Control);
        Assert.Equal(["third 3"], calls);
        Assert.Empty(passed);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-155")] // App/Window/ThreadWindow.swift:8-20
    public void The_monitor_installed_by_a_nil_assignment_is_kept_and_serves_a_later_closure() => headless.OnUiThread(() =>
    {
        // didSet runs on every assignment, nil included; the one monitor reads the closure live (:14).
        var window = Show(new ThreadWindow());
        var passed = Passed(window);
        window.SelectThreadNumber = null;
        Press(window, Key.D4, PhysicalKey.Digit4, RawInputModifiers.Control);
        var numbers = new List<long>();
        window.SelectThreadNumber = numbers.Add;
        Press(window, Key.D4, PhysicalKey.Digit4, RawInputModifiers.Control);
        window.SelectThreadNumber = null;
        window.SelectThreadNumber = numbers.Add;
        Press(window, Key.D9, PhysicalKey.Digit9, RawInputModifiers.Control);
        Assert.Equal([4L, 9L], numbers);
        Assert.Equal([Key.D4], passed);
        window.Close();
    });

    // ---- PR-WIN-156: Tab chords for next and previous thread ---------------------------------------------------

    [Fact]
    [Trait("Req", "PR-WIN-156")] // App/Window/ThreadWindow.swift:25-38
    public void Control_Tab_selects_the_next_thread_and_Control_Shift_Tab_the_previous_and_both_are_consumed() => headless.OnUiThread(() => WithShortcuts(_ =>
    {
        var window = Show(new ThreadWindow());
        var passed = Passed(window);
        var offsets = new List<long>();
        window.SelectThread = offsets.Add;
        Press(window, Key.Tab, PhysicalKey.Tab, RawInputModifiers.Control);
        Press(window, Key.Tab, PhysicalKey.Tab, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.Equal([1L, -1L], offsets);
        Assert.Empty(passed);
        window.Close();
    }));

    [Fact]
    [Trait("Req", "PR-WIN-156")] // App/Window/ThreadWindow.swift:28-35
    public void A_matching_Tab_chord_is_consumed_even_without_the_select_closure() => headless.OnUiThread(() => WithShortcuts(_ =>
    {
        // `selectThread?(1); return true`: the optional call does nothing and the event is still taken.
        var window = Show(new ThreadWindow());
        var passed = Passed(window);
        Press(window, Key.Tab, PhysicalKey.Tab, RawInputModifiers.Control);
        Press(window, Key.Tab, PhysicalKey.Tab, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.Empty(passed);
        window.Close();
    }));

    [Fact]
    [Trait("Req", "PR-WIN-156")] // App/Window/ThreadWindow.swift:26-37
    public void A_Tab_chord_matching_neither_command_is_returned_unchanged() => headless.OnUiThread(() => WithShortcuts(_ =>
    {
        var window = Show(new ThreadWindow());
        var passed = Passed(window);
        var offsets = new List<long>();
        window.SelectThread = offsets.Add;
        Press(window, Key.Tab, PhysicalKey.Tab, RawInputModifiers.Control | RawInputModifiers.Alt);
        Press(window, Key.Tab, PhysicalKey.Tab, RawInputModifiers.Alt);
        Assert.Empty(offsets);
        Assert.Equal([Key.Tab, Key.Tab], passed);
        window.Close();
    }));

    [Fact]
    [Trait("Req", "PR-WIN-156")] // App/Window/ThreadWindow.swift:27-32
    public void The_chords_are_read_live_so_a_remapped_Tab_chord_is_honored() => headless.OnUiThread(() => WithShortcuts(store =>
    {
        var window = Show(new ThreadWindow());
        var passed = Passed(window);
        var offsets = new List<long>();
        window.SelectThread = offsets.Add;
        store.Set(new KeyChord(0x0F, ChordModifiers.Control | ChordModifiers.Alt), AppShortcut.NextThread);
        store.Set(new KeyChord(0x0F, ChordModifiers.Alt | ChordModifiers.Shift), AppShortcut.PreviousThread);
        Press(window, Key.Tab, PhysicalKey.Tab, RawInputModifiers.Control | RawInputModifiers.Alt);
        Press(window, Key.Tab, PhysicalKey.Tab, RawInputModifiers.Alt | RawInputModifiers.Shift);
        Press(window, Key.Tab, PhysicalKey.Tab, RawInputModifiers.Control);
        Assert.Equal([1L, -1L], offsets);
        Assert.Equal([Key.Tab], passed);
        window.Close();
    }));

    [Fact]
    [Trait("Req", "PR-WIN-156")] // App/Window/ThreadWindow.swift:26
    public void A_next_thread_chord_rebound_off_Tab_is_not_taken_by_the_window() => headless.OnUiThread(() => WithShortcuts(store =>
    {
        // Rebound to Control-J (macOS 38, scan 0x24): the key-code gate sends it on to the ordinary route.
        var window = Show(new ThreadWindow());
        var passed = Passed(window);
        var offsets = new List<long>();
        window.SelectThread = offsets.Add;
        store.Set(new KeyChord(0x24, ChordModifiers.Control), AppShortcut.NextThread);
        Press(window, Key.J, PhysicalKey.J, RawInputModifiers.Control);
        Press(window, Key.Tab, PhysicalKey.Tab, RawInputModifiers.Control);
        Assert.Empty(offsets);
        Assert.Equal([Key.J, Key.Tab], passed);
        window.Close();
    }));

    [Fact]
    [Trait("Req", "PR-WIN-156")] // App/Window/ThreadWindow.swift:28-35
    public void When_both_commands_share_a_Tab_chord_next_wins() => headless.OnUiThread(() => WithShortcuts(store =>
    {
        var window = Show(new ThreadWindow());
        var offsets = new List<long>();
        window.SelectThread = offsets.Add;
        store.Set(new KeyChord(0x0F, ChordModifiers.Control), AppShortcut.PreviousThread);
        Press(window, Key.Tab, PhysicalKey.Tab, RawInputModifiers.Control);
        Assert.Equal([1L], offsets);
        window.Close();
    }));

    [Fact]
    [Trait("Req", "PR-WIN-156")] // App/Window/ThreadWindow.swift:26
    public void With_a_sheet_attached_the_Tab_chords_are_returned_unchanged() => headless.OnUiThread(() => WithShortcuts(_ =>
    {
        var window = Show(new ThreadWindow());
        var passed = Passed(window);
        var offsets = new List<long>();
        window.SelectThread = offsets.Add;
        window.AttachedSheet = new AvaloniaWindow();
        Press(window, Key.Tab, PhysicalKey.Tab, RawInputModifiers.Control);
        Press(window, Key.Tab, PhysicalKey.Tab, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.Empty(offsets);
        Assert.Equal([Key.Tab, Key.Tab], passed);
        window.Close();
    }));

    [Fact]
    [Trait("Req", "PR-WIN-156")] // App/Window/ThreadWindow.swift:25-38
    public void The_Tab_chord_is_taken_before_a_focused_control_sees_it() => headless.OnUiThread(() => WithShortcuts(_ =>
    {
        // performKeyEquivalent runs before the first responder's keyDown, so a focused field never gets Control-Tab.
        var field = new TextBox();
        var window = Show(new ThreadWindow { Content = field });
        field.Focus();
        var fieldSaw = new List<Key>();
        field.AddHandler(InputElement.KeyDownEvent, (_, e) => fieldSaw.Add(e.Key), RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        var offsets = new List<long>();
        window.SelectThread = offsets.Add;
        Press(window, Key.Tab, PhysicalKey.Tab, RawInputModifiers.Control);
        Assert.Equal([1L], offsets);
        Assert.Empty(fieldSaw);
        window.Close();
    }));

    // ---- PR-WIN-157: Escape closes a secondary window ----------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-WIN-157")] // App/Window/ThreadWindow.swift:48-56
    public void Escape_with_no_modifier_closes_the_secondary_window_and_is_consumed() => headless.OnUiThread(() =>
    {
        var window = Show(new SecondaryWindow());
        var passed = Passed(window);
        var closed = 0;
        window.Closed += (_, _) => closed++;
        Press(window, Key.Escape, PhysicalKey.Escape, RawInputModifiers.None);
        Assert.Equal(1, closed);
        Assert.False(window.IsVisible);
        Assert.Empty(passed);
    });

    [Fact]
    [Trait("Req", "PR-WIN-157")] // App/Window/ThreadWindow.swift:50
    public void Escape_with_any_modifier_is_not_taken() => headless.OnUiThread(() =>
    {
        var window = Show(new SecondaryWindow());
        var passed = Passed(window);
        var closed = 0;
        window.Closed += (_, _) => closed++;
        Press(window, Key.Escape, PhysicalKey.Escape, RawInputModifiers.Shift);
        Press(window, Key.Escape, PhysicalKey.Escape, RawInputModifiers.Control);
        Press(window, Key.Escape, PhysicalKey.Escape, RawInputModifiers.Alt);
        Assert.Equal(0, closed);
        Assert.True(window.IsVisible);
        Assert.Equal([Key.Escape, Key.Escape, Key.Escape], passed);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-157")] // App/Window/ThreadWindow.swift:50
    public void Escape_with_only_the_masked_Windows_key_still_closes() => headless.OnUiThread(() =>
    {
        // intersection(allowedModifiers).isEmpty: fn is outside the set on macOS, the Windows key here (ED-099 (3)).
        var window = Show(new SecondaryWindow());
        var closed = 0;
        window.Closed += (_, _) => closed++;
        Press(window, Key.Escape, PhysicalKey.Escape, RawInputModifiers.Meta);
        Assert.Equal(1, closed);
    });

    [Fact]
    [Trait("Req", "PR-WIN-157")] // App/Window/ThreadWindow.swift:51
    public void A_focused_text_field_keeps_its_Escape_and_the_window_stays_open() => headless.OnUiThread(() =>
    {
        var field = new TextBox();
        var window = Show(new SecondaryWindow { Content = field });
        field.Focus();
        var fieldSaw = new List<Key>();
        field.AddHandler(InputElement.KeyDownEvent, (_, e) => fieldSaw.Add(e.Key), RoutingStrategies.Tunnel);
        var closed = 0;
        window.Closed += (_, _) => closed++;
        Press(window, Key.Escape, PhysicalKey.Escape, RawInputModifiers.None);
        Assert.Equal(0, closed);
        Assert.Equal([Key.Escape], fieldSaw);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-157")] // App/Window/ThreadWindow.swift:48-54
    public void Escape_is_taken_before_a_focused_control_that_is_not_a_text_field_sees_it() => headless.OnUiThread(() =>
    {
        // sendEvent runs before the hosting view: a focused non-text control never gets the Escape.
        var control = new Border { Focusable = true };
        var window = Show(new SecondaryWindow { Content = control });
        control.Focus();
        var controlSaw = new List<Key>();
        control.AddHandler(InputElement.KeyDownEvent, (_, e) => controlSaw.Add(e.Key), RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        var closed = 0;
        window.Closed += (_, _) => closed++;
        Press(window, Key.Escape, PhysicalKey.Escape, RawInputModifiers.None);
        Assert.Equal(1, closed);
        Assert.Empty(controlSaw);
    });

    // ---- PR-WIN-158: Command-W (Ctrl+W) closes a secondary window ---------------------------------------------

    [Fact]
    [Trait("Req", "PR-WIN-158")] // App/Window/ThreadWindow.swift:58-64
    public void Control_W_closes_the_secondary_window_and_is_consumed() => headless.OnUiThread(() =>
    {
        var window = Show(new SecondaryWindow());
        var passed = Passed(window);
        var closed = 0;
        window.Closed += (_, _) => closed++;
        Press(window, Key.W, PhysicalKey.W, RawInputModifiers.Control);
        Assert.Equal(1, closed);
        Assert.False(window.IsVisible);
        Assert.Empty(passed);
    });

    [Fact]
    [Trait("Req", "PR-WIN-158")] // App/Window/ThreadWindow.swift:59
    public void Only_the_exact_Control_W_chord_closes() => headless.OnUiThread(() =>
    {
        var window = Show(new SecondaryWindow());
        var passed = Passed(window);
        var closed = 0;
        window.Closed += (_, _) => closed++;
        Press(window, Key.W, PhysicalKey.W, RawInputModifiers.None);
        Press(window, Key.W, PhysicalKey.W, RawInputModifiers.Control | RawInputModifiers.Shift);
        Press(window, Key.W, PhysicalKey.W, RawInputModifiers.Control | RawInputModifiers.Alt);
        Press(window, Key.Q, PhysicalKey.Q, RawInputModifiers.Control);
        Assert.Equal(0, closed);
        Assert.Equal([Key.W, Key.W, Key.W, Key.Q], passed);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-WIN-158")] // App/Window/ThreadWindow.swift:59
    public void Control_W_is_hard_coded_and_ignores_the_archive_command_chord() => headless.OnUiThread(() => WithShortcuts(store =>
    {
        // Archive thread defaults to Command-W (Shortcuts.swift:226); rebinding it to Control-E (macOS 14, scan 0x12)
        // changes nothing here: Control-W still closes, Control-E does not.
        store.Set(new KeyChord(0x12, ChordModifiers.Control), AppShortcut.ArchiveThread);
        var window = Show(new SecondaryWindow());
        var passed = Passed(window);
        var closed = 0;
        window.Closed += (_, _) => closed++;
        Press(window, Key.E, PhysicalKey.E, RawInputModifiers.Control);
        Assert.Equal(0, closed);
        Assert.Equal([Key.E], passed);
        Press(window, Key.W, PhysicalKey.W, RawInputModifiers.Control);
        Assert.Equal(1, closed);
    }));

    [Fact]
    [Trait("Req", "PR-WIN-158")] // App/Window/ThreadWindow.swift:59
    public void With_a_sheet_attached_Control_W_is_returned_unchanged() => headless.OnUiThread(() =>
    {
        var window = Show(new SecondaryWindow());
        var passed = Passed(window);
        var closed = 0;
        window.Closed += (_, _) => closed++;
        window.AttachedSheet = new AvaloniaWindow();
        Press(window, Key.W, PhysicalKey.W, RawInputModifiers.Control);
        Assert.Equal(0, closed);
        Assert.Equal([Key.W], passed);
        window.AttachedSheet = null;
        Press(window, Key.W, PhysicalKey.W, RawInputModifiers.Control);
        Assert.Equal(1, closed);
    });

    [Fact]
    [Trait("Req", "PR-WIN-158")] // App/Window/ThreadWindow.swift:59
    public void Control_W_closes_even_while_a_text_field_has_focus() => headless.OnUiThread(() =>
    {
        // Unlike Escape (:51), the key equivalent has no first-responder exclusion.
        var field = new TextBox();
        var window = Show(new SecondaryWindow { Content = field });
        field.Focus();
        var closed = 0;
        window.Closed += (_, _) => closed++;
        Press(window, Key.W, PhysicalKey.W, RawInputModifiers.Control);
        Assert.Equal(1, closed);
    });
}
