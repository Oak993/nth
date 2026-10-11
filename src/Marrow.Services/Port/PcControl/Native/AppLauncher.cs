// PORT-ONLY: PC control (OD-0140, ED-NEW-pcctl-1; DESIGN.md 3.1). Installed apps from the Start menu's shell:AppsFolder
// (each item's display name and parsing name: an AUMID for packaged apps, a known-folder path for desktop apps), and
// launching by ShellExecuteEx with no arguments: an .exe by its full path, or an AppsFolder item by its id. No command
// line, no cmd, no PowerShell (ED-NEW-pcctl-3). Denied apps are refused before anything starts. Never used by automated tests.
using System.Runtime.InteropServices;
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl.Native;

public sealed class AppLauncher : IAppLauncher
{
    private const uint SIGDN_NORMALDISPLAY = 0;
    private const uint SIGDN_PARENTRELATIVEPARSING = 0x80018001;
    private static readonly Guid BHID_EnumItems = new("94f60519-2850-4924-aa5a-d15e84868039");
    private static readonly Guid IID_IShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");
    private static readonly Guid IID_IEnumShellItems = new("70629033-e363-4a28-a567-0db78006e6d7");

    public IReadOnlyList<PcInstalledApp> Installed()
    {
        var apps = new List<PcInstalledApp>();
        var shellItem = IID_IShellItem;
        if (SHCreateItemFromParsingName("shell:AppsFolder", 0, ref shellItem, out var folder) != 0 || folder is null) return apps;
        try
        {
            var handler = BHID_EnumItems;
            var enumIid = IID_IEnumShellItems;
            if (folder.BindToHandler(0, ref handler, ref enumIid, out var enumerator) != 0 || enumerator is not IEnumShellItems items) return apps;
            try
            {
                var batch = new IShellItem?[1];
                while (items.Next(1, batch, out var fetched) == 0 && fetched == 1 && batch[0] is { } item)
                {
                    try
                    {
                        if (item.GetDisplayName(SIGDN_NORMALDISPLAY, out var name) == 0 && item.GetDisplayName(SIGDN_PARENTRELATIVEPARSING, out var id) == 0
                            && !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(id))
                            apps.Add(new PcInstalledApp(name, id));
                    }
                    finally { Marshal.ReleaseComObject(item); }
                }
            }
            finally { Marshal.ReleaseComObject(items); }
        }
        finally { Marshal.ReleaseComObject(folder); }
        return apps.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public PcLaunch Launch(string appIdOrPath)
    {
        if (Target(appIdOrPath) is not { } target) return new PcLaunch(false, null, "give an app_id from pc_list_apps or the full path of an .exe");
        var info = new Win32.SHELLEXECUTEINFO
        {
            Size = Marshal.SizeOf<Win32.SHELLEXECUTEINFO>(),
            Mask = Win32.SEE_MASK_NOCLOSEPROCESS | Win32.SEE_MASK_FLAG_NO_UI,
            Verb = "open",
            File = target,
            // Started without taking the user's focus (owner 2026-10-11: Tactus works in the background).
            Show = Win32.SW_SHOWNOACTIVATE,
        };
        if (!Win32.ShellExecuteEx(ref info)) return new PcLaunch(false, null, "Windows could not start it (error " + Marshal.GetLastWin32Error().ToString(System.Globalization.CultureInfo.InvariantCulture) + ")");
        int? processId = null;
        if (info.Process != 0)
        {
            processId = Win32.GetProcessId(info.Process);
            Win32.CloseHandle(info.Process);
        }
        return new PcLaunch(true, processId, null);
    }

    /// <summary>What ShellExecuteEx opens: a full .exe path that exists, or "shell:AppsFolder\&lt;id&gt;"; null for anything else.</summary>
    public static string? Target(string? appIdOrPath)
    {
        if (string.IsNullOrWhiteSpace(appIdOrPath)) return null;
        var text = appIdOrPath.Trim();
        if (text.IndexOfAny(['"', '\r', '\n', '\0', '|', '<', '>', '^', '&', '/', '%']) >= 0) return null;
        if (Path.IsPathFullyQualified(text))
            return text.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(text) ? text : null;
        // An AppsFolder id is an AUMID ("Publisher.App_hash!App") or a parsing name ("{known-folder-guid}\dir\app.exe"):
        // never a URL, a drive path, a UNC path or another shell: location.
        if (text.Contains(':', StringComparison.Ordinal) || text.StartsWith('\\') || text.StartsWith('/')) return null;
        return "shell:AppsFolder\\" + text;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int SHCreateItemFromParsingName(string path, nint bindContext, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItem? item);

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        [PreserveSig] int BindToHandler(nint bindContext, ref Guid handler, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object? result);
        [PreserveSig] int GetParent([MarshalAs(UnmanagedType.Interface)] out IShellItem? parent);
        [PreserveSig] int GetDisplayName(uint sigdn, [MarshalAs(UnmanagedType.LPWStr)] out string? name);
        [PreserveSig] int GetAttributes(uint mask, out uint attributes);
        [PreserveSig] int Compare([MarshalAs(UnmanagedType.Interface)] IShellItem other, uint hint, out int order);
    }

    [ComImport, Guid("70629033-e363-4a28-a567-0db78006e6d7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumShellItems
    {
        [PreserveSig] int Next(uint count, [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.Interface, SizeParamIndex = 0)] IShellItem?[] items, out uint fetched);
        [PreserveSig] int Skip(uint count);
        [PreserveSig] int Reset();
        [PreserveSig] int Clone([MarshalAs(UnmanagedType.Interface)] out IEnumShellItems? clone);
    }
}
