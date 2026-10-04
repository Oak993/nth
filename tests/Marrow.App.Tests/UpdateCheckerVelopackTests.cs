using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.SelfUpdate;
using Marrow.Core.Support;
using Marrow.Testing;
using Xunit;

namespace Marrow.App.Tests;

// UE-1 (OD-0062 Velopack; OD-0100, 2026-10-04: Marrow's own updates come from the owner's public GitHub repository, read through Velopack).
// The checker keeps UpdateChecker.swift's shape (check(), the persisted release, the 'last checked' stamp, the failure branch); only the
// source of the answer changes. Every engine here is the fake of FakeSelfUpdateEngine: no test reaches Velopack or GitHub.
public sealed class UpdateCheckerVelopackTests
{
    private static AppInfo Running(string version = "1.0.0", bool isDevelopment = false) => ReleaseFeed.Running(version, isDevelopment);

    private static readonly SelfUpdateOffer Newer = new("2.0.0", "## What is new\n\n- a thing", 63_600_000);

    [Fact]
    [Trait("Decision", "OD-0100")]
    public void An_empty_feed_asks_the_engine_nothing_and_fails_with_the_no_feed_message()
    {
        using var runtime = new TestRuntime();
        var engine = new FakeSelfUpdateEngine { Offer = Newer };
        var checker = runtime.OnMain(() => new UpdateChecker(Running(), [], "", engine));
        Assert.False(runtime.OnMain(() => checker.Check().GetAwaiter().GetResult())); // OD-0012 I1 stays: an empty feed is no feed
        Assert.Equal("No update feed is set.", runtime.OnMain(() => checker.LastError));
        Assert.Empty(engine.Calls);
    }

    [Fact]
    [Trait("Decision", "OD-0100")]
    public void A_run_that_is_not_a_Velopack_install_has_no_feed_to_read_and_says_so()
    {
        using var runtime = new TestRuntime();
        var engine = new FakeSelfUpdateEngine { Installed = false, Offer = Newer };
        var checker = runtime.OnMain(() => new UpdateChecker(Running(), [], MarrowReleaseFeed.Url, engine));
        Assert.False(runtime.OnMain(() => checker.Check().GetAwaiter().GetResult()));
        Assert.Equal("No update feed is set.", runtime.OnMain(() => checker.LastError));
        Assert.NotNull(runtime.OnMain(() => checker.LastCheckedAt));
        Assert.Equal(["IsInstalled"], engine.Calls);                                       // never checked
        Assert.DoesNotContain("updateChecker_release", runtime.Defaults.Writes);
    }

    [Fact]
    [Trait("Decision", "OD-0100")]
    public void A_newer_release_becomes_the_available_update_and_is_persisted()
    {
        using var runtime = new TestRuntime();
        var engine = new FakeSelfUpdateEngine { Offer = Newer };
        var checker = runtime.OnMain(() => new UpdateChecker(Running("1.0.0"), [], MarrowReleaseFeed.Url, engine));
        Assert.True(runtime.OnMain(() => checker.Check().GetAwaiter().GetResult()));
        var update = runtime.OnMain(() => checker.Update)!;
        Assert.Equal("2.0.0", update.Version);
        Assert.Equal("v2.0.0", update.Tag);
        Assert.Equal(63_600_000, update.Size);
        Assert.Equal("## What is new\n\n- a thing", update.Notes);
        Assert.Equal(MarrowReleaseFeed.LatestReleasePage, update.PageURL.AbsoluteString);
        Assert.Equal(MarrowReleaseFeed.LatestReleasePage, update.DownloadURL.AbsoluteString);
        Assert.Null(update.ReleasedAt);
        Assert.True(runtime.OnMain(() => checker.UpdateAvailable));
        Assert.Null(runtime.OnMain(() => checker.LastError));
        Assert.Contains("updateChecker_release", runtime.Defaults.Writes);
        Assert.Equal(["IsInstalled", "Check"], engine.Calls);
    }

    [Fact]
    [Trait("Decision", "OD-0100")]
    public void A_release_without_notes_or_size_reads_as_empty_notes_and_no_size()
    {
        using var runtime = new TestRuntime();
        var engine = new FakeSelfUpdateEngine { Offer = new SelfUpdateOffer("1.0.1", null, null) };
        var checker = runtime.OnMain(() => new UpdateChecker(Running("1.0.0"), [], MarrowReleaseFeed.Url, engine));
        runtime.OnMain(() => checker.Check().GetAwaiter().GetResult());
        var update = runtime.OnMain(() => checker.Update)!;
        Assert.Equal("", update.Notes);
        Assert.Null(update.Size);
    }

