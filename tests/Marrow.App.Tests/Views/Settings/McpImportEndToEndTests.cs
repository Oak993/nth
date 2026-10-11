using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Port.MCP.Import;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port.MCP.Import;
using Marrow.Services.MCP;
using Marrow.Services.Port;
using Marrow.Services.Port.MCP.Import;
using Marrow.Testing;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.MCPSettingsPageFixture;

namespace Marrow.App.Tests.Views.Settings;

// ED-NEW-mcpimport-5 / -7 (review of claude/mcp-import): the whole import through the real sheet and the real MCP store (mcp.json plus the Credential Manager split, in memory here): what cannot be imported is marked and left
// out, what needs a value comes in empty and says so, key-like values are locked, and the sheet shows the import under way while the store works one server at a time.
[Collection("Headless")]
public sealed class McpImportEndToEndTests(HeadlessSession headless)
{
    private static string? Id(Control control) => AutomationProperties.GetAutomationId(control);

    private static void Pump(TestRuntime runtime, Func<bool> done)
    {
        var end = DateTime.UtcNow.AddSeconds(15);
        while (!done())
        {
            if (DateTime.UtcNow > end) throw new TimeoutException("The condition never held.");
            runtime.Loop.RunUntilIdle();
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(2);
        }
        runtime.Loop.RunUntilIdle();
    }

    [Fact]
    [Trait("Decision", "ED-NEW-mcpimport-5")]
    [Trait("Decision", "ED-NEW-mcpimport-7")]
    public void Import_all_through_the_sheet_and_the_real_store_saves_what_can_come_in_and_marks_the_rest()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime(); using var harness = new AppModelHarness(withRuntime: false);
            using var secrets = SecretStore.Shared.Override(new SecretStore(new AppModelCredentialBackend()));
            var presenter = new RecordingPresenter(); using var presentation = Presentation.Presenter.Override(presenter);
            var key = "sk-" + new string('0', 12) + "fixture" + new string('9', 12); // a key-shaped fixture value, built at run time
            const string path = @"C:\fixture\.cursor\mcp.json";
            ForeignMcpServer Server(string name, string[]? args = null, params (string, string)[] env) =>
                new(ForeignMcpApp.Cursor, "", name, MCPCustomServer.Kind.Stdio, "npx", args ?? ["-y", "@fixture/" + name], "", [.. env.Select(e => new ForeignMcpVariable(e.Item1, e.Item2))], path);
            var scan = new ForeignMcpScan(
            [
                Server("files", env: [("API_KEY", key), ("LOG_LEVEL", "${env:LOGLEVEL}")]),
                Server("gh", env: [("GITHUB_TOKEN", "${input:github_token}")]),
                new(ForeignMcpApp.Cursor, "", "old", MCPCustomServer.Kind.Http, "", [], "https://old.example.test/sse", [], path, LegacySse: true),
                Server("prompted-arg", args: ["--token", "${input:x}"]),
            ], []);
            using var seam = McpImportSeams.Scan.Override(() => scan);
            using var lookup = McpImportSeams.Lookup.Override(name => name == "LOGLEVEL" ? "debug" : null);
            var model = runtime.OnMain(() => new AppModel());
            var page = new MCPSettingsPage("");
            var header = new StackPanel();
            foreach (var control in page.ChromeAccessories) header.Children.Add(control);
            var window = runtime.OnMain(() => Attach(header, model));
            try
            {
                var import = Assert.IsType<Button>(page.ChromeAccessories[1]);
                var controller = McpImportLauncher.ModelFor(model.Mcp).Controller;
                var phases = new List<McpImportPhase>();
                controller.Changed += () => phases.Add(controller.Phase);

                runtime.OnMain(() => Click(import));
                Pump(runtime, () => controller.Phase == McpImportPhase.Choosing);
                var sheet = LogicalQuery.SelfAndDescendants<McpImportSheetView>(presenter.Calls.Last(c => c.Presented).Popup).Single();
                runtime.OnMain(sheet.Apply);
                var found = Texts(sheet);
                Assert.Contains(McpImportWords.CantImport, found);
                Assert.Contains(McpImportWords.NeedsValueRow, found);
                Assert.Contains(found, t => t.Contains("2 servers can\u2019t be imported", StringComparison.Ordinal));
                var ids = Nodes<Control>(sheet).Select(Id).Where(i => i is not null).ToArray();
                Assert.Contains("mcp-import-tick-Cursor-files", ids);
                Assert.Contains("mcp-import-tick-Cursor-gh", ids);
                Assert.DoesNotContain("mcp-import-tick-Cursor-old", ids); // not tickable: nth cannot take it
                Assert.DoesNotContain("mcp-import-tick-Cursor-prompted-arg", ids);

                runtime.OnMain(() => Click(Nodes<Button>(sheet).Single(b => Id(b) == "mcp-import-all")));
                Pump(runtime, () => controller.Phase == McpImportPhase.Done);
                runtime.OnMain(sheet.Apply);

                Assert.Contains(McpImportPhase.Importing, phases); // the sheet had the import under way, not a frozen window
                Assert.Equal(McpImportPhase.Done, phases[^1]);
                Assert.Equal(["custom-files", "custom-gh"], model.Mcp.Customs.Select(c => c.Id).Order());
                Assert.Equal("2 imported, 1 needs a value.", controller.Summary);
                Assert.Contains("gh: set GITHUB_TOKEN in the server\u2019s editor.", Texts(sheet));

                var files = model.Mcp.Customs.Single(c => c.Id == "custom-files");
                Assert.Equal([new MCPCustomServer.Variable("API_KEY", true), new MCPCustomServer.Variable("LOG_LEVEL", false)], files.Env);
                Assert.Equal(key, MCPKeychain.Value("custom-files", "API_KEY")); // locked: in the Credential Manager
                Assert.Equal("debug", model.Mcp.Connection("custom-files")!.Values["LOG_LEVEL"]); // filled in from the environment, and plain in mcp.json
                Assert.DoesNotContain(model.Mcp.Connection("custom-files")!.Values, v => v.Value.Contains("${", StringComparison.Ordinal));
                Assert.True(model.Mcp.Customs.Single(c => c.Id == "custom-gh").Env.Single().IsSecret);
                Assert.True(string.IsNullOrEmpty(MCPKeychain.Value("custom-gh", "GITHUB_TOKEN"))); // nothing was saved for it, least of all the placeholder
                Assert.DoesNotContain(model.Mcp.Customs, c => c.Id.Contains("old", StringComparison.Ordinal) || c.Id.Contains("prompted", StringComparison.Ordinal));

                runtime.OnMain(() => Click(Nodes<Button>(sheet).Single(b => Id(b) == "mcp-import-close")));
                Assert.Equal(McpImportPhase.Closed, controller.Phase);
            }
            finally { runtime.OnMain(() => { window.Dispose(); Dispatcher.UIThread.RunJobs(); }); }
        });
    }
}
