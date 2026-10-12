// PORT-ONLY: owner request 2026-10-11 (ED-NEW-mcpimport-8). A sheet in the window (WindowSheetLayer) is centred on its anchor and pushed back inside the window. The import sheets are opened from the header
// row's "Import", which sits at the very top right: anchored on that button the sheet would be pushed against the top right corner, under the caption buttons. They are anchored on something page-sized
// instead, so they open in the middle of the window like "Import skills" does from the Skills page.
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Views.Settings;

namespace Marrow.App.Port.Settings;

internal static class ImportSheetAnchor
{
    /// <summary>The page-sized control a sheet opened from <paramref name="from"/> (a header button) is centred on: the Settings view it is in, else the content of its window, else <paramref name="from"/> itself.</summary>
    public static Control For(Control from)
    {
        if (from.GetVisualAncestors().OfType<SettingsView>().FirstOrDefault() is { } settings) return settings;
        return TopLevel.GetTopLevel(from) is Avalonia.Controls.Window { Content: Control content } ? content : from;
    }
}
