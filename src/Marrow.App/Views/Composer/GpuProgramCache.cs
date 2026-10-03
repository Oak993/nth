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
// Each folder's key.txt names the renderer, the GL version, the build and the machine: this PC's graphics adapters with
// their driver versions (DXGI), so GpuWarmup.IsWarm can tell, before any window is drawn, that a driver update or a new GPU
// leaves the kept programs unproven (OD-0096: the first-run screen shows again). A completed warm-up removes the folders of
// other builds and of this machine's earlier adapters or drivers.

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

    /// <summary>Tests: where the machine's identity comes from (DXGI by default; null when it cannot be read).</summary>
    internal static Func<string?> MachineSource { get; set; } = Adapters;

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

    /// <summary>This machine's graphics adapters and their drivers, one line (null when they cannot be read).</summary>
    internal static string? Machine()
    {
        try { return MachineSource(); }
        catch (Exception) { return null; }
    }

    /// <summary>Reads the machine's identity ahead (a pool thread), so the render thread's install does not.</summary>
    internal static void Prepare() => machine ??= Machine();

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

    /// <summary>True when <paramref name="keyFolder"/>'s key names this build and this machine as it is now.</summary>
    internal static bool MadeHere(string keyFolder, string here)
    {
        var key = Path.Combine(keyFolder, "key.txt");
        if (!File.Exists(key)) return false;
        var lines = File.ReadAllLines(key);
        return lines.Length >= 4 && lines[2] == Build && lines[3] == here;
    }

    // never read again: another build's folders, and this machine's from adapters or drivers it no longer has (a folder with
    // no key yet is being made by another instance and is left alone)
    private static void RemoveOthers(string root, string keep, string? here)
    {
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
                    if (lines.Length < 3 || lines[2] != Build || (here is not null && lines.Length >= 4 && lines[3] != here))
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
        var keyFile = Path.Combine(made, "key.txt");
        var part = keyFile + "." + Environment.ProcessId + ".part";
        File.WriteAllText(part, renderer + "\n" + version + "\n" + build + "\n" + (machine ??= Machine() ?? "") + "\n");
        File.Move(part, keyFile, overwrite: true);
        folder = made; // only now: entries go where the key is
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

    // set: keep the entry (written beside, then moved over, so a reader never sees half a file; a failed move leaves no part)
    private static void Put(nint key, nint keySize, nint value, nint valueSize)
    {
        string? part = null;
        try
        {
            if (FileFor(key, keySize) is not { } file || value == 0 || valueSize <= 0) return;
            var bytes = new byte[valueSize];
            Marshal.Copy(value, bytes, 0, bytes.Length);
            part = file + "." + Environment.ProcessId + ".part";
            File.WriteAllBytes(part, bytes);
            File.Move(part, file, overwrite: true);
            Interlocked.Increment(ref Writes);
        }
        catch (Exception) // a full or locked disk, the entry held open by another instance: the program is simply compiled next time
        {
            if (part is not null) try { File.Delete(part); } catch (Exception) { }
        }
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

    // DXGI: every hardware adapter (the software one only where there is none) with its user-mode driver's version, as
    // ANGLE reads it (IDXGIAdapter::CheckInterfaceSupport for IDXGIDevice), sorted, so the order adapters enumerate in does
    // not matter; no LUID (it changes every boot)
    private static string? Adapters()
    {
        if (!OperatingSystem.IsWindows()) return null;
        if (CreateDXGIFactory1(new Guid("770aae78-f26f-4dba-a829-253c83d1b387"), out var factory) < 0 || factory == 0) return null;
        var hardware = new List<string>();
        var software = new List<string>();
        try
        {
            var enumerate = Method<EnumAdapters1>(factory, 12);
            uint index = 0;
            while (enumerate(factory, index++, out var adapter) >= 0 && adapter != 0) // DXGI_ERROR_NOT_FOUND ends it
            {
                try
                {
                    if (Method<GetDesc1>(adapter, 10)(adapter, out var desc) < 0) continue;
                    var driver = Method<CheckInterfaceSupport>(adapter, 9)(adapter, new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c"), out var umd) >= 0
                        ? $"{(umd >> 48) & 0xFFFF}.{(umd >> 32) & 0xFFFF}.{(umd >> 16) & 0xFFFF}.{umd & 0xFFFF}" : "no driver version";
                    var line = $"{desc.VendorId:x4}:{desc.DeviceId:x4}:{desc.SubSysId:x8}:{desc.Revision:x2} {desc.Description.Trim()} {driver}";
                    ((desc.Flags & 2) != 0 ? software : hardware).Add(line); // DXGI_ADAPTER_FLAG_SOFTWARE
                }
                finally { Marshal.Release(adapter); }
            }
        }
        finally { Marshal.Release(factory); }
        var found = hardware.Count > 0 ? hardware : software;
        found.Sort(StringComparer.Ordinal);
        return string.Join(" | ", found);
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
