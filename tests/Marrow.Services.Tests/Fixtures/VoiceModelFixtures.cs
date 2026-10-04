using System.Security.Cryptography;
using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Voice.Models;
using Marrow.Testing.Downloads;

namespace Marrow.Services.Tests.Fixtures;

// VT-7 test fixture: small synthetic model files (a manifest row for a few KB of fake bytes, the id of a real row so names look real) and a CUDA pack
// pin for a fake nupkg. Nothing here is a real model or a real NVIDIA/Whisper.net file.
internal static class VoiceModelFixtures
{
    internal static ModelFile Model(FakeArtifact artifact, string id = "tiny-q5_1") =>
        new(id, "Tiny", ModelVersion.Compact, false, artifact.Length, artifact.Sha256Hex);

    internal static readonly string[] PackNames = ["ggml-base-whisper.dll", "ggml-cpu-whisper.dll", "ggml-cuda-whisper.dll", "ggml-whisper.dll", "whisper.dll"];

    internal static string Sha512Hex(byte[] bytes) => Convert.ToHexStringLower(SHA512.HashData(bytes));

    /// <summary>A zip shaped like the nupkg: five files under build/win-x64, plus entries that must not be unpacked.</summary>
    internal static (FakeArtifact Nupkg, CudaRuntimePack Pin) Pack(Action<List<(string Path, byte[] Content)>>? tweak = null, Func<FakeArtifact, string>? sha512 = null)
    {
        var entries = new List<(string Path, byte[] Content)> { ("[Content_Types].xml", "<x/>"u8.ToArray()), ("Whisper.net.Runtime.Cuda12.Windows.nuspec", "n"u8.ToArray()) };
        for (var i = 0; i < PackNames.Length; i++) entries.Add(("build/win-x64/" + PackNames[i], FakeArtifact.Pattern(2_000 + i * 100, seed: 700 + i).Bytes));
        entries.Add(("../evil.dll", "evil"u8.ToArray()));
        tweak?.Invoke(entries);
        var nupkg = FakeArtifact.Zip([.. entries]);
        var files = PackNames.Select((n, i) => new CudaPackFile(n, 2_000 + i * 100)).ToList();
        var pin = new CudaRuntimePack("Test.Cuda12", "1.0.0", "https://example.invalid/pack.nupkg", nupkg.Length, sha512?.Invoke(nupkg) ?? Sha512Hex(nupkg.Bytes),
            nupkg.Sha256Hex, "runtimes/cuda12/win-x64", "build/win-x64", files);
        return (nupkg, pin);
    }
}
