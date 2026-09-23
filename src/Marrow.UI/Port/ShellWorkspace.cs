// PORT-ONLY: the two NSWorkspace calls AttachmentPreview.swift's footer makes directly from a View
// (:361 activateFileViewerSelecting, :366 open) and other UI files call the same way. Marrow.Services.Port.Workspace
// (MCPOAuth.swift:434) is a Services-layer seam for opening a web URL; the UI layer may depend only on Core
// (section 2.1), so this is a small UI-layer twin of the same ShellExecuteW technique for local-file actions.
using System.Runtime.InteropServices;

namespace Marrow.UI.Port;

/// <summary><c>NSWorkspace.shared.open(url)</c> and <c>.activateFileViewerSelecting([url])</c> for a local file, as a
/// seam tests can replace (R-TEST-10: no test launches a real shell action).</summary>
public static class ShellWorkspace
{
    public static Action<string> OpenWithDefaultApp { get; set; } = path => ShellExecuteW(IntPtr.Zero, "open", path, null, null, ShowNormal);

    // :361 NSWorkspace.shared.activateFileViewerSelecting([attachment.url]): reveals the file in its folder, selected.
    public static Action<string> RevealInFileExplorer { get; set; } = path =>
        ShellExecuteW(IntPtr.Zero, "open", "explorer.exe", "/select,\"" + path + "\"", null, ShowNormal);

    private const int ShowNormal = 1; // SW_SHOWNORMAL

    [DllImport("shell32.dll", EntryPoint = "ShellExecuteW", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr ShellExecuteW(IntPtr hwnd, string lpOperation, string lpFile, string? lpParameters,
        string? lpDirectory, int nShowCmd);
}
