// PORT-ONLY: the two NSWorkspace calls AttachmentPreview.swift's footer makes directly from a View
// (:361 activateFileViewerSelecting, :366 open) and other UI files call the same way. Marrow.Services.Port.Workspace
// (MCPOAuth.swift:434) is a Services-layer seam for opening a web URL; the UI layer may depend only on Core
// (section 2.1), so this is a small UI-layer twin of the same ShellExecuteW technique for local-file actions.
// ED-203/ED-204 (PA-0020, OD-0026): the generic-opener hardening and the reveal-vs-open mechanism
// UI.Markdown.MarkdownLinkOpener (LinkParagraphView.swift:226-245) uses.
using System.IO;
using System.Runtime.InteropServices;
using Marrow.Core.Port;

namespace Marrow.UI.Port;

/// <summary><c>NSWorkspace.shared.open(url)</c> and <c>.activateFileViewerSelecting([url])</c> for a local file, as a
/// seam tests can replace (R-TEST-10: no test launches a real shell action).</summary>
public static class ShellWorkspace
{
    // ED-203: refuse a relative or scheme-less-host string before calling ShellExecuteW, the same absolute-URL
    // requirement NSWorkspace.shared.open enforces on the source's own platform. This changes nothing for this
    // seam's own callers today (AttachmentPreview.swift's footer always hands an absolute Windows path, whose
    // drive letter parses as a one-letter scheme; MarkdownLinkOpener's generic branch only ever forwards a URL
    // that already carries one).
    public static Action<string> OpenWithDefaultApp { get; set; } = path =>
    {
        if (ShouldAttemptShellExecute(path)) ShellExecuteW(IntPtr.Zero, "open", path, null, null, ShowNormal);
    };

    // :361 NSWorkspace.shared.activateFileViewerSelecting([attachment.url]): reveals the file in its folder,
    // selected. The target handed to ShellExecuteW is always the fixed "explorer.exe"; the caller's own path is
    // only ever its argument, so this needs no ED-203 gate (that gate is for a caller string used as the target).
    public static Action<string> RevealInFileExplorer { get; set; } = path =>
        ShellExecuteW(IntPtr.Zero, "open", "explorer.exe", "/select,\"" + path + "\"", null, ShowNormal);

    // ED-203: (a) no URL scheme, or (b) a host with no scheme (the two-slash relative-host form, "//host/share").
    // (b) is always also (a) once parsed - a URL with a host but no scheme still parses with Scheme null - kept
    // as its own clause so both of the decision's named shapes ("calc"; "//host/share") read directly off it.
    internal static bool ShouldAttemptShellExecute(string path)
    {
        var url = SwiftURL.Create(path);
        if (url is null || url.Scheme is null) return false; // (a)
        if (url.Host is not null && url.Scheme is null) return false; // (b)
        return true;
    }

    // ED-204: Windows' own "potentially dangerous file type" test (the one its downloaded-attachment handling
    // uses), so the floor list below follows whatever a stock Windows 11 answers, even where a machine's own
    // registry disagrees.
    public static Func<string, bool> IsDangerousFileType { get; set; } = AssocIsDangerousNative;

    // ED-204's fixed floor, held even where AssocIsDangerous disagrees (.ps1 is included because a user may have
    // associated it with PowerShell).
    private static readonly string[] DangerousExtensionFloor =
    [
        ".exe", ".com", ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta",
        ".msi", ".msp", ".mst", ".scr", ".lnk", ".url", ".cpl", ".pif", ".reg", ".msc", ".scf", ".inf", ".appref-ms",
    ];

    /// <summary>
    /// ED-204: a local chat-link target of a type Windows runs directly is revealed, selected, in File Explorer
    /// instead of opened. A directory is not a program: it opens, as Finder opens a folder, whatever its name
    /// looks like.
    /// </summary>
    public static bool ShouldReveal(string path) => !Directory.Exists(path) && IsDangerousExtension(ExtensionOf(path));

    internal static bool IsDangerousExtension(string extension)
    {
        if (extension.Length == 0) return false;
        foreach (var candidate in DangerousExtensionFloor)
            if (string.Equals(candidate, extension, StringComparison.OrdinalIgnoreCase)) return true;
        return IsDangerousFileType(extension);
    }

    // The last "."-led segment after the last path separator ('/' or '\', either may appear in a Windows path
    // reaching here); never System.IO.Path.GetExtension, which only recognizes '/' as a separator on Linux, so a
    // dev-machine test run would silently drift from the Windows target for a backslash-separated path.
    internal static string ExtensionOf(string path)
    {
        var separator = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
        var dot = path.LastIndexOf('.');
        return dot > separator ? path[dot..] : "";
    }

    private const int ShowNormal = 1; // SW_SHOWNORMAL

    [DllImport("shell32.dll", EntryPoint = "ShellExecuteW", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr ShellExecuteW(IntPtr hwnd, string lpOperation, string lpFile, string? lpParameters,
        string? lpDirectory, int nShowCmd);

    [DllImport("shlwapi.dll", EntryPoint = "AssocIsDangerous", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool AssocIsDangerousNative(string pszAssocExtension);
}
