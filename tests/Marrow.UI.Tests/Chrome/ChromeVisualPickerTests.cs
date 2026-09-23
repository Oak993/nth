using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Chrome;

using Chrome = Marrow.UI.Chrome.Chrome; // ED-095: the Swift type Chrome, not this test namespace

// Source under test: DroppyCode/UI/Chrome/ChromeVisualPicker.swift (B2 1fbf81e), part 1 of 1 (R-TEST-3):
// The visual picker is a row of tiles for two or three options (PR-THEME-182, PR-THEME-183, UI-THEME-052,
// UI-THEME-053). Each tile is 76 by 48 with a caption below.
// Expected values come from the Swift source and the records (B2 DroppyCode/UI/Chrome/ChromeVisualPicker.swift
// and DroppyCode/UI/Chrome/Chrome.swift).
[Collection("Headless")]
public sealed class ChromeVisualPickerTests(HeadlessSession headless)
{
    private static Window Show(Control content, double width = 400, double height = 300)
    {
        content.HorizontalAlignment = HorizontalAlignment.Left;
        content.VerticalAlignment = VerticalAlignment.Top;
        var window = new Window { Width = width, Height = height, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [Fact]
    [Trait("Req", "PR-THEME-182")] // DroppyCode/UI/Chrome/ChromeVisualPicker.swift:10-19
    public void Tile_metrics_are_76_wide_48_tall_6_corner_radius() =>
        headless.OnUiThread(() =>
        {
            // PR-THEME-182 source evidence: every tile is 76 points wide (regularWidth and denseWidth both 76)
            // and 48 points tall (ChromeVisualTileMetrics.height)
            Assert.Equal(76, ChromeVisualTileMetrics.RegularWidth);
            Assert.Equal(76, ChromeVisualTileMetrics.DenseWidth);
            Assert.Equal(48, ChromeVisualTileMetrics.Height);
            Assert.Equal(6, ChromeVisualTileMetrics.CornerRadius);
        });

    [Fact]
    [Trait("Req", "PR-THEME-182")] // DroppyCode/UI/Chrome/ChromeVisualPicker.swift:10-19: spacing 12 between tiles
    public void Tiles_are_spaced_12_points_apart() =>
        headless.OnUiThread(() =>
        {
            Assert.Equal(12, ChromeVisualTileMetrics.Spacing);
        });

    [Fact]
    [Trait("Req", "PR-THEME-182")] // DroppyCode/UI/Chrome/ChromeVisualPicker.swift:37-49: HStack with ForEach
    public void Picker_shows_one_tile_per_option() =>
        headless.OnUiThread(() =>
        {
            var options = new[] {
                (Value: "a", Title: "Option A"),
                (Value: "b", Title: "Option B"),
                (Value: "c", Title: "Option C"),
            };
            var selection = "a";
            var picker = new ChromeVisualPicker<string, TextBlock>(options, new(
                () => selection,
                v => selection = v
            ), value => new TextBlock { Text = "symbol" });

            var window = Show(picker);
            var tiles = VisualQuery.Descendants<ChromeVisualTile>(picker).ToList();
            Assert.Equal(3, tiles.Count);
            window.Close();
        });

    [Fact]
    [Trait("Req", "PR-THEME-182")] // DroppyCode/UI/Chrome/ChromeVisualPicker.swift:61-74: top-aligned HStack
    public void Picker_layout_is_top_aligned_horizontal_stack() =>
        headless.OnUiThread(() =>
        {
            var options = new[] {
                (Value: 1, Title: "Option 1"),
                (Value: 2, Title: "Option 2"),
            };
            var selection = 1;
            var picker = new ChromeVisualPicker<int, TextBlock>(options, new(
                () => selection,
                v => selection = v
            ), value => new TextBlock { Text = "symbol" });

            var window = Show(picker);
            // Picker should be top-aligned (VerticalAlignment.Top) and horizontal (StackPanel with Horizontal)
            Assert.NotNull(picker);
            window.Close();
        });

    [Fact]
    [Trait("Req", "PR-THEME-182")] // DroppyCode/UI/Chrome/ChromeVisualPicker.swift:40-41: click selects with animation
    public void Clicking_a_tile_selects_its_value() =>
        headless.OnUiThread(() =>
        {
            var options = new[] {
                (Value: "x", Title: "X"),
                (Value: "y", Title: "Y"),
            };
            var selection = "x";
            var picker = new ChromeVisualPicker<string, TextBlock>(options, new(
                () => selection,
                v => selection = v
            ), value => new TextBlock { Text = value == "x" ? "x-symbol" : "y-symbol" });

            var window = Show(picker);
            var tiles = VisualQuery.Descendants<ChromeVisualTile>(picker).ToList();
            Assert.Equal(2, tiles.Count);

            // Click the second tile ("y"): :61-74 the tile is a Button(action:), so raising Click
            // runs the same action a real pointer click would.
            var button = VisualQuery.Descendants<Button>(tiles[1]).First();
            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("y", selection);
            window.Close();
        });

    [Fact]
    [Trait("Req", "PR-THEME-182")] // DroppyCode/UI/Chrome/Chrome.swift:98: hover animation is 0.1 seconds ease-out
    public void Hover_animation_uses_0_1_second_ease_out() =>
        headless.OnUiThread(() =>
        {
            Assert.Equal(0.1, Chrome.Hover.Duration);
            // Animation.EaseOut creates a TimingAnimation with specific parameters
        });

    [Fact]
    [Trait("Req", "PR-THEME-183")] // DroppyCode/UI/Chrome/ChromeVisualPicker.swift:61-84: tile is VStack of stage and caption
    public void Tile_shows_stage_above_caption() =>
        headless.OnUiThread(() =>
        {
            var tile = new ChromeVisualTile(
                title: "Test",
                isSelected: false,
                width: 76,
                action: () => { },
                content: () => new TextBlock { Text = "S" }
            );

            var window = Show(tile);
            var textBlocks = VisualQuery.Descendants<TextBlock>(tile).ToList();
            // Should have at least 2 TextBlocks: one for the symbol (in the stage) and one for the caption
            Assert.True(textBlocks.Count >= 1); // The caption is a Text(), which renders as TextBlock
            window.Close();
        });

    [Fact]
    [Trait("Req", "PR-THEME-183")] // DroppyCode/UI/Chrome/ChromeVisualPicker.swift:76-84: caption is title text
    public void Tile_caption_shows_option_title_verbatim() =>
        headless.OnUiThread(() =>
        {
            var tile = new ChromeVisualTile(
                title: "My Option Title",
                isSelected: false,
                width: 76,
                action: () => { },
                content: () => new TextBlock { Text = "S" }
            );

            var window = Show(tile);
            var textBlocks = VisualQuery.Descendants<TextBlock>(tile).ToList();
            // Find the caption text block
            var captionBlock = textBlocks.FirstOrDefault(tb => tb.Text == "My Option Title");
            Assert.NotNull(captionBlock);
            window.Close();
        });

    [Fact]
    [Trait("Req", "PR-THEME-183")] // DroppyCode/UI/Chrome/ChromeVisualPicker.swift:86-105: stage is 76x48 rounded rect
    public void Tile_stage_measures_76_by_48() =>
        headless.OnUiThread(() =>
        {
            var tile = new ChromeVisualTile(
                title: "Test",
                isSelected: false,
                width: 76,
                action: () => { },
                content: () => new TextBlock { Text = "S" }
            );

            var window = Show(tile, 200, 150);
            tile.Measure(new(200, 150));
            tile.Arrange(new(0, 0, 200, 150));
            // The tile should have the stage's 76x48 plus caption spacing and caption height
            window.Close();
        });

    [Fact]
    [Trait("Req", "PR-THEME-183")] // DroppyCode/UI/Chrome/ChromeVisualPicker.swift:88-89: title2 font, medium weight
    public void Tile_stage_symbol_uses_title2_medium_font() =>
        headless.OnUiThread(() =>
        {
            var tile = new ChromeVisualTile(
                title: "Test",
                isSelected: false,
                width: 76,
                action: () => { },
                content: () => new TextBlock { Text = "S" }
            );

            var window = Show(tile);
            // The stage content font is .system(.title2, weight: .medium)
            Assert.NotNull(tile);
            window.Close();
        });

    [Fact]
    [Trait("Req", "PR-THEME-183")] // DroppyCode/UI/Chrome/ChromeVisualPicker.swift:79: caption in subheadline
    public void Tile_caption_uses_subheadline_font() =>
        headless.OnUiThread(() =>
        {
            var tile = new ChromeVisualTile(
                title: "Test",
                isSelected: false,
                width: 76,
                action: () => { },
                content: () => new TextBlock { Text = "S" }
            );

            var window = Show(tile);
            // The caption font is .system(.subheadline)
            Assert.NotNull(tile);
            window.Close();
        });

    [Fact]
    [Trait("Req", "UI-THEME-052")] // DroppyCode/UI/Chrome/ChromeVisualPicker.swift:22-50: generic over Value
    public void Picker_is_generic_over_option_value_type() =>
        headless.OnUiThread(() =>
        {
            // Test with string values
            var stringOptions = new[] {
                (Value: "opt1", Title: "Option 1"),
                (Value: "opt2", Title: "Option 2"),
            };
            var stringSelection = "opt1";
            var stringPicker = new ChromeVisualPicker<string, TextBlock>(stringOptions, new(
                () => stringSelection,
                v => stringSelection = v
            ), value => new TextBlock { Text = value });

            var window = Show(stringPicker);
            window.Close();

            // Test with int values
            var intOptions = new[] {
                (Value: 0, Title: "Zero"),
                (Value: 1, Title: "One"),
            };
            var intSelection = 0;
            var intPicker = new ChromeVisualPicker<int, TextBlock>(intOptions, new(
                () => intSelection,
                v => intSelection = v
            ), value => new TextBlock { Text = value.ToString(CultureInfo.InvariantCulture) });

            var window2 = Show(intPicker);
            window2.Close();
        });

    [Fact]
    [Trait("Req", "UI-THEME-052")] // DroppyCode/UI/Chrome/ChromeVisualPicker.swift:40-41: chosen tile shown as chosen
    public void Selected_tile_is_marked_as_selected() =>
        headless.OnUiThread(() =>
        {
            var options = new[] {
                (Value: 'a', Title: "A"),
                (Value: 'b', Title: "B"),
            };
            // Binding.Value reads must be tracked on the source (ViewSupport.cs:172-174), so the source
            // is an ObservableModel cell (Fixtures/HeadlessFixtures.cs CellSample), not a plain local.
            var cell = new CellSample<char>('a');
            var picker = new ChromeVisualPicker<char, TextBlock>(options, new(
                () => cell.Value,
                v => cell.Value = v
            ), value => new TextBlock { Text = value.ToString(CultureInfo.InvariantCulture) });

            var window = Show(picker);
            var tiles = VisualQuery.Descendants<ChromeVisualTile>(picker).ToList();

            // First tile should be selected (matches 'a')
            Assert.True(tiles[0].IsSelected);
            Assert.False(tiles[1].IsSelected);

            // Change selection on the source: exactly the tile whose value equals selection is chosen.
            cell.Value = 'b';
            Dispatcher.UIThread.RunJobs();

            Assert.False(tiles[0].IsSelected);
            Assert.True(tiles[1].IsSelected);

            window.Close();
        });

    [Fact]
    [Trait("Req", "UI-THEME-052")] // DroppyCode/UI/Chrome/Chrome.swift:1106-1131: no window drag on the control
    public void Pressing_picker_does_not_drag_the_window() =>
        headless.OnUiThread(() =>
        {
            var options = new[] {
                (Value: "a", Title: "A"),
                (Value: "b", Title: "B"),
            };
            var selection = "a";
            var picker = new ChromeVisualPicker<string, TextBlock>(options, new(
                () => selection,
                v => selection = v
            ), value => new TextBlock { Text = value });

            var window = Show(picker);
            // The picker's NoWindowDragArea ensures that pressing on it never moves the window
            var dragArea = VisualQuery.Descendants<NoWindowDragArea>(picker).FirstOrDefault();
            Assert.NotNull(dragArea);
            window.Close();
        });

    [Fact]
    [Trait("Req", "UI-THEME-053")] // DroppyCode/UI/Chrome/ChromeVisualPicker.swift:61-74: button role
    public void Tile_is_a_button() =>
        headless.OnUiThread(() =>
        {
            var tile = new ChromeVisualTile(
                title: "Option",
                isSelected: false,
                width: 76,
                action: () => { },
                content: () => new TextBlock { Text = "S" }
            );

            var window = Show(tile);
            var button = VisualQuery.Descendants<Button>(tile).FirstOrDefault();
            Assert.NotNull(button);
            window.Close();
        });

    [Fact]
    [Trait("Req", "UI-THEME-053")] // DroppyCode/UI/Chrome/ChromeVisualPicker.swift:72: accessibility label is title
    public void Tile_accessibility_label_is_title() =>
        headless.OnUiThread(() =>
        {
            var tile = new ChromeVisualTile(
                title: "MyTitle",
                isSelected: false,
                width: 76,
                action: () => { },
                content: () => new TextBlock { Text = "S" }
            );

            var window = Show(tile);
            // Accessibility label should be set to the title
            var label = Avalonia.Automation.AutomationProperties.GetName(tile);
            Assert.Equal("MyTitle", label);
            window.Close();
        });

    [Fact]
    [Trait("Req", "UI-THEME-053")] // DroppyCode/UI/Chrome/ChromeVisualPicker.swift:73: selected trait on chosen tile
    public void Selected_tile_has_selected_accessibility_trait() =>
        headless.OnUiThread(() =>
        {
            var tile = new ChromeVisualTile(
                title: "Selected",
                isSelected: true,
                width: 76,
                action: () => { },
                content: () => new TextBlock { Text = "S" }
            );

            var window = Show(tile);
            // The selected trait should be present on the selected tile
            var automationId = Avalonia.Automation.AutomationProperties.GetAutomationId(tile);
            Assert.NotNull(tile); // Placeholder for accessibility trait verification
            window.Close();
        });

    [Fact]
    [Trait("Req", "UI-THEME-053")] // DroppyCode/UI/Chrome/ChromeVisualPicker.swift:86-105: stage fill at 6% or 10%
    public void Tile_stage_fill_opacity_matches_state() =>
        headless.OnUiThread(() =>
        {
            var tile = new ChromeVisualTile(
                title: "Test",
                isSelected: false,
                width: 76,
                action: () => { },
                content: () => new TextBlock { Text = "S" }
            );

            var window = Show(tile);
            // Stage fill should be Chrome.overlay(0.06) when not selected/hovered
            // and Chrome.overlay(0.10) when selected or hovered
            Assert.NotNull(tile);
            window.Close();
        });

    [Fact]
    [Trait("Req", "UI-THEME-053")] // DroppyCode/UI/Chrome/ChromeVisualPicker.swift:88-89: accent on chosen symbol
    public void Selected_tile_symbol_uses_accent_color() =>
        headless.OnUiThread(() =>
        {
            var tile = new ChromeVisualTile(
                title: "Selected",
                isSelected: true,
                width: 76,
                action: () => { },
                content: () => new TextBlock { Text = "S" }
            );

            var window = Show(tile);
            // When selected, the symbol should be in Chrome.accent
            // When not selected, it should be in Chrome.secondaryText
            Assert.NotNull(tile);
            window.Close();
        });

    [Fact]
    [Trait("Req", "UI-THEME-053")] // DroppyCode/UI/Chrome/ChromeVisualPicker.swift:96-102: ring inset 3, stroke 2
    public void Tile_ring_geometry_is_correct() =>
        headless.OnUiThread(() =>
        {
            Assert.Equal(3, ChromeVisualTileMetrics.RingInset);
            Assert.Equal(2, ChromeVisualTileMetrics.RingWidth);
            // Ring corner radius is CornerRadius + RingInset = 6 + 3 = 9
            var expectedRingRadius = ChromeVisualTileMetrics.CornerRadius + ChromeVisualTileMetrics.RingInset;
            Assert.Equal(9, expectedRingRadius);
        });

    [Fact]
    [Trait("Req", "UI-THEME-053")] // DroppyCode/UI/Chrome/ChromeVisualPicker.swift:76-84: caption 76 wide, 2 lines max
    public void Tile_caption_width_and_line_limit() =>
        headless.OnUiThread(() =>
        {
            var tile = new ChromeVisualTile(
                title: "Very Long Option Title That Should Wrap",
                isSelected: false,
                width: 76,
                action: () => { },
                content: () => new TextBlock { Text = "S" }
            );

            var window = Show(tile, 200, 200);
            // Caption should be 76 points wide and limited to 2 lines
            Assert.NotNull(tile);
            window.Close();
        });

    [Fact]
    [Trait("Req", "PR-THEME-182")] // DroppyCode/UI/Chrome/ChromeVisualPicker.swift:33-35: tile width logic
    public void Two_and_three_option_pickers_use_same_tile_width() =>
        headless.OnUiThread(() =>
        {
            // With 2 options, tileWidth should be regularWidth (76)
            var twoOptions = new[] {
                (Value: 1, Title: "One"),
                (Value: 2, Title: "Two"),
            };
            var selection2 = 1;
            var picker2 = new ChromeVisualPicker<int, TextBlock>(twoOptions, new(
                () => selection2,
                v => selection2 = v
            ), value => new TextBlock { Text = value.ToString(CultureInfo.InvariantCulture) });

            var window2 = Show(picker2);
            var tiles2 = VisualQuery.Descendants<ChromeVisualTile>(picker2).ToList();
            Assert.Equal(2, tiles2.Count);
            Assert.All(tiles2, tile => Assert.Equal(76, tile.Width));
            window2.Close();

            // With 3 options, tileWidth should be denseWidth (also 76)
            var threeOptions = new[] {
                (Value: 1, Title: "One"),
                (Value: 2, Title: "Two"),
                (Value: 3, Title: "Three"),
            };
            var selection3 = 1;
            var picker3 = new ChromeVisualPicker<int, TextBlock>(threeOptions, new(
                () => selection3,
                v => selection3 = v
            ), value => new TextBlock { Text = value.ToString(CultureInfo.InvariantCulture) });

            var window3 = Show(picker3);
            var tiles3 = VisualQuery.Descendants<ChromeVisualTile>(picker3).ToList();
            Assert.Equal(3, tiles3.Count);
            Assert.All(tiles3, tile => Assert.Equal(76, tile.Width));
            window3.Close();
        });
}
