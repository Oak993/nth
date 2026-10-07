// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092, OD-0099 Q-V-9 C and Q-V-19 B; first-release voice typing, VT-14): the app's start-up composition of the voice seams (voice-SPEC V-2.4, V-2.5).
using Marrow.Core.Port;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Support;
using Marrow.Services.Port.Voice.Capture;
using Marrow.Services.Port.Voice.Engine;
using Marrow.Services.Port.Voice.Models;
using Marrow.Services.Port.Voice.Probe;

namespace Marrow.App.Port.Voice;

/// <summary>
/// Binds the production implementations behind the voice seams, once, at start-up (<c>MarrowApplication</c>'s AfterSetup). Every seam's process default fails closed (V-2.5), so a test, which
/// never calls <see cref="Install"/>, reaches no microphone, no hardware, no model file and no connection. VT-14 binds the ones its page and the capture need: the endpoint host
/// (<see cref="NAudioCaptureHost"/>), the privacy gate (<see cref="MicrophoneAccess"/>), the microphone (<see cref="WasapiCapture"/>), the hardware probe (<see cref="HardwareProbe"/>) and
/// the model store and downloads. The engine, the VAD and the resampler are VT-15's.
/// </summary>
internal static class VoiceComposition
{
    private static VoiceCompositionParts? installed;
    private static IDisposable? firstRun;
    /// <summary>The folder inside the app folder that holds the bundled voice assets (the Services csproj copies them there).</summary>
    internal static string AssetsFolder(string appBase) => Path.Combine(appBase, "Port", "Voice", "Assets");

    /// <summary>
    /// Builds the parts for a data root and an app folder. Nothing here opens a device, reads a model or touches the network: the host lists on demand, the probe reads when asked, and
    /// the store opens its folder when the model list first asks.
    /// </summary>
    /// <param name="dataRoot">The data root (StoragePaths.Root in production).</param>
    /// <param name="appBase">The app folder, where the natives and the bundled assets are.</param>
    /// <param name="microphone">The stored Microphone pick, read at each start (<c>voiceTyping.mic</c>).</param>
    /// <param name="modelInUse">The stored model in use (<c>voiceTyping.model</c>), which a removal refuses.</param>
    internal static VoiceCompositionParts Build(string dataRoot, string appBase, Func<string?> microphone, Func<string?> modelInUse)
    {
        var host = new NAudioCaptureHost();
        var inventory = new FileRuntimeInventory(appBase, dataRoot, () => false); // GpuBackendCheck sets the vendor from the adapter it checks
        var vad = Path.Combine(AssetsFolder(appBase), "ggml-silero-v6.2.0.bin");
        var probe = new HardwareProbe(new WindowsHardwareSource(), new GpuBackendCheck(inventory.Current, new WhisperNetVadLoadTest(vad)));
        var models = new StoreSpeechModelBackend(() =>
        {
            var store = new ModelStore(dataRoot, modelInUse);
            // ENGINEERING DECISION: partial downloads wait in <data root>\models\downloads, beside the whisper folder the store owns.
            var downloader = new ModelDownloader(store, ModelDownloader.CreateDownloader(Path.Combine(dataRoot, "models", "downloads")));
            return (store, downloader);
        });
        return new VoiceCompositionParts(host, new MicrophoneAccess(new WinRtMicrophonePrivacySource(), host), new WasapiCapture(host, microphone), probe, models);
    }

    /// <summary>
    /// <c>voiceTyping</c> as the settings object holds it now (<see cref="VoiceTypingMirror"/>: the store can be a write behind, CZ-13.5); before any settings object exists, as the store holds it;
    /// the default when nothing is stored.
    /// </summary>
    internal static VoiceTypingSettings StoredTyping()
    {
        var store = UserDefaults.Standard.Current;
        return VoiceTypingMirror.Read(store) ?? (store.Data(VoiceTypingSettings.StoreKey) is { } stored ? VoiceTypingSettings.FromJson(stored) : VoiceTypingSettings.Default);
    }

    /// <summary>Binds the production parts as the process defaults. Called once, from the application's AfterSetup, after the storage roots are bound.</summary>
    public static void Install()
    {
        var parts = Build(StoragePaths.Root, AppContext.BaseDirectory, () => StoredTyping().Mic, () => StoredTyping().Model);
        CaptureSeams.Host.SetProcessDefault(parts.Host);
        VoiceSeams.Microphone.SetProcessDefault(parts.Microphone);
        VoiceSeams.Capture.SetProcessDefault(parts.Capture);
        HardwareProbeSeams.Probe.SetProcessDefault(parts.Probe);
        SpeechModelSeams.Backend.SetProcessDefault(parts.Models);
        installed = parts;
    }

    // Before GR-1 plans its work: tests never call Install, so the startup path stays fail closed in a test runtime.
    internal static void Attach(AppSettings settings)
    {
        if (installed is not { } parts || VoiceFirstRun.Find(settings) is not null) return;
        var source = (IVoiceModelSource)parts.Models;
        var hardware = new WindowsHardwareSource();
        var inventory = new FileRuntimeInventory(AppContext.BaseDirectory, StoragePaths.Root,
            () => settings.VoiceHardware?.Gpu.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) == true);
        var engine = new VerifiedSpeechEngine(new WhisperNetEngine(new WhisperNetBackend(), inventory.Current, hardware.PhysicalCores), source);
        var identity = VoiceStartupIdentity.Read(hardware, HardwareCheckIdentity.App, HardwareCheckIdentity.Runtime);
        var preparation = new VoicePreparation(parts.Probe, source, () => engine, new AppSettingsVoiceAccess(settings),
            () => SelfTestClip.Load(SelfTestClip.DefaultPath), HardwareCheckIdentity.App, HardwareCheckIdentity.Runtime,
            currentKey: () => identity is null ? null : HardwareKey.Compute(identity),
            currentPart: () => identity is null ? null : HardwareKey.ComputeHardwarePart(identity));
        VoiceSeams.Engine.SetProcessDefault(engine);
        VoiceSeams.Vad.SetProcessDefault(new DeferredVoiceVad(() =>
        {
            // V-6.1: open on the runtime already selected by the probe / self-test; no native model is opened at startup.
            RuntimeKind kind;
            try { kind = WhisperNativeRuntimes.Loaded(); }
            catch (InvalidOperationException) { kind = RuntimeKind.Cpu; }
            var library = kind == RuntimeKind.Cuda12
                ? Path.Combine(StoragePaths.Root, "runtimes", "cuda12", "win-x64", "whisper.dll")
                : FileRuntimeInventory.AppRuntimeLibrary(AppContext.BaseDirectory, kind);
            return SileroVad.Open(Path.Combine(AssetsFolder(AppContext.BaseDirectory), "ggml-silero-v6.2.0.bin"), library, false, ThreadCount.For(hardware.PhysicalCores()));
        }));
        VoiceSeams.Resampler.SetProcessDefault(new Marrow.Core.Port.Voice.Dsp.PolyphaseResampler());
        SpeechModelSeams.SelfTest.SetProcessDefault(new PreparationSelfTest(preparation));
        firstRun = Connect(settings, preparation, source);
    }

    internal static IDisposable Connect(AppSettings settings, VoicePreparation preparation, IVoiceModelSource source)
    {
        VoiceFirstRun.Bind(settings, new VoiceFirstRun(preparation, new VoiceSpeedQuestion(preparation), source));
        var registration = preparation.RegisterStep();
        VoiceFeature.SwitchOn();
        return registration;
    }
}
