using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Marrow.Core.Support;

namespace Marrow.Setup.Port;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 8a, "Open nth"; VELOPACK_CHECK.md finding 6; ED-NEW-inst-fix-4). Starts nth as the
/// signed-in user when the setup itself runs elevated: Explorer, which runs un-elevated, is asked to start it. The chain is
/// the documented one: <c>IShellWindows.FindWindowSW(SWC_DESKTOP)</c> gives the desktop's browser, its active view's
/// background object is an <c>IShellFolderViewDual</c>, whose <c>Application</c> is Explorer's <c>IShellDispatch2</c>, and
/// its <c>ShellExecute</c> starts the program inside Explorer. A plain start from an elevated setup would give nth the
/// setup's admin token (its files, its drag and drop and its own updates would then belong to an elevated process).
/// The calls run on their own STA thread, bounded by a patience, so a hung Explorer never holds the window.
/// No fallback to an elevated start: when Explorer cannot be reached the start fails and the window stays on Ready.
/// </summary>
internal static unsafe partial class UserShellStart
{
    private static readonly Guid ShellWindowsClsid = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    private static readonly Guid ShellWindowsIid = new("85CB6900-4D95-11CF-960C-0080C7F4EE85");
    private static readonly Guid ServiceProviderIid = new("6D5140C1-7436-11CE-8034-00AA006009FA");
    private static readonly Guid TopLevelBrowserSid = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid ShellBrowserIid = new("000214E2-0000-0000-C000-000000000046");
    private static readonly Guid DispatchIid = new("00020400-0000-0000-C000-000000000046");
    private static readonly Guid FolderViewDualIid = new("E7A1AF80-4D96-11CF-960C-0080C7F4EE85");
    private static readonly Guid ShellDispatch2Iid = new("A4C6892C-3BA9-11D2-9DEA-00C04FB16162");

    private const uint ClsctxLocalServer = 0x4; // ShellWindows is served by Explorer
    private const int SwcDesktop = 8, SwfoNeedDispatch = 1, SvgioBackground = 0, SwShowNormal = 1;

    // Vtable slots (shldisp.h, shobjidl.h): IUnknown 0-2; IDispatch 3-6; IOleWindow 3-4.
    internal const int QueryInterfaceSlot = 0;
    internal const int FindWindowSWSlot = 15;         // IShellWindows: get_Count 7, Item 8, _NewEnum 9, Register 10, RegisterPending 11, Revoke 12, OnNavigate 13, OnActivated 14, FindWindowSW 15
    internal const int QueryServiceSlot = 3;          // IServiceProvider
    internal const int QueryActiveShellViewSlot = 15; // IShellBrowser: InsertMenusSB 5 ... GetControlWindow 13, SendControlMsg 14, QueryActiveShellView 15
    internal const int GetItemObjectSlot = 15;        // IShellView: TranslateAccelerator 5 ... SaveViewState 13, SelectItem 14, GetItemObject 15
    internal const int GetApplicationSlot = 7;        // IShellFolderViewDual: get_Application 7
    internal const int ShellExecuteSlot = 31;         // IShellDispatch: 23 methods at 7-29; IShellDispatch2: IsRestricted 30, ShellExecute 31

