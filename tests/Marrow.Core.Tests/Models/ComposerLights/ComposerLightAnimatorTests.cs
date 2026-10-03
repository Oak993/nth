using Marrow.Core.Models.ComposerLights;

namespace Marrow.Core.Tests.Models.ComposerLights;

// DEVIATION OD-0089 (owner-ruled addition; ED-251): how one composer's light changes over time, as the playground's board
// runs it (gen.py): registered properties transition .6 s on cubic-bezier(.25, .1, .25, 1) (the aura's intensity --au-i
// .9 s); a light group fades in over .6 s and out over .5 s (.g-aura/.g-beam); Active's --bb-act eases .6 s on, .5 s off; a
// structural change of Border beam cross-fades its slots over .6 s (beamSlots(), .bbs); under prefers-reduced-motion every
// transition is none and nothing moves (tick() k = 0). OD-0076: nothing changes by a cut.
public sealed class ComposerLightAnimatorTests
{
    private static ComposerLightAnimator Make(ComposerLightSettings? s = null, bool reduce = false, ComposerLightPhase phase = ComposerLightPhase.Rest) =>
        new(s ?? ComposerLightSettings.Default, phase, dark: true, 820, 52, reduce);

    [Fact]
    [Trait("Decision", "ED-251")] // CSS Transitions: ease (.25, .1, .25, 1); a reversal runs for the shortened time
    public void Transitions_ease_and_reverse_in_proportion()
    {
        Assert.Equal(0.8024033877399112, CssTiming.Ease(0.5), 6);
        var t = new CssTransition(0);
        t.Set(1, 10, 0.6);
        Assert.True(t.IsRunning(10.3));
        Assert.Equal(0.8024033877399112, t.Value(10.3), 6);
        Assert.Equal(1, t.Value(10.6), 12);
        t.Set(0, 10.3, 0.6);                       // back to where it came from: a reversal
        Assert.Equal(0.8024033877399112, t.Value(10.3), 6);
        Assert.True(t.IsRunning(10.3 + 0.6 * 0.80 - 0.01));
        Assert.False(t.IsRunning(10.3 + 0.6 * 0.81));
        var u = new CssTransition(5);
        u.Set(7, 0, 0);
        Assert.Equal(7, u.Value(0));
    }

    [Fact]
    [Trait("Decision", "ED-251")] // gen.py .g-aura/.g-beam: opacity .6 s ease in, .5 s ease out; Off draws nothing once the fade is done
    public void A_change_of_style_fades_one_light_out_and_the_other_in()
    {
        var a = Make();
        a.Advance(0);
        Assert.Equal(1, a.DrawnCount);
        Assert.False(a.AuraVisible);
        a.SetSettings(ComposerLightSettings.Default with { Style = ComposerLightStyle.Aura }, 1);
        a.Advance(1.25);
        Assert.Equal(CssTiming.Ease(0.25 / 0.6), a.Aura.GroupAlpha, 9);
        Assert.Equal(1 - CssTiming.Ease(0.25 / 0.5), a.Slots[a.Drawn[0]].Alpha, 9);
        a.Advance(1.55);
        Assert.Equal(0, a.DrawnCount);                // the beam has faded out after .5 s
        Assert.True(a.Aura.GroupAlpha < 1);
        a.Advance(1.6);
        Assert.Equal(1, a.Aura.GroupAlpha, 12);
        a.SetSettings(ComposerLightSettings.Default with { Style = ComposerLightStyle.Off }, 2);
        Assert.True(a.Advance(2.3));                  // still fading: frames are wanted
        Assert.False(a.Advance(2.6));                 // nothing left to draw: no more frames
        Assert.False(a.AuraVisible);
        Assert.Equal(0, a.DrawnCount);
    }

    [Fact]
    [Trait("Decision", "ED-251")] // logic.js beamSlots(): a structural change goes into another slot and the two cross-fade over .6 s
    public void A_change_of_type_cross_fades_between_slots()
    {
        var a = Make();
        a.Advance(0);
        var first = a.CurrentSlot;
        a.SetSettings(ComposerLightSettings.Default with { Beam = BorderBeamOptions.Default.WithType(BeamType.Line) }, 1);
        var next = a.CurrentSlot;
        Assert.NotEqual(first, next);
        Assert.Equal(BeamType.Line, a.SlotKey(next)!.Value.Type);
        a.Advance(1.3);
        Assert.Equal(2, a.DrawnCount);
        Assert.Equal(1 - CssTiming.Ease(0.5), a.SlotOpacity(first, 1.3), 9);
        Assert.Equal(CssTiming.Ease(0.5), a.SlotOpacity(next, 1.3), 9);
        // the continuous values (Line's opacity products) transition over the same .6 s, not by a cut
        Assert.Equal(0.26 + (1.14 - 0.26) * CssTiming.Ease(0.5), a.Values.So, 9);
        a.Advance(1.7);
        Assert.Equal(1, a.DrawnCount);
        Assert.Equal(next, a.Drawn[0]);
        // coming back while the old slot still fades reuses it
        a.SetSettings(ComposerLightSettings.Default, 1.75);
        Assert.Equal(first, a.CurrentSlot);
    }

