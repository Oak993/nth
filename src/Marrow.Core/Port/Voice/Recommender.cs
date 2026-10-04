// PORT-ONLY, owner-ruled addition (OD-0092; first-release voice typing, VT-1): the hardware check's recommendation (voice-SPEC V-10.2,
// from BENCH "Draft recommendation table"). The probe that fills HardwareProfile is a later unit (HardwareProbe, Services).
namespace Marrow.Core.Port.Voice;

/// <summary>The graphics adapter's maker, as far as the recommendation cares (V-10.2: NVIDIA, AMD, Intel, anything else).</summary>
public enum GpuVendor { Nvidia, Amd, Intel, Other }

/// <summary>Which GPU backend of Whisper.net loaded and passed the check (V-10.1): none, Vulkan or CUDA.</summary>
public enum GpuBackend { None, Vulkan, Cuda }

/// <summary>What the probe found (V-10.1): the processor, memory, the main graphics adapter, and the working GPU backend.</summary>
public sealed record HardwareProfile(
    string CpuName, int PhysicalCores, int MemoryGB, string GpuDescription, GpuVendor GpuVendor,
    long DedicatedVramBytes, long FreeVramBytes, string DriverVersion, GpuBackend Backend);

/// <summary>Whether a recommendation rests on a working GPU backend or on the processor alone (it decides the reason line's template).</summary>
public enum RecommendationBasis { Gpu, Cpu }

/// <summary>The recommended model file and what it rests on.</summary>
public sealed record Recommendation(string ModelId, RecommendationBasis Basis);

/// <summary>The recommender (V-10.2).</summary>
public static class Recommender
{
    private const long ThreeGigabytes = 3L << 30;

    /// <summary>True only for a forced English (V-10.2: "English when forced to English, otherwise multilingual").</summary>
    public static bool IsEnglish(string? language) => string.Equals(language, "en", StringComparison.Ordinal);

    /// <summary>
    /// V-10.2. <paramref name="language"/> is the resolved language code (<see cref="VoiceLanguage.Resolve"/>) or "auto".
    /// A working GPU backend on an NVIDIA GPU with at least 3 GB (2^30 bytes) free: large-v3-turbo-q8_0; any other working GPU backend:
    /// large-v3-turbo-q5_0; the processor alone: by physical cores (8 or more) and language.
    /// </summary>
    public static Recommendation Recommend(HardwareProfile profile, string? language)
    {
        if (profile.Backend != GpuBackend.None)
            return new Recommendation(
                profile.GpuVendor == GpuVendor.Nvidia && profile.FreeVramBytes >= ThreeGigabytes ? "large-v3-turbo-q8_0" : "large-v3-turbo-q5_0",
                RecommendationBasis.Gpu);
        var english = IsEnglish(language);
        var id = profile.PhysicalCores >= 8
            ? (english ? "small.en-q8_0" : "large-v3-turbo-q5_0") // multilingual: the self-test decides, falling back to medium-q8_0 (the next rung down)
            : (english ? "base.en" : "small-q5_1");
        return new Recommendation(id, RecommendationBasis.Cpu);
    }

    // V-10.2 "Never recommended (BENCH 'Dominated')": large-v3-q5_0, the medium.en family, tiny and base for non-English, the f16
    // versions of small, medium and turbo; large-v3 (f16) only as the user's own choice.
    private static readonly HashSet<string> Never = new(StringComparer.Ordinal)
    {
        "large-v3-q5_0",
        "medium.en-q5_0", "medium.en-q8_0", "medium.en",
        "tiny-q5_1", "tiny-q8_0", "tiny", "base-q5_1", "base-q8_0", "base",
        "small", "small.en", "medium", "large-v3-turbo",
        "large-v3",
    };

    /// <summary>True for a model the recommender never offers (V-10.2).</summary>
    public static bool IsNeverRecommended(string? id) => id is not null && Never.Contains(id);
}
