using System.Reflection;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;

namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY test helper (R-TEST-3): puts a registry into a state that only an earlier successful read or delivered
// catalog would otherwise leave behind, by reflection on the two private gates (nothing public sets them). Used only to
// arrange a precondition ("the attempt is already closed", "the limits were just read"); every assertion still goes
// through what a caller can observe.

/// <summary>Arranges the once-per-launch catalog gate and the sixty second limits gate of a registry.</summary>
public static class ProviderRegistryState
{
    /// <summary>Marks the provider's catalog attempt as already made this launch (as a delivered list would).</summary>
    public static void MarkCatalogAttempted(ProviderRegistry registry, ProviderKind provider)
    {
        var field = typeof(ProviderRegistry).GetField("attemptedCatalogs", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((HashSet<ProviderKind>)field.GetValue(registry)!).Add(provider);
    }

    /// <summary>Whether the provider's catalog attempt is closed for this launch (the set has no public reader).</summary>
    public static bool IsCatalogAttempted(ProviderRegistry registry, ProviderKind provider)
    {
        var field = typeof(ProviderRegistry).GetField("attemptedCatalogs", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return ((HashSet<ProviderKind>)field.GetValue(registry)!).Contains(provider);
    }

    /// <summary>Stamps the provider's last successful limits read as "now" on the current clock.</summary>
    public static void StampLimitsRead(ProviderRegistry registry, ProviderKind provider)
    {
        var field = typeof(ProviderRegistry).GetField("limitsFetchedAt", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((Dictionary<ProviderKind, DateTimeOffset>)field.GetValue(registry)!)[provider] = SwiftDate.Now;
    }
}