    [Fact]
    [Trait("Decision", "ED-251")] // gen.py --bb-act: .5 s off, .6 s on; Active off draws nothing and asks for no frames once faded
    public void Active_fades_the_beam_out_and_in()
    {
        var a = Make();
        a.Advance(0);
        a.SetSettings(ComposerLightSettings.Default with { Beam = BorderBeamOptions.Default with { Active = false } }, 1);
        a.Advance(1.25);
        Assert.Equal(1 - CssTiming.Ease(0.5), a.Slots[a.Drawn[0]].Alpha, 9);
        Assert.False(a.Advance(1.6));
        Assert.Equal(0, a.DrawnCount);
        a.SetSettings(ComposerLightSettings.Default, 2);
        a.Advance(2.3);
        Assert.Equal(CssTiming.Ease(0.5), a.Slots[a.Drawn[0]].Alpha, 9);
    }

    [Fact]
    [Trait("Decision", "ED-251")] // logic.js auraVars(): --au-i = a[st] / 100, transitioned over .9 s (gen.py); opacity i tm, rim i ro tm, inside i ro tm .75
    public void The_uiverse_light_follows_rest_typing_and_running()
    {
        var a = Make(ComposerLightSettings.Default with { Style = ComposerLightStyle.Aura });
        a.Advance(0);
        Assert.Equal(0.40, a.Aura.HaloAlpha, 12);
        Assert.Equal(0.40, a.Aura.RimAlpha, 12);
        Assert.Equal(0.30, a.Aura.InnerAlpha, 12);
        a.SetPhase(ComposerLightPhase.Running, 1);
        a.Advance(1.45);
        Assert.Equal(0.40 + 0.35 * CssTiming.Ease(0.5), a.Aura.HaloAlpha, 9);
        a.Advance(1.95);
        Assert.Equal(0.75, a.Aura.HaloAlpha, 12);
        a.SetDark(false, 2);                            // --au-tm 1 -> 1.2 over .6 s; opacity clamps at 1
        a.Advance(2.7);
        Assert.Equal(0.9, a.Aura.HaloAlpha, 9);
        Assert.Equal(0.675, a.Aura.InnerAlpha, 9);
    }

    [Fact]
    [Trait("Decision", "ED-251")] // logic.js beamEff(): "Follow the state" scales the strength by .55 at rest, .8 typing, 1 running; off, the beam ignores the state
    public void Border_beam_follows_the_state_only_when_asked()
    {
        var plain = Make(phase: ComposerLightPhase.Rest);
        plain.SetPhase(ComposerLightPhase.Running, 0);
        plain.Advance(1);
        Assert.Equal(0.26, plain.Values.So, 12);
        var follow = Make(ComposerLightSettings.Default with { Beam = BorderBeamOptions.Default with { Respond = true } });
        follow.Advance(0);
        Assert.Equal(0.26 * 0.55, follow.Values.So, 12);
        follow.SetPhase(ComposerLightPhase.Typing, 1);
        follow.Advance(1.3);
        Assert.Equal(0.26 * 0.55 + 0.26 * 0.25 * CssTiming.Ease(0.5), follow.Values.So, 9);
        follow.Advance(2);
        Assert.Equal(0.26 * 0.8, follow.Values.So, 12);
    }

    [Fact]
    [Trait("Decision", "ED-251")] // gen.py @media (prefers-reduced-motion): transition none; logic.js tick(): k = 0
    public void Reduce_motion_draws_one_still_and_applies_changes_at_once()
    {
        var a = Make(reduce: true);
        Assert.False(a.Advance(0));
        Assert.Equal(1, a.DrawnCount);
        Assert.Equal(0, a.Slots[a.Drawn[0]].Phase);
        a.Advance(5);
        Assert.Equal(0, a.Slots[a.Drawn[0]].Phase);       // the beam never turns
        a.SetSettings(ComposerLightSettings.Default with { Style = ComposerLightStyle.Aura }, 6);
        a.Advance(6);
        Assert.Equal(1, a.Aura.GroupAlpha);
        Assert.Equal(0, a.DrawnCount);
        Assert.Equal(10, a.Aura.Angle, 4);
        // switching reduce motion on mid-fade lands the fade at once
        var b = Make();
        b.Advance(0);
        b.SetSettings(ComposerLightSettings.Default with { Style = ComposerLightStyle.Aura }, 1);
        b.SetReduceMotion(true, 1.1);
        b.Advance(1.1);
        Assert.Equal(1, b.Aura.GroupAlpha);
        Assert.Equal(0, b.DrawnCount);
    }

