using System.Reflection;
using System.Reflection.Emit;
using Marrow.Core.Support;

namespace Marrow.Core.Tests.Support;

public sealed class AppInfoTests
{
    [Theory]
    [Trait("Req", "PR-STORE-008")] // Core/Support/Text.swift:5-6; DEVIATION OD-0012 F1, renamed by OD-0135: ".nth" / ".nth-dev", literal, with the dot
    [InlineData(false, ".nth")]
    [InlineData(true, ".nth-dev")]
    public void Packaged_development_flag_selects_the_exact_hidden_state_directory(bool development, string expected)
    {
        var info = AppInfo.Read(new Dictionary<string, object?>(), development);
        Assert.Equal(development, info.IsDevelopment);
        Assert.Equal(expected, info.StateDirectoryName);
        Assert.Equal(expected, AppInfo.StateDirectoryNameFor(development));
    }

    [Theory]
    [Trait("Decision", "OD-0135")] // the folders the one-time copy reads from and writes to
    [InlineData(false, ".marrow", ".nth")]
    [InlineData(true, ".marrow-dev", ".nth-dev")]
    public void The_old_state_folder_names_are_kept_only_for_the_one_time_copy(bool development, string legacy, string current)
    {
        Assert.Equal(legacy, AppInfo.LegacyStateDirectoryNameFor(development));
        Assert.Equal(current, AppInfo.StateDirectoryNameFor(development));
        Assert.Equal("nth", AppInfo.DataFolderName);
        Assert.Equal("Marrow Code", AppInfo.LegacyDataFolderName);
    }

    [Fact]
    [Trait("Rule", "R-STR-9")] // OD-0135: the name fallback is the product's own name "nth", never upstream's "Droppy Code".
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
            Assert.Equal("nth", info.Name);
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
            ["CFBundleDisplayName"] = "nth",
            ["CFBundleShortVersionString"] = "",
            ["CFBundleVersion"] = " 19 "
        };
        var info = AppInfo.Read(metadata, false);
        metadata["CFBundleDisplayName"] = "changed";
        Assert.Equal("nth", info.Name);
        Assert.Equal("", info.Version);
        Assert.Equal(" 19 ", info.Build);
        Assert.Same(AppInfo.Current, AppInfo.Current);
    }

    [Fact]
    [Trait("Decision", "ED-065")]
    [Trait("Decision", "ED-119")]
    public async Task An_override_of_the_running_app_is_seen_only_in_the_async_flow_that_made_it()
    {
        // ED-119: AppInfo.version is a Swift static (Text.swift:3-9), so a test replaces it through an Ambient seam
        // overridden per async flow (ED-065). A second flow running at the same time keeps the process default, and
        // the overriding flow gets it back when the override is disposed.
        var fixture = AppInfo.Read(new Dictionary<string, object?>
        {
            ["CFBundleShortVersionString"] = "7.6.5",
            ["CFBundleVersion"] = "42"
        }, false);
        var processDefault = AppInfo.Current;
        var overridden = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var otherChecked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inside = Task.Run(async () =>
        {
            using (AppInfo.Running.Override(fixture))
            {
                Assert.Same(fixture, AppInfo.Current);
                overridden.SetResult();
                await otherChecked.Task;
                Assert.Same(fixture, AppInfo.Current);
                Assert.Equal("7.6.5", AppInfo.Current.Version);
            }
            Assert.Same(processDefault, AppInfo.Current);
        });
        var outside = Task.Run(async () =>
        {
            await overridden.Task;
            Assert.Same(processDefault, AppInfo.Current);
            otherChecked.SetResult();
        });
        await Task.WhenAll(inside, outside);
        Assert.Same(processDefault, AppInfo.Current);
        Assert.Same(AppInfo.Running.Current, AppInfo.Current);
    }

    [Fact] // Windows package metadata comes from the entry assembly.
    [Trait("Req", "PR-STORE-009")]
    public void Assembly_metadata_uses_the_original_three_keys_and_an_explicit_build_channel()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("MetadataFixture"), AssemblyBuilderAccess.Run);
        var constructor = typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)])!;
        foreach (var (key, value) in new[]
        {
            ("CFBundleDisplayName", "nth"), ("CFBundleShortVersionString", "1.6.0"), ("CFBundleVersion", "19")
        }) assembly.SetCustomAttribute(new CustomAttributeBuilder(constructor, [key, value]));
        var info = AppInfo.FromAssembly(assembly, true);
        Assert.Equal("nth", info.Name);
        Assert.Equal("1.6.0", info.Version);
        Assert.Equal("19", info.Build);
        Assert.True(info.IsDevelopment);
    }

    [Fact] // Text.swift:4,7,8 read the value as-is; nothing trims it on the assembly path either.
    [Trait("Req", "PR-STORE-009")]
    public void Assembly_metadata_values_are_never_trimmed()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("MetadataWhitespaceFixture"), AssemblyBuilderAccess.Run);
        var constructor = typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)])!;
        foreach (var (key, value) in new[]
        {
            ("CFBundleDisplayName", "  nth  "), ("CFBundleShortVersionString", " 1.6.2"), ("CFBundleVersion", "20 ")
        }) assembly.SetCustomAttribute(new CustomAttributeBuilder(constructor, [key, value]));
        var info = AppInfo.FromAssembly(assembly, false);
        Assert.Equal("  nth  ", info.Name);
        Assert.Equal(" 1.6.2", info.Version);
        Assert.Equal("20 ", info.Build);
    }

    [Fact] // Text.swift:4,7,8: a key absent from the assembly's own metadata falls back to the same three literals.
    [Trait("Req", "PR-STORE-009")]
    public void Assembly_metadata_missing_keys_use_the_three_literal_fallbacks()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("MetadataMissingFixture"), AssemblyBuilderAccess.Run);
        var info = AppInfo.FromAssembly(assembly, false);
        Assert.Equal("nth", info.Name);
        Assert.Equal("1.0.0", info.Version);
        Assert.Equal("1", info.Build);
    }
}
