using System.Diagnostics;
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
// Installed from the render thread, before the knob's programs are first linked (GpuWarmup's first step), and again on
// every new GPU context (a device lost or a display change can give ANGLE a new display; a display that has the callbacks
// already refuses them again, harmlessly). Off where the renderer is not ANGLE (software, another GL) and when the storage
// paths are not bound (tests, unless a test names a folder).
// Each folder's key.txt names the renderer, the GL version, the build and the machine: the graphics adapter the app draws
// with and its driver's version (DXGI), so GpuWarmup.IsWarm can tell, before any window is drawn, that a driver update of
// that adapter or a move of the app to another one leaves the kept programs unproven (OD-0096: the first-run screen shows
// again), while a change to an adapter the app does not draw with leaves them warm. The adapter is the one Avalonia 12.1.2's
// ANGLE makes its D3D11 device on (AngleWin32EglDisplay.CreateD3D11Display, decompiled: IDXGIFactory1::EnumAdapters1(0)
// where no Win32PlatformOptions.GraphicsAdapterSelectionCallback is set, and the port sets none; on ARM64 with several
// adapters the first that is not an Adreno), read the same way. A completed warm-up removes the folders of other builds and
// of adapters or drivers this machine no longer has (a folder made on its other adapter, as it is now, is kept).

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
    private static nint installedFor; // the GPU context it was last installed for (render thread)
    private static string? folder;
    private static volatile string? machine;

    /// <summary>Tests: the folder to keep the entries in (null: the app's cache folder, when the storage paths are bound).</summary>
    internal static string? FolderOverride { get; set; }

    /// <summary>Tests: where the identity of the adapter the app draws with comes from (DXGI by default; null when it
    /// cannot be read).</summary>
    internal static Func<string?> MachineSource { get; set; } = DrawingAdapter;

    /// <summary>Tests: where every graphics adapter this machine has comes from, each named as the identity names one (DXGI
    /// by default; null when they cannot be read). The clean-up keeps the folders made on any of them.</summary>
    internal static Func<IReadOnlyList<string>?> AdaptersSource { get; set; } = EveryAdapter;

    /// <summary>Tests: what installs the callbacks on a new GPU context (the render thread's current display by default);
    /// its answer is <see cref="State"/>.</summary>
    internal static Func<string?> Installer { get; set; } = Install;

    /// <summary>Why it is not on (null once installed), for the native record.</summary>
    internal static string? State { get; private set; } = "not tried";

    /// <summary>The folder in use, once installed.</summary>
    internal static string? Folder => folder;

    /// <summary>Entries written, entries found, entries asked for and missing (this process).</summary>
    internal static int Writes, Hits, Misses;

    /// <summary>Entries written or found so far (a warm-up's programs each link one or the other).</summary>
    internal static int Entries => Volatile.Read(ref Writes) + Volatile.Read(ref Hits);

    /// <summary>Installs the callbacks on the render thread's current display, once per GPU context (the render thread,
    /// <paramref name="context"/> current).</summary>
    internal static void EnsureInstalled(nint context)
    {
        if (context == installedFor) return;
        installedFor = context;
        try { State = Installer(); }
        catch (Exception error) { State = "failed: " + error.GetType().Name + ": " + error.Message; }
    }

    /// <summary>The cache's root folder: the test's, or the app's (null when the storage paths are not bound).</summary>
    internal static string? Root()
    {
        if (FolderOverride is { } given) return given;
        try { return Path.Combine(StoragePaths.Root, "Cache", "GPU Programs"); }
        catch (Exception) { return null; }
    }

    /// <summary>The native libraries the programs are made and kept with: Skia's and Avalonia's ANGLE (its program
    /// binaries are its own format).</summary>
    internal static IReadOnlyList<string> NativeLibraries { get; } = ["libSkiaSharp.dll", "av_libglesv2.dll"];

    /// <summary>This build of the app: its module's id and the native libraries' versions (<see cref="NativeLibraries"/>, as
    /// installed beside it), so a release that patches only Skia or ANGLE is a new build too (the module's id alone does not
    /// change with them).</summary>
    internal static string Build { get; } =
        BuildOf(typeof(GpuProgramCache).Assembly.ManifestModule.ModuleVersionId.ToString("N"), NativeLibraries.Select(Installed).ToList());

    /// <summary>A build's name from its module's id and its native libraries (each "name version size", or "name none").</summary>
    internal static string BuildOf(string module, IReadOnlyList<string> natives) =>
        module + "-" + Hex(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", natives))))[..16];

    /// <summary>A native library where the process loads it from, beside the app (a published, self-contained app) or under
    /// runtimes\(rid)\native (a build run in place): "name version size", or "name none" when it is in neither.</summary>
    internal static string Installed(string name)
    {
        try
        {
            foreach (var path in new[]
            {
                Path.Combine(AppContext.BaseDirectory, name),
                Path.Combine(AppContext.BaseDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", name),
            })
            {
                if (!File.Exists(path)) continue;
                return name + " " + (FileVersionInfo.GetVersionInfo(path).FileVersion ?? "no version") + " " + new FileInfo(path).Length;
            }
        }
        catch (Exception) { }
        return name + " none";
    }

    /// <summary>The graphics adapter the app draws with and its driver, one line (null when they cannot be read).</summary>
    internal static string? Machine()
    {
        try { return MachineSource(); }
        catch (Exception) { return null; }
    }

    /// <summary>Every graphics adapter this machine has, each named as <see cref="Machine"/> names one (null when they
    /// cannot be read).</summary>
    internal static IReadOnlyList<string>? Adapters()
    {
        try { return AdaptersSource(); }
        catch (Exception) { return null; }
    }

    /// <summary>Reads the drawing adapter's identity ahead (a pool thread, at the start of each warm-up pass), so the render
    /// thread's install does not, and an install on a new GPU context names the adapter as it is then.</summary>
    internal static void Prepare() => machine = Machine();

    /// <summary>The identity the last <see cref="Prepare"/> read, an install's key names (tests, the native record).</summary>
    internal static string? Prepared => machine;

    /// <summary>
    /// A warm-up completed with its programs in <paramref name="keyFolder"/>: the root's marker names this build, that folder
    /// and the machine (GpuWarmup.IsWarm), and the folders other builds, adapters or drivers made are removed.
    /// </summary>
    internal static void MarkWarm(string keyFolder)
    {
        if (Root() is not { } root) return;
        var here = Machine();
        try { File.WriteAllLines(Path.Combine(root, "warm.txt"), [Build, Path.GetFileName(keyFolder), here ?? ""]); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        RemoveOthers(root, keyFolder, here);
    }

    /// <summary>True when <paramref name="keyFolder"/>'s key names this build and the machine as it is now (the programs in
    /// it were made on the adapter the app draws with now: an install reads the adapter when its warm-up pass begins, the
    /// marker when the pass ends, and the two differ when the app moved to another adapter in between).</summary>
    internal static bool MadeHere(string keyFolder, string here)
    {
        var key = Path.Combine(keyFolder, "key.txt");
        if (!File.Exists(key)) return false;
        var lines = File.ReadAllLines(key);
        return lines.Length >= 4 && lines[2] == Build && lines[3] == here;
    }

    // never read again: another build's folders, and those made on adapters or drivers the machine no longer has (a folder
    // made on another adapter it has, as that adapter is now, is kept for when the app draws with it again; a folder with no
    // key yet is being made by another instance and is left alone)
    private static void RemoveOthers(string root, string keep, string? here)
    {
        var known = new HashSet<string>(StringComparer.Ordinal);
        if (here is not null)
        {
            known.Add(here);
            if (Adapters() is { } all) known.UnionWith(all);
        }
        try
        {
            var kept = Path.GetFullPath(keep).TrimEnd(Path.DirectorySeparatorChar);
            foreach (var other in Directory.EnumerateDirectories(root))
            {
                if (string.Equals(Path.GetFullPath(other).TrimEnd(Path.DirectorySeparatorChar), kept, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var key = Path.Combine(other, "key.txt");
                    if (!File.Exists(key)) continue;
                    var lines = File.ReadAllLines(key);
                    if (lines.Length < 3 || lines[2] != Build || (here is not null && lines.Length >= 4 && !known.Contains(lines[3])))
                        Directory.Delete(other, recursive: true);
                }
                catch (IOException) { }                 // in use (another instance): next time
                catch (UnauthorizedAccessException) { }
            }
        }
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
        var made = Path.Combine(root, key);
        Directory.CreateDirectory(made);
        // the key written beside and moved over: another instance's clean-up never reads half of it
        ReplaceWhole(Path.Combine(made, "key.txt"), Encoding.UTF8.GetBytes(renderer + "\n" + version + "\n" + build + "\n" + (machine ??= Machine() ?? "") + "\n"));
        folder = made; // only now: entries go where the key is
        Marshal.GetDelegateForFunctionPointer<SetBlobCacheFuncs>(setFuncs)(display,
            Marshal.GetFunctionPointerForDelegate(setBlob), Marshal.GetFunctionPointerForDelegate(getBlob));
        return null;
    }

    /// <summary>Writes <paramref name="file"/> whole: beside it, then moved over it, so a reader never sees half a file; when
    /// the write or the move fails, nothing is left beside it and the failure is thrown.</summary>
    internal static void ReplaceWhole(string file, byte[] bytes)
    {
        var part = file + "." + Environment.ProcessId + ".part";
        try
        {
            File.WriteAllBytes(part, bytes);
            File.Move(part, file, overwrite: true);
        }
        catch (Exception)
        {
            try { File.Delete(part); } catch (Exception) { }
            throw;
        }
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    private static string? FileFor(nint key, nint keySize)
    {
        if (folder is null || key == 0 || keySize <= 0) return null;
        var bytes = new byte[keySize];
        Marshal.Copy(key, bytes, 0, bytes.Length);
        return Path.Combine(folder, Hex(SHA256.HashData(bytes))[..40] + ".bin");
    }

    // set: keep the entry (written whole: a failed write or move leaves no part)
    private static void Put(nint key, nint keySize, nint value, nint valueSize)
    {
        try
        {
            if (FileFor(key, keySize) is not { } file || value == 0 || valueSize <= 0) return;
            var bytes = new byte[valueSize];
            Marshal.Copy(value, bytes, 0, bytes.Length);
            ReplaceWhole(file, bytes);
            Interlocked.Increment(ref Writes);
        }
        catch (Exception) { } // a full or locked disk, the entry held open by another instance: the program is simply compiled next time
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

    /// <summary>
    /// The adapter Avalonia 12.1.2's ANGLE draws with, as AngleWin32EglDisplay.CreateD3D11Display picks it (no
    /// GraphicsAdapterSelectionCallback set): DXGI's first, or on ARM64 with several adapters the first whose description
    /// does not name an Adreno (its blocklist's fall-back), given the adapters' descriptions in DXGI's order.
    /// </summary>
    internal static int Drawn(IReadOnlyList<string?> descriptions, bool arm64)
    {
        if (!arm64 || descriptions.Count <= 1) return 0;
        for (var k = 0; k < descriptions.Count; k++)
            if (descriptions[k]?.ToLowerInvariant() is not { } description || !description.Contains("adreno", StringComparison.Ordinal))
                return k;
        return 0;
    }

    private static string? DrawingAdapter()
    {
        if (Enumerate() is not { Count: > 0 } adapters) return null;
        return adapters[Drawn(adapters.ConvertAll(adapter => adapter.Description), RuntimeInformation.ProcessArchitecture == Architecture.Arm64)].Line;
    }

    private static IReadOnlyList<string>? EveryAdapter() => Enumerate()?.Select(adapter => adapter.Line).OfType<string>().ToList();

    // DXGI's adapters in its order (IDXGIFactory1, as Avalonia makes it), each "vendor:device:subsystem:revision description
    // driver" with its user-mode driver's version as ANGLE reads it (IDXGIAdapter::CheckInterfaceSupport for IDXGIDevice), no
    // LUID (it changes every boot), and its description as Avalonia reads it; an adapter DXGI cannot describe keeps its place
    // with no line
    private static List<(string? Line, string? Description)>? Enumerate()
    {
        if (!OperatingSystem.IsWindows()) return null;
        if (CreateDXGIFactory1(new Guid("770aae78-f26f-4dba-a829-253c83d1b387"), out var factory) < 0 || factory == 0) return null;
        var found = new List<(string? Line, string? Description)>();
        try
        {
            var enumerate = Method<EnumAdapters1>(factory, 12);
            uint index = 0;
            while (enumerate(factory, index++, out var adapter) >= 0 && adapter != 0) // DXGI_ERROR_NOT_FOUND ends it
            {
                try
                {
                    if (Method<GetDesc1>(adapter, 10)(adapter, out var desc) < 0) { found.Add((null, null)); continue; }
                    var driver = Method<CheckInterfaceSupport>(adapter, 9)(adapter, new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c"), out var umd) >= 0
                        ? $"{(umd >> 48) & 0xFFFF}.{(umd >> 32) & 0xFFFF}.{(umd >> 16) & 0xFFFF}.{umd & 0xFFFF}" : "no driver version";
                    found.Add(($"{desc.VendorId:x4}:{desc.DeviceId:x4}:{desc.SubSysId:x8}:{desc.Revision:x2} {desc.Description.Trim()} {driver}", desc.Description));
                }
                finally { Marshal.Release(adapter); }
            }
        }
        finally { Marshal.Release(factory); }
        return found;
    }

    private static T Method<T>(nint self, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(self), slot * nint.Size));

    [DllImport("dxgi.dll", ExactSpelling = true)] private static extern int CreateDXGIFactory1(in Guid riid, out nint factory);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumAdapters1(nint self, uint index, out nint adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDesc1(nint self, out AdapterDesc1 desc);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CheckInterfaceSupport(nint self, in Guid iid, out long umdVersion);

    // DXGI_ADAPTER_DESC1
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDesc1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId, DeviceId, SubSysId, Revision;
        public nuint DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLowPart;
        public int LuidHighPart;
        public uint Flags;
    }
}
