using Marrow.App.Port.Ignis.Runtime;
using Marrow.App.Port.Ignis.Settings;
using Marrow.App.Port.Ignis.Views;
using Marrow.App.Port.Updates;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;
using Marrow.Core.Port.Ignis.Engine;
using Marrow.Services.Port.Ignis;
using Marrow.Services.Providers;
using Marrow.Testing;

namespace Marrow.App.Tests.Port.Ignis;

// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-runtime; lead integration of units U1-U5). End to end with the real parts: the
// real ThreadRuntime over the offline scripted session (no provider is ever reached), the real controller, the U1 engine,
// the bundled points store and the savings ledger in a temporary folder, the U4 timeline filter and the U5 history model,
// on the harness's manual clock. The points feed is a loopback server behind a fail-closed wire.
public sealed class IgnisIntegrationTests : IDisposable
{
    private const string Opus = "claude-opus-5-5";

    private readonly string data = Directory.CreateTempSubdirectory("ignis-int-").FullName;
    private readonly IgnisSavingsLedger ledger;
    private readonly IgnisPointsStore points;
    private readonly IgnisSettings settings = new(new InMemorySettingsStore());
    private readonly FakeIgnisEnvironment environment = new();
    private readonly IgnisController controller;

    public IgnisIntegrationTests()
    {
        ledger = new IgnisSavingsLedger(data);
        points = new IgnisPointsStore(data, LoopbackHttpServer.RefusedUrl("/ignis/model-points.json"));
        controller = new IgnisController(settings, points, ledger, IgnisEngine.Shared, environment);
    }

