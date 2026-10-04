// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092, OD-0099 Q-V-9 C; first-release voice typing, VT-6): Whisper.net's runtime options (V-3.2, V-7.1; voice-pins.json cuda.feasibility).
using Whisper.net.LibraryLoader;

namespace Marrow.Services.Port.Voice.Engine;

/// <summary>
/// Sets <c>RuntimeOptions</c> before a native load and reads which runtime loaded. The options are process-wide and the loader loads a native library once: the order given at
/// the first load of the process is the one that counts (VT-0). When Cuda12 is in the order the library path points into the data root (<c>RuntimeOptions.LibraryPath</c> is read
/// through <c>Path.GetDirectoryName</c>, so a file name inside the root) and the CUDA folder goes on PATH, because Whisper.net finds cudart by its bare name (VT-0 probe cases 14
/// and 17). Falling back to the app folder's Vulkan and CPU runtimes when a library path is set is UNVERIFIED until the voice-evidence run on Windows.
/// </summary>
public static class WhisperNativeRuntimes
{
    public static void Configure(IReadOnlyList<RuntimeKind> order, string? libraryRoot)
    {
        RuntimeOptions.RuntimeLibraryOrder = [.. order.Select(ToLibrary)];
        if (order.Contains(RuntimeKind.Cuda12) && libraryRoot is not null)
        {
            RuntimeOptions.LibraryPath = Path.Combine(libraryRoot, "runtime.dll");
            var folder = Path.Combine(libraryRoot, "runtimes", "cuda12", "win-x64");
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            if (!path.Split(Path.PathSeparator).Contains(folder, StringComparer.OrdinalIgnoreCase))
                Environment.SetEnvironmentVariable("PATH", folder + Path.PathSeparator + path);
        }
    }

    /// <summary>The runtime that loaded, or an error when the loader reports none or one the engine does not offer.</summary>
    public static RuntimeKind Loaded() => RuntimeOptions.LoadedLibrary switch
    {
        RuntimeLibrary.Cuda12 => RuntimeKind.Cuda12,
        RuntimeLibrary.Vulkan => RuntimeKind.Vulkan,
        RuntimeLibrary.Cpu => RuntimeKind.Cpu,
        var other => throw new InvalidOperationException("Whisper.net reports the runtime " + (other?.ToString() ?? "none") + " as loaded."),
    };

    private static RuntimeLibrary ToLibrary(RuntimeKind kind) => kind switch
    {
        RuntimeKind.Cuda12 => RuntimeLibrary.Cuda12,
        RuntimeKind.Vulkan => RuntimeLibrary.Vulkan,
        RuntimeKind.Cpu => RuntimeLibrary.Cpu,
    };
}
