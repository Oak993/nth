using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;

namespace Marrow.Core.Tests.Web;

// nth web surfaces, unit W3b: the shortcut scope rule (work/design/web-surfaces/DESIGN.md 3.7 "Scope rule, three scopes",
// 13.1 "Shortcut scope tests: in a content scope only the reserved list is taken; Ctrl+\ and Ctrl+D reach the terminal;
// Ctrl+; and Ctrl+Shift+G reach a page"). Every chord is the design's table; the reserved list follows the user's
// bindings; nothing collides with a source default.
[Trait("Decision", "ED-NEW-web-w3b-1")]
public sealed class ShortcutScopeTests
{
    private const long B = 0x30;
    private const long D = 0x20;
    private const long G = 0x22;
    private const long A = 0x1E;
    private const long Backslash = 0x2B;
    private const long Semicolon = 0x27;
    private const long F6 = 0x40;
    private const long T = 0x14;
    private const long Escape = 0x01;
    private const long Left = 0xE04B;
    private const long Right = 0xE04D;
    private const long Up = 0xE048;
    private const long Down = 0xE050;
    private const ChordModifiers Ctrl = ChordModifiers.Control;
    private const ChordModifiers Alt = ChordModifiers.Alt;
    private const ChordModifiers Shift = ChordModifiers.Shift;

    private static KeyChord Chord(long code, ChordModifiers modifiers) => new(code, modifiers);

    private static void WithStore(Action<ShortcutStore> body)
    {
        using var runtime = new TestRuntime();
        var store = runtime.OnMain(() => (ShortcutStore)Activator.CreateInstance(typeof(ShortcutStore), nonPublic: true)!);
        using var shared = ShortcutStore.Shared.Override(store);
        runtime.OnMain(() => body(store));
    }

    [Fact]
    public void Every_chord_of_the_designs_table_is_the_default_of_its_command()
    {
        WithStore(store =>
        {
            Assert.Equal(Chord(B, Ctrl | Shift), store.Chord(AppShortcut.ToggleBrowser));
            Assert.Equal(Chord(Semicolon, Ctrl), store.Chord(AppShortcut.ToggleSideChat));
            Assert.Equal(Chord(A, Ctrl | Shift), store.Chord(AppShortcut.ToggleArtifacts));
            Assert.Equal(Chord(G, Ctrl | Shift), store.Chord(AppShortcut.ToggleBackgroundTasks));
            Assert.Equal(Chord(D, Ctrl), store.Chord(AppShortcut.ToggleChanges));
            Assert.Equal(Chord(0x24, Ctrl), store.Chord(AppShortcut.ToggleTerminal)); // Ctrl+J
            Assert.Equal(Chord(Backslash, Ctrl), store.Chord(AppShortcut.ClosePane));
            Assert.Equal(Chord(Backslash, Ctrl | Shift), store.Chord(AppShortcut.ExpandPane));
            Assert.Equal(Chord(F6, ChordModifiers.None), store.Chord(AppShortcut.NextPane));
            Assert.Equal(Chord(F6, Shift), store.Chord(AppShortcut.PreviousPane));
            Assert.Equal(Chord(Left, Ctrl | Alt), store.Chord(AppShortcut.MovePaneEarlier));
            Assert.Equal(Chord(Right, Ctrl | Alt), store.Chord(AppShortcut.MovePaneLater));
            Assert.Equal(Chord(T, Ctrl | Alt), store.Chord(AppShortcut.JoinPaneTabs));
            Assert.Equal("Ctrl+Shift+B", store.Chord(AppShortcut.ToggleBrowser)!.Description);
            Assert.Equal("F6", store.Chord(AppShortcut.NextPane)!.Description);
            Assert.Equal("Shift+F6", store.Chord(AppShortcut.PreviousPane)!.Description);
        });
    }

    [Fact]
    public void No_default_chord_is_shared_by_two_commands_or_taken_by_the_system()
    {
        WithStore(store =>
        {
            var chords = SwiftEnum<AppShortcut>.AllCases.Select(store.Chord).OfType<KeyChord>().ToList();
            Assert.Equal(chords.Count, chords.Distinct().Count());
            // Every command's own chord is accepted for itself (so none is among the system's reserved ones).
            foreach (var command in SwiftEnum<AppShortcut>.AllCases)
                if (store.Chord(command) is { } chord) Assert.Null(store.Refusal(chord, command));
        });
    }