    public void Dispose()
    {
        try { Directory.Delete(data, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static ThreadRuntimeTurnRig Rig() =>
        new(ProviderKind.Claude, configure: thread => thread with { Model = Opus }, settings: app => app.Settings.ChimeWhenFinished = false);

    private static IgnisTurnUsage Usage(long read, long write1h = 0, long context = 100_000, long uncached = 100) =>
        new(ProviderKind.Claude, Opus, null, uncached, read, 0, write1h, 0, 400, context, 1_000_000, true);

    private static void WaitForSends(ThreadRuntimeTurnRig rig, int count) =>
        rig.H.Main.Loop.RunUntil(() => rig.Session.Inputs.Count >= count, AppModelHarness.Bound);

    /// <summary>One user turn that reports its cache use, then the chat is read: between tasks.</summary>
    private static void Turn(ThreadRuntimeTurnRig rig, string text, IgnisTurnUsage usage, int sends)
    {
        _ = rig.StartTurn(text);
        WaitForSends(rig, sends);
        rig.H.Main.OnMain(() =>
        {
            rig.Session.Emit(new ProviderEvent.MessageCompleted("reply-" + sends, "Done."));
            rig.Session.Emit(new ProviderEvent.CacheUsage(usage));
        });
        rig.Complete();
        rig.H.Main.OnMain(() => rig.App.UpdateThread(rig.InitialThread.Id, thread => thread with { HasUnread = false }));
    }

    private static TimelineEntry[] Entries(ThreadRuntimeTurnRig rig) => rig.H.Main.OnMain(() => rig.Runtime.Entries.ToArray());

    private static TimelineEntry[] Visible(ThreadRuntimeTurnRig rig) => rig.H.Main.OnMain(() => IgnisViews.Visible(rig.Runtime.Entries.ToArray()).ToArray());

    [Fact]
    [Trait("Decision", "OD-0134")]
    public void A_Claude_plan_chats_cache_reads_are_saved_in_the_ledger_and_shown_in_the_settings_history()
    {
        using var views = IgnisViews.Override(controller);
        using var rig = Rig();
        var usage = Usage(read: 40_000, write1h: 2_000);
        var expected = IgnisMoney.ReuseSaving(points.Find(ProviderKind.Claude, Opus)!, usage);
        Assert.True(expected > 0m);

        Turn(rig, "Fix the parser", usage, 1);

        var thread = rig.InitialThread.Id;
        var today = ledger.LocalDay(rig.H.Main.OnMain(() => SwiftDate.Now));
        Assert.Equal(expected, ledger.AllTimeSaved);
        Assert.Equal(expected, ledger.ChatDay(thread, today).Reuse);
        var status = rig.H.Main.OnMain(() => controller.Status(thread));
        Assert.Equal(IgnisAccountKind.Plan, status.Account);
        Assert.Equal(expected, status.SavedToday);

        var history = IgnisSavingsHistory.From(controller.Ledger, today);
        Assert.Equal(expected, history.AllTime);
        Assert.Equal(today, history.Since);
        var month = history.Months.Single(m => m.IsCurrent);
        Assert.Equal(expected, month.BySource[IgnisSource.Reuse]);
        var row = Assert.Single(month.DayRows(today));
        Assert.Equal(today, row.Day);
        Assert.Contains(row.Did, did => did.Source == IgnisSource.Reuse);

        Assert.Equal(expected, new IgnisSavingsLedger(data).AllTimeSaved); // on disk, read back after a restart
    }

    [Fact]
    [Trait("Decision", "OD-0134")]
    public void Keep_warm_sends_a_hidden_ping_between_tasks_and_nothing_of_its_turn_shows_in_the_timeline()
    {
        environment.Draft = true;
        using var views = IgnisViews.Override(controller);
        using var rig = Rig();
        Turn(rig, "Fix the parser", Usage(read: 40_000, write1h: 2_000), 1);
        var visibleBefore = Visible(rig).Length;
        Assert.Equal(TimeSpan.FromHours(1), rig.H.Main.OnMain(() => controller.Status(rig.InitialThread.Id)).Ttl);

        rig.H.Main.Advance(TimeSpan.FromMinutes(40));
        Assert.Single(rig.Session.Inputs); // not due yet
        rig.H.Main.Advance(TimeSpan.FromMinutes(18));
        WaitForSends(rig, 2);
        Assert.Equal(IgnisWords.PingText, rig.Session.Inputs[1].Text);

        var ping = rig.H.Main.OnMain(() => rig.Runtime.Turns[^1]);
        Assert.True(IgnisEvent.IsPingItem(ping.UserItemID));
        rig.H.Main.OnMain(() =>
        {
            rig.Session.Emit(new ProviderEvent.MessageCompleted("reply-ping", "ok"));
            rig.Session.Emit(new ProviderEvent.CacheUsage(Usage(read: 100_000, context: 100_100, uncached: 60)));
        });
        rig.Complete();

        Assert.False(rig.Thread.HasUnread);
        var all = Entries(rig);
        Assert.Contains(all, entry => entry.Item.TurnID == ping.Id && entry.Item.TimelineItemContent is TimelineItem.Content.Assistant);
        var visible = Visible(rig);
        Assert.DoesNotContain(visible, entry => IgnisEvent.IsPingItem(entry.Item.Id));
        Assert.DoesNotContain(visible, entry => entry.Item.TurnID == ping.Id && !IgnisEvent.IsEventItem(entry.Item.Id));
        Assert.Equal(visibleBefore, visible.Count(entry => !IgnisEvent.IsEventItem(entry.Item.Id)));

        rig.H.Main.OnMain(() => settings.Update(value => value with { ShowPings = true }));
        Assert.Contains(Visible(rig), entry => entry.Id == ping.UserItemID);
    }

    [Fact]
    [Trait("Decision", "OD-0134")]
    public void At_the_tidy_point_the_chat_is_tidied_through_the_sessions_tidy_once_it_is_between_tasks()
    {
        var tidies = new List<int>();
        using var tidy = IgnisSessionHooks.Tidy.Override((_, keep) => { tidies.Add(keep); return Task.CompletedTask; });
        using var ends = IgnisSessionHooks.TidyEndsOnReturn.Override(_ => true); // the API tidy-up: silent, done on return
        using var views = IgnisViews.Override(controller);
        using var rig = Rig();
        var point = IgnisEngine.Shared.TidyPoint(points.Find(ProviderKind.Claude, Opus)!)!.Value;

        Turn(rig, "Fix the parser", Usage(read: point, write1h: 2_000, context: point + 10_000), 1);
        rig.H.Main.Advance(IgnisController.BusyRetry + TimeSpan.FromSeconds(1));

        Assert.Equal([IgnisSettings.DefaultKeepTurns], tidies);
        Assert.Single(rig.Session.Inputs); // no turn of its own
        var entries = Entries(rig);
        Assert.DoesNotContain(entries, entry => entry.Item.TimelineItemContent is TimelineItem.Content.Notice { Value.Message: "Compacting context." });
        var line = Assert.Single(entries, entry => IgnisEvent.IsEventItem(entry.Item.Id));
        Assert.Equal(IgnisEventKind.TidiedUp, ledger.Event(line.Item.Id[IgnisEvent.TimelinePrefix.Length..])?.Kind);
        Assert.False(rig.H.Main.OnMain(() => controller.Status(rig.InitialThread.Id)).Tidying);
        Assert.True(rig.H.Main.OnMain(() => ((IIgnisRuntimePort)rig.Runtime).IsBetweenTasks));
    }

    [Fact]
    [Trait("Decision", "OD-0134")]
    public void A_tidy_up_that_tidied_nothing_writes_no_line_and_is_not_retried_before_the_next_request()
    {
        var tidies = 0;
        using var tidy = IgnisSessionHooks.Tidy.Override((_, _) => { tidies++; throw new InvalidOperationException("nothing to tidy"); });
        using var views = IgnisViews.Override(controller);
        using var rig = Rig();
        var point = IgnisEngine.Shared.TidyPoint(points.Find(ProviderKind.Claude, Opus)!)!.Value;

        Turn(rig, "Fix the parser", Usage(read: point, write1h: 2_000, context: point + 10_000), 1);
        rig.H.Main.Advance(TimeSpan.FromMinutes(2));

        Assert.Equal(1, tidies);
        Assert.DoesNotContain(Entries(rig), entry => IgnisEvent.IsEventItem(entry.Item.Id));
        Assert.DoesNotContain(Entries(rig), entry => entry.Item.TimelineItemContent is TimelineItem.Content.Notice { Value.NoticeLevel: Notice.Level.Warning });
        Assert.True(rig.H.Main.OnMain(() => ((IIgnisRuntimePort)rig.Runtime).IsBetweenTasks));
    }

    [Fact]
    [Trait("Decision", "OD-0134")]
    public void Off_does_nothing_no_ping_no_tidy_no_line_no_money()
    {
        var tidies = 0;
        using var tidy = IgnisSessionHooks.Tidy.Override((_, _) => { tidies++; return Task.CompletedTask; });
        settings.Update(value => value with { DefaultMode = IgnisMode.Off });
        environment.Draft = true;
        using var views = IgnisViews.Override(controller);
        using var rig = Rig();

        Turn(rig, "Fix the parser", Usage(read: 900_000, write1h: 2_000, context: 960_000), 1);
        rig.H.Main.Advance(TimeSpan.FromHours(3));

        Assert.Single(rig.Session.Inputs);
        Assert.Equal(0, tidies);
        Assert.Equal(0m, ledger.AllTimeSaved);
        Assert.Null(ledger.Since);
        Assert.DoesNotContain(Entries(rig), entry => IgnisEvent.IsEventItem(entry.Item.Id) || IgnisEvent.IsPingItem(entry.Item.Id));
        Assert.Equal(IgnisCacheState.Off, rig.H.Main.OnMain(() => controller.Status(rig.InitialThread.Id)).Cache);
    }

    [Fact]
    [Trait("Decision", "OD-0134")]
    public async Task The_model_points_update_row_reads_and_installs_from_the_loopback_feed_only()
    {
        using var runtime = new TestRuntime();
        using var offline = new InProcessHttpHandler(); // fail-closed: any host but loopback is refused and listed
        using var wire = URLSession.Transport.Override(offline);
        await using var server = new LoopbackHttpServer();
        var store = new IgnisPointsStore(data, server.Url("/ignis/model-points.json"));
        var ignis = new IgnisController(settings, store, ledger, IgnisEngine.Shared, environment);
        using var bound = IgnisSeams.Controller.Override(() => ignis);
        var item = Assert.IsType<IgnisPointsUpdateItem>(Assert.Single(UpdateEverythingProduction.IgnisItems()));
        var newer = IgnisPointsData.Json.Replace("\"version\": \"2026.10.10\"", "\"version\": \"2026.10.20\"", StringComparison.Ordinal);
        var body = System.Text.Encoding.UTF8.GetBytes(newer);

        var latest = item.LatestVersion(CancellationToken.None);
        var first = await server.NextExchange();
        Assert.Equal(("GET", "/ignis/model-points.json"), (first.Method, first.Target));
        await first.Respond(200, "OK", body, ("Content-Type", "application/json"));
        Assert.Equal("2026.10.20", await latest);
        Assert.Equal("2026.10.10", store.Current.Version);

        var update = item.Update(new Progress<double?>(), CancellationToken.None);
        var second = await server.NextExchange();
        await second.Respond(200, "OK", body, ("Content-Type", "application/json"));
        var outcome = await update;

        Assert.True(outcome.Succeeded);
        Assert.Equal("2026.10.20", outcome.NewVersion);
        Assert.True(outcome.Changed);
        Assert.Equal("2026.10.20", store.Current.Version);
        Assert.Equal("2026.10.20", new IgnisPointsStore(data, LoopbackHttpServer.RefusedUrl("/x")).Current.Version);
        Assert.Empty(offline.Received);
    }

    [Fact]
    [Trait("Decision", "OD-0134")]
    public void The_session_hooks_and_smart_helpers_bind_to_the_U2_methods()
    {
        var scripted = new ThreadRuntimeScriptedSession();
        Assert.Null(IgnisRuntimeStartup.KeepWarm(scripted)); // CLIs keep warm with hidden ping turns
        Assert.False(IgnisRuntimeStartup.TidyEndsOnReturn(scripted));

        Assert.Equal("claude-haiku-5-5", IgnisRuntimeStartup.HelperModel(controller, new IgnisHelperQuery(ProviderKind.Claude, Opus)));
        Assert.Equal("claude-haiku-5-5", IgnisRuntimeStartup.HelperModel(controller, new IgnisHelperQuery(ProviderKind.Claude, "opus")));
        Assert.Null(IgnisRuntimeStartup.HelperModel(controller, new IgnisHelperQuery(ProviderKind.Codex, Opus)));
        Assert.Null(IgnisRuntimeStartup.HelperModel(null, new IgnisHelperQuery(ProviderKind.Claude, Opus)));
        settings.Update(value => value with { SmartHelpers = false });
        Assert.Null(IgnisRuntimeStartup.HelperModel(controller, new IgnisHelperQuery(ProviderKind.Claude, Opus)));
        settings.Update(value => value with { SmartHelpers = true, DefaultMode = IgnisMode.Off });
        Assert.Null(IgnisRuntimeStartup.HelperModel(controller, new IgnisHelperQuery(ProviderKind.Claude, Opus)));
    }
}
