using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.HydraSettingsPageRig;

namespace Marrow.App.Tests.Views.Settings;

// Swift-derived tests (test-first) for DroppyCode/App/Views/Settings/HydraSettingsPage.swift (B3 52bc792): the pair row
// (:239-318), the two buttons (:204-236) and the pair editor's name field and popover (:336-379). The page is in
// HydraSettingsPageSpecTests, the editor's pickers in HydraSettingsPagePairSpecTests.
[Collection("Headless")]
public sealed class HydraSettingsPageRowSpecTests(HeadlessSession headless)
{
    private void RowOf(HydraPair pair, Action<HydraSettingsScene> check, Action<AppModel>? arrange = null) =>
        Run(headless, _ => new HydraPairRow(pair), check, 900, model =>
        {
            model.Settings.AddHydraPair(pair);
            arrange?.Invoke(model);
        });

    private void EditorOf(HydraPair pair, Action<HydraSettingsScene> check, Action<AppModel>? arrange = null) =>
        Run(headless, _ => new HydraPairEditor(pair.Id) { Width = 360 }, check, 900, model =>
        {
            model.Settings.AddHydraPair(pair);
            arrange?.Invoke(model);
        });

    private void Page(Action<HydraSettingsScene> check, Action<AppModel>? arrange = null) =>
        Run(headless, _ => new HydraSettingsPage(), check, 900, arrange);

    private static Button OpenButton(Control row) => Of<Button>(row).First();

    private static Button RemoveButton(Control row) => Of<Button>(row).Last();

