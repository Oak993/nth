using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Services.MCP;
using Marrow.Testing;
using static Marrow.App.Tests.Fixtures.MCPSettingsPageFixture;

namespace Marrow.App.Tests.Views.Settings;

// PR-MCP-219: MCPKeyPanel.swift:61-63 observes changes to the entry's connected flag.
// Only in-memory connection states and a non-secret optional text field are used;
// no Connect, Save, browser, credential, or provider operation runs.
[Collection("Headless")]
public sealed class MCPKeyPanelConnectionTests(HeadlessSession headless)
{
    private static MCPCatalogEntry OptionalEntry() => Entry("offline-key-panel") with
    {
        KeysURL = null,
        Fields = new[] { new MCPField("host", "Server Host", "example.invalid", MCPField.Kind.Text, IsRequired: false) },
    };

    private static void Publish(AppModel model, string id, MCPConnectionState state)
    {
        // Same observable publication seam as MCPSettingsPageFollowupTests.
        typeof(MCPStore).GetProperty(nameof(MCPStore.States))!.SetValue(model.Mcp,
            new Dictionary<string, MCPConnectionState>(model.Mcp.States, StringComparer.Ordinal) { [id] = state });
    }

    [Fact]
    [Trait("Req", "PR-MCP-219")]
    [Trait("Req", "PR-MCP-213")] // MCPSettingsPage.swift:1002-1004 opens this editor for an optional field.
    public void An_already_connected_entry_stays_open_and_editable()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var harness = new AppModelHarness(withRuntime: false);
            var model = runtime.OnMain(() => new AppModel());
            var entry = OptionalEntry();
            var closes = 0;
            runtime.OnMain(() => Publish(model, entry.Id, new MCPConnectionState.Connected(2)));
            var view = runtime.OnMain(() => new MCPKeyPanelView(entry, model, () => closes++));
            var window = runtime.OnMain(() => Attach(view, model));
            try
            {
                Assert.Equal(0, closes);
                runtime.OnMain(() =>
                {
                    Field(view, "example.invalid").Text = "edited.invalid";
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal("edited.invalid", model.Mcp.Value(entry.Fields[0], entry));
                });
                runtime.OnMain(() => Publish(model, "another-server", new MCPConnectionState.Connected(1)));
                runtime.OnMain(() => Publish(model, entry.Id, new MCPConnectionState.Connected(3)));
                Assert.Equal(0, closes);
                Assert.Equal("edited.invalid", Field(view, "example.invalid").Text);
            }
            finally { runtime.OnMain(window.Dispose); }
        });
    }

    [Fact]
    [Trait("Req", "PR-MCP-219")]
    public void A_panel_opened_during_a_probe_closes_only_when_its_entry_becomes_connected()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var harness = new AppModelHarness(withRuntime: false);
            var model = runtime.OnMain(() => new AppModel());
            var entry = OptionalEntry();
            var closes = 0;
            runtime.OnMain(() => Publish(model, entry.Id, new MCPConnectionState.Connecting()));
            var view = runtime.OnMain(() => new MCPKeyPanelView(entry, model, () => closes++));
            var window = runtime.OnMain(() => Attach(view, model));
            try
            {
                Assert.Equal(0, closes);
                runtime.OnMain(() => Publish(model, "another-server", new MCPConnectionState.Connected(1)));
                runtime.OnMain(() => Publish(model, entry.Id, new MCPConnectionState.Failed("Offline fixture failure.")));
                Assert.Equal(0, closes);
                runtime.OnMain(() => Publish(model, entry.Id, new MCPConnectionState.Connecting()));
                runtime.OnMain(() => Publish(model, entry.Id, new MCPConnectionState.Connected(2)));
                Assert.Equal(1, closes);
                // The callback records closure without removing the view, exposing duplicate notifications.
                runtime.OnMain(() => Publish(model, entry.Id, new MCPConnectionState.Connected(3)));
                runtime.OnMain(() => Publish(model, "another-server", new MCPConnectionState.Failed("Unrelated.")));
                Assert.Equal(1, closes);
            }
            finally { runtime.OnMain(window.Dispose); }
        });
    }

    [Fact]
    [Trait("Req", "PR-MCP-219")]
    public void Detaching_cancels_the_watch_and_reattaching_does_not_replay_a_connection()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var harness = new AppModelHarness(withRuntime: false);
            var model = runtime.OnMain(() => new AppModel());
            var entry = OptionalEntry();
            var closes = 0;
            runtime.OnMain(() => Publish(model, entry.Id, new MCPConnectionState.Connecting()));
            var view = runtime.OnMain(() => new MCPKeyPanelView(entry, model, () => closes++));
            var host = new Border { Child = view };
            var window = runtime.OnMain(() => Attach(host, model));
            try
            {
                runtime.OnMain(() =>
                {
                    // Dispose the watch before its already-queued update can run.
                    Publish(model, entry.Id, new MCPConnectionState.Connected(1));
                    host.Child = null;
                });
                Assert.Equal(0, closes);
                runtime.OnMain(() => host.Child = view);
                Assert.Equal(0, closes);
                runtime.OnMain(() => Publish(model, entry.Id, new MCPConnectionState.Connecting()));
                runtime.OnMain(() => Publish(model, entry.Id, new MCPConnectionState.Connected(2)));
                Assert.Equal(1, closes);
            }
            finally { runtime.OnMain(window.Dispose); }
        });
    }
}
