using Marrow.App.Port.Settings;
using Marrow.Core.Models;
using Marrow.Core.Models.ComposerLights;

namespace Marrow.App.Tests.Port.Settings;

// customization-SPEC CZ-4.5.3 (the preview maker is session state) and CZ-4.6 (the preview phase).
public sealed class PreviewSessionTests
{
    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-T34
    [Trait("Spec", "CZ-T34")]
    public void The_session_opens_on_the_default_providers_maker_at_rest_and_each_session_opens_afresh()
    {
        var session = new PreviewSession(ProviderKind.Codex);
        Assert.Equal("OpenAI", session.Maker);
        Assert.Equal(PreviewState.Rest, session.State);
        session.Step(1);
        session.Step(1);
        Assert.Equal("DeepSeek", session.Maker);
        // not persisted: a page opened later starts from the default provider's maker again
        Assert.Equal("OpenAI", new PreviewSession(ProviderKind.Codex).Maker);
        Assert.Equal("Claude", new PreviewSession(ProviderKind.OpenRouter).Maker); // Purple has no maker
    }

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-T32, CZ-T34: one change event per change, none for the same value
    public void A_change_raises_one_event_and_the_same_value_raises_none()
    {
        var session = new PreviewSession(ProviderKind.Claude);
        var changes = 0;
        session.Changed += () => changes++;
        session.Pick("Claude");
        session.Pick(PreviewState.Rest);
        Assert.Equal(0, changes);
        session.Pick("Zai");
        Assert.Equal(1, changes);
        session.Step(-1);
        Assert.Equal(("OpenCodeGo", 2), (session.Maker, changes));
        session.Pick(PreviewState.Typing);
        Assert.Equal(3, changes);
        Assert.Throws<ArgumentOutOfRangeException>(() => session.Pick("Nobody"));
        Assert.Equal(3, changes);
    }

    [Fact]
    [Trait("Decision", "ED-251")] // customization-SPEC CZ-T35, CZ-4.6
    [Trait("Spec", "CZ-T35")]
    public void The_phase_follows_the_chrome_controls_pick_and_touching_the_working_dots_picks_Running()
    {
        var session = new PreviewSession(ProviderKind.Claude);
        Assert.Equal(ComposerLightPhase.Rest, session.Phase);
        session.Pick(PreviewState.Typing);
        Assert.Equal(ComposerLightPhase.Typing, session.Phase);
        session.Pick(PreviewState.Running);
        Assert.Equal(ComposerLightPhase.Running, session.Phase);
        session.Pick(PreviewState.Rest);
        Assert.Equal(ComposerLightPhase.Rest, session.Phase);
        session.WorkingDotsTouched();
        Assert.Equal(PreviewState.Running, session.State);
        Assert.Equal(ComposerLightPhase.Running, session.Phase);
    }
}
