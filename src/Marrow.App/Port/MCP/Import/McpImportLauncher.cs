// PORT-ONLY: owner request 2026-10-11 (ED-NEW-mcpimport-1/-2/-8). Opens the "Import servers" sheet from the header's "Import" on Settings > MCP, in the window like "Import skills", centred on the
// page (ImportSheetAnchor), not on the small header button. One import model per MCP store, kept for the life of the app, so a page built again shares one sheet.
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Port.Settings;
using Marrow.App.Port.Skills.Pages;
using Marrow.Services.MCP;
using Marrow.Services.Port.MCP.Import;

namespace Marrow.App.Port.MCP.Import;

internal static class McpImportLauncher
{
    private static readonly ConditionalWeakTable<MCPStore, McpImportModel> Models = new();

    // the import hands the UI thread back between servers: one frame at a time (ED-NEW-mcpimport-7)
    private static Task Frame() => Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background).GetTask();

    public static McpImportModel ModelFor(MCPStore store) => Models.GetValue(store, s => new McpImportModel(new McpImportController(() => McpImportSeams.Scan.Current(), new McpStoreImportTarget(s), McpImportSeams.Lookup.Current, Frame)));

    public static void Open(Control anchor)
    {
        var model = ModelFor(AppEnvironment.GetAppModel(anchor).Mcp);
        if (model.SheetShown) return;
        model.SheetShown = true;
        _ = model.Controller.OpenAsync();
        ChatSheetPresentation.Show(ImportSheetAnchor.For(anchor), dismiss => new ImportWashLayer(new McpImportSheetView(model, dismiss)), () =>
        {
            model.SheetShown = false;
            model.Controller.Close(); // Escape, Cancel, Close, or the page going away: nothing half-done stays behind
        }, inWindow: true, still: LibraryUi.Still(anchor));
    }
}