    [Fact]
    [Trait("Decision", "ED-251")] // the default beam turns once a duration (3.69 s on 820 x 52) at full speed
    public void The_default_beam_turns_once_every_auto_duration()
    {
        var a = Make();
        a.Advance(0);
        for (var t = 1; t <= 600; t++) a.Advance(t / 60.0);
        var dur = BorderBeamMath.Duration(BorderBeamOptions.Default, 820, 52, 22);
        // the eased start (k rises from 1, held: no start delay) so ten seconds is 10 / dur turns
        Assert.Equal(BorderBeamMath.Frac(10 / dur), a.Slots[a.Drawn[0]].Phase, 6);
    }

    [Theory]
    [Trait("Decision", "ED-251")] // OD-0089: a bounded per-frame cost; a frame allocates nothing (only a change of settings does)
    [InlineData(BeamType.Large)]
    [InlineData(BeamType.Small)]
    [InlineData(BeamType.Line)]
    [InlineData(BeamType.PulseInner)]
    [InlineData(BeamType.PulseOutside)]
    public void A_frame_allocates_nothing(BeamType type)
    {
        var a = Make(ComposerLightSettings.Default with { Beam = BorderBeamOptions.Default.WithType(type) });
        for (var t = 0; t < 10; t++) a.Advance(t / 60.0);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var t = 10; t < 130; t++) a.Advance(t / 60.0);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        var aura = Make(ComposerLightSettings.Default with { Style = ComposerLightStyle.Aura });
        for (var t = 0; t < 10; t++) aura.Advance(t / 60.0);
        before = GC.GetAllocatedBytesForCurrentThread();
        for (var t = 10; t < 130; t++) aura.Advance(t / 60.0);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    [Trait("Decision", "ED-251")] // the composer growing (a second line of text) is layout: the light follows its size at once
    public void A_growing_composer_is_followed_at_once()
    {
        var a = Make(ComposerLightSettings.Default with { Style = ComposerLightStyle.Aura });
        a.Advance(0);
        Assert.Equal(4 * (52 + 4) / 400.0, a.Aura.Sy, 12);
        a.SetSize(820, 72, 1);
        a.Advance(1.01);
        Assert.Equal(72, a.Aura.H);
        Assert.Equal(4 * (72 + 4) / 400.0, a.Aura.Sy, 12);
        Assert.Equal(4 * (72 + 3) / 400.0, a.Aura.Rsy, 12);
    }

    [Theory]
    [Trait("Decision", "ED-251")] // REVIEW: a ComposerLightView sends its first snapshot on attach, before layout (0 x 0); Line's fitted spikes (beam.js bbSpikes: step .14 x 366 x H/42) and Auto duration (bbFitDur: W / (H / 42) / 366) must not hang, throw or poison the clock, and the light turns once the composer has its size
    [InlineData(BeamType.Line)]
    [InlineData(BeamType.Large)]
    public void A_light_that_starts_before_layout_turns_once_laid_out(BeamType type)
    {
        var s = ComposerLightSettings.Default with { Beam = BorderBeamOptions.Default.WithType(type) };
        var a = new ComposerLightAnimator(s, ComposerLightPhase.Rest, dark: true, 0, 0, reduceMotion: false);
        a.Advance(0);
        a.SetSize(820, 52, 0.016);
        for (var t = 1; t <= 60; t++) a.Advance(t / 60.0);
        Assert.True(double.IsFinite(a.Clock.BeamAngle) && double.IsFinite(a.Clock.LineX) && double.IsFinite(a.Clock.LineEdge), $"angle {a.Clock.BeamAngle}, Line x {a.Clock.LineX}");
        // switching to another type later must find a live clock too
        a.SetSettings(ComposerLightSettings.Default, 1.01);
        a.Advance(1.02);
        Assert.True(double.IsFinite(a.Slots[a.CurrentSlot].Phase), $"phase {a.Slots[a.CurrentSlot].Phase}");
    }
}
