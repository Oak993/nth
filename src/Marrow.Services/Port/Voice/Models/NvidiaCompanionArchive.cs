// PORT-ONLY: OD-0104 authorizes NVIDIA's pinned redistributables; voice-pins.json cuda.nvidiaCompanions is the source.
namespace Marrow.Services.Port.Voice.Models;

public sealed record NvidiaCompanionArchive(string Id, string Url, long Bytes, string Sha256, IReadOnlyList<string> Entries)
{
    public static NvidiaCompanionArchive Cublas { get; } = new("libcublas-12.4.2.65",
        "https://developer.download.nvidia.com/compute/cuda/redist/libcublas/windows-x86_64/libcublas-windows-x86_64-12.4.2.65-archive.zip",
        391101865, "4229d2b590e4382e1b36651dde644e04c5a8664f99f53bb43dca967982a84f58",
        ["libcublas-windows-x86_64-12.4.2.65-archive/bin/cublas64_12.dll", "libcublas-windows-x86_64-12.4.2.65-archive/bin/cublasLt64_12.dll"]);

    public static NvidiaCompanionArchive Cudart { get; } = new("cuda_cudart-12.4.99",
        "https://developer.download.nvidia.com/compute/cuda/redist/cuda_cudart/windows-x86_64/cuda_cudart-windows-x86_64-12.4.99-archive.zip",
        2474121, "1034ca43da4afbc79f69269cc60dc081de631e1c0d229d60f045d9516409317a",
        ["cuda_cudart-windows-x86_64-12.4.99-archive/bin/cudart64_12.dll"]);

    public static IReadOnlyList<NvidiaCompanionArchive> All { get; } = [Cublas, Cudart];
}

public enum NvidiaCompanionResult { Installed, Failed, Cancelled }
