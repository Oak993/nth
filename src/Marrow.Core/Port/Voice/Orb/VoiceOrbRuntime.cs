// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-4, voice-SPEC V-11.1, V-11.2).
namespace Marrow.Core.Port.Voice.Orb;

/// <summary>How a <see cref="VoiceOrbRuntime"/> is laid out (voice-orbs.js <c>make(canvas, opts)</c>).</summary>
public readonly record struct VoiceOrbRuntimeOptions(
    double SizePx,
    double BallPx,
    double DevicePixelRatio = 1,
    bool Dark = true,
    bool EdgeSource = false,
    bool Supersample = true,
    double Phase = 0,
    int SupersampleBelow = VoiceOrbRuntime.DesignSupersampleBelow);

/// <summary>
/// The CPU side of the design's <c>makeOrb().frame</c> (voice-orbs.js:479-575): the state sample, the audio rules, the integrated motion
/// phase, the framing, the glow. The result is the 136-float uniform block and the <c>mrw</c> uniform the renderer uploads. All arithmetic is
/// the design's (double), every store into the block is a float32 as in a JavaScript <c>Float32Array</c>.
/// </summary>
public sealed class VoiceOrbRuntime
{
    private readonly float[] _idle;
    private readonly float[] _thinking;
    private readonly float[] _values = new float[OrbSeeds.Length];
    private readonly bool _edgeSource;
    private readonly bool _supersample;
    private readonly int _supersampleBelow;
    private bool _dark;
    private double _ballFraction;
    private int _devicePx;
    private int _renderScale;
    private int _renderPx;
    private double? _lastT;
    private OrbMrw _mrw;

    public VoiceOrbRuntime(VoiceOrbKind kind, VoiceOrbRuntimeOptions options)
    {
        _idle = OrbSeeds.Idle(kind).ToArray();
        _thinking = OrbSeeds.Thinking(kind).ToArray();
        _dark = options.Dark;
        _edgeSource = options.EdgeSource;
        _supersample = options.Supersample;
        _supersampleBelow = options.SupersampleBelow;
        Phase = options.Phase;
        Resize(options.SizePx, options.BallPx, options.DevicePixelRatio);
    }

    /// <summary>The motion phase (seconds of preset time); a swapped-in orb takes the old one's (V-11.5).</summary>
    public double Phase { get; set; }

    /// <summary>The device pixels of the slot (<c>dev</c>).</summary>
    public int DevicePx => _devicePx;

    /// <summary>The design's threshold: below 128 device pixels the shader runs into a 2x surface (voice-orbs.js:496, <c>dev &lt; 128 ? 2 : 1</c>).</summary>
    public const int DesignSupersampleBelow = 128;

    /// <summary>1, or 2 when the slot is under the options' threshold (the design's 128 device pixels unless the view asks for more) and the shader runs into a 2x surface (V-11.3).</summary>
    public int RenderScale => _renderScale;

    /// <summary>The edge of the render surface in pixels (<c>rdev</c>).</summary>
    public int RenderPx => _renderPx;

    /// <summary>The uniform block after the last <see cref="Frame"/>.</summary>
    public ReadOnlySpan<float> Values => _values;

    /// <summary>The <c>mrw</c> uniform after the last <see cref="Frame"/>.</summary>
    public OrbMrw Mrw => _mrw;

    /// <summary>Lays the orb out for a slot (<c>resize(px, ball)</c>, voice-orbs.js:489-499).</summary>
    public void Resize(double sizePx, double ballPx, double devicePixelRatio)
    {
        // `Math.min(3, window.devicePixelRatio || 1)`: zero and NaN count as 1
        var dpr = Math.Min(3, devicePixelRatio > 0 ? devicePixelRatio : 1);
        _ballFraction = ballPx != 0 && !double.IsNaN(ballPx) ? ballPx / sizePx : 0;
        _devicePx = checked((int)Math.Max(2, Math.Floor(sizePx * dpr + 0.5))); // Math.round: half up
        _renderScale = !_supersample ? 1 : _devicePx < _supersampleBelow ? 2 : 1;
        _renderPx = _devicePx * _renderScale;
    }

    /// <summary>The theme only changes the lift shadow's strength (V-11.6).</summary>
    public void SetDark(bool dark) => _dark = dark;

    /// <summary>Computes one frame at time <paramref name="t"/> (seconds).</summary>
    public void Frame(double t, in OrbFrameState state)
    {
        // the export's frame(): the state sample, the audio rules, the integrated motion phase
        var progress = Math.Min(1, Math.Max(0, Number(state.Act)));
        OrbExportRuntime.SampleTransition(_idle, _thinking, progress, _values);
        var frameDelta = _lastT is null ? 0 : Math.Min(0.1, Math.Max(0, t - _lastT.Value));
        _lastT = t;
        if (state.Bands is { } bands) OrbExportRuntime.ApplyAudioUniforms(_values, bands);

        // Marrow: the state layer drives the speed (a multiple of the Idle speed) in place of the speed rule
        if (state.Speed is { } speed) _values[3] = (float)(Math.Max(0, speed) * _idle[3]);
        if (!state.ReduceMotion) Phase += frameDelta * Math.Max((double)_values[3], 0);
        _values[0] = _renderPx;
        _values[1] = _renderPx;
        _values[2] = (float)(Phase / Math.Max((double)_values[3], 0.001));

        // Marrow framing: the source's own radius and edge softness, set for the button
        if (_ballFraction > 0)
        {
            _values[4] = (float)(_ballFraction * (state.Scale is { } scale ? scale : 1));
            if (!_edgeSource)
            {
                var radPx = (double)_values[4] * _renderPx / 2;
                _values[16] = (float)Math.Min(0.15, Math.Max(0.005, (1.5 / Math.Max(radPx, 1) - 0.01) / 2));
            }
        }

        if (state.Glow is { } glow) _values[17] = (float)Math.Max(0, glow); // Marrow: our glow, through the source's Outer Glow
        _mrw = new OrbMrw(Math.Max(0, Math.Min(1, Number(state.Lift))), Math.Max(0, Math.Min(1, Number(state.Desat))), _dark ? 1 : 0);
    }

    // `+x || 0`: a NaN counts as 0
    private static double Number(double value) => double.IsNaN(value) ? 0 : value;
}
