using Avalonia.Controls;
using Marrow.Core.Models;

// Source: DroppyCode/App/Views/Settings/HydraCookbookPanel.swift (B2 1fbf81e), :1-134.
// SKELETON (cycle c100, group 3). Every member with behaviour throws MARROW_STUB until its unit ports it.
// Form (ED-095, R-VIEW-1): a SwiftUI View is a Decorator whose constructor builds `body`. `@Environment(AppModel.self)`
// (:6, :73) is read by the body, not passed in, so it is no constructor parameter; how the port reads an environment
// object is the implementing unit's (NEEDS DECISION in the group report). The Swift-private row type is public, as
// the App views ported so far do with their file-private types (ShortcutsSettingsPage.cs).

namespace Marrow.App.Views.Settings;

/// <summary>:5-68 <c>struct HydraCookbookPanel: View</c>.</summary>
public sealed class HydraCookbookPanel : Decorator
{
    // :8-67 body
    public HydraCookbookPanel() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/HydraCookbookPanel.swift:8");
}

/// <summary>:72-134 <c>private struct HydraCookbookRow: View</c>.</summary>
public sealed class HydraCookbookRow : Decorator
{
    /// <param name="recipe">:74 <c>let recipe: HydraPairRecipe</c>.</param>
    /// <param name="pair">:75 <c>let pair: HydraPair?</c>.</param>
    /// <param name="missing">:76 <c>let missing: ProviderKind?</c>.</param>
    // :78-133 body
    public HydraCookbookRow(HydraPairRecipe recipe, HydraPair? pair, ProviderKind? missing) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Settings/HydraCookbookPanel.swift:78");
}
