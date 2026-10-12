using System.Text;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Marrow.App.Port.MCP.Import;
using Marrow.App.Port.Settings;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Port.Skills.PluginImport;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port.MCP.Import;
using Marrow.Core.Port.Skills.Import;
using Marrow.Core.Port.Skills.Pages;
using Marrow.Services.Port.MCP.Import;
using Marrow.Services.Port.Skills.Import;
using Marrow.Services.Port.Skills.PluginImport;
using Marrow.Testing;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.MCPSettingsPageFixture;
using OwnKind = Marrow.Services.Port.Skills.Custom.CustomAddKind;

namespace Marrow.App.Tests.Views.Settings;

// ED-NEW-mcpimport-1/-2 (owner 2026-10-11): Skills, Plugins and MCP each carry "+ Add" and "Import" in the header row, and each opens its flow. The scans are seams: no test reads the real profile.
[Collection("Headless")]
public sealed class PageImportActionsTests(HeadlessSession headless)
{
    private static string? Id(Control control) => AutomationProperties.GetAutomationId(control);

    private static void Header(LibraryPageHarness h, IReadOnlyList<Control> accessories)
    {
        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
        foreach (var control in accessories) row.Children.Add(control);
        h.Root.Children.Add(row);
        h.Settle();
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-1")]
    [Trait("Decision", "ED-NEW-mcpimport-2")]
    public void Mcp_has_add_and_import_in_the_header_and_they_open_the_server_editor_and_the_import_list()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime(); using var harness = new AppModelHarness(withRuntime: false);
            var presenter = new RecordingPresenter(); using var presentation = Presentation.Presenter.Override(presenter);
            var found = new ForeignMcpScan([new(ForeignMcpApp.Cursor, "", "fixture-files", MCPCustomServer.Kind.Stdio, "npx", ["-y", "@fixture/files"], "", [], @"C:\fixture\.cursor\mcp.json")], []);
            var scan = found;
            using var seam = McpImportSeams.Scan.Override(() => scan);
            var model = runtime.OnMain(() => new AppModel());
            var page = new MCPSettingsPage("");
            var header = new StackPanel();
            foreach (var control in page.ChromeAccessories) header.Children.Add(control);
            var window = runtime.OnMain(() => Attach(page, model));
            var headerWindow = runtime.OnMain(() => Attach(header, model));
            try
            {
                var add = Assert.IsType<MCPHeaderAdd>(page.ChromeAccessories[0]);
                var import = Assert.IsType<Button>(page.ChromeAccessories[1]);
                Assert.Equal(("Add", "Import"), (AutomationProperties.GetName(add.Button), AutomationProperties.GetName(import)));
                Assert.Equal((PageHeaderButton.AddId, PageHeaderButton.ImportId), (Id(add.Button), Id(import)));

                runtime.OnMain(() => Click(add.Button)); // the very editor "Add server" opens
                Assert.True(add.Adding);
                var editorPopup = Assert.IsType<Panel>(add.Child).Children.OfType<Popup>().Single();
                var built = runtime.OnMain(() => MCPSettingsPage.PopoverContent(editorPopup)); // the popup's own builder: a new server's editor
                Assert.Single(LogicalQuery.SelfAndDescendants<MCPCustomEditor>(built));

                Assert.DoesNotContain(Nodes<Button>(page), b => Id(b) == PageHeaderButton.ImportId); // the pair lives in the header; the Custom card stays as the source draws it
                var controller = McpImportLauncher.ModelFor(model.Mcp).Controller;
                runtime.OnMain(() => Click(import));
                runtime.Loop.RunUntil(() => controller.Phase == McpImportPhase.Choosing, TimeSpan.FromSeconds(10));
                var sheet = LogicalQuery.SelfAndDescendants<McpImportSheetView>(presenter.Calls.Last(c => c.Presented).Popup).Single();
                runtime.OnMain(sheet.Apply);
                var words = Texts(sheet);
                Assert.Contains(McpImportWords.Title, words);
                Assert.Contains(McpImportWords.Cursor, words);
                Assert.Contains("fixture-files", words);
                Assert.Contains("npx -y @fixture/files", words);

                runtime.OnMain(() => Click(Nodes<Button>(sheet).Single(b => Id(b) == "mcp-import-cancel")));
                Assert.Equal(McpImportPhase.Closed, controller.Phase);

                scan = ForeignMcpScan.Empty; // opened again: nothing found shows the empty line
                runtime.OnMain(() => Click(import));
                runtime.Loop.RunUntil(() => controller.Phase == McpImportPhase.Choosing, TimeSpan.FromSeconds(10));
                var again = LogicalQuery.SelfAndDescendants<McpImportSheetView>(presenter.Calls.Last(c => c.Presented).Popup).Single();
                runtime.OnMain(again.Apply);
                Assert.Contains(McpImportWords.NothingFound, Texts(again));
                runtime.OnMain(() => Click(Nodes<Button>(again).Single(b => Id(b) == "mcp-import-cancel")));
            }
            finally { runtime.OnMain(() => { headerWindow.Dispose(); window.Dispose(); Dispatcher.UIThread.RunJobs(); }); }
        });
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-2")]
    public void Skills_has_add_and_import_in_the_header_and_import_opens_the_import_skills_sheet()
    {
        using var rig = new ImportRig();
        rig.Fill();
        LibraryPageHarness.Run(headless, LibrarySection.Skills, h =>
        {
            var accessories = h.Page.ChromeAccessories;
            Assert.Equal(["custom-add", PageHeaderButton.ImportId], accessories.Select(c => c is CustomAddAction ? "custom-add" : Id(c)));
            Header(h, accessories);
            var presenter = new RecordingPresenter();
            using (Presentation.Presenter.Override(presenter))
            {
                Assert.True(h.Press("custom-add")); // its source menu (From a folder, a .zip, GitHub)
                Assert.Contains(presenter.Calls, c => c.Presented);
            }
            Assert.True(h.Press(PageHeaderButton.ImportId));
            h.Until(() => h.TryFind("import-sheet") is not null && rig.Controller.Phase == ImportPhase.Choosing);
            Assert.Contains("alpha", h.Words(h.Find<Control>("import-sheet")));
        }, imports: rig.Model);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-2")]
    public void Plugins_has_add_and_import_in_the_header_and_import_brings_a_plugin_in_as_add_does() => LibraryPageHarness.Run(headless, LibrarySection.Plugins, h =>
    {
        var files = new CustomOwnFake();
        files.Source.Add(".claude-plugin/plugin.json", Encoding.UTF8.GetBytes("{ \"name\": \"fixture-plugin\", \"description\": \"A fixture plugin.\" }"));
        var host = files.Open();
        var services = CustomAddSeams.For(host);
        using var bound = CustomAddSeams.Backends.Override(services);
        using var seam = PluginImportSeams.Scan.Override(_ => new ForeignPluginScan([new(ForeignPluginApp.ClaudeCode, "fixture-plugin", "A fixture plugin.", "market", "/fake/source")], []));
        var page = new PluginsSettingsPage();
        h.Root.Children.Add(page);
        var accessories = page.ChromeAccessories;
        Assert.Equal(["custom-add", PageHeaderButton.ImportId], accessories.Select(c => c is CustomAddAction ? "custom-add" : Id(c)));
        Header(h, accessories);

        Assert.True(h.Press(PageHeaderButton.ImportId));
        var controller = PluginImportLauncher.Model()!.Controller;
        h.Until(() => h.TryFind("plugin-import-sheet") is not null && controller.Phase == ImportPhase.Choosing);
        Assert.Contains("fixture-plugin", h.Words(h.Find<Control>("plugin-import-sheet")));
        Assert.Contains(PluginImportWords.Title, h.Words(h.Find<Control>("plugin-import-sheet")));
        Assert.True(h.Press("plugin-import-all"));
        h.Until(() => controller.Phase == ImportPhase.Done);
        Assert.Equal("1 imported.", controller.Summary);
        Assert.Equal("fixture-plugin", Assert.Single(host.List(OwnKind.Plugins)).Name);
        Assert.True(h.Press("plugin-import-close"));
        h.Root.Children.Remove(page);
    });
}
