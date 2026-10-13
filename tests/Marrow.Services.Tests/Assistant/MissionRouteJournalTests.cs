using System.Text.Json.Nodes;
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-14 (DESIGN.md rev 5, 11.6, 15.3; ED-NEW-dextra-as14-4): a mission remembers the account the router chose for it and
// whether the owner pinned its model. Both are journaled under optional keys written only when set, so a journal written by the
// previous build loads unchanged and a mission nobody routed is written exactly as it was.
[Trait("Decision", "ED-NEW-dextra-as14-4")]
[Trait("Feature", "AS-14")]
public sealed class MissionRouteJournalTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 13, 9, 30, 0, TimeSpan.Zero);

    private static readonly Guid Account = Guid.Parse("a1000000-0000-4000-8000-0000000000a1");

    private static MissionRecord Bare() => new(new Mission(Guid.Parse("20000000-0000-4000-8000-000000000002"), "Fix the login timeout", "dextra", MissionOrigin.Owner, MissionState.Working))
    {
        Brief = "Make the login wait 30 s.",
        ProjectId = Guid.Parse("50000000-0000-4000-8000-000000000005"),
        ProjectPath = @"C:\code\app",
        Provider = ProviderKind.Claude,
        Model = "model-a",
        Created = At,
    };

    [Fact]
    public void The_chosen_account_and_the_pin_come_back_whole()
    {
        var record = Bare() with { AccountId = Account, ModelPinned = true };
        var json = MissionJournal.ToJson(record);
        Assert.Equal("a1000000-0000-4000-8000-0000000000a1", json["account"]!.GetValue<string>());
        Assert.True(json["modelPinned"]!.GetValue<bool>());
        var back = MissionJournal.FromJson(json);
        Assert.Equal(record, back);
        Assert.Equal(Account, back!.AccountId);
        Assert.True(back.ModelPinned);
    }

    [Fact]
    public void A_mission_nobody_routed_is_written_without_either_key_and_reads_back_unrouted()
    {
        var json = MissionJournal.ToJson(Bare());
        Assert.False(json.ContainsKey("account"));
        Assert.False(json.ContainsKey("modelPinned"));
        var back = MissionJournal.FromJson(json)!;
        Assert.Null(back.AccountId);
        Assert.False(back.ModelPinned);
    }

    [Fact]
    public void A_journal_written_by_the_previous_build_loads_with_the_defaults()
    {
        var old = MissionJournal.ToJson(Bare());
        old.Remove("account");
        old.Remove("modelPinned");
        var back = MissionJournal.FromJson(old)!;
        Assert.Equal(Bare(), back);
    }

    [Fact]
    public void An_account_that_is_not_an_id_or_a_pin_that_is_not_a_flag_leaves_the_body_damaged_and_unread()
    {
        var badAccount = MissionJournal.ToJson(Bare() with { AccountId = Account });
        badAccount["account"] = "not-an-id";
        Assert.Null(MissionJournal.FromJson(badAccount));
        var badPin = MissionJournal.ToJson(Bare() with { ModelPinned = true });
        badPin["modelPinned"] = "yes";
        Assert.Null(MissionJournal.FromJson(badPin));
    }

    [Fact]
    public void A_mission_that_was_live_when_nth_ended_keeps_its_account_and_pin_through_the_interrupted_rule()
    {
        var live = MissionJournal.ToJson(Bare() with { AccountId = Account, ModelPinned = true });
        var interrupted = MissionJournal.Interrupted(live);
        Assert.NotNull(interrupted);
        var back = MissionJournal.FromJson(interrupted)!;
        Assert.Equal(MissionState.Paused, back.Mission.State);
        Assert.Equal(Account, back.AccountId);
        Assert.True(back.ModelPinned);
        Assert.IsType<JsonObject>(interrupted);
    }
}
