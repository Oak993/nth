// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092, OD-0099 Q-V-9 C; first-release voice typing, VT-6): which Whisper.net runtimes are really on disk (V-3.2, V-7.1; voice-pins.json cuda.feasibility.halfInstalledPack).
using Marrow.Services.Port.Voice.Models;

namespace Marrow.Services.Port.Voice.Engine;

/// <summary>
/// Reads the folders: the shipped CPU runtime (<c>runtimes\win-x64</c>) and Vulkan runtime (<c>runtimes\vulkan\win-x64</c>) beside the app, and the downloaded CUDA 12 runtime
/// in <c>&lt;data root&gt;\runtimes\cuda12\win-x64</c>. The CUDA runtime is offered only when the pack's five files and the three NVIDIA libraries the pack does not carry are all
/// there: a half-installed folder aborts the whole process when the loader goes on to the next runtime (VT-0 probe case 15). Sizes and hashes were checked by the installer
/// (VT-7); this reads presence only.
/// </summary>
public sealed class FileRuntimeInventory(string appBase, string dataRoot, Func<bool> nvidiaPresent)
{
    /// <summary>The NVIDIA CUDA runtime libraries the pack does not carry (voice-pins.json cuda.feasibility.packDoesNotCarry).</summary>
    public static IReadOnlyList<string> CudaCompanions { get; } = ["cudart64_12.dll", "cublas64_12.dll", "cublasLt64_12.dll"];

    /// <summary>The whisper.dll of a runtime shipped beside the app.</summary>
    public static string AppRuntimeLibrary(string appBase, RuntimeKind kind) => kind switch
    {
        RuntimeKind.Cpu => Path.Combine(appBase, "runtimes", "win-x64", "whisper.dll"),
        RuntimeKind.Vulkan => Path.Combine(appBase, "runtimes", "vulkan", "win-x64", "whisper.dll"),
        RuntimeKind.Cuda12 => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The CUDA runtime is not shipped beside the app (Q-V-9 C)."),
    };

    public RuntimeFacts Current()
    {
        var installed = new List<RuntimeKind>(3);
        if (File.Exists(AppRuntimeLibrary(appBase, RuntimeKind.Cpu))) installed.Add(RuntimeKind.Cpu);
        if (File.Exists(AppRuntimeLibrary(appBase, RuntimeKind.Vulkan))) installed.Add(RuntimeKind.Vulkan);
        var pack = CudaRuntimePack.Cuda12;
        var folder = Path.Combine([dataRoot, .. pack.LoaderFolder.Split('/')]);
        if (pack.Files.Select(f => f.Name).Concat(CudaCompanions).All(name => File.Exists(Path.Combine(folder, name)))) installed.Add(RuntimeKind.Cuda12);
        return new RuntimeFacts(installed, nvidiaPresent(), dataRoot);
    }
}
