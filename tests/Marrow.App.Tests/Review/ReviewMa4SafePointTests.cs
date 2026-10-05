using Marrow.Core.Models;
using Marrow.Services.Providers;
using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Review;

// Independent review of codex/ma-4 (finding MA4-2): the Handshake (NOTES: starts near the limit while the reply is still running; the switch happens at a safe point BETWEEN STEPS of
// a running turn) needs a summary of a thread that is mid-turn but between steps. SaveHandoff refuses unless the whole runtime is Idle, so the summary can only be taken after the turn
// ends, which is the hard-stop case the Handshake exists to avoid.
public sealed class ReviewMa4SafePointTests
{
    [Fact]
    [Trait("Feature", "MA-4")]
    [Trait("Review", "MA-4")]
    [Trait("Rule", "R-TEST-4")]
    public void REVIEW_a_handoff_can_be_saved_between_two_steps_of_a_running_turn()
    {
        using var rig = new ThreadRuntimeTurnRig();
        rig.Send("Do two things.");
        rig.H.Main.OnMain(() => rig.Session.Emit(new ProviderEvent.MessageCompleted("assistant-1", "First thing done.")));
        // The turn is still running (no TurnCompleted), no tool is open, no approval or question is pending: a safe point.
        var result = rig.H.Main.OnMain(() => Record.Exception(() => rig.Runtime.SaveHandoff("diff")));
        Assert.Null(result);
    }
}
