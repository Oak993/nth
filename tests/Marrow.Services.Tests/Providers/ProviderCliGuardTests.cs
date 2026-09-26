using Marrow.Core.Models;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// PORT-ONLY (R-TEST-10; ED-211): proves the provider-CLI root guard is installed for this assembly and fails closed.

public sealed class ProviderCliGuardTests
{
    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Decision", "ED-211")]
    public void The_guard_is_installed_before_any_test_and_excludes_the_real_CLI_folders()
    {
        var roots = ProviderRegistry.ExecutableRootsForTesting;
        Assert.NotNull(roots);
        Assert.NotEmpty(roots);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var realInstallFolders = new[]
        {
            Path.Combine(profile, ".local", "bin"),
            Path.Combine(local, "Programs"),
            Path.Combine(roaming, "npm"),
        };
        foreach (var folder in realInstallFolders)
        {
            var probe = Path.GetFullPath(Path.Combine(folder, "claude.exe"));
            Assert.DoesNotContain(roots, root => probe.StartsWith(Path.GetFullPath(root).TrimEnd('\\', '/') + "\\", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Decision", "ED-211")]
    public void A_provider_binary_outside_the_allowed_roots_resolves_to_nothing()
    {
        using var runtime = new TestRuntime();
        var outside = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "where.exe");
        Assert.True(File.Exists(outside));
        var settings = new FakeProviderSettings();
        settings.Paths[ProviderKind.Claude] = outside;
        var registry = new ProviderRegistry(settings);
        Assert.Null(registry.Executable(ProviderKind.Claude));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Decision", "ED-211")]
    public void A_stand_in_inside_the_scratch_folder_still_resolves()
    {
        using var runtime = new TestRuntime();
        var folder = Path.Combine(Path.GetTempPath(), "marrow-cli-guard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var standIn = Path.Combine(folder, "claude.exe");
            File.WriteAllBytes(standIn, new byte[] { 0x4D, 0x5A });
            var settings = new FakeProviderSettings();
            settings.Paths[ProviderKind.Claude] = standIn;
            var registry = new ProviderRegistry(settings);
            var resolved = registry.Executable(ProviderKind.Claude);
            Assert.NotNull(resolved);
            Assert.Equal(Path.GetFullPath(standIn), Path.GetFullPath(resolved!.Path.Replace('/', '\\').TrimStart('\\')), ignoreCase: true);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
