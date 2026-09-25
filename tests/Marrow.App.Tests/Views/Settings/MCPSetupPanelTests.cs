using Marrow.Services.MCP;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Views.Settings;

[Collection("Headless")]
public sealed class MCPSetupPanelTests(HeadlessSession headless)
{
    private static MCPCatalogEntry Entry(string id) =>
        new(
            id,
            "GitLab",
            "GitLab",
            "Source control",
            MCPCategory.Developer,
            "",
            0,
            new MCPTransport.Stdio("node", ValueList<string>.Empty),
            "https://gitlab.com");

    private static MCPSetupGuide Guide() => new(
        "GitLab",
        "You're signed in, but GitLab keeps its MCP server off until a group owner turns it on. Until then, the address answers not found.",
        new ValueList<string>(["Open the group settings.", "Enable the MCP server."]),
        "Open my groups",
        "https://gitlab.com/groups",
        "GitLab can take a minute to apply the change.");

    private static void SetState(AppModel model, string id, MCPConnectionState state)
    {
        var field = typeof(MCPStore).GetField("statesField", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var states = (Dictionary<string, MCPConnectionState>)field.GetValue(model.Mcp)!;
        states[id] = state;
    }

    private static string AllText(Control control)
    {
        var parts = new List<string>();
        void Walk(Control node)
        {
            if (node is TextBlock text) parts.Add(text.Text ?? "");
            if (node is ContentControl { Content: Control child }) Walk(child);
            if (node is ContentControl { Content: string label }) parts.Add(label);
            if (node is Panel panel) foreach (var kid in panel.Children) Walk(kid);
            if (node is Decorator { Child: Control decorated }) Walk(decorated);
        }
        Walk(control);
        return string.Join("\n", parts);
    }

    [Fact]
    [Trait("Req", "PR-MCP-220")] // App/Views/Settings/MCPSetupPanel.swift:18-20
    public void IsPresenting_is_false_when_the_panel_is_not_visible()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();

            Assert.False(MCPSetupPanel.Shared.IsPresenting(Entry("gitlab")));
        });
    }

    [Fact]
    [Trait("Req", "PR-MCP-220")] // App/Views/Settings/MCPSetupPanel.swift:45-49
    public void Close_clears_the_recorded_entry_id_so_a_later_present_reopens_the_guide_page()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();

            var field = typeof(MCPSetupPanel).GetField("presentedEntryID", BindingFlags.Instance | BindingFlags.NonPublic)!;
            field.SetValue(MCPSetupPanel.Shared, "gitlab");

            MCPSetupPanel.Shared.Close();

            Assert.Null(field.GetValue(MCPSetupPanel.Shared));
        });
    }

    [Fact]
    [Trait("Req", "PR-MCP-221")] // App/Views/Settings/MCPSetupPanel.swift:53-61
    public void Fit_does_nothing_when_the_panel_is_not_visible()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();

            var field = typeof(MCPSetupPanel).GetField("panel", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.Null(field.GetValue(MCPSetupPanel.Shared));

            MCPSetupPanel.Shared.Fit(250);

            Assert.Null(field.GetValue(MCPSetupPanel.Shared));
        });
    }

    [Fact]
    [Trait("Req", "PR-MCP-222")] // App/Views/Settings/MCPSetupPanel.swift:117-166
    public void The_view_shows_the_reason_the_numbered_steps_and_the_open_page_button_before_any_check()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            SetState(model, "gitlab", new MCPConnectionState.NeedsSetup(Guide()));

            var view = new MCPSetupPanelView(Entry("gitlab"), Guide(), model, () => { });

            var text = AllText(view);
            Assert.Contains(Guide().Reason, text, StringComparison.Ordinal);
            Assert.Contains("Open the group settings.", text, StringComparison.Ordinal);
            Assert.Contains("Enable the MCP server.", text, StringComparison.Ordinal);
            Assert.Contains("Open my groups", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Still off", text, StringComparison.Ordinal);
        });
    }

    [Fact]
    [Trait("Req", "PR-MCP-222")] // App/Views/Settings/MCPSetupPanel.swift:155-160
    public void Once_checked_a_repeat_needs_setup_state_shows_the_still_off_status_line_with_the_guides_note()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            SetState(model, "gitlab", new MCPConnectionState.NeedsSetup(Guide()));

            var view = new MCPSetupPanelView(Entry("gitlab"), Guide(), model, () => { });
            var checkedOnceField = typeof(MCPSetupPanelView).GetField("checkedOnce", BindingFlags.Instance | BindingFlags.NonPublic)!;
            checkedOnceField.SetValue(view, true);
            var updateBody = typeof(MCPSetupPanelView).GetMethod("UpdateBody", BindingFlags.Instance | BindingFlags.NonPublic)!;
            updateBody.Invoke(view, null);

            var text = AllText(view);
            Assert.Contains("Still off on the server's side. " + Guide().Note, text, StringComparison.Ordinal);
        });
    }

    [Fact]
    [Trait("Req", "PR-MCP-222")] // App/Views/Settings/MCPSetupPanel.swift:155-160
    public void Before_any_check_a_needs_setup_state_does_not_show_the_still_off_status_line()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            SetState(model, "gitlab", new MCPConnectionState.NeedsSetup(Guide()));

            var view = new MCPSetupPanelView(Entry("gitlab"), Guide(), model, () => { });

            Assert.DoesNotContain("Still off", AllText(view), StringComparison.Ordinal);
        });
    }

    [Fact]
    [Trait("Req", "PR-MCP-222")] // App/Views/Settings/MCPSetupPanel.swift:161-166
    public void A_failed_state_after_a_check_shows_its_message_verbatim()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            SetState(model, "gitlab", new MCPConnectionState.Failed("could not reach the server"));

            var view = new MCPSetupPanelView(Entry("gitlab"), Guide(), model, () => { });
            var checkedOnceField = typeof(MCPSetupPanelView).GetField("checkedOnce", BindingFlags.Instance | BindingFlags.NonPublic)!;
            checkedOnceField.SetValue(view, true);
            var updateBody = typeof(MCPSetupPanelView).GetMethod("UpdateBody", BindingFlags.Instance | BindingFlags.NonPublic)!;
            updateBody.Invoke(view, null);

            Assert.Contains("could not reach the server", AllText(view), StringComparison.Ordinal);
        });
    }

    [Fact]
    [Trait("Req", "PR-MCP-223")] // App/Views/Settings/MCPSetupPanel.swift:239-251
    public void A_connected_state_replaces_the_body_with_the_success_view_reading_the_tool_count()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            SetState(model, "gitlab", new MCPConnectionState.Connected(3));

            var view = new MCPSetupPanelView(Entry("gitlab"), Guide(), model, () => { });

            var text = AllText(view);
            Assert.Contains("Connected · 3 tools", text, StringComparison.Ordinal);
            Assert.DoesNotContain(Guide().Reason, text, StringComparison.Ordinal);
        });
    }
}
