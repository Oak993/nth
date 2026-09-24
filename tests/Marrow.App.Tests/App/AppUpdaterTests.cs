using Marrow.App;
using Marrow.Core.Support;
using Marrow.Testing;

namespace Marrow.App.Tests.App;

public sealed class AppUpdaterTests
{
    private const string RelaunchVersionKey = "appUpdater_pendingRelaunchVersion";

    private static IDisposable UseRunningVersion(string version) =>
        AppInfo.Running.Override(AppInfo.Read(
            new Dictionary<string, object?> { ["CFBundleShortVersionString"] = version },
            isDevelopment: false));

    [Fact]
    [Trait("Req", "PR-UPDATE-058")] // App/AppUpdater.swift:254-258
    public void A_matching_relaunch_marker_is_removed_and_consumed_once()
    {
        using var runtime = new TestRuntime();
        using var appInfo = UseRunningVersion("1.3.0");
        runtime.Defaults.Set(RelaunchVersionKey, "1.3.0");

        Assert.True(AppUpdater.Shared.ConsumeRelaunchMarker());
        Assert.Null(runtime.Defaults.Object(RelaunchVersionKey) as string);
        Assert.False(AppUpdater.Shared.ConsumeRelaunchMarker());
        Assert.Equal([RelaunchVersionKey, RelaunchVersionKey], runtime.Defaults.Writes);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-058")] // App/AppUpdater.swift:254-258
    public void A_stale_marker_is_removed_before_the_raw_version_mismatch_returns_false()
    {
        using var runtime = new TestRuntime();
        using var appInfo = UseRunningVersion("1.3.0");
        runtime.Defaults.Set(RelaunchVersionKey, "1.3");

        Assert.False(AppUpdater.Shared.ConsumeRelaunchMarker());
        Assert.Null(runtime.Defaults.Object(RelaunchVersionKey) as string);
        Assert.False(AppUpdater.Shared.ConsumeRelaunchMarker());
        Assert.Equal([RelaunchVersionKey, RelaunchVersionKey], runtime.Defaults.Writes);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-058")] // App/AppUpdater.swift:254-256
    public void A_missing_relaunch_marker_returns_false_without_removing_a_key()
    {
        using var runtime = new TestRuntime();

        Assert.False(AppUpdater.Shared.ConsumeRelaunchMarker());

        Assert.Null(runtime.Defaults.Object(RelaunchVersionKey) as string);
        Assert.Empty(runtime.Defaults.Writes);
    }
}
