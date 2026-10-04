using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Core.Port.Updates;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// Unit UE-2 (BUILD-PLAN.md 2.4): ProviderRegistry.UpdateCLI, the update run exposed with a result, and UpdatableProviders. WINDOWS-ONLY like the rest of this project's registry tests.
// Like ProviderRegistryCLIUpdateTests, every call runs under a seam that resolves no executable, so no real CLI is ever reached; UpdateCLIIfDue's own tests are unchanged.
public sealed class ProviderRegistryUpdateCliTests
{
    private static string TempDir()
    {
        var root = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        Assert.False(string.IsNullOrEmpty(root), "MARROW_PORT_DATA_DIR must be set before tests run.");
        var dir = Path.Combine(root!, "provider-registry-update-cli", Guid.NewGuid().ToString("N")).Replace('\\', '/');
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    [Trait("Feature", "UE-2")]
    [Trait("Rule", "R-TEST-4")]
    public void The_providers_with_an_updater_are_claude_and_codex()
    {
        Assert.Equal([ProviderKind.Codex, ProviderKind.Claude], ProviderRegistry.UpdatableProviders);
        Assert.Contains(typeof(ICliUpdateRunner), typeof(ProviderRegistry).GetInterfaces());
    }

    [Fact]
    [Trait("Feature", "UE-2")]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_provider_without_an_updater_and_a_custom_binary_path_are_skipped_and_nothing_is_run()
    {
        using var runtime = new TestRuntime();
        var settings = new FakeProviderSettings();
        settings.Paths[ProviderKind.Claude] = TempDir() + "/my-own-claude-build.exe";
        var registry = new ProviderRegistry(settings);
        using var seam = ProviderRegistry.SetCLIUpdaterExecutableForTesting(_ => null);
        var calls = new List<ShellInvocation>();
        using (ShellObserver.Observe(calls.Add))
        {
            Assert.Equal(CliUpdateStatus.Skipped, (await Task.Run(() => runtime.Loop.Run(() => registry.UpdateCLI(ProviderKind.Cursor)))).Status);
            Assert.Equal(CliUpdateStatus.Skipped, (await Task.Run(() => runtime.Loop.Run(() => registry.UpdateCLI(ProviderKind.Claude)))).Status);
        }
        Assert.Empty(calls);
    }

    [Fact]
    [Trait("Feature", "UE-2")]
    [Trait("Rule", "R-TEST-4")]
    public async Task With_no_executable_the_result_is_not_installed_and_no_attempt_date_is_written()
    {
        using var runtime = new TestRuntime();
        var registry = new ProviderRegistry(new FakeProviderSettings());
        using var seam = ProviderRegistry.SetCLIUpdaterExecutableForTesting(_ => null);
        var calls = new List<ShellInvocation>();
        CliUpdateResult result;
        using (ShellObserver.Observe(calls.Add))
            result = await Task.Run(() => runtime.Loop.Run(() => registry.UpdateCLI(ProviderKind.Claude)));
        Assert.Equal(CliUpdateStatus.NotInstalled, result.Status);
        Assert.Empty(calls);
        Assert.Null(runtime.Defaults.Object("providerCLIUpdateDates"));
    }
}
