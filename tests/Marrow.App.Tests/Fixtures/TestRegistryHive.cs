using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 10, unit U5, ED-NEW-inst-u5-1). A private registry hive in a temporary file, loaded
/// with <c>RegLoadAppKey</c>: its root key stands in for <c>HKEY_CURRENT_USER</c>, so <c>RegistryUserHive</c> runs on real
/// registry calls without ever opening the real <c>HKCU</c>. The hive is visible to this process only and is unloaded when
/// its last handle closes; the file is deleted with its folder.
/// </summary>
internal sealed class TestRegistryHive : IDisposable
{
    private const int KeyAllAccess = 0xF003F;

    private readonly string folder;

    public TestRegistryHive()
    {
        folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "nth-tests", Guid.NewGuid().ToString("N"))).FullName;
        var error = RegLoadAppKeyW(Path.Combine(folder, "user.dat"), out var handle, KeyAllAccess, 0, 0);
        if (error != 0) throw new Win32Exception(error, "RegLoadAppKey failed.");
        Root = RegistryKey.FromHandle(new SafeRegistryHandle(handle, ownsHandle: true));
    }

    /// <summary>The hive's root key (in place of <c>Registry.CurrentUser</c>).</summary>
    public RegistryKey Root { get; }

    /// <summary>Creates a key (and its parents) as Velopack or the user would; the code under test never creates keys.</summary>
    public RegistryKey CreateKey(string subKey) => Root.CreateSubKey(subKey, writable: true);

    public void Dispose()
    {
        Root.Dispose();
        try { Directory.Delete(folder, recursive: true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { } // the hive unloads a moment later; %TEMP% is cleaned by Windows
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int RegLoadAppKeyW(string file, out nint key, int desired, int options, int reserved);
}
