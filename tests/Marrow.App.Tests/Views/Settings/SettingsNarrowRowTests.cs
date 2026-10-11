using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Xunit;
using static Marrow.App.Tests.Fixtures.HydraSettingsPageRig;

namespace Marrow.App.Tests.Views.Settings;

// ED-NEW-textsize-1 (owner, 2026-10-11, Settings small and zoomed in, the Hydra page): the nine-segment "Heads at once" picker
// took the row's width and its title broke inside words ("Hea / ds at / once"), its detail too ("head / s as the job need…").
// A settings row now (1) wraps its words whole, never inside one, (2) stacks the control under the text once the title column
// beside it would be narrower than ChromeRow.MinimumLabelWidth, easing between the layouts, and (3) the Hydra header's
// subtitle wraps instead of being cut. Each page is drawn at 150 % (over the base scale) in a window as wide as the Settings window.
[Collection("Headless")]
public sealed class SettingsNarrowRowTests(HeadlessSession headless)
{
    private const int Zoom = 150;

    // As a window root draws it: the zoom level over the base scale (ED-NEW-textsize-2), 1.95 in all.
    private static Control Zoomed(Control page) => new AppZoomModifier(Zoom) { BaseScale = AppZoom.BaseScale }.Body(page);

    private static Control ControlOf(ChromeRow row) => ((Grid)((Border)row.Child!).Child!).Children[1];

    // Every line of every text in the row ends at a word's end: never a letter on both sides of a break.
    private static void AssertNoBreakInsideAWord(Control root)
    {
        foreach (var text in Of<TextBlock>(root).Where(text => !string.IsNullOrEmpty(text.Text) && text.IsEffectivelyVisible))
        {
            var words = text.Text!;
            var lines = text.TextLayout.TextLines;
            for (var index = 0; index < lines.Count - 1; index++)
            {
                var end = lines[index].FirstTextSourceIndex + lines[index].Length;
                if (end <= 0 || end >= words.Length) continue;
                Assert.False(char.IsLetterOrDigit(words[end - 1]) && char.IsLetterOrDigit(words[end]),
                    $"\"{words}\" breaks inside a word after \"{words[..end]}\" ({lines.Count} lines at {text.Bounds.Width:0.#} wide)");
            }
        }
    }

    // Nothing in the row is drawn past either of its sides.
    private static void AssertInside(ChromeRow row)
    {
        foreach (var part in row.GetVisualDescendants().OfType<Control>().Where(part => part.IsEffectivelyVisible && part.Bounds.Width > 0))
        {
            var left = part.TranslatePoint(default, row)!.Value.X;
            var right = part.TranslatePoint(new Point(part.Bounds.Width, 0), row)!.Value.X;
            Assert.True(left >= -0.5 && right <= row.Bounds.Width + 0.5,
                $"{part.GetType().Name} spans {left:0.#}..{right:0.#} in a row {row.Bounds.Width:0.#} wide");
        }
    }

    private static void AssertStackedAtTheLeadingEdge(ChromeRow row)
    {
        Assert.True(row.IsStacked, "the row did not stack its control under the text");
        var control = ControlOf(row);
        var text = (Control)((Grid)((Border)row.Child!).Child!).Children[0];
        Assert.Equal(text.TranslatePoint(default, row)!.Value.X, control.TranslatePoint(default, row)!.Value.X, 3);
        Assert.True(control.TranslatePoint(default, row)!.Value.Y >= text.TranslatePoint(new Point(0, text.Bounds.Height), row)!.Value.Y,
            "the stacked control is not under the text");
    }

    [Fact]
    [Trait("Decision", "ED-NEW-textsize-1")]
    public void Heads_at_once_stacks_and_breaks_no_word_in_a_narrow_window_at_150_percent() =>
        Run(headless, _ => Zoomed(new HydraSettingsPage()), scene =>
        {
            scene.Window.UpdateLayout();
            var row = Row(scene.View, "Heads at once");
            AssertStackedAtTheLeadingEdge(row);
            AssertNoBreakInsideAWord(row);
            AssertInside(row);
            Assert.Single(Of<TextBlock>(row).First().TextLayout.TextLines); // the title across the whole row is one line
            // The page header's subtitle wraps onto lines of whole words instead of being cut off.
            var subtitle = Of<TextBlock>(scene.View).Single(text => text.Text == "One chat leads a team of heads on big jobs.");
            Assert.Equal(0, subtitle.MaxLines);
            Assert.Equal(TextWrapping.WrapWithOverflow, subtitle.TextWrapping);
            AssertNoBreakInsideAWord(subtitle);
            Assert.True(subtitle.TextLayout.TextLines.Sum(line => line.Length) >= subtitle.Text!.Length, "the subtitle is cut");
        }, width: 780);

    [Fact]
    [Trait("Decision", "ED-NEW-textsize-1")]
    public void An_Appearance_row_stacks_and_breaks_no_word_in_a_narrow_window_at_150_percent() =>
        Run(headless, _ => Zoomed(new AppearanceSettingsPage()), scene =>
        {
            scene.Window.UpdateLayout();
            var row = Row(scene.View, "Sidebar");
            AssertStackedAtTheLeadingEdge(row);
            AssertNoBreakInsideAWord(row);
            AssertInside(row);
            foreach (var other in Of<ChromeRow>(scene.View)) { AssertNoBreakInsideAWord(other); AssertInside(other); }
        }, width: 780);

    [Fact]
    [Trait("Decision", "ED-NEW-textsize-1")]
    public void A_wide_window_keeps_the_control_beside_the_text() =>
        Run(headless, _ => Zoomed(new HydraSettingsPage()), scene =>
        {
            scene.Window.UpdateLayout();
            var row = Row(scene.View, "Heads at once");
            Assert.False(row.IsStacked);
            var control = ControlOf(row);
            Assert.Equal(Avalonia.Layout.HorizontalAlignment.Right, control.HorizontalAlignment);
            AssertNoBreakInsideAWord(row);
            AssertInside(row);
        }, width: 2400);

    [Fact]
    [Trait("Decision", "ED-NEW-textsize-1")]
    public void Narrowing_the_window_across_the_threshold_eases_the_row_into_its_stacked_layout() =>
        Run(headless, _ => Zoomed(new HydraSettingsPage()), scene =>
        {
            scene.Window.UpdateLayout();
            var row = Row(scene.View, "Heads at once");
            Assert.False(row.IsStacked);
            var before = row.Bounds.Height;
            var controlBefore = ControlOf(row).TranslatePoint(default, row)!.Value;

            scene.Window.Width = 780;
            Sync();
            scene.Window.UpdateLayout();
            Assert.True(row.IsStacked);
            // The first frame after the change: the row and its control are where they were, not at their new places.
            Assert.Equal(before, row.Bounds.Height, 3);
            Assert.Equal(controlBefore.Y, ControlOf(row).TranslatePoint(default, row)!.Value.Y, 3);

            Advance(0.1);
            scene.Window.UpdateLayout();
            var midway = row.Bounds.Height;
            Advance(0.4);
            scene.Window.UpdateLayout();
            var after = row.Bounds.Height;
            Assert.True(after > before, $"the stacked row ({after}) is not taller than the row beside ({before})");
            Assert.InRange(midway, before + 0.5, after - 0.5); // on its way: no snap
            Assert.Null(ControlOf(row).RenderTransform); // at rest the control carries no move
            AssertStackedAtTheLeadingEdge(row);
            AssertInside(row);
        }, width: 2400);
}
