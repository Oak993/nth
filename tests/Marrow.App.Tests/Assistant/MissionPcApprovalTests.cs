using System.Reflection;
using System.Text.Json.Nodes;
using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Assistant;

namespace Marrow.App.Tests.Assistant;

[Trait("Decision", "ED-NEW-mission-pc-1")]
public sealed class MissionPcApprovalTests
{
    private static Task<bool> Approve(DextraTaintRig rig, MissionRecord record, string tool, CancellationToken token = default)
    {
        var method = typeof(AssistantController).GetMethod("ApprovePc");
        Assert.NotNull(method);
        var registration = new PcRegistration(record.LeadThread!.Value.ToString("D"), ProviderKind.Claude, "model-a", true) { Head = "a mission chat" };
        var mission = typeof(PcRegistration).GetProperty("MissionId");
        Assert.NotNull(mission);
        mission.SetValue(registration, record.Id);
        return (Task<bool>)method.Invoke(rig.Controller, [registration, tool, new JsonObject { ["text"] = "sentinel-secret-123" }, token])!;
    }

    private static void BindDesktop(DextraTaintRig rig, IDesktopGate desktop)
    {
        var property = typeof(AssistantController).GetProperty("MissionDesktop", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        property.SetValue(rig.Controller, desktop);
    }

    [Theory]
    [InlineData("pc_type")]
    [InlineData("web_open")]
    public async Task Ask_waits_for_the_card_No_refuses_and_the_log_never_keeps_typed_secrets(string tool)
    {
        using var rig = new DextraTaintRig();
        var record = rig.Mission();
        BindDesktop(rig, new DesktopGate(new FakeDesktop(), TimeProvider.System, (_, _, _) => Task.FromResult(true), (_, _) => { }));
        var call = Approve(rig, record, tool);
        Assert.False(call.IsCompleted);
        var card = Assert.Single(rig.Controller.Queue.Waiting);
        Assert.Equal(record.Id, card.MissionId);
        Assert.Equal(record.LeadThread, card.ThreadId);
        Assert.NotNull(card.Class);
        rig.Controller.Queue.Answer(card.Id, new DecisionAnswer(DecisionAnswerKind.Deny));
        Assert.False(await call);
        Assert.Empty(rig.Agents.Resolutions);
        Assert.Contains(rig.Logged(), e => e.Tool == tool && e.Mission == record.Id.ToString("D") && e.Agent == record.LeadThread!.Value.ToString("D"));
        Assert.Contains(rig.Logged(), e => e.Result?.StartsWith("asked:", StringComparison.Ordinal) == true);
        Assert.Contains(rig.Logged(), e => e.Result?.Contains("denied by the owner", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(rig.Logged(), e => System.Text.Json.JsonSerializer.Serialize(e).Contains("sentinel-secret-123", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_real_read_taints_the_mission_before_its_next_read_is_decided()
    {
        using var rig = new DextraTaintRig();
        var record = rig.Mission();
        BindDesktop(rig, new DesktopGate(new FakeDesktop(), TimeProvider.System, (_, _, _) => Task.FromResult(true), (_, _) => { }));
        Assert.True(await Approve(rig, record, "pc_read"));
        Assert.True(rig.Controller.Runner.TaintOf(record.Id, record.LeadThread!.Value).IsTainted);
        var next = Approve(rig, record, "pc_read");
        Assert.False(next.IsCompleted);
        var card = Assert.Single(rig.Controller.Queue.Waiting);
        Assert.Contains("untrusted", card.Why!, StringComparison.OrdinalIgnoreCase);
        rig.Controller.Queue.Answer(card.Id, new DecisionAnswer(DecisionAnswerKind.Deny));
        Assert.False(await next);
    }

    [Fact]
    public async Task Stop_withdraws_the_card_and_a_late_Yes_cannot_allow_the_call()
    {
        using var rig = new DextraTaintRig();
        var record = rig.Mission();
        BindDesktop(rig, new DesktopGate(new FakeDesktop(), TimeProvider.System, (_, _, _) => Task.FromResult(true), (_, _) => { }));
        var call = Approve(rig, record, "pc_type");
        var card = Assert.Single(rig.Controller.Queue.Waiting);
        Assert.Null(rig.Controller.Runner.Stop(record.Id));
        Assert.False(await call);
        rig.Controller.Queue.Answer(card.Id, new DecisionAnswer(DecisionAnswerKind.AllowOnce));
        Assert.False(await call);
    }

    [Fact]
    public async Task An_audit_failure_refuses_before_the_call_can_run()
    {
        using var rig = new DextraTaintRig();
        var record = rig.Mission();
        BindDesktop(rig, new DesktopGate(new FakeDesktop(), TimeProvider.System, (_, _, _) => Task.FromResult(true), (_, _) => { }));
        rig.AuditFails = _ => true;
        Assert.False(await Approve(rig, record, "pc_read"));
        Assert.Empty(rig.Agents.Resolutions);
    }

    [Fact]
    public async Task Cancellation_withdraws_the_card_and_a_late_Yes_runs_nothing()
    {
        using var rig = new DextraTaintRig();
        var record = rig.Mission();
        BindDesktop(rig, new DesktopGate(new FakeDesktop(), TimeProvider.System, (_, _, _) => Task.FromResult(true), (_, _) => { }));
        using var stop = new CancellationTokenSource();
        var call = Approve(rig, record, "pc_type", stop.Token);
        var card = Assert.Single(rig.Controller.Queue.Waiting);
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        Assert.Empty(rig.Controller.Queue.Waiting);
        rig.Controller.Queue.Answer(card.Id, new DecisionAnswer(DecisionAnswerKind.AllowOnce));
        Assert.Empty(rig.Agents.Resolutions);
    }

    [Fact]
    public async Task Stop_ends_a_locked_desktop_wait_without_raising_an_approval()
    {
        using var rig = new DextraTaintRig();
        var record = rig.Mission();
        var desktop = new DesktopGate(new FakeDesktop { IsLocked = true }, new Marrow.Testing.ManualTimeProvider(),
            (_, _, _) => Task.FromResult(true), (_, _) => { });
        BindDesktop(rig, desktop);
        var call = Approve(rig, record, "pc_type");
        Assert.True(desktop.IsWaiting(record.Id));
        Assert.Null(rig.Controller.Runner.Stop(record.Id));
        Assert.False(await call);
        Assert.False(desktop.IsWaiting(record.Id));
        Assert.Empty(rig.Controller.Queue.Waiting);
    }

    [Fact]
    public async Task Visus_reaches_approval_on_a_locked_PC_without_waiting_for_the_desktop()
    {
        using var rig = new DextraTaintRig();
        var record = rig.Mission();
        var desktop = new DesktopGate(new FakeDesktop { IsLocked = true }, new Marrow.Testing.ManualTimeProvider(),
            (_, _, _) => Task.FromResult(true), (_, _) => { });
        BindDesktop(rig, desktop);
        var call = Approve(rig, record, "web_open");
        Assert.False(desktop.IsWaiting(record.Id));
        var card = Assert.Single(rig.Controller.Queue.Waiting);
        rig.Controller.Queue.Answer(card.Id, new DecisionAnswer(DecisionAnswerKind.Deny));
        Assert.False(await call);
    }

    [Fact]
    public async Task A_locked_desktop_waits_at_DesktopGate_and_cancellation_never_reaches_approval()
    {
        using var rig = new DextraTaintRig();
        var record = rig.Mission();
        var desktop = new DesktopGate(new FakeDesktop { IsLocked = true }, new Marrow.Testing.ManualTimeProvider(),
            (_, _, _) => Task.FromResult(true), (_, _) => { });
        BindDesktop(rig, desktop);
        using var stop = new CancellationTokenSource();
        var call = Approve(rig, record, "pc_type", stop.Token);
        Assert.True(desktop.IsWaiting(record.Id));
        Assert.Empty(rig.Controller.Queue.Waiting);
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        Assert.Empty(rig.Controller.Queue.Waiting);
        Assert.False(desktop.IsWaiting(record.Id));
    }
}
