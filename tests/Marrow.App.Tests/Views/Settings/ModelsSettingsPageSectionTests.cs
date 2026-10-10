using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;
using static Marrow.App.Tests.Fixtures.ModelsSettingsSectionRig;

namespace Marrow.App.Tests.Views.Settings;

// Provider sections of DroppyCode/App/Views/Settings/ModelsSettingsPage.swift (B3 52bc7926), :298-448: the instant
// filter, the collapsible header, the eager card, the empty-catalogue row and the page-held expansion.
[Collection("Headless")]
public sealed class ModelsSettingsPageSectionTests(HeadlessSession headless)
{
    private static ModelOption Option(string id, string name, string? detail = null, string? fastTier = null) => new(id, name, detail: detail, fastTier: fastTier);

    private static Action<AppModel> Catalogue(ProviderKind provider, params ModelOption[] options) =>
        model => model.Providers.UpdateCatalog(new ValueList<ModelOption>(options), provider);

    private static Action<AppModel> ClearCatalogue(ProviderKind provider) => ModelsSettingsSectionRig.ClearCatalogue(provider);

    private static ModelPin Pin(string id) => new(ProviderKind.Codex, id);

    private static double[] Fractions(IEnumerable<ChromeRevealLayout> reveals) => reveals.Select(reveal => reveal.Fraction).ToArray();

    /// <summary>The reveal around each catalogue row's divider and around each row, in catalogue order (:346-395).</summary>
    private static (ChromeRevealLayout[] Dividers, ChromeRevealLayout[] Rows) Reveals(Control section)
    {
        var all = Of<ChromeRevealLayout>(section).ToArray();
        return (all.Where(reveal => reveal.Children[0] is ChromeRowDivider).ToArray(), all.Where(reveal => reveal.Children[0] is ChromeRow).ToArray());
    }

