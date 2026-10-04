using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Palette;
using Marrow.Core.Models;
using Xunit;
using static Marrow.App.Tests.Fixtures.CommandPaletteRig;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Palette;

// REVIEW (adversarial review of unit/blitz-tail, UI-WIN-032 geometry "a text block ..., at least 8 points of space, then the chord").
// CommandPalette.swift:110-137 builds every row as HStack(spacing: 12) { icon; VStack(text); Spacer(minLength: 8); if let shortcut { chord } }:
// the Spacer is unconditional, so a row with NO chord (every thread row and every project row, :185-195) still keeps at least 8 points
// (with the stack's spacing, 12 + 8) between its text block and the row's trailing edge. The port's only space after the text is the
// chord's own left margin (CommandPalette.cs `chordBlock.Margin = new Thickness(20, 0, 0, 0)`), which a hidden chord does not take, so a
// thread whose title fills its room runs the text block up to the row's trailing edge: 0 points of space.
[Collection("Headless")]
public sealed class CommandPaletteChordReviewTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "UI-WIN-032")] // CommandPalette.swift:110-137, :132 Spacer(minLength: 8) is not inside the `if let shortcut`
    [Trait("Rule", "R-TEST-4")] // partial proof of UI-WIN-032: the space after the text block on a row with no chord
    public void REVIEW_a_thread_row_whose_title_fills_its_room_keeps_at_least_8_points_after_the_text_block()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var directory = harness.NewFolder("reviewproject");
            var title = "Reviewlong " + string.Join(" ", Enumerable.Repeat("a thread title far longer than the palette row has room for", 4));
            AvaloniaWindow window = null!;
            CommandPalette palette = null!;
            harness.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("Reviewproject", directory).Url);
                model.InsertThread(AppModelSamples.Thread(project) with { Title = title });
                (_, window, palette) = Build(model);
                palette.QueryField.Text = "reviewlong";
            });
            harness.Main.OnMain(() =>
            {
                Settle(window);
                var row = RowParts(RowTitled(palette, title));
                Assert.False(row.Chord.IsVisible); // a thread row never carries a chord (:185-189)

                // The title's own width is far wider than the row: it fills all the room the row gives the text block.
                var probe = new TextBlock { Text = title, FontSize = row.Title.FontSize, FontFamily = row.Title.FontFamily, FontWeight = row.Title.FontWeight };
                probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Assert.True(probe.DesiredSize.Width > row.Grid.Bounds.Width, $"title {probe.DesiredSize.Width} wide in a {row.Grid.Bounds.Width} row");

                var after = row.Grid.Bounds.Width - row.TextStack.Bounds.Right;
                Assert.True(after >= 8, $"{after} points between the text block and the row's trailing edge on a row with no chord");
            });
        });
    }
}
