// PORT-ONLY, owner-ruled addition (OD-0097, second amendment; first-release voice typing, VT-9, voice-SPEC V-13.2, V-13.3): the glow's per-frame state on the render thread.
using Marrow.Core.Models.ComposerLights;
using Marrow.Core.Port.Voice.Glow;

namespace Marrow.UI.Port.Voice.Glow;

/// <summary>
/// What <c>mountVoiceGlow</c> keeps for one glow (voice-glow.js:1018-1150): the fade of its wrapper (<c>--vb-opacity</c>: <c>vb-fade-in</c> 0.6 s and <c>vb-fade-out</c> 0.5 s, CSS
/// <c>ease</c>), the driver (VT-4's <see cref="VoiceGlowDriver"/>, registered only while the glow is active or fading) and the look for the theme and the processing flag.
/// The driver is stepped once per frame the host draws, with the real gap since the last frame (the driver caps it at 0.05 s); a glow that comes back after it was gone
/// restarts its driver with a 1/60 s step (a registration's first step).
/// </summary>
public sealed class VoiceGlowScene
{
    /// <summary><c>vb-fade-in</c>: 0.6 s <c>ease</c>.</summary>
    public const double FadeInSeconds = 0.6;

    /// <summary><c>vb-fade-out</c>: 0.5 s <c>ease</c>.</summary>
    public const double FadeOutSeconds = 0.5;

    private readonly CssTransition fade = new(0);
    private VoiceGlowDriver? driver;
    private VoiceGlowLook? look;
    private VoiceGlowFrame? frame;
    private double? lastNow;
    private bool running;
    private bool lookDark, lookProcessing, lookReduce;
    private double lookRadius = double.NaN;
    private int steps;

    /// <summary>The wrapper's opacity (<c>--vb-opacity</c>), 0 to 1.</summary>
    public double Opacity { get; private set; }

    /// <summary>True while anything of the glow is to be drawn (its opacity is above 0).</summary>
    public bool Visible => Opacity > 0;

    /// <summary>True while the glow is active or fading out: the next frame is wanted (an active glow breathes and follows the voice).</summary>
    public bool NeedsFrames => running;

    /// <summary>How many times the driver has been stepped (tests: it is not stepped while the glow is gone).</summary>
    public int DriverSteps => steps;

    /// <summary>The driver's last frame; null before the glow has ever run.</summary>
    public VoiceGlowFrame? Frame => frame;

    /// <summary>The look the last frame was drawn with; null before the glow has ever run.</summary>
    internal VoiceGlowLook? Look => look;

    /// <summary>Steps the fade and (while the glow runs) the driver to <paramref name="now"/> (seconds, monotonic).</summary>
    public void Advance(double now, VoiceGlowInput input)
    {
        // A delayed composition sample cannot replay behind a frame already prepared by the injected clock.
        if (lastNow is { } previousTime && now < previousTime) return;
        var dt = lastNow is { } last ? Math.Max(0, now - last) : 0;
        lastNow = now;

        // vb-fade-in / vb-fade-out as CSS transitions from where the opacity is (ENGINEERING DECISION: the design's fade-out animation starts from 1); reduce motion: at once
        fade.Set(input.Active ? 1 : 0, now, input.ReduceMotion ? 0 : input.Active ? FadeInSeconds : FadeOutSeconds);
        Opacity = fade.Value(now);

        var runs = input.Active || fade.IsRunning(now) || Opacity > 0;
        if (!runs)
        {
            running = false;
            return;
        }

        EnsureLook(input);
        if (!running || driver is null)
        {
            // the package registers the glow with the shared driver when it becomes active or starts to fade: the registration's first step is 1/60 s
            driver ??= new VoiceGlowDriver(look!.Config);
            driver.Restart();
            running = true;
        }
        driver.Configure(look!.Config);
        frame = driver.Step(dt, input.Level.Value, input.Width, input.Height);
        steps += 1;
    }

    // the look follows the theme, the processing flag, reduce motion and the radius; the recipe is rebuilt only when one of them changes
    private void EnsureLook(VoiceGlowInput input)
    {
        if (look is not null && lookDark == input.Dark && lookProcessing == input.Processing && lookReduce == input.ReduceMotion && lookRadius == input.Radius) return;
        look = VoiceGlowLook.From(VoiceGlowComposerProps.For(input.Dark, input.Processing, input.ReduceMotion, input.Radius));
        lookDark = input.Dark;
        lookProcessing = input.Processing;
        lookReduce = input.ReduceMotion;
        lookRadius = input.Radius;
    }
}
