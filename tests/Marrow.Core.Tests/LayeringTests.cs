using System.Reflection;

namespace Marrow.Core.Tests;

/// <summary>
/// Enforces the layer rule upstream only stated in AGENTS.md and never enforced (its four "layers"
/// compile into one Swift module). Core depends on nothing in the app and on no UI framework.
/// </summary>
public class LayeringTests
{
    private static readonly string[] Referenced = typeof(Marrow.Core.CoreAssembly).Assembly
        .GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToArray();

    [Fact]
    [Trait("Decision", "ED-064")] // IMPLEMENTING.md 2.1 layer table, adopted under ED-064.
    public void Core_references_no_other_Marrow_layer()
        => Assert.DoesNotContain(Referenced, n => n.StartsWith("Marrow.", StringComparison.Ordinal));

    [Fact]
    [Trait("Decision", "ED-064")] // IMPLEMENTING.md 2.1 layer table, adopted under ED-064.
    public void Core_references_no_UI_framework()
        => Assert.DoesNotContain(Referenced, n => n.StartsWith("Avalonia", StringComparison.Ordinal)
                                               || n.StartsWith("SkiaSharp", StringComparison.Ordinal));
}
