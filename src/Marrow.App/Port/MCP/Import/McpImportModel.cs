// PORT-ONLY: owner request 2026-10-11 (ED-NEW-mcpimport-1). The observable face of the "Import servers" controller, the pattern of ImportModel (SK-18): the views track its Revision, which grows on
// the UI thread after every change. Also the seam the scan comes through: the app reads the real profile; a test hands its own scan.
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port.MCP.Import;

namespace Marrow.App.Port.MCP.Import;

internal sealed class McpImportModel : MainActorModel
{
    private int revision;
    private bool posted;
    private int pulse;

    public McpImportModel(McpImportController controller)
    {
        Controller = controller;
        controller.Changed += OnChanged;
        revision = controller.Revision;
    }

    public McpImportController Controller { get; }

    /// <summary>Whether the sheet is on screen (one sheet, whichever door opened it).</summary>
    public bool SheetShown { get; set; }

    public int Revision { get => Read(revision); private set => Set(ref revision, value); }

    private void OnChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Revision = Interlocked.Increment(ref pulse) + Controller.Revision;
            return;
        }
        lock (this)
        {
            if (posted) return;
            posted = true;
        }
        Dispatcher.UIThread.Post(() =>
        {
            lock (this) posted = false;
            Revision = Interlocked.Increment(ref pulse) + Controller.Revision;
        });
    }
}

internal static class McpImportSeams
{
    /// <summary>The read-only scan of the other apps' MCP configs on this PC. A test overrides it, so no test reads the real profile.</summary>
    public static Ambient<Func<ForeignMcpScan>> Scan { get; } = new(() => (Func<ForeignMcpScan>)DefaultScan);

    private static ForeignMcpScan DefaultScan()
    {
        var home = LoginEnvironment.HomeDirectory;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!Path.IsPathRooted(home)) return ForeignMcpScan.Empty;
        return new ForeignMcpScanner(ForeignMcpScanner.Places(home, Path.IsPathRooted(appData) ? appData : Path.Combine(home, "AppData", "Roaming"))).Scan();
    }
}
