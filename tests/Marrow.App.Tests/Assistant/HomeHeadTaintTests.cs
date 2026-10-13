using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-7 (DESIGN.md rev 5, 12.4 "Taint follows text": "a merge is tainted if any input is"; ED-NEW-dextra-as7-13):
// a Hydra head of Dextra's home chat is Dextra's too, so it is followed like the home chat. Its lead wrote its brief, so
// it starts with the lead's taint; what it reads reaches its lead at once and again with its report; and the owner's own
// message clears only the chat it was typed in.
[Trait("Feature", "AS-7")]
[Trait("Decision", "ED-NEW-dextra-as7-13")]
public sealed class HomeHeadTaintTests
{
    [Fact]
    public void A_head_of_a_tainted_home_chat_starts_tainted_and_cannot_change_standing_configuration()
    {
        using var rig = new DextraTaintRig();
        var home = rig.Home();
        rig.Tool(home.Id, ToolCall.Kind.Web, "https://hostile.example/page");
        var head = rig.HeadOf(home);
        rig.Controller.OnHeadAdded(home, head);

        Assert.Equal(AssistantChatRole.Home, rig.Controller.Identify(head.Id)!.Role);
        Assert.Equal([ContentOrigin.Web], rig.Controller.HomeTaint(head.Id).Taint.Sources.Select(s => s.Origin));
        var context = rig.Controller.PolicyContext(DextraTaintRig.AsChat(head));
        Assert.True(context.Tainted);
        var duty = PolicyEngine.Decide(ActionClassifier.Classify(new ActionRequest.AssistantTool("schedule_duty"), new ClassifierContext()), context);
        Assert.Equal((Verdict.Deny, PolicyRule.NeedsUntaintedRequest), (duty.Verdict, duty.Rule));

        // A head of a clean home chat starts clean.
        var calm = rig.Home();
        var calmHead = rig.HeadOf(calm);
        rig.Controller.OnHeadAdded(calm, calmHead);
        Assert.False(rig.Controller.HomeTaint(calmHead.Id).IsTainted);
    }

    [Fact]
    public void What_a_head_reads_taints_its_lead_at_once_and_again_with_its_report_after_the_owner_cleared_the_lead()
    {
        using var rig = new DextraTaintRig();
        var home = rig.Home();
        var head = rig.HeadOf(home);
        rig.Controller.OnHeadAdded(home, head);
        Assert.False(rig.Controller.HomeTaint(head.Id).IsTainted);

        rig.Tool(head.Id, ToolCall.Kind.Web, "https://hostile.example/page");
        Assert.True(rig.Controller.HomeTaint(head.Id).IsTainted);
        Assert.Equal([ContentOrigin.Web], rig.Controller.HomeTaint(home.Id).Taint.Sources.Select(s => s.Origin));

        // The owner writes to the lead: the lead starts fresh, the head still carries what it read.
        rig.Controller.OwnerMessage(home.Id);
        Assert.False(rig.Controller.HomeTaint(home.Id).IsTainted);
        Assert.True(rig.Controller.HomeTaint(head.Id).IsTainted);

        // The head's report merges into the lead: the lead carries the head's taint again.
        rig.Controller.OnHeadFinished(head.Id, TurnStatus.Completed);
        Assert.Equal([ContentOrigin.Web], rig.Controller.HomeTaint(home.Id).Taint.Sources.Select(s => s.Origin));

        // The owner's own message in the head clears the head alone.
        rig.Controller.OwnerMessage(head.Id);
        Assert.False(rig.Controller.HomeTaint(head.Id).IsTainted);
        Assert.True(rig.Controller.HomeTaint(home.Id).IsTainted);
    }

    [Fact]
    public void A_head_of_a_chat_that_is_no_assistants_is_not_followed_and_taints_no_lead()
    {
        using var rig = new DextraTaintRig();
        var plain = MissionRig.NewChat(() => new ChatThread(rig.Project.Id, Marrow.Core.Models.ProviderKind.Claude, "model-a", null, RuntimeMode.Supervised));
        rig.Chats[plain.Id] = plain;
        var head = rig.HeadOf(plain);
        rig.Controller.OnHeadAdded(plain, head);
        rig.Tool(head.Id, ToolCall.Kind.Web, "https://hostile.example/page");
        rig.Controller.OnHeadFinished(head.Id, TurnStatus.Completed);
        Assert.False(rig.Controller.HomeTaint(head.Id).IsTainted);
        Assert.False(rig.Controller.HomeTaint(plain.Id).IsTainted);
        Assert.Empty(rig.Saved);
    }
}
