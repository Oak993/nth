using Marrow.App.Port;
using Marrow.App.Port.Updates;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Port.SelfUpdate;
using Marrow.Core.Port.Updates;
using Marrow.Testing;
using Xunit;

namespace Marrow.App.Tests.Port.Updates;

// UE-5 (OD-0100: "Marrow itself (Velopack)" is one of the items of Update everything; BUILD-PLAN.md 2.4 UE-2: "Marrow (through an interface UE-1 implements)"). The item reads the running
// version and the feed's newest through the UpdateChecker, downloads through the self-update engine and applies on "Restart Marrow". The engine and the applier are fakes: nothing is
// downloaded, applied or restarted, and no request leaves the process.
public sealed class MarrowUpdateItemTests
{
    private static readonly SelfUpdateOffer Newer = new("2.0.0", "notes", 63_600_000);

    private static (MarrowUpdateItem Item, List<(string Package, string Version)> Applied) Make(TestRuntime runtime, FakeSelfUpdateEngine engine)
    {
        var checker = runtime.OnMain(() => new UpdateChecker(ReleaseFeed.Running("1.0.0"), [], MarrowReleaseFeed.Url, engine));
        var applied = new List<(string, string)>();
        return (new MarrowUpdateItem(checker, engine, (package, version) => applied.Add((package, version))), applied);
    }

    private static T Run<T>(TestRuntime runtime, Func<Task<T>> body) => runtime.OnMain(() => body().GetAwaiter().GetResult());

    [Fact]
    [Trait("Decision", "ED-308")]
    public void A_run_that_is_not_a_Velopack_install_has_nothing_to_update_so_Marrow_is_not_a_row()
    {
        using var runtime = new TestRuntime();
        var engine = new FakeSelfUpdateEngine { Installed = false, Offer = Newer };
        var (item, _) = Make(runtime, engine);
        Assert.Null(Run(runtime, () => item.InstalledVersion(CancellationToken.None)));
        Assert.DoesNotContain("Check", engine.Calls);
    }

    [Fact]
    [Trait("Decision", "ED-308")]
    public void Marrow_is_an_item_of_its_own_kind_with_its_own_name()
    {
        using var runtime = new TestRuntime();
        var (item, _) = Make(runtime, new FakeSelfUpdateEngine());
        Assert.Equal(("marrow", "Nth", UpdateItemKind.Marrow), (item.Id, item.Title, item.Kind));
    }

    [Fact]
    [Trait("Decision", "ED-308")]
    public void The_installed_version_is_the_running_one_and_the_latest_is_what_the_feed_offers()
    {
        using var runtime = new TestRuntime();
        var (item, _) = Make(runtime, new FakeSelfUpdateEngine { Offer = Newer });
        Assert.Equal("1.0.0", Run(runtime, () => item.InstalledVersion(CancellationToken.None)));
        Assert.Equal("2.0.0", Run(runtime, () => item.LatestVersion(CancellationToken.None)));
    }

    [Fact]
    [Trait("Decision", "ED-308")]
    public void A_feed_with_nothing_newer_names_the_running_version_as_the_latest()
    {
        using var runtime = new TestRuntime();
        var (item, _) = Make(runtime, new FakeSelfUpdateEngine { Offer = null });
        Assert.Equal("1.0.0", Run(runtime, () => item.LatestVersion(CancellationToken.None)));
    }

    [Fact]
    [Trait("Decision", "ED-308")]
    public void A_feed_that_cannot_be_reached_names_no_latest_version()
    {
        using var runtime = new TestRuntime();
        var (item, _) = Make(runtime, new FakeSelfUpdateEngine { CheckThrows = new HttpRequestException("offline") });
        Assert.Null(Run(runtime, () => item.LatestVersion(CancellationToken.None)));
    }

    [Fact]
    [Trait("Decision", "ED-308")]
    public void Updating_downloads_the_offered_version_with_the_percent_as_a_fraction_and_waits_for_the_restart()
    {
        using var runtime = new TestRuntime();
        var engine = new FakeSelfUpdateEngine { Offer = Newer, Percents = [0, 25, 100] };
        var (item, applied) = Make(runtime, engine);
        Run(runtime, () => item.LatestVersion(CancellationToken.None));
        var progress = new CollectedProgress();
        var outcome = Run(runtime, () => item.Update(progress, CancellationToken.None));
        Assert.Equal(new UpdateOutcome(true, "2.0.0", true, true, null), outcome);
        Assert.Equal([0d, 0.25, 1d], progress.Values.Select(v => v!.Value));
        Assert.Contains("Download(2.0.0)", engine.Calls);
        Assert.DoesNotContain("Apply", engine.Calls);
        Assert.Empty(applied);
    }

    [Fact]
    [Trait("Decision", "ED-308")]
    public void Restart_hands_the_downloaded_package_to_the_applier()
    {
        using var runtime = new TestRuntime();
        var engine = new FakeSelfUpdateEngine { Offer = Newer };
        var (item, applied) = Make(runtime, engine);
        Run(runtime, () => item.LatestVersion(CancellationToken.None));
        Run(runtime, () => item.Update(new CollectedProgress(), CancellationToken.None));
        runtime.OnMain(item.Restart);
        Assert.Equal([(engine.PackagePath, "2.0.0")], applied);
    }

    [Fact]
    [Trait("Decision", "ED-308")]
    public void Restart_with_nothing_downloaded_does_nothing()
    {
        using var runtime = new TestRuntime();
        var (item, applied) = Make(runtime, new FakeSelfUpdateEngine { Offer = Newer });
        runtime.OnMain(item.Restart);
        Assert.Empty(applied);
    }

    [Fact]
    [Trait("Decision", "ED-308")]
    public void A_download_that_fails_surfaces_the_reason_to_the_orchestrator()
    {
        using var runtime = new TestRuntime();
        var engine = new FakeSelfUpdateEngine { Offer = Newer, DownloadThrows = new HttpRequestException("The server returned 404.") };
        var (item, _) = Make(runtime, engine);
        Run(runtime, () => item.LatestVersion(CancellationToken.None));
        var error = Assert.ThrowsAny<Exception>(() => Run(runtime, () => item.Update(new CollectedProgress(), CancellationToken.None)));
        Assert.Contains("404", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "ED-308")]
    public void Updating_when_no_newer_version_is_known_is_refused_not_guessed()
    {
        using var runtime = new TestRuntime();
        var engine = new FakeSelfUpdateEngine { Offer = null };
        var (item, _) = Make(runtime, engine);
        Run(runtime, () => item.LatestVersion(CancellationToken.None));
        Assert.ThrowsAny<InvalidOperationException>(() => Run(runtime, () => item.Update(new CollectedProgress(), CancellationToken.None)));
        Assert.DoesNotContain(engine.Calls, c => c.StartsWith("Download", StringComparison.Ordinal));
    }
}
