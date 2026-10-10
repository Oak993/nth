using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;
using Marrow.Testing;

namespace Marrow.Core.Tests.Port.Ignis;

// Ignis U0 (OD-0134, ED-NEW-ignis-contract; work/ignis/PLAN.md): the shared contract. Settings round-trip under the one
// "ignis" key, the design's words exactly, and a fake controller / runtime port / ledger / points that compile against
// the interfaces the other units implement.
public sealed class IgnisContractTests
{
    [Fact]
    [Trait("Feature", "IG-0")]
    [Trait("Decision", "OD-0134")] // design Settings › Ignis defaults
    public void Defaults_are_the_designs_and_nothing_is_written_until_a_change()
    {
        var store = new InMemorySettingsStore();
        var settings = new IgnisSettings(store);
        var d = settings.Value;
        Assert.Equal(IgnisMode.WarmAndTidy, d.DefaultMode);
        Assert.True(d.Auto);
        Assert.True(d.IncludeHeads);
        Assert.True(d.SmartHelpers);
        Assert.True(d.ShowExplanations);
        Assert.False(d.ShowPings);
        Assert.Equal(12, d.KeepTurns);
        Assert.True(d.AutoUpdatePoints);
        Assert.False(d.NoticeSeen);
        Assert.Empty(d.Chats);
        Assert.Equal("ignis", IgnisSettings.SettingsKey);
        Assert.Empty(store.Writes);
        Assert.Equal(new IgnisChatPreference(IgnisMode.WarmAndTidy, true), settings.ForChat(Guid.NewGuid()));
    }

    [Fact]
    [Trait("Feature", "IG-0")]
    [Trait("Decision", "OD-0134")]
    public void Settings_round_trip_through_the_one_key_and_chat_choices_persist()
    {
        var store = new InMemorySettingsStore();
        var settings = new IgnisSettings(store);
        var changed = 0;
        settings.Changed += () => changed++;
        var thread = new Guid("0f8fad5b-d9cb-469f-a165-70867728950e");

        settings.Update(v => v with { DefaultMode = IgnisMode.KeepWarm, Auto = false, IncludeHeads = false, SmartHelpers = false, ShowExplanations = false, ShowPings = true, KeepTurns = 24, AutoUpdatePoints = false, NoticeSeen = true });
        settings.SetChat(thread, new IgnisChatPreference(IgnisMode.Off, true));

        Assert.All(store.Writes, key => Assert.Equal("ignis", key));
        Assert.Equal(2, changed);
        var json = Assert.IsType<string>(store.Object("ignis"));
        Assert.Equal(
            "{\"defaultMode\":\"keepWarm\",\"auto\":false,\"heads\":false,\"helpers\":false,\"explanations\":false,\"showPings\":true,\"keepTurns\":24,\"autoUpdatePoints\":false,\"noticeSeen\":true,\"chats\":{\"0f8fad5b-d9cb-469f-a165-70867728950e\":{\"mode\":\"off\",\"auto\":true}}}",
            json);

        var reread = new IgnisSettings(store);
        Assert.Equal(settings.Value, reread.Value);
        Assert.Equal(new IgnisChatPreference(IgnisMode.Off, true), reread.ForChat(thread));

        // a chat set back to the defaults is not stored; forgetting a chat removes it
        settings.SetChat(thread, new IgnisChatPreference(IgnisMode.KeepWarm, false));
        Assert.Empty(settings.Value.Chats);
        settings.SetChat(thread, new IgnisChatPreference(IgnisMode.WarmAndTidy, false));
        settings.ForgetChat(thread);
        Assert.Empty(settings.Value.Chats);

        // setting an equal value writes nothing
        var writes = store.Writes.Count;
        settings.Value = settings.Value with { };
        Assert.Equal(writes, store.Writes.Count);
    }

    [Theory]
    [Trait("Feature", "IG-0")]
    [Trait("Decision", "OD-0134")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("{\"defaultMode\":\"turbo\",\"auto\":\"yes\",\"keepTurns\":0,\"chats\":{\"nope\":{\"mode\":\"off\",\"auto\":true}}}")]
    public void A_bad_stored_value_reads_as_the_defaults(string stored)
    {
        var store = new InMemorySettingsStore();
        store.Set("ignis", stored);
        Assert.Equal(IgnisPreferences.Default, new IgnisSettings(store).Value);
    }

