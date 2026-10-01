using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Controls.Primitives;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.HydraSettingsPageRig;

namespace Marrow.App.Tests.Views.Settings;

// Swift-derived tests (test-first) for DroppyCode/App/Views/Settings/HydraSettingsPage.swift (B3 52bc792): the pair editor
// (:336-589), its rows and metrics. The page, the pair row and the buttons are in the neighbouring Spec test files.
[Collection("Headless")]
public sealed class HydraSettingsPagePairSpecTests(HeadlessSession headless)
{
    private static ValueList<string> Efforts(params string[] efforts) => new(efforts);

    private void Editor(HydraPair pair, Action<HydraSettingsScene> check, Action<AppModel>? arrange = null, double width = 360) =>
        Run(headless, _ => new HydraPairEditor(pair.Id) { Width = width }, check, 900, model =>
        {
            model.Settings.AddHydraPair(pair);
            arrange?.Invoke(model);
        });

    private static ModelOption Model(string id, string name, params string[] efforts) => new(id, name, efforts: Efforts(efforts));

    // ---- PR-SETTINGS-167, UI-SETTINGS-036 --------------------------------------------------------------------------

    private static readonly ModelOption Alpha = Model("alpha", "Alpha-One", "low", "high");

    [Fact]
    [Trait("Req", "PR-SETTINGS-167")] // HydraSettingsPage.swift:382,440,544-558 two labels, Lead then Heads
    [Trait("Req", "UI-SETTINGS-036")] // HydraSettingsPage.swift:382-498 the order of the rows
    public void The_editor_draws_Lead_then_Heads_with_the_sources_rows_in_order() => Editor(new HydraPair(ProviderKind.Claude), scene =>
    {
        Assert.Equal(new[]
        {
            "section:Lead", "row:Provider", "row:Model", "row:Effort",
            "section:Heads", "row:Provider", "row:Model", "row:Effort", "row:At once",
        }, EditorOutline(scene.View).ToArray());
    }, model => SetCatalog(model, ProviderKind.Claude, Alpha));

