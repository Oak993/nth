using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Port.Settings;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Port.Skills.PluginImport;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port.Skills.Import;
using Marrow.Core.Port.Skills.Pages;
using Marrow.Services.Port.Skills.Import;
using Marrow.Services.Port.Skills.PluginImport;

namespace Marrow.App.Tests.Views.Settings;

// ED-NEW-mcpimport-8 (review of claude/mcp-import): the sheets opened from a header "Import" are centred on the page, not pushed against the top right corner under the caption buttons because the small header
// button sits there. The header row is put at the top left of the test window, the very place a sheet anchored on it would be pushed against.
[Collection("Headless")]
public sealed class ImportSheetAnchorTests(HeadlessSession headless)
{
    private static string? Id(Control control) => AutomationProperties.GetAutomationId(control);

    private static void Header(LibraryPageHarness h, IReadOnlyList<Control> accessories)
    {
        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        foreach (var control in accessories) row.Children.Add(control);
        h.Root.Children.Add(row);
        h.Settle();
    }

    // The sheet's rectangle in its layer, and the layer's size (the window's).
    private static (Rect Sheet, Size Layer) Placed(LibraryPageHarness h)
    {
        h.Settle();
        var layer = h.Root.Children.OfType<WindowSheetLayer>().Single();
        return (layer.Sheet.Bounds, layer.Bounds.Size);
    }

    private static void AssertCentred(Rect sheet, Size layer)
    {
        Assert.True(sheet.Width > 0 && sheet.Height > 0);
        Assert.InRange(sheet.Center.X, layer.Width / 2 - 1.5, layer.Width / 2 + 1.5);
        if (sheet.Height < layer.Height - 2) Assert.InRange(sheet.Center.Y, layer.Height / 2 - 1.5, layer.Height / 2 + 1.5);
        else Assert.Equal(0, sheet.Top, 1); // taller than the window: pinned to the top, still not pushed right
        Assert.True(sheet.Left > 1 && sheet.Right < layer.Width - 1, "The sheet sits against a side of the window.");
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-8")]
    public void The_plugins_import_sheet_opens_in_the_middle_of_the_window()
    {
        LibraryPageHarness.Run(headless, LibrarySection.Plugins, h =>
        {
            var files = new CustomOwnFake();
            files.Source.Add(".claude-plugin/plugin.json", Encoding.UTF8.GetBytes("{ \"name\": \"fixture-plugin\", \"description\": \"A fixture plugin.\" }"));
            using var bound = CustomAddSeams.Backends.Override(CustomAddSeams.For(files.Open()));
            using var seam = PluginImportSeams.Scan.Override(_ => new ForeignPluginScan([new(ForeignPluginApp.ClaudeCode, "fixture-plugin", "A fixture plugin.", "market", "/fake/source")], []));
            var page = new PluginsSettingsPage();
            h.Root.Children.Add(page);
            Header(h, page.ChromeAccessories);

            Assert.True(h.Press(PageHeaderButton.ImportId));
            h.Until(() => h.TryFind("plugin-import-sheet") is not null && PluginImportLauncher.Model()!.Controller.Phase == ImportPhase.Choosing);
            var (sheet, layer) = Placed(h);
            AssertCentred(sheet, layer);

            Assert.True(h.Press("plugin-import-cancel"));
            h.Root.Children.Remove(page);
        });
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-8")]
    public void The_skills_import_sheet_opened_from_the_header_opens_in_the_middle_of_the_window()
    {
        using var rig = new ImportRig();
        rig.Fill();
        LibraryPageHarness.Run(headless, LibrarySection.Skills, h =>
        {
            Header(h, h.Page.ChromeAccessories);
            Assert.True(h.Press(PageHeaderButton.ImportId));
            h.Until(() => h.TryFind("import-sheet") is not null && rig.Controller.Phase == ImportPhase.Choosing);
            var (sheet, layer) = Placed(h);
            AssertCentred(sheet, layer);
            Assert.True(h.Press("import-cancel"));
        }, imports: rig.Model);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-8")]
    public void The_anchor_is_the_settings_view_a_header_button_is_in_else_the_window_content()
    {
        SettingsNavigation.Shared.RequestedPage = SettingsPage.Mcp;
        SettingsViewRig.Run(headless, (view, model, sidebar) =>
        {
            var button = view.GetVisualDescendants().OfType<Button>().First(b => Id(b) == PageHeaderButton.ImportId);
            Assert.Same(view, ImportSheetAnchor.For(button));
            Assert.True(ImportSheetAnchor.For(button).Bounds.Width > 400); // page-sized, not the little button
        });

        headless.OnUiThread(() =>
        {
            var button = new Button();
            var content = new Panel { Children = { button } };
            var window = new Avalonia.Controls.Window { Content = content, Width = 400, Height = 300 };
            try
            {
                window.Show();
                Assert.Same(content, ImportSheetAnchor.For(button));
                var alone = new Button();
                Assert.Same(alone, ImportSheetAnchor.For(alone)); // a control in no window is its own anchor
            }
            finally { window.Close(); }
        });
    }
}
