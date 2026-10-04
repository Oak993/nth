using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Updates;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// Unit UE-2 integration review: ProviderRegistry.UpdateCLI's four results with a real (stand-in) updater run. The unit's own tests covered only Skipped and NotInstalled, so the
// ED-NEW-f-ue-2 rule that a failed updater with an unmoved version is Failed (UpdateCLIIfDue ignores the status), the Updated/Unchanged split, the missing 12-hour gate and the
// attempt date were not under test. Like ProviderRegistryCLIUpdateTests, every call runs under the executable seam on the offline stand-in: no real CLI is reached.
public sealed class ProviderRegistryUpdateCliRunTests
{
    private static ProviderRegistry MakeRegistry(TestRuntime runtime)
    {
        var payload = new Dictionary<string, object[]>
        {
            ["deepseek"] = [new { id = "seed-ds", name = "Seed", efforts = Array.Empty<string>() }],
            ["zai"] = [new { id = "seed-zai", name = "Seed", efforts = Array.Empty<string>() }],
            ["commandcode"] = [new { id = "seed-cc", name = "Seed", efforts = Array.Empty<string>() }],
        };
        runtime.Defaults.Set("providerModelCatalogs", System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(payload)));
        return new ProviderRegistry(new FakeProviderSettings());
    }

    private static SwiftURL FileUrl(string windowsPath) => SwiftURL.Create("marrow-native-path:" + windowsPath.Replace('\\', '/'))!;

    private static IDisposable Seam(ProviderProbeScript script) =>
        ProviderRegistry.SetCLIUpdaterExecutableForTesting(p => p == ProviderKind.Claude ? FileUrl(script.ExecutablePath) : null);

    [Fact]
    [Trait("Feature", "UE-2")]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_moved_version_is_updated_with_the_before_and_after_texts_and_the_attempt_date_is_written()
    {
        using var runtime = new TestRuntime();
        var registry = MakeRegistry(runtime);
        var script = ProviderProbeStandIns.CopyFor("ue2-updated").SetStdout("1.0.0\n").SetExitCode(0);
        script.SetPostUpdateStdout("2.0.0\n");
        using var seam = Seam(script);

        var result = await Task.Run(() => runtime.Loop.Run(() => registry.UpdateCLI(ProviderKind.Claude)));

        Assert.Equal(new CliUpdateResult(CliUpdateStatus.Updated, "1.0.0", "2.0.0"), result);
        Assert.Equal("2.0.0", registry.Status(ProviderKind.Claude).Version);
        var dates = (IReadOnlyDictionary<string, object>)runtime.Defaults.Object("providerCLIUpdateDates")!;
        Assert.Equal(SwiftDate.Seconds1970(SwiftDate.Now), Assert.IsType<double>(dates["claude"]));
        Assert.Empty(registry.UpdatingCLIs);
    }

    [Fact]
    [Trait("Feature", "UE-2")]
    [Trait("Rule", "R-TEST-4")]
    public async Task An_updater_that_succeeds_and_leaves_the_version_is_unchanged_and_runs_version_update_version()
    {
        using var runtime = new TestRuntime();
        var registry = MakeRegistry(runtime);
        var script = ProviderProbeStandIns.CopyFor("ue2-unchanged").SetStdout("1.0.0\n").SetExitCode(0);
        using var seam = Seam(script);

        var result = await Task.Run(() => runtime.Loop.Run(() => registry.UpdateCLI(ProviderKind.Claude)));

        Assert.Equal(new CliUpdateResult(CliUpdateStatus.Unchanged, "1.0.0", "1.0.0"), result);
        Assert.Equal([["--version"], ["update"], ["--version"]], script.RecordedCalls());
    }

    [Fact]
    [Trait("Feature", "UE-2")]
    [Trait("Rule", "R-TEST-4")]
    public async Task An_updater_that_exits_with_an_error_and_leaves_the_version_is_failed_but_still_writes_the_attempt_date()
    {
        using var runtime = new TestRuntime();
        var registry = MakeRegistry(runtime);
        var script = ProviderProbeStandIns.CopyFor("ue2-failed-exit").SetStdout("1.0.0\n").SetExitCode(0);
        script.SetUpdateExitCode(1);
        using var seam = Seam(script);

        var result = await Task.Run(() => runtime.Loop.Run(() => registry.UpdateCLI(ProviderKind.Claude)));

        Assert.Equal(CliUpdateStatus.Failed, result.Status);
        var dates = (IReadOnlyDictionary<string, object>)runtime.Defaults.Object("providerCLIUpdateDates")!;
        Assert.Equal(SwiftDate.Seconds1970(SwiftDate.Now), Assert.IsType<double>(dates["claude"]));
    }

    [Fact]
    [Trait("Feature", "UE-2")]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_version_that_cannot_be_read_afterwards_is_failed()
    {
        using var runtime = new TestRuntime();
        var registry = MakeRegistry(runtime);
        var script = ProviderProbeStandIns.CopyFor("ue2-after-fails").SetStdout("1.0.0\n").SetExitCode(0);
        script.SetPostUpdateExitCode(1);
        using var seam = Seam(script);

        var result = await Task.Run(() => runtime.Loop.Run(() => registry.UpdateCLI(ProviderKind.Claude)));

        Assert.Equal(new CliUpdateResult(CliUpdateStatus.Failed, "1.0.0", null), result);
    }

    [Fact]
    [Trait("Feature", "UE-2")]
    [Trait("Rule", "R-TEST-4")]
    public async Task The_twelve_hour_gate_does_not_apply_because_the_user_asked()
    {
        using var runtime = new TestRuntime();
        var registry = MakeRegistry(runtime);
        runtime.Defaults.Set("providerCLIUpdateDates", new Dictionary<string, object> { ["claude"] = SwiftDate.Seconds1970(SwiftDate.Now) - 60.0 });
        var script = ProviderProbeStandIns.CopyFor("ue2-no-gate").SetStdout("1.0.0\n").SetExitCode(0);
        using var seam = Seam(script);

        var result = await Task.Run(() => runtime.Loop.Run(() => registry.UpdateCLI(ProviderKind.Claude)));

        Assert.Equal(CliUpdateStatus.Unchanged, result.Status);
        Assert.Contains(["update"], script.RecordedCalls());
    }
}
