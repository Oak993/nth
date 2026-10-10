using Marrow.App.Port.Settings;
using Marrow.App.Port.Updates;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;
using Marrow.Core.Port.Updates;

namespace Marrow.App.Tests.Port.Ignis;

// Ignis U5 (OD-0134, ED-NEW-ignis-settings; design "Settings › Updates"): Ignis's model points as one row of "Update everything". Installed is
// the points in use, Latest the feed's; it runs after the tools and before Marrow; the page and the pill list it right after Marrow. The feed
// is a fake; nothing reaches a network or a model provider.
public sealed class IgnisPointsUpdateRowTests
{
    private static ScriptedUpdateItem MarrowItem() => new("marrow", "Marrow", UpdateItemKind.Marrow, "1.0.0", "1.0.0");

    private static ScriptedUpdateItem Claude() => new("cli:claude", "Claude Code", UpdateItemKind.ProviderCli, "2.1.290", "2.1.293");

    [Fact]
    [Trait("Decision", "ED-NEW-ignissettings-1")] // design row: "Ignis model points", "2026.10.08 → 2026.10.10", Update
    public async Task The_row_reads_installed_and_latest_from_the_points_and_the_feed()
    {
        var points = new FakeSettingsPoints("2026.10.08") { Feed = "2026.10.10" };
        var item = new IgnisPointsUpdateItem(points, () => false);
        Assert.Equal((IgnisPointsUpdateItem.ItemId, IgnisWords.UpdatesRowTitle, UpdateItemKind.ModelPoints), (item.Id, item.Title, item.Kind));
        var orchestrator = new UpdateOrchestrator([item]);
        await orchestrator.CheckNow();
        var row = Assert.Single(orchestrator.Rows);
        Assert.Equal((UpdateRowState.UpdateAvailable, "2026.10.08", "2026.10.10"), (row.State, row.Installed, row.Latest));
        Assert.Equal([false], points.Checks); // "Update automatically" off: a check installs nothing

        await orchestrator.UpdateEverything();
        row = Assert.Single(orchestrator.Rows);
        Assert.Equal((UpdateRowState.Updated, "2026.10.10"), (row.State, row.Installed));
        Assert.False(row.RestartRequired);
        Assert.Equal("2026.10.10", points.Current.Version);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-ignissettings-1")] // "Update automatically" on: the check installs at once and the row says Up to date
    public async Task Updating_automatically_installs_on_the_check()
    {
        var points = new FakeSettingsPoints("2026.10.08") { Feed = "2026.10.10" };
        var orchestrator = new UpdateOrchestrator([new IgnisPointsUpdateItem(points, () => true)]);
        await orchestrator.CheckNow();
        var row = Assert.Single(orchestrator.Rows);
        Assert.Equal((UpdateRowState.UpToDate, "2026.10.10", "2026.10.10"), (row.State, row.Installed, row.Latest));
        Assert.Equal([true], points.Checks);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-ignissettings-1")] // PLAN lead decision: until the feed is published the bundled points are used and the row says Up to date; a feed error is kept
    public async Task An_unpublished_feed_is_up_to_date_and_a_failed_one_checks_when_updated()
    {
        var unpublished = new UpdateOrchestrator([new IgnisPointsUpdateItem(new FakeSettingsPoints("2026.10.08"), () => true)]);
        await unpublished.CheckNow();
        Assert.Equal((UpdateRowState.UpToDate, "2026.10.08", "2026.10.08"), (unpublished.Rows[0].State, unpublished.Rows[0].Installed, unpublished.Rows[0].Latest));

        var failing = new UpdateOrchestrator([new IgnisPointsUpdateItem(new FakeSettingsPoints("2026.10.08") { FeedError = "The feed could not be read." }, () => false)]);
        await failing.CheckNow();
        Assert.Equal((UpdateRowState.ChecksWhenUpdated, "The feed could not be read."), (failing.Rows[0].State, failing.Rows[0].Message));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-ignissettings-1")] // run order: the tools, then the model points, Marrow last
    public async Task The_points_run_after_the_tools_and_before_Marrow()
    {
        var order = new List<string>();
        var marrow = MarrowItem();
        marrow.Latest = "1.1.0";
        marrow.OnUpdate = _ => { order.Add("marrow"); return Task.FromResult(new UpdateOutcome(true, "1.1.0", true, true, null)); };
        var claude = Claude();
        claude.OnUpdate = _ => { order.Add("cli:claude"); return Task.FromResult(new UpdateOutcome(true, "2.1.293", true, false, null)); };
        var points = new FakeSettingsPoints("2026.10.08") { Feed = "2026.10.10" };
        var ignis = new RecordingItem(new IgnisPointsUpdateItem(points, () => false), order);
        var orchestrator = new UpdateOrchestrator([marrow, ignis, claude]);
        Assert.Equal(["cli:claude", IgnisPointsUpdateItem.ItemId, "marrow"], orchestrator.Rows.Select(row => row.Id));
        await orchestrator.UpdateEverything();
        Assert.Equal(["cli:claude", IgnisPointsUpdateItem.ItemId, "marrow"], order);
        Assert.True(Enum.GetValues<UpdateItemKind>().ToList().IndexOf(UpdateItemKind.ModelPoints) == Enum.GetValues<UpdateItemKind>().ToList().IndexOf(UpdateItemKind.Marrow) - 1);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-ignissettings-1")] // the page lists the points right after Marrow, in Marrow's section, with the design's line under the name
    public void The_page_lists_the_points_row_right_after_Marrow()
    {
        IReadOnlyList<UpdateRow> rows =
        [
            new("cli:claude", "Claude Code", UpdateItemKind.ProviderCli, UpdateRowState.UpToDate, "2.1.293", "2.1.293", null, null, false),
            new(IgnisPointsUpdateItem.ItemId, IgnisWords.UpdatesRowTitle, UpdateItemKind.ModelPoints, UpdateRowState.UpdateAvailable, "2026.10.08", "2026.10.10", null, null, false),
            new("marrow", "Marrow", UpdateItemKind.Marrow, UpdateRowState.UpToDate, "1.0.0", "1.0.0", null, null, false),
        ];
        var page = UpdatesProjector.Project(rows, new UpdatesFlags(false, false, false, false, null, null));
        Assert.Equal(["Marrow", "Provider tools"], page.Sections.Select(section => section.Title));
        Assert.Equal(["marrow", IgnisPointsUpdateItem.ItemId], page.Sections[0].Rows.Select(row => row.Id));
        var points = page.Sections[0].Rows[1];
        Assert.Equal((IgnisWords.UpdatesRowTitle, IgnisWords.UpdatesRowDetail, "2026.10.08", "2026.10.10", "Update available", true),
            (points.Title, points.Sub, points.Installed, points.Latest, points.StatusText, points.CanUpdate));
        Assert.Equal(("1 update available", "Ignis model points 2026.10.10."), (page.Header.Title, page.Header.Detail));

        var pill = new UpdatePillProjector().Project(rows, busy: false);
        Assert.Equal(["marrow", IgnisPointsUpdateItem.ItemId, "cli:claude"], pill.Rows.Select(row => row.Id));
        Assert.Equal(IgnisWords.UpdatesRowDetail, pill.Rows[1].Subtitle);
        Assert.True(pill.Rows[1].ShowsLatest);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-ignissettings-1")] // the production inventory appends the points beside the skills rows while Ignis runs, and has none without it
    public void The_inventory_appends_the_points_item_only_while_Ignis_is_bound()
    {
        Assert.Empty(UpdateEverythingProduction.IgnisItems());
        using var store = UserDefaults.Standard.Override(new Marrow.Testing.InMemorySettingsStore());
        var controller = new FakeController(new IgnisSettings(UserDefaults.Standard.Current), new FakeSettingsPoints("2026.10.08"));
        using (IgnisSeams.Controller.Override(() => controller))
        {
            var item = Assert.IsType<IgnisPointsUpdateItem>(Assert.Single(UpdateEverythingProduction.IgnisItems()));
            Assert.Equal(UpdateItemKind.ModelPoints, item.Kind);
        }
        Assert.Empty(UpdateEverythingProduction.IgnisItems());
    }

    private sealed class RecordingItem(IUpdateItem inner, List<string> order) : IUpdateItem
    {
        public string Id => inner.Id;
        public string Title => inner.Title;
        public UpdateItemKind Kind => inner.Kind;
        public Task<string?> InstalledVersion(CancellationToken cancellation) => inner.InstalledVersion(cancellation);
        public Task<string?> LatestVersion(CancellationToken cancellation) => inner.LatestVersion(cancellation);
        public Task<UpdateOutcome> Update(IProgress<double?> progress, CancellationToken cancellation) { order.Add(Id); return inner.Update(progress, cancellation); }
    }

    private sealed class FakeController(IgnisSettings settings, IIgnisPoints points) : IIgnisController
    {
        public event Action<Guid>? Changed { add { } remove { } }
        public IgnisSettings Settings => settings;
        public IIgnisPoints Points => points;
        public ISavingsLedger Ledger { get; } = new FakeSettingsLedger([]);
        public IgnisChatStatus Status(Guid thread) => IgnisChatStatus.Unknown(thread, IgnisMode.WarmAndTidy, true);
        public void SetMode(Guid thread, IgnisMode mode) { }
        public void SetAuto(Guid thread, bool auto) { }
        public void KeepWarmNow(Guid thread) { }
        public void TidyUpNow(Guid thread) { }
        public string Report(Guid thread) => "";
        public void Attach(IIgnisRuntimePort port) { }
        public void Detach(Guid thread) { }
        public void Observe(Guid thread, IgnisTurnUsage usage) { }
        public void TurnFinished(Guid thread) { }
        public void UsageLimitHit(Guid thread, DateTimeOffset? resetsAt) { }
    }
}
