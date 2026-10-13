using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Testing;

namespace Marrow.App.Tests.Assistant.Home;

// Dextra AS-8 (DESIGN.md rev 5, 2.6 first run, 5.4 the two reading switches, 10.4 the stop chord, 12.2 grants kept across launches,
// 16 "Settings › Assistant"; ED-NEW-dextra-as8-8): the owner's Dextra choices live under one "dextra" key as compact JSON; a damaged
// value keeps its default, a damaged or disallowed grant is dropped, and only grants still worth keeping are stored.
[Trait("Feature", "AS-8")]
[Trait("Decision", "ED-NEW-dextra-as8-8")]
public sealed class DextraSettingsTests
{
    private static readonly DateTimeOffset Now = HomeSamples.Now;

    private static Grant Login(GrantScope scope, int minutesAgo = 10) =>
        new(Guid.NewGuid(), "dextra", ActionClass.Login, GrantTarget.Site("https://shop.example"), scope) { Created = Now.AddMinutes(-minutesAgo) };

    [Fact]
    public void Nothing_stored_reads_the_defaults_and_writes_nothing()
    {
        var store = new InMemorySettingsStore();
        var settings = new DextraSettings(store);
        var value = settings.Value;

        Assert.Equal(DextraPreferences.Default, value);
        Assert.True(value.On);
        Assert.False(value.FirstRunDone);
        Assert.Null(value.BrainProvider); // Automatic
        Assert.True(value.ReadSameProvider);
        Assert.False(value.ReadOtherProviders);
        Assert.Equal(DelegateScope.Off, value.Delegate);
        Assert.True(value.MissionsCollapsed);
        Assert.Empty(value.Grants);
        Assert.Equal("Ctrl+Alt+Esc", settings.Chord);
        Assert.Empty(store.Writes);
        Assert.Equal(["digest", "watch-builds", "tidy-chats"], DextraSettings.FirstDuties.Select(d => d.Id));
    }

    [Fact]
    public void Every_choice_and_every_kind_of_grant_survives_a_round_trip()
    {
        var mission = Guid.NewGuid();
        var full = DextraPreferences.Default with
        {
            Id = Guid.NewGuid(),
            On = false,
            FirstRunDone = true,
            BrainProvider = ProviderKind.Codex,
            BrainAccount = Guid.NewGuid(),
            BrainModel = "gpt-test",
            ReadSameProvider = false,
            ReadOtherProviders = true,
            Delegate = DelegateScope.ThisProject,
            DelegateProject = Guid.NewGuid(),
            FirstDuty = "watch-builds",
            StopChord = "Ctrl+Alt+Pause",
            MissionsCollapsed = false,
            Grants = new ValueList<Grant>(new[]
            {
                Login(GrantScope.Standing, 30) with { Fields = new ValueList<string>(new[] { "sign in" }), Excluded = new ValueList<string>(new[] { "upload" }), FromDecision = Guid.NewGuid() },
                Login(GrantScope.UntilDate(Now.AddDays(7)), 20) with { Conditions = new ValueList<string>(new[] { "weekdays only" }) },
                Login(GrantScope.ForMission(mission), 10) with { UsedBy = new ValueList<string>(new[] { "audit-1" }) },
            }),
        };

        Assert.Equal(full, DextraSettings.FromJson(DextraSettings.ToJson(full)));
        var store = new InMemorySettingsStore();
        new DextraSettings(store).Value = full;
        Assert.Equal(full, new DextraSettings(store).Value);
        Assert.Equal("Ctrl+Alt+Pause", new DextraSettings(store).Chord);
        Assert.Equal(DextraPreferences.Default, DextraSettings.FromJson(DextraSettings.ToJson(DextraPreferences.Default)));
    }

