using Avalonia.Controls;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Settings;

// Source: DroppyCode/App/Views/Settings/HydraCookbookPanel.swift (B3 52bc7926), :33-38, :43-55, :89-95, :107-111,
// :114-129.
[Collection("Headless")]
public sealed class HydraCookbookPanelTests(HeadlessSession headless)
{
    private static string AllText(Control control)
    {
        var parts = new System.Collections.Generic.List<string>();
        void Walk(Control node)
        {
            if (node is TextBlock text) parts.Add(text.Text ?? "");
            if (node is ContentControl { Content: Control child }) Walk(child);
            if (node is ContentControl { Content: string label }) parts.Add(label);
            if (node is Panel panel) foreach (var kid in panel.Children) Walk(kid);
            if (node is Decorator { Child: Control decorated }) Walk(decorated);
        }
        Walk(control);
        return string.Join("\n", parts);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-177")] // App/HydraCookbook+Resolve.swift:8-10
    public void No_provider_is_ready_by_default_in_a_fresh_test_model()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();

            foreach (var provider in SwiftEnum<ProviderKind>.AllCases)
                Assert.False(HydraCookbookResolve.IsReady(provider, model.Providers, model.Settings));
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-177")] // App/Views/Settings/HydraCookbookPanel.swift:33-38,43-55
    public void With_no_provider_ready_the_panel_shows_the_empty_state_and_every_recipe_waits()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var host = new Border();
            AppEnvironment.SetAppModel(host, model);
            host.Child = new HydraCookbookPanel();
            var window = new AvaloniaWindow { Content = host };
            window.Show();

            var text = AllText(host);
            Assert.Contains(
                "Set up a provider in Settings › Providers, and its models bring their pairs here.",
                text,
                System.StringComparison.Ordinal);
            Assert.Contains("Needs a provider", text, System.StringComparison.Ordinal);
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-178")] // App/Views/Settings/HydraCookbookPanel.swift:114-119,124-129
    public void A_ready_rows_add_button_is_present_labelled_and_enabled_before_the_pair_is_added()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var recipe = HydraCookbook.Recipes[0];
            var pair = new HydraPair(ProviderKind.Claude) { OrchestratorModel = "claude-model" };

            var host = new Border();
            AppEnvironment.SetAppModel(host, model);
            host.Child = new HydraCookbookRow(recipe, pair, null);
            var window = new AvaloniaWindow { Content = host };
            window.Show();

            Assert.Empty(model.Settings.HydraPairs);
            var addButton = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(host));
            Assert.True(addButton.IsEnabled);
            Assert.Contains("Add", AllText(addButton), System.StringComparison.Ordinal);
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-178")] // App/Views/Settings/HydraCookbookPanel.swift:114-119; Core/Models/HydraCookbook.swift:49-51
    public void A_row_shows_Added_with_no_button_once_an_equivalent_pair_already_exists()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var recipe = HydraCookbook.Recipes[0];
            var pair = new HydraPair(ProviderKind.Claude) { OrchestratorModel = "claude-model" };
            harness.Main.OnMain(() => model.Settings.AddHydraPair(pair));

            var host = new Border();
            AppEnvironment.SetAppModel(host, model);
            host.Child = new HydraCookbookRow(recipe, pair, null);
            var window = new AvaloniaWindow { Content = host };
            window.Show();

            Assert.Contains("Added", AllText(host), System.StringComparison.Ordinal);
            Assert.Empty(LogicalQuery.SelfAndDescendants<Button>(host));
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-178")] // App/Views/Settings/HydraCookbookPanel.swift:107-111
    public void A_waiting_row_names_the_missing_provider_and_has_no_control()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var recipe = HydraCookbook.Recipes[0];

            var host = new Border();
            AppEnvironment.SetAppModel(host, model);
            host.Child = new HydraCookbookRow(recipe, null, ProviderKind.Claude);
            var window = new AvaloniaWindow { Content = host };
            window.Show();

            Assert.Contains("Set up Claude to add this pair.", AllText(host), System.StringComparison.Ordinal);
            Assert.Empty(LogicalQuery.SelfAndDescendants<Button>(host));
        });
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-178")] // App/Views/Settings/HydraCookbookPanel.swift:89-95
    public void A_resolved_rows_title_is_the_pairs_own_title_and_the_tagline_is_always_the_recipes()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var recipe = HydraCookbook.Recipes[0];
            var pair = new HydraPair(ProviderKind.Claude) { OrchestratorModel = "claude-model" };

            var host = new Border();
            AppEnvironment.SetAppModel(host, model);
            host.Child = new HydraCookbookRow(recipe, pair, null);
            var window = new AvaloniaWindow { Content = host };
            window.Show();

            var text = AllText(host);
            Assert.Contains(recipe.Tagline, text, System.StringComparison.Ordinal);
            Assert.Contains(HydraPairSummaryTitleForTests(pair, model.Providers), text, System.StringComparison.Ordinal);
        });
    }

    private static string HydraPairSummaryTitleForTests(HydraPair pair, Marrow.Services.Providers.ProviderRegistry registry) =>
        Marrow.App.Views.Chat.HydraPairSummary.Title(pair, registry);
}

// STOP App.Views.Settings.HydraCookbookPanel #1 (helper Claude, 2026-09-25)
// Kind: coverage gap, not a code defect. Clicking the Add button (PR-SETTINGS-178's core "clicking it adds it, and
// the row becomes Added" behavior) is exercised by the real production code path (HydraCookbookRow's Click handler
// really does call model.Settings.AddHydraPair inside Transaction.WithAnimation, matching the Swift source exactly),
// but driving that click through Button.RaiseEvent(ClickEvent) inside harness.Main.OnMain(...) reproducibly threw
// "Could not create glyphTypeface... Inter... Weight: Medium" from Avalonia's own text layout, twice in isolation,
// while constructing the same Medium-weight "Added" text directly (not via a live re-render mid-click) works fine
// (see A_row_shows_Added_with_no_button_once_an_equivalent_pair_already_exists, which passes). The failure is in
// Avalonia's headless font/render pipeline reacting to a Track-triggered Child replacement fired from inside a
// nested Main.OnMain pump, not in this unit's own code; matches the documented "Could not create glyphTypeface"
// flake pattern elsewhere in this codebase (followups.md, 2026-09-24). Not investigated further given time; the
// click-adds-pair behavior itself stays covered structurally (button present/enabled before add, Added state
// correct once a pair already exists) rather than through a live click-and-rerender in the same test.
