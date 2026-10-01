using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.ModelsSettingsSectionRig;

namespace Marrow.App.Tests.Views.Settings;

// The catalogue rows of DroppyCode/App/Views/Settings/ModelsSettingsPage.swift (B3 52bc7926), :346-395 (the add control and the
// fast-mode glyph of a model row) and :86-91 (the no-match line).
[Collection("Headless")]
public sealed class ModelsSettingsPageCatalogueRowTests(HeadlessSession headless)
{
    private const ProviderKind Provider = ProviderKind.Codex;

    private static ModelOption Option(string id, string name, string? fastTier = null) => new(id, name, fastTier: fastTier);

    /// <summary>Alpha One has a fast tier, Beta Two none, Gamma Three an empty one (a tier is declared: fastTier != nil).</summary>
    private static Action<AppModel> Catalogue(params ModelPin[] chosen) => model =>
    {
        model.Providers.UpdateCatalog(new ValueList<ModelOption>([Option("a", "Alpha-One", "flex"), Option("b", "Beta-Two"), Option("c", "Gamma-Three", "")]), Provider);
        foreach (var pin in chosen) model.Settings.AddToModelList(pin);
    };

    private static ModelPin Pin(string id) => new(Provider, id);

    private static ChromeRow RowOf(ModelsSettingsSectionRig rig, string title) =>
        Of<ChromeRow>(rig.SectionOf(Provider)).Single(row => Of<TextBlock>(row).Any(text => text.Text == title));

    private static Button ToggleOf(ChromeRow row) => Of<Button>(row).Single();

    private static Color ColourOf(SwiftImage image) => Assert.IsAssignableFrom<ISolidColorBrush>(image.Foreground).Color;

    private static SwiftImage GlyphOf(Control control) => Assert.Single(Of<SwiftImage>(control));

    [Fact]
    [Trait("Req", "UI-SETTINGS-039")] // ModelsSettingsPage.swift:376-378 Image(systemName: isAdded ? "checkmark.circle.fill" : "plus.circle") .font(.system(size: 17)) .foregroundStyle(isAdded ? Chrome.accent : Chrome.secondaryText)
    public void The_add_control_is_a_17_point_plus_circle_in_secondary_text_and_a_checkmark_circle_in_the_accent_once_chosen() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([Provider], Catalogue(Pin("b")));
        var add = GlyphOf(ToggleOf(RowOf(rig, "Alpha One")));
        Assert.Equal("plus.circle", add.SymbolName);
        Assert.Equal(17, SwiftImage.GetFont(add)!.Resolve().FontSize);
        Assert.Equal(FontWeight.Normal, SwiftImage.GetFont(add)!.Resolve().FontWeight);
        Assert.Equal(Chrome.SecondaryText.Resolve(add.ActualThemeVariant), ColourOf(add));

        var chosen = GlyphOf(ToggleOf(RowOf(rig, "Beta Two")));
        Assert.Equal("checkmark.circle.fill", chosen.SymbolName);
        Assert.Equal(17, SwiftImage.GetFont(chosen)!.Resolve().FontSize);
        Assert.Equal(Chrome.Accent.Resolve(chosen.ActualThemeVariant), ColourOf(chosen));
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-039")] // ModelsSettingsPage.swift:382-387 .disabled(!isAdded && isFull), .opacity(0.35), .help(…), .accessibilityLabel(…)
    public void The_add_control_reads_add_remove_or_full_in_its_tooltip_and_keeps_the_model_name_in_its_accessibility_label() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([Provider], Catalogue(Pin("b")));
        var add = ToggleOf(RowOf(rig, "Alpha One"));
        Assert.Equal("Add to the picker", ToolTip.GetTip(add));
        Assert.Equal("Add Alpha One to the picker", AutomationProperties.GetName(add));
        Assert.True(add.IsEnabled);
        Assert.Equal(1, add.Opacity);

