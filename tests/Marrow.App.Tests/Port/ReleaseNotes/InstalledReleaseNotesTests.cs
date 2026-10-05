using Marrow.App.Port.Updates;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Port.SelfUpdate;
using Marrow.Core.Support;
using Marrow.Testing;

namespace Marrow.App.Tests.Port.ReleaseNotes;

[Trait("Decision", "ED-NEW-ue-6")]
[Trait("Feature", "UE-6")]
public sealed class InstalledReleaseNotesTests
{
    private const string Notes = "## [1.8.0]\n### Bug fixes\n- Keep the downloaded versions notes\n";

    private static AvailableUpdate Offered() => new()
    {
        Version = "1.8.0", Tag = "v1.8.0", Notes = Notes,
        DownloadURL = SwiftURL.Create("https://example.invalid/package")!, PageURL = SwiftURL.Create("https://example.invalid/release")!,
    };

    [Fact]
    [Trait("Spec", "UE-T75")]
    public void A_newer_offer_between_download_and_restart_cannot_replace_the_downloaded_versions_notes()
    {
        using var runtime = new TestRuntime();
        runtime.Defaults.Set("updateChecker_release", AvailableUpdate.EncodeToBytes(Offered()));
        var engine = new FakeSelfUpdateEngine { Offer = new SelfUpdateOffer("1.9.0", "### New features\n- Future notes", 100) };
        var checker = runtime.OnMain(() => new UpdateChecker(ReleaseFeed.Running("1.7.0"), [], MarrowReleaseFeed.Url, engine));
        var applied = new List<string>();
        var item = new MarrowUpdateItem(checker, engine, (_, version) => applied.Add(version));
        runtime.OnMain(() => item.Update(new CollectedProgress(), CancellationToken.None).GetAwaiter().GetResult());
        runtime.OnMain(() => checker.Check().GetAwaiter().GetResult());
        Assert.Equal("1.9.0", checker.Update!.Version);
        item.Restart();
        Assert.Equal(new[] { "1.8.0" }, applied);
        var restarted = runtime.OnMain(() => new UpdateChecker(ReleaseFeed.Running("1.8.0"), [], ""));
        Assert.Equal(Notes, restarted.InstalledRelease?.Notes);
        Assert.Equal("1.8.0", restarted.InstalledRelease?.Version);
        Assert.Equal("1.9.0", restarted.Update?.Version);
    }

    [Fact]
    [Trait("Spec", "UE-T76")]
    public void About_install_retains_its_captured_notes_when_a_later_check_replaces_the_offer_during_download()
    {
        using var runtime = new TestRuntime();
        using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.7.0"));
        using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
        var gate = new TaskCompletionSource();
        var engine = new FakeSelfUpdateEngine { DownloadGate = gate.Task, Offer = new SelfUpdateOffer("1.9.0", "### New features\n- Future notes", 100) };
        runtime.Defaults.Set("updateChecker_release", AvailableUpdate.EncodeToBytes(Offered()));
        var checker = runtime.OnMain(() => new UpdateChecker(AppInfo.Current, [], MarrowReleaseFeed.Url, engine));
        runtime.OnMain(() => new AppUpdater(new FakeUpdateInstallBoundary { VersionToReturn = "1.8.0" }, engine).Install(checker.Update!));
        runtime.OnMain(() => checker.Check().GetAwaiter().GetResult());
        gate.SetResult();
        runtime.Loop.RunUntil(() => engine.Calls.Contains("Apply", StringComparer.Ordinal), TimeSpan.FromSeconds(10));
        var restarted = runtime.OnMain(() => new UpdateChecker(ReleaseFeed.Running("1.8.0"), [], ""));
        Assert.Equal(Notes, restarted.InstalledRelease?.Notes);
        Assert.Equal("1.9.0", restarted.Update?.Version);
    }
}
