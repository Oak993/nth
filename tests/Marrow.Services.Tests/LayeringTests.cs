namespace Marrow.Services.Tests;

/// <summary>Services reaches only into Core: never into UI or App, and never into a UI framework.</summary>
public class LayeringTests
{
    private static readonly string[] Referenced = typeof(Marrow.Services.ServicesAssembly).Assembly
        .GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToArray();

    [Theory]
    [InlineData("Marrow.UI")]
    [InlineData("Marrow.App")]
    public void Services_does_not_reference(string forbidden) => Assert.DoesNotContain(forbidden, Referenced);

    [Fact]
    public void Services_references_no_UI_framework()
        => Assert.DoesNotContain(Referenced, n => n.StartsWith("Avalonia", StringComparison.Ordinal));
}
