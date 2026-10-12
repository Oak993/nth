using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0145, ED-NEW-pcctl-25; DESIGN.md 10.4 "Reply instead opens the mini composer. The text
// goes to the model as the reason, with Don't allow"). The engine's not_approved result carries what the user said, and
// a See-only answer to a step that needs Use says so in the user's words of the card.
[Trait("Decision", "ED-NEW-pcctl-25")]
public sealed class PcApprovalReasonTests
{
    private const string Look = "{\"window\":\"w:1a\",\"screenshot\":\"no\"}";

    [Fact]
    public async Task A_denial_carries_the_users_reason()
    {
        var harness = new PcEngineHarness();
        harness.Notepad();
        var engine = harness.Engine();
        harness.Approver.Answer = PcApproval.Denied;
        harness.Approver.Said = "Use the web version";

        var result = await PcEngineHarness.Call(engine, "pc_observe", Look);

        Assert.Equal(PcErrorCode.NotApproved, result.Error);
        Assert.Equal("the user did not allow Tactus to see this app. The user said: Use the web version", result.Reason);
    }

    [Fact]
    public async Task A_see_only_answer_says_so()
    {
        var harness = new PcEngineHarness();
        harness.Notepad();
        var engine = harness.Engine();
        Assert.True((await PcEngineHarness.Call(engine, "pc_observe", Look)).Ok);
        harness.Approver.Answer = PcApproval.Denied;
        harness.Approver.Said = PcBrokerApprover.SeeOnly;

        var result = await PcEngineHarness.Call(engine, "pc_click", "{\"window\":\"w:1a\",\"element\":4}");

        Assert.Equal(PcErrorCode.NotApproved, result.Error);
        Assert.Equal(PcBrokerApprover.SeeOnly, result.Reason);
    }

    [Fact]
    public async Task A_plain_denial_is_unchanged()
    {
        var harness = new PcEngineHarness();
        harness.Notepad();
        var engine = harness.Engine();
        harness.Approver.Answer = PcApproval.Denied;

        var result = await PcEngineHarness.Call(engine, "pc_observe", Look);

        Assert.Equal("the user did not allow Tactus to see this app", result.Reason);
    }
}