        var remove = ToggleOf(RowOf(rig, "Beta Two"));
        Assert.Equal("Remove from the picker", ToolTip.GetTip(remove));
        Assert.Equal("Remove Beta Two from the picker", AutomationProperties.GetName(remove));
        Assert.True(remove.IsEnabled);
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-039")] // ModelsSettingsPage.swift:313,382-387 with AppSettings.modelListLimit (15) chosen, a model not chosen is disabled at 0.35 with the full tooltip; a chosen one can still be removed
    public void With_fifteen_chosen_the_add_control_of_an_unchosen_model_is_disabled_dimmed_and_says_the_picker_is_full() => headless.OnUiThread(() =>
    {
        var filled = Enumerable.Range(0, 14).Select(i => new ModelPin(ProviderKind.Claude, "x" + i.ToString(System.Globalization.CultureInfo.InvariantCulture))).Append(Pin("b")).ToArray();
        using var rig = new ModelsSettingsSectionRig([Provider], Catalogue(filled), height: 2600);
        Assert.Equal(15, rig.Model.Settings.ModelList.Count);

        var add = ToggleOf(RowOf(rig, "Alpha One"));
        Assert.False(add.IsEnabled);
        Assert.Equal(0.35, add.Opacity, 0.001);
        Assert.Equal("The picker holds up to 15 models", ToolTip.GetTip(add));
        Assert.Equal("Add Alpha One to the picker", AutomationProperties.GetName(add)); // the label does not say it is disabled

        var remove = ToggleOf(RowOf(rig, "Beta Two"));
        Assert.True(remove.IsEnabled);
        Assert.Equal(1, remove.Opacity);
        Assert.Equal("Remove from the picker", ToolTip.GetTip(remove));

        rig.Click(remove); // room again
        var again = ToggleOf(RowOf(rig, "Alpha One"));
        Assert.True(again.IsEnabled);
        Assert.Equal(1, again.Opacity);
        Assert.Equal("Add to the picker", ToolTip.GetTip(again));
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-039")] // ModelsSettingsPage.swift:365-374 click toggles membership; :368-370 while a search is active without animation, :371-373 otherwise inside Chrome.panelSlide
    public void Clicking_the_add_control_toggles_membership_animated_in_the_panel_slide_unless_a_search_is_active() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([Provider], Catalogue());
        rig.WatchModelList();
        rig.Click(ToggleOf(RowOf(rig, "Alpha One")));
        Assert.Equal([Pin("a")], rig.Model.Settings.ModelList.ToArray());
        Assert.Equal(Chrome.PanelSlide, rig.ModelListTransaction);

        rig.Click(ToggleOf(RowOf(rig, "Alpha One")));
        Assert.Empty(rig.Model.Settings.ModelList);

        rig.Page.SetQuery("alpha");
        rig.Sync();
        rig.WatchModelList();
        rig.Click(ToggleOf(RowOf(rig, "Alpha One")));
        Assert.Equal([Pin("a")], rig.Model.Settings.ModelList.ToArray());
        Assert.Null(rig.ModelListTransaction); // instant while filtering
        rig.Click(ToggleOf(RowOf(rig, "Alpha One")));
        Assert.Empty(rig.Model.Settings.ModelList);
        Assert.Null(rig.ModelListTransaction);
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-039")] // ModelsSettingsPage.swift:317-321,392-394 in a collapsed section an unchosen row is hidden: not enabled, not hit-testable, hidden from accessibility; a chosen one stays
    public void In_a_collapsed_section_the_add_control_of_an_unchosen_model_is_hidden_with_its_row_and_a_chosen_one_stays() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([Provider], Catalogue(Pin("b")));
        rig.Click(rig.HeaderOf(Provider)); // collapse
        rig.Advance(2);

        var hidden = RowOf(rig, "Alpha One");
        var hiddenToggle = ToggleOf(hidden);
        var reveal = Assert.IsType<ChromeRevealLayout>(hidden.Parent);
        Assert.Equal(0, reveal.Fraction);
        Assert.False(hidden.IsEnabled);
        Assert.False(hidden.IsHitTestVisible);
        Assert.False(hiddenToggle.IsEffectivelyEnabled);
        Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(reveal));

        var shown = RowOf(rig, "Beta Two");
        var shownReveal = Assert.IsType<ChromeRevealLayout>(shown.Parent);
        Assert.Equal(1, shownReveal.Fraction);
        Assert.True(shown.IsEnabled);
        Assert.True(shown.IsHitTestVisible);
        Assert.True(ToggleOf(shown).IsEffectivelyEnabled);
        Assert.Equal(AccessibilityView.Content, AutomationProperties.GetAccessibilityView(shownReveal));

        rig.Click(ToggleOf(shown)); // removing the chosen model from the collapsed section closes its row too
        rig.Advance(2);
        Assert.Equal(0, shownReveal.Fraction);
        Assert.False(shown.IsHitTestVisible);
        Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(shownReveal));
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-040")] // ModelsSettingsPage.swift:359 `if option.supportsFast`; Provider.swift:255 supportsFast = fastTier != nil
    public void The_fast_mode_glyph_is_present_only_for_a_model_that_declares_a_fast_tier() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([Provider], Catalogue());
        Assert.Single(Of<SwiftImage>(RowOf(rig, "Alpha One")), image => image.SymbolName == "bolt");
        Assert.DoesNotContain(Of<SwiftImage>(RowOf(rig, "Beta Two")), image => image.SymbolName == "bolt");
        Assert.Single(Of<SwiftImage>(RowOf(rig, "Gamma Three")), image => image.SymbolName == "bolt"); // an empty tier is still a tier
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-040")] // ModelsSettingsPage.swift:358-364 HStack(spacing: 10) { bolt .font(.system(size: 11)) .foregroundStyle(Chrome.secondaryText) .help("Supports fast mode"); add control }
    public void The_fast_mode_glyph_is_an_11_point_secondary_label_with_a_tooltip_10_points_before_the_add_control() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([Provider], Catalogue());
        var row = RowOf(rig, "Alpha One");
        var bolt = Of<SwiftImage>(row).Single(image => image.SymbolName == "bolt");
        Assert.Equal(11, SwiftImage.GetFont(bolt)!.Resolve().FontSize);
        Assert.Equal(FontWeight.Normal, SwiftImage.GetFont(bolt)!.Resolve().FontWeight);
        Assert.Equal(Chrome.SecondaryText.Resolve(bolt.ActualThemeVariant), ColourOf(bolt));
        Assert.Equal("Supports fast mode", ToolTip.GetTip(bolt));