    [Fact]
    public void In_nth_chrome_every_chord_applies()
    {
        WithStore(store =>
        {
            foreach (var command in SwiftEnum<AppShortcut>.AllCases)
                if (store.Chord(command) is { } chord) Assert.True(ShortcutScopes.Takes(ShortcutScope.Chrome, store, chord), command.ToString());
            Assert.True(ShortcutScopes.Takes(ShortcutScope.Chrome, store, Chord(0x2E, Ctrl))); // even a chord no command holds
        });
    }

    [Fact]
    public void In_a_content_scope_only_the_reserved_list_is_taken()
    {
        WithStore(store =>
        {
            Assert.True(ShortcutScopes.Takes(ShortcutScope.Content, store, Chord(F6, ChordModifiers.None)));
            Assert.True(ShortcutScopes.Takes(ShortcutScope.Content, store, Chord(F6, Shift)));
            Assert.True(ShortcutScopes.Takes(ShortcutScope.Content, store, Chord(Escape, Ctrl | Alt))); // the Tactus Stop chord
            Assert.True(ShortcutScopes.Takes(ShortcutScope.Content, store, Chord(Backslash, Ctrl | Shift)));
            Assert.True(ShortcutScopes.Takes(ShortcutScope.Content, store, Chord(B, Ctrl | Shift)));
            foreach (var arrow in new[] { Left, Right, Up, Down })
                Assert.True(ShortcutScopes.Takes(ShortcutScope.Content, store, Chord(arrow, Ctrl | Alt)), $"Ctrl+Alt+{arrow:X}");

            // The terminal keeps EOF and SIGQUIT; a page keeps its date, its find-previous and the browser keys.
            Assert.False(ShortcutScopes.Takes(ShortcutScope.Content, store, Chord(D, Ctrl)));
            Assert.False(ShortcutScopes.Takes(ShortcutScope.Content, store, Chord(Backslash, Ctrl)));
            Assert.False(ShortcutScopes.Takes(ShortcutScope.Content, store, Chord(Semicolon, Ctrl)));
            Assert.False(ShortcutScopes.Takes(ShortcutScope.Content, store, Chord(G, Ctrl | Shift)));
            foreach (var key in new long[] { 0x11, 0x13, 0x14, 0x26, 0x21, 0x0B, 0x0C, 0x0D }) // W R T L F 0 - =
                Assert.False(ShortcutScopes.Takes(ShortcutScope.Content, store, Chord(key, Ctrl)), $"{key:X}");
            Assert.False(ShortcutScopes.Takes(ShortcutScope.Content, store, Chord(Escape, ChordModifiers.None))); // Esc never leaves the page
            Assert.False(ShortcutScopes.Takes(ShortcutScope.Content, store, Chord(F6, Ctrl))); // only F6 and Shift+F6
            Assert.False(ShortcutScopes.Takes(ShortcutScope.Content, store, Chord(0x3F, ChordModifiers.None))); // F5 reloads the page
            Assert.False(ShortcutScopes.Takes(ShortcutScope.Content, store, Chord(Left, Alt))); // Alt+Left is back
            Assert.False(ShortcutScopes.Takes(ShortcutScope.Content, store, Chord(T, Ctrl | Alt))); // joining tabs is not reserved
        });
    }

    [Fact]
    public void Only_the_marked_commands_work_inside_pages()
    {
        WithStore(store =>
        {
            var marked = SwiftEnum<AppShortcut>.AllCases.Where(command => command.WorksInsidePages()).ToHashSet();
            Assert.Equal(
                new HashSet<AppShortcut> { AppShortcut.ToggleBrowser, AppShortcut.ExpandPane, AppShortcut.NextPane, AppShortcut.PreviousPane, AppShortcut.MovePaneEarlier, AppShortcut.MovePaneLater },
                marked);
            foreach (var command in SwiftEnum<AppShortcut>.AllCases)
                if (store.Chord(command) is { } chord)
                    Assert.True(command.WorksInsidePages() == (ShortcutScopes.Takes(ShortcutScope.Content, store, chord) && ShortcutScopes.Resolve(store, chord) == command), command.ToString());
        });
    }