    // ---- UI-SETTINGS-034 --------------------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "UI-SETTINGS-034")] // HydraSettingsPage.swift:246-297 padding 16 / 10 / 9, text block spacing 2, body title over subheadline summary in primary and secondary colour
    public void A_pair_row_has_the_sources_padding_text_block_and_typography() => RowOf(new HydraPair(ProviderKind.Claude), scene =>
    {
        var grid = Assert.IsType<Grid>(((Decorator)scene.View).Child);
        Assert.Equal(new Thickness(16, 9, Chrome.RowControlTrailingPadding, 9), grid.Margin);
        var title = Of<TextBlock>(scene.View).First();
        var summary = Of<TextBlock>(scene.View).Skip(1).First();
        var column = Assert.IsType<StackPanel>(title.Parent);
        Assert.Same(column, summary.Parent);
        Assert.Equal(2, column.Spacing);
        Assert.Equal(13, title.FontSize); // .body
        Assert.Equal(11, summary.FontSize); // .subheadline
        var theme = scene.View.ActualThemeVariant;
        Assert.Equal(Chrome.PrimaryText.Resolve(theme), Assert.IsAssignableFrom<ISolidColorBrush>(title.Foreground).Color);
        Assert.Equal(Chrome.SecondaryText.Resolve(theme), Assert.IsAssignableFrom<ISolidColorBrush>(summary.Foreground).Color);
        Assert.Equal(HydraPairSummary.Title(scene.Model.Settings.HydraPairs.Single(), scene.Model.Providers), title.Text);
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-034")] // HydraSettingsPage.swift:253,323-329 the pair's provider marks sit in a leading frame at least 22 wide
    public void A_pair_rows_icons_sit_in_a_leading_frame_of_at_least_22() => RowOf(new HydraPair(ProviderKind.Claude), scene =>
    {
        var icons = Of<HydraPairIcons>(scene.View).Single();
        var mark = Assert.IsType<HydraPairMark>(icons.Child);
        Assert.Equal(22, mark.MinWidth);
        Assert.Equal(Avalonia.Layout.HorizontalAlignment.Left, mark.HorizontalAlignment);
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-034")] // HydraSettingsPage.swift:263-266 the slider hint is invisible until the pointer is over the row, then fully opaque
    public void The_slider_hint_fades_in_on_hover_and_out_on_leaving() => RowOf(new HydraPair(ProviderKind.Claude), scene =>
    {
        var hint = Of<SwiftImage>(OpenButton(scene.View)).Single(image => image.SymbolName == "slider.horizontal.3");
        Assert.Equal(0, hint.Opacity);
        Assert.Equal(Chrome.SecondaryText.Resolve(hint.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(hint.Foreground).Color);
        var open = OpenButton(scene.View);
        var centre = open.TranslatePoint(new Point(open.Bounds.Width / 2, open.Bounds.Height / 2), scene.Window)!.Value;
        scene.Window.MouseMove(centre);
        Sync();
        Assert.Equal(1, hint.Opacity);
        scene.Window.MouseMove(new Point(scene.Window.Width - 1, scene.Window.Height - 1));
        Sync();
        Assert.Equal(0, hint.Opacity);
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-034")] // HydraSettingsPage.swift:277-288 the minus control is 24 x 24, secondary, and removes the pair outright: no confirmation is asked
    public void The_minus_control_is_24_square_and_removes_with_no_confirmation() => RowOf(new HydraPair(ProviderKind.Claude), scene =>
    {
        var remove = RemoveButton(scene.View);
        Assert.Equal(24, remove.Width);
        Assert.Equal(24, remove.Height);
        var glyph = Assert.IsType<SwiftImage>(remove.Content);
        Assert.Equal("minus.circle", glyph.SymbolName);
        Assert.Equal(Chrome.SecondaryText.Resolve(glyph.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(glyph.Foreground).Color);
        Click(remove);
        Assert.Empty(scene.Model.Settings.HydraPairs);
        Assert.DoesNotContain(scene.Presenter.Calls, call => call.Presented);
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-034")] // HydraSettingsPage.swift:303-317 the second line joins the providers, efforts, a head cap and a not-set-up warning with " · "
    public void The_second_line_joins_providers_cap_and_warning_with_a_middle_dot() => RowOf(
        new HydraPair(ProviderKind.Claude) { WorkerProvider = ProviderKind.Codex, MaxHeads = 1 }, scene =>
        {
            var summary = Of<TextBlock>(scene.View).Skip(1).First().Text!;
            var parts = summary.Split(" · ");
            Assert.Equal(HydraPairSummary.Providers(new HydraPair(ProviderKind.Claude) { WorkerProvider = ProviderKind.Codex }), parts[0]);
            Assert.Equal(new[] { "One head at a time", $"{ProviderKind.Codex.DisplayName} is not set up, heads stay on {ProviderKind.Claude.DisplayName}" }, parts[^2..]);
        }, model => SetInstalled(model, ProviderKind.Claude));

    // ---- PR-SETTINGS-165 --------------------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-SETTINGS-165")] // HydraSettingsPage.swift:249-276 clicking the row opens a popover on its trailing edge holding the editor, 360 wide
    public void Clicking_a_pair_row_opens_a_trailing_popover_with_a_360_wide_editor() => RowOf(new HydraPair(ProviderKind.Claude), scene =>
    {
        var pair = scene.Model.Settings.HydraPairs.Single();
        Click(OpenButton(scene.View));
        var popup = LastPresented(scene);
        Assert.Equal(PlacementMode.Right, popup.Placement); // arrowEdge: .trailing
        Assert.Same(OpenButton(scene.View), popup.PlacementTarget);
        RaiseOpened(popup);
        var editor = Of<HydraPairEditor>(popup.Child!).Single();
        Assert.Equal(360, editor.Width);
        Assert.Equal(pair.Id, Assert.Single(scene.Model.Settings.HydraPairs).Id);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-165")] // HydraSettingsPage.swift:343,368,510-513 the name field is not focused when the editor opens
    public void The_name_field_is_not_focused_when_the_editor_opens() => EditorOf(new HydraPair(ProviderKind.Claude), scene =>
    {
        Sync();
        Sync();
        var field = Of<TextBox>(scene.View).Single();
        Assert.False(field.IsFocused);
        Assert.False(field.IsKeyboardFocusWithin);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-165")] // HydraSettingsPage.swift:368 .focused: the field takes focus only when it is clicked
    public void The_name_field_takes_focus_when_it_is_clicked() => EditorOf(new HydraPair(ProviderKind.Claude), scene =>
    {
        var field = Of<TextBox>(scene.View).Single();
        var centre = field.TranslatePoint(new Point(field.Bounds.Width / 2, field.Bounds.Height / 2), scene.Window)!.Value;
        scene.Window.MouseDown(centre, Avalonia.Input.MouseButton.Left);
        scene.Window.MouseUp(centre, Avalonia.Input.MouseButton.Left);
        Sync();
        Assert.True(field.IsFocused);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-165")] // HydraSettingsPage.swift:346 the editor looks the pair up on every evaluation and draws nothing when it has been removed
    public void The_editor_draws_nothing_once_its_pair_is_removed() => EditorOf(new HydraPair(ProviderKind.Claude), scene =>
    {
        Assert.NotEmpty(EditorChildren(scene.View));
        scene.Model.RemoveHydraPair(scene.Model.Settings.HydraPairs.Single().Id);
        Sync();
        Assert.Empty(EditorChildren(scene.View));
    });

    // ---- UI-SETTINGS-035 --------------------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "UI-SETTINGS-035")] // HydraSettingsPage.swift:360-381 the block is inset 14 with 14 above, the caption 4 below the field, in subheadline secondary text
    public void The_name_block_has_the_sources_insets_and_caption() => EditorOf(new HydraPair(ProviderKind.Claude), scene =>
    {
        var block = Assert.IsType<StackPanel>(EditorChildren(scene.View)[0]);
        Assert.Equal(new Thickness(14, 14, 14, 0), block.Margin);
        Assert.Equal(4, block.Spacing);
        Assert.Equal(2, block.Children.Count);
        var caption = Assert.IsType<TextBlock>(block.Children[1]);
        Assert.Equal("Shown in the picker and the composer", caption.Text);
        Assert.Equal(new Thickness(2, 0), caption.Margin); // :378 .padding(.horizontal, 2)
        Assert.Equal(11, caption.FontSize); // .subheadline
        Assert.Equal(Chrome.SecondaryText.Resolve(caption.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(caption.Foreground).Color);
        var field = Of<TextBox>(block).Single();
        Assert.Equal(15, field.FontSize); // .title3
        Assert.Equal(FontWeight.SemiBold, field.FontWeight);
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-035")] // HydraSettingsPage.swift:363-366 empty: the models title shows as the placeholder
    public void An_unnamed_pair_shows_its_models_title_as_the_placeholder() => EditorOf(new HydraPair(ProviderKind.Claude) { OrchestratorModel = "alpha" }, scene =>
    {
        var pair = scene.Model.Settings.HydraPairs.Single();
        var field = Of<TextBox>(scene.View).Single();
        Assert.Equal("", field.Text ?? "");
        Assert.Equal(HydraPairSummary.ModelsTitle(pair, scene.Model.Providers), field.PlaceholderText);
        Assert.False(string.IsNullOrEmpty(field.PlaceholderText));
    }, model => SetCatalog(model, ProviderKind.Claude, new ModelOption("alpha", "Alpha One")));

    [Fact]
    [Trait("Req", "UI-SETTINGS-035")] // HydraSettingsPage.swift:363-366 every keystroke writes the name, and emptying the field stores none
    public void Typing_writes_the_name_at_once_and_emptying_it_stores_none() => EditorOf(new HydraPair(ProviderKind.Claude), scene =>
    {
        var field = Of<TextBox>(scene.View).Single();
        field.Text = "R";
        Sync();
        Assert.Equal("R", scene.Model.Settings.HydraPairs.Single().Name);
        field.Text = "Re";
        Sync();
        Assert.Equal("Re", scene.Model.Settings.HydraPairs.Single().Name);
        field.Text = "";
        Sync();
        Assert.Null(scene.Model.Settings.HydraPairs.Single().Name);
    });

    // ---- UI-SETTINGS-032, PR-SETTINGS-176 ---------------------------------------------------------------------------

    private static HydraCookbookButton CookbookButton(Control page) => Of<HydraCookbookButton>(page).Single();

    [Fact]
    [Trait("Req", "UI-SETTINGS-032")] // HydraSettingsPage.swift:110,211 the button sits in the Pairs header's trailing slot after 8 points, "book" symbol and the words "Open cookbook"
    [Trait("Req", "PR-SETTINGS-176")] // HydraSettingsPage.swift:204-220 a glass button labelled "Open cookbook" with a book symbol
    public void The_cookbook_button_is_the_last_thing_in_the_Pairs_header() => Page(scene =>
    {
        var section = Of<ChromeSection>(scene.View).First(candidate => Of<TextBlock>(candidate).First().Text == "Pairs");
        var header = Of<StackPanel>(section).First(stack => stack.Orientation == Avalonia.Layout.Orientation.Horizontal);
        var button = CookbookButton(scene.View);
        Assert.Same(button, header.Children[^1]);
        Assert.Equal(new Thickness(8, 0, 0, 0), button.Margin);
        var inner = Of<Button>(button).Single();
        Assert.Contains("glass", inner.Classes);
        Assert.Equal("book", Of<SwiftImage>(inner).Single().SymbolName);
        Assert.Equal("Open cookbook", Of<TextBlock>(inner).Single().Text);
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-032")] // HydraSettingsPage.swift:208-217 a click opens the popover below the button
    [Trait("Req", "PR-SETTINGS-176")] // HydraSettingsPage.swift:215-218,17-30 the popover is the 460 x 540 cookbook panel with its Cookbook header and pitch
    public void The_cookbook_button_opens_a_bottom_popover_holding_the_460_by_540_panel() => Page(scene =>
    {
        var inner = Of<Button>(CookbookButton(scene.View)).Single();
        Click(inner);
        var popup = LastPresented(scene);
        Assert.Equal(PlacementMode.Bottom, popup.Placement); // arrowEdge: .bottom
        Assert.Same(inner, popup.PlacementTarget);
        RaiseOpened(popup);
        var panel = Of<HydraCookbookPanel>(popup.Child!).Single();
        Assert.Equal(460, panel.Width);
        Assert.Equal(540, panel.Height);
        var text = Text(panel);
        Assert.Contains("Cookbook", text, StringComparison.Ordinal);
        Assert.Contains("Pairs built from the models in your library: which one leads, at what effort, and which ones build. Add one and it is in the model picker.", text, StringComparison.Ordinal);
        // :208-210 the click only sets isOpen = true (the record's "toggles" is a misreading): a second click leaves it open.
        var calls = scene.Presenter.Calls.Count;
        Click(inner);
        Assert.Equal(calls, scene.Presenter.Calls.Count);
        // Dismissed from outside, the popover reports closed and the next click opens it again.
        RaiseClosed(popup);
        Sync();
        Assert.False(scene.Presenter.Calls.Last().Presented);
        Click(inner);
        Assert.True(scene.Presenter.Calls.Last().Presented);
        Assert.Same(popup, LastPresented(scene));
    });

    // ---- UI-SETTINGS-033 --------------------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "UI-SETTINGS-033")] // HydraSettingsPage.swift:222-236 "Add pair" with a plus, always enabled even with no provider installed: it falls back to the default provider
    public void Add_pair_is_enabled_with_no_provider_installed_and_adds_one_on_the_default_provider() => Page(scene =>
    {
        var add = Of<Button>(Of<HydraAddPairButton>(scene.View).Single()).Single();
        Assert.True(add.IsEnabled);
        Assert.Contains("glass", add.Classes);
        Assert.Equal("plus", Of<SwiftImage>(add).Single().SymbolName);
        Assert.Equal("Add pair", Of<TextBlock>(add).Single().Text);
        Click(add);
        var pair = Assert.Single(scene.Model.Settings.HydraPairs);
        Assert.Equal(scene.Model.Settings.DefaultProvider, pair.Provider);
    }, model => SetInstalled(model));

    [Fact]
    [Trait("Req", "UI-SETTINGS-033")] // HydraSettingsPage.swift:152-155 with pairs the button moves to the "Another pair" row, and still appends
    public void With_pairs_Add_pair_sits_in_the_Another_pair_row_and_appends() => Page(scene =>
    {
        var row = Row(scene.View, "Another pair");
        Assert.Equal("One per provider is enough.", Detail(row));
        Click(Of<Button>(Of<HydraAddPairButton>(row).Single()).Single());
        Assert.Equal(2, scene.Model.Settings.HydraPairs.Count);
        Assert.Equal(1, Of<ChromeRow>(scene.View).Count(candidate => Of<TextBlock>(candidate).First().Text == "Another pair"));
    }, model => model.Settings.AddHydraPair(new HydraPair(ProviderKind.Claude)));
}
