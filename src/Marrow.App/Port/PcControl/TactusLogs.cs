// PORT-ONLY: Tactus, the activity log's places (OD-0140, ED-NEW-pcctl-28; DESIGN.md 4.6, 4.7 "Activity log"; unit 14). No
// Swift twin. Each chat's log is %LOCALAPPDATA%\Marrow Code\pc-control\<thread>\<yyyy-mm-dd>.jsonl (PcAuditLog). Settings'
// "Open log folder" opens the root, a chat's own log opens its folder, and "Clear logs" moves the logs to the Recycle Bin,
// never deleting them outright. The shell and the Recycle Bin are parts, so tests never open a window or touch the bin.
using Marrow.Services.Port.PcControl;
using Marrow.Core.Support;

namespace Marrow.App.Port.PcControl;

public sealed class TactusLogs
{
    private readonly Func<string> root;
    private readonly Action<string> open;
    private readonly Action<string> recycle;

    /// <param name="root">The logs' root folder (default: the app's pc-control data folder).</param>
    /// <param name="open">Opens a folder in the shell (default: File Explorer).</param>
    /// <param name="recycle">Moves a folder to the Recycle Bin (default: the shell's own move).</param>
    public TactusLogs(Func<string>? root = null, Action<string>? open = null, Action<string>? recycle = null)
    {
        this.root = root ?? (() => StoragePaths.Directory("pc-control"));
        this.open = open ?? OpenInExplorer;
        this.recycle = recycle ?? ToRecycleBin;
    }

    public string Root => root();

    /// <summary>The folder of one chat's log (the folder PcAuditLog writes, whether or not it exists yet).</summary>
    public string FolderOf(string thread) => Path.GetDirectoryName(new PcAuditLog(thread, root).PathFor(DateTimeOffset.Now))!;

    /// <summary>Whether a chat has any log yet.</summary>
    public bool HasLog(string thread) => Directory.Exists(FolderOf(thread)) && Directory.EnumerateFiles(FolderOf(thread), "*.jsonl").Any();

    /// <summary>"Open log folder": the root, made first so the shell always has a folder to show.</summary>
    public void OpenRoot()
    {
        Directory.CreateDirectory(Root);
        open(Root);
    }

    /// <summary>A chat's own log folder; false when the chat has no log yet.</summary>
    public bool OpenChat(string thread)
    {
        if (!HasLog(thread)) return false;
        open(FolderOf(thread));
        return true;
    }

    /// <summary>"Clear logs": each chat's log folder goes to the Recycle Bin; the count moved.</summary>
    public int Clear()
    {
        if (!Directory.Exists(Root)) return 0;
        var moved = 0;
        foreach (var folder in Directory.EnumerateDirectories(Root).ToList())
        {
            recycle(folder);
            moved++;
        }
        return moved;
    }

    private static void OpenInExplorer(string folder) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe") { ArgumentList = { folder }, UseShellExecute = false })?.Dispose();

    // SHFileOperation with FOF_ALLOWUNDO: the shell's own move to the Recycle Bin, with no prompt and no progress window
    private static void ToRecycleBin(string folder)
    {
        var operation = new ShFileOperation
        {
            Func = 3, // FO_DELETE
            From = folder + "\0", // the list ends with a second null (the marshaller adds the first)
            Flags = 0x40 | 0x10 | 0x4 | 0x400, // FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI
        };
        var result = SHFileOperationW(ref operation);
        if (result != 0) throw new IOException("The log folder could not be moved to the Recycle Bin (" + result.ToString(System.Globalization.CultureInfo.InvariantCulture) + ").");
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct ShFileOperation
    {
        public IntPtr Window;
        public uint Func;
        public string From;
        public string? To;
        public ushort Flags;
        public int AnyAborted;
        public IntPtr NameMappings;
        public string? ProgressTitle;
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SHFileOperationW(ref ShFileOperation operation);
}