    [Fact]
    [Trait("Req", "PR-SETTINGS-167")] // HydraSettingsPage.swift:546-558 a hairline inset 14 with 12 above, then the word at subheadline semibold in the secondary colour with 10 above and 4 below
    public void A_section_label_is_a_hairline_over_a_quiet_word_with_the_sources_insets() => Editor(new HydraPair(ProviderKind.Claude), scene =>
    {
        foreach (var label in EditorChildren(scene.View).Skip(1).OfType<StackPanel>())
        {
            Assert.Equal(0, label.Spacing);
            Assert.Equal(2, label.Children.Count);
            var line = Assert.IsType<Border>(label.Children[0]);
            Assert.Equal(1, line.Height);
            Assert.Equal(new Thickness(14, 12, 14, 0), line.Margin);
            var word = Assert.IsType<TextBlock>(label.Children[1]);
            Assert.Equal(new Thickness(14, 10, 14, 4), word.Margin);
            Assert.Equal(11, word.FontSize); // .subheadline
            Assert.Equal(FontWeight.SemiBold, word.FontWeight);
            Assert.Equal(Chrome.SecondaryText.Resolve(word.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(word.Foreground).Color);
        }
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-167")] // HydraSettingsPage.swift:548 Divider(): the port's one hairline for a Divider() (PlainControls.Divider), not the secondary text colour
    public void A_section_labels_hairline_is_the_ports_divider_wash() => Editor(new HydraPair(ProviderKind.Claude), scene =>
    {
        var line = Assert.IsType<Border>(EditorChildren(scene.View).Skip(1).OfType<StackPanel>().First().Children[0]);
        // Every Divider() in a vertical stack draws the port's one hairline (PlainControls.Divider: primary text at 0.22).
        Assert.Equal(Chrome.PrimaryText.Opacity(0.22).Resolve(line.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(line.Background).Color);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-167")] // HydraSettingsPage.swift:522-542 rows 14 inset and 5 padded, 12 between label and control, a one-line tail-truncated title, no detail line
    [Trait("Req", "UI-SETTINGS-036")] // HydraSettingsPage.swift:522-542 row geometry and typography
    public void An_editor_row_has_the_sources_padding_spacing_and_one_line_title_and_never_a_detail() => Editor(new HydraPair(ProviderKind.Claude), scene =>
    {
        var rows = EditorChildren(scene.View).OfType<Grid>().ToArray();
        Assert.Equal(7, rows.Length);
        foreach (var row in rows)
        {
            Assert.Equal(new Thickness(14, 5), row.Margin);
            Assert.Equal(new[] { "*", "12", "Auto" }, row.ColumnDefinitions.Select(column => column.Width.ToString()).Select(text => text.Replace("1*", "*", StringComparison.Ordinal)).ToArray());
            var column = Assert.IsType<StackPanel>(row.Children.Single(child => Grid.GetColumn(child) == 0));
            Assert.Single(column.Children); // no editor row passes a detail
            var title = Assert.IsType<TextBlock>(column.Children[0]);
            Assert.Equal(1, title.MaxLines);
            Assert.Equal(TextTrimming.CharacterEllipsis, title.TextTrimming);
            Assert.Equal(13, title.FontSize); // .body
            Assert.Equal(Chrome.PrimaryText.Resolve(title.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(title.Foreground).Color);
        }
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-167")] // HydraSettingsPage.swift:537-538 control().layoutPriority(1): short of room the title gives way, the control does not
    public void When_the_editor_is_narrow_the_control_keeps_its_width_and_the_title_gives_way() => Editor(new HydraPair(ProviderKind.Claude), scene =>
    {
        var wide = (Control)RequireEditorControl(scene.View, "Lead", "Model");
        var wideWidth = wide.Bounds.Width;
        var wideTitle = Of<TextBlock>(((Control)wide.Parent!)).First();
        var wideTitleWidth = wideTitle.Bounds.Width;
        scene.View.Width = wideWidth + 14 + 14 + 12 + 20;
        scene.View.InvalidateMeasure();
        Sync();
        var narrow = (Control)RequireEditorControl(scene.View, "Lead", "Model");
        Assert.Equal(wideWidth, narrow.Bounds.Width, 0.5);
        var title = Of<TextBlock>(((Control)narrow.Parent!)).First();
        Assert.True(title.Bounds.Width < wideTitleWidth);
        Assert.Equal("Model", title.Text);
    }, model => SetCatalog(model, ProviderKind.Claude, Alpha), 600);

    [Fact]
    [Trait("Req", "UI-SETTINGS-036")] // HydraSettingsPage.swift:425,475 the two model pickers are capped at 190 wide; the others are not
    public void The_two_model_pickers_are_capped_at_190_and_the_others_are_not() => Editor(new HydraPair(ProviderKind.Claude), scene =>
    {
        Assert.Equal(190, ((Control)RequireEditorControl(scene.View, "Lead", "Model")).MaxWidth);
        Assert.Equal(190, ((Control)RequireEditorControl(scene.View, "Heads", "Model")).MaxWidth);
        Assert.NotEqual(190, ((Control)RequireEditorControl(scene.View, "Lead", "Provider")).MaxWidth);
        Assert.NotEqual(190, ((Control)RequireEditorControl(scene.View, "Heads", "At once")).MaxWidth);
    }, model => SetCatalog(model, ProviderKind.Claude, Alpha));

    // ---- PR-SETTINGS-168 --------------------------------------------------------------------------------------------

    private static HydraPair Configured(ProviderKind lead, ProviderKind? headsProvider = null) =>
        new(lead)
        {
            OrchestratorModel = "alpha", OrchestratorEffort = "high",
            WorkerProvider = headsProvider, WorkerModel = "worker-model", WorkerEffort = "low",
        };

    private static void Arrange(AppModel model, params ProviderKind[] installed)
    {
        SetInstalled(model, installed);
        SetCatalog(model, ProviderKind.Claude, Alpha);
        SetCatalog(model, ProviderKind.Codex, Alpha);
        SetCatalog(model, ProviderKind.Cursor, Alpha);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-168")] // HydraSettingsPage.swift:348,383-387 every enabled installed provider, plus the pair's own provider though it is not installed
    public void The_lead_provider_picker_offers_installed_enabled_providers_and_the_pairs_own() => Editor(Configured(ProviderKind.Claude), scene =>
    {
        // Claude is not installed here but is the pair's; Codex and Cursor are installed, Cursor switched off.
        var titles = PickerTitles(scene, RequireEditorControl(scene.View, "Lead", "Provider"));
        Assert.Equal(new[] { ProviderKind.Codex.DisplayName, ProviderKind.Claude.DisplayName }, titles.ToArray()); // ProviderKind.allCases order: codex first (Provider.swift:4-5)
    }, model =>
    {
        Arrange(model, ProviderKind.Codex, ProviderKind.Cursor);
        model.Settings.SetEnabled(false, ProviderKind.Cursor);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-168")] // HydraSettingsPage.swift:388-406 a changed provider clears the lead's model and effort and, heads without a provider of their own, the heads' too
    public void Changing_the_lead_provider_clears_the_lead_and_the_heads_that_were_on_the_old_provider() => Editor(Configured(ProviderKind.Claude), scene =>
    {
        PickerChoose(scene, RequireEditorControl(scene.View, "Lead", "Provider"), ProviderKind.Codex.DisplayName);
        var pair = scene.Model.Settings.HydraPairs.Single();
        Assert.Equal(ProviderKind.Codex, pair.Provider);
        Assert.Null(pair.OrchestratorModel);
        Assert.Null(pair.OrchestratorEffort);
        Assert.Null(pair.WorkerProvider);
        Assert.Null(pair.WorkerModel);
        Assert.Null(pair.WorkerEffort);
    }, model => Arrange(model, ProviderKind.Claude, ProviderKind.Codex));

    [Fact]
    [Trait("Req", "PR-SETTINGS-168")] // HydraSettingsPage.swift:401-402 heads on the provider the lead moves to are simply on the lead's again: the override goes, their model and effort stay
    public void Moving_the_lead_onto_the_heads_provider_drops_the_override_and_keeps_the_heads_choices() => Editor(Configured(ProviderKind.Claude, ProviderKind.Codex), scene =>
    {
        PickerChoose(scene, RequireEditorControl(scene.View, "Lead", "Provider"), ProviderKind.Codex.DisplayName);
        var pair = scene.Model.Settings.HydraPairs.Single();
        Assert.Equal(ProviderKind.Codex, pair.Provider);
        Assert.Null(pair.OrchestratorModel);
        Assert.Null(pair.OrchestratorEffort);
        Assert.Null(pair.WorkerProvider);
        Assert.Equal("worker-model", pair.WorkerModel);
        Assert.Equal("low", pair.WorkerEffort);
    }, model => Arrange(model, ProviderKind.Claude, ProviderKind.Codex, ProviderKind.Cursor));

    [Fact]
    [Trait("Req", "PR-SETTINGS-168")] // HydraSettingsPage.swift:398-403 heads on a provider of their own that is not the new lead's are left alone
    public void Moving_the_lead_elsewhere_leaves_heads_on_their_own_provider_untouched() => Editor(Configured(ProviderKind.Claude, ProviderKind.Cursor), scene =>
    {
        PickerChoose(scene, RequireEditorControl(scene.View, "Lead", "Provider"), ProviderKind.Codex.DisplayName);
        var pair = scene.Model.Settings.HydraPairs.Single();
        Assert.Equal(ProviderKind.Codex, pair.Provider);
        Assert.Null(pair.OrchestratorModel);
        Assert.Null(pair.OrchestratorEffort);
        Assert.Equal(ProviderKind.Cursor, pair.WorkerProvider);
        Assert.Equal("worker-model", pair.WorkerModel);
        Assert.Equal("low", pair.WorkerEffort);
    }, model => Arrange(model, ProviderKind.Claude, ProviderKind.Codex, ProviderKind.Cursor));

    [Fact]
    [Trait("Req", "PR-SETTINGS-168")] // HydraSettingsPage.swift:390 guard $0.provider != provider: choosing the same provider changes nothing
    public void Choosing_the_same_lead_provider_is_a_no_op() => Editor(Configured(ProviderKind.Claude), scene =>
    {
        var before = scene.Model.Settings.HydraPairs.Single();
        PickerChoose(scene, RequireEditorControl(scene.View, "Lead", "Provider"), ProviderKind.Claude.DisplayName);
        Assert.Equal(before, scene.Model.Settings.HydraPairs.Single());
    }, model => Arrange(model, ProviderKind.Claude, ProviderKind.Codex));

    // ---- PR-SETTINGS-169 --------------------------------------------------------------------------------------------

    private static readonly ModelOption Beta = Model("beta", "Beta", "high", "max");
    private static readonly ModelOption Gamma = Model("gamma", "Gamma");

    [Fact]
    [Trait("Req", "PR-SETTINGS-169")] // HydraSettingsPage.swift:411-427 "Any model" first, then the catalog's short names in catalog order
    public void The_lead_model_picker_lists_Any_model_then_the_catalog_in_order() => Editor(new HydraPair(ProviderKind.Claude), scene =>
    {
        Assert.Equal(new[] { "Any model", Alpha.ShortName, Beta.ShortName, Gamma.ShortName }, PickerTitles(scene, RequireEditorControl(scene.View, "Lead", "Model")).ToArray());
    }, model => SetCatalog(model, ProviderKind.Claude, Alpha, Beta, Gamma));

    [Fact]
    [Trait("Req", "PR-SETTINGS-169")] // HydraSettingsPage.swift:419-421 a chosen model that lacks the chosen effort clears it in the same update
    public void Choosing_a_lead_model_without_the_chosen_effort_clears_the_effort() => Editor(new HydraPair(ProviderKind.Claude) { OrchestratorModel = "alpha", OrchestratorEffort = "low" }, scene =>
    {
        PickerChoose(scene, RequireEditorControl(scene.View, "Lead", "Model"), Beta.ShortName);
        var pair = scene.Model.Settings.HydraPairs.Single();
        Assert.Equal("beta", pair.OrchestratorModel);
        Assert.Null(pair.OrchestratorEffort);
    }, model => SetCatalog(model, ProviderKind.Claude, Alpha, Beta, Gamma));

    [Fact]
    [Trait("Req", "PR-SETTINGS-169")] // HydraSettingsPage.swift:419-421 a model that does declare the chosen effort keeps it
    public void Choosing_a_lead_model_that_has_the_chosen_effort_keeps_it() => Editor(new HydraPair(ProviderKind.Claude) { OrchestratorModel = "alpha", OrchestratorEffort = "high" }, scene =>
    {
        PickerChoose(scene, RequireEditorControl(scene.View, "Lead", "Model"), Beta.ShortName);
        var pair = scene.Model.Settings.HydraPairs.Single();
        Assert.Equal("beta", pair.OrchestratorModel);
        Assert.Equal("high", pair.OrchestratorEffort);
    }, model => SetCatalog(model, ProviderKind.Claude, Alpha, Beta, Gamma));

    [Fact]
    [Trait("Req", "PR-SETTINGS-169")] // HydraSettingsPage.swift:419 when the model is not in the catalog at all (Any model), the lookup returns nothing and the effort is left alone
    public void Choosing_Any_model_leaves_the_chosen_effort_alone() => Editor(new HydraPair(ProviderKind.Claude) { OrchestratorModel = "alpha", OrchestratorEffort = "low" }, scene =>
    {
        PickerChoose(scene, RequireEditorControl(scene.View, "Lead", "Model"), "Any model");
        var pair = scene.Model.Settings.HydraPairs.Single();
        Assert.Null(pair.OrchestratorModel);
        Assert.Equal("low", pair.OrchestratorEffort);
    }, model => SetCatalog(model, ProviderKind.Claude, Alpha, Beta, Gamma));

    // ---- PR-SETTINGS-170 --------------------------------------------------------------------------------------------

    private static IReadOnlyList<string> Effort(HydraSettingsScene scene, string section) =>
        PickerTitles(scene, RequireEditorControl(scene.View, section, "Effort"));

    [Fact]
    [Trait("Req", "PR-SETTINGS-170")] // HydraSettingsPage.swift:355,428-439,568-574 no model chosen: every effort any model offers, deduplicated in first-seen order, after "Chat's effort"
    public void With_no_lead_model_the_effort_picker_offers_every_effort_the_provider_knows() => Editor(new HydraPair(ProviderKind.Claude), scene =>
    {
        Assert.Equal(new[] { "Chat's effort", "Low", "High", "Max" }, Effort(scene, "Lead").ToArray());
    }, model => SetCatalog(model, ProviderKind.Claude, Alpha, Beta, Gamma));

    [Fact]
    [Trait("Req", "PR-SETTINGS-170")] // HydraSettingsPage.swift:579-582 a chosen model offers its own scale
    public void A_chosen_lead_model_offers_its_own_scale() => Editor(new HydraPair(ProviderKind.Claude) { OrchestratorModel = "beta" }, scene =>
    {
        Assert.Equal(new[] { "Chat's effort", "High", "Max" }, Effort(scene, "Lead").ToArray());
    }, model => SetCatalog(model, ProviderKind.Claude, Alpha, Beta, Gamma));

    [Fact]
    [Trait("Req", "PR-SETTINGS-170")] // HydraSettingsPage.swift:579-582 a chosen model that declares no scale falls back to every effort of the provider
    public void A_chosen_lead_model_with_no_declared_scale_falls_back_to_the_providers_efforts() => Editor(new HydraPair(ProviderKind.Claude) { OrchestratorModel = "gamma" }, scene =>
    {
        Assert.Equal(new[] { "Chat's effort", "Low", "High", "Max" }, Effort(scene, "Lead").ToArray());
    }, model => SetCatalog(model, ProviderKind.Claude, Alpha, Beta, Gamma));

    [Fact]
    [Trait("Req", "PR-SETTINGS-170")] // ModelOption.swift:286-288 as the picker reads it: xhigh and extra-high are Extra High, "" is Default, others first-letter upper-cased
    public void Effort_options_read_through_the_effort_title_rule() => Editor(new HydraPair(ProviderKind.Claude) { OrchestratorModel = "odd" }, scene =>
    {
        Assert.Equal(new[] { "Chat's effort", "Extra High", "Extra High", "Default", "Medium", "High" }, Effort(scene, "Lead").ToArray());
    }, model => SetCatalog(model, ProviderKind.Claude, Model("odd", "Odd", "xhigh", "extra-high", "", "medium", "high")));

    [Fact]
    [Trait("Req", "PR-SETTINGS-170")] // HydraSettingsPage.swift:428,478 an effort row is drawn only when its side has efforts to offer
    public void Without_any_effort_anywhere_neither_effort_row_is_drawn() => Editor(new HydraPair(ProviderKind.Claude), scene =>
    {
        Assert.Equal(new[] { "section:Lead", "row:Provider", "row:Model", "section:Heads", "row:Provider", "row:Model", "row:At once" }, EditorOutline(scene.View).ToArray());
        Assert.Null(EditorControl(scene.View, "Lead", "Effort"));
        Assert.Null(EditorControl(scene.View, "Heads", "Effort"));
    }, model => SetCatalog(model, ProviderKind.Claude, Gamma));

    [Fact]
    [Trait("Req", "UI-SETTINGS-036")] // HydraSettingsPage.swift:428,478 an Effort row is absent entirely when its side has no efforts to offer (the rows, not an empty picker)
    public void An_effort_row_is_absent_not_empty_when_its_side_has_nothing_to_offer() => Editor(new HydraPair(ProviderKind.Claude) { WorkerProvider = ProviderKind.Codex }, scene =>
    {
        Assert.NotNull(EditorControl(scene.View, "Lead", "Effort"));
        Assert.Null(EditorControl(scene.View, "Heads", "Effort"));
    }, model =>
    {
        SetInstalled(model, ProviderKind.Claude, ProviderKind.Codex);
        SetCatalog(model, ProviderKind.Claude, Alpha);
        SetCatalog(model, ProviderKind.Codex, Gamma);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-170")] // HydraSettingsPage.swift:358,482 heads on the lead's provider with no model of their own offer the lead's own list, inheriting as "Same as the chat"
    public void Heads_on_the_leads_provider_offer_the_leads_efforts_and_inherit_the_chat() => Editor(new HydraPair(ProviderKind.Claude) { OrchestratorModel = "beta" }, scene =>
    {
        Assert.Equal(new[] { "Same as the chat", "High", "Max" }, Effort(scene, "Heads").ToArray());
    }, model => SetCatalog(model, ProviderKind.Claude, Alpha, Beta, Gamma));

    [Fact]
    [Trait("Req", "PR-SETTINGS-170")] // HydraSettingsPage.swift:353,358 a chosen heads model offers its own scale, looked up on the heads provider
    public void A_chosen_heads_model_offers_its_own_scale() => Editor(new HydraPair(ProviderKind.Claude) { OrchestratorModel = "beta", WorkerModel = "alpha" }, scene =>
    {
        Assert.Equal(new[] { "Same as the chat", "Low", "High" }, Effort(scene, "Heads").ToArray());
    }, model => SetCatalog(model, ProviderKind.Claude, Alpha, Beta, Gamma));

    private static readonly ModelOption Mini = new("mini", "Mini", efforts: Efforts("minimal", "low"), isDefault: true);
    private static readonly ModelOption Pro = Model("pro", "Pro", "medium", "high");

    [Fact]
    [Trait("Req", "PR-SETTINGS-170")] // HydraSettingsPage.swift:358,482 heads elsewhere with no model: the heads provider's default model's scale, inheriting as "<Provider>'s default"
    public void Heads_elsewhere_with_no_model_offer_the_default_models_efforts_and_inherit_the_providers_default() => Editor(new HydraPair(ProviderKind.Claude) { OrchestratorModel = "beta", WorkerProvider = ProviderKind.Codex }, scene =>
    {
        Assert.Equal(new[] { $"{ProviderKind.Codex.DisplayName}'s default", "Minimal", "Low" }, Effort(scene, "Heads").ToArray());
    }, model =>
    {
        SetInstalled(model, ProviderKind.Claude, ProviderKind.Codex);
        SetCatalog(model, ProviderKind.Claude, Alpha, Beta, Gamma);
        SetCatalog(model, ProviderKind.Codex, Pro, Mini);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-170")] // HydraSettingsPage.swift:358 heads elsewhere whose default model declares no scale fall back to every effort of the heads provider
    public void Heads_elsewhere_whose_default_model_has_no_scale_offer_every_effort_of_their_provider() => Editor(new HydraPair(ProviderKind.Claude) { WorkerProvider = ProviderKind.Codex }, scene =>
    {
        Assert.Equal(new[] { $"{ProviderKind.Codex.DisplayName}'s default", "Medium", "High" }, Effort(scene, "Heads").ToArray());
    }, model =>
    {
        SetInstalled(model, ProviderKind.Claude, ProviderKind.Codex);
        SetCatalog(model, ProviderKind.Claude, Alpha);
        SetCatalog(model, ProviderKind.Codex, new ModelOption("bare", "Bare", isDefault: true), Pro);
    });

    // ---- PR-SETTINGS-171 --------------------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-SETTINGS-171")] // HydraSettingsPage.swift:441-460 "Same as the lead" first, then every enabled installed provider (or the current heads provider) except the lead's
    public void The_heads_provider_picker_offers_Same_as_the_lead_then_the_other_providers() => Editor(new HydraPair(ProviderKind.Claude), scene =>
    {
        Assert.Equal(new[] { "Same as the lead", ProviderKind.Codex.DisplayName }, PickerTitles(scene, RequireEditorControl(scene.View, "Heads", "Provider")).ToArray());
    }, model =>
    {
        SetInstalled(model, ProviderKind.Claude, ProviderKind.Codex, ProviderKind.Cursor);
        model.Settings.SetEnabled(false, ProviderKind.Cursor);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-171")] // HydraSettingsPage.swift:348-349,443 the pair's current heads provider is offered though it is not installed
    public void The_current_heads_provider_stays_on_offer_even_when_it_is_not_installed() => Editor(new HydraPair(ProviderKind.Claude) { WorkerProvider = ProviderKind.Cursor }, scene =>
    {
        Assert.Equal(new[] { "Same as the lead", ProviderKind.Cursor.DisplayName }, PickerTitles(scene, RequireEditorControl(scene.View, "Heads", "Provider")).ToArray());
    }, model => SetInstalled(model, ProviderKind.Claude));

    [Fact]
    [Trait("Req", "PR-SETTINGS-171")] // HydraSettingsPage.swift:458 asset: ($0 ?? pair.provider).iconName: with no override the lead provider's icon, otherwise the heads provider's
    public void The_heads_provider_picker_shows_the_leads_icon_until_heads_go_elsewhere() => Editor(new HydraPair(ProviderKind.Claude), scene =>
    {
        var picker = RequireEditorControl(scene.View, "Heads", "Provider");
        Assert.Equal(ProviderKind.Claude.IconName, Of<SwiftImage>(picker).Single(image => image.AssetName is not null).AssetName);
        PickerChoose(scene, picker, ProviderKind.Codex.DisplayName);
        picker = RequireEditorControl(scene.View, "Heads", "Provider");
        Assert.Equal(ProviderKind.Codex.IconName, Of<SwiftImage>(picker).Single(image => image.AssetName is not null).AssetName);
    }, model => SetInstalled(model, ProviderKind.Claude, ProviderKind.Codex));

    [Fact]
    [Trait("Req", "PR-SETTINGS-171")] // HydraSettingsPage.swift:447-454 a real change clears the heads' model and effort
    public void Choosing_another_heads_provider_stores_it_and_clears_the_heads_model_and_effort() => Editor(Configured(ProviderKind.Claude), scene =>
    {
        PickerChoose(scene, RequireEditorControl(scene.View, "Heads", "Provider"), ProviderKind.Codex.DisplayName);
        var pair = scene.Model.Settings.HydraPairs.Single();
        Assert.Equal(ProviderKind.Codex, pair.WorkerProvider);
        Assert.Null(pair.WorkerModel);
        Assert.Null(pair.WorkerEffort);
        Assert.Equal("alpha", pair.OrchestratorModel);
        Assert.Equal("high", pair.OrchestratorEffort);
    }, model => Arrange(model, ProviderKind.Claude, ProviderKind.Codex));

    [Fact]
    [Trait("Req", "PR-SETTINGS-171")] // HydraSettingsPage.swift:448,451-453 "Same as the lead" over heads elsewhere stores nil, not an explicit equal value, and clears the heads' model and effort
    public void Choosing_Same_as_the_lead_removes_the_override_and_clears_the_heads_choices() => Editor(Configured(ProviderKind.Claude, ProviderKind.Codex), scene =>
    {
        PickerChoose(scene, RequireEditorControl(scene.View, "Heads", "Provider"), "Same as the lead");
        var pair = scene.Model.Settings.HydraPairs.Single();
        Assert.Null(pair.WorkerProvider);
        Assert.Null(pair.WorkerModel);
        Assert.Null(pair.WorkerEffort);
    }, model => Arrange(model, ProviderKind.Claude, ProviderKind.Codex));

    [Fact]
    [Trait("Req", "PR-SETTINGS-171")] // HydraSettingsPage.swift:449 guard $0.workerProvider != chosen: a choice that changes nothing is a no-op and keeps the heads' model and effort
    public void Choosing_Same_as_the_lead_when_there_is_no_override_changes_nothing() => Editor(Configured(ProviderKind.Claude), scene =>
    {
        var before = scene.Model.Settings.HydraPairs.Single();
        PickerChoose(scene, RequireEditorControl(scene.View, "Heads", "Provider"), "Same as the lead");
        Assert.Equal(before, scene.Model.Settings.HydraPairs.Single());
        Assert.Equal("worker-model", scene.Model.Settings.HydraPairs.Single().WorkerModel);
    }, model => Arrange(model, ProviderKind.Claude, ProviderKind.Codex));

    // ---- PR-SETTINGS-172 --------------------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-SETTINGS-172")] // HydraSettingsPage.swift:461-464 heads on the lead's provider: "Same as the chat" first, then the catalog's short names
    public void The_heads_model_picker_inherits_the_chat_while_heads_run_on_the_leads_provider() => Editor(new HydraPair(ProviderKind.Claude), scene =>
    {
        Assert.Equal(new[] { "Same as the chat", Alpha.ShortName, Beta.ShortName }, PickerTitles(scene, RequireEditorControl(scene.View, "Heads", "Model")).ToArray());
    }, model => SetCatalog(model, ProviderKind.Claude, Alpha, Beta));

    [Fact]
    [Trait("Req", "PR-SETTINGS-172")] // HydraSettingsPage.swift:463 heads elsewhere: "<Provider>'s default" first, then the heads provider's own catalog
    public void The_heads_model_picker_inherits_the_heads_providers_default_while_heads_run_elsewhere() => Editor(new HydraPair(ProviderKind.Claude) { WorkerProvider = ProviderKind.Codex }, scene =>
    {
        Assert.Equal(new[] { $"{ProviderKind.Codex.DisplayName}'s default", Pro.ShortName, Mini.ShortName }, PickerTitles(scene, RequireEditorControl(scene.View, "Heads", "Model")).ToArray());
    }, model =>
    {
        SetInstalled(model, ProviderKind.Claude, ProviderKind.Codex);
        SetCatalog(model, ProviderKind.Claude, Alpha, Beta);
        SetCatalog(model, ProviderKind.Codex, Pro, Mini);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-172")] // HydraSettingsPage.swift:469-471 an already-chosen heads effort the new model does not declare is cleared, looked up on the heads provider
    public void Choosing_a_heads_model_without_the_chosen_effort_clears_it_using_the_heads_provider() => Editor(new HydraPair(ProviderKind.Claude) { WorkerProvider = ProviderKind.Codex, WorkerModel = "pro", WorkerEffort = "high" }, scene =>
    {
        // "mini" exists on Codex only, with no "high": a lookup on the lead's provider (Claude) would find nothing and keep the effort.
        PickerChoose(scene, RequireEditorControl(scene.View, "Heads", "Model"), Mini.ShortName);
        var pair = scene.Model.Settings.HydraPairs.Single();
        Assert.Equal("mini", pair.WorkerModel);
        Assert.Null(pair.WorkerEffort);
    }, model =>
    {
        SetInstalled(model, ProviderKind.Claude, ProviderKind.Codex);
        SetCatalog(model, ProviderKind.Claude, Alpha, Beta);
        SetCatalog(model, ProviderKind.Codex, Pro, Mini);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-172")] // HydraSettingsPage.swift:469-471 a model that offers the chosen heads effort keeps it
    public void Choosing_a_heads_model_that_has_the_chosen_effort_keeps_it() => Editor(new HydraPair(ProviderKind.Claude) { WorkerModel = "alpha", WorkerEffort = "high" }, scene =>
    {
        PickerChoose(scene, RequireEditorControl(scene.View, "Heads", "Model"), Beta.ShortName);
        var pair = scene.Model.Settings.HydraPairs.Single();
        Assert.Equal("beta", pair.WorkerModel);
        Assert.Equal("high", pair.WorkerEffort);
    }, model => SetCatalog(model, ProviderKind.Claude, Alpha, Beta));

    // ---- PR-SETTINGS-173 --------------------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-SETTINGS-173")] // HydraSettingsPage.swift:490-498 "As many as it takes" first (not the page's "No cap"), then 1 to 8
    public void The_at_once_picker_lists_As_many_as_it_takes_then_one_to_eight() => Editor(new HydraPair(ProviderKind.Claude), scene =>
    {
        Assert.Equal(new[] { "As many as it takes", "1", "2", "3", "4", "5", "6", "7", "8" },
            PickerTitles(scene, RequireEditorControl(scene.View, "Heads", "At once")).ToArray());
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-173")] // HydraSettingsPage.swift:493-495 a chosen cap is written to the pair, and choosing the first option writes none
    public void Choosing_a_cap_writes_it_to_the_pair_and_the_first_option_clears_it() => Editor(new HydraPair(ProviderKind.Claude), scene =>
    {
        PickerChoose(scene, RequireEditorControl(scene.View, "Heads", "At once"), "3");
        Assert.Equal(3, scene.Model.Settings.HydraPairs.Single().MaxHeads);
        Assert.Equal("3", Of<TextBlock>(RequireEditorControl(scene.View, "Heads", "At once")).Single().Text);
        PickerChoose(scene, RequireEditorControl(scene.View, "Heads", "At once"), "As many as it takes");
        Assert.Null(scene.Model.Settings.HydraPairs.Single().MaxHeads);
    });

    // ---- PR-SETTINGS-174 --------------------------------------------------------------------------------------------

    private static string[] Notes(Control editor) =>
        EditorChildren(editor).OfType<TextBlock>().Select(note => note.Text!).ToArray();

    [Fact]
    [Trait("Req", "PR-SETTINGS-174")] // HydraSettingsPage.swift:499-507 an empty catalog that is not loading says "No models loaded for <Provider> yet."
    public void An_empty_catalog_says_no_models_are_loaded() => Editor(new HydraPair(ProviderKind.Claude), scene =>
    {
        Assert.Equal(new[] { $"No models loaded for {ProviderKind.Claude.DisplayName} yet." }, Notes(scene.View));
    }, model => SetCatalog(model, ProviderKind.Claude));

    [Fact]
    [Trait("Req", "PR-SETTINGS-174")] // HydraSettingsPage.swift:501 while that provider's catalog is being loaded the line reads "Loading <Provider>'s models…" with a single ellipsis character
    public void A_catalog_being_loaded_says_so_with_one_ellipsis_character() => Editor(new HydraPair(ProviderKind.Claude), scene =>
    {
        Assert.Equal(new[] { $"Loading {ProviderKind.Claude.DisplayName}'s models…" }, Notes(scene.View));
    }, model =>
    {
        SetCatalog(model, ProviderKind.Claude);
        SetLoading(model, ProviderKind.Claude);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-174")] // HydraSettingsPage.swift:499-507,586-588 a same-provider pair shows at most one line, and none once the catalog has models
    public void A_loaded_catalog_draws_no_line() => Editor(new HydraPair(ProviderKind.Claude), scene =>
    {
        Assert.Empty(Notes(scene.View));
    }, model => SetCatalog(model, ProviderKind.Claude, Alpha));

    [Fact]
    [Trait("Req", "PR-SETTINGS-174")] // HydraSettingsPage.swift:586-588 heads elsewhere: the lead's line, then the heads', each only while its own catalog is empty; one loading and one empty read differently
    public void Heads_elsewhere_get_their_own_line_after_the_leads() => Editor(new HydraPair(ProviderKind.Claude) { WorkerProvider = ProviderKind.Codex }, scene =>
    {
        Assert.Equal(new[]
        {
            $"No models loaded for {ProviderKind.Claude.DisplayName} yet.",
            $"Loading {ProviderKind.Codex.DisplayName}'s models…",
        }, Notes(scene.View));
        SetCatalog(scene.Model, ProviderKind.Claude, Alpha);
        Assert.Equal(new[] { $"Loading {ProviderKind.Codex.DisplayName}'s models…" }, Notes(scene.View));
        SetCatalog(scene.Model, ProviderKind.Codex, Pro);
        Assert.Empty(Notes(scene.View));
    }, model =>
    {
        SetInstalled(model, ProviderKind.Claude, ProviderKind.Codex);
        SetCatalog(model, ProviderKind.Claude);
        SetCatalog(model, ProviderKind.Codex);
        SetLoading(model, ProviderKind.Codex);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-174")] // HydraSettingsPage.swift:499-507 a line is 11pt secondary text, inset 14 with 8 above
    public void A_catalog_line_has_the_sources_type_and_insets() => Editor(new HydraPair(ProviderKind.Claude), scene =>
    {
        var note = EditorChildren(scene.View).OfType<TextBlock>().Single();
        Assert.Equal(11, note.FontSize); // .subheadline
        Assert.Equal(new Thickness(14, 8, 14, 0), note.Margin);
        Assert.Equal(Chrome.SecondaryText.Resolve(note.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(note.Foreground).Color);
    }, model => SetCatalog(model, ProviderKind.Claude));
}
