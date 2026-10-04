using Avalonia.Layout;
using Avalonia.Controls;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;
using Marrow.App.Tests.App.Fixtures;
using Avalonia.Interactivity;
using Avalonia.Media;
using Marrow.App.Views.Chat;
using Marrow.UI.Chrome;

namespace Marrow.App.Tests.Views.Settings;

// Source: DroppyCode/App/Views/Settings/HydraCookbookPanel.swift (B3 52bc7926), :33-38, :43-55, :89-95, :107-111,
// :114-129.
[Collection("Headless")]
public sealed class HydraCookbookPanelTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-SETTINGS-177")] // App/Views/Settings/HydraCookbookPanel.swift:11-16,39-42; App/HydraCookbook+Resolve.swift:49-58
    [Trait("Req", "PR-SETTINGS-178")] // App/Views/Settings/HydraCookbookPanel.swift:114-129
    [Trait("Req", "UI-SETTINGS-037")] // App/Views/Settings/HydraCookbookPanel.swift:40-52,83-95,130-131
    public void Review_real_panel_resolves_the_first_tied_model_and_adds_its_ready_row() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness();
        var model = h.MakeModel();
        Border? host = null;
        AvaloniaWindow? window = null;
        h.Main.OnMain(() =>
        {
            HydraCookbookResolveFixture.SetEnabled(model.Settings, ProviderKind.Claude, true);
            HydraCookbookResolveFixture.SetInstalled(model.Providers, ProviderKind.Claude, true);
            HydraCookbookResolveFixture.SetModels(model.Providers, ProviderKind.Claude,
                HydraCookbookResolveFixture.Model("fable-first", "Fable 5.1 First"),
                HydraCookbookResolveFixture.Model("fable-second", "Fable 5.1 Second"),
                HydraCookbookResolveFixture.Model("opus", "Opus 5.5"));
            var pair = HydraCookbookResolve.Resolve(HydraCookbook.Recipes[0], model.Providers, model.Settings);
            Assert.NotNull(pair); Assert.Equal("fable-first", pair.OrchestratorModel);
            host = new Border(); AppEnvironment.SetAppModel(host, model); host.Child = new HydraCookbookPanel();
            window = new AvaloniaWindow { Content = host }; window.Show();
            var row = Assert.Single(LogicalQuery.SelfAndDescendants<HydraCookbookRow>(host), r => AllText(r).Contains(HydraCookbook.Recipes[0].Tagline, StringComparison.Ordinal));
            Assert.Equal(1, row.Opacity);
            var mark = Assert.Single(LogicalQuery.SelfAndDescendants<HydraPairMark>(row)); Assert.Equal(52, mark.Width);
            var text = LogicalQuery.SelfAndDescendants<TextBlock>(row).First(); Assert.Equal(FontWeight.SemiBold, text.FontWeight);
            // :41, :53 Divider().opacity(0.35).padding(.leading, 16): one-point hairlines that are actually drawn (an
            // Avalonia Separator has no template in this theme-less app and drew nothing).
            window.UpdateLayout();
            var dividers = LogicalQuery.SelfAndDescendants<Border>(host).Where(s => s.Height == 1 && s.Opacity == 0.35).ToList();
            Assert.NotEmpty(dividers); Assert.All(dividers, d => Assert.Equal(16, d.Margin.Left));
            Assert.All(dividers, d => Assert.True(d.Bounds.Height == 1 && d.Bounds.Width > 0 && d.Background is not null, "divider not drawn: " + d.Bounds));
            Assert.Contains(LogicalQuery.SelfAndDescendants<HydraCookbookRow>(host), r => r.Opacity == 0.55);
            var add = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(row));
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains(model.Settings.HydraPairs, p => p.OrchestratorModel == "fable-first");
        });
        h.Main.OnMain(() =>
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Contains("Added", AllText(host!), StringComparison.Ordinal);
            window!.Close(); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        });
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-177")]
    [Trait("Req", "PR-SETTINGS-178")]
    public void An_open_cookbook_offers_and_adds_a_recipe_after_its_catalog_arrives() =>
        headless.OnUiThread(() =>
        {
            using var h = new AppModelHarness();
            using var noCLI =
                Marrow.Services.Providers.ProviderRegistry
                    .SetCLIUpdaterExecutableForTesting(_ => null);
            var model = h.MakeModel();
            var recipe = HydraCookbook.Recipes.Single(
                candidate => candidate.Id == "astra-astra");
            var host = new Border();
            AppEnvironment.SetAppModel(host, model);
            AvaloniaWindow? window = null;

            try
            {
                h.Main.OnMain(() =>
                {
                    HydraCookbookResolveFixture.SetEnabled(
                        model.Settings, ProviderKind.Codex, true);
                    HydraCookbookResolveFixture.SetInstalled(
                        model.Providers, ProviderKind.Codex, true);
                    HydraCookbookResolveFixture.SetModels(
                        model.Providers, ProviderKind.Codex,
                        HydraCookbookResolveFixture.Model(
                            "unrelated", "Unrelated"));

                    host.Child = new HydraCookbookPanel();
                    window = new AvaloniaWindow { Content = host };
                    window.Show();
                });

                // Complete the initial attachment/layout before changing the catalog:
                // a late first render must not masquerade as a catalog notification.
                h.Main.OnMain(() =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    var empty = Assert.Single(
                        LogicalQuery.SelfAndDescendants<TextBlock>(host),
                        text => text.Text ==
                            "Set up a provider in Settings › Providers, and its models bring their pairs here.");
                    Assert.True(empty.IsVisible);
                    Assert.True(empty.Bounds.Width > 0);
                    Assert.True(empty.Bounds.Height > 0);
                    Assert.DoesNotContain(
                        LogicalQuery.SelfAndDescendants<HydraCookbookRow>(host),
                        row => AllText(row).Contains(
                            recipe.Tagline, StringComparison.Ordinal));
                });

                var originalPanel = host.Child;
                var statuses = model.Providers.Statuses;

                // Deliver through the real catalog update API without changing
                // provider readiness, saved pairs, or the open panel.
                h.Main.OnMain(() =>
                    HydraCookbookResolveFixture.SetModels(
                        model.Providers, ProviderKind.Codex,
                        HydraCookbookResolveFixture.Model(
                            "gpt-6-astra", "GPT-6 Astra",
                            ["high", "medium"])));

                h.Main.OnMain(() =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    Assert.Same(originalPanel, host.Child);
                    Assert.Equal(statuses, model.Providers.Statuses);
                    Assert.Empty(model.Settings.HydraPairs);

                    var row = Assert.Single(
                        LogicalQuery.SelfAndDescendants<HydraCookbookRow>(host),
                        candidate => AllText(candidate).Contains(
                            recipe.Tagline, StringComparison.Ordinal));
                    Assert.Equal(1, row.Opacity);
                    var add = Assert.Single(
                        LogicalQuery.SelfAndDescendants<Button>(row));
                    Assert.True(add.IsEnabled);
                    Assert.Contains("Add", AllText(add), StringComparison.Ordinal);
                    add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                });

                h.Main.OnMain(() =>
                {
                    var pair = Assert.Single(model.Settings.HydraPairs);
                    Assert.Equal(ProviderKind.Codex, pair.Provider);
                    Assert.Equal("gpt-6-astra", pair.OrchestratorModel);
                    Assert.Equal("high", pair.OrchestratorEffort);
                    Assert.Equal("gpt-6-astra", pair.WorkerModel);
                    Assert.Equal("medium", pair.WorkerEffort);
                });
            }
            finally
            {
                h.Main.OnMain(() =>
                {
                    window?.Close();
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                });
            }
        });

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

    [Fact]
    [Trait("Req", "UI-SETTINGS-037")] // App/Views/Settings/HydraCookbookPanel.swift:81-129 HStack(spacing: 12) { mark; texts; Spacer(minLength: 8); trailing }: 12 + 8 + 12 points at least before the trailing control, centred, wrapping title
    public void A_ready_row_keeps_thirty_two_points_before_its_trailing_control_and_centres_and_wraps()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var recipe = HydraCookbook.Recipes[0];
            var pair = new HydraPair(ProviderKind.Claude) { OrchestratorModel = "claude-model" };
            var host = new Border();
            AppEnvironment.SetAppModel(host, model);
            var row = new HydraCookbookRow(recipe, pair, null);
            host.Child = row;
            var window = new AvaloniaWindow { Content = host, Width = 300, Height = 400 };
            window.Show();
            var grid = LogicalQuery.SelfAndDescendants<Grid>(row).First();
            var texts = grid.Children.OfType<StackPanel>().Single();
            var trailing = grid.Children.Single(child => Grid.GetColumn(child) == 2);
            Assert.Equal(32, trailing.Bounds.Left - texts.Bounds.Right, 0.5);
            Assert.Equal(VerticalAlignment.Center, trailing.VerticalAlignment);
            var title = texts.Children.OfType<TextBlock>().First();
            Assert.Equal(TextWrapping.Wrap, title.TextWrapping);
            window.Close();
        });
    }
}

// PR-SETTINGS-178 (2026-10-04, OD-0098): the earlier coverage-gap note about driving the Add click is closed. The click is exercised by
// Review_real_panel_resolves_the_first_tied_model_and_adds_its_ready_row and An_open_cookbook_offers_and_adds_a_recipe_after_its_catalog_arrives
// (through the real panel) and by HydraCookbookRowSwiftProofTests.A_resolvable_rows_trailing_control_is_the_Add_button_with_a_plus_and_one_click_turns_the_row_into_Added
// (Chrome.panelSlide's reduce-motion read overridden). The row's look (marks, fonts, colours, third line, waiting sentence, Added label) has no frame in the
// reference videos, so it is proven from HydraCookbookPanel.swift:72-133 by HydraCookbookRowSwiftProofTests.
