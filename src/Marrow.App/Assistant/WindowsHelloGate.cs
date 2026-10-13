// PORT-ONLY: Dextra AS-20 (DESIGN.md rev 5, 12.7 "Windows Hello for card changes and every purchase"; ED-NEW-dextra-as20-12).
// No Swift twin.
// Windows Hello as nth asks it: UserConsentVerifier over nth's own window (face, fingerprint or PIN). When Hello is not set up on
// this PC, Windows' own password prompt (CredUIPromptForWindowsCredentials) for the signed-in account, checked with LogonUser and
// compared with the account nth runs as, so another account's password does not pass. The password stays in pinned buffers
// only for the call that checks it: the packed buffer and the unpacked password are zeroed before they are let go. Unit tests
// never reach this class (the fake gate stands in); HelloGateNativeTests asks the real one, by hand.
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Marrow.Core.Port.Assistant;
using Windows.Security.Credentials.UI;

namespace Marrow.App.Assistant;

[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class WindowsHelloGate(Func<nint>? owner = null) : IHelloGate
{
    private const int ErrorCancelled = 1223;
    private const uint EnumerateCurrentUser = 0x200;    // CREDUIWIN_ENUMERATE_CURRENT_USER: only the signed-in account's tile
    private const uint PackProtected = 0x1;             // CRED_PACK_PROTECTED_CREDENTIALS
    private const int LogonInteractive = 2;
    private const int ProviderDefault = 0;
    private const int NameMax = 514;
    private const int PasswordMax = 256;

    public async Task<HelloResult> Verify(string reason, CancellationToken cancellationToken)
    {
        var message = "nth: " + reason;
        var window = owner?.Invoke() ?? await Dispatcher.UIThread.InvokeAsync(MainWindow);
        UserConsentVerifierAvailability availability;
        try { availability = await UserConsentVerifier.CheckAvailabilityAsync().AsTask(cancellationToken).ConfigureAwait(false); }
        catch (Exception error) when (error is COMException or InvalidOperationException or UnauthorizedAccessException) { availability = UserConsentVerifierAvailability.NotConfiguredForUser; }
        return availability switch
        {
            UserConsentVerifierAvailability.Available => await Hello(message, window, cancellationToken).ConfigureAwait(false),
            UserConsentVerifierAvailability.DeviceBusy => HelloResult.Busy,
            // Not set up for this account, no device, or turned off by policy: Windows' own password prompt instead.
            _ => await Task.Run(() => Password(message, window), cancellationToken).ConfigureAwait(false),
        };
    }

    private static nint MainWindow() =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } main }
            && main.TryGetPlatformHandle() is { HandleDescriptor: "HWND", Handle: not 0 } handle
            ? handle.Handle
            : 0;

    private static async Task<HelloResult> Hello(string message, nint window, CancellationToken cancellationToken)
    {
        UserConsentVerificationResult result;
        try
        {
            var asked = window != 0
                ? UserConsentVerifierInterop.RequestVerificationForWindowAsync(window, message)
                : UserConsentVerifier.RequestVerificationAsync(message);
            result = await asked.AsTask(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is COMException or InvalidOperationException or UnauthorizedAccessException) { return HelloResult.Unavailable; }
        return result switch
        {
            UserConsentVerificationResult.Verified => HelloResult.Verified,
            UserConsentVerificationResult.Canceled => HelloResult.Cancelled,
            UserConsentVerificationResult.RetriesExhausted => HelloResult.Failed,
            UserConsentVerificationResult.DeviceBusy => HelloResult.Busy,
            _ => HelloResult.Unavailable,
        };
    }

    // Windows' password prompt for the signed-in account. Only that account's password verifies.
    private static HelloResult Password(string message, nint window)
    {
        var info = new CredUiInfo { Size = Marshal.SizeOf<CredUiInfo>(), Parent = window, Message = message, Caption = "Windows Security" };
        uint package = 0;
        var asked = CredUIPromptForWindowsCredentialsW(ref info, 0, ref package, 0, 0, out var packed, out var packedSize, 0, EnumerateCurrentUser);
        if (asked == ErrorCancelled) return HelloResult.Cancelled;
        if (asked != 0 || packed == 0) return HelloResult.Unavailable;
        var user = GC.AllocateArray<char>(NameMax, pinned: true);
        var domain = GC.AllocateArray<char>(NameMax, pinned: true);
        var password = GC.AllocateArray<char>(PasswordMax, pinned: true);
        try
        {
            uint userLength = NameMax, domainLength = NameMax, passwordLength = PasswordMax;
            if (!CredUnPackAuthenticationBufferW(PackProtected, packed, packedSize, user, ref userLength, domain, ref domainLength, password, ref passwordLength))
                return HelloResult.Failed;
            Split(user, domain);
            if (!LogonUserW(user, domain[0] == '\0' ? null : domain, password, LogonInteractive, ProviderDefault, out var token)) return HelloResult.Failed;
            try
            {
                using var signedIn = new WindowsIdentity(token);
                using var current = WindowsIdentity.GetCurrent();
                return signedIn.User is { } sid && sid == current.User ? HelloResult.Verified : HelloResult.Failed;
            }
            finally { CloseHandle(token); }
        }
        finally
        {
            Array.Clear(password);
            Array.Clear(user);
            Array.Clear(domain);
            RtlZeroMemory(packed, packedSize);
            Marshal.FreeCoTaskMem(packed);
        }
    }

    // "DOMAIN\user" with no domain of its own is split; a Microsoft account signs in by its address alone.
    private static void Split(char[] user, char[] domain)
    {
        if (domain[0] != '\0') return;
        var length = Array.IndexOf(user, '\0');
        if (length < 0) length = user.Length;
        var slash = Array.IndexOf(user, '\\', 0, length);
        if (slash <= 0) return;
        var prefix = new string(user, 0, slash);
        if (!string.Equals(prefix, "MicrosoftAccount", StringComparison.OrdinalIgnoreCase)) prefix.CopyTo(0, domain, 0, Math.Min(prefix.Length, domain.Length - 1));
        var rest = length - slash - 1;
        Array.Copy(user, slash + 1, user, 0, rest);
        Array.Clear(user, rest, user.Length - rest);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CredUiInfo
    {
        public int Size;
        public nint Parent;
        public string Message;
        public string Caption;
        public nint Banner;
    }

    [DllImport("credui.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int CredUIPromptForWindowsCredentialsW(ref CredUiInfo info, int authError, ref uint authPackage, nint inBuffer, uint inBufferSize,
        out nint outBuffer, out uint outBufferSize, nint save, uint flags);

    [DllImport("credui.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredUnPackAuthenticationBufferW(uint flags, nint buffer, uint bufferSize, [In, Out] char[] user, ref uint userLength,
        [In, Out] char[] domain, ref uint domainLength, [In, Out] char[] password, ref uint passwordLength);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LogonUserW([In] char[] user, [In] char[]? domain, [In] char[] password, int logonType, int logonProvider, out nint token);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern void RtlZeroMemory(nint destination, nuint length);
}
