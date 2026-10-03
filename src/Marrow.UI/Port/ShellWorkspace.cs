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
    // requirement NSWorkspace.shared.open enforces on the source's own platform. This seam's callers today are
    // MarkdownLinkOpener's own local-file hand-off (a resolved absolute path, drive-letter-prefixed or, for the
    // source's own POSIX shape, not), MarkdownLinkOpener's generic fallback for a link that carries a real scheme
    // (review A29: a bare "/"-rooted or "\\"-rooted string has none, so it is not "a URL that already carries
    // one" as an earlier version of this comment claimed), and AttachmentPreview/DownloadsPopover's own absolute
    // Windows or UNC path (review A29, finding 6: a UNC path is only unsafe from an unreviewed chat link, which
    // MarkdownLinkOpener.IsUnc already refuses before ever reaching this seam; a trusted caller's own UNC path,
    // e.g. a redirected Downloads folder, is exactly what NSWorkspace.shared.open opens on the source's platform).
    public static Action<string> OpenWithDefaultApp { get; set; } = path =>
    {
        if (Marrow.Core.Port.ShellGuard.InTestHost) return; // R-TEST-10: a test never opens a real browser or app
        if (ShouldAttemptShellExecute(path)) ShellExecuteW(IntPtr.Zero, "open", path, null, null, ShowNormal);
    };

    // :361 NSWorkspace.shared.activateFileViewerSelecting([attachment.url]): reveals the file in its folder,
    // selected. The target handed to ShellExecuteW is always the fixed "explorer.exe"; the caller's own path is
    // only ever its argument, so this needs no ED-203 gate (that gate is for a caller string used as the target).
    public static Action<string> RevealInFileExplorer { get; set; } = path =>
    {
        if (Marrow.Core.Port.ShellGuard.InTestHost) return; // R-TEST-10
        ShellExecuteW(IntPtr.Zero, "open", "explorer.exe", "/select,\"" + path + "\"", null, ShowNormal);
    };

    /// <summary>
    /// ED-203: accepted when the string is (1) an absolute URL with a real scheme (an <c>http(s)://</c> or
    /// <c>file://</c> link), (2) a drive-letter path (whose drive letter parses as a one-letter scheme,
    /// <see cref="MarkdownLinkOpener.LocalFilePath"/>'s own reconstructed strings), provided what follows the
    /// drive letter is itself rooted rather than drive-relative (review A29: "C:tool.exe" resolves against
    /// whatever the process's current directory on the C: drive happens to be - exactly the kind of relative
    /// reference NSWorkspace.shared.open refuses on the source's platform), or (3) scheme-less but rooted (a
    /// single leading "/", as the source's own POSIX local-file branch hands this seam, or a leading "\\"/"/\",
    /// a UNC path a trusted non-chat caller passed). Refused: a bare relative token ("calc") and the two-slash
    /// relative-host form ("//host/share") that a scheme-less URL with a Host but no Scheme parses into.
    /// </summary>
    internal static bool ShouldAttemptShellExecute(string path)
    {
        var url = SwiftURL.Create(path);
        if (url is null) return false;

        if (url.Scheme is { Length: 1 } drive && IsAsciiLetter(drive[0]))
            return url.Host is not null || IsRootedPath(url.Path); // review A29: refuse a drive-relative path

        if (url.Scheme is not null) return true;
        if (url.Host is not null) return false; // the two-slash relative-host form ("//host/share")
        return IsRootedPath(url.Path);
    }

    private static bool IsRootedPath(string path) => path.Length > 0 && (path[0] == '/' || path[0] == '\\');

    private static bool IsAsciiLetter(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

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
    /// looks like. The extension that decides this is read off <see cref="CanonicalFileName"/>, not the literal
    /// path text (review A29: Windows itself would resolve the two to the same file).
    /// </summary>
    public static bool ShouldReveal(string path) => !Directory.Exists(path) && IsDangerousExtension(ExtensionOf(CanonicalFileName(path)));

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

    // review A29: Win32 file-name resolution (CreateFileW and everything built on it, including ShellExecuteW)
    // drops trailing dots and spaces from a literal name - "tool.exe " and "tool.exe." both resolve to "tool.exe"
    // on disk - and "<name>::$DATA" addresses <name>'s own unnamed data stream, the same underlying file, opened
    // the same way. ExtensionOf must see the name Windows will actually open, or a dangerous extension hidden
    // behind either form (a trailing "%20" once the URL is decoded; an explicit ADS suffix) goes unrecognised and
    // ShouldReveal answers false for what is, on disk, exactly the type ED-204's floor list names.
    internal static string CanonicalFileName(string path)
    {
        const string dataStreamSuffix = "::$DATA";
        var name = path.EndsWith(dataStreamSuffix, StringComparison.OrdinalIgnoreCase)
            ? path[..^dataStreamSuffix.Length]
            : path;
        var end = name.Length;
        while (end > 0 && (name[end - 1] == ' ' || name[end - 1] == '.')) end--;
        return name[..end];
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
