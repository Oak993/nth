using System.Reflection;
using Marrow.Core.Support;

namespace Marrow.App.Tests;

// OD-0135: the product is named nth. The name the app shows (the main window title, AppInfo.Name, the Credential Manager
// service of its keys) is read from the app assembly's own CFBundleDisplayName metadata; the fallback for a missing key is
// "nth" too, so no build can show upstream's "Droppy Code" or the old "Marrow".
[Trait("Decision", "OD-0135")]
public sealed class ProductNameTests
{
    private static readonly Assembly App = typeof(Marrow.App.Program).Assembly;

    [Fact]
    public void The_app_assembly_names_the_product_Nth_for_its_build_channel()
    {
#if DEBUG
        const string expected = "nth Dev";
#else
        const string expected = "nth";
#endif
        Assert.Equal(expected, AppInfo.FromAssembly(App, isDevelopment: false).Name);
        Assert.Equal("nth", App.GetCustomAttribute<AssemblyTitleAttribute>()?.Title);
    }

    [Fact]
    public void An_assembly_without_the_metadata_still_reads_Nth()
    {
        var info = AppInfo.FromAssembly(typeof(ProductNameTests).Assembly, isDevelopment: false);
        Assert.Equal("nth", info.Name);
        Assert.DoesNotContain("Droppy", info.Name, StringComparison.Ordinal);
        Assert.DoesNotContain("Marrow", info.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void The_release_and_development_names_are_declared_in_the_one_metadata_source()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(repository.FullName, "Marrow.sln"))) repository = repository.Parent!;
        var source = File.ReadAllText(Path.Combine(repository.FullName, "src", "Marrow.App", "AssemblyInfo.cs"));
        Assert.Contains("[assembly: AssemblyMetadata(\"CFBundleDisplayName\", \"nth Dev\")]", source, StringComparison.Ordinal);
        Assert.Contains("[assembly: AssemblyMetadata(\"CFBundleDisplayName\", \"nth\")]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"CFBundleDisplayName\", \"Marrow", source, StringComparison.Ordinal);
    }
}
