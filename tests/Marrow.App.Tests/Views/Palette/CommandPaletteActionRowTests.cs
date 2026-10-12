using System;
using System.Linq;
using Avalonia.Controls;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Palette;
using Marrow.Core.Models;
using Marrow.UI.Port;
using Xunit;
using static Marrow.App.Tests.Fixtures.CommandPaletteRig;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Palette;

// Source: DroppyCode/App/Views/Palette/CommandPalette.swift (B3 52bc792), :164-174 (the selected-thread action rows).
// UI-GIT-025 is the "Toggle changes" row, UI-TERM-033 the "Toggle terminal" row; both are the same row recipe, so one
// Theory proves both: only with a thread selected, after the previous action, its symbol, its live chord, its effect.
[Collection("Headless")]
public sealed class CommandPaletteActionRowTests(HeadlessSession headless)
{
    private void ProveRow(AppShortcut shortcut, string title, string symbol, string chord) => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        var directory = harness.NewFolder("p");
        using var store = ShortcutStore.Shared.Override(FreshStore());
        AvaloniaWindow window = null!;
        CommandPalette palette = null!;
        harness.Main.OnMain(() =>
        {
            // No thread selected: the row is absent (:164 guard).
            var (_, bare, barePalette) = Build(model);
            Assert.DoesNotContain(title, Titles(barePalette));
            bare.Close();

            var project = model.AddProject(new Project("P", directory).Url);
            model.SelectedThreadID = model.NewThread(project)!.Id;
            (_, window, palette) = Build(model);
            var titles = Titles(palette);
            var index = Array.IndexOf(titles, title);
            Assert.True(index >= 0, title + " is present once a thread is selected");

            // The row's title is the shortcut's own title in Settings.
            Assert.Equal(title, shortcut.Title());

            // Order: terminal, then changes, then the finish row (:166-174).
            if (shortcut == AppShortcut.ToggleChanges)
            {
                Assert.Equal("Toggle terminal", titles[index - 1]);
                Assert.Equal(model.FinishActionTitle, titles[index + 1]);
            }
            else
            {
                Assert.Equal("Toggle changes", titles[index + 1]);
                Assert.NotEqual("Toggle changes", titles[index - 1]);
            }

            var parts = RowParts(palette.RowsPanel.Children[index]);
            Assert.Equal(symbol, Assert.IsType<SwiftImage>(Assert.Single(parts.Icon.Children)).SymbolName);
            Assert.Equal(chord, parts.Chord.Text);
            Assert.True(parts.Chord.IsVisible);
            Assert.False(parts.Subtitle.IsVisible); // an action row has no subtitle
        });
        harness.Main.OnMain(() =>
        {
            var runtime = model.Runtime(model.SelectedThreadID!.Value);
            // Web W4 (ED-NEW-web-w4-1): Toggle changes opens the Changes pane (never the popover, which stays shut); Toggle terminal flips the terminal.
            bool State()
            {
                if (shortcut != AppShortcut.ToggleChanges) return runtime.IsTerminalVisible;
                Assert.False(runtime.IsDiffVisible);
                return runtime.IsChangesPaneOpen;
            }
            Assert.False(State());

            for (var pass = 1; pass <= 2; pass++)
            {
                model.IsCommandPalettePresented = true;
                Click(window, Centre(RowTitled(palette, title), window));
                Assert.False(model.IsCommandPalettePresented); // :204 closes first
                Assert.Equal(pass == 1, State()); // the first run turns the drawer on, the second off
            }
        });
    });

    private void ProveClearedChord(AppShortcut shortcut, string title, string other) => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        var directory = harness.NewFolder("p");
        var fresh = FreshStore();
        using var store = ShortcutStore.Shared.Override(fresh);
        harness.Main.OnMain(() =>
        {
            fresh.Set(null, shortcut);
            var project = model.AddProject(new Project("P", directory).Url);
            model.SelectedThreadID = model.NewThread(project)!.Id;
            var (_, _, palette) = Build(model);
            Assert.False(RowParts(RowTitled(palette, title)).Chord.IsVisible);
            Assert.True(RowParts(RowTitled(palette, other)).Chord.IsVisible); // the neighbouring row keeps its own chord
        });
    });

    [Fact]
    [Trait("Req", "UI-GIT-025")] // CommandPalette.swift:169-171: absent without a thread, after terminal, plusminus symbol, Ctrl+D, flips the changes drawer
    public void The_toggle_changes_row_has_its_symbol_live_chord_and_runs_its_effect() =>
        ProveRow(AppShortcut.ToggleChanges, "Toggle changes", "plusminus", "Ctrl+D");

    [Fact]
    [Trait("Req", "UI-TERM-033")] // CommandPalette.swift:164-168: absent without a thread, terminal symbol, Ctrl+J, flips the terminal drawer
    public void The_toggle_terminal_row_has_its_symbol_live_chord_and_runs_its_effect() =>
        ProveRow(AppShortcut.ToggleTerminal, "Toggle terminal", "terminal", "Ctrl+J");

    [Fact]
    [Trait("Req", "UI-GIT-025")] // the cleared-chord state of the Toggle changes row
    public void The_toggle_changes_row_hides_its_chord_when_the_chord_is_cleared() =>
        ProveClearedChord(AppShortcut.ToggleChanges, "Toggle changes", "Toggle terminal");

    [Fact]
    [Trait("Req", "UI-TERM-033")] // states[1]: the cleared-chord state of the Toggle terminal row
    public void The_toggle_terminal_row_hides_its_chord_when_the_chord_is_cleared() =>
        ProveClearedChord(AppShortcut.ToggleTerminal, "Toggle terminal", "Toggle changes");
}