    [Fact]
    public void The_reserved_list_follows_the_users_bindings()
    {
        WithStore(store =>
        {
            var custom = Chord(0x12, Ctrl | Alt); // Ctrl+Alt+E
            store.Set(custom, AppShortcut.ExpandPane);
            Assert.True(ShortcutScopes.Takes(ShortcutScope.Content, store, custom));
            Assert.False(ShortcutScopes.Takes(ShortcutScope.Content, store, Chord(Backslash, Ctrl | Shift)), "the old default is no longer reserved");
            Assert.Equal(AppShortcut.ExpandPane, ShortcutScopes.Resolve(store, custom));

            store.Set(null, AppShortcut.ToggleBrowser); // cleared: nothing is reserved for it
            Assert.False(ShortcutScopes.Takes(ShortcutScope.Content, store, Chord(B, Ctrl | Shift)));
            Assert.Null(ShortcutScopes.Resolve(store, Chord(B, Ctrl | Shift)));

            // A non-reserved command taking a chord does not make it reserved.
            var date = Chord(0x25, Ctrl | Alt);
            store.Set(date, AppShortcut.JoinPaneTabs);
            Assert.False(ShortcutScopes.Takes(ShortcutScope.Content, store, date));
            Assert.Equal(AppShortcut.JoinPaneTabs, ShortcutScopes.Resolve(store, date));

            // The Tactus Stop chord is fixed.
            Assert.True(ShortcutScopes.Takes(ShortcutScope.Content, store, ShortcutScopes.TactusStop));
            Assert.Equal(Chord(Escape, Ctrl | Alt), ShortcutScopes.TactusStop);
        });
    }

    [Fact]
    public void Up_and_Down_move_a_pane_like_Left_and_Right_and_follow_a_rebinding_of_the_arrows()
    {
        WithStore(store =>
        {
            Assert.Equal(AppShortcut.MovePaneEarlier, ShortcutScopes.Resolve(store, Chord(Left, Ctrl | Alt)));
            Assert.Equal(AppShortcut.MovePaneEarlier, ShortcutScopes.Resolve(store, Chord(Up, Ctrl | Alt)));
            Assert.Equal(AppShortcut.MovePaneLater, ShortcutScopes.Resolve(store, Chord(Right, Ctrl | Alt)));
            Assert.Equal(AppShortcut.MovePaneLater, ShortcutScopes.Resolve(store, Chord(Down, Ctrl | Alt)));
            Assert.Null(ShortcutScopes.Resolve(store, Chord(Up, Ctrl))); // not without the same modifiers

            // Re-bound to the same arrows with other modifiers, the twins go with them.
            store.Set(Chord(Left, Ctrl | Alt | Shift), AppShortcut.MovePaneEarlier);
            Assert.Equal(AppShortcut.MovePaneEarlier, ShortcutScopes.Resolve(store, Chord(Up, Ctrl | Alt | Shift)));
            Assert.Null(ShortcutScopes.Resolve(store, Chord(Up, Ctrl | Alt)));
            Assert.True(ShortcutScopes.IsReserved(store, Chord(Up, Ctrl | Alt | Shift)));

            // Re-bound to a letter, there is no twin.
            store.Set(Chord(0x2C, Ctrl | Alt), AppShortcut.MovePaneLater);
            Assert.Null(ShortcutScopes.Resolve(store, Chord(Down, Ctrl | Alt)));
            Assert.False(ShortcutScopes.IsReserved(store, Chord(Down, Ctrl | Alt)));
        });
    }

    [Fact]
    public void A_twin_chord_cannot_be_recorded_for_another_command_and_an_explicit_binding_wins_over_a_twin()
    {
        WithStore(store =>
        {
            var up = Chord(Up, Ctrl | Alt);
            Assert.Equal("Move pane earlier already uses this chord.", store.Refusal(up, AppShortcut.ToggleSideChat));
            Assert.Null(store.Refusal(up, AppShortcut.MovePaneEarlier)); // its own twin is its to take
            Assert.Equal("Move pane later already uses this chord.", store.Refusal(Chord(Down, Ctrl | Alt), AppShortcut.ClosePane));
        });
    }

    [Fact]
    public void The_chords_the_system_already_uses_are_still_refused_to_the_new_commands()
    {
        WithStore(store =>
        {
            Assert.Equal("Windows or nth already uses this chord.", store.Refusal(Chord(0x2E, Ctrl), AppShortcut.ClosePane)); // Ctrl+C
            Assert.Equal("Add Ctrl or Alt so typing still works.", store.Refusal(Chord(Backslash, ChordModifiers.None), AppShortcut.ClosePane));
            Assert.Null(store.Refusal(Chord(F6, Ctrl), AppShortcut.NextPane)); // F6 does not type text
            Assert.Equal("Toggle changes already uses this chord.", store.Refusal(Chord(D, Ctrl), AppShortcut.ToggleBrowser));
        });
    }
}
