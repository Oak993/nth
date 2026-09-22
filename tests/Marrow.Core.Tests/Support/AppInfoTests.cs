using System.Reflection;
using System.Reflection.Emit;
using Marrow.Core.Support;

namespace Marrow.Core.Tests.Support;

public sealed class AppInfoTests
{
    [Theory]
    [Trait("Rule", "R-STR-9")] // No Req: pins .droppy-code, which PR-STORE-008 changes to .marrow (OD-0012 F1); rework.
    [InlineData(false, ".droppy-code")]
    [InlineData(true, ".droppy-code-dev")]
    public void Packaged_development_flag_selects_the_exact_hidden_state_directory(bool development, string expected)
    {
        var info = AppInfo.Read(new Dictionary<string, object?>(), development);
        Assert.Equal(development, info.IsDevelopment);
        Assert.Equal(expected, info.StateDirectoryName);
    }

    [Fact]
    [Trait("Rule", "R-STR-9")] // No Req: pins the Droppy Code fallback, which PR-STORE-009 changes to Marrow (OD-0012 F1); rework.
    public void Missing_and_wrongly_typed_metadata_use_the_three_literal_fallbacks()
    {
        foreach (var metadata in new[]
        {
            new Dictionary<string, object?>(),
            new Dictionary<string, object?>
            {
                ["CFBundleDisplayName"] = false,
                ["CFBundleShortVersionString"] = 123,
                ["CFBundleVersion"] = null
            }
        })
        {
            var info = AppInfo.Read(metadata, false);
            Assert.Equal("Droppy Code", info.Name);
            Assert.Equal("1.0.0", info.Version);
            Assert.Equal("1", info.Build);
        }
    }

    [Fact]
    [Trait("Req", "PR-STORE-009")]
    public void Present_strings_including_empty_ones_are_snapshotted_without_normalization()
    {
        var metadata = new Dictionary<string, object?>
        {
            ["CFBundleDisplayName"] = "Marrow",
            ["CFBundleShortVersionString"] = "",
            ["CFBundleVersion"] = " 19 "
        };
        var info = AppInfo.Read(metadata, false);
        metadata["CFBundleDisplayName"] = "changed";
        Assert.Equal("Marrow", info.Name);
        Assert.Equal("", info.Version);
        Assert.Equal(" 19 ", info.Build);
        Assert.Same(AppInfo.Current, AppInfo.Current);
    }

    [Fact] // Windows package metadata comes from the entry assembly.
    [Trait("Req", "PR-STORE-009")]
    public void Assembly_metadata_uses_the_original_three_keys_and_an_explicit_build_channel()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("MetadataFixture"), AssemblyBuilderAccess.Run);
        var constructor = typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)])!;
        foreach (var (key, value) in new[]
        {
            ("CFBundleDisplayName", "Marrow"), ("CFBundleShortVersionString", "1.6.0"), ("CFBundleVersion", "19")
        }) assembly.SetCustomAttribute(new CustomAttributeBuilder(constructor, [key, value]));
        var info = AppInfo.FromAssembly(assembly, true);
        Assert.Equal("Marrow", info.Name);
        Assert.Equal("1.6.0", info.Version);
        Assert.Equal("19", info.Build);
        Assert.True(info.IsDevelopment);
    }
}