    /// <summary>Starts <paramref name="launch"/> through Explorer; false when Explorer could not be reached or did not answer in time.</summary>
    public static bool Start(LaunchTarget launch, TimeSpan patience)
    {
        ArgumentNullException.ThrowIfNull(launch);
        var started = 0;
        var thread = new Thread(() => Volatile.Write(ref started, TryStart(launch) ? 1 : 0)) { IsBackground = true, Name = "nth Setup: Open nth (STA)" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return thread.Join(patience) && Volatile.Read(ref started) == 1;
    }

    private static bool TryStart(LaunchTarget launch)
    {
        nint windows = 0, desktop = 0, provider = 0, browser = 0, view = 0, background = 0, folderView = 0, application = 0, shell = 0;
        try
        {
            var clsid = ShellWindowsClsid;
            var iid = ShellWindowsIid;
            if (CoCreateInstance(&clsid, 0, ClsctxLocalServer, &iid, &windows) < 0 || windows == 0) return false;
            ComVariant location = default, root = default; // VT_EMPTY, VT_EMPTY: the desktop
            var hwnd = 0;
            var find = (delegate* unmanaged[Stdcall]<nint, ComVariant*, ComVariant*, int, int*, int, nint*, int>)Slot(windows, FindWindowSWSlot);
            if (find(windows, &location, &root, SwcDesktop, &hwnd, SwfoNeedDispatch, &desktop) != 0 || desktop == 0) return false; // S_FALSE: no desktop window
            if (QueryInterface(desktop, ServiceProviderIid, out provider) < 0) return false;
            var sid = TopLevelBrowserSid;
            var browserIid = ShellBrowserIid;
            var queryService = (delegate* unmanaged[Stdcall]<nint, Guid*, Guid*, nint*, int>)Slot(provider, QueryServiceSlot);
            if (queryService(provider, &sid, &browserIid, &browser) < 0 || browser == 0) return false;
            var activeView = (delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(browser, QueryActiveShellViewSlot);
            if (activeView(browser, &view) < 0 || view == 0) return false;
            var dispatchIid = DispatchIid;
            var itemObject = (delegate* unmanaged[Stdcall]<nint, uint, Guid*, nint*, int>)Slot(view, GetItemObjectSlot);
            if (itemObject(view, SvgioBackground, &dispatchIid, &background) < 0 || background == 0) return false;
            if (QueryInterface(background, FolderViewDualIid, out folderView) < 0) return false;
            var getApplication = (delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(folderView, GetApplicationSlot);
            if (getApplication(folderView, &application) < 0 || application == 0) return false;
            if (QueryInterface(application, ShellDispatch2Iid, out shell) < 0) return false;
            AllowForeground(hwnd);
            return Execute(shell, launch);
        }
        finally
        {
            foreach (var held in (ReadOnlySpan<nint>)[shell, application, folderView, background, view, browser, provider, desktop, windows])
            {
                if (held != 0) _ = Marshal.Release(held);
            }
        }
    }

    private static bool Execute(nint shell, LaunchTarget launch)
    {
        var file = Marshal.StringToBSTR(launch.Target);
        var arguments = ComVariant.CreateRaw(VarEnum.VT_BSTR, Marshal.StringToBSTR(launch.Arguments));
        var directory = ComVariant.CreateRaw(VarEnum.VT_BSTR, Marshal.StringToBSTR(launch.WorkingDirectory));
        var operation = ComVariant.CreateRaw(VarEnum.VT_BSTR, Marshal.StringToBSTR("")); // the default verb
        var show = ComVariant.CreateRaw(VarEnum.VT_I4, SwShowNormal);
        try
        {
            var execute = (delegate* unmanaged[Stdcall]<nint, nint, ComVariant, ComVariant, ComVariant, ComVariant, int>)Slot(shell, ShellExecuteSlot);
            return execute(shell, file, arguments, directory, operation, show) >= 0;
        }
        finally
        {
            Marshal.FreeBSTR(file);
            arguments.Dispose();
            directory.Dispose();
            operation.Dispose();
            show.Dispose();
        }
    }

    // nth comes up in front: Explorer may hand the foreground on to what it starts.
    private static void AllowForeground(int desktopWindow)
    {
        if (desktopWindow != 0 && GetWindowThreadProcessId(desktopWindow, out var explorer) != 0) _ = AllowSetForegroundWindow(explorer);
    }

    private static nint Slot(nint instance, int slot) => (*(nint**)instance)[slot];

    private static int QueryInterface(nint instance, Guid iid, out nint result)
    {
        nint found = 0;
        var query = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Slot(instance, QueryInterfaceSlot);
        var hr = query(instance, &iid, &found);
        result = hr < 0 ? 0 : found;
        return hr;
    }

    [LibraryImport("ole32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int CoCreateInstance(Guid* clsid, nint outer, uint context, Guid* iid, nint* instance);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetWindowThreadProcessId(nint window, out uint processId);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(uint processId);
}
