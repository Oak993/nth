using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Port;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

// Source: DroppyCode/App/Views/Settings/HydraCookbookPanel.swift (B3 52bc792), :1-134.
// The resolve logic itself (isReady/provider(for:)/missingProvider/resolve/model(named:)/version(of:)) is
// DroppyCode/App/HydraCookbook+Resolve.swift:1-69, not yet its own unit; ported here as a private static class
// since nothing else in the tree calls it yet (R-FILE-1 would give it its own twin once another caller exists).
// B3 (AM-0003): both Swift files are byte-identical to B2 (last touched 5c63f538a/2afcd6c37, long before
// 52bc7926; line counts unchanged at 134 and 69) -- this file needs no code change for B3. The recipe data
// this panel renders (Core/Models/HydraCookbook.swift) DID change at B3 (five titles renamed to "Opus 5.5"
// and two recipes added, astra-sol and sol-luna, :29-42) but that file is Core.Models.HydraCookbook, a
// separate, unassigned unit; this panel iterates HydraCookbook.Recipes rather than hardcoding a count or
// title, so its own body is unaffected by that change (PR-SETTINGS-177 b3_note).

namespace Marrow.App.Views.Settings;

/// <summary>:5-68 <c>struct HydraCookbookPanel: View</c>: the pairs cookbook, resolved for the providers set up
/// here, added with one click.</summary>
public sealed class HydraCookbookPanel : Decorator
{
    // :8-67 body
    public HydraCookbookPanel()
    {
        var header = new StackPanel { Spacing = 3, HorizontalAlignment = HorizontalAlignment.Stretch };
        var title = new TextBlock { Text = "Cookbook", Foreground = Chrome.PrimaryText.Brush(header) }; // :19-21
        SwiftFont.Title3.FontWeight(SwiftFont.Weight.Semibold).Apply(title);
        header.Children.Add(title);
        var subtitle = new TextBlock // :22-25
        {
            Text = "Pairs built from the models in your library: which one leads, at what effort, and which ones build. Add one and it is in the model picker.",
            Foreground = Chrome.SecondaryText.Brush(header),
            TextWrapping = TextWrapping.Wrap,
        };
        SwiftFont.Subheadline.Apply(subtitle);
        header.Children.Add(subtitle);

        var listContent = new StackPanel { Spacing = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var scroll = new ScrollViewer { Content = listContent, HorizontalAlignment = HorizontalAlignment.Stretch };

        var root = new StackPanel { Spacing = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        root.Children.Add(new Border { Child = header, Padding = new Avalonia.Thickness(16, 16, 16, 12) });
        root.Children.Add(new Separator { Opacity = 0.5 }); // :30
        root.Children.Add(scroll);

        Child = root;
        Width = 460;
        Height = 540; // :60

        Marrow.Services.Providers.ProviderRegistry? registry = null;
        Marrow.App.AppSettings? settings = null;

        void Rebuild()
        {
            if (registry is null || settings is null) return;
            listContent.Children.Clear();

            var ready = HydraCookbookResolve.RecipesWithPair(registry, settings); // :11-13
            var waiting = HydraCookbookResolve.RecipesMissingProvider(registry, settings); // :14-16

            if (ready.Count == 0) // :33-38
            {
                var empty = new TextBlock
                {
                    Text = "Set up a provider in Settings › Providers, and its models bring their pairs here.", // U+203A SINGLE RIGHT-POINTING ANGLE QUOTATION MARK
                    Foreground = Chrome.SecondaryText.Brush(listContent),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Avalonia.Thickness(16),
                };
                SwiftFont.Callout.Apply(empty);
                listContent.Children.Add(empty);
            }

            foreach (var (recipe, pair) in ready) // :39-42
            {
                listContent.Children.Add(new HydraCookbookRow(recipe, pair, null));
                listContent.Children.Add(new Separator { Opacity = 0.35, Margin = new Avalonia.Thickness(16, 0, 0, 0) });
            }

            if (waiting.Count > 0) // :43-55
            {
                var needsProvider = new TextBlock
                {
                    Text = "Needs a provider",
                    Foreground = Chrome.SecondaryText.Brush(listContent),
                    Margin = new Avalonia.Thickness(16, 14, 16, 4),
                };
                SwiftFont.Subheadline.FontWeight(SwiftFont.Weight.Semibold).Apply(needsProvider);
                listContent.Children.Add(needsProvider);

                foreach (var (recipe, provider) in waiting)
                {
                    var row = new HydraCookbookRow(recipe, null, provider) { Opacity = 0.55 };
                    listContent.Children.Add(row);
                    listContent.Children.Add(new Separator { Opacity = 0.35, Margin = new Avalonia.Thickness(16, 0, 0, 0) });
                }
            }

            listContent.Margin = new Avalonia.Thickness(0, 0, 0, 8); // :57 .padding(.bottom, 8)
        }

        // :6-7 @Environment(AppModel.self) private var model: deferred to attach, so an ancestor has had the
        // chance to call AppEnvironment.SetAppModel before this reads it (matching how Track already defers).
        this.Track(() => AppEnvironment.GetAppModel(this), model =>
        {
            registry = model.Providers;
            settings = model.Settings;
            Rebuild();
        });
        this.Track(() => registry?.Statuses, _ => Rebuild());
        this.Track(() => settings?.HydraPairs, _ => Rebuild());
        this.Task(() => 0, async _ => // :61-65
        {
            if (registry is null || settings is null) return;
            foreach (var provider in SwiftEnum<ProviderKind>.AllCases)
            {
                if (HydraCookbookResolve.IsReady(provider, registry, settings))
                    await registry.LoadCatalog(provider);
            }
        });
    }
}

/// <summary>:72-134 <c>private struct HydraCookbookRow: View</c>: one recipe, its resolved pair or the provider it
/// still needs, and the button that adds it.</summary>
public sealed class HydraCookbookRow : Decorator
{
    /// <param name="recipe">:74 <c>let recipe: HydraPairRecipe</c>.</param>
    /// <param name="pair">:75 <c>let pair: HydraPair?</c>.</param>
    /// <param name="missing">:76 <c>let missing: ProviderKind?</c>.</param>
    // :78-133 body
    public HydraCookbookRow(HydraPairRecipe recipe, HydraPair? pair, ProviderKind? missing)
    {
        Padding = new Avalonia.Thickness(16, 10); // :131-132

        // :73 @Environment(AppModel.self) private var model: deferred to attach (see HydraCookbookPanel's own
        // Track for why), then re-run whenever settings.hydraPairs changes so isAdded (:80) stays current.
        this.Track(
            () =>
            {
                var model = AppEnvironment.GetAppModel(this);
                return (model, model.Settings.HydraPairs);
            },
            state => Rebuild(state.model));

        void Rebuild(Marrow.App.AppModel model)
        {
        var registry = model.Providers;
        var isAdded = pair is { } addedPair && HydraCookbook.IsAdded(addedPair, model.Settings.HydraPairs); // :80

        var row = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(Avalonia.Controls.GridLength.Auto), new ColumnDefinition(Avalonia.Controls.GridLength.Star), new ColumnDefinition(Avalonia.Controls.GridLength.Auto) },
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var mark = new HydraPairMark( // :83-87
            pair?.Provider ?? recipe.Lead.Providers.FirstOrDefault(ProviderKind.Claude),
            pair?.HeadsProvider ?? recipe.Heads.Providers.FirstOrDefault(ProviderKind.Claude))
        {
            Width = 52,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        Grid.SetColumn(mark, 0);
        row.Children.Add(mark);

        var texts = new StackPanel { Spacing = 2, Margin = new Avalonia.Thickness(12, 0, 8, 0) };
        var rowTitle = new TextBlock // :89-91
        {
            Text = pair is { } titlePair ? HydraPairSummary.Title(titlePair, registry) : recipe.Title,
            Foreground = Chrome.PrimaryText.Brush(texts),
        };
        SwiftFont.Body.FontWeight(SwiftFont.Weight.Semibold).Apply(rowTitle);
        texts.Children.Add(rowTitle);

        var tagline = new TextBlock // :92-95
        {
            Text = recipe.Tagline,
            Foreground = Chrome.SecondaryText.Brush(texts),
            TextWrapping = TextWrapping.Wrap,
        };
        SwiftFont.Subheadline.Apply(tagline);
        texts.Children.Add(tagline);

        if (pair is { } detailPair) // :96-106
        {
            string? providersLine = detailPair.HeadsProvider != detailPair.Provider
                ? detailPair.Provider.DisplayName + " lead, " + detailPair.HeadsProvider.DisplayName + " heads"
                : null;
            var parts = new List<string>();
            if (providersLine is { } line) parts.Add(line);
            if (HydraPairSummary.Efforts(detailPair) is { } efforts) parts.Add(efforts);
            var detail = string.Join(" · ", parts); // U+00B7 MIDDLE DOT
            if (detail.Length > 0)
            {
                var detailText = new TextBlock
                {
                    Text = detail,
                    Foreground = Chrome.SecondaryText.Opacity(0.8).Brush(texts),
                    MaxLines = 2,
                    TextWrapping = TextWrapping.Wrap,
                };
                SwiftFont.Subheadline.Apply(detailText);
                texts.Children.Add(detailText);
            }
        }
        else if (missing is { } missingProvider) // :107-111
        {
            var missingText = new TextBlock
            {
                Text = "Set up " + missingProvider.DisplayName + " to add this pair.",
                Foreground = Chrome.SecondaryText.Opacity(0.8).Brush(texts),
            };
            SwiftFont.Subheadline.Apply(missingText);
            texts.Children.Add(missingText);
        }
        Grid.SetColumn(texts, 1);
        row.Children.Add(texts);

        if (pair is { } actionPair) // :114-129
        {
            Control trailing;
            if (isAdded)
            {
                var added = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Avalonia.Thickness(10, 0, 10, 0) };
                var check = SwiftImage.SystemName("checkmark").Font(SwiftFont.Callout.FontWeight(SwiftFont.Weight.Medium));
                check.ForegroundStyle(Chrome.SecondaryText);
                added.Children.Add(check);
                var addedText = new TextBlock { Text = "Added", Foreground = Chrome.SecondaryText.Brush(added) };
                SwiftFont.Callout.FontWeight(SwiftFont.Weight.Medium).Apply(addedText);
                added.Children.Add(addedText);
                trailing = added;
            }
            else
            {
                var addContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                addContent.Children.Add(SwiftImage.SystemName("plus"));
                addContent.Children.Add(new TextBlock { Text = "Add" });
                var addButton = new Button { Content = addContent }; // MARROW_DEFERRED ED-180: .buttonStyle(.glassProminent).controlSize(.small)
                addButton.Click += (_, _) =>
                    Transaction.WithAnimation(Chrome.PanelSlide, () => model.Settings.AddHydraPair(actionPair)); // :121-123
                trailing = addButton;
            }
            Grid.SetColumn(trailing, 2);
            row.Children.Add(trailing);
        }

        Child = row;
        }
    }
}

// DroppyCode/App/HydraCookbook+Resolve.swift:1-69. Compatibility helpers used by this panel;
// equal-version models retain their original registry order (:55-58).
internal static class HydraCookbookResolve
{
    // :8-10 static func isReady(_ provider:registry:settings:) -> Bool
    public static bool IsReady(ProviderKind provider, ProviderRegistry registry, AppSettings settings) =>
        settings.IsEnabled(provider) && registry.Status(provider).IsInstalled;

