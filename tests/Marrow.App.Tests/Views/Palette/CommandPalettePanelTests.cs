using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.App.Views.Palette;
using Marrow.UI.Port;
using Xunit;
using static Marrow.App.Tests.Fixtures.CommandPaletteRig;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Palette;

// Source: DroppyCode/App/Views/Palette/CommandPalette.swift (B3 52bc792), :42 and :96-98 (the palette panel).
[Collection("Headless")]
public sealed class CommandPalettePanelTests(HeadlessSession headless)
{
    private static Border PanelOf(CommandPalette palette) => Assert.IsType<Border>(Assert.IsType<Panel>(palette.Child).Children[1]);

    // Partial proof of UI-WIN-029 (the interaction clause); the geometry and glass clauses are the test below.
    [Fact]
    [Trait("Req", "UI-WIN-029")] // :42, :96-98
    [Trait("Rule", "R-TEST-4")]
    public void A_press_inside_the_panel_never_dismisses_but_one_on_the_scrim_does()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var (_, window, palette) = Build(model, null, 900, 700);
                model.IsCommandPalettePresented = true;
                var panel = PanelOf(palette);
                var firstRow = palette.RowsPanel.Children[0];
                var lastRow = palette.RowsPanel.Children[^1];
                // Panel-local points in every blank band: the 18-pt side inset and the 15-pt top inset of the search row,
                // the 2-pt gap between two rows, the 6-pt inset beside the rows and under the last row.
                var points = new[]
                {
                    new Point(9, 40),
                    new Point(300, 7),
                    firstRow.TranslatePoint(new Point(firstRow.Bounds.Width / 2, firstRow.Bounds.Height + 1), panel)!.Value,
                    firstRow.TranslatePoint(new Point(-3, 10), panel)!.Value,
                    lastRow.TranslatePoint(new Point(lastRow.Bounds.Width / 2, lastRow.Bounds.Height + 3), panel)!.Value,
                };
                foreach (var local in points)
                {
                    var point = panel.TranslatePoint(local, window)!.Value;
                    var hit = Assert.IsAssignableFrom<Visual>(window.InputHitTest(point));
                    Assert.True(ReferenceEquals(hit, panel) || panel.IsVisualAncestorOf(hit), $"{local} lands on the scrim, not the panel");
                    Click(window, point);
                    Assert.True(model.IsCommandPalettePresented, $"a press at panel point {local} dismissed the palette");
                }

                Click(window, new Point(24, 0.85 * 700)); // outside the panel: the scrim
                Assert.False(model.IsCommandPalettePresented);
            });
        });
    }

    // Partial proof of UI-WIN-029 (geometry and glass); together with the test above every clause of the record is asserted.
    [Fact]
    [Trait("Req", "UI-WIN-029")] // :96-98
    [Trait("Rule", "R-TEST-4")]
    public void The_panel_is_centred_600_wide_72_from_the_top_and_grows_with_its_rows_with_untinted_continuous_glass()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", harness.NewFolder("p")).Url);
                for (var index = 0; index < 8; index++) model.InsertThread(AppModelSamples.Thread(project) with { Title = "Thread " + index });
                var (_, window, palette) = Build(model, null, 900, 700);
                var panel = PanelOf(palette);
                Assert.True(palette.RowsPanel.Children.Count >= 11);
                Assert.Equal(150, panel.Bounds.X);
                Assert.Equal(72, panel.Bounds.Y);
                Assert.Equal(600, panel.Bounds.Width);
                Assert.True(double.IsPositiveInfinity(panel.MaxHeight));
                Assert.Empty(LogicalQuery.SelfAndDescendants<ScrollViewer>(palette));

                var effect = SwiftGlass.GetEffect(panel)!;
                Assert.Null(effect.TintColor);
                Assert.False(effect.IsInteractive);
                Assert.Null(SwiftGlass.GetContainerSpacing(panel));
                Assert.Equal(GlassShapeKind.RoundedRectangle, effect.Shape.Kind);
                Assert.Equal(24, effect.Shape.CornerRadius);
                Assert.Equal(RoundedCornerStyle.Continuous, effect.Shape.Style);

                // Twelve rows in a window too short to hold them: the panel simply overflows (the glass keeps the content's height).
                window.Height = 300;
                Settle(window);
                var content = Assert.IsType<StackPanel>(panel.Child);
                var rowsBottom = palette.RowsPanel.TranslatePoint(new Point(0, palette.RowsPanel.Bounds.Height), window)!.Value.Y;
                Assert.True(rowsBottom > 300, $"the rows overflow the short window (bottom {rowsBottom})");
                Assert.Equal(content.DesiredSize.Height, panel.Bounds.Height);
            });
        });
    }
}
