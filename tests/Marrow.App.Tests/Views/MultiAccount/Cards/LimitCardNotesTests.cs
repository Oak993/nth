using Avalonia.Controls;
using Marrow.App.Port.MultiAccount.Cards;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Accounts;
using Marrow.Core.Port.MultiAccount.Settings;

namespace Marrow.App.Tests.Views.MultiAccount.Cards;

// PORT-ONLY: MA-3b-12, limit :3999-4100, real handed sheet :3010-3023.
[Collection("Headless")]
public sealed class LimitCardNotesTests(HeadlessSession headless)
{
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Wait_leaves_the_exact_note_and_switch_now_uses_the_thread_action()
    {
        var f = FirstTimeCardTests.Make(false); MultiAccountCardRows? host = null; var switches = 0;
        MultiAccountHarness.Run(headless, f.Rig, () => { var body = new StackPanel(); host = MultiAccountCardRows.Append(body, f.Accounts.ThreadId, f.Cards, () => f.Accounts.Accounts, 12, new MultiAccountCardContext(() => { switches++; return Task.CompletedTask; }, null, TimeZoneInfo.Utc)); return body; }, h =>
        {
            Assert.Contains("Claude Max 1 reached its 5-hour limit · resets 3:40 PM", h.Words());
            Assert.True(h.Press("limit-action-wait")); h.Settle();
            Assert.Contains("Waiting for Claude Max 1 · resets 3:40 PM · your message sends then", h.Words());
            Assert.True(h.Press("limit-switch-now")); Assert.Equal(1, switches);
            Assert.DoesNotContain("limit-action-wait", h.All().Select(c => Avalonia.Automation.AutomationProperties.GetAutomationId(c)));
        });
    }
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Continue_note_opens_the_record_from_the_seam_and_not_the_settings_example()
    {
        var f = FirstTimeCardTests.Make(false, DecisionKind.OnLimit, DecisionKind.Bring);
        var record = new HandedOverExample("What was handed over", "to Claude Max 2", "The recorded handoff", null, [new("Your messages", "3", "Kept word for word", true)], "handoff-actual.md", "The full conversation goes with it", null);
        var reads = 0;
        MultiAccountHarness.Run(headless, f.Rig, () => { var body = new StackPanel(); MultiAccountCardRows.Append(body, f.Accounts.ThreadId, f.Cards, () => f.Accounts.Accounts, 12, new MultiAccountCardContext(null, () => { reads++; return record; }, TimeZoneInfo.Utc)); return body; }, h =>
        {
            Assert.True(h.Press("limit-action-switch")); h.Settle();
            Assert.Contains("Continued on Claude Max 2 · nth’s summary", h.Words());
            Assert.True(h.Press("limit-handed-over")); h.Settle(); Assert.Equal(1, reads);
            Assert.Contains("The recorded handoff", h.Words()); Assert.Contains("handoff-actual.md", h.Words()); Assert.DoesNotContain("handoff-thread-0412.md", h.Words());
            Assert.True(h.Press("handed-close")); h.Settle(); Assert.Null(h.TryFind("handed-sheet"));
        });
    }
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Waiting_phase_event_renders_a_note_and_idle_clears_it_without_a_decision_request()
    {
        var f = FirstTimeCardTests.Make(false); f.Cards.Raise(f.State with { Request = null, To = HandshakePhase.Waiting, ResumesAt = f.State.Request!.ResetsAt });
        MultiAccountHarness.Run(headless, f.Rig, () => { var body = new StackPanel(); MultiAccountCardRows.Append(body, f.Accounts.ThreadId, f.Cards, () => f.Accounts.Accounts, 12, new MultiAccountCardContext(null, null, TimeZoneInfo.Utc)); return body; }, h =>
        {
            Assert.Contains("Waiting for Claude Max 1 · resets 3:40 PM · your message sends then", h.Words());
            f.Cards.Raise(f.State with { Request = null, To = HandshakePhase.Idle }); h.Settle(); Assert.Empty(h.Words());
        });
    }
}