    // :14-19 static func provider(for side:registry:settings:) -> ProviderKind?
    private static ProviderKind? Provider(HydraPairRecipe.Side side, ProviderRegistry registry, AppSettings settings)
    {
        foreach (var provider in side.Providers)
        {
            if (IsReady(provider, registry, settings) &&
                (side.Models.Count == 0 || Model(side.Models, provider, registry) is not null))
                return provider;
        }
        return null;
    }

    // :23-28 static func missingProvider(for recipe:registry:settings:) -> ProviderKind?
    public static ProviderKind? MissingProvider(HydraPairRecipe recipe, ProviderRegistry registry, AppSettings settings)
    {
        foreach (var side in new[] { recipe.Lead, recipe.Heads })
        {
            if (!side.Providers.Any(provider => IsReady(provider, registry, settings)))
                return side.Providers.Count > 0 ? side.Providers[0] : null;
        }
        return null;
    }

    // :32-45 static func resolve(_:registry:settings:) -> HydraPair?
    public static HydraPair? Resolve(HydraPairRecipe recipe, ProviderRegistry registry, AppSettings settings)
    {
        if (Provider(recipe.Lead, registry, settings) is not { } leadProvider ||
            Provider(recipe.Heads, registry, settings) is not { } headsProvider)
            return null;

        var lead = Model(recipe.Lead.Models, leadProvider, registry);
        var heads = Model(recipe.Heads.Models, headsProvider, registry);
        return new HydraPair(leadProvider)
        {
            OrchestratorModel = lead?.Id,
            OrchestratorEffort = HydraCookbook.Effort(recipe.Lead.Effort, lead ?? registry.DefaultModel(leadProvider)),
            WorkerProvider = headsProvider != leadProvider ? headsProvider : null,
            WorkerModel = heads?.Id,
            WorkerEffort = HydraCookbook.Effort(recipe.Heads.Effort, heads ?? registry.DefaultModel(headsProvider)),
            MaxHeads = recipe.MaxHeads,
        };
    }

