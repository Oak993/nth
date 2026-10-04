using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Settings;

// Source: DroppyCode/App/Views/Settings/HydraCookbookPanel.swift (B3 52bc7926), :72-133 HydraCookbookRow.
// PR-SETTINGS-178 asks for the row's look and its one-click add. Its look is proven here from the values the Swift states (OD-0098: where the
// reference videos hold no frame of a state, the pinned source is the proof): the marks in a 52 point leading frame, the VStack at spacing 2, the
// title at .body semibold in the primary colour, the tagline and the optional third line at .subheadline in the secondary colour (the third line
// at 0.8 opacity, two lines at most, drawn only when it has something to say), the waiting sentence, the Added label at .callout medium with 10
// points of horizontal padding, the Add button, and the row padded 16 by 10. The click itself is HydraCookbookPanelTests'
// (Review_real_panel_resolves_the_first_tied_model_and_adds_its_ready_row, An_open_cookbook_offers_and_adds_a_recipe_after_its_catalog_arrives).
[Collection("Headless")]
public sealed class HydraCookbookRowSwiftProofTests(HeadlessSession headless)
{
    private static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static void Opacity(Color expectedInk, double opacity, IBrush? brush)
    {
        var actual = ColorOf(brush);
        Assert.Equal((expectedInk.R, expectedInk.G, expectedInk.B), (actual.R, actual.G, actual.B));
        Assert.InRange(actual.A, expectedInk.A * opacity - 1, expectedInk.A * opacity + 1);
    }

