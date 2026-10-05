using System.Security.Cryptography;
using Marrow.Services.Port.Voice.Models;
using Marrow.Testing.Downloads;

namespace Marrow.App.Tests.Fixtures;

// VT-17 test fixture: a pack pin for a small fake nupkg shaped like the real one (five files under build/win-x64 and entries that must not be unpacked). Nothing here is a real
// Whisper.net or NVIDIA file.
internal static class CudaPackFixture
{
    internal static readonly string[] PackNames = ["ggml-base-whisper.dll", "ggml-cpu-whisper.dll", "ggml-cuda-whisper.dll", "ggml-whisper.dll", "whisper.dll"];

    internal static (FakeArtifact Nupkg, CudaRuntimePack Pin) Pack(bool wrongSha512 = false)
    {
        var entries = new List<(string Path, byte[] Content)> { ("[Content_Types].xml", "<x/>"u8.ToArray()), ("Test.Cuda12.nuspec", "n"u8.ToArray()) };
        for (var i = 0; i < PackNames.Length; i++) entries.Add(("build/win-x64/" + PackNames[i], FakeArtifact.Pattern(2_000 + i * 100, seed: 700 + i).Bytes));
        var nupkg = FakeArtifact.Zip([.. entries]);
        var files = PackNames.Select((n, i) => new CudaPackFile(n, 2_000 + i * 100)).ToList();
        var sha512 = wrongSha512 ? new string('0', 128) : Convert.ToHexStringLower(SHA512.HashData(nupkg.Bytes));
        return (nupkg, new CudaRuntimePack("Test.Cuda12", "1.0.0", "https://example.invalid/pack.nupkg", nupkg.Length, sha512, nupkg.Sha256Hex, "runtimes/cuda12/win-x64", "build/win-x64", files));
    }
}
