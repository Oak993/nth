// PORT-ONLY: owner request 2026-10-11 (ED-NEW-mcpimport-1/-2). Opens the "Import servers" sheet from either of its two doors on Settings > MCP (the header's "Import" and the Custom section's
// "Import from other apps"), in the window like "Import skills". One import model per MCP store, kept for the life of the app, so both doors and a page built again share one sheet.
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Marrow.App.Port.Skills.Pages;
using Marrow.Services.MCP;
using Marrow.Services.Port.MCP.Import;

namespace Marrow.App.Port.MCP.Import;

internal static class McpImportLauncher
{
    private static readonly ConditionalWeakTable<MCPStore, McpImportModel> Models = new();

    public static McpImportModel ModelFor(MCPStore store) => Models.GetValue(store, s => new McpImportModel(new McpImportController(() => McpImportSeams.Scan.Current(), new McpStoreImportTarget(s))));

    public static void Open(Control anchor)
    {
        var model = ModelFor(AppEnvironment.GetAppModel(anchor).Mcp);
        if (model.SheetShown) return;
        model.SheetShown = true;
        _ = model.Controller.OpenAsync();
        ChatSheetPresentation.Show(anchor, dismiss => new Marrow.App.Port.Settings.ImportWashLayer(new McpImportSheetView(model, dismiss)), () =>
        {
            model.SheetShown = false;
            model.Controller.Close(); // Escape, Cancel, Close, or the page going away: nothing half-done stays behind
        }, inWindow: true, still: LibraryUi.Still(anchor));
    }
}