    // :49-62 static func model(named:for:registry:) -> ModelOption?
    private static ModelOption? Model(IReadOnlyList<string> names, ProviderKind provider, ProviderRegistry registry)
    {
        var options = registry.Models(provider);
        foreach (var name in names)
        {
            var matches = options.Where(option =>
                SwiftString.LocalizedCaseInsensitiveContains(option.Name, name) ||
                SwiftString.LocalizedCaseInsensitiveContains(option.Id, name)).ToList();
            if (matches.Count == 0) continue;
            return matches.Aggregate((lhs, rhs) =>
            {
                var lhsVersion = Version(lhs.Name) ?? -1;
                var rhsVersion = Version(rhs.Name) ?? -1;
                if (lhsVersion != rhsVersion) return lhsVersion > rhsVersion ? lhs : rhs;
                var lhsIndex = options.ToList().FindIndex(option => option.Id == lhs.Id);
                var rhsIndex = options.ToList().FindIndex(option => option.Id == rhs.Id);
                return lhsIndex < rhsIndex ? lhs : rhs;
            });
        }
        return null;
    }

    // :65-68 private static func version(of name:) -> Double?
    private static double? Version(string name)
    {
        var match = System.Text.RegularExpressions.Regex.Match(name, @"[0-9]+(?:\.[0-9]+)?");
        return match.Success ? double.Parse(match.Value, System.Globalization.CultureInfo.InvariantCulture) : null;
    }

    // Used by HydraCookbookPanel to build the ready/waiting lists (:11-16 the two compactMap lets in body).
    public static IReadOnlyList<(HydraPairRecipe Recipe, HydraPair Pair)> RecipesWithPair(ProviderRegistry registry, AppSettings settings) =>
        HydraCookbook.Recipes
            .Select(recipe => (recipe, pair: Resolve(recipe, registry, settings)))
            .Where(entry => entry.pair is not null)
            .Select(entry => (entry.recipe, entry.pair!))
            .ToList();

    public static IReadOnlyList<(HydraPairRecipe Recipe, ProviderKind Provider)> RecipesMissingProvider(ProviderRegistry registry, AppSettings settings) =>
        HydraCookbook.Recipes
            .Select(recipe => (recipe, provider: MissingProvider(recipe, registry, settings)))
            .Where(entry => entry.provider is not null)
            .Select(entry => (entry.recipe, entry.provider!.Value))
            .ToList();
}
