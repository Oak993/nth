using Avalonia.Input;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using static Marrow.App.Tests.Fixtures.AppCommandsRig;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/AppCommands.swift (B3 52bc792), :53-62, the four zoom items, each a literal key equivalent
// (".keyboardShortcut("+", modifiers: .command)", "-", "0" and the hidden "="), which a SwiftUI menu matches by the
// character the pressed key types on the active keyboard layout. ED-270 (Q-ZOOM): the port matches them that way
// (Port/MenuKeyEquivalents.Typed, Port/KeyboardLayoutText.cs). The layouts below are tables of what each key types
// (Fixtures/KeyboardLayouts.cs), so no test depends on the PC's own layout. These tests press real key events through the
// production dispatch (MenuKeyEquivalents.Attach with AppCommands.PerformKeyDown, the call WindowManager.MakeWindow makes).
// Each is one clause of the zoom records (PR-WIN-222 to PR-WIN-225, UI-WIN-074, UI-WIN-077: "a character key equivalent,
// not a US key position"); the title, the steps, the persistence, the ends and the order are proved in AppCommandsTests.
[Collection("Headless")]
public sealed class AppCommandsZoomTests(HeadlessSession headless)
{
    private const RawInputModifiers Ctrl = RawInputModifiers.Control;
    private const RawInputModifiers CtrlShift = RawInputModifiers.Control | RawInputModifiers.Shift;

    // A window with the production dispatch and a model at 100, pressed on the layout the caller has overridden.
    private static (AppModel Model, AvaloniaWindow Window, List<Key> Passed, IDisposable Cleanup) Rig(AppModelHarness h)
    {
        var shared = ShortcutStore.Shared.Override(FreshStore());
        h.WriteLibrary([], []);
        var model = new AppModel();
        var commands = new AppCommands(model);
        var window = Show(new AvaloniaWindow());
        var passed = Passed(window);
        var attached = MenuKeyEquivalents.Attach(window, commands.PerformKeyDown);
        model.Settings.AppZoom = 100;
        return (model, window, passed, new Cleanup(() => { attached.Dispose(); window.Close(); shared.Dispose(); }));
    }

    private sealed class Cleanup(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }

    [Fact]
    [Trait("Decision", "ED-270")]
    [Trait("Rule", "R-TEST-4")] // partial for PR-WIN-222, PR-WIN-223, PR-WIN-224, PR-WIN-225, UI-WIN-074, UI-WIN-077: the "character, not position" clause, German layout
    public void On_a_German_keyboard_the_keys_that_type_plus_minus_and_zero_zoom_and_the_key_at_the_US_minus_position_does_not() => headless.OnUiThread(() =>
    {
        using var layout = KeyboardLayoutText.Current.Override(KeyboardLayouts.German);
        using var h = new AppModelHarness(withRuntime: false);
        var (model, window, passed, cleanup) = Rig(h);
        using var _ = cleanup;

        // AppCommands.swift:53-54: the key that types "+" (German: its own key, at the US "]" position) zooms in.
        Press(window, Key.OemPlus, PhysicalKey.BracketRight, Ctrl);
        Assert.Equal(110, model.Settings.AppZoom);
        Assert.Empty(passed);
        // :55-56: the key that types "-" (German: at the US "/" position) zooms out.
        Press(window, Key.OemMinus, PhysicalKey.Slash, Ctrl);
        Assert.Equal(100, model.Settings.AppZoom);
        Assert.Empty(passed);

        // The key at the US "-" position types "ß", and the one at the US "=" position is a dead accent: neither types a
        // zoom character, so the chord passes the dispatch unconsumed (ED-270: the build was changed to read what is typed).
        Press(window, Key.OemMinus, PhysicalKey.Minus, Ctrl);
        Press(window, Key.OemPlus, PhysicalKey.Equal, Ctrl);
        Assert.Equal(100, model.Settings.AppZoom);
        Assert.Equal([Key.OemMinus, Key.OemPlus], passed);
        passed.Clear();

        // Shift with the "+" key types "*", which no item holds.
        Press(window, Key.OemPlus, PhysicalKey.BracketRight, CtrlShift);
        Assert.Equal(100, model.Settings.AppZoom);
        Assert.Equal([Key.OemPlus], passed);
        passed.Clear();

        // :57-58: the "0" key types "0", from any level.
        model.Settings.AppZoom = 150;
        Press(window, Key.D0, PhysicalKey.Digit0, Ctrl);
        Assert.Equal(100, model.Settings.AppZoom);
        Assert.Empty(passed);

        // The keypad's + and - type those characters on every layout (ED-230, ED-270).
        Press(window, Key.Add, PhysicalKey.NumPadAdd, Ctrl);
        Assert.Equal(110, model.Settings.AppZoom);
        Press(window, Key.Subtract, PhysicalKey.NumPadSubtract, Ctrl);
        Assert.Equal(100, model.Settings.AppZoom);
        Assert.Empty(passed);
    });

