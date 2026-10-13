using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Port.Assistant;

[Trait("Decision", "ED-NEW-dextra-open-13")]
public sealed class DextraTriageFallbackTests
{
    private static readonly BrainModel Unavailable = new("codex", "unavailable", "codex:default", "Codex")
        { Usable = false, InputPerMillion = 1, OutputPerMillion = 1 };

    [Fact]
    public void An_unusable_cheap_brain_cannot_override_a_usable_restricted_answer_model()
    {
        var available = new BrainModel("claude", "available", "claude:default", "Claude")
            { InputPerMillion = 2, OutputPerMillion = 2 };
        var pick = BrainPicker.Pick(BrainJob.Triage, [Unavailable, available], Unavailable);
        Assert.Same(available, pick.Model);
        Assert.False(pick.IsBrain);
    }

    [Fact]
    public void An_unusable_brain_is_never_the_triage_fallback()
    {
        Assert.Null(BrainPicker.Pick(BrainJob.Triage, [], Unavailable).Model);
    }

    [Fact]
    public void The_strong_brain_still_has_its_approved_planning_role()
    {
        Assert.Same(Unavailable, BrainPicker.Pick(BrainJob.Planning, [], Unavailable).Model);
    }
}
