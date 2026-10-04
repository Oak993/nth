// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092; first-release voice typing, VT-3): where the voice seams are bound
// (voice-SPEC V-2.4, V-2.5). One Ambient per seam: the process default is the fail-closed stub, the app's start-up composition
// replaces it with the production implementation, and a test overrides it for its own async flow.
namespace Marrow.Core.Port.Voice.Seams;

/// <summary>The ambient voice seams. Every default throws when touched (V-2.5; V-T46).</summary>
public static class VoiceSeams
{
    public static Ambient<IAudioCapture> Capture { get; } = new(new FailClosedAudioCapture());

    public static Ambient<IVoiceActivityDetector> Vad { get; } = new(new FailClosedVoiceActivityDetector());

    public static Ambient<ISpeechEngine> Engine { get; } = new(new FailClosedSpeechEngine());

    public static Ambient<IMicrophoneAccess> Microphone { get; } = new(new FailClosedMicrophoneAccess());

    public static Ambient<IStreamingResampler> Resampler { get; } = new(new FailClosedStreamingResampler());

    public static Ambient<IModelTransport> ModelTransport { get; } = new(new FailClosedModelTransport());

    /// <summary>
    /// Every seam of this unit with its interface type and the fail-closed default, for the V-T46 test. A later unit that adds a
    /// seam (VT-6 <c>IHardwareProbe</c>, VT-7 <c>IModelStore</c>, VT-16 the OpenAI handler) adds its line here and its stub above.
    /// </summary>
    public static IReadOnlyList<(Type Interface, object FailClosedDefault)> All { get; } =
    [
        (typeof(IAudioCapture), new FailClosedAudioCapture()),
        (typeof(IVoiceActivityDetector), new FailClosedVoiceActivityDetector()),
        (typeof(ISpeechEngine), new FailClosedSpeechEngine()),
        (typeof(IMicrophoneAccess), new FailClosedMicrophoneAccess()),
        (typeof(IStreamingResampler), new FailClosedStreamingResampler()),
        (typeof(IModelTransport), new FailClosedModelTransport()),
    ];
}

/// <summary>
/// The seams a <c>VoiceSession</c> uses, resolved when touched. <see cref="Ambient"/> reads each seam's <c>Current</c> at the moment of
/// use, so a session built in a test with no bindings fails closed the first time it reaches for one (V-2.5), not at construction.
/// </summary>
public sealed class VoiceSeamSet
{
    private readonly Func<IAudioCapture> capture;
    private readonly Func<IVoiceActivityDetector> vad;
    private readonly Func<ISpeechEngine> engine;
    private readonly Func<IMicrophoneAccess> microphone;
    private readonly Func<IStreamingResampler> resampler;

    public VoiceSeamSet(Func<IAudioCapture> capture, Func<IVoiceActivityDetector> vad, Func<ISpeechEngine> engine,
        Func<IMicrophoneAccess> microphone, Func<IStreamingResampler> resampler)
    {
        this.capture = capture;
        this.vad = vad;
        this.engine = engine;
        this.microphone = microphone;
        this.resampler = resampler;
    }

    public IAudioCapture Capture => capture();

    public IVoiceActivityDetector Vad => vad();

    public ISpeechEngine Engine => engine();

    public IMicrophoneAccess Microphone => microphone();

    public IStreamingResampler Resampler => resampler();

    /// <summary>Each seam read from the ambient bindings at the time of use.</summary>
    public static VoiceSeamSet Ambient { get; } = new(
        () => VoiceSeams.Capture.Current, () => VoiceSeams.Vad.Current, () => VoiceSeams.Engine.Current,
        () => VoiceSeams.Microphone.Current, () => VoiceSeams.Resampler.Current);

    /// <summary>Explicit seams (the scripted session source and tests); the resampler defaults to the ambient one.</summary>
    public static VoiceSeamSet Of(IAudioCapture capture, IVoiceActivityDetector vad, ISpeechEngine engine, IMicrophoneAccess microphone,
        IStreamingResampler? resampler = null) =>
        new(() => capture, () => vad, () => engine, () => microphone, resampler is null ? () => VoiceSeams.Resampler.Current : () => resampler);
}
