// PORT-ONLY, owner-ruled addition (OD-0092, OD-0099 Q-V-4 A; first-release voice typing, VT-14): what "Check my hardware" leaves behind (voice-SPEC V-10.6, V-16.4) and the
// reason line under the recommendation (V-10.4). Pure: the page hands in what the probe found.
namespace Marrow.Core.Port.Voice;

/// <summary>The stored result of the user's own check and the reason line.</summary>
public static class ManualHardwareCheck
{
    private const double Gigabyte = 1L << 30;

    /// <summary>
    /// The <c>voiceHardware</c> record of a check the user ran (V-16.4, origin manual): the key of V-10.5 for this profile, app and runtime; the hardware as probed; the
    /// recommendation as <c>candidate</c>; the model in use now as <c>model</c>; the time to whole seconds. The check itself times nothing (V-10.6: it downloads nothing),
    /// so ENGINEERING DECISION: a record of the same key keeps what the self-test measured for the same model (<c>rtf</c>) and its <c>liveWords</c>; with none, <c>rtf</c> is 0 (not
    /// measured) and live words follow lead ruling R1 (a working GPU backend stays live) and R8 (the processor writes after you stop until the self-test and the question say otherwise).
    /// </summary>
    public static VoiceHardwareRecord Record(HardwareProfile profile, Recommendation recommendation, VoiceHardwareRecord? previous, string? modelInUse,
        DateTimeOffset now, string app, string runtimeVersion)
    {
        var key = HardwareKey.Compute(profile, app, runtimeVersion);
        var same = previous is not null && string.Equals(previous.Key, key, StringComparison.Ordinal);
        var model = modelInUse ?? (same ? previous!.Model : "");
        return new VoiceHardwareRecord
        {
            Key = key,
            Origin = HardwareOrigin.Manual,
            Cpu = profile.CpuName,
            Cores = profile.PhysicalCores,
            RamGB = profile.MemoryGB,
            Gpu = profile.GpuDescription,
            VramGB = Math.Round(profile.DedicatedVramBytes / Gigabyte, 1),
            FreeVramGB = Math.Round(profile.FreeVramBytes / Gigabyte, 1),
            Backend = profile.Backend switch { GpuBackend.Vulkan => VoiceBackend.Vulkan, GpuBackend.Cuda => VoiceBackend.Cuda, _ => VoiceBackend.Cpu },
            Candidate = recommendation.ModelId,
            Model = model,
            Rtf = same && string.Equals(previous!.Model, model, StringComparison.Ordinal) ? previous.Rtf : 0,
            LiveWords = same ? previous!.LiveWords : profile.Backend != GpuBackend.None,
            At = new DateTimeOffset(now.UtcDateTime.Ticks - now.UtcDateTime.Ticks % TimeSpan.TicksPerSecond, TimeSpan.Zero),
            App = app,
        };
    }

    /// <summary>
    /// V-10.4 (Q-V-4 A): the reason line with this PC's measured time. It needs a time measured for the recommended model itself (the stored record's <c>rtf</c> with
    /// <c>model</c> equal to the recommendation); with none the reason is empty, because the templates carry a measured number and none may be made up. The bigger model's time
    /// (t2) is not stored, so the CPU line ends after its first clause ("measured or omitted").
    /// </summary>
    public static string Reason(HardwareProfile profile, Recommendation recommendation, VoiceHardwareRecord? stored)
    {
        if (stored is null || stored.Rtf <= 0 || !string.Equals(stored.Model, recommendation.ModelId, StringComparison.Ordinal)) return "";
        return recommendation.Basis == RecommendationBasis.Gpu
            ? RecommendationReason.Gpu(profile.GpuDescription, stored.Rtf)
            : RecommendationReason.Cpu(recommendation.ModelId, stored.Rtf, null, null);
    }
}
