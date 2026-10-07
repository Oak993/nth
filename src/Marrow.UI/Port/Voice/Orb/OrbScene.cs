// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092; first-release voice typing, VT-8, voice-SPEC V-11): the orb's per-frame state: motion, the two orbs and their crossfade (V-11.2 to V-11.5).
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Orb;

namespace Marrow.UI.Port.Voice.Orb;

/// <summary>
/// One <see cref="OrbMotion"/> drives both orbs (the state is the composer's, not the preset's); each orb has its own <see cref="VoiceOrbRuntime"/>. The view is
/// <see cref="ViewPx"/> square with the ball at the slot diameter inside it (V-11.3). On a change of the chosen orb the new one is made with the old one's motion
/// phase (V-11.5: "the new orb takes the old one's phase") and the two crossfade. An orb with no mix left is not stepped.
/// </summary>
public sealed class OrbScene
{
    /// <summary>The view's edge in view pixels (V-11.3: 72 x 72, room for the glow and the pop).</summary>
    public const double ViewPx = 72;

    private readonly OrbMotion motion;
    private readonly OrbCrossfade fade;
    private readonly VoiceOrbRuntime?[] runtimes = new VoiceOrbRuntime?[2];
    private readonly double[] opacities = new double[2];
    private double? lastNow;
    private double dpr = 1;
    private double ball;
    private bool dark;
    private double viewSize = ViewPx;
    private bool laidOut;

    public OrbScene(VoiceOrbKind kind, double slotDiameter, bool dark)
    {
        motion = new OrbMotion(slotDiameter);
        fade = new OrbCrossfade(kind);
        this.dark = dark;
        ball = slotDiameter;
    }

    public OrbMotion Motion => motion;

    public OrbCrossfade Fade => fade;

    /// <summary>The runtime of an orb, or null while it has none (it is made when first chosen or swapped in).</summary>
    public VoiceOrbRuntime? Runtime(VoiceOrbKind kind) => runtimes[(int)kind];

    /// <summary>The opacity an orb is drawn with this frame (0: not drawn).</summary>
    public double Opacity(VoiceOrbKind kind) => opacities[(int)kind];

    /// <summary>The device pixels per view pixel the scene lays the orbs out for.</summary>
    public double Dpr => dpr;

    /// <summary>Steps everything to <paramref name="now"/> (seconds) and prepares each visible orb's frame for <paramref name="devicePixelRatio"/>.</summary>
    public void Advance(double now, in OrbSceneInput input, double devicePixelRatio)
    {
        // A delayed composition sample cannot replay behind a frame already prepared by the injected clock.
        if (lastNow is { } previousTime && now < previousTime) return;
        var dt = lastNow is { } last ? Math.Max(0, now - last) : 0;
        lastNow = now;
        motion.Step(now, dt, new OrbMotionInput(input.Mode, input.Hovered, input.Pressed, input.Bands, input.SlotDiameter, input.ReduceMotion));

        var previous = fade.Chosen;
        if (fade.Step(input.Kind, dt, input.ReduceMotion)) SwapIn(previous, input.Kind);
        EnsureRuntime(input.Kind);

        var layoutChanged = !laidOut || devicePixelRatio != dpr || motion.Diameter != ball || viewSize != input.ViewSize;
        viewSize = input.ViewSize;
        dpr = devicePixelRatio;
        ball = motion.Diameter;
        laidOut = true;
        var darkChanged = input.Dark != dark;
        dark = input.Dark;

        for (var i = 0; i < 2; i++)
        {
            var kind = (VoiceOrbKind)i;
            var runtime = runtimes[i];
            if (runtime is null || !fade.Visible(kind))
            {
                opacities[i] = 0;
                continue;
            }
            if (layoutChanged) runtime.Resize(viewSize, ball, dpr);
            if (darkChanged) runtime.SetDark(dark);
            runtime.Frame(now, input.TileGlow is { } glow
                ? new OrbFrameState(Act: 0, Speed: 1, Scale: 1, Glow: glow, ReduceMotion: input.ReduceMotion)
                : motion.FrameState(input.Mode, input.Bands, input.ReduceMotion));
            opacities[i] = fade.Mix(kind);
        }
    }

    private void EnsureRuntime(VoiceOrbKind kind)
    {
        if (runtimes[(int)kind] is null) runtimes[(int)kind] = Make(kind, 0);
    }

    // the new orb takes the old one's phase (voice-composer.js:369-372, 763-768)
    private void SwapIn(VoiceOrbKind from, VoiceOrbKind to)
    {
        var phase = runtimes[(int)from]?.Phase ?? 0;
        runtimes[(int)to] = Make(to, phase);
    }

    private VoiceOrbRuntime Make(VoiceOrbKind kind, double phase) =>
        new(kind, new VoiceOrbRuntimeOptions(viewSize, ball, dpr, dark, Phase: phase));
}
