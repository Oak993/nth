using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Marrow.Core.Support;

namespace Marrow.App.Views.Composer;

// ED-252 (lead's ruling, 2026-10-03, the owner's zero-stutter bar): the knob's programs compile once per machine, not once
// per launch. Skia's own persistent program cache (GrContextOptions::fPersistentCache) is not reachable: SkiaSharp 3.119's
// GRContextOptions carries only AvoidStencilBuffers, RuntimeProgramCacheSize, GlyphCacheTextureMaximumBytes,
// AllowPathMaskCaching, DoManualMipmapping and BufferMapThreshold (decompiled), and Avalonia makes the context. The GL layer
// under it is: Avalonia's ANGLE (av_libglesv2.dll, the D3D11 backend) implements EGL_ANDROID_blob_cache, which hands every
// linked program's binary (with its compiled D3D shaders) to the application to keep, and asks for it back before it
// compiles. This installs those two callbacks on the display the render thread draws with, storing each entry as a file
// under the app's cache folder (StoragePaths.Root\Cache\GPU Programs\<the GPU, its driver and the app's build>), so a later
// launch on the same machine, driver and build links the knob's programs from their binaries instead of compiling them.
// Installed from the render thread, before the knob's programs are first linked (GpuWarmup.s first step); ANGLE accepts
// the callbacks once per display. Off where the renderer is not ANGLE (software, another GL) and when the storage paths
// are not bound (tests, unless a test names a folder).

/// <summary>ANGLE's program-binary cache, kept on disk (see the file note).</summary>
internal static class GpuProgramCache
{
    private const int EglExtensions = 0x3055, GlRenderer = 0x1F01, GlVersion = 0x1F02;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint GetCurrentDisplay();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint QueryString(nint display, int name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint GetProcAddress([MarshalAs(UnmanagedType.LPStr)] string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint GetString(int name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetBlobCacheFuncs(nint display, nint set, nint get);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetBlob(nint key, nint keySize, nint value, nint valueSize);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint GetBlob(nint key, nint keySize, nint value, nint valueSize);

    // kept alive for the process: ANGLE calls them on the render thread whenever it links a program
    private static readonly SetBlob setBlob = Put;
    private static readonly GetBlob getBlob = Take;
    private static int installed;
    private static string? folder;

    /// <summary>Tests: the folder to keep the entries in (null: the app's cache folder, when the storage paths are bound).</summary>
    internal static string? FolderOverride { get; set; }

    /// <summary>Why it is not on (null once installed), for the native record.</summary>
    internal static string? State { get; private set; } = "not tried";

    /// <summary>The folder in use, once installed.</summary>
    internal static string? Folder => folder;

    /// <summary>Entries written, entries found, entries asked for and missing (this process).</summary>
    internal static int Writes, Hits, Misses;

    /// <summary>Installs the callbacks on the render thread's current display (the render thread, a GL context current).</summary>
    internal static void EnsureInstalled()
    {
        if (Interlocked.Exchange(ref installed, 1) == 1) return;
        try { State = Install(); }
        catch (Exception error) { State = "failed: " + error.GetType().Name + ": " + error.Message; }
    }

    /// <summary>The cache's root folder: the test's, or the app's (null when the storage paths are not bound).</summary>
    internal static string? Root()
    {
        if (FolderOverride is { } given) return given;
        try { return Path.Combine(StoragePaths.Root, "Cache", "GPU Programs"); }
        catch (Exception) { return null; }
    }

    /// <summary>This build of the app (its module's id).</summary>
    internal static string Build { get; } = typeof(GpuProgramCache).Assembly.ManifestModule.ModuleVersionId.ToString("N");

    /// <summary>A warm-up completed: the root's marker names this build and the folder of the GPU it ran on (GpuWarmup.IsWarm).</summary>
    internal static void MarkWarm(string keyFolder)
    {
        if (Root() is not { } root) return;
        try { File.WriteAllLines(Path.Combine(root, "warm.txt"), [Build, Path.GetFileName(keyFolder)]); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string? Install()
    {
        if (Root() is not { } root) return "off: the storage paths are not bound";
        if (!NativeLibrary.TryLoad("av_libglesv2", typeof(GpuProgramCache).Assembly, null, out var library)) return "off: no ANGLE (av_libglesv2) in this process";
        // Avalonia's ANGLE is one library exporting its EGL entry points under ANGLE's own names (EGL_GetCurrentDisplay ...)
        T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
        var display = Export<GetCurrentDisplay>("EGL_GetCurrentDisplay")();
        if (display == 0) return "off: no current EGL display on this thread";
        var extensions = Marshal.PtrToStringAnsi(Export<QueryString>("EGL_QueryString")(display, EglExtensions)) ?? "";
        if (!extensions.Split(' ').Contains("EGL_ANDROID_blob_cache")) return "off: the display has no EGL_ANDROID_blob_cache";
        var setFuncs = Export<GetProcAddress>("EGL_GetProcAddress")("eglSetBlobCacheFuncsANDROID");
        if (setFuncs == 0) return "off: eglSetBlobCacheFuncsANDROID not found";
        var glGetString = Export<GetString>("glGetString");
        var renderer = Marshal.PtrToStringAnsi(glGetString(GlRenderer)) ?? "";
        var version = Marshal.PtrToStringAnsi(glGetString(GlVersion)) ?? "";
        var build = Build;
        // the GPU and its driver (ANGLE's renderer string names both), the GL version, and this build of the app
        var key = Hex(SHA256.HashData(Encoding.UTF8.GetBytes(renderer + "\n" + version + "\n" + build)))[..24];
        folder = Path.Combine(root, key);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "key.txt"), renderer + "\n" + version + "\n" + build + "\n");
        Marshal.GetDelegateForFunctionPointer<SetBlobCacheFuncs>(setFuncs)(display,
            Marshal.GetFunctionPointerForDelegate(setBlob), Marshal.GetFunctionPointerForDelegate(getBlob));
        return null;
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    private static string? FileFor(nint key, nint keySize)
    {
        if (folder is null || key == 0 || keySize <= 0) return null;
        var bytes = new byte[keySize];
        Marshal.Copy(key, bytes, 0, bytes.Length);
        return Path.Combine(folder, Hex(SHA256.HashData(bytes))[..40] + ".bin");
    }

    // set: keep the entry (written beside, then moved over, so a reader never sees half a file)
    private static void Put(nint key, nint keySize, nint value, nint valueSize)
    {
        try
        {
            if (FileFor(key, keySize) is not { } file || value == 0 || valueSize <= 0) return;
            var bytes = new byte[valueSize];
            Marshal.Copy(value, bytes, 0, bytes.Length);
            var part = file + "." + Environment.ProcessId + ".part";
            File.WriteAllBytes(part, bytes);
            File.Move(part, file, overwrite: true);
            Interlocked.Increment(ref Writes);
        }
        catch (Exception) { } // a full or locked disk: the program is simply compiled next time
    }

    // get: the entry's size, copied when the buffer is large enough (ANGLE asks once for the size, then with a buffer)
    private static nint Take(nint key, nint keySize, nint value, nint valueSize)
    {
        try
        {
            if (FileFor(key, keySize) is not { } file || !File.Exists(file)) { Interlocked.Increment(ref Misses); return 0; }
            var bytes = File.ReadAllBytes(file);
            if (value != 0 && valueSize >= bytes.Length) { Marshal.Copy(bytes, 0, value, bytes.Length); Interlocked.Increment(ref Hits); }
            return bytes.Length;
        }
        catch (Exception) { return 0; }
    }
}
