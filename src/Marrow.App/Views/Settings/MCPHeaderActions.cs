// PORT-ONLY: owner requests 2026-10-11 (ED-NEW-mcpimport-1/-2). Settings > MCP's "+ Add" and "Import" in the header row, where the Skills and Plugins pages keep theirs: Add opens the very
// editor the Custom section's "Add server" opens (MCPCustomEditor, a new server), Import opens "Import servers". The section's own rows stay.
using Avalonia.Controls;
using Marrow.App.Port.MCP.Import;
using Marrow.App.Port.Settings;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Views.Settings;

internal sealed class MCPHeaderAdd : Decorator
{
    private sealed class ViewState : MainActorModel
    {
        private bool adding;
        public bool Adding { get => Read(adding); set => Set(ref adding, value); }
    }
    private readonly ViewState state = new();

    public MCPHeaderAdd()
    {
        var button = PageHeaderButton.Make("plus", PageHeaderButton.AddWords, PageHeaderButton.AddId, () => state.Adding = !state.Adding);
        Button = button;
        Child = new Panel { Children = { button, MCPSettingsPage.Popover(button, new Binding<bool>(() => state.Adding, v => state.Adding = v), () => new MCPCustomEditor(null), this, buildBeforeOpen: false) } };
    }

    internal Button Button { get; }

    internal bool Adding => state.Adding;
}

internal static class MCPHeaderActions
{
    public static IReadOnlyList<Control> Make()
    {
        Button? import = null;
        import = PageHeaderButton.Import(() => McpImportLauncher.Open(import!));
        return [new MCPHeaderAdd(), import];
    }
}
