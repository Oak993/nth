using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Input;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Palette;
using Marrow.UI.Port;
using Xunit;
using static Marrow.App.Tests.Fixtures.CommandPaletteRig;
using AvaloniaWindow = Avalonia.Controls.Window;
using ChromeColors = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Tests.Views.Palette;

// Source: DroppyCode/App/Views/Palette/CommandPalette.swift (B3 52bc792), :43-62 (the search row) and :100-101.
// UI-WIN-030 is proven by the two tests below together with the existing PR-WIN-161..165 tests in CommandPaletteTests
// (exact placeholder and no clear button, focus on appear, clamped arrows, Escape with a query, Return runs).
[Collection("Headless")]
public sealed class CommandPaletteSearchRowTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "UI-WIN-030")] // :43-49, :64-65: row insets 18 by 15, 10-point gap, magnifier then field, title3 text, secondary magnifier, no clear button
    [Trait("Rule", "R-TEST-4")] // partial proof of UI-WIN-030: the row's layout and type; the key and selection clauses are the test below
    public void The_search_row_is_inset_18_by_15_with_a_10_point_gap_title3_text_and_a_secondary_magnifier()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var (_, window, palette) = Build(model);
                var field = palette.QueryField;
                var searchRow = Assert.IsType<Grid>(field.Parent); // an HStack: the magnifier at its own width, the field taking the rest
                Assert.Equal(new Thickness(18, 15, 18, 15), searchRow.Margin);
                Assert.Equal(2, searchRow.Children.Count);
                var magnifier = Assert.IsType<SwiftImage>(searchRow.Children[0]);
                Assert.Equal("magnifyingglass", magnifier.SymbolName);
                Assert.Same(field, searchRow.Children[1]);
                Assert.Equal(SwiftFont.Title3.PointSize, field.FontSize);
                Assert.Equal(15, field.FontSize); // ED-059 title3; the visual size is UNVERIFIED under OD-0001
                Assert.Equal(ChromeColors.SecondaryText.Resolve(magnifier.ActualThemeVariant),
                    Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(magnifier.Foreground).Color);
                Assert.Empty(LogicalQuery.SelfAndDescendants<Button>(searchRow)); // no clear button anywhere in the row
                // magnifier, then 10 points, then the field, which fills what is left of the row (a text field is flexible in an HStack)
                Assert.Equal(magnifier.Bounds.Right + 10, field.Bounds.X, 1);
                Assert.Equal(searchRow.Bounds.Width, field.Bounds.Right, 1);
                Assert.Equal(564, searchRow.Bounds.Width, 1); // 600 minus the 18-point insets
            });
        });
    }

    [Fact]
    [Trait("Req", "UI-WIN-030")] // :50-62, :101: typing resets the selection to the first row; Up/Down and Escape are consumed, Escape keeps the text
    [Trait("Rule", "R-TEST-4")] // partial proof of UI-WIN-030: the keys; the layout is the test above
    public void Typing_resets_the_selection_and_the_arrow_keys_never_reach_the_text_box()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            AvaloniaWindow window = null!;
            CommandPalette palette = null!;
            harness.Main.OnMain(() =>
            {
                (_, window, palette) = Build(model);
                Press(window, Key.Down, PhysicalKey.ArrowDown);
                Press(window, Key.Down, PhysicalKey.ArrowDown);
            });
            harness.Main.OnMain(() =>
            {
                Assert.Equal(2, palette.Selection);
                window.KeyTextInput("n"); // typing into the focused field
            });
            harness.Main.OnMain(() =>
            {
                Settle(window);
                Assert.Equal("n", palette.QueryField.Text);
                Assert.Equal(0, palette.Selection); // :101 onChange(of: query) { selection = 0 }

                // Up and Down are consumed by the palette: the caret does not move (a single-line text box would
                // send the caret to the start on Up and to the end on Down).
                palette.QueryField.Text = "abcdef";
            });
            harness.Main.OnMain(() =>
            {
                Settle(window);
                var consumed = new System.Collections.Generic.List<(Key Key, bool Handled)>();
                window.AddHandler(InputElement.KeyDownEvent, (_, e) => consumed.Add((e.Key, e.Handled)), Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
                palette.QueryField.CaretIndex = 3;
                Press(window, Key.Up, PhysicalKey.ArrowUp);
                Assert.Equal(3, palette.QueryField.CaretIndex);
                Press(window, Key.Down, PhysicalKey.ArrowDown);
                Assert.Equal(3, palette.QueryField.CaretIndex);
                Assert.Equal([(Key.Up, true), (Key.Down, true)], consumed); // both arrows are consumed (:51-58 return .handled)

                // Escape closes without clearing what was typed.
                model.IsCommandPalettePresented = true;
                Press(window, Key.Escape, PhysicalKey.Escape, "\u001b");
                Assert.False(model.IsCommandPalettePresented);
                Assert.Equal("abcdef", palette.QueryField.Text);
            });
        });
    }
}
