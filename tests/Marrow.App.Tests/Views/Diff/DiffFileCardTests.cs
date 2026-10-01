using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Diff;
using Marrow.Services.Git;
using Marrow.UI.Port;
using Xunit;
using static Marrow.App.Tests.Fixtures.DiffInspectorSamples;
using static Marrow.App.Tests.Fixtures.DiffInspectorTree;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Diff;

// Source: DroppyCode/App/Views/Diff/DiffInspector.swift (B3 52bc792), :175-250 (a file's card).
[Collection("Headless")]
public sealed class DiffFileCardTests(HeadlessSession headless)
{
    private static DiffFile WithHunk() =>
        FileOf(Hunk("@@ -1,2 +1,2 @@", Line(DiffLine.Kind.Deletion, "old", 1, null), Line(DiffLine.Kind.Addition, "new", null, 1)));

    private static AvaloniaWindow Show(Control content, double width = 500)
    {
        var window = new AvaloniaWindow { Width = width, Height = 400, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        window.UpdateLayout();
        return window;
    }

    [Fact]
    [Trait("Req", "UI-GIT-015")] // App/Views/Diff/DiffInspector.swift:192-231 (a vertical stack with spacing 0 on a continuous rounded rectangle, radius 12, filled with the quaternary fill at 0.3)
    public void The_card_is_a_stack_on_a_rounded_rectangle_of_radius_12_filled_at_a_third_of_the_quaternary_fill() => headless.OnUiThread(() =>
    {
        var card = new DiffFileCard(WithHunk(), isCollapsed: false, () => { });

        var background = Assert.IsType<Border>(card.Child);
        Assert.Equal(new CornerRadius(12), background.CornerRadius);
        Assert.Equal(((ISolidColorBrush)SwiftColor.Quaternary.Opacity(0.3).Brush(card)).Color, Assert.IsAssignableFrom<ISolidColorBrush>(background.Background).Color);
        var stack = Assert.IsType<StackPanel>(background.Child);
        Assert.Equal(0, stack.Spacing);
        Assert.Equal(Avalonia.Layout.Orientation.Vertical, stack.Orientation);
    });

    [Fact]
    [Trait("Req", "UI-GIT-015")] // App/Views/Diff/DiffInspector.swift:215-226 (collapsed: only the header; expanded with hunks: the line list; binary or hunkless: a short caption)
    public void A_card_shows_its_header_alone_when_collapsed_and_the_lines_or_a_caption_when_expanded() => headless.OnUiThread(() =>
    {
        var collapsed = new DiffFileCard(WithHunk(), isCollapsed: true, () => { });
        Assert.Single(Descendants<Button>(collapsed));
        Assert.Empty(Descendants<DiffLinesView>(collapsed));
        Assert.Single(Assert.IsType<StackPanel>(Assert.IsType<Border>(collapsed.Child).Child).Children);

        var expanded = new DiffFileCard(WithHunk(), isCollapsed: false, () => { });
        Assert.Single(Descendants<DiffLinesView>(expanded));
        Assert.DoesNotContain("Binary file", Texts(expanded));
        Assert.DoesNotContain("Diff details unavailable", Texts(expanded));

        var binary = new DiffFileCard(WithHunk() with { IsBinary = true }, isCollapsed: false, () => { });
        Assert.Contains("Binary file", Texts(binary));
        Assert.Empty(Descendants<DiffLinesView>(binary));

        var hunkless = new DiffFileCard(FileOf(), isCollapsed: false, () => { });
        Assert.Contains("Diff details unavailable", Texts(hunkless));
        Assert.Empty(Descendants<DiffLinesView>(hunkless));
    });

    [Fact]
    [Trait("Req", "UI-GIT-015")] // App/Views/Diff/DiffInspector.swift:194-213 (the header is drawn, and a click anywhere in it, even on empty space, toggles collapse)
    public void The_header_is_drawn_and_a_click_anywhere_in_it_toggles_collapse() => headless.OnUiThread(() =>
    {
        var toggles = 0;
        var card = new DiffFileCard(WithHunk(), isCollapsed: true, () => toggles++);
        var window = Show(card);
        try
        {
            // What the header says is on screen, in the card's visual tree.
            Assert.Contains(card.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "File.swift");
            var header = Assert.Single(card.GetVisualDescendants().OfType<Button>());
            Assert.True(header.Bounds.Width > 400, "the header spans the card");

            // The empty stretch between the path and the statistics belongs to the header too (:211 contentShape(.rect)).
            var onSpace = header.TranslatePoint(new Point(header.Bounds.Width / 2, header.Bounds.Height / 2), window)!.Value;
            window.MouseDown(onSpace, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(onSpace, MouseButton.Left, RawInputModifiers.None);

            Assert.Equal(1, toggles);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "UI-GIT-015")] // App/Views/Diff/DiffInspector.swift:182-190 (the card compares itself on path, counts, hunk count, change kind, binary flag and collapse state only)
    public void A_card_compares_equal_on_its_cheap_fields_alone() => headless.OnUiThread(() =>
    {
        DiffFile Of(string path = "a.txt", DiffLine.Kind kind = DiffLine.Kind.Addition, int hunks = 1, DiffFile.Change change = DiffFile.Change.Modified, bool binary = false, string text = "x") =>
            new(path, change, binary, new Marrow.Core.Port.ValueList<DiffHunk>(Enumerable.Range(0, hunks).Select(_ => Hunk("@@", Line(kind, text, 1, 1)))));
        DiffFileCard Card(DiffFile file, bool collapsed = false, Action? toggle = null) => new(file, collapsed, toggle ?? (() => { }));

        var baseline = Card(Of());
        Assert.True(DiffFileCard.InputsEqual(baseline, Card(Of(text: "other text"), toggle: () => { })));

        Assert.False(DiffFileCard.InputsEqual(baseline, Card(Of(path: "b.txt"))));
        Assert.False(DiffFileCard.InputsEqual(baseline, Card(Of(kind: DiffLine.Kind.Context)))); // one addition fewer
        Assert.False(DiffFileCard.InputsEqual(Card(Of(kind: DiffLine.Kind.Deletion)), Card(Of(kind: DiffLine.Kind.Context)))); // one deletion fewer
        Assert.False(DiffFileCard.InputsEqual(baseline, Card(Of(hunks: 2, kind: DiffLine.Kind.Addition)))); // one hunk more (and one more addition)
        Assert.False(DiffFileCard.InputsEqual(Card(Of(kind: DiffLine.Kind.Context)), Card(Of(kind: DiffLine.Kind.Context, hunks: 2)))); // only the hunk count differs
        Assert.False(DiffFileCard.InputsEqual(baseline, Card(Of(change: DiffFile.Change.Renamed))));
        Assert.False(DiffFileCard.InputsEqual(baseline, Card(Of(binary: true))));
        Assert.False(DiffFileCard.InputsEqual(baseline, Card(Of(), collapsed: true)));
    });
}
