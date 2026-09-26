// PORT-ONLY: NSWorkspace.shared.open(_:) for a web address (MCPOAuth.swift:434), as a seam tests replace.
// ED-203 (PA-0020): refuse a relative or scheme-less-host URL before calling ShellExecuteW.
using System.Runtime.InteropServices;
using Marrow.Core.Port;

namespace Marrow.Services.Port;

/// <summary>
/// <c>NSWorkspace.shared.open(url)</c> for an http(s) address: hands it to the user's default browser. PR-MCP-139's
/// windows_path: "The browser is opened with the OS default-handler call; do not embed a web view". On Windows the
/// default-handler call is ShellExecuteW with the "open" verb, which starts the handler registered for the scheme.
/// The result is the Boolean NSWorkspace returns (true when the shell accepted the request). Tests override
/// <see cref="Open"/> so no browser is started (R-TEST-10).
/// </summary>
public static class Workspace
{
    public static Ambient<Func<SwiftURL, bool>> Open { get; } = new(() => ShellOpen);

    // ED-203: refuse a URL with no scheme (which also covers a host with no scheme, the two-slash relative-host
    // form: a URL with a host but no scheme still parses with Scheme null) before calling ShellExecuteW - the
    // same absolute-URL requirement NSWorkspace.shared.open enforces on the source's own platform. This changes
    // nothing for this seam's own caller (MCPOAuth.swift:434 always hands it an absolute http(s) URL). Exposed
    // separately from ShellOpen (review A29, finding 5) so a test can assert the gate's own answer without
    // reaching the real ShellExecuteW (R-TEST-10: no test launches a real shell action).
    internal static bool ShouldAttemptShellExecute(SwiftURL url) => url.Scheme is not null;

    // ShellExecuteW returns a value greater than 32 on success (SE_ERR_* values are 32 or less).
    private static bool ShellOpen(SwiftURL url) =>
        ShouldAttemptShellExecute(url)
        && ShellExecuteW(IntPtr.Zero, "open", url.AbsoluteString, null, null, ShowNormal).ToInt64() > 32;

    private const int ShowNormal = 1; // SW_SHOWNORMAL

    [DllImport("shell32.dll", EntryPoint = "ShellExecuteW", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr ShellExecuteW(IntPtr hwnd, string lpOperation, string lpFile, string? lpParameters,
        string? lpDirectory, int nShowCmd);
}
