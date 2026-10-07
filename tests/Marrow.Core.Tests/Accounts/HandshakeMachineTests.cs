using Marrow.Core.Accounts;

namespace Marrow.Core.Tests.Accounts;

// NOTES "Handshake", "Handshake timing": Standby, Linking, In sync, Switch; the switch happens only between steps, never mid-edit.
public sealed class HandshakeMachineTests
{
    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T31")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Below_the_start_point_nothing_happens()
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll();
        rig.SetUsed("c1", 97);
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Empty(rig.Events);
        Assert.Empty(rig.Host.Log);
        Assert.Equal(HandshakePhase.Idle, rig.Machine.Phase);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T32")]
    [Trait("Rule", "R-TEST-4")]
    public async Task At_the_start_point_the_four_phases_run_in_order_and_the_thread_ends_on_the_next_account()
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll();
        rig.Host.ReadBack = "all of it";
        rig.SetUsed("c1", 98);
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal([HandshakePhase.Standby, HandshakePhase.Linking, HandshakePhase.InSync, HandshakePhase.Switch, HandshakePhase.Idle], rig.Phases());
        Assert.Equal(["standby:c2", "briefing", "receive:readback", "switch:c2:noresume:safe"], rig.Host.Log);
        Assert.Equal("c2", rig.Host.ActiveAccountId);
        Assert.All(rig.Events, e => Assert.Equal(("c1", HandshakeReason.NearLimit), (e.OutgoingAccountId, e.Reason)));
        Assert.Equal("c2", rig.Events[0].IncomingAccountId);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T33")]
    [Trait("Rule", "R-TEST-4")]
    public async Task The_incoming_account_gets_marrows_summary_and_the_outgoing_accounts_briefing()
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll(bring: DecisionValues.AiSummary);
        rig.SetUsed("c1", 98);
        await rig.Machine.TickAsync(rig.Ct);
        var package = Assert.Single(rig.Host.Packages);
        Assert.Equal(Bring.AiSummary, package.Bring);
        Assert.Equal("briefing", package.Briefing);
        Assert.Equal(HandshakeRig.Thread, Guid.Parse(package.Summary.Text["summary of ".Length..]));
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T34")]
    [Trait("Rule", "R-TEST-4")]
    public async Task The_switch_waits_for_a_safe_point_and_never_happens_mid_edit()
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll();
        rig.Host.AtSafePoint = false;
        rig.SetUsed("c1", 98);
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal(HandshakePhase.InSync, rig.Machine.Phase);
        Assert.DoesNotContain(rig.Host.Log, l => l.StartsWith("switch", StringComparison.Ordinal));
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal(HandshakePhase.InSync, rig.Machine.Phase);
        rig.Host.AtSafePoint = true;
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal(HandshakePhase.Idle, rig.Machine.Phase);
        Assert.Equal("switch:c2:noresume:safe", rig.Host.Log[^1]);
        Assert.Single(rig.Host.Log, l => l.StartsWith("standby", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T35")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Marrow_checks_the_read_back_and_fills_only_the_gaps()
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll();
        rig.Summaries.KeyFacts = ["Login.cs", "fix the redirect"];
        rig.Host.ReadBack = "I know about login.cs.";
        rig.SetUsed("c1", 98);
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal(["fix the redirect"], Assert.Single(rig.Host.GapsSent));
        var ready = Assert.Single(rig.Events, e => e.Kind == HandshakeEventKind.InSyncReady);
        Assert.Equal("1 gap filled", ready.Detail);
        Assert.True(rig.Host.Log.IndexOf("gaps:fix the redirect") < rig.Host.Log.FindIndex(l => l.StartsWith("switch", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T36")]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_read_back_with_no_gaps_sends_nothing()
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll();
        rig.Summaries.KeyFacts = ["Login.cs"];
        rig.Host.ReadBack = "login.cs";
        rig.SetUsed("c1", 98);
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Empty(rig.Host.GapsSent);
        Assert.Equal("in sync", Assert.Single(rig.Events, e => e.Kind == HandshakeEventKind.InSyncReady).Detail);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T37")]
    [Trait("Rule", "R-TEST-4")]
    public async Task The_per_account_point_overrides_the_default()
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll();
        rig.Store.Update("c1", a => a with { HandshakeAt = HandshakePoint.At(99) });
        rig.SetUsed("c1", 98.5);
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Empty(rig.Host.Log);
        rig.SetUsed("c1", 99);
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal("c2", rig.Host.ActiveAccountId);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T38")]
    [Trait("Rule", "R-TEST-4")]
    public async Task With_the_Handshake_off_nothing_starts_near_the_limit()
    {
        await using var rig = new HandshakeRig(options: new HandshakeOptions(Enabled: false));
        rig.SettleAll();
        rig.SetUsed("c1", 99);
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Empty(rig.Host.Log);
    }

    [Theory]
    [InlineData(DecisionKind.OnLimit, null)]
    [InlineData(DecisionKind.OnLimit, DecisionValues.Wait)]
    [InlineData(DecisionKind.OnLimit, DecisionValues.Ask)]
    [InlineData(DecisionKind.Bring, null)]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T39")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Nothing_is_prepared_ahead_while_a_choice_it_needs_is_unset_or_says_wait_or_ask(DecisionKind kind, string? value)
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll();
        rig.DecisionStore.SetSetting(kind, value);
        rig.SetUsed("c1", 99);
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Empty(rig.Host.Log);
        Assert.Empty(rig.Events);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T40")]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_standby_that_cannot_start_degrades_to_idle_and_is_not_retried_on_every_tick()
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll();
        rig.Host.StandbyFails = new InvalidOperationException("cli missing");
        rig.SetUsed("c1", 98);
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal(HandshakePhase.Idle, rig.Machine.Phase);
        var degraded = Assert.Single(rig.Events, e => e.Kind == HandshakeEventKind.Degraded);
        Assert.Contains("cli missing", degraded.Detail, StringComparison.Ordinal);
        rig.Host.StandbyFails = null;
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Empty(rig.Host.Log);
        rig.SetUsed("c1", 40);
        await rig.Machine.TickAsync(rig.Ct);
        rig.SetUsed("c1", 98);
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal("c2", rig.Host.ActiveAccountId);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T41")]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_failed_briefing_degrades_but_the_switch_still_carries_on_with_the_summary()
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll();
        rig.Host.BriefingFails = new InvalidOperationException("no reply");
        rig.SetUsed("c1", 98);
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Contains(rig.Events, e => e.Kind == HandshakeEventKind.Degraded);
        Assert.Null(Assert.Single(rig.Host.Packages).Briefing);
        Assert.Equal("c2", rig.Host.ActiveAccountId);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T42")]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_pill_step_hands_its_model_effort_and_fast_mode_to_the_standby()
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll();
        rig.Host.Pill = new Pill("p", [new PillStep("c1"), new PillStep("x1", "gpt", "high", true)]);
        rig.Host.StepIndex = 0;
        rig.SetUsed("c1", 98);
        await rig.Machine.TickAsync(rig.Ct);
        var target = Assert.Single(rig.Host.Targets);
        Assert.Equal(("x1", 1, new StepSettings("gpt", "high", true)), (target.Account.Id, target.StepIndex, target.Settings));
        Assert.Equal(1, rig.Host.StepIndex);
    }

    [Fact]
    [Trait("Feature", "MA-5")]
    [Trait("Spec", "MA-T43")]
    [Trait("Rule", "R-TEST-4")]
    public async Task The_last_pill_step_loops_to_the_highest_available_step_with_a_Handshake()
    {
        await using var rig = new HandshakeRig();
        rig.SettleAll();
        rig.Host.Pill = new Pill("p", [new PillStep("c1"), new PillStep("c2"), new PillStep("x1")]);
        rig.Host.StepIndex = 2;
        rig.Host.ActiveAccountId = "x1";
        rig.SetUsed("c2", 100, rig.Clock.GetUtcNow().AddHours(2), isOut: true);
        rig.SetUsed("x1", 98);
        await rig.Machine.TickAsync(rig.Ct);
        Assert.Equal(["standby:c1", "briefing", "receive:readback", "switch:c1:noresume:safe"], rig.Host.Log);
        Assert.Equal(0, rig.Host.StepIndex);
    }
}