    [Fact]
    [Trait("Decision", "ED-270")]
    [Trait("Rule", "R-TEST-4")] // partial for PR-WIN-222, PR-WIN-223, PR-WIN-224, PR-WIN-225, UI-WIN-074, UI-WIN-077: the "character, not position" clause, US layout
    public void On_a_US_keyboard_equals_and_shift_equals_zoom_in_and_shift_with_the_minus_and_zero_keys_does_not_zoom() => headless.OnUiThread(() =>
    {
        using var layout = KeyboardLayoutText.Current.Override(KeyboardLayouts.Us);
        using var h = new AppModelHarness(withRuntime: false);
        var (model, window, passed, cleanup) = Rig(h);
        using var _ = cleanup;

        // :59-62: "US keyboards produce '=' for Command-plus without Shift", the hidden item answers it.
        Press(window, Key.OemPlus, PhysicalKey.Equal, Ctrl);
        Assert.Equal(110, model.Settings.AppZoom);
        // :53-54: with Shift the key types "+", and Shift is part of typing it, so the chord is Ctrl with "+".
        Press(window, Key.OemPlus, PhysicalKey.Equal, CtrlShift);
        Assert.Equal(125, model.Settings.AppZoom);
        Assert.Empty(passed);

        // With Shift the "-" key types "_" and the "0" key ")": a different character, and a Shift the items do not take.
        Press(window, Key.OemMinus, PhysicalKey.Minus, CtrlShift);
        Press(window, Key.D0, PhysicalKey.Digit0, CtrlShift);
        Assert.Equal(125, model.Settings.AppZoom);
        Assert.Equal([Key.OemMinus, Key.D0], passed);
    });

    [Fact]
    [Trait("Decision", "ED-270")]
    [Trait("Rule", "R-TEST-4")] // partial for PR-WIN-222, PR-WIN-223, PR-WIN-224, PR-WIN-225, UI-WIN-074, UI-WIN-077: the "character, not position" clause, French layout
    public void On_a_French_keyboard_the_zero_is_Shift_with_its_key_and_the_minus_sits_where_the_US_six_does_and_still_zooms_out() => headless.OnUiThread(() =>
    {
        using var layout = KeyboardLayoutText.Current.Override(KeyboardLayouts.French);
        using var h = new AppModelHarness(withRuntime: false);
        var (model, window, passed, cleanup) = Rig(h);
        using var _ = cleanup;

        // The number row types "-" plain on the US "6" position: Zoom Out (:55-56) comes before "Thread 6" in the body
        // (:95-98), as a menu takes the first item holding the chord, so the thread selection is not asked.
        Press(window, Key.D6, PhysicalKey.Digit6, Ctrl);
        Assert.Equal(90, model.Settings.AppZoom);
        Assert.Empty(passed);

        // "0" is Shift with that key, and Shift is part of typing it: Actual Size (:57-58). Plain, the key types "à".
        Press(window, Key.D0, PhysicalKey.Digit0, Ctrl);
        Assert.Equal(90, model.Settings.AppZoom);
        Assert.Equal([Key.D0], passed);
        passed.Clear();
        Press(window, Key.D0, PhysicalKey.Digit0, CtrlShift);
        Assert.Equal(100, model.Settings.AppZoom);
        Assert.Empty(passed);

        // "=" is at the US "=" position, "+" is Shift with it.
        Press(window, Key.OemPlus, PhysicalKey.Equal, Ctrl);
        Press(window, Key.OemPlus, PhysicalKey.Equal, CtrlShift);
        Assert.Equal(125, model.Settings.AppZoom);
        Assert.Empty(passed);
    });

    [Fact]
    [Trait("Decision", "ED-270")]
    [Trait("Rule", "R-TEST-4")] // partial for the zoom records: the reader of the active layout (Port/KeyboardLayoutText.cs) on this machine
    public void The_Windows_layout_reader_gives_one_character_for_a_letter_key_none_for_a_key_outside_the_table_and_leaves_the_keyboard_state_alone() => headless.OnUiThread(() =>
    {
        var reader = KeyboardLayoutText.Current.Current;
        // The default is the Windows reader unless the headless session installed the US table (Fixtures/KeyboardLayouts.cs);
        // construct the Windows one directly, which reads this PC's active layout whatever it is.
        var windows = (IKeyboardLayoutText)Activator.CreateInstance(
            typeof(MenuKeyEquivalents).Assembly.GetType("Marrow.App.Port.WindowsKeyboardLayoutText")!, nonPublic: true)!;
        Assert.NotNull(reader);
        var plain = windows.Typed(PhysicalKey.K, false);
        var shifted = windows.Typed(PhysicalKey.K, true);
        Assert.Equal(1, plain?.Length);
        Assert.Equal(1, shifted?.Length);
        Assert.Equal(plain, windows.Typed(PhysicalKey.K, false)); // a second read is the same: no pending state was consumed
        Assert.Null(windows.Typed(PhysicalKey.Home, false)); // an extended key types no character
        Assert.Null(windows.Typed(PhysicalKey.None, false)); // a key outside the table
    });
}
