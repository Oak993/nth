// PORT-ONLY, owner-ruled addition (OD-0092, OD-0099 Q-V-9 C and Q-V-13 B; first-release voice typing, VT-7): the pinned CUDA runtime pack (Q-V-9 C; values are VT-0's, voice-pins.json "cuda").
namespace Marrow.Services.Port.Voice.Models;

/// <summary>
/// A nupkg pin. <see cref="Sha256"/> is null when VT-0 pinned only the SHA-512 (nuget's own hash): the shared downloader verifies SHA-256, so
/// such a pack is UNPINNED for download and the installer refuses it (ED-NEW-f-vt-7 (5)).
/// </summary>
public sealed record CudaRuntimePack(string Id, string Version, string Url, long Bytes, string Sha512, string? Sha256, string LoaderFolder,
    string PackFolder, IReadOnlyList<CudaPackFile> Files)
{
    /// <summary>
    /// Whisper.net.Runtime.Cuda12.Windows 1.9.1, the pack VT-0 reproduced loading from a downloaded folder. Every value is voice-pins.json's,
    /// the SHA-256 included (the lead computed it from the cached copy whose size and SHA-512 equal the pin: ED-NEW-f-vt-7 (5), Windows review).
    /// </summary>
    public static CudaRuntimePack Cuda12 { get; } = new("Whisper.net.Runtime.Cuda12.Windows", "1.9.1",
        "https://api.nuget.org/v3-flatcontainer/whisper.net.runtime.cuda12.windows/1.9.1/whisper.net.runtime.cuda12.windows.1.9.1.nupkg",
        250058371, "a73be8db5ef2f271cf7196aff36cc9fee1ecb67b02169367e9da1dace8b65a3c18de3ef1c8f701061197791befa51b9989163472e4fc0444dc63d9ca1aa54e67", "eb8d4233c202fe948cace50e3fa08fec7beead699db86a17df9820d03180a30c", "runtimes/cuda12/win-x64", "build/win-x64",
        [
        new("ggml-base-whisper.dll", 636928),
        new("ggml-cpu-whisper.dll", 782848),
        new("ggml-cuda-whisper.dll", 563957248),
        new("ggml-whisper.dll", 67072),
        new("whisper.dll", 484864),
        ]);
}
