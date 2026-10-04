using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Palette;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Xunit;
using static Marrow.App.Tests.Fixtures.CommandPaletteRig;
using AvaloniaWindow = Avalonia.Controls.Window;
using ChromeColors = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Tests.Views.Palette;

// Source: DroppyCode/App/Views/Palette/CommandPalette.swift (B3 52bc792), :133-137 and :151-178 (the chord label).
[Collection("Headless")]
public sealed class CommandPaletteChordTests(HeadlessSession headless)
{
    // Windows notation (OD-0008 B7): upstream's command-key glyphs become Ctrl, Shift, Alt words joined with plus.
    [Fact]
    [Trait("Req", "UI-WIN-033")] // :151-178: every action row carries its default chord in Windows notation, the tour row none
    [Trait("Rule", "R-TEST-4")] // partial proof of UI-WIN-033: the default chords; cleared, remapped, project and thread rows, type and colour are the tests below
    public void Action_rows_show_their_default_chords_in_windows_notation_with_a_selected_thread()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var directory = harness.NewFolder("p");
            using var store = ShortcutStore.Shared.Override(FreshStore());
            harness.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", directory).Url);
                var thread = model.NewThread(project)!;
                model.UpdateThread(thread.Id, value => value with { Title = "An existing chat" }); // a thread row, so no action row is called "New thread" twice
                model.SelectedThreadID = thread.Id;
                var (_, _, palette) = Build(model);
                string? ChordOf(string title) => RowParts(RowTitled(palette, title)) is { Chord.IsVisible: true } parts ? parts.Chord.Text : null;
                Assert.Equal("Ctrl+N", ChordOf("New thread"));
                Assert.Equal("Ctrl+Shift+N", ChordOf("New thread in worktree"));
                Assert.Equal("Ctrl+O", ChordOf("Add project"));
                Assert.Equal("Ctrl+J", ChordOf("Toggle terminal"));
                Assert.Equal("Ctrl+D", ChordOf("Toggle changes"));
                Assert.Equal("Ctrl+Shift+Delete", ChordOf(model.FinishActionTitle));
                Assert.Equal("Ctrl+,", ChordOf("Settings"));
                Assert.Null(ChordOf("Welcome tour")); // the source gives the tour command no chord
            });
        });
    }

    [Theory]
    [InlineData("New thread")]
    [InlineData("Add project")]
    [InlineData("Settings")]
    [Trait("Req", "UI-WIN-033")] // states: a cleared chord hides the label and takes no layout space; a remapped chord shows at the next open
    [Trait("Rule", "R-TEST-4")] // partial proof of UI-WIN-033: cleared and remapped chords
    public void A_cleared_chord_removes_the_label_and_a_remapped_chord_shows_at_the_next_open(string title)
    {
        var shortcut = title switch { "New thread" => AppShortcut.NewThread, "Add project" => AppShortcut.AddProject, _ => AppShortcut.OpenSettings };
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var fresh = FreshStore();
            using var store = ShortcutStore.Shared.Override(fresh);
            harness.Main.OnMain(() =>
            {
                fresh.Set(null, shortcut);
                var (_, window, palette) = Build(model);
                var cleared = RowParts(RowTitled(palette, title));
                Assert.False(cleared.Chord.IsVisible);
                Assert.Equal(0, cleared.Chord.Bounds.Width); // the chord takes no layout space
                // :111, :132: the HStack's 12-point spacing and the Spacer(minLength: 8) stay without the chord (ED-279)
                Assert.Equal(cleared.Grid.Bounds.Width - 20, cleared.TextStack.Bounds.Right, 1);
                window.Close();

                fresh.Set(new KeyChord(0x25, ChordModifiers.Control | ChordModifiers.Alt), shortcut); // Ctrl+Alt+K, remapped
                var (_, _, reopened) = Build(model); // the palette is built anew each time it opens
                var remapped = RowParts(RowTitled(reopened, title));
                Assert.True(remapped.Chord.IsVisible);
                Assert.Equal("Ctrl+Alt+K", remapped.Chord.Text);
            });
        });
    }

    [Fact]
    [Trait("Req", "UI-WIN-033")] // :124-129, :185-195: thread and project rows never carry a chord
    [Trait("Rule", "R-TEST-4")] // partial proof of UI-WIN-033: rows that are not actions
    public void Project_and_thread_rows_never_show_a_chord()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var directory = harness.NewFolder("chordproject");
            AvaloniaWindow window = null!;
            CommandPalette palette = null!;
            string projectName = "";
            harness.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("Chordproject", directory).Url);
                projectName = project.Name;
                model.InsertThread(AppModelSamples.Thread(project) with { Title = "Chordproject thread" });
                (_, window, palette) = Build(model);
                palette.QueryField.Text = "chordproject";
            });
            harness.Main.OnMain(() =>
            {
                Settle(window);
                var titles = Titles(palette);
                Assert.Contains("Chordproject thread", titles);
                Assert.Contains("New thread in " + projectName, titles);
                foreach (var title in new[] { "Chordproject thread", "New thread in " + projectName })
                    Assert.False(RowParts(RowTitled(palette, title)).Chord.IsVisible);
            });
        });
    }

    [Fact]
    [Trait("Req", "UI-WIN-033")] // :133-137: caption size, secondary text colour, at the trailing edge
    [Trait("Rule", "R-TEST-4")] // partial proof of UI-WIN-033: the chord's type and colour
    public void A_chord_is_caption_sized_and_secondary_coloured()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            using var store = ShortcutStore.Shared.Override(FreshStore());
            harness.Main.OnMain(() =>
            {
                var (_, window, palette) = Build(model);
                var chord = RowParts(RowTitled(palette, "New thread")).Chord;
                Assert.Equal(10, chord.FontSize);
                Assert.Equal(Marrow.UI.Port.SwiftFont.Caption.PointSize, chord.FontSize);
                Assert.Equal(ChromeColors.SecondaryText.Resolve(window.ActualThemeVariant),
                    Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(chord.Foreground).Color);
            });
        });
    }

    // The Swift row is HStack(spacing: 12) { icon; text; Spacer(minLength: 8); chord } (:111, :132-137): the chord is the trailing item and the Spacer keeps at
    // least 8 points between the text and it, as little room as the row asks for. A palette row is a fixed 600 points wide, so the narrowest case is made by measuring
    // the row's own background at infinity (the width its content needs, the title untruncated) and arranging it at exactly that width, and then at a wider one.
    // The headless backend cannot shape a title that has to truncate (no Inter glyph typeface), so a row narrower than its content is not run.
    [Theory]
    [InlineData("New thread", 0)]
    [InlineData("New thread in worktree", 0)]
    [InlineData("Add project", 0)]
    [InlineData("Add project", 120)]
    [Trait("Req", "UI-WIN-033")] // :132-137 geometry: the trailing edge of the row, after at least 8 points of space
    [Trait("Req", "UI-WIN-032")] // :111, :132: the text block, at least 8 points of space, then the chord
    [Trait("Rule", "R-TEST-4")] // partial proof of UI-WIN-032 and UI-WIN-033: the chord's place (the floor of 8 points, the 12 + 8 + 12 of the stack and the trailing edge; ED-279)
    public void The_chord_is_the_trailing_item_with_at_least_8_points_between_it_and_the_text_at_the_narrowest_the_row_asks_for(string title, double extraRoom)
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            using var store = ShortcutStore.Shared.Override(FreshStore());
            harness.Main.OnMain(() =>
            {
                var (_, _, palette) = Build(model);
                var row = RowParts(RowTitled(palette, title));
                Assert.True(row.Chord.IsVisible);

                // The narrowest the row asks for is its natural content (extra room of 0 points).
                row.Background.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var natural = row.Background.DesiredSize;
                var width = natural.Width + extraRoom;
                row.Background.Measure(new Size(width, double.PositiveInfinity));
                row.Background.Arrange(new Rect(0, 0, width, row.Background.DesiredSize.Height));

                var chordLeft = row.Chord.TranslatePoint(default, row.Grid)!.Value.X;
                var chordRight = chordLeft + row.Chord.Bounds.Width;
                Assert.True(chordLeft - row.TextStack.Bounds.Right >= 8, $"{chordLeft - row.TextStack.Bounds.Right} points between the text block and the chord at {width} wide");
                // :111 HStack(spacing: 12) puts 12 before the Spacer(minLength: 8) (:132) and 12 after it, before the chord: 32 in all.
                Assert.Equal(32, chordLeft - row.TextStack.Bounds.Right, 1);
                Assert.Equal(row.Grid.Bounds.Width, chordRight, 1); // the chord is the trailing item: its right edge is the row's inner trailing edge
            });
        });
    }
}
