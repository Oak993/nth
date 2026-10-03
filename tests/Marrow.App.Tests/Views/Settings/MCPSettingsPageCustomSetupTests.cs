using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.MCPSettingsPageFixture;
using static Marrow.App.Tests.Fixtures.MCPSettingsPageTestFirst;

namespace Marrow.App.Tests.Views.Settings;

// UI-MCP-040: a catalogue card presents the setup panel on its own the moment its state becomes needsSetup (MCPSettingsPage.swift:284-290),
// a custom row has no such watcher (:447-575): it only offers "Fix it" and presents on the press (:521-524).
[Collection("Headless")]
public sealed class MCPSettingsPageCustomSetupTests(HeadlessSession headless)
{
    private static readonly MCPSetupGuide Guide = new("Turn it on", "The server is off.", new[] { "Step one" }, "Open page", "", null);

    [Fact]
    [Trait("Req", "UI-MCP-040")] // App/Views/Settings/MCPSettingsPage.swift:521-524,447-575 against :284-290; the Fix it look (.glassProminent) has no Mac frame (OD-0098: its source values are proven in MCPSettingsPageActionPillTests)
    public void Custom_row_never_presents_the_setup_panel_by_itself_when_the_state_becomes_needs_setup()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime(); using var harness = new AppModelHarness(withRuntime: false);
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            using var presentation = Presentation.Presenter.Override(new RecordingPresenter());
            var model = runtime.OnMain(() => new AppModel());
            try
            {
                var custom = Custom(); var entry = custom.CatalogEntry(); var card = Entry("transition-card");
                MCPCustomRow row = null!; MCPServerCard cardView = null!; IDisposable rowWindow = null!, cardWindow = null!;
                runtime.OnMain(() =>
                {
                    State(model, custom.Id, new MCPConnectionState.NotConnected()); State(model, card.Id, new MCPConnectionState.NotConnected());
                    row = new MCPCustomRow(custom); cardView = new MCPServerCard(card); rowWindow = Attach(row, model); cardWindow = Attach(cardView, model);
                });
                runtime.OnMain(() => { Assert.False(MCPSetupPanel.Shared.IsPresenting(entry)); Assert.False(MCPSetupPanel.Shared.IsPresenting(card)); });
                // Both change to needsSetup the same way a finished probe publishes it.
                runtime.OnMain(() => Publish(model, custom.Id, new MCPConnectionState.NeedsSetup(Guide)));
                runtime.OnMain(() => { Dispatcher.UIThread.RunJobs(); Assert.Contains("Fix it", Texts(row)); Assert.False(MCPSetupPanel.Shared.IsPresenting(entry), "the custom row presented the setup panel by itself"); });
                // The positive control: the card does present, so the watcher the row lacks is observable in this rig.
                runtime.OnMain(() => Publish(model, card.Id, new MCPConnectionState.NeedsSetup(Guide)));
                runtime.OnMain(() => { Dispatcher.UIThread.RunJobs(); Assert.True(MCPSetupPanel.Shared.IsPresenting(card), "the catalogue card did not present"); Assert.False(MCPSetupPanel.Shared.IsPresenting(entry)); MCPSetupPanel.Shared.Close(); });
                // Only the press presents it for the custom row.
                runtime.OnMain(() => { Click(Button(row, "Fix it")); Assert.True(MCPSetupPanel.Shared.IsPresenting(entry)); });
                runtime.OnMain(() => { MCPSetupPanel.Shared.Close(); rowWindow.Dispose(); cardWindow.Dispose(); });
            }
            finally { runtime.OnMain(() => { MCPSetupPanel.Shared.Close(); Dispatcher.UIThread.RunJobs(); }); }
        });
    }
}
