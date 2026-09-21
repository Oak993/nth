namespace Marrow.UI.Tests;

/// <summary>UI reaches only into Core: never into Services or App.</summary>
public class LayeringTests
{
    private static readonly string[] Referenced = typeof(Marrow.UI.UIAssembly).Assembly
        .GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToArray();

    [Theory]
    [InlineData("Marrow.Services")]
    [InlineData("Marrow.App")]
    public void UI_does_not_reference(string forbidden) => Assert.DoesNotContain(forbidden, Referenced);
}
