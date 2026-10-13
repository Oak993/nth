using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

// PORT-ONLY: web surfaces W10, "new shortcuts re-bindable" and the mark of a chord that works inside pages (E:/Droppy/work/design/web-surfaces/DESIGN.md 3.7, 9 "Shortcuts: the new chords appear in 'Window and panels'", 14 W10; ED-NEW-web-w3b-1 point 5, ED-NEW-web-w10-4). The eleven pane
// commands each have a recordable row in Window and panels; the six whose chord nth takes from a page or the terminal say so beside their title, where it is read, and not only in the tooltip; a re-bound chord moves the reserved list with it.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w10-4")]
public sealed class WebW10ShortcutMarkTests(HeadlessSession headless)
{
    private static ShortcutStore FreshStore() => (ShortcutStore)Activator.CreateInstance(typeof(ShortcutStore), nonPublic: true)!;

    private static readonly AppShortcut[] PaneCommands =
    [
        AppShortcut.ToggleBrowser, AppShortcut.ToggleSideChat, AppShortcut.ToggleArtifacts, AppShortcut.ToggleBackgroundTasks, AppShortcut.ClosePane,
        AppShortcut.ExpandPane, AppShortcut.NextPane, AppShortcut.PreviousPane, AppShortcut.MovePaneEarlier, AppShortcut.MovePaneLater, AppShortcut.JoinPaneTabs,
    ];

    [Fact]
    public void Each_of_the_eleven_pane_commands_has_a_recordable_row_and_only_the_reserved_six_carry_the_mark() => headless.OnUiThread(() =>
    {
        using var defaults = UserDefaults.Standard.Override(new InMemorySettingsStore());
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var shared = ShortcutStore.Shared.Override(FreshStore());
        var window = new Avalonia.Controls.Window { Width = 560, Height = 1200, Content = new ShortcutsSettingsPage() };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var rows = window.GetVisualDescendants().OfType<ShortcutEntryRow>().ToList();
            foreach (var command in PaneCommands)
                Assert.Contains(rows, row => row.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == command.Title()));

            var marked = rows.Where(row => row.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == ShortcutEntryRow.WorksInsidePagesMark && text.IsEffectivelyVisible)).ToList();
            Assert.Equal(ShortcutScopes.ReservedCommands.Count, marked.Count);
            Assert.Equal(6, marked.Count);
            foreach (var command in ShortcutScopes.ReservedCommands)
            {
                var row = rows.Single(candidate => candidate.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == command.Title()));
                Assert.Contains(row, marked);
                // beside the title: the mark and the title are two children of one grid
                var title = row.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == command.Title());
                var mark = row.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == ShortcutEntryRow.WorksInsidePagesMark);
                Assert.Same(title.Parent, mark.Parent);
                Assert.Equal("Works inside pages", mark.Text);
                Assert.Contains(ShortcutEntryRow.WorksInsidePages, ToolTip.GetTip(title) as string ?? "", StringComparison.Ordinal); // the tooltip keeps the sentence
            }
            Assert.DoesNotContain(marked, row => row.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == AppShortcut.ToggleSideChat.Title()));
            Assert.DoesNotContain(marked, row => row.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == AppShortcut.NewThread.Title()));
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void Re_binding_a_command_moves_the_reserved_chord_with_it_and_clearing_it_reserves_none() => headless.OnUiThread(() =>
    {
        using var defaults = UserDefaults.Standard.Override(new InMemorySettingsStore());
        var store = FreshStore();
        var before = ShortcutScopes.ReservedChords(store);
        var original = store.Chord(AppShortcut.ToggleBrowser);
        Assert.NotNull(original);
        Assert.Contains(original, before);

        var moved = new KeyChord(0x58, ChordModifiers.Control | ChordModifiers.Shift); // Ctrl+Shift+B is the default; Ctrl+Shift+F12 (scan code 0x58) is free
        store.Set(moved, AppShortcut.ToggleBrowser);
        var after = ShortcutScopes.ReservedChords(store);
        Assert.Contains(moved, after);
        Assert.DoesNotContain(original, after);

        store.Set(null, AppShortcut.ToggleBrowser);
        Assert.DoesNotContain(moved, ShortcutScopes.ReservedChords(store));
        Assert.Null(store.Chord(AppShortcut.ToggleBrowser));
    });
}
