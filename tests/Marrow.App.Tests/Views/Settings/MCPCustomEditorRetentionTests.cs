using System;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.MCP;
using Marrow.Services.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.MCPSettingsPageFixture;

namespace Marrow.App.Tests.Views.Settings;

// B3 52bc792: MCPSettingsPage.swift:387-440 retains keyed rows and the add popup;
// :864-889 saves the definition first, then leaves the same editor open on value-save failure.
// The existing refusing in-memory backend supplies the failure; no probe or transport runs.
[Collection("Headless")]
public sealed class MCPCustomEditorRetentionTests(HeadlessSession headless)
{
    private static Control OpenContent(Popup popup)
    {
        // Use the real deferred-content handler, as MCPSettingsPageProofsMcpATests does.
        ((EventHandler)typeof(Popup).GetField("Opened", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(popup)!).Invoke(popup, EventArgs.Empty);
        return Assert.IsAssignableFrom<Control>(popup.Child);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "PR-MCP-208")]
    public void A_value_save_failure_keeps_the_active_editor_and_retry_reuses_the_saved_id(bool editing)
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var harness = new AppModelHarness(withRuntime: false);
            var backend = new MCPSettingsRefusingCredentialBackend { Refuse = true };
            using var secrets = SecretStore.Shared.Override(new SecretStore(backend));
            var presenter = new RecordingPresenter();
            using var presentation = Presentation.Presenter.Override(presenter);
            var model = runtime.OnMain(() => new AppModel());
            if (editing)
            {
                runtime.OnMain(() =>
                {
                    var server = Custom(1);
                    Assert.Null(model.Mcp.SaveCustom(server));
                    Connection(model, new MCPConnection(server.Id) { ConnectedAt = DateTimeOffset.UtcNow });
                    State(model, server.Id, new MCPConnectionState.Connected(1));
                });
            }

            var page = new MCPSettingsPage("");
            var window = runtime.OnMain(() => Attach(page, model));
            try
            {
                var section = Assert.Single(Nodes<MCPCustomSection>(page));
                MCPCustomRow? originalRow = null;
                Popup popup;
                if (editing)
                {
                    originalRow = Assert.Single(Nodes<MCPCustomRow>(section));
                    var options = Nodes<Button>(originalRow).Single(button => ToolTip.GetTip(button) as string == "Server options");
                    var popups = Assert.IsType<Panel>(options.Parent).Children.OfType<Popup>().ToArray();
                    runtime.OnMain(() => Click(options));
                    var menu = Assert.IsAssignableFrom<Control>(popups[0].Child);
                    runtime.OnMain(() => Click(Button(menu, "Edit")));
                    popup = popups[1];
                }
                else
                {
                    var add = Button(section, "Add server");
                    runtime.OnMain(() => Click(add));
                    popup = Assert.IsType<Panel>(add.Parent).Children.OfType<Popup>().Single();
                }

                var body = runtime.OnMain(() => OpenContent(popup));
                var editor = Assert.Single(Nodes<MCPCustomEditor>(body));
                var saveLabel = editing ? "Save" : "Add server";
                runtime.OnMain(() =>
                {
                    Field(editor, "My server").Text = "Example";
                    Field(editor, "npx").Text = "offline-fixture";
                    if (!editing) Click(Button(editor, "Add environment variable"));
                    Field(editor, "KEY").Text = "TOKEN";
                    Field(editor, "value").Text = "fixture-only-value";
                    Dispatcher.UIThread.RunJobs();
                    Click(Nodes<Button>(editor).Single(button =>
                        ToolTip.GetTip(button) as string == "Stored in mcp.json; click to keep it in the Credential Manager instead"));
                    Click(Button(editor, saveLabel));
                    Dispatcher.UIThread.RunJobs();
                });

                // OnMain drains the Customs/Connections/States rebuilds before these assertions.
                Assert.Same(section, Assert.Single(Nodes<MCPCustomSection>(page)));
                Assert.Contains(popup, Nodes<Popup>(section));
                Assert.Same(body, popup.Child);
                Assert.Same(editor, Assert.Single(Nodes<MCPCustomEditor>(Assert.IsAssignableFrom<Control>(popup.Child))));
                Assert.True(presenter.Calls.Last(call => call.Popup == popup).Presented);
                Assert.Contains("The Credential Manager could not save a value. Keep this editor open and try again.", Texts(editor));
                Assert.Equal("fixture-only-value", Field(editor, "value").Text);
                var saved = Assert.Single(model.Mcp.Customs);
                Assert.Equal("custom-example", saved.Id);
                Assert.Equal(saved.Id, Read<string>(editor, "savedID"));
                if (editing)
                {
                    Assert.Same(originalRow, Assert.Single(Nodes<MCPCustomRow>(section)));
                    Assert.IsType<MCPConnectionState.NotConnected>(model.Mcp.State(saved.Id));
                }

                runtime.OnMain(() =>
                {
                    backend.Refuse = false;
                    Field(editor, "My server").Text = "Renamed";
                    Dispatcher.UIThread.RunJobs();
                    Click(Button(editor, saveLabel));
                    Dispatcher.UIThread.RunJobs();
                });

                saved = Assert.Single(model.Mcp.Customs);
                Assert.Equal("custom-example", saved.Id); // savedID survives; the retry replaces rather than duplicates.
                Assert.Equal("Renamed", saved.Name);
                Assert.False(presenter.Calls.Last(call => call.Popup == popup).Presented);
                Assert.Equal("fixture-only-value", runtime.OnMain(() => model.Mcp.StoredValue(saved.Id, "TOKEN", true)));
                var currentRow = Assert.Single(Nodes<MCPCustomRow>(section));
                Assert.Contains("Renamed", Texts(currentRow));
                if (editing) Assert.Same(originalRow, currentRow);
            }
            finally { runtime.OnMain(() => { window.Dispose(); Dispatcher.UIThread.RunJobs(); }); }
        });
    }
}
