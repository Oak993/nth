using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Window;
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
        return (model, window, passed, new DisposeAction(() => { attached.Dispose(); window.Close(); shared.Dispose(); }));
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

        // The number row types "-" plain on the US "6" position. In this bare window, as in the Settings and tour windows,
        // Zoom Out (:55-56) comes before "Thread 6" in the body (:95-98), as a menu takes the first item holding the chord.
        // The main window is different: its ThreadWindow takes Ctrl with the number row by position first (ThreadWindow.swift:11-23),
        // so there the same chord selects Thread 6 (the production-window test below).
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

    // The fixture tables above are only an honest stand-in for a layout if Windows types what they say. This reads the
    // production reader (Port/KeyboardLayoutText.cs) over the US, German and French layouts (Fixtures/WindowsLayoutReaders.cs
    // loads each by its identifier, without activating it) for every key a table names, plain and with Shift, and so is
    // independent of the PC's own layout. A reader that drops Shift, or that reads a fixed US table, fails it.
    [Fact]
    [Trait("Decision", "ED-270")]
    [Trait("Rule", "R-TEST-4")] // partial for the zoom records: the production reader's character reading, with and without Shift, on three layouts
    public void The_Windows_reader_over_the_US_German_and_French_layouts_types_what_the_fixture_tables_say_plain_and_with_Shift() => headless.OnUiThread(() =>
    {
        using var layouts = new WindowsLayoutReaders();
        foreach (var (name, reader, table) in new[]
        {
            ("US", layouts.Us, KeyboardLayouts.Us),
            ("German", layouts.German, KeyboardLayouts.German),
            ("French", layouts.French, KeyboardLayouts.French),
        })
        {
            foreach (var key in table.Keys)
            {
                foreach (var shift in new[] { false, true })
                {
                    var expected = table.Typed(key, shift);
                    var actual = reader.Typed(key, shift);
                    Assert.True(expected == actual, $"{name} layout, key {key}, Shift {shift}: the table says {expected ?? "no character"}, Windows types {actual ?? "no character"}");
                }
            }
        }
    });

    // The production reader on the keys the zoom chords sit on, with the characters written out (not read from a table): the
    // US "=", "-", "0", "]" and "/" positions on the real German and French layouts, plain and with Shift. A reader over a fixed
    // US table types "=", "-", "0", "]", "/" on all of them and fails every German and French row here; one that ignores
    // Shift fails the shifted ones. A dead key (the German "=" position, a dead acute accent) types no character.
    // The expected characters are those of the Windows layouts KBDGR (00000407) and KBDFR (0000040C); this test reads them.
    [Fact]
    [Trait("Decision", "ED-270")]
    [Trait("Rule", "R-TEST-4")] // partial for the zoom records: the production reader's character for five zoom-chord keys on real non-US layouts
    public void The_Windows_reader_types_the_characters_Windows_gives_the_Equal_Minus_Digit0_BracketRight_and_Slash_keys_on_the_German_and_French_layouts_plain_and_with_Shift() => headless.OnUiThread(() =>
    {
        using var layouts = new WindowsLayoutReaders();
        (string Layout, IKeyboardLayoutText Reader, PhysicalKey Key, string? Plain, string? Shifted)[] rows =
        [
            ("US", layouts.Us, PhysicalKey.Equal, "=", "+"),
            ("US", layouts.Us, PhysicalKey.Minus, "-", "_"),
            ("US", layouts.Us, PhysicalKey.Digit0, "0", ")"),
            ("US", layouts.Us, PhysicalKey.BracketRight, "]", "}"),
            ("US", layouts.Us, PhysicalKey.Slash, "/", "?"),
            ("German", layouts.German, PhysicalKey.Equal, null, null),
            ("German", layouts.German, PhysicalKey.Minus, "ß", "?"),
            ("German", layouts.German, PhysicalKey.Digit0, "0", "="),
            ("German", layouts.German, PhysicalKey.BracketRight, "+", "*"),
            ("German", layouts.German, PhysicalKey.Slash, "-", "_"),
            ("French", layouts.French, PhysicalKey.Equal, "=", "+"),
            ("French", layouts.French, PhysicalKey.Minus, ")", "°"),
            ("French", layouts.French, PhysicalKey.Digit0, "à", "0"),
            ("French", layouts.French, PhysicalKey.BracketRight, "$", "£"),
            ("French", layouts.French, PhysicalKey.Slash, "!", "§"),
        ];
        foreach (var (layout, reader, key, plain, shifted) in rows)
        {
            Assert.True(plain == reader.Typed(key, false), $"{layout} {key}: Windows types {reader.Typed(key, false) ?? "no character"}, not {plain ?? "no character"}");
            Assert.True(shifted == reader.Typed(key, true), $"{layout} {key} with Shift: Windows types {reader.Typed(key, true) ?? "no character"}, not {shifted ?? "no character"}");
        }
    });

    // ED-283 item 7: the layouts the fixture loads are in this process's layout list while it is alive and gone
    // when it is disposed, and the layout the thread has active never moves, so the tests leave the user's session as found.
    [Fact]
    [Trait("Decision", "ED-270")]
    [Trait("Rule", "R-TEST-4")] // partial for the zoom records: the fixture of the production-reader tests leaves no layout behind
    public void The_layout_fixture_leaves_the_sessions_layout_list_and_the_active_layout_as_it_found_them() => headless.OnUiThread(() =>
    {
        var before = WindowsLayoutReaders.Installed().OrderBy(handle => handle.ToInt64()).ToArray();
        var active = WindowsLayoutReaders.Active();
        IntPtr[] loaded;
        using (var layouts = new WindowsLayoutReaders())
        {
            loaded = new[] { "00000409", "00000407", "0000040C" }.Select(layouts.HandleOf).Distinct().ToArray();
            Assert.Equal(3, loaded.Length);
            var during = WindowsLayoutReaders.Installed();
            Assert.All(loaded, handle => Assert.Contains(handle, during)); // the fixture does load them
            Assert.Equal(active, WindowsLayoutReaders.Active());
        }
        Assert.Equal(before, WindowsLayoutReaders.Installed().OrderBy(handle => handle.ToInt64()).ToArray());
        Assert.Equal(active, WindowsLayoutReaders.Active());
        Assert.All(loaded.Where(handle => !before.Contains(handle)), handle => Assert.DoesNotContain(handle, WindowsLayoutReaders.Installed()));
    });

    // PR-WIN-222, PR-WIN-224, PR-WIN-225, UI-WIN-074 through the production reader and the production windows. The reviewer's
    // mutant "the reader ignores Shift" left every other zoom test green, because they read a table layout; here the layout
    // is read by WindowsKeyboardLayoutText over the real US and French layouts, and the keys are pressed in the main window
    // WindowManager makes, with its Settings panel (OD-0140) closed and open. Each record's remaining clauses (title, never disabled,
    // the step and the end, the write of appZoom even at an end, the rescale of both hosted trees, no menu bar, no palette row, and
    // for Actual Size that Ctrl+0 does not front the main window) are asserted in the same test.
    [Fact]
    [Trait("Req", "PR-WIN-222")] // App/AppCommands.swift:53-54; App/Window/WindowManager.swift:173-175
    [Trait("Req", "PR-WIN-224")] // App/AppCommands.swift:57-58; OD-0012 G6
    [Trait("Req", "PR-WIN-225")] // App/AppCommands.swift:59-62
    [Trait("Req", "UI-WIN-074")] // App/AppCommands.swift:53-54
    [Trait("Decision", "ED-270")]
    [Trait("Decision", "OD-0140")] // Settings is a panel inside the main window
    public void Read_by_Windows_Ctrl_Shift_equals_and_Ctrl_equals_zoom_in_and_on_a_French_keyboard_Ctrl_Shift_0_is_Actual_Size_with_the_Settings_panel_closed_and_open() => headless.OnUiThread(() =>
    {
        using var layouts = new WindowsLayoutReaders();
        using var launch = new LaunchHarness();
        using var store = ShortcutStore.Shared.Override(FreshStore());
        var model = launch.Manager.Model;
        var defaults = launch.Harness.Defaults;
        void OnMain(Action action) => launch.Harness.Main.OnMain(action);
        T Get<T>(Func<T> function) => launch.Harness.Main.OnMain(function);
        AvaloniaWindow main = null!;
        Control stage = null!;
        OnMain(() =>
        {
            launch.Manager.ShowMain();
            launch.Manager.ShowSettings();
            main = LaunchHarness.WindowOf(launch.MainContent)!;
            // OD-0140: Settings is a panel inside the main window; its own hosted root (WindowManager.HostedRoot) zooms the panel's
            // stage, the Settings content's host.
            Assert.Same(main, LaunchHarness.WindowOf(launch.SettingsContent));
            stage = Assert.IsType<SettingsOverlay>(launch.Manager.SettingsOverlay).Stage;
            Assert.True(stage.IsVisualAncestorOf(launch.SettingsContent));
        });
        double[] Scales() => Get(() =>
        {
            Dispatcher.UIThread.RunJobs();
            return new[] { launch.MainContent, stage }
                .Select(content => Assert.IsType<ScaleTransform>(content.RenderTransform).ScaleX).ToArray();
        });
        long Zoom() => Get(() => model.Settings.AppZoom);
        void SetZoom(long percent) => OnMain(() => model.Settings.AppZoom = percent);
        // OD-0140: a key pressed "in Settings" is pressed in the main window with the Settings panel open over it, and one "in the
        // main window" with the panel closed.
        const bool InMain = false, InSettings = true;
        void SetPanel(bool open)
        {
            // Opening Settings fronts the window it is in (WindowManager.Present); that activation runs on the dispatcher, so it lands
            // here rather than inside a later step's key press.
            OnMain(() => { if (open) launch.Manager.ShowSettings(); else launch.Manager.CloseSettings(); Dispatcher.UIThread.RunJobs(); });
            Assert.Equal(open, Get(() => launch.Manager.IsSettingsOpen));
        }
        void Zoomed(string chord, bool inSettings, Key key, PhysicalKey physical, RawInputModifiers modifiers, long to)
        {
            SetPanel(inSettings);
            OnMain(() => defaults.Writes.Clear());
            OnMain(() => Press(main, key, physical, modifiers));
            Assert.True(Zoom() == to, $"{chord}: the zoom is {Zoom()}, not {to}");
            Assert.Equal(["appZoom"], Get(() => defaults.Writes.ToList())); // AppSettings.swift:253-254, even when the value stays
            var scale = to / 100d;
            Assert.Equal([scale, scale], Scales()); // WindowManager.swift:173-175, every hosted tree
            Assert.Equal(inSettings, Get(() => launch.Manager.IsSettingsOpen)); // a zoom chord leaves the panel as it was
        }
        void NotZoomed(string chord, bool inSettings, Key key, PhysicalKey physical, RawInputModifiers modifiers)
        {
            SetPanel(inSettings);
            var before = Zoom();
            OnMain(() => defaults.Writes.Clear());
            OnMain(() => Press(main, key, physical, modifiers));
            Assert.True(before == Zoom(), $"{chord} changed the zoom from {before} to {Zoom()}");
            Assert.Empty(Get(() => defaults.Writes.ToList()));
        }
        try
        {
            // The items as the Body holds them: exact titles, never disabled, in the source's order (:53-62).
            OnMain(() =>
            {
                var commands = new AppCommands(model);
                Assert.Equal(["Zoom In", "Zoom Out", "Actual Size", "Zoom In"], commands.Body.Where(command => command.MatchesTypedCharacter).Select(command => command.Title));
                Assert.All(commands.Body.Where(command => command.MatchesTypedCharacter), command => Assert.False(command.IsDisabled));
                Assert.True(launch.Manager.IsSettingsOpen);
                Assert.Null(NativeMenu.GetMenu(main)); // OD-0008 C3: no menu bar
                Assert.Empty(main.GetVisualDescendants().OfType<Menu>()); // the Settings panel included
            });

            using (KeyboardLayoutText.Current.Override(layouts.Us))
            {
                SetZoom(100);
                Assert.Equal([1d, 1d], Scales());
                // :53-54 "+" is Shift with the US "=" key: the chord a user presses for Zoom In. Windows must report Shift.
                Zoomed("US Ctrl+Shift+=", InMain, Key.OemPlus, PhysicalKey.Equal, CtrlShift, 110);
                // :59-62 the hidden item: the same key without Shift ("US keyboards produce '=' for Command-plus without Shift").
                Zoomed("US Ctrl+=", InSettings, Key.OemPlus, PhysicalKey.Equal, Ctrl, 125);
                Zoomed("US Ctrl+Shift+= in Settings", InSettings, Key.OemPlus, PhysicalKey.Equal, CtrlShift, 150);
                Zoomed("US Ctrl+=", InMain, Key.OemPlus, PhysicalKey.Equal, Ctrl, 175);
                Zoomed("US Ctrl+Shift+=", InMain, Key.OemPlus, PhysicalKey.Equal, CtrlShift, 200);
                // At 200 both items are still enabled and the step leaves 200, writing the setting again.
                OnMain(() => Assert.All(new AppCommands(model).Body.Where(command => command.Title == "Zoom In"), command => Assert.False(command.IsDisabled)));
                Zoomed("US Ctrl+Shift+= at 200", InMain, Key.OemPlus, PhysicalKey.Equal, CtrlShift, 200);
                Zoomed("US Ctrl+= at 200", InSettings, Key.OemPlus, PhysicalKey.Equal, Ctrl, 200);

                // Shift with the US "0" key types ")" and with "-" types "_": no item holds either.
                NotZoomed("US Ctrl+Shift+0", InMain, Key.D0, PhysicalKey.Digit0, CtrlShift);
                NotZoomed("US Ctrl+Shift+-", InMain, Key.OemMinus, PhysicalKey.Minus, CtrlShift);
                // :55-56 and :57-58.
                Zoomed("US Ctrl+-", InMain, Key.OemMinus, PhysicalKey.Minus, Ctrl, 175);
                Zoomed("US Ctrl+0", InSettings, Key.D0, PhysicalKey.Digit0, Ctrl, 100);
                SetZoom(75);
                Zoomed("US Ctrl+0 from 75", InMain, Key.D0, PhysicalKey.Digit0, Ctrl, 100);
                Zoomed("US Ctrl+- from 100", InMain, Key.OemMinus, PhysicalKey.Minus, Ctrl, 90);
                Zoomed("US Ctrl+0 from 90", InMain, Key.D0, PhysicalKey.Digit0, Ctrl, 100);
            }

            using (KeyboardLayoutText.Current.Override(layouts.French))
            {
                SetZoom(150);
                Zoomed("French Ctrl+Shift+0 in Settings", InSettings, Key.D0, PhysicalKey.Digit0, CtrlShift, 100); // "0" is Shift with that key
                SetZoom(150);
                NotZoomed("French Ctrl+0, which types the letter a with a grave accent", InSettings, Key.D0, PhysicalKey.Digit0, Ctrl);
                Zoomed("French Ctrl+Shift+0 in the main window", InMain, Key.D0, PhysicalKey.Digit0, CtrlShift, 100);
                // "=" is at the US "=" position, "+" is Shift with it: both zoom in.
                Zoomed("French Ctrl+=", InMain, Key.OemPlus, PhysicalKey.Equal, Ctrl, 110);
                Zoomed("French Ctrl+Shift+=", InSettings, Key.OemPlus, PhysicalKey.Equal, CtrlShift, 125);
            }

            // OD-0012 G6: Ctrl+0 is Actual Size alone; with the main window behind another, the chord pressed in Settings fronts nothing
            // (OD-0140: Settings is a panel inside the main window, so that window cannot be hidden while Settings takes the key).
            using (KeyboardLayoutText.Current.Override(layouts.Us))
            {
                SetPanel(InSettings);
                OnMain(() => Deactivate(main));
                var built = launch.Events.Count(value => value == "main window built");
                SetZoom(150);
                OnMain(() => Press(main, Key.D0, PhysicalKey.Digit0, Ctrl));
                Assert.Equal(100, Zoom());
                Assert.False(Get(() => main.IsActive));
                Assert.True(Get(() => launch.Manager.IsSettingsOpen));
                Assert.Equal(built, launch.Events.Count(value => value == "main window built"));
            }

            // The source gives these commands no palette row (CommandPalette.swift:153-178).
            var (_, paletteWindow, palette) = Get(() => CommandPaletteRig.Build(model));
            try
            {
                Assert.NotEmpty(Get(() => RowNames(palette))); // the palette lists its own rows ...
                foreach (var query in new[] { "zoom", "actual size" })
                {
                    OnMain(() => palette.QueryField.Text = query); // (the filter runs from a posted job, so the rows are read after)
                    Assert.Empty(Get(() => RowNames(palette))); // ... and none is a zoom command
                }
            }
            finally { OnMain(() => paletteWindow.Close()); }
        }
        finally { OnMain(() => { launch.Manager.CloseSettings(); main.Hide(); Dispatcher.UIThread.RunJobs(); }); }
    });

    // The main window is a ThreadWindow (WindowManager.swift:63-65), whose digit monitor takes Command with the number row
    // by key position before the menu sees the event (ThreadWindow.swift:11-23: "taken before the event is dispatched"). On a
    // French keyboard the key that types "-" is the US "6" position, so in the main window Ctrl with it selects Thread 6 and
    // does not zoom out. With the Settings panel open over it (OD-0140: Settings is a panel inside the main window, no window of
    // its own) the monitor stands still, as under any sheet (ThreadWindow.swift:12-14), so the same chord reaches the menu's
    // Zoom Out (:55-56), which comes before Thread 6 in the body (:95-98), as it did in the Settings window. The earlier French
    // test above uses a bare window.
    [Fact]
    [Trait("Decision", "ED-270")]
    [Trait("Decision", "OD-0140")]
    [Trait("Rule", "R-TEST-4")] // partial for PR-WIN-110 and PR-WIN-223: the French number-row chord with and without the Settings panel, nothing else of either record
    public void On_a_French_keyboard_Ctrl_with_the_key_that_types_a_minus_selects_Thread_6_in_the_main_window_and_zooms_out_with_the_Settings_panel_open() => headless.OnUiThread(() =>
    {
        using var layouts = new WindowsLayoutReaders();
        using var launch = new LaunchHarness(h =>
        {
            var project = AppModelSamples.Project("P", h.NewFolder("p"));
            h.WriteLibrary([project], Enumerable.Range(0, 7).Select(_ => AppModelSamples.Thread(project)));
        });
        using var store = ShortcutStore.Shared.Override(FreshStore());
        launch.Harness.Main.OnMain(() =>
        {
            var model = launch.Manager.Model;
            var order = model.SidebarThreads.Select(thread => thread.Id).ToList();
            launch.Manager.ShowMain();
            launch.Manager.ShowSettings();
            var main = LaunchHarness.WindowOf(launch.MainContent)!;
            var settings = LaunchHarness.WindowOf(launch.SettingsContent)!;
            try
            {
                Assert.Same(main, settings); // OD-0140: the panel is in the main window
                Assert.True(main is ThreadWindow);
                Assert.Equal(7, order.Count);
                Assert.Null(model.SelectedThreadID);
                using var layout = KeyboardLayoutText.Current.Override(layouts.French);
                model.Settings.AppZoom = 100;

                launch.Manager.CloseSettings();
                Assert.False(launch.Manager.IsSettingsOpen);
                Press(main, Key.D6, PhysicalKey.Digit6, Ctrl);
                Assert.Equal(order[5], model.SelectedThreadID); // ThreadWindow.swift:23 key code 22 is Thread 6; AppModel order[number - 1]
                Assert.Equal(100, model.Settings.AppZoom); // the digit monitor took the chord first

                model.SelectedThreadID = null;
                launch.Manager.ShowSettings();
                Assert.True(launch.Manager.IsSettingsOpen);
                Press(settings, Key.D6, PhysicalKey.Digit6, Ctrl);
                Assert.Equal(90, model.Settings.AppZoom); // AppCommands.swift:55-56, before Thread 6 in the body
                Assert.Null(model.SelectedThreadID);
                Assert.True(launch.Manager.IsSettingsOpen);

                // The number keys that type digits only with Shift still select by position in the main window.
                launch.Manager.CloseSettings();
                Press(main, Key.D2, PhysicalKey.Digit2, Ctrl);
                Assert.Equal(order[1], model.SelectedThreadID);
            }
            finally { launch.Manager.CloseSettings(); main.Hide(); Dispatcher.UIThread.RunJobs(); }
        });
    });
}