    [Fact]
    [Trait("Feature", "IG-0")]
    [Trait("Decision", "OD-0134")] // phoenix-designs.html, Kindle and shared sections
    public void Words_are_the_designs_exactly()
    {
        Assert.Equal(["Off", "Keep warm", "Warm + tidy up"], Enum.GetValues<IgnisMode>().Select(IgnisWords.ModeName));
        Assert.Equal(["Reused quick memory", "Kept warm", "Tidy-ups", "Smart helpers"], Enum.GetValues<IgnisSource>().Select(IgnisWords.SourceName));
        Assert.Equal("42m", IgnisWords.CapsuleText(IgnisCacheState.Warm, TimeSpan.FromSeconds(41 * 60 + 5)));
        Assert.Equal("Cold", IgnisWords.CapsuleText(IgnisCacheState.Cold, null));
        Assert.Equal("Off", IgnisWords.CapsuleText(IgnisCacheState.Off, null));
        Assert.Equal("42 min left", IgnisWords.TimeLeft(IgnisCacheState.Warm, TimeSpan.FromMinutes(42)));
        Assert.Equal("Under a minute left", IgnisWords.TimeLeft(IgnisCacheState.Warm, TimeSpan.FromSeconds(20)));
        Assert.Equal("Warms with your next message", IgnisWords.TimeLeft(IgnisCacheState.Cold, null));
        Assert.Equal("Ignis is off", IgnisWords.TimeLeft(IgnisCacheState.Off, null));
        Assert.Equal(["Cold", "15m", "30m", "45m", "1h"], IgnisWords.TrackTicks(TimeSpan.FromHours(1)));
        Assert.Equal("24% full", IgnisWords.PercentFull(0.244));
        Assert.Equal(
            "Today Ignis saved $3.18. It keeps the quick memory warm while you work and tidies up the chat before it gets costly, always between tasks.",
            IgnisWords.Summary(3.18m, IgnisNext.WarmAndTidy));
        Assert.Equal("Ignis is off in this chat, so it isn’t saving anything right now.", IgnisWords.Next(IgnisNext.Off));
        Assert.Equal("Auto is off, so Ignis waits for you to keep warm or tidy up.", IgnisWords.Next(IgnisNext.AutoOffWarmAndTidy));
        Assert.Equal("This chat used 9% of your week.", IgnisWords.WeekShare(0.09));
        Assert.Equal("At this pace you’ll reach the weekly limit Thursday evening.", IgnisWords.WeeklyLimitAt(IgnisWords.DayPart(new DateTimeOffset(2026, 10, 15, 19, 0, 0, TimeSpan.Zero))));
        Assert.Equal("API key: you pay per token. This chat: $4.12.", IgnisWords.ApiKeyLine(4.12m));
        Assert.Equal("Tidying up…", IgnisWords.TidyingUp);
        Assert.Equal("What is quick memory?", IgnisWords.InfoAccessibleName(IgnisWords.InfoQuickMemoryName));
        Assert.Equal("Tools from 3 servers", IgnisWords.ToolsFromServers(3));
        Assert.Equal("2 servers unused this week.", IgnisWords.ServersUnused(2));
        Assert.Equal("Turning them off saves about 9K per message.", IgnisWords.TurningThemOffSaves(9_000));
        Assert.Equal("Last 12 turns", IgnisWords.KeepTurns(12));
        Assert.Equal("Since 3 February 2026, across all chats", IgnisWords.SinceAcrossAllChats(new DateOnly(2026, 2, 3)));
        Assert.Equal("6 days · about $9.73 a day", IgnisWords.MonthTileDetail(6, 58.40m));
        Assert.Equal("Where October’s savings came from", IgnisWords.WhereMonthsSavingsCameFrom(new DateOnly(2026, 10, 1)));
        Assert.Equal("Tue 6 Oct", IgnisWords.DayLabel(new DateOnly(2026, 10, 6), new DateOnly(2026, 10, 10)));
        Assert.Equal("kept warm 1 time", IgnisWords.KeptWarmTimes(1));
        Assert.Equal("Price step at 100K", IgnisWords.WhyPriceStep(100_000));
        Assert.Equal("Rebuild is cheapest before the last quarter of its 400K window", IgnisWords.WhyLastQuarter(400_000));
        Assert.Equal("2026.10.08 → 2026.10.10", IgnisWords.UpdatesRowVersions("2026.10.08", "2026.10.10"));
        Assert.Equal("$1,478.00", IgnisWords.Money(1478m));
        Assert.Equal("$1,478", IgnisWords.MoneyWhole(1477.82m));
        Assert.Equal("2¢", IgnisWords.Cents(0.02m));
        Assert.Equal(
            "To keep this chat's cache warm, Ignis sends Codex a tiny message now and then. You'll see a short line here instead.",
            IgnisWords.NoticeBody("Codex"));
        Assert.Equal("[nth Ignis] Automatic keep-alive, not from the user. Use no tools or agents and change nothing. Reply exactly: ok", IgnisWords.PingText);
    }

