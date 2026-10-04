// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092, OD-0099 Q-V-9 C; first-release voice typing, VT-6): the real whisper.cpp VAD context (V-6.1).
using System.Reflection;
using System.Runtime.InteropServices;

namespace Marrow.Services.Port.Voice.Engine;

/// <summary>
/// <see cref="IVadNative"/> over the five whisper.cpp exports. The process binds its P/Invoke library name "whisper" to ONE whisper.dll (the first one opened: .NET resolves a
/// DllImport once), so a later <see cref="Open"/> with another path is refused. WINDOWS ONLY in practice (the shipped natives are win-x64); its tests are Windows-only.
/// </summary>
public sealed class WhisperVadNative : IVadNative
{
    private static readonly Lock Gate = new();
    private static nint library;
    private static string? libraryPath;
    private static bool resolverSet;

    private nint context;
    private float[] buffer = new float[512 * 8];

    private WhisperVadNative(nint context) => this.context = context;

    /// <summary>Loads <paramref name="whisperLibraryPath"/> (once per process), then the VAD model on it.</summary>
    public static WhisperVadNative Open(string modelPath, string whisperLibraryPath, bool useGpu, int threads)
    {
        // Found on the Windows run (NVIDIA RTX 4080, Vulkan): whisper.cpp's VAD with use_gpu on aborts the whole process in ggml ("pre-allocated tensor (leaf_0) in a buffer
        // (Vulkan0) that cannot run the operation"), which nothing can catch. The VAD costs 0.4 ms a frame on the processor, so it never gets the GPU.
        if (useGpu) throw new ArgumentException("whisper.cpp's VAD aborts the process on a GPU backend; it runs on the processor.", nameof(useGpu));
        lock (Gate)
        {
            if (libraryPath is not null && !string.Equals(libraryPath, whisperLibraryPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The VAD already uses " + libraryPath + "; one whisper.dll is bound per process.");
            if (libraryPath is null)
            {
                library = NativeLibrary.Load(whisperLibraryPath);
                libraryPath = whisperLibraryPath;
            }
            if (!resolverSet)
            {
                NativeLibrary.SetDllImportResolver(typeof(WhisperVadImports).Assembly, Resolve);
                resolverSet = true;
            }
        }
        var path = Marshal.StringToCoTaskMemUTF8(modelPath);
        try
        {
            var parameters = new WhisperVadContextParams { Threads = threads, UseGpu = useGpu ? (byte)1 : (byte)0, GpuDevice = 0 };
            var context = WhisperVadImports.InitFromFileWithParams(path, parameters);
            if (context == 0) throw new InvalidOperationException("whisper.cpp could not open the VAD model " + modelPath);
            return new WhisperVadNative(context);
        }
        finally
        {
            Marshal.FreeCoTaskMem(path);
        }
    }

    private static nint Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath) =>
        name == WhisperVadImports.Library ? library : 0;

    public float[] Detect(ReadOnlySpan<float> samples)
    {
        ObjectDisposedException.ThrowIf(context == 0, this);
        if (buffer.Length < samples.Length) buffer = new float[samples.Length];
        samples.CopyTo(buffer);
        if (WhisperVadImports.DetectSpeech(context, buffer, samples.Length) == 0) throw new InvalidOperationException("whisper_vad_detect_speech failed.");
        var count = WhisperVadImports.ProbabilityCount(context);
        var probabilities = new float[count];
        if (count > 0) Marshal.Copy(WhisperVadImports.Probabilities(context), probabilities, 0, count);
        return probabilities;
    }

    public void Dispose()
    {
        var freed = Interlocked.Exchange(ref context, 0);
        if (freed != 0) WhisperVadImports.Free(freed);
    }
}
