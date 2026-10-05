using Marrow.Core.Accounts;
using Marrow.Core.Accounts.Pills;
using Marrow.Core.Models;
using Marrow.Core.Port.Accounts;
using Marrow.Core.Port.MultiAccount.Capsule;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.Testing.MultiAccountSettings;

namespace Marrow.Core.Tests.Port.MultiAccount.Capsule;

// MA-9 (step 3a, OD-0115): what the composer's capsule is for a thread: shown only with a second account or a pill relay, the monogram, the usage, the words for a screen reader.
[Trait("Feature", "MA-9")]
[Trait("Rule", "R-TEST-4")]
public sealed class ComposerCapsuleModelTests
{
    private static ComposerCapsuleModel Model(AccountsRig rig) => new(rig.Accounts, rig.Pills, a => rig.IsConnected(a), id => rig.Usage.GetValueOrDefault(id));

    private static (AccountsRig Rig, Account First, Account Second) TwoClaude()
    {
        var rig = new AccountsRig();
        var first = rig.Connect(ProviderKind.Claude, "Max");
        var second = rig.Connect(rig.Accounts.Add(ProviderKind.Claude, "Max"));
        return (rig, first, second);
    }

    [Theory]
    [InlineData("Claude Max 2", false, "M2")]
    [InlineData("Claude Pro", false, "P")]
    [InlineData("Codex 1", false, "C1")]
    [InlineData("Codex", false, "C")]
    [InlineData("Claude", false, "C")]
    [InlineData("Work laptop", true, "WL")]
    [InlineData("Team 3", true, "T3")]
    [InlineData("Personal", true, "P")]
    [InlineData("deep thinker", true, "DT")]
    public void The_monogram_follows_the_pages_rule(string name, bool custom, string expected) =>
        Assert.Equal(expected, AccountMono.Of(name, custom));

    [Fact]
    public void One_account_of_a_service_and_no_relay_shows_no_capsule()
    {
        var rig = new AccountsRig();
        var only = rig.Connect(ProviderKind.Claude);
        Assert.Null(Model(rig).For(new CapsuleSubject(ProviderKind.Claude, only.Id, null, false, null)));
    }

    [Fact]
    public void A_second_connected_account_of_the_service_shows_the_plain_capsule_with_the_active_accounts_monogram()
    {
        var (rig, first, second) = TwoClaude();
        var shown = Model(rig).For(new CapsuleSubject(ProviderKind.Claude, second.Id, null, false, null));
        Assert.NotNull(shown);
        Assert.Equal("M2", shown!.Mono);
        Assert.Null(shown.Pill);
        Assert.Equal("Claude Max 2", shown.AccountName);
    }

    [Fact]
    public void A_second_account_that_is_not_connected_or_is_off_does_not_count()
    {
        var rig = new AccountsRig();
        var first = rig.Connect(ProviderKind.Claude, "Max");
        var second = rig.Accounts.Add(ProviderKind.Claude, "Max");
        Assert.Null(Model(rig).For(new CapsuleSubject(ProviderKind.Claude, first.Id, null, false, null)));
        rig.Connect(second);
        Assert.NotNull(Model(rig).For(new CapsuleSubject(ProviderKind.Claude, first.Id, null, false, null)));
        rig.Accounts.SetEnabled(second.Id, false);
        Assert.Null(Model(rig).For(new CapsuleSubject(ProviderKind.Claude, first.Id, null, false, null)));
    }

    [Fact]
    public void The_arc_is_the_five_hour_use_and_the_capsule_is_hot_from_ninety_percent()
    {
        var (rig, first, second) = TwoClaude();
        rig.Usage[first.Id] = new AccountUsageLine(0.42, null, 0.1, null, false, false);
        var model = Model(rig);
        var shown = model.For(new CapsuleSubject(ProviderKind.Claude, first.Id, null, false, null))!;
        Assert.Equal(0.42, shown.Use, 9);
        Assert.False(shown.Hot);
        Assert.Equal("Account: Claude Max 1, 42% of its 5-hour limit used. Change account", shown.Aria);
        rig.Usage[first.Id] = new AccountUsageLine(0.93, null, 0.1, null, false, false);
        Assert.True(model.For(new CapsuleSubject(ProviderKind.Claude, first.Id, null, false, null))!.Hot);
    }