    [Fact]
    [Trait("Feature", "IG-0")]
    [Trait("Decision", "OD-0134")]
    public void Event_lines_history_rows_amounts_and_ids()
    {
        var thread = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 10, 10, 10, 6, 0, TimeSpan.Zero);
        var warm = new IgnisEvent("a", thread, IgnisEventKind.KeptWarm, at, 0.02m);
        var tidy = new IgnisEvent("b", thread, IgnisEventKind.TidiedUp, at, 0.42m, IgnisAmountKind.Saved, AtTokens: 400_000);
        var cold = new IgnisEvent("c", thread, IgnisEventKind.WentCold, at, 0.21m, IgnisAmountKind.Lost);
        Assert.Equal("Ignis kept the quick memory warm", IgnisWords.EventLine(warm));
        Assert.Equal("Ignis tidied up the chat", IgnisWords.EventLine(tidy));
        Assert.Equal("The quick memory went cold. Your next message warms it again", IgnisWords.EventLine(cold));
        Assert.Equal("Tidied up the chat at 400K", IgnisWords.HistoryRow(tidy));
        Assert.Equal(["$0.02", "saved $0.42", "lost $0.21"], new[] { warm, tidy, cold }.Select(IgnisWords.EventAmount));
        Assert.Equal("10:06", IgnisWords.EventTime(at));
        Assert.Equal("ignis:event:b", tidy.TimelineItemID);
        Assert.True(IgnisEvent.IsEventItem(tidy.TimelineItemID));
        var ping = IgnisEvent.NewPingItemID();
        Assert.StartsWith("ignis:ping:", ping, StringComparison.Ordinal);
        Assert.True(IgnisEvent.IsPingItem(ping));

        var pingItem = new TimelineItem(null, new TimelineItem.Content.Notice(new Notice { NoticeLevel = Notice.Level.Info, Message = "ok" }), ping);
        Assert.False(IgnisSeams.ShowsItem(pingItem, showPings: false));
        Assert.True(IgnisSeams.ShowsItem(pingItem, showPings: true));
        Assert.True(IgnisSeams.TimelineFilter.Current(pingItem)); // unbound: shows everything
        Assert.Null(IgnisSeams.Controller.Current());
        Assert.Null(IgnisSeams.HelperAgents.Current(new IgnisHelperQuery(ProviderKind.Claude, "opus")));
    }

    [Fact]
    [Trait("Feature", "IG-0")]
    [Trait("Decision", "OD-0134")]
    public void Turn_usage_totals_and_the_save_report()
    {
        var usage = new IgnisTurnUsage(ProviderKind.Claude, "opus", null, 10, 1000, 100, 200, 5, 50, 1315, 1_000_000, true);
        Assert.Equal(305, usage.CacheWrite);
        Assert.Equal(1315, usage.TotalInput);
        Assert.False(IgnisTurnUsage.NotReported(ProviderKind.Codex, null, null).Reported);

        var report = IgnisWords.Report(new IgnisReport(
            new DateTimeOffset(2026, 10, 6, 10, 48, 0, TimeSpan.Zero), "Opus 5.5", IgnisMode.WarmAndTidy, true, 3.18m,
            new Dictionary<IgnisSource, decimal> { [IgnisSource.Reuse] = 1.62m, [IgnisSource.KeptWarm] = 0.78m, [IgnisSource.TidyUp] = 0.42m, [IgnisSource.SmartHelpers] = 0.36m },
            0.21m, 0.94, 0.244, IgnisWords.ReportApiLine(4.12m), 25.58m, 1478m));
        Assert.Equal(string.Join("\n",
            "Ignis report · this chat · Tuesday 10:48",
            "Model: Opus 5.5 · Mode: Warm + tidy up · Auto on",
            "",
            "Saved today: $3.18",
            "  Reused quick memory  $1.62",
            "  Kept warm            $0.78",
            "  Tidy-ups             $0.42",
            "  Smart helpers        $0.36",
            "Lost when it went cold: $0.21 · Quick memory reused 94% of the time",
            "Chat size: 24% full",
            "",
            "API key: this chat cost $4.12 so far.",
            "Last 7 days: $25.58 saved · All chats: $1,478 saved"), report);

        var status = IgnisChatStatus.Unknown(Guid.NewGuid(), IgnisMode.KeepWarm, false);
        Assert.Equal((IgnisCacheState.Cold, IgnisNext.AutoOffKeepWarm), (status.Cache, status.Next));
        Assert.Null(status.ContextFill);
    }

    [Fact]
    [Trait("Feature", "IG-0")]
    [Trait("Decision", "OD-0134")]
    public void A_fake_controller_compiles_against_the_contract_and_binds_through_the_seam()
    {
        var store = new InMemorySettingsStore();
        var controller = new FakeIgnisController(new IgnisSettings(store));
        using var bound = IgnisSeams.Controller.Override(() => controller);
        var port = new FakeIgnisPort();
        controller.Attach(port);
        var c = IgnisSeams.Controller.Current()!;
        c.SetMode(port.ThreadID, IgnisMode.KeepWarm);
        c.KeepWarmNow(port.ThreadID);
        Assert.Equal(1, port.Pings);
        Assert.Equal(IgnisMode.KeepWarm, c.Status(port.ThreadID).Mode);
        Assert.Equal(IgnisMode.KeepWarm, c.Settings.ForChat(port.ThreadID).Mode);
    }
}
