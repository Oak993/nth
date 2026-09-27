using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Marrow.App.Port;
using Marrow.App.Tests.App.Fixtures;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Settings;

// HydraCookbookPanel.cs used to carry its own private copy of the cookbook resolution logic (an
// internal HydraCookbookResolve class in the Marrow.App.Views.Settings namespace, :266-358 as of
// B3 52bc7926), duplicating the public, tested implementation the Swift source itself calls
// (DroppyCode/App/HydraCookbook+Resolve.swift, ported as the extension(HydraCookbook) members of
// Marrow.App.HydraCookbookResolve in src/Marrow.App/HydraCookbook+Resolve.cs). Line-by-line
// comparison against that Swift file found the two C# copies behaviour-identical for every input:
// same isReady/provider/missingProvider/resolve logic, and the same model(named:) tie-break (an
// equal version keeps the earliest original registry index -- Aggregate's cumulative comparison in
// the duplicate computes the same maximum-under-a-total-order as the shared copy's "replace only on
// a strictly greater version" loop), and the same no-match on an empty name fragment either way
// (SwiftString.LocalizedCaseInsensitiveContains already returns false for an empty needle, so the
// duplicate's implicit reliance on that and the shared copy's explicit guard agree). So this is a
// refactor (down to a single implementation), not a behaviour fix, per the defect brief's own
// carve-out for that case: no case exists where the two copies differ, so there is nothing to pin
// through a behavioural red test. These tests instead pin: (1) structurally, that the panel no
// longer carries a second type named HydraCookbookResolve outside Marrow.App, so the duplicate
// cannot silently return; and (2) observably, that the real production panel's rendered ready and
// waiting rows for every HydraCookbook.Recipes entry match what the single shared
// HydraCookbook.Resolve/MissingProvider implementation computes for the same registry and settings,
// including a tied-version model catalog (the case the duplicate's own header comment called out at
// :19, "equal-version models retain their original registry order").
[Collection("Headless")]
public sealed class HydraCookbookPanelResolverTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-SETTINGS-177")] // App/Views/Settings/HydraCookbookPanel.swift:11-16; App/HydraCookbook+Resolve.swift:1-69
    public void The_panel_carries_no_private_duplicate_of_the_cookbook_resolve_logic()
    {
        var assembly = typeof(HydraCookbookPanel).Assembly;
        var duplicates = assembly.GetTypes()
            .Where(t => t.Name == "HydraCookbookResolve" && t.Namespace != "Marrow.App")
            .ToList();

        Assert.True(
            duplicates.Count == 0,
            "Found a duplicate HydraCookbookResolve outside Marrow.App: " +
            string.Join(", ", duplicates.Select(t => t.FullName)));

        // The single shared implementation itself must still exist (extension members declared
        // for HydraCookbook in Marrow.App.HydraCookbookResolve).
        Assert.NotNull(assembly.GetType("Marrow.App.HydraCookbookResolve"));
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-177")] // App/Views/Settings/HydraCookbookPanel.swift:11-16,39-42; App/HydraCookbook+Resolve.swift:8-45
    public void The_real_panels_ready_and_waiting_rows_match_the_shared_resolver_for_every_recipe() =>
        headless.OnUiThread(() =>
        {
            using var h = new AppModelHarness();
            var model = h.MakeModel();
            h.Main.OnMain(() =>
            {
                // Claude ready with a tied-version catalog (so the model(named:) tie-break -- an
                // equal version keeps the earliest registry index -- is exercised the same way the
                // duplicate's own header comment called out); every other provider stays unset, so
                // recipes whose heads need one of them land in "missing provider" instead -- both
                // branches of the panel are exercised against HydraCookbook.Recipes as shipped.
                HydraCookbookResolveFixture.SetEnabled(model.Settings, ProviderKind.Claude, true);
                HydraCookbookResolveFixture.SetInstalled(model.Providers, ProviderKind.Claude, true);
                HydraCookbookResolveFixture.SetModels(model.Providers, ProviderKind.Claude,
                    HydraCookbookResolveFixture.Model("claude-tied-a", "Claude Sonnet 4.5"),
                    HydraCookbookResolveFixture.Model("claude-tied-b", "Claude Sonnet 4.5"),
                    HydraCookbookResolveFixture.Model("claude-opus", "Claude Opus 5.5"));

                // Expected outcome for every recipe, computed straight from the single shared
                // resolver (Marrow.App.HydraCookbookResolve's extension(HydraCookbook) members).
                var expectedReady = new Dictionary<string, HydraPair>();
                var expectedWaiting = new Dictionary<string, ProviderKind>();
                foreach (var recipe in HydraCookbook.Recipes)
                {
                    var pair = HydraCookbook.Resolve(recipe, model.Providers, model.Settings);
                    if (pair is not null) { expectedReady[recipe.Id] = pair; continue; }
                    var missing = HydraCookbook.MissingProvider(recipe, model.Providers, model.Settings);
                    if (missing is not null) expectedWaiting[recipe.Id] = missing.Value;
                }

                Assert.NotEmpty(expectedReady);
                Assert.NotEmpty(expectedWaiting);

                var host = new Border();
                AppEnvironment.SetAppModel(host, model);
                host.Child = new HydraCookbookPanel();
                var window = new AvaloniaWindow { Content = host };
                window.Show();

                var rows = LogicalQuery.SelfAndDescendants<HydraCookbookRow>(host).ToList();
                Assert.Equal(expectedReady.Count + expectedWaiting.Count, rows.Count);

                foreach (var recipe in HydraCookbook.Recipes)
                {
                    var row = rows.SingleOrDefault(r => RowText(r).Contains(recipe.Tagline, System.StringComparison.Ordinal));
                    Assert.NotNull(row);
                    var text = RowText(row!);

                    if (expectedReady.TryGetValue(recipe.Id, out var pair))
                    {
                        Assert.Equal(1, row!.Opacity);
                        Assert.Contains(HydraPairSummary.Title(pair, model.Providers), text, System.StringComparison.Ordinal);
                        if (HydraPairSummary.Efforts(pair) is { } efforts)
                            Assert.Contains(efforts, text, System.StringComparison.Ordinal);
                        Assert.DoesNotContain("to add this pair.", text, System.StringComparison.Ordinal);
                    }
                    else
                    {
                        Assert.True(expectedWaiting.TryGetValue(recipe.Id, out var provider));
                        Assert.Equal(0.55, row!.Opacity);
                        Assert.Contains($"Set up {provider.DisplayName} to add this pair.", text, System.StringComparison.Ordinal);
                    }
                }
            });
        });

    private static string RowText(Control control)
    {
        var parts = new List<string>();
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
}