    [Fact]
    public void A_plan_without_a_five_hour_window_shows_the_week_and_says_so()
    {
        var (rig, first, _) = TwoClaude();
        rig.Usage[first.Id] = new AccountUsageLine(null, null, 0.3, null, true, false);
        var shown = Model(rig).For(new CapsuleSubject(ProviderKind.Claude, first.Id, null, false, null))!;
        Assert.Equal(0.3, shown.Use, 9);
        Assert.Equal("Account: Claude Max 1, 30% of its weekly limit used. Change account", shown.Aria);
    }

    [Fact]
    public void An_account_with_no_reported_usage_has_no_arc_and_no_percent_in_its_label()
    {
        var (rig, first, _) = TwoClaude();
        var shown = Model(rig).For(new CapsuleSubject(ProviderKind.Claude, first.Id, null, false, null))!;
        Assert.Equal(0, shown.Use, 9);
        Assert.Equal("Account: Claude Max 1. Change account", shown.Aria);
    }

    [Fact]
    public void A_custom_name_is_the_accounts_name_and_gives_its_monogram()
    {
        var (rig, first, _) = TwoClaude();
        rig.Accounts.Rename(first.Id, "Work laptop");
        var shown = Model(rig).For(new CapsuleSubject(ProviderKind.Claude, first.Id, null, false, null))!;
        Assert.Equal("WL", shown.Mono);
        Assert.Equal("Work laptop", shown.AccountName);
    }

    [Fact]
    public void A_thread_on_a_pill_shows_the_pill_capsule_even_with_one_account_per_service()
    {
        var rig = new AccountsRig();
        var claude = rig.Connect(ProviderKind.Claude, "Max");
        var codex = rig.Connect(ProviderKind.Codex);
        var pill = rig.MakePill("Deep work", claude, codex);
        var relay = new PillThreadState(pill.Id, 1, codex.Id.ToString("D"));
        var shown = Model(rig).For(new CapsuleSubject(ProviderKind.Codex, codex.Id, relay, false, null))!;
        Assert.NotNull(shown.Pill);
        Assert.Equal(2, shown.Pill!.Steps.Count);
        Assert.Equal(1, shown.Pill.Cur);
        Assert.Equal("Deep work", shown.Pill.Name);
        Assert.Equal(shown.Pill.Steps[1].Mono, shown.Mono);
        Assert.Equal("Pill Deep work, step 2 of 2: Codex. Open accounts and pills", shown.Aria);
    }

    [Fact]
    public void The_pill_capsule_follows_the_relay_step_the_handshake_standby_and_the_use_of_both_accounts()
    {
        var rig = new AccountsRig();
        var claude = rig.Connect(ProviderKind.Claude, "Max");
        var codex = rig.Connect(ProviderKind.Codex);
        var pill = rig.MakePill("Night shift", claude, codex);
        rig.Usage[claude.Id] = new AccountUsageLine(0.5, null, null, null, false, false);
        rig.Usage[codex.Id] = new AccountUsageLine(0.2, null, null, null, false, false);
        var relay = new PillThreadState(pill.Id, 0, claude.Id.ToString("D"));
        var shown = Model(rig).For(new CapsuleSubject(ProviderKind.Claude, claude.Id, relay, true, codex.Id))!.Pill!;
        Assert.True(shown.Handshake);
        Assert.Equal(1, shown.Stand);
        Assert.Equal(0.5, shown.Use);
        Assert.Equal(0.2, shown.NextUse);
    }

    [Fact]
    public void A_relay_whose_pill_is_gone_falls_back_to_the_plain_account_capsule()
    {
        var (rig, first, _) = TwoClaude();
        var shown = Model(rig).For(new CapsuleSubject(ProviderKind.Claude, first.Id, new PillThreadState(Guid.NewGuid(), 0, first.Id.ToString("D")), false, null))!;
        Assert.Null(shown.Pill);
    }

    [Fact]
    public void Each_step_has_a_hue_that_is_the_services_own_tint_not_a_usage_colour()
    {
        var rig = new AccountsRig();
        var claude = rig.Connect(ProviderKind.Claude, "Max");
        var codex = rig.Connect(ProviderKind.Codex);
        var pill = rig.MakePill("Mix", claude, codex);
        var steps = Model(rig).For(new CapsuleSubject(ProviderKind.Claude, claude.Id, new PillThreadState(pill.Id, 0, claude.Id.ToString("D")), false, null))!.Pill!.Steps;
        Assert.NotEqual(steps[0].Hue, steps[1].Hue);
    }
}