    [Fact]
    public void Changed_is_raised_only_by_a_real_change()
    {
        var store = new InMemorySettingsStore();
        var settings = new DextraSettings(store);
        var changes = 0;
        settings.Changed += () => changes++;

        settings.Update(p => p with { On = true }); // already on
        Assert.Equal(0, changes);
        Assert.Empty(store.Writes);
        settings.Update(p => p with { FirstRunDone = true });
        Assert.Equal(1, changes);
        var id = settings.EnsureId();
        Assert.Equal(id, settings.EnsureId());
        Assert.Equal(2, changes);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    [InlineData("\"dextra\"")]
    [InlineData("{\"on\": ")]
    public void A_damaged_value_reads_as_the_defaults(string stored)
    {
        Assert.Equal(DextraPreferences.Default, DextraSettings.FromJson(stored));
    }

    [Fact]
    public void A_member_of_the_wrong_kind_keeps_its_default_and_a_damaged_or_disallowed_grant_is_dropped()
    {
        var good = Login(GrantScope.Standing);
        var goodJson = DextraSettings.ToJson(DextraPreferences.Default with { Grants = new ValueList<Grant>(new[] { good }) });
        var goodGrant = goodJson[(goodJson.IndexOf("[{", StringComparison.Ordinal) + 1)..goodJson.LastIndexOf(']')];
        var json = "{\"on\":\"yes\",\"firstRunDone\":1,\"brainProvider\":\"Nope\",\"delegate\":\"sometimes\",\"firstDuty\":\"juggle\",\"stopChord\":\"\","
            + "\"grants\":[{\"id\":\"x\"}," + goodGrant + ","
            + "{\"id\":\"" + Guid.NewGuid() + "\",\"assistant\":\"dextra\",\"class\":\"Outward\",\"targetKind\":\"Recipients\",\"target\":[\"sam@example.com\"],\"scope\":\"Standing\",\"created\":\"2026-09-22T12:00:00.0000000+00:00\"},"
            + "{\"id\":\"" + Guid.NewGuid() + "\",\"assistant\":\"dextra\",\"class\":\"Login\",\"targetKind\":\"Site\",\"target\":[\"https://shop.example\"],\"scope\":\"Until\",\"created\":\"2026-09-22T12:00:00.0000000+00:00\"},"
            + "7]}";

        var read = DextraSettings.FromJson(json);
        Assert.True(read.On);
        Assert.False(read.FirstRunDone);
        Assert.Null(read.BrainProvider);
        Assert.Equal(DelegateScope.Off, read.Delegate);
        Assert.Null(read.FirstDuty);
        Assert.Null(read.StopChord);
        Assert.Equal(good, Assert.Single(read.Grants)); // no id, a sending grant no rule allows, an Until with no end, a number
    }

    [Fact]
    public void Only_grants_still_worth_keeping_are_stored_oldest_first()
    {
        var store = new InMemorySettingsStore();
        var settings = new DextraSettings(store);
        var standing = Login(GrantScope.Standing, 50);
        var mission = Login(GrantScope.ForMission(Guid.NewGuid()), 40);
        var week = Login(GrantScope.UntilDate(Now.AddDays(7)), 30);
        var ended = Login(GrantScope.UntilDate(Now), 60);
        var revoked = Login(GrantScope.Standing, 20).Revoked(Now.AddMinutes(-1));
        var spent = Login(GrantScope.Once, 15).UsedByEntry("audit-9");
        var unused = Login(GrantScope.Once, 5);

        settings.KeepGrants([unused, week, spent, revoked, ended, mission, standing], Now);
        Assert.Equal([standing.Id, mission.Id, week.Id, unused.Id], settings.Value.Grants.Select(g => g.Id));

        var writes = store.Writes.Count;
        settings.KeepGrants([standing, mission, week, unused], Now);
        Assert.Equal(writes, store.Writes.Count); // the same list writes nothing
        Assert.False(DextraSettings.Keeps(week, Now.AddDays(7)));
    }

    [Fact]
    public void The_brain_carries_the_two_reading_switches_and_a_local_brain_runs_on_the_PC()
    {
        var settings = new DextraSettings(new InMemorySettingsStore());
        settings.Update(p => p with { ReadSameProvider = false, ReadOtherProviders = true });

        Assert.Equal(new FeedBrain(null, false, false, true), settings.Brain(null));
        Assert.Equal(new FeedBrain(ProviderKind.Claude, false, false, true), settings.Brain(ProviderKind.Claude));
        Assert.True(settings.Brain(ProviderKind.Local).RunsLocally);
    }
}
