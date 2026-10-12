// PORT-ONLY: owner request 2026-10-11 (ED-NEW-mcpimport-2): "Skills, Plugins and MCP need to have the add and import options." The glass capsule of a page's header row, in the look of the Skills and
// Plugins pages' "+ Add" (CustomAddAction: SwiftGlass's button, a 12 point glyph and 12 point words, 5 apart), so "+ Add" and "Import" read as one pair on all three pages.
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.App.Port.Skills.Pages;
using Marrow.UI.Port;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.Settings;

internal static class PageHeaderButton
{
    public const string ImportWords = "Import";
    public const string AddWords = "Add";
    public const string ImportId = "header-import";
    public const string AddId = "header-add";

    public static Button Make(string symbol, string words, string id, Action action)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { LibraryUi.Glyph(symbol, 12, ChromeTokens.PrimaryText), LibraryUi.Text(words, 12) } };
        foreach (var part in row.Children) part.VerticalAlignment = VerticalAlignment.Center;
        var button = PlainControls.Button(row, action);
        SwiftGlass.StyleButton(button);
        button.Padding = new Avalonia.Thickness(13, 0, 13, 0);
        button.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
        button.Named(words, id);
        return button;
    }

    /// <summary>The header's "Import" (a tray with an arrow into it).</summary>
    public static Button Import(Action action) => Make("square.and.arrow.down", ImportWords, ImportId, action);
}