    private static AvaloniaWindow Show(HydraCookbookRow row, AppModel model, out Border host)
    {
        host = new Border();
        AppEnvironment.SetAppModel(host, model);
        host.Child = row;
        var window = new AvaloniaWindow { Content = host, Width = 460, Height = 400 };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static Grid GridOf(HydraCookbookRow row) => Assert.IsType<Grid>(row.Child);
    private static StackPanel TextsOf(HydraCookbookRow row) => GridOf(row).Children.OfType<StackPanel>().Single(panel => Grid.GetColumn(panel) == 1);

    [Fact]
    [Trait("Req", "PR-SETTINGS-178")] // HydraCookbookPanel.swift:81-95,131-132: marks in a 52 frame, spacing 2, padded 16 x 10, title .body semibold primary, tagline .subheadline secondary and wrapping
    public void A_resolved_row_is_the_52_point_marks_then_the_title_and_the_tagline_and_nothing_else_for_a_same_provider_pair_without_efforts() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        var recipe = HydraCookbook.Recipes[0];
        var pair = new HydraPair(ProviderKind.Claude) { OrchestratorModel = "claude-model" };
        var row = new HydraCookbookRow(recipe, pair, null);
        var window = Show(row, model, out _);
        var variant = row.ActualThemeVariant;
        Assert.Equal(new Thickness(16, 10), row.Padding); // :131-132
        var mark = Assert.Single(GridOf(row).Children.OfType<Marrow.UI.Chrome.HydraPairMark>());
        Assert.Equal(52, mark.Width); Assert.Equal(HorizontalAlignment.Left, mark.HorizontalAlignment); // :86-87
        Assert.Equal(VerticalAlignment.Center, mark.VerticalAlignment); // :81 alignment: .center
        var texts = TextsOf(row);
        Assert.Equal(2, texts.Spacing); // :88
        // :104 an empty Text would take a line: a same-provider pair with no efforts draws no third line.
        Assert.Equal(2, texts.Children.Count);
        var title = Assert.IsType<TextBlock>(texts.Children[0]); var tagline = Assert.IsType<TextBlock>(texts.Children[1]);
        Assert.Equal(13, title.FontSize); Assert.Equal(FontWeight.SemiBold, title.FontWeight); // .system(.body, weight: .semibold)
        Assert.Equal(Chrome.PrimaryText.Resolve(variant), ColorOf(title.Foreground));
        Assert.Equal(recipe.Tagline, tagline.Text); Assert.Equal(11, tagline.FontSize); // :92-95 .system(.subheadline)
        Assert.Equal(Chrome.PrimaryText.Resolve(variant).A * 0.7, ColorOf(tagline.Foreground).A, 1.0); // Chrome.secondaryText = primary at 0.7
        Assert.Equal(TextWrapping.Wrap, tagline.TextWrapping); // fixedSize(horizontal: false, vertical: true): wraps, never clips
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-178")] // HydraCookbookPanel.swift:96-106: the third line, "<Lead> lead, <Heads> heads" and the efforts phrase joined with " · ", .subheadline at 0.8 secondary, two lines
    public void The_third_line_joins_the_providers_line_and_the_efforts_phrase_with_a_middle_dot_and_is_held_to_two_lines() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        var recipe = HydraCookbook.Recipes[0];
        var cases = new (HydraPair Pair, string Expected)[]
        {
            (new HydraPair(ProviderKind.Claude) { OrchestratorModel = "m", WorkerProvider = ProviderKind.Codex, WorkerModel = "w" }, "Claude lead, Codex heads"),
            (new HydraPair(ProviderKind.Claude) { OrchestratorModel = "m", OrchestratorEffort = "high", WorkerModel = "m", WorkerEffort = "high" }, "High effort"),
            (new HydraPair(ProviderKind.Claude) { OrchestratorModel = "m", OrchestratorEffort = "high", WorkerModel = "m", WorkerEffort = "medium" }, "Lead high, heads medium effort"),
            (new HydraPair(ProviderKind.Claude) { OrchestratorModel = "m", WorkerProvider = ProviderKind.Codex, WorkerModel = "w", OrchestratorEffort = "high", WorkerEffort = "medium" },
                "Claude lead, Codex heads · Lead high, heads medium effort"),
        };
        foreach (var (pair, expected) in cases)
        {
            var row = new HydraCookbookRow(recipe, pair, null);
            var window = Show(row, model, out _);
            var texts = TextsOf(row);
            Assert.Equal(3, texts.Children.Count);
            var third = Assert.IsType<TextBlock>(texts.Children[2]);
            Assert.Equal(expected, third.Text);
            Assert.Equal(11, third.FontSize); // .system(.subheadline)
            Assert.Equal(2, third.MaxLines); // :104 lineLimit(2)
            Opacity(Chrome.PrimaryText.Resolve(row.ActualThemeVariant), 0.7 * 0.8, third.Foreground); // Chrome.secondaryText.opacity(0.8)
            window.Close();
        }
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-178")] // HydraCookbookPanel.swift:82-87,107-111,114: a waiting row's marks are the recipe's first choices (Claude when it names none), its sentence is verbatim and it has no trailing control
    public void A_waiting_row_shows_the_first_choices_marks_and_the_verbatim_sentence_at_0_8_secondary_with_no_control() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        foreach (var recipe in HydraCookbook.Recipes)
        {
            var row = new HydraCookbookRow(recipe, null, recipe.Lead.Providers.First());
            var window = Show(row, model, out _);
            var mark = Assert.Single(GridOf(row).Children.OfType<Marrow.UI.Chrome.HydraPairMark>());
            Assert.Equal(recipe.Lead.Providers.FirstOrDefault(ProviderKind.Claude), mark.Lead);
            Assert.Equal(recipe.Heads.Providers.FirstOrDefault(ProviderKind.Claude), mark.Heads);
            var texts = TextsOf(row);
            Assert.Equal(recipe.Title, Assert.IsType<TextBlock>(texts.Children[0]).Text); // :89 the recipe's own title when there is no pair
            Assert.Equal(recipe.Tagline, Assert.IsType<TextBlock>(texts.Children[1]).Text); // :92 the tagline is always the recipe's
            var sentence = Assert.IsType<TextBlock>(texts.Children[2]);
            Assert.Equal("Set up " + recipe.Lead.Providers.First().DisplayName + " to add this pair.", sentence.Text);
            Assert.Equal(11, sentence.FontSize);
            Opacity(Chrome.PrimaryText.Resolve(row.ActualThemeVariant), 0.7 * 0.8, sentence.Foreground);
            Assert.DoesNotContain(GridOf(row).Children, child => Grid.GetColumn(child) == 2); // :114 no pair: no trailing control
            window.Close();
        }
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-178")] // HydraCookbookPanel.swift:115-120: an added pair's trailing control is a non-interactive Label "Added" with a checkmark, .callout medium, secondary, 10 points of horizontal padding
    public void An_added_pair_trails_a_non_interactive_Added_label_with_a_checkmark_in_callout_medium() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        var recipe = HydraCookbook.Recipes[0];
        var pair = new HydraPair(ProviderKind.Claude) { OrchestratorModel = "claude-model" };
        harness.Main.OnMain(() => model.Settings.AddHydraPair(pair));
        var row = new HydraCookbookRow(recipe, pair, null);
        var window = Show(row, model, out _);
        var trailing = Assert.IsType<StackPanel>(GridOf(row).Children.Single(child => Grid.GetColumn(child) == 2));
        Assert.Equal(new Thickness(10, 0), new Thickness(trailing.Margin.Left, 0, trailing.Margin.Right, 0)); // .padding(.horizontal, 10)
        var check = Assert.IsType<SwiftImage>(trailing.Children[0]); var label = Assert.IsType<TextBlock>(trailing.Children[1]);
        Assert.Equal("checkmark", check.SymbolName);
        Assert.Equal("Added", label.Text);
        Assert.Equal(12, label.FontSize); Assert.Equal(FontWeight.Medium, label.FontWeight); // .system(.callout, weight: .medium)
        Assert.Equal(SwiftFont.Callout.FontWeight(SwiftFont.Weight.Medium), SwiftImage.GetFont(check));
        Assert.Equal(Chrome.PrimaryText.Resolve(row.ActualThemeVariant).A * 0.7, ColorOf(label.Foreground).A, 1.0);
        Assert.Empty(trailing.GetLogicalDescendants().OfType<Button>()); // no button
        Assert.Empty(GridOf(row).GetLogicalDescendants().OfType<Button>());
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-178")] // HydraCookbookPanel.swift:121-128,48-53: a resolvable row's Add is a glass-prominent button, Label("Add", systemImage: "plus"); a second equivalent pair makes the row Added at once
    public void A_resolvable_rows_trailing_control_is_the_Add_button_with_a_plus_and_one_click_turns_the_row_into_Added() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        using var motion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = false }); // Chrome.panelSlide reads the system setting
        var model = harness.MakeModel();
        var recipe = HydraCookbook.Recipes[0];
        var pair = new HydraPair(ProviderKind.Claude) { OrchestratorModel = "claude-model" };
        var row = new HydraCookbookRow(recipe, pair, null);
        var window = Show(row, model, out _);
        var button = Assert.Single(GridOf(row).Children.OfType<Button>());
        Assert.Equal(2, Grid.GetColumn(button)); Assert.Equal(VerticalAlignment.Center, button.VerticalAlignment);
        var content = Assert.IsType<StackPanel>(button.Content);
        Assert.Equal("plus", Assert.IsType<SwiftImage>(content.Children[0]).SymbolName);
        Assert.Equal("Add", Assert.IsType<TextBlock>(content.Children[1]).Text);
        Assert.Empty(model.Settings.HydraPairs);
        harness.Main.OnMain(() =>
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); // one click, no confirmation (:121-123)
            Assert.Single(model.Settings.HydraPairs);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        });
        // The row is rebuilt as Added as soon as the first click lands, so a second click is impossible (the button is gone).
        Assert.Empty(GridOf(row).GetLogicalDescendants().OfType<Button>());
        Assert.Contains(GridOf(row).GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == "Added");
        Assert.Single(model.Settings.HydraPairs);
        window.Close();
    });
}