    private static Color Resolved(Control control, IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static Button LoadButton(Control section) => Of<Button>(section).Single(button => button.Content as string == "Load models");

    [Fact]
    [Trait("Req", "PR-SETTINGS-180")] // ModelsSettingsPage.swift:28-29 "Filtering is instant with no transitions", :406 .animation(nil, value: query)
    public void Typing_and_clearing_the_search_show_and_hide_catalogue_rows_at_once_with_no_motion() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([ProviderKind.Codex], Catalogue(ProviderKind.Codex, Option("a", "Alpha"), Option("b", "Beta"), Option("c", "Gamma")));
        var section = rig.SectionOf(ProviderKind.Codex);
        Assert.Equal([1, 1, 1], Fractions(Reveals(section).Rows));

        rig.Page.SetQuery("alpha");
        rig.Sync(); // no frame is advanced: a transition would still be at its start
        Assert.Equal([1, 0, 0], Fractions(Reveals(section).Rows));
        Assert.Equal([0, 0, 0], Fractions(Reveals(section).Dividers)); // the first visible row has no divider above it

        rig.Page.SetQuery("");
        rig.Sync();
        Assert.Equal([1, 1, 1], Fractions(Reveals(section).Rows));
        Assert.Equal([0, 1, 1], Fractions(Reveals(section).Dividers));

        rig.Page.SetQuery("beta");
        rig.Sync();
        Assert.Equal([0, 1, 0], Fractions(Reveals(section).Rows));
        Assert.Equal([0, 0, 0], Fractions(Reveals(section).Dividers));
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-190")] // ModelsSettingsPage.swift:325-329 spacing Chrome.sectionHeaderSpacing, :398-409 the eager card: same fill and shape as ChromeCard, :346 the whole catalogue in catalogue order
    public void A_provider_section_is_a_header_over_an_eager_card_of_the_whole_catalogue_in_catalogue_order() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([ProviderKind.Codex], Catalogue(ProviderKind.Codex, Option("z", "Zulu"), Option("a", "Alpha"), Option("m", "Mike")));
        var section = rig.SectionOf(ProviderKind.Codex);
        var stack = Assert.IsType<StackPanel>(section.Child);
        var header = Assert.IsType<CollapsibleProviderHeader>(stack.Children[0]);
        var card = Assert.IsType<Border>(stack.Children[1]); // not a ChromeCard
        Assert.Empty(Of<ChromeCard>(section));

        var headerBottom = header.TranslatePoint(new Point(0, header.Bounds.Height), rig.Window)!.Value.Y;
        var cardTop = card.TranslatePoint(default, rig.Window)!.Value.Y;
        Assert.Equal(10, cardTop - headerBottom, 0.5);

        Assert.Equal(new CornerRadius(16), card.CornerRadius);
        Assert.True(card.ClipToBounds);
        Assert.Equal(Chrome.Overlay(0.03).Resolve(card.ActualThemeVariant), Resolved(card, card.Background));

        // Every catalogue row is mounted, none dropped and none re-sorted.
        Assert.Equal(["Zulu", "Alpha", "Mike"], Of<ChromeRow>(card).Select(row => Of<TextBlock>(row).First().Text ?? "").ToArray());
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-190")] // ModelsSettingsPage.swift:411 .task { await registry.loadCatalog(provider) } on the section, :305 whether or not it is expanded
    public void Showing_a_section_starts_an_unforced_catalogue_load_even_while_it_is_collapsed() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([], ClearCatalogue(ProviderKind.Deepseek), ungated: [ProviderKind.Deepseek]);
        Assert.Empty(rig.Model.Providers.Models(ProviderKind.Deepseek));
        var collapsed = new Binding<bool>(() => false, _ => { });
        rig.Mount(new ProviderModelsSection(rig.Model, ProviderKind.Deepseek, collapsed));
        Assert.NotEmpty(rig.Model.Providers.Models(ProviderKind.Deepseek)); // the offline seed arrived: the load ran
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-190")] // ModelsSettingsPage.swift:411 the appear-time load is not forced (loadCatalog(provider), force defaults to false)
    public void The_section_s_own_load_is_unforced_so_a_provider_already_attempted_is_not_loaded_again() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([ProviderKind.Deepseek], ClearCatalogue(ProviderKind.Deepseek)); // the attempt gate is closed for Deepseek
        Assert.True(rig.IsGated(ProviderKind.Deepseek));
        Assert.Empty(rig.Model.Providers.Models(ProviderKind.Deepseek));
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-191")] // ModelsSettingsPage.swift:330-340 the empty row: "Loading models…" / "No models loaded", the glass "Load models" button, disabled while loading
    public void An_empty_catalogue_shows_one_row_titled_by_its_load_state_with_no_detail_line() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([ProviderKind.Deepseek], ClearCatalogue(ProviderKind.Deepseek));
        var section = rig.SectionOf(ProviderKind.Deepseek);
        var row = Assert.Single(Of<ChromeRow>(section));
        Assert.Equal(["No models loaded", "Load models"], Of<TextBlock>(row).Select(text => text.Text ?? "").ToArray()); // the title, no detail line, then the button's label
        Assert.Empty(Of<ChromeRowDivider>(section));
        Assert.True(LoadButton(section).IsEnabled);

        // nth step 7: while the models load, three skeleton rows (3 x 40) stand in for the "Loading models…" title (U+2026), which stays as
        // their accessible name; the glass button stays at the trailing edge, disabled
        rig.SetLoading(ProviderKind.Deepseek, true);
        var loadingSection = rig.SectionOf(ProviderKind.Deepseek);
        Assert.Empty(Of<ChromeRow>(loadingSection));
        var skeleton = Assert.Single(Of<NthSkeleton>(loadingSection));
        Assert.Equal(NthSkeletonKind.List, skeleton.Kind); Assert.Equal(3, skeleton.Rows); Assert.Equal(120, skeleton.Height);
        Assert.Equal("Loading models…", AutomationProperties.GetName(skeleton));
        Assert.Equal(AccessibilityView.Content, AutomationProperties.GetAccessibilityView(skeleton));
        Assert.DoesNotContain(Of<TextBlock>(loadingSection), text => text.Text == "Loading models…"); // the text is only the rows' accessible name
        Assert.Contains(Of<TextBlock>(loadingSection), text => text.Text == "Load models");
        Assert.False(LoadButton(rig.SectionOf(ProviderKind.Deepseek)).IsEnabled);

        rig.SetLoading(ProviderKind.Deepseek, false);
        Assert.Equal(["No models loaded", "Load models"], Of<TextBlock>(Assert.Single(Of<ChromeRow>(rig.SectionOf(ProviderKind.Deepseek)))).Select(text => text.Text ?? "").ToArray());
        Assert.True(LoadButton(rig.SectionOf(ProviderKind.Deepseek)).IsEnabled);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-191")] // ModelsSettingsPage.swift:334-336 Button("Load models") { Task { await registry.loadCatalog(provider, force: true) } }; :411 the appear-time load is not forced
    public void The_load_models_button_forces_a_load_that_the_sections_own_unforced_load_skipped() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([ProviderKind.Deepseek], ClearCatalogue(ProviderKind.Deepseek));
        Assert.Empty(rig.Model.Providers.Models(ProviderKind.Deepseek)); // the section's own load found the attempt gate shut and did nothing
        var section = rig.SectionOf(ProviderKind.Deepseek);

        rig.Click(LoadButton(section));

        Assert.NotEmpty(rig.Model.Providers.Models(ProviderKind.Deepseek)); // the forced load went through the gate
        var shown = rig.SectionOf(ProviderKind.Deepseek);
        Assert.DoesNotContain("No models loaded", TextOf(shown), StringComparison.Ordinal);
        Assert.DoesNotContain(Of<Button>(shown), button => button.Content as string == "Load models"); // the rows replaced the empty row
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-191")] // ModelsSettingsPage.swift:338-339 cardVisible, :332 ChromeRevealLayout(fraction: cardVisible ? 1 : 0), :341-344 clipped, disabled, not hit-testable, accessibilityHidden
    public void With_an_empty_catalogue_the_row_collapses_with_the_section_and_is_disabled_unreachable_and_hidden_from_accessibility() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([ProviderKind.Deepseek], ClearCatalogue(ProviderKind.Deepseek));
        var section = rig.SectionOf(ProviderKind.Deepseek);
        var reveal = Assert.Single(Of<ChromeRevealLayout>(section));
        var card = Assert.IsType<Border>(Assert.IsType<StackPanel>(section.Child).Children[1]);
        Assert.Equal(1, reveal.Fraction);
        Assert.True(reveal.ClipToBounds);
        Assert.True(LoadButton(section).IsEffectivelyEnabled);
        Assert.True(card.IsHitTestVisible);
        Assert.Equal(AccessibilityView.Content, AutomationProperties.GetAccessibilityView(card));

        rig.Click(rig.HeaderOf(ProviderKind.Deepseek)); // collapse
        rig.Advance(2);

        Assert.Equal(0, reveal.Fraction); // closed to zero height
        Assert.False(reveal.IsEnabled);
        Assert.False(reveal.IsHitTestVisible);
        Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(reveal));
        Assert.False(LoadButton(section).IsEffectivelyEnabled);
        Assert.False(card.IsHitTestVisible);
        Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(card));

        rig.Click(rig.HeaderOf(ProviderKind.Deepseek)); // expand again
        rig.Advance(2);
        Assert.Equal(1, reveal.Fraction);
        Assert.True(LoadButton(section).IsEffectivelyEnabled);
        Assert.True(card.IsHitTestVisible);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-191")] // ModelsSettingsPage.swift:330 `if catalogue.isEmpty`: only an empty UNFILTERED catalogue shows the row, never a populated one that a search filters to nothing
    public void A_populated_catalogue_that_the_search_filters_to_nothing_shows_no_empty_row() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([ProviderKind.Codex], Catalogue(ProviderKind.Codex, Option("a", "Alpha"), Option("b", "Beta")));
        var section = new ProviderModelsSection(rig.Model, ProviderKind.Codex, new Binding<bool>(() => true, _ => { }), "zzz");
        rig.Mount(section);

        var text = TextOf(section);
        Assert.DoesNotContain("No models loaded", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Loading models", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Of<Button>(section), button => button.Content as string == "Load models");
        Assert.Equal([0, 0], Fractions(Reveals(section).Rows)); // every row is closed, none of them replaced by the empty row
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-191")] // ModelsSettingsPage.swift:324 cardVisible = isExpanded || isFiltering || …: a search reveals the empty row of a collapsed section
    public void A_collapsed_section_with_an_empty_catalogue_still_shows_its_row_while_a_search_is_active() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([], ClearCatalogue(ProviderKind.Deepseek));
        var collapsedSearching = new ProviderModelsSection(rig.Model, ProviderKind.Deepseek, new Binding<bool>(() => false, _ => { }), "x");
        rig.Mount(collapsedSearching);
        Assert.Equal(1, Assert.Single(Of<ChromeRevealLayout>(collapsedSearching)).Fraction);
        Assert.True(LoadButton(collapsedSearching).IsEffectivelyEnabled);

        var collapsedIdle = new ProviderModelsSection(rig.Model, ProviderKind.Deepseek, new Binding<bool>(() => false, _ => { }), "");
        rig.Mount(collapsedIdle);
        Assert.Equal(0, Assert.Single(Of<ChromeRevealLayout>(collapsedIdle)).Fraction);
        Assert.False(LoadButton(collapsedIdle).IsEffectivelyEnabled);
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-067")] // ModelsSettingsPage.swift:334-338 Button("Load models") .buttonStyle(.glass) .disabled(registry.loadingCatalogs.contains(provider))
    public void The_load_models_button_is_a_glass_button_that_is_disabled_while_that_provider_loads() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([ProviderKind.Deepseek], ClearCatalogue(ProviderKind.Deepseek));
        var button = LoadButton(rig.SectionOf(ProviderKind.Deepseek));
        var glass = Assert.Single(button.GetVisualDescendants().OfType<Control>().Select(SwiftGlass.GetEffect), effect => effect is not null);
        Assert.Equal(GlassEffect.Regular(GlassShape.Capsule(RoundedCornerStyle.Continuous)).Interactive(), glass);
        Assert.True(button.IsEnabled);

        rig.SetLoading(ProviderKind.Deepseek, true);
        Assert.False(LoadButton(rig.SectionOf(ProviderKind.Deepseek)).IsEnabled);
        rig.SetLoading(ProviderKind.Deepseek, false);
        Assert.True(LoadButton(rig.SectionOf(ProviderKind.Deepseek)).IsEnabled);
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-067")] // ModelsSettingsPage.swift:334-336 click starts a forced reload; :411 the section's own load is unforced
    public void Clicking_the_load_models_button_starts_a_forced_reload() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([ProviderKind.Deepseek], ClearCatalogue(ProviderKind.Deepseek));
        Assert.True(rig.IsGated(ProviderKind.Deepseek));
        Assert.Empty(rig.Model.Providers.Models(ProviderKind.Deepseek));
        rig.Click(LoadButton(rig.SectionOf(ProviderKind.Deepseek)));
        Assert.NotEmpty(rig.Model.Providers.Models(ProviderKind.Deepseek)); // only a forced ask passes the gate
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-067")] // ModelsSettingsPage.swift:324,330-344 revealed only for an empty unfiltered catalogue while expanded or searching; otherwise clipped, disabled, hidden from accessibility
    public void The_load_models_button_is_revealed_only_for_an_empty_catalogue_while_expanded_or_searching() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([ProviderKind.Deepseek, ProviderKind.Codex], model =>
        {
            ClearCatalogue(ProviderKind.Deepseek)(model);
            Catalogue(ProviderKind.Codex, Option("a", "Alpha"))(model);
        });
        Assert.DoesNotContain(Of<Button>(rig.SectionOf(ProviderKind.Codex)), button => button.Content as string == "Load models"); // a populated catalogue has none
        var empty = rig.SectionOf(ProviderKind.Deepseek);
        var reveal = Assert.Single(Of<ChromeRevealLayout>(empty));
        Assert.Equal(1, reveal.Fraction);

        rig.Click(rig.HeaderOf(ProviderKind.Deepseek));
        rig.Advance(2);
        Assert.Equal(0, reveal.Fraction);
        var card = Assert.IsType<Border>(Assert.IsType<StackPanel>(empty.Child).Children[1]);
        Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(card));
        Assert.False(LoadButton(empty).IsEffectivelyEnabled);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-269")] // ModelsSettingsPage.swift:12-14,80 `providerExpansion[provider] ?? true`: absent means expanded
    public void Every_provider_section_starts_expanded() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([ProviderKind.Codex, ProviderKind.Antigravity], model =>
        {
            Catalogue(ProviderKind.Codex, Option("a", "Alpha"))(model);
            Catalogue(ProviderKind.Antigravity, Option("b", "Beta"))(model);
        });
        foreach (var provider in new[] { ProviderKind.Codex, ProviderKind.Antigravity })
        {
            var header = rig.HeaderOf(provider);
            Assert.Equal("Expanded", AutomationProperties.GetItemStatus(header));
            Assert.Equal([1], Fractions(Reveals(rig.SectionOf(provider)).Rows));
        }
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-269")] // ModelsSettingsPage.swift:75-84 toggling stores that provider's state on the page; :12-14 typing in or clearing the search keeps it
    public void A_collapsed_section_stays_collapsed_through_typing_and_clearing_the_search_and_only_that_one() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([ProviderKind.Codex, ProviderKind.Antigravity], model =>
        {
            Catalogue(ProviderKind.Codex, Option("a", "Alpha"))(model);
            Catalogue(ProviderKind.Antigravity, Option("b", "Beta"))(model);
        });
        rig.Click(rig.HeaderOf(ProviderKind.Codex));
        rig.Advance(2);
        Assert.Equal("Collapsed", AutomationProperties.GetItemStatus(rig.HeaderOf(ProviderKind.Codex)));
        Assert.Equal("Expanded", AutomationProperties.GetItemStatus(rig.HeaderOf(ProviderKind.Antigravity)));
        Assert.Equal([0], Fractions(Reveals(rig.SectionOf(ProviderKind.Codex)).Rows));

        rig.Page.SetQuery("a"); // rebuilds the sections
        rig.Sync();
        Assert.Equal("Collapsed", AutomationProperties.GetItemStatus(rig.HeaderOf(ProviderKind.Codex)));
        rig.Page.SetQuery("");
        rig.Advance(2);

        Assert.Equal("Collapsed", AutomationProperties.GetItemStatus(rig.HeaderOf(ProviderKind.Codex)));
        Assert.Equal("Show the model list", ToolTip.GetTip(rig.HeaderOf(ProviderKind.Codex)));
        Assert.Equal([0], Fractions(Reveals(rig.SectionOf(ProviderKind.Codex)).Rows));
        Assert.Equal("Expanded", AutomationProperties.GetItemStatus(rig.HeaderOf(ProviderKind.Antigravity)));
        Assert.Equal([1], Fractions(Reveals(rig.SectionOf(ProviderKind.Antigravity)).Rows));
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-269")] // ModelsSettingsPage.swift:14 @State private var providerExpansion: view state, "never persisted"; rebuilt page = all expanded
    public void The_expansion_is_page_state_a_rebuilt_page_is_all_expanded_and_nothing_is_written() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([ProviderKind.Codex], Catalogue(ProviderKind.Codex, Option("a", "Alpha")));
        var writes = rig.Defaults.Writes.Count;
        rig.Click(rig.HeaderOf(ProviderKind.Codex));
        rig.Advance(2);
        rig.Page.SetQuery("alpha");
        rig.Page.SetQuery("");
        rig.Sync();
        Assert.Equal("Collapsed", AutomationProperties.GetItemStatus(rig.HeaderOf(ProviderKind.Codex)));
        Assert.Equal(writes, rig.Defaults.Writes.Count); // no setting was written

        var host = new Border();
        AppEnvironment.SetAppModel(host, rig.Model);
        var returned = new ModelsSettingsPage();
        host.Child = returned;
        rig.Mount(host); // leave the page and come back to a new one
        var header = Of<Button>(returned).First(button => AutomationProperties.GetName(button) == "Codex");
        Assert.Equal("Expanded", AutomationProperties.GetItemStatus(header));
        Assert.Equal(writes, rig.Defaults.Writes.Count);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-270")] // ModelsSettingsPage.swift:341-342,424 withAnimation(Chrome.panelSlide) { isExpanded.toggle() }, :436 the chevron turns in the same transaction
    public void Clicking_the_header_flips_the_section_and_the_chevron_together_in_the_panel_slide_animation() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([ProviderKind.Codex], Catalogue(ProviderKind.Codex, Option("a", "Alpha")));
        var header = rig.HeaderOf(ProviderKind.Codex);
        double Angle() => Assert.IsType<RotateTransform>(Of<SwiftImage>(header).Single(image => image.SymbolName == "chevron.right").RenderTransform).Angle;
        var row = Reveals(rig.SectionOf(ProviderKind.Codex)).Rows.Single();
        Assert.Equal(90, Angle()); // expanded: pointing down

        rig.Click(header);
        Assert.Equal("Collapsed", AutomationProperties.GetItemStatus(header));
        Assert.Equal(90, Angle(), 0.01); // the slide has not moved on yet
        Assert.Equal(1, row.Fraction);

        rig.Advance(0.1);
        var progress = Chrome.PanelSlide.Progress(0.1);
        Assert.InRange(progress, 0.05, 0.95);
        Assert.Equal(90 * (1 - progress), Angle(), 0.5); // mid-slide, on the panel-slide curve
        Assert.Equal(1 - progress, row.Fraction, 0.01); // the card slides closed in the same transaction

        rig.Advance(2);
        Assert.Equal(0, Angle(), 0.01); // collapsed: pointing right
        Assert.Equal(0, row.Fraction);

        rig.Click(header);
        rig.Advance(2);
        Assert.Equal(90, Angle(), 0.01);
        Assert.Equal(1, row.Fraction);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-270")] // ModelsSettingsPage.swift:442-445 .help, .accessibilityLabel/.accessibilityValue/.accessibilityHint from the page
    public void On_the_page_the_header_reads_the_two_state_tooltip_and_the_header_accessibility() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([ProviderKind.Codex], Catalogue(ProviderKind.Codex, Option("a", "Alpha")));
        var header = rig.HeaderOf(ProviderKind.Codex);
        Assert.Equal("Hide the model list", ToolTip.GetTip(header));
        Assert.Equal("Codex", AutomationProperties.GetName(header));
        Assert.Equal("Expanded", AutomationProperties.GetItemStatus(header));
        Assert.Equal("Shows or hides the model list", AutomationProperties.GetHelpText(header));
        Assert.Equal(1, AutomationProperties.GetHeadingLevel(header));

        rig.Click(header);
        Assert.Equal("Show the model list", ToolTip.GetTip(header));
        Assert.Equal("Collapsed", AutomationProperties.GetItemStatus(header));
        Assert.Equal("Codex", AutomationProperties.GetName(header));
        Assert.Equal("Shows or hides the model list", AutomationProperties.GetHelpText(header));
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-270")] // ModelsSettingsPage.swift:438,440-441 .contentShape(.rect), ChromeQuietButtonStyle (no pressed feedback), .frame(height: 22); Chrome.swift:275-280
    public void The_header_is_22_points_tall_clickable_across_its_whole_width_and_has_no_pressed_feedback() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([ProviderKind.Codex], Catalogue(ProviderKind.Codex, Option("a", "Alpha")));
        var header = rig.HeaderOf(ProviderKind.Codex);
        Assert.Equal(22, header.Bounds.Height, 0.01);
        var origin = header.TranslatePoint(default, rig.Window)!.Value;
        var wide = new Point(origin.X + header.Bounds.Width / 2, origin.Y + 11); // the empty spacer between the name and the chevron

        var before = Of<TextBlock>(header).Select(text => (text.Opacity, text.Foreground)).ToArray();
        rig.Window.MouseMove(wide);
        rig.Window.MouseDown(wide, Avalonia.Input.MouseButton.Left);
        rig.Sync();
        Assert.Equal(before, Of<TextBlock>(header).Select(text => (text.Opacity, text.Foreground)).ToArray()); // nothing dims while held
        Assert.Equal(1, header.Opacity);
        rig.Window.MouseUp(wide, Avalonia.Input.MouseButton.Left);
        rig.Sync();

        Assert.Equal("Collapsed", AutomationProperties.GetItemStatus(header)); // the click on empty space toggled it
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-080")] // ModelsSettingsPage.swift:426-437,441 HStack(spacing: 8) of icon 16, name, Spacer(minLength: 0), chevron; .frame(height: 22); :325 10 above the card
    public void The_header_lays_out_icon_name_and_chevron_8_points_apart_with_the_chevron_at_the_far_edge() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([ProviderKind.Codex], Catalogue(ProviderKind.Codex, Option("a", "Alpha")));
        var header = rig.HeaderOf(ProviderKind.Codex);
        double Left(Control control) => control.TranslatePoint(default, rig.Window)!.Value.X;
        var icon = Of<ProviderIcon>(header).Single();
        var name = Of<TextBlock>(header).Single(text => text.Text == "Codex");
        var chevron = Of<SwiftImage>(header).Single(image => image.SymbolName == "chevron.right");

        Assert.Equal(22, header.Bounds.Height, 0.01);
        Assert.Equal(Left(header), Left(icon), 0.5);
        var glyph = Assert.IsType<SwiftImage>(icon.Child);
        Assert.Equal(16, glyph.Bounds.Width, 0.01);
        Assert.Equal(16, glyph.Bounds.Height, 0.01);
        Assert.Equal(8, Left(name) - (Left(icon) + icon.Bounds.Width), 0.5);
        var line = Assert.IsType<Grid>(chevron.Parent);
        Assert.Equal(line.Bounds.Width, chevron.Bounds.Right, 0.5); // the spacer pushes the chevron to the far edge (layout bounds: the chevron is turned)
        Assert.True(chevron.Bounds.X - name.Bounds.Right > 100); // and it is not hugging the name
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-080")] // ModelsSettingsPage.swift:429-435 name 13 semibold hierarchical .secondary; chevron 11 semibold Chrome.secondaryText
    public void The_header_text_and_chevron_use_the_source_typography_and_colours() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([ProviderKind.Codex], Catalogue(ProviderKind.Codex, Option("a", "Alpha")));
        var header = rig.HeaderOf(ProviderKind.Codex);
        var name = Of<TextBlock>(header).Single(text => text.Text == "Codex");
        Assert.Equal(13, name.FontSize);
        Assert.Equal(FontWeight.SemiBold, name.FontWeight);
        Assert.Equal(SwiftColor.Secondary.Resolve(name.ActualThemeVariant), Resolved(name, name.Foreground));

        var chevron = Of<SwiftImage>(header).Single(image => image.SymbolName == "chevron.right");
        var font = SwiftImage.GetFont(chevron)!.Resolve();
        Assert.Equal(11, font.FontSize);
        Assert.Equal(FontWeight.SemiBold, font.FontWeight);
        Assert.Equal(Chrome.SecondaryText.Resolve(chevron.ActualThemeVariant), Resolved(chevron, chevron.Foreground));
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-080")] // ModelsSettingsPage.swift:436,442,444 chevron at 90 degrees, "Hide the model list" and "Expanded" when expanded (the default); 0 degrees, "Show the model list" and "Collapsed" when collapsed
    public void The_header_states_expanded_by_default_and_collapsed_after_a_click() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([ProviderKind.Codex], Catalogue(ProviderKind.Codex, Option("a", "Alpha")));
        var header = rig.HeaderOf(ProviderKind.Codex);
        double Angle() => Assert.IsType<RotateTransform>(Of<SwiftImage>(header).Single(image => image.SymbolName == "chevron.right").RenderTransform).Angle;
        Assert.Equal((90d, "Hide the model list", "Expanded"), (Angle(), (string?)ToolTip.GetTip(header), AutomationProperties.GetItemStatus(header)));

        rig.Click(header);
        rig.Advance(2);
        Assert.Equal((0d, "Show the model list", "Collapsed"), (Angle(), (string?)ToolTip.GetTip(header), AutomationProperties.GetItemStatus(header)));
        Assert.Equal("Codex", AutomationProperties.GetName(header));
        Assert.Equal("Shows or hides the model list", AutomationProperties.GetHelpText(header));
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-080")] // ModelsSettingsPage.swift:423-425 Button { withAnimation(Chrome.panelSlide) { isExpanded.toggle() } }: keyboard activation toggles like a click
    public void Activating_the_focused_header_from_the_keyboard_toggles_the_section() => headless.OnUiThread(() =>
    {
        using var rig = new ModelsSettingsSectionRig([ProviderKind.Codex], Catalogue(ProviderKind.Codex, Option("a", "Alpha")));
        var header = rig.HeaderOf(ProviderKind.Codex);
        header.Focus();
        rig.Sync();
        Assert.True(header.IsFocused);
        rig.Window.KeyPress(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Enter, "\r");
        rig.Window.KeyRelease(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Enter, "\r");
        rig.Sync();
        Assert.Equal("Collapsed", AutomationProperties.GetItemStatus(header));
    });
}