        var toggle = ToggleOf(row);
        Assert.Same(bolt.Parent, toggle.Parent);
        Assert.Equal(10, toggle.Bounds.X - bolt.Bounds.Right, 0.5);

        Assert.False(Avalonia.LogicalTree.LogicalExtensions.GetLogicalAncestors(bolt).OfType<Button>().Any()); // a label, not a control
        Assert.False(bolt.Focusable);
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-040")] // ModelsSettingsPage.swift:392-394 the fast glyph is part of the row: hidden with it in a collapsed section when the model is not chosen
    public void The_fast_mode_glyph_hides_with_its_row_in_a_collapsed_section_unless_the_model_is_chosen() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([Provider], Catalogue(Pin("c"))); // Alpha unchosen, Gamma chosen: both have a fast tier
        Assert.Equal([1d, 1d, 1d], Of<ChromeRevealLayout>(rig.SectionOf(Provider)).Where(reveal => reveal.Children[0] is ChromeRow).Select(reveal => reveal.Fraction).ToArray());

        rig.Click(rig.HeaderOf(Provider));
        rig.Advance(2);

        var alpha = RowOf(rig, "Alpha One");
        Assert.Equal(0, Assert.IsType<ChromeRevealLayout>(alpha.Parent).Fraction);
        Assert.False(alpha.IsHitTestVisible);
        Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(alpha.Parent!));
        var gamma = RowOf(rig, "Gamma Three");
        Assert.Equal(1, Assert.IsType<ChromeRevealLayout>(gamma.Parent).Fraction);
        Assert.True(gamma.IsHitTestVisible);
        Assert.NotNull(Of<SwiftImage>(gamma).Single(image => image.SymbolName == "bolt"));
    });

    private static TextBlock? NoMatch(ModelsSettingsSectionRig rig) =>
        Of<TextBlock>(rig.Page).SingleOrDefault(text => text.Text?.StartsWith("No models match", StringComparison.Ordinal) == true);

    [Fact]
    [Trait("Req", "UI-SETTINGS-042")] // ModelsSettingsPage.swift:86-90 Text(verbatim: "No models match “\(trimmed)”.") .font(.system(size: 13)) .foregroundStyle(Chrome.secondaryText) .padding(.horizontal, 4)
    public void The_no_match_line_is_13_point_secondary_text_inset_4_with_curly_quotes_around_the_trimmed_query() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([], query: "  zzz\n");
        var line = Assert.IsType<TextBlock>(NoMatch(rig));
        Assert.Equal("No models match “zzz”.", line.Text);
        Assert.Equal(13, line.FontSize);
        Assert.Equal(FontWeight.Normal, line.FontWeight);
        Assert.Equal(new Thickness(4, 0, 4, 0), line.Margin);
        Assert.Equal(Chrome.SecondaryText.Resolve(line.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(line.Foreground).Color);
        Assert.Same(line, Assert.IsType<StackPanel>(line.Parent).Children.Last()); // below the sections
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-042")] // ModelsSettingsPage.swift:86 `!trimmed.isEmpty, visiblePins.isEmpty, providers.isEmpty`: an empty query never shows it
    public void The_no_match_line_never_shows_for_an_empty_query() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([], query: "   ");
        Assert.Null(NoMatch(rig));
        rig.Page.SetQuery("zzz");
        rig.Sync();
        Assert.NotNull(NoMatch(rig));
        rig.Page.SetQuery("");
        rig.Sync();
        Assert.Null(NoMatch(rig));
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-042")] // ModelsSettingsPage.swift:86,24-27,105-110 a chosen model that matches keeps the line away, matched by id (an unknown model has no name)
    public void The_no_match_line_is_absent_while_a_chosen_model_matches() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([], model => model.Settings.AddToModelList(new ModelPin(ProviderKind.Claude, "special-raw-id")), query: "SPECIAL");
        Assert.Null(NoMatch(rig));
        rig.Page.SetQuery("nothing-like-it");
        rig.Sync();
        Assert.Equal("No models match “nothing-like-it”.", NoMatch(rig)?.Text);
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-042")] // ModelsSettingsPage.swift:25-27,86 a provider whose catalogue has a match keeps the line away; one whose catalogue is empty never matches, even by its own name
    public void The_no_match_line_is_absent_while_a_provider_catalogue_matches_and_shown_for_a_provider_with_no_models() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([Provider, ProviderKind.Deepseek], model =>
        {
            Catalogue()(model);
            ModelsSettingsSectionRig.ClearCatalogue(ProviderKind.Deepseek)(model);
        }, query: "alpha");
        Assert.Null(NoMatch(rig)); // Codex lists Alpha
        rig.Page.SetQuery("codex"); // the provider's name matches a model of its catalogue
        rig.Sync();
        Assert.Null(NoMatch(rig));

        rig.Page.SetQuery("deepseek"); // Deepseek is available, but its catalogue is empty: nothing in it can match
        rig.Sync();
        Assert.Equal("No models match “deepseek”.", NoMatch(rig)?.Text);
    });
}