    [Fact]
    [Trait("Decision", "OD-0100")]
    public void When_the_feeds_newest_is_the_running_version_nothing_is_offered_and_an_old_offer_is_dropped()
    {
        using var runtime = new TestRuntime();
        var engine = new FakeSelfUpdateEngine { Offer = Newer };
        var checker = runtime.OnMain(() => new UpdateChecker(Running("1.0.0"), [], MarrowReleaseFeed.Url, engine));
        runtime.OnMain(() => checker.Check().GetAwaiter().GetResult());
        Assert.NotNull(runtime.OnMain(() => checker.Update));
        engine.Offer = null;                                                              // the feed has nothing newer than this version now
        Assert.False(runtime.OnMain(() => checker.Check().GetAwaiter().GetResult()));
        Assert.Null(runtime.OnMain(() => checker.Update));
        Assert.Null(runtime.OnMain(() => checker.LastError));
        Assert.NotNull(runtime.OnMain(() => checker.LastCheckedAt));
    }

    [Fact]
    [Trait("Decision", "OD-0100")]
    public void A_failed_read_is_the_cant_reach_GitHub_line_and_no_update_is_kept_or_written()
    {
        using var runtime = new TestRuntime();
        var engine = new FakeSelfUpdateEngine { CheckThrows = new HttpRequestException("connection refused") };
        var checker = runtime.OnMain(() => new UpdateChecker(Running(), [], MarrowReleaseFeed.Url, engine));
        Assert.False(runtime.OnMain(() => checker.Check().GetAwaiter().GetResult()));
        Assert.Equal("Couldn't reach GitHub.", runtime.OnMain(() => checker.LastError)); // ASCII U+0027, UpdateChecker.swift:182, GitLab to GitHub by OD-0100
        Assert.False(runtime.OnMain(() => checker.IsChecking));
        Assert.NotNull(runtime.OnMain(() => checker.LastCheckedAt));
        Assert.DoesNotContain("updateChecker_release", runtime.Defaults.Writes);
    }

    [Fact]
    [Trait("Decision", "OD-0100")]
    public void An_error_the_checker_knows_keeps_its_own_words_with_GitHub_named()
    {
        using var runtime = new TestRuntime();
        var engine = new FakeSelfUpdateEngine { CheckThrows = new UpdateError(new UpdateError.Case.BadResponse(503)) };
        var checker = runtime.OnMain(() => new UpdateChecker(Running(), [], MarrowReleaseFeed.Url, engine));
        runtime.OnMain(() => checker.Check().GetAwaiter().GetResult());
        Assert.Equal("GitHub answered with HTTP 503.", runtime.OnMain(() => checker.LastError));
    }

    [Fact]
    [Trait("Decision", "OD-0100")]
    public void A_development_build_never_checks()
    {
        using var runtime = new TestRuntime();
        var engine = new FakeSelfUpdateEngine { Offer = Newer };
        var checker = runtime.OnMain(() => new UpdateChecker(Running(isDevelopment: true), [], MarrowReleaseFeed.Url, engine));
        Assert.False(runtime.OnMain(() => checker.Check().GetAwaiter().GetResult()));
        Assert.Empty(engine.Calls);
    }

    [Fact]
    [Trait("Decision", "OD-0100")]
    public void The_repository_feed_reads_through_the_shared_engine_when_none_is_given()
    {
        using var runtime = new TestRuntime();
        var engine = new FakeSelfUpdateEngine { Offer = Newer };
        using var shared = SelfUpdateEngine.OverrideShared(engine);
        var checker = runtime.OnMain(() => new UpdateChecker(Running("1.0.0"), [], UpdateChecker.DefaultFeed));
        Assert.True(runtime.OnMain(() => checker.Check().GetAwaiter().GetResult()));
        Assert.Equal(["IsInstalled", "Check"], engine.Calls);
    }

    [Fact]
    [Trait("Decision", "OD-0100")]
    public void A_background_check_that_finds_the_release_notifies_once_for_that_version()
    {
        using var runtime = new TestRuntime();
        var engine = new FakeSelfUpdateEngine { Offer = Newer };
        var checker = runtime.OnMain(() => new UpdateChecker(Running("1.0.0"), [], MarrowReleaseFeed.Url, engine));
        runtime.OnMain(() => checker.Check(background: true).GetAwaiter().GetResult());
        Assert.Equal("2.0.0", runtime.Defaults.Object("updateChecker_notifiedVersion"));   // the marker NotifyOnce writes (UpdateChecker.swift:248)
        var writes = runtime.Defaults.Writes.Count(k => k == "updateChecker_notifiedVersion");
        runtime.OnMain(() => checker.Check(background: true).GetAwaiter().GetResult());
        Assert.Equal(writes, runtime.Defaults.Writes.Count(k => k == "updateChecker_notifiedVersion"));
    }
}
