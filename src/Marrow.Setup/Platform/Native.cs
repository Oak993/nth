using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Marrow.Setup.Platform;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3 and 5; unit U1, ED-NEW-inst-u1-1). The Win32 calls nth Setup makes, as source-generated
/// <c>LibraryImport</c>s (no runtime marshalling stubs), so the setup can be compiled ahead of time.
/// </summary>
internal static unsafe partial class Native
{
    // Window messages the setup window answers or watches.
    internal const uint WM_SYSCOLORCHANGE = 0x0015, WM_SETTINGCHANGE = 0x001A, WM_QUERYENDSESSION = 0x0011, WM_ENDSESSION = 0x0016;
    internal const uint WM_NCHITTEST = 0x0084, WM_NCMOUSEMOVE = 0x00A0, WM_NCLBUTTONDOWN = 0x00A1, WM_NCLBUTTONUP = 0x00A2, WM_NCLBUTTONDBLCLK = 0x00A3;
    internal const uint WM_MOUSEMOVE = 0x0200, WM_LBUTTONUP = 0x0202, WM_SYSCOMMAND = 0x0112, WM_CAPTURECHANGED = 0x0215, WM_NCMOUSELEAVE = 0x02A2;
    internal const uint WM_THEMECHANGED = 0x031A, WM_DWMCOMPOSITIONCHANGED = 0x031E, WM_DPICHANGED = 0x02E0;

    // WM_SYSCOMMAND commands (the low four bits are the system's own).
    internal const int SC_MINIMIZE = 0xF020, SC_MAXIMIZE = 0xF030, SC_CLOSE = 0xF060, SC_RESTORE = 0xF120;

    // Hit-test results.
    internal const int HTNOWHERE = 0, HTCLIENT = 1, HTCAPTION = 2, HTMINBUTTON = 8, HTMAXBUTTON = 9, HTCLOSE = 20;

    // SystemParametersInfo actions.
    internal const uint SPI_GETHIGHCONTRAST = 0x0042, SPI_SETHIGHCONTRAST = 0x0043, SPI_GETCLIENTAREAANIMATION = 0x1042, SPI_SETCLIENTAREAANIMATION = 0x1043;
    internal const uint HCF_HIGHCONTRASTON = 0x00000001;

    // GetSysColor indexes.
    internal const int COLOR_WINDOW = 5, COLOR_WINDOWTEXT = 8, COLOR_HIGHLIGHT = 13, COLOR_HIGHLIGHTTEXT = 14, COLOR_BTNFACE = 15, COLOR_GRAYTEXT = 17, COLOR_BTNTEXT = 18, COLOR_HOTLIGHT = 26;

    // Window styles.
    internal const int GWL_STYLE = -16;
    internal const long WS_THICKFRAME = 0x00040000, WS_MAXIMIZEBOX = 0x00010000, WS_MINIMIZEBOX = 0x00020000;

    // DWM attributes.
    internal const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_SYSTEMBACKDROP_TYPE = 38;

    internal const int SW_RESTORE = 9;
    internal const uint TME_LEAVE = 0x00000002, TME_NONCLIENT = 0x00000010;

    [StructLayout(LayoutKind.Sequential)]
    internal struct HighContrast
    {
        public uint Size;
        public uint Flags;
        public nint DefaultScheme;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TrackMouseEventInfo
    {
        public uint Size;
        public uint Flags;
        public nint Window;
        public uint HoverTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SystemParametersInfoBool(uint action, uint parameter, out int value, uint winIni);

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SystemParametersInfoHighContrast(uint action, uint parameter, ref HighContrast value, uint winIni);

    [LibraryImport("user32.dll")]
    internal static partial uint GetSysColor(int index);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShutdownBlockReasonCreate(nint window, string reason);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShutdownBlockReasonDestroy(nint window);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShutdownBlockReasonQuery(nint window, char* buffer, ref uint length);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AllowSetForegroundWindow(uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(nint window, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsIconic(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindow(nint window);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostMessage(nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    internal static partial nint SendMessage(nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    internal static partial nint SetCapture(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ReleaseCapture();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool TrackMouseEvent(ref TrackMouseEventInfo info);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static partial nint GetWindowLongPtr(nint window, int index);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowRect(nint window, out Rect rect);

    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmGetWindowAttribute(nint window, uint attribute, out int value, uint size);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AttachConsole(int processId);

    // Registry change notifications (Text size lives in HKCU\Software\Microsoft\Accessibility).
    internal const uint REG_NOTIFY_CHANGE_LAST_SET = 0x00000004, REG_NOTIFY_THREAD_AGNOSTIC = 0x10000000;

    [LibraryImport("advapi32.dll")]
    internal static partial int RegNotifyChangeKeyValue(SafeRegistryHandle key, [MarshalAs(UnmanagedType.Bool)] bool watchSubtree, uint filter, SafeWaitHandle changed, [MarshalAs(UnmanagedType.Bool)] bool asynchronous);

    // WinVerifyTrust (the setup's own signature, PLAN.md 3, step 3).
    internal const uint WTD_UI_NONE = 2, WTD_REVOKE_NONE = 0, WTD_CHOICE_FILE = 1, WTD_STATEACTION_VERIFY = 1, WTD_STATEACTION_CLOSE = 2;
    internal const uint WTD_REVOCATION_CHECK_NONE = 0x00000010, WTD_CACHE_ONLY_URL_RETRIEVAL = 0x00001000;
    internal const int TRUST_E_NOSIGNATURE = unchecked((int)0x800B0100), TRUST_E_SUBJECT_FORM_UNKNOWN = unchecked((int)0x800B0003), TRUST_E_PROVIDER_UNKNOWN = unchecked((int)0x800B0001);
    internal static readonly Guid WintrustActionGenericVerifyV2 = new(0x00AAC56B, 0xCD44, 0x11D0, 0x8C, 0xC2, 0x00, 0xC0, 0x4F, 0xC2, 0x95, 0xEE);

    [StructLayout(LayoutKind.Sequential)]
    internal struct WinTrustFileInfo
    {
        public uint StructSize;
        public nint FilePath;
        public nint File;
        public nint KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WinTrustData
    {
        public uint StructSize;
        public nint PolicyCallbackData;
        public nint SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public nint FileInfo;
        public uint StateAction;
        public nint StateData;
        public nint UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
        public nint SignatureSettings;
    }

    [LibraryImport("wintrust.dll")]
    internal static partial int WinVerifyTrust(nint window, ref Guid action, ref WinTrustData data);

    [LibraryImport("wintrust.dll")]
    internal static partial nint WTHelperProvDataFromStateData(nint stateData);

    [LibraryImport("wintrust.dll")]
    internal static partial nint WTHelperGetProvSignerFromChain(nint providerData, uint signerIndex, [MarshalAs(UnmanagedType.Bool)] bool counterSigner, uint counterSignerIndex);

    internal const uint CERT_NAME_SIMPLE_DISPLAY_TYPE = 4;

    [LibraryImport("crypt32.dll", EntryPoint = "CertGetNameStringW")]
    internal static partial uint CertGetNameString(nint certificate, uint type, uint flags, nint typeParameter, char* name, uint length);
}
