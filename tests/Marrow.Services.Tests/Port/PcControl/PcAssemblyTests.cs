// PORT-ONLY: Tactus (ED-NEW-pcctl-40; DESIGN.md 2.7, 2.10, 3.3, 4.1, 4.6, 10.2). The one composition of the engine's parts,
// on fake backends: every chat shares the run control, lanes, Pause, the ask broker and the ladder, and has its own audit
// log; an approval waits in the ask broker until the pill or the chat answers it; a standing grant from the grant store
// skips the ask; the real mouse and keyboard stay off by default; the master switch going off stops everything; and the
// assembly disposes what it owns. No real window, input or capture.
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-40")]
public sealed class PcAssemblyTests
{
    private const string Look = "{\"window\":\"w:1a\",\"screenshot\":\"no\"}";

    [Fact]
    public void Every_chat_gets_its_own_audit_log_over_the_shared_parts()
    {
        using var rig = new PcAssemblyRig();
        var assembly = rig.Build();

        var first = assembly.PartsFor("chat-a");
        var again = assembly.PartsFor("chat-a");
        var other = assembly.PartsFor("chat-b");

        Assert.Same(first, again);
        Assert.NotSame(first, other);
        Assert.NotSame(first.Audit, other.Audit);
        Assert.Same(rig.Audits["chat-a"], first.Audit);
        Assert.Same(rig.Audits["chat-b"], other.Audit);
        Assert.Same(first.Control, other.Control);
        Assert.Same(first.Lanes, other.Lanes);
        Assert.Same(first.Pause, other.Pause);
        Assert.Same(first.Asks, other.Asks);
        Assert.Same(first.Ownership, other.Ownership);
        Assert.Same(first.Ladder, other.Ladder);
        Assert.Same(first.Lease, other.Lease);
        Assert.Same(rig.StopKey, first.StopKey);
        Assert.Same(assembly.Pointer, first.Pointer);
        Assert.Same(rig.Backends.Capture, first.Capture);
        Assert.DoesNotContain(rig.Audits.Values, audit => ReferenceEquals(audit, assembly.Shared.Audit));
        Assert.True(first.Settings().Enabled);
    }

    [Fact]
    public async Task An_approval_waits_in_the_ask_broker_until_the_pill_answers_and_the_call_is_logged_for_its_chat()
    {
        using var rig = new PcAssemblyRig();
        rig.Notepad();
        var assembly = rig.Build();
        using var engine = new PcControlEngine(assembly.PartsFor("chat-a"), new PcCaller("chat-a", () => true, true));

        var call = PcEngineHarness.Call(engine, "pc_observe", Look);
        await PcTestWait.Until(() => assembly.Asks.Count > 0);
        var ask = assembly.Asks.Pending[0];

        Assert.Equal(PcAskKind.Approval, ask.Kind);
        Assert.Equal("chat-a", ask.Thread);
        Assert.False(call.IsCompleted);
        Assert.Equal(PcResolve.Done, assembly.Asks.Resolve(ask.Id, ask.Revision, PcApproval.Chat, PcAnswerSource.Pill));
        var result = await call.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(result.Ok, result.Render());
        Assert.Contains(rig.Audits["chat-a"].Entries, entry => entry.Tool == "pc_observe" && entry.Result == "ok");
        Assert.False(rig.Audits.ContainsKey("chat-b"));
    }

    [Fact]
    public async Task A_standing_grant_from_the_grant_store_skips_the_ask()
    {
        using var rig = new PcAssemblyRig();
        rig.Notepad();
        var asked = new List<(string, PcCapability)>();
        var assembly = rig.Build(new PcSurface
        {
            Standing = (app, capability) =>
            {
                lock (asked) asked.Add((app.ExeName, capability));
                return app.ExeName == "notepad.exe" ? PcApproval.Always : null;
            },
        });
        using var engine = new PcControlEngine(assembly.PartsFor("chat-a"), new PcCaller("chat-a", () => true, true));

        var result = await PcEngineHarness.Call(engine, "pc_observe", Look).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(result.Ok, result.Render());
        Assert.Equal(0, assembly.Asks.Count);
        Assert.Contains(("notepad.exe", PcCapability.See), asked);
    }

    [Fact]
    public async Task The_real_mouse_and_keyboard_stay_off_by_default()
    {
        using var rig = new PcAssemblyRig();
        rig.Notepad();
        var assembly = rig.Build(new PcSurface { Standing = (_, _) => PcApproval.Always });
        using var engine = new PcControlEngine(assembly.PartsFor("chat-a"), new PcCaller("chat-a", () => true, true));
        await PcEngineHarness.Call(engine, "pc_observe", Look);

        var unknown = await PcEngineHarness.Call(engine, "pc_key", "{\"window\":\"w:1a\",\"keys\":\"ctrl+shift+q\"}");

        Assert.False(PcSettings.Default.RealInput);
        Assert.Equal(PcErrorCode.NeedsRealInput, unknown.Error);
        Assert.Contains(PcRealInputReason.Off, unknown.Reason, StringComparison.Ordinal);
        Assert.Equal(0, rig.Hold.SwallowedCount);
    }

    [Fact]
    public void The_switch_going_off_stops_every_chat_and_the_pill_stop_interrupts_the_turns()
    {
        using var rig = new PcAssemblyRig();
        var assembly = rig.Build();
        var stops = new List<(PcScope, PcStopSource)>();
        assembly.Control.Stopped += (scope, source) => stops.Add((scope, source));

        assembly.SwitchedOff();
        assembly.Stop.PillStop();

        Assert.Equal((PcScope.All, PcStopSource.Settings), stops[0]);
        Assert.Equal(PcStopPath.PillStop, assembly.Stop.Last!.Value.Path);
        Assert.Equal(1, rig.InterruptCalls);
    }

    [Fact]
    public void A_window_counts_as_tactus_while_a_chat_holds_it()
    {
        using var rig = new PcAssemblyRig();
        var assembly = rig.Build();

        Assert.False(assembly.IsTactusWindow(0x1a));
        Assert.Null(assembly.Ownership.Claim(0x1a, "chat-a", "the lead chat"));
        Assert.True(assembly.IsTactusWindow(0x1a));
        Assert.False(assembly.IsTactusWindow(0));
        assembly.Ownership.Release("chat-a");
        Assert.False(assembly.IsTactusWindow(0x1a));
    }

    [Fact]
    public void Disposing_the_assembly_disposes_what_it_owns_once()
    {
        var rig = new PcAssemblyRig();
        rig.Build();

        rig.Dispose();
        rig.Dispose();

        Assert.Equal(1, rig.Owned.Count);
    }

    [Fact]
    public void Heads_are_attended_only_with_recent_input_and_the_lead_window_open_and_the_lead_chat_always()
    {
        Assert.True(PcAttendance.Attended(head: false, idleMs: long.MaxValue, leadMinimized: true));
        Assert.True(PcAttendance.Attended(head: true, idleMs: 0, leadMinimized: false));
        Assert.True(PcAttendance.Attended(head: true, idleMs: PcAttendance.AwayAfterMs, leadMinimized: false));
        Assert.False(PcAttendance.Attended(head: true, idleMs: PcAttendance.AwayAfterMs + 1, leadMinimized: false));
        Assert.False(PcAttendance.Attended(head: true, idleMs: 1000, leadMinimized: true));
        Assert.False(PcAttendance.Attended(head: true, idleMs: -1, leadMinimized: false));
    }
}
