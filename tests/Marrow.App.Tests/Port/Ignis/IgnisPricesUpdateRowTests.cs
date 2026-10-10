using Marrow.App.Port.Updates;
using Marrow.Core.Port.Ignis;
using Marrow.Core.Port.Updates;
using Marrow.Testing;

namespace Marrow.App.Tests.Port.Ignis;

// Ignis price catalogue on the Updates row (OD-0134, ED-NEW-ignis-data): one action updates the curated points and the
// all-model price catalogue; the versions read "2026.10.10 · prices 2026-09-22"; with "Update automatically" on, a check asks
// each at most once a day; an unpublished points feed is still up to date while the catalogue is refreshed; errors keep the
// row's existing behaviour. The feeds are fakes on the manual clock; nothing reaches a network or a model provider.
public sealed class IgnisPricesUpdateRowTests
{
    [Fact]
    [Trait("Decision", "ED-NEW-ignissettings-1")]
    public async Task Update_refreshes_the_points_and_the_prices_and_the_versions_name_the_prices_day()
    {
        using var runtime = new TestRuntime();
        var points = new FakePricedPoints("2026.10.08") { Feed = "2026.10.10" };
        var orchestrator = new UpdateOrchestrator([new IgnisPointsUpdateItem(points, () => false)]);

        await orchestrator.CheckNow();
        var row = Assert.Single(orchestrator.Rows);
        // "Update automatically" off: a check asks the small points feed only, and no catalogue is held yet.
        Assert.Equal((UpdateRowState.UpdateAvailable, "2026.10.08", "2026.10.10"), (row.State, row.Installed, row.Latest));
        Assert.Equal([false], points.Checks);
        Assert.Empty(points.PriceChecks);

        await orchestrator.UpdateEverything();
        row = Assert.Single(orchestrator.Rows);
        Assert.Equal((UpdateRowState.Updated, "2026.10.10 · prices 2026-09-22"), (row.State, row.Installed));
        Assert.Equal([true], points.PriceChecks); // Update always asks (forced); the store's ETag makes an unchanged file one 304
        Assert.Equal([false, true], points.Checks);
        Assert.False(row.RestartRequired);
        Assert.Equal("2026.10.10 · prices 2026-09-22", IgnisWords.UpdatesRowVersion("2026.10.10", "2026-09-22"));
        Assert.Equal("2026.10.10", IgnisWords.UpdatesRowVersion("2026.10.10", null));
        Assert.True(VersionText.Same("2026.10.10 · prices 2026-09-22", "2026.10.10 · prices 2026-09-23"));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-ignissettings-1")]
    public async Task A_prices_only_update_counts_as_a_change()
    {
        using var runtime = new TestRuntime();
        var points = new FakePricedPoints("2026.10.10");
        var outcome = await new IgnisPointsUpdateItem(points, () => false).Update(new Progress<double?>(), CancellationToken.None);
        Assert.Equal((true, "2026.10.10 · prices 2026-09-22", true), (outcome.Succeeded, outcome.NewVersion, outcome.Changed));

        outcome = await new IgnisPointsUpdateItem(points, () => false).Update(new Progress<double?>(), CancellationToken.None);
        Assert.Equal((true, "2026.10.10 · prices 2026-09-22", false), (outcome.Succeeded, outcome.NewVersion, outcome.Changed));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-ignissettings-1")]
    public async Task Updating_automatically_asks_each_feed_at_most_once_a_day()
    {
        using var runtime = new TestRuntime();
        var points = new FakePricedPoints("2026.10.08") { Feed = "2026.10.10" };
        var orchestrator = new UpdateOrchestrator([new IgnisPointsUpdateItem(points, () => true)]);

        await orchestrator.CheckNow();
        var row = Assert.Single(orchestrator.Rows);
        Assert.Equal((UpdateRowState.UpToDate, "2026.10.10 · prices 2026-09-22", "2026.10.10 · prices 2026-09-22"), (row.State, row.Installed, row.Latest));
        Assert.Equal([true], points.Checks);
        Assert.Equal((1, 1), (points.PriceChecks.Count, points.PriceFetches));
        Assert.False(points.PriceChecks[0]);

        runtime.Advance(TimeSpan.FromHours(23));
        await orchestrator.CheckNow();
        Assert.Equal(UpdateRowState.UpToDate, orchestrator.Rows[0].State);
        Assert.Equal([true], points.Checks); // the points feed was asked 23 hours ago
        Assert.Equal(1, points.PriceFetches); // and so was the catalogue (the store skips it)

        runtime.Advance(TimeSpan.FromHours(1));
        await orchestrator.CheckNow();
        row = orchestrator.Rows[0];
        Assert.Equal((UpdateRowState.UpToDate, "2026.10.10 · prices 2026-09-23"), (row.State, row.Installed));
        Assert.Equal([true, true], points.Checks);
        Assert.Equal(2, points.PriceFetches);
        Assert.Equal(TimeSpan.FromDays(1), IgnisPointsUpdateItem.CheckEvery);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-ignissettings-1")]
    public async Task An_unpublished_points_feed_is_up_to_date_and_the_catalogue_still_runs()
    {
        using var runtime = new TestRuntime();
        var points = new FakePricedPoints("2026.10.10");
        var orchestrator = new UpdateOrchestrator([new IgnisPointsUpdateItem(points, () => true)]);
        await orchestrator.CheckNow();
        var row = Assert.Single(orchestrator.Rows);
        Assert.Equal((UpdateRowState.UpToDate, "2026.10.10 · prices 2026-09-22", "2026.10.10 · prices 2026-09-22"), (row.State, row.Installed, row.Latest));
        Assert.Equal(1, points.PriceFetches);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-ignissettings-1")]
    public async Task A_catalogue_error_keeps_the_rows_error_behaviour()
    {
        using var runtime = new TestRuntime();
        var points = new FakePricedPoints("2026.10.10") { PricesError = "The price catalogue was not found." };
        var orchestrator = new UpdateOrchestrator([new IgnisPointsUpdateItem(points, () => true)]);
        await orchestrator.CheckNow();
        var row = Assert.Single(orchestrator.Rows);
        Assert.Equal((UpdateRowState.ChecksWhenUpdated, "2026.10.10", "The price catalogue was not found."), (row.State, row.Installed, row.Message));

        await orchestrator.UpdateEverything();
        row = Assert.Single(orchestrator.Rows);
        Assert.Equal((UpdateRowState.Failed, "The price catalogue was not found."), (row.State, row.Message));

        points.PricesError = null;
        await orchestrator.Retry(IgnisPointsUpdateItem.ItemId);
        row = Assert.Single(orchestrator.Rows);
        Assert.Equal((UpdateRowState.Updated, "2026.10.10 · prices 2026-09-22"), (row.State, row.Installed));

        // A points feed error still wins the message, as before.
        var failing = new FakePricedPoints("2026.10.10") { FeedError = "The feed could not be read.", PricesError = "The price catalogue was not found." };
        var other = new UpdateOrchestrator([new IgnisPointsUpdateItem(failing, () => false)]);
        await other.CheckNow();
        Assert.Equal((UpdateRowState.ChecksWhenUpdated, "The feed could not be read."), (other.Rows[0].State, other.Rows[0].Message));
        Assert.Empty(failing.PriceChecks);
    }
}
