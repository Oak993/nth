using Marrow.Services.MCP;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
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
    [Trait("Req", "PR-MCP-220")] // App/Views/Settings/MCPSetupPanel.swift:16-18
    public void IsPresenting_is_false_when_the_panel_is_not_visible()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();

            Assert.False(MCPSetupPanel.Shared.IsPresenting(Entry("gitlab")));
        });
    }

    [Fact]
    [Trait("Req", "PR-MCP-220")] // App/Views/Settings/MCPSetupPanel.swift:35-38
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
    [Trait("Req", "PR-MCP-221")] // App/Views/Settings/MCPSetupPanel.swift:42-50
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
    [Trait("Req", "PR-MCP-222")] // App/Views/Settings/MCPSetupPanel.swift:72-104
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
    [Trait("Req", "PR-MCP-222")] // App/Views/Settings/MCPSetupPanel.swift:106-110
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
    [Trait("Req", "PR-MCP-222")] // App/Views/Settings/MCPSetupPanel.swift:106-110
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
    [Trait("Req", "PR-MCP-222")] // App/Views/Settings/MCPSetupPanel.swift:111-115
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
    [Trait("Req", "PR-MCP-223")] // App/Views/Settings/MCPSetupPanel.swift:189-211
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

    // App/Views/Settings/MCPSetupPanel.swift:124-130 (unchanged B2->B3): `.background { WindowDragArea() }
    // .background { WindowBackdrop(opacity: 0.5) }` - SwiftUI's background modifier stacks its content behind the
    // view it modifies, in call order, so content sits above WindowDragArea, which sits above WindowBackdrop.
    // PaddingHost (this file) reproduces that with a Grid, the same idiom MCPKeyPanel.cs's PaddingHost already
    // uses for the identical Swift pattern (ED-200: WindowDragArea is a leaf Control in B3, not a Decorator a
    // caller can wrap content in, so the nested-Decorator composition this used in B2 no longer compiles).
    [Fact]
    [Trait("Req", "PR-MCP-220")]
    [Trait("Decision", "ED-200")]
    public void PaddingHost_grid_stacks_content_over_drag_area_over_backdrop()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();

            var view = new MCPSetupPanelView(Entry("gitlab"), Guide(), model, () => { });

            var border = Assert.IsType<Border>(view.Child);
            var layers = Assert.IsType<Grid>(border.Child);
            Assert.Equal(3, layers.Children.Count);
            // Grid children are added bottom-to-top: index 0 renders furthest back, the last index topmost -
            // and, in Avalonia, receives pointer hit-tests first - matching Swift's content-over-drag-over-backdrop.
            Assert.IsType<WindowBackdrop>(layers.Children[0]);
            Assert.IsType<WindowDragArea>(layers.Children[1]);

            var bodyField = typeof(MCPSetupPanelView).GetField("body", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.Same(bodyField.GetValue(view), layers.Children[2]);
        });
    }

    [Fact]
    [Trait("Req", "PR-MCP-223")] // App/Views/Settings/MCPSetupPanel.swift:135-141 onChange(of: state.isConnected) does not run for the initial value: a panel presented while already connected is not dismissed
    public void A_panel_presented_while_already_connected_is_not_dismissed_on_its_own()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            using var frames = FrameClock.Source.Override(new ManualFrameSource()); // the success glyph springs on the frame seam
            var model = harness.MakeModel();
            SetState(model, "gitlab", new MCPConnectionState.Connected(3));
            var done = 0;
            MCPSetupPanelView view = null!;
            Avalonia.Controls.Window window = null!;
            harness.Main.OnMain(() =>
            {
                view = new MCPSetupPanelView(Entry("gitlab"), Guide(), model, () => done++);
                window = new Avalonia.Controls.Window { Content = view };
                window.Show();
            });
            harness.Main.RunUntilIdle();
            harness.Main.AdvanceMilliseconds(2000);
            Assert.Equal(0, done);
            Assert.Contains("Connected", AllText(view), StringComparison.Ordinal);
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-MCP-223")] // App/Views/Settings/MCPSetupPanel.swift:135-141 a change to connected dismisses the panel after 1.2 seconds
    public void A_transition_to_connected_dismisses_the_panel_after_one_point_two_seconds()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            using var frames = FrameClock.Source.Override(new ManualFrameSource()); // the success glyph springs on the frame seam
            var model = harness.MakeModel();
            var done = 0;
            Avalonia.Controls.Window window = null!;
            harness.Main.OnMain(() =>
            {
                var host = new Border { Child = new MCPSetupPanelView(Entry("gitlab"), Guide(), model, () => done++) };
                window = new Avalonia.Controls.Window { Content = host };
                window.Show();
            });
            harness.Main.RunUntilIdle();
            var field = typeof(MCPStore).GetField("statesField", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var next = new Dictionary<string, MCPConnectionState>((Dictionary<string, MCPConnectionState>)field.GetValue(model.Mcp)!) { ["gitlab"] = new MCPConnectionState.Connected(2) };
            harness.Main.OnMain(() => typeof(MCPStore).GetProperty("States")!.SetValue(model.Mcp, next));
            harness.Main.RunUntilIdle();
            Assert.Equal(0, done);
            harness.Main.AdvanceMilliseconds(1300);
            Assert.Equal(1, done);
            window.Close();
        });
    }
}
