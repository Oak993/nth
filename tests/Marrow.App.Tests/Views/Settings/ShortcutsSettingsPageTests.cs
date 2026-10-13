using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

// Source-derived tests for DroppyCode/App/Views/Settings/ShortcutsSettingsPage.swift (B2 1fbf81e), unit
// App.Views.Settings.ShortcutsSettingsPage, part 1 of 1. Every expected value is transcribed from the Swift
// lines cited on each test or from the record's "deviation" field (OD-0008 B7 Windows notation), never from
// the C# under test. Section shortcut counts (12 Threads, 7 Window) come from AppShortcut's declaration order
// (Core/Models/Shortcuts.swift:106-128) and AppShortcutSectionExtensions.Shortcuts, already proven by
// Marrow.Core.Tests.Models.ShortcutsTests. NewThread's default chord is Ctrl+N (keyCode 0x31, Control) and
// CommandPalette's is Ctrl+K (keyCode 0x25, Control), both cited from that file's table (:49, :61).
[Collection("Headless")]
public sealed class ShortcutsSettingsPageTests(HeadlessSession headless)
{
    private static ShortcutStore FreshStore() => (ShortcutStore)Activator.CreateInstance(typeof(ShortcutStore), nonPublic: true)!;

    private static Avalonia.Controls.Window Show(Control content)
    {
        var window = new Avalonia.Controls.Window { Width = 500, Height = 900, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static IEnumerable<T> Descendants<T>(Visual root) where T : Visual => root.GetVisualDescendants().OfType<T>();

    [Fact]
    [Trait("Req", "PR-SETTINGS-212")] // App/Views/Settings/ShortcutsSettingsPage.swift:6-16
    public void Page_has_three_sections_in_order_with_the_recordable_row_counts()
    {
        using var defaults = UserDefaults.Standard.Override(new InMemorySettingsStore());
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        headless.OnUiThread(() =>
        {
            using var shared = ShortcutStore.Shared.Override(FreshStore());
            var window = Show(new ShortcutsSettingsPage());
            var titles = Descendants<TextBlock>(window).Select(t => t.Text).ToList();
            var threadsAt = titles.IndexOf("Threads");
            var windowAt = titles.IndexOf("Window and panels");
            var composerAt = titles.IndexOf("Composer");
            Assert.True(threadsAt >= 0 && windowAt > threadsAt && composerAt > windowAt); // :9-12 section order

            var rows = Descendants<ShortcutRecorder>(window).Count();
            Assert.Equal(12 + 7 + 11, rows); // :9-11, twelve + seven recordable rows, and the eleven pane commands of web W3b

            var fixedTitles = new[] { "Send", "New line", "Steer a chat", "Recall an earlier prompt", "Approve a request", "Jump to a thread" }; // :69-74
            Assert.All(fixedTitles, title => Assert.Contains(title, titles));
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-213")] // App/Views/Settings/ShortcutsSettingsPage.swift:18-53
    public void Reset_pill_shows_only_once_its_section_has_a_customisation_and_clears_the_section()
    {
        using var defaults = UserDefaults.Standard.Override(new InMemorySettingsStore());
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        headless.OnUiThread(() =>
        {
            var store = FreshStore();
            using var shared = ShortcutStore.Shared.Override(store);
            var before = Show(new ShortcutsSettingsPage());
            var beforeButtons = Descendants<Button>(before).Where(b => AutomationProperties.GetName(b) == "Reset").ToList();
            Assert.Equal(2, beforeButtons.Count); // one per recordable section (:9-11); Composer never gets one (:79-81)
            Assert.All(beforeButtons, b => Assert.Equal(0, b.Opacity)); // :46 no customisation yet
            before.Close();

            store.Set(new KeyChord(0x2E, ChordModifiers.Control | ChordModifiers.Alt | ChordModifiers.Shift), AppShortcut.NewThread); // NewThread is in Threads
            var after = Show(new ShortcutsSettingsPage());
            var afterButtons = Descendants<Button>(after).Where(b => AutomationProperties.GetName(b) == "Reset").ToList();
            Assert.Single(afterButtons, b => b.Opacity == 1); // :46 shown once its section has an override

            var visible = afterButtons.First(b => b.Opacity == 1);
            visible.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            // Reset removes the override, restoring the shipped default (Ctrl+N) rather than clearing to null (:48-49,
            // PR-SETTINGS-220's note: "Reset removes the override, which restores the shipped chord").
            Assert.False(store.IsCustomized(AppShortcut.NewThread));
            Assert.Equal(AppShortcut.NewThread.DefaultChord(), store.Chord(AppShortcut.NewThread));
            after.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-215")] // App/Views/Settings/ShortcutsSettingsPage.swift:151-201
    public void Recorder_shows_record_shortcut_when_cleared_and_the_windows_notation_chord_when_set()
    {
        using var defaults = UserDefaults.Standard.Override(new InMemorySettingsStore());
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        headless.OnUiThread(() =>
        {
            var store = FreshStore();
            using var shared = ShortcutStore.Shared.Override(store);
            // ShowMainWindow ships with no default chord (OD-0012 G6, ShortcutsTests.cs header), so Chord() reads
            // null until the user records one: the case the "Record shortcut" wording covers.
            var cleared = new ShortcutRecorder(AppShortcut.ShowMainWindow);
            Show(cleared);
            var clearedLabel = Descendants<TextBlock>(cleared).First();
            Assert.Equal("Record shortcut", clearedLabel.Text); // :176 verbatim, no chord

            // NewThread's shipped default is Ctrl+N (ShortcutsTests.cs:49), read with no override needed.
            var set = new ShortcutRecorder(AppShortcut.NewThread);
            Show(set);
            var setLabel = Descendants<TextBlock>(set).First();
            Assert.Equal("Ctrl+N", setLabel.Text); // :167 chord.Description in Windows notation, DEVIATION OD-0008 B7
        });
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-044")] // App/Views/Settings/ShortcutsSettingsPage.swift:151-182
    public void Recorder_control_is_a_fixed_168_by_24_button()
    {
        using var defaults = UserDefaults.Standard.Override(new InMemorySettingsStore());
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        headless.OnUiThread(() =>
        {
            using var shared = ShortcutStore.Shared.Override(FreshStore());
            var recorder = new ShortcutRecorder(AppShortcut.NewThread);
            var window = Show(recorder);
            var panel = Descendants<Panel>(recorder).First(p => p.Width == 168); // :181
            Assert.Equal(168, panel.Width);
            Assert.Equal(24, panel.Height); // :181
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-220")] // App/Views/Settings/ShortcutsSettingsPage.swift:428-469, :204-222
    public void Recording_popover_content_draws_the_caps_the_message_and_the_offered_actions()
    {
        using var defaults = UserDefaults.Standard.Override(new InMemorySettingsStore());
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        headless.OnUiThread(() =>
        {
            var session = new ShortcutRecordingSession();
            var clearRan = false;
            var resetRan = false;
            var actions = new[] // mirrors ShortcutRecorder's private actions(current:) (:204-222) for a set, customised shortcut
            {
                new ShortcutRecordingAction("clear", "Clear", true, () => clearRan = true), // :208-212
                new ShortcutRecordingAction("reset", "Reset to default", false, () => resetRan = true), // :215-219
            };
            session.Begin(AppShortcut.NewThread, actions, ChordModifiers.None, () => { }, _ => { });
            session.HandleKeyDown(0x31, ChordModifiers.Control, isAutoRepeat: false); // Ctrl+N: NewThread's own default, accepted, no message

            var content = new ShortcutRecordingPopoverContent(session);
            var window = Show(content);
            var texts = Descendants<TextBlock>(content).Select(t => t.Text).ToList();
            Assert.Contains("Clear", texts); // :208 present: the shortcut currently has a chord
            Assert.Contains("Reset to default", texts); // :215 present: customised and DefaultChord != nil
            Assert.Equal("Press keys…", AutomationProperties.GetName(content.Child!)); // :467, U+2026 HORIZONTAL ELLIPSIS

            var buttons = Descendants<Button>(content).ToList();
            buttons.First(b => Descendants<TextBlock>(b).Any(t => t.Text == "Clear"))
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.True(clearRan); // :211 action.perform runs Clear's closure
            buttons.First(b => Descendants<TextBlock>(b).Any(t => t.Text == "Reset to default"))
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.True(resetRan); // :218
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-220")] // App/Views/Settings/ShortcutsSettingsPage.swift:433 VStack(spacing: 12), centre alignment by default
    [Trait("Req", "UI-SETTINGS-046")] // App/Views/Settings/ShortcutsSettingsPage.swift:434-459 the caps, the message and the actions
    public void Recording_popover_centres_the_caps_the_message_and_the_actions_each_at_its_own_width()
    {
        using var defaults = UserDefaults.Standard.Override(new InMemorySettingsStore());
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var shared = ShortcutStore.Shared.Override(FreshStore());
        headless.OnUiThread(() =>
        {
            var session = new ShortcutRecordingSession();
            var actions = new[]
            {
                new ShortcutRecordingAction("clear", "Clear", true, () => { }),
                new ShortcutRecordingAction("reset", "Reset to default", false, () => { }),
            };
            session.Begin(AppShortcut.CommandPalette, actions, ChordModifiers.None, () => { }, _ => { });
            session.HandleKeyDown(0x31, ChordModifiers.Control, isAutoRepeat: false); // Ctrl+N is New thread's chord: rejected, with a message
            Assert.NotNull(session.Message);

            var content = new ShortcutRecordingPopoverContent(session);
            var window = Show(content);
            var stack = Assert.IsType<StackPanel>(Assert.IsType<Border>(content.Child).Child);
            Assert.Equal(3, stack.Children.Count);
            // A VStack centres each child at its own width; the StackPanel stretched the three rows to the widest (the message or
            // the actions), so the keycaps sat at the left edge.
            foreach (var row in stack.Children)
            {
                Assert.Equal(row.DesiredSize.Width, row.Bounds.Width, 0.5);
                Assert.Equal(stack.Bounds.Width / 2, row.Bounds.Center.X, 0.5);
            }
            Assert.NotEqual(stack.Bounds.Width, stack.Children[0].Bounds.Width); // the caps are the narrow row here
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-048")] // App/Views/Settings/ShortcutsSettingsPage.swift:68-75
    [Trait("Req", "PR-SETTINGS-222")] // App/Views/Settings/ShortcutsSettingsPage.swift:68-75
    public void Fixed_row_chords_read_verbatim_in_windows_notation_with_the_en_dash_kept()
    {
        using var defaults = UserDefaults.Standard.Override(new InMemorySettingsStore());
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        headless.OnUiThread(() =>
        {
            var window = Show(new FixedShortcutsCard());
            var texts = Descendants<TextBlock>(window).Select(t => t.Text).ToList();
            Assert.Contains("Return", texts); // :69, :71 Send and Steer a chat share this chord verbatim
            Assert.Contains("Shift+Return", texts); // :70, DEVIATION OD-0008 B7 (upstream "⇧ Return")
            Assert.Contains("↑", texts); // :72, unchanged
            Assert.Contains("Ctrl+Return", texts); // :73, DEVIATION OD-0008 B7 (upstream "⌘ Return")
            Assert.Contains("Ctrl+1–9", texts); // :74, U+2013 EN DASH kept, DEVIATION OD-0008 B7 (upstream "⌘ 1–9")
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-214")] // App/Views/Settings/ShortcutsSettingsPage.swift:104-110
    public void Row_metrics_match_the_swift_constants()
    {
        Assert.Equal(12, ShortcutRowMetrics.RowPadding); // :105
        Assert.Equal(16, ShortcutRowMetrics.DividerLeadingInset); // :106
        Assert.Equal(16, ShortcutRowMetrics.ControlSpacing); // :107
        Assert.Equal(168, ShortcutRowMetrics.ControlWidth); // :108
        Assert.Equal(24, ShortcutRowMetrics.ControlHeight); // :109
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-045")] // App/Views/Settings/ShortcutsSettingsPage.swift:296-303
    public void Modifier_caps_are_produced_in_the_fixed_ctrl_alt_shift_order()
    {
        var caps = Keycap.ModifierCaps(ChordModifiers.Control | ChordModifiers.Alt | ChordModifiers.Shift, KeycapPhase.Pressed);
        Assert.Equal(["Ctrl", "Alt", "Shift"], caps.Select(c => c.Symbol)); // DEVIATION OD-0008 B7: Ctrl, Alt, Shift replace control/option/shift/command
        Assert.Equal(["control", "option", "shift"], caps.Select(c => c.Id));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-221")] // App/Views/Settings/ShortcutsSettingsPage.swift:309-315
    public void Caps_for_a_chord_are_its_modifiers_then_the_key()
    {
        var chord = new KeyChord(0x31, ChordModifiers.Control | ChordModifiers.Shift);
        var caps = Keycap.CapsFor(chord);
        Assert.Equal(["Ctrl", "Shift", "N"], caps.Select(c => c.Symbol)); // :310, KeyNames.Label(0x31) == "N"
        Assert.All(caps, c => Assert.Equal(KeycapPhase.Pressed, c.Phase));
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-046")] // App/Views/Settings/ShortcutsSettingsPage.swift:317-324
    public void Keycap_tint_colours_are_the_exact_rgb_triples()
    {
        Assert.Equal(new SwiftColor(0.94, 0.43, 0.46).Resolve(Avalonia.Styling.ThemeVariant.Light), KeycapColors.Rejected.Resolve(Avalonia.Styling.ThemeVariant.Light)); // :323
        Assert.Equal(new SwiftColor(0.30, 0.84, 0.45).Resolve(Avalonia.Styling.ThemeVariant.Light), KeycapColors.Accepted.Resolve(Avalonia.Styling.ThemeVariant.Light)); // :323
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-217")] // App/Views/Settings/ShortcutsSettingsPage.swift:525-562
    public void Begin_seeds_a_single_ghost_command_cap_and_listening_verdict()
    {
        using var runtime = new TestRuntime();
        var session = new ShortcutRecordingSession();
        runtime.OnMain(() => session.Begin(AppShortcut.NewThread, [], ChordModifiers.None, () => { }, _ => { }));
        Assert.Equal(ShortcutVerdict.Listening, session.Verdict); // :539
        Assert.Single(session.Caps);
        Assert.Equal(Keycap.GhostCommand, session.Caps[0]); // :500 [Keycap.ghostCommand]
        Assert.Equal(KeycapTint.Neutral, session.Tint); // :519
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-218")] // App/Views/Settings/ShortcutsSettingsPage.swift:596-633
    public void A_free_chord_is_judged_accepted_and_a_colliding_one_is_rejected()
    {
        using var runtime = new TestRuntime();
        using var shared = ShortcutStore.Shared.Override(FreshStore());
        var session = new ShortcutRecordingSession();
        runtime.OnMain(() => session.Begin(AppShortcut.CommandPalette, [], ChordModifiers.None, () => { }, _ => { }));

        runtime.OnMain(() => session.HandleKeyDown(0x31, ChordModifiers.Control | ChordModifiers.Alt | ChordModifiers.Shift, isAutoRepeat: false)); // free chord (:621-633)
        Assert.Equal(ShortcutVerdict.Accepted, session.Verdict); // :628
        Assert.Null(session.Message); // :629
        runtime.OnMain(() => session.HandleKeyUp(0x31));

        runtime.OnMain(() => session.Begin(AppShortcut.CommandPalette, [], ChordModifiers.None, () => { }, _ => { }));
        runtime.OnMain(() => session.HandleKeyDown(0x31, ChordModifiers.Control, isAutoRepeat: false)); // NewThread's own default chord, Ctrl+N
        Assert.Equal(ShortcutVerdict.Rejected, session.Verdict); // :624
        Assert.NotNull(session.Message); // :625
        Assert.Equal(KeycapTint.Rejected, session.Tint); // :520
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-218")] // App/Views/Settings/ShortcutsSettingsPage.swift:635-657
    public void An_accepted_chord_commits_300ms_after_the_last_key_lifts_and_closes()
    {
        using var runtime = new TestRuntime();
        using var shared = ShortcutStore.Shared.Override(FreshStore());
        var session = new ShortcutRecordingSession();
        KeyChord? committed = null;
        var closed = false;
        runtime.OnMain(() => session.Begin(AppShortcut.CommandPalette, [], ChordModifiers.None, () => closed = true, c => committed = c));
        runtime.OnMain(() => session.HandleKeyDown(0x31, ChordModifiers.Control | ChordModifiers.Alt | ChordModifiers.Shift, isAutoRepeat: false));
        runtime.OnMain(() => session.HandleKeyUp(0x31));
        runtime.OnMain(() => session.HandleFlagsChanged(ChordModifiers.None)); // :614-615 letGo once every key is up

        runtime.AdvanceMilliseconds(299);
        Assert.Null(committed); // :645-649 not yet, before 300ms
        runtime.AdvanceMilliseconds(2);
        Assert.NotNull(committed); // :647 onCommit(latched)
        Assert.Equal(0x31L, committed!.KeyCode);
        Assert.True(closed); // :648 close()
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-218")] // App/Views/Settings/ShortcutsSettingsPage.swift:600-603
    public void Bare_escape_cancels_and_escape_with_a_modifier_is_an_ordinary_key()
    {
        using var runtime = new TestRuntime();
        using var shared = ShortcutStore.Shared.Override(FreshStore());
        var session = new ShortcutRecordingSession();
        var closed = false;
        runtime.OnMain(() => session.Begin(AppShortcut.CommandPalette, [], ChordModifiers.None, () => closed = true, _ => { }));
        runtime.OnMain(() => session.HandleKeyDown(53, ChordModifiers.None, isAutoRepeat: false)); // :600-603 bare Escape
        Assert.True(closed); // cancel() -> close()

        closed = false;
        runtime.OnMain(() => session.Begin(AppShortcut.CommandPalette, [], ChordModifiers.None, () => closed = true, _ => { }));
        runtime.OnMain(() => session.HandleKeyDown(53, ChordModifiers.Control, isAutoRepeat: false)); // Escape with a modifier: an ordinary key
        Assert.False(closed);
        Assert.Equal(ShortcutVerdict.Accepted, session.Verdict); // Ctrl+Escape is unclaimed, so it judges accepted
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-218")] // App/Views/Settings/ShortcutsSettingsPage.swift:596-597
    public void An_auto_repeat_key_down_is_ignored()
    {
        using var runtime = new TestRuntime();
        var session = new ShortcutRecordingSession();
        runtime.OnMain(() => session.Begin(AppShortcut.NewThread, [], ChordModifiers.None, () => { }, _ => { }));
        runtime.OnMain(() => session.HandleKeyDown(0x31, ChordModifiers.Control, isAutoRepeat: true)); // :597
        Assert.Equal(ShortcutVerdict.Listening, session.Verdict); // never judged
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-218")] // App/Views/Settings/ShortcutsSettingsPage.swift:583-594
    public void Adding_a_modifier_with_nothing_latched_refreshes_the_held_modifier_caps()
    {
        using var runtime = new TestRuntime();
        var session = new ShortcutRecordingSession();
        runtime.OnMain(() => session.Begin(AppShortcut.NewThread, [], ChordModifiers.None, () => { }, _ => { }));
        runtime.OnMain(() => session.HandleFlagsChanged(ChordModifiers.Control)); // :586, :592
        Assert.Equal(2, session.Caps.Count);
        Assert.Equal("Ctrl", session.Caps[0].Symbol);
        Assert.Equal(Keycap.GhostKey, session.Caps[1]); // :670
        runtime.OnMain(() => session.HandleFlagsChanged(ChordModifiers.None)); // :589-590 letGo with nothing latched just refreshes (:637)
        Assert.Single(session.Caps);
        Assert.Equal(Keycap.GhostCommand, session.Caps[0]); // :665-666
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-220")] // App/Views/Settings/ShortcutsSettingsPage.swift:635-654
    public void A_rejected_chord_drops_and_returns_to_listening_when_the_keys_lift()
    {
        using var runtime = new TestRuntime();
        using var shared = ShortcutStore.Shared.Override(FreshStore());
        var session = new ShortcutRecordingSession();
        runtime.OnMain(() => session.Begin(AppShortcut.CommandPalette, [], ChordModifiers.None, () => { }, _ => { }));
        runtime.OnMain(() => session.HandleKeyDown(0x31, ChordModifiers.Control, isAutoRepeat: false)); // Ctrl+N: NewThread's default chord, rejected
        Assert.Equal(ShortcutVerdict.Rejected, session.Verdict);
        runtime.OnMain(() => session.HandleKeyUp(0x31));
        runtime.OnMain(() => session.HandleFlagsChanged(ChordModifiers.None)); // :650-653
        Assert.Equal(ShortcutVerdict.Listening, session.Verdict); // back to listening, nothing committed
        Assert.Single(session.Caps);
        Assert.Equal(Keycap.GhostCommand, session.Caps[0]);
    }
}
