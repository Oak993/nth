using Avalonia;
using Avalonia.Controls;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Testing;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.MCPSettingsPageFixture;

namespace Marrow.App.Tests.Views.Settings;

// Source: DroppyCode/App/Views/Settings/MCPSettingsPage.swift (B3 52bc792), :55-74; ED-218.
[Collection("Headless")]
public sealed class MCPSettingsPageTransitionTests(HeadlessSession headless)
{
    private static void Publish(AppModel model, string id) => typeof(Marrow.Services.MCP.MCPStore).GetProperty("States")!.SetValue(
        model.Mcp, new Dictionary<string, MCPConnectionState>(model.Mcp.States) { [id] = new MCPConnectionState.Connected(2) });
    private static void Layout(Control page) => ((Avalonia.Controls.Window)TopLevel.GetTopLevel(page)!).UpdateLayout();

    [Fact]
    [Trait("Decision", "ED-218")]
    [Trait("Req", "UI-MCP-018")] // App/Views/Settings/MCPSettingsPage.swift:55-57
    [Trait("Req", "PR-MCP-203")] // App/Views/Settings/MCPSettingsPage.swift:64-74
    public void Entering_celebration_glides_the_grid_even_when_a_state_rebuild_was_already_queued() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var harness = new AppModelHarness(withRuntime: false);
        var frames = new ManualFrameSource(); using var source = FrameClock.Source.Override(frames);
        using var sound = SoundOutput.Current.Override(new MCPSettingsSilentSound());
        AppModel model = null!; MCPSettingsPage page = null!; IDisposable window = null!; Control neighbor = null!; Grid grid = null!; Point old = default;
        runtime.OnMain(() =>
        {
            model = new AppModel(); page = new MCPSettingsPage(""); window = Attach(page, model); Layout(page);
            var github = Nodes<MCPServerCard>(page).Single(c => Read<MCPCatalogEntry>(c, "entry").Id == "github");
            grid = Assert.IsType<Grid>(github.Parent); neighbor = grid.Children.Single(c => Grid.GetRow(c) == 1 && Grid.GetColumn(c) == 0);
            old = neighbor.TranslatePoint(default, grid)!.Value; Publish(model, "github");
        });
        try
        {
            runtime.OnMain(() => { Layout(page); Assert.NotEqual(old.Y, neighbor.Bounds.Y); Assert.Equal(old.Y, neighbor.TranslatePoint(default, grid)!.Value.Y, 7); });
            runtime.AdvanceMilliseconds(100); runtime.OnMain(() => frames.Advance(TimeSpan.FromMilliseconds(100)));
            runtime.OnMain(() =>
            {
                var omega = 2 * Math.PI / 0.35; var progress = 1 - Math.Exp(-omega * 0.1) * (1 + omega * 0.1);
                Assert.Equal(old.Y + (neighbor.Bounds.Y - old.Y) * progress, neighbor.TranslatePoint(default, grid)!.Value.Y, 7);
            });
        }
        finally { runtime.OnMain(window.Dispose); }
    });

    [Fact]
    [Trait("Decision", "ED-218")]
    [Trait("Req", "UI-MCP-018")] // App/Views/Settings/MCPSettingsPage.swift:55-57
    [Trait("Req", "PR-MCP-203")] // App/Views/Settings/MCPSettingsPage.swift:64-74
    public void A_card_exits_with_scale_point_nine_and_opacity_while_the_next_row_glides_on_the_grid_spring() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var harness = new AppModelHarness(withRuntime: false);
        var frames = new ManualFrameSource(); using var source = FrameClock.Source.Override(frames);
        using var sound = SoundOutput.Current.Override(new MCPSettingsSilentSound());
        AppModel model = null!; MCPSettingsPage page = null!; IDisposable window = null!;
        MCPServerCard leaving = null!; Control neighbor = null!; Grid grid = null!; Point old = default;
        runtime.OnMain(() =>
        {
            model = new AppModel(); page = new MCPSettingsPage(""); window = Attach(page, model); Layout(page);
            leaving = Nodes<MCPServerCard>(page).Single(c => Read<MCPCatalogEntry>(c, "entry").Id == "github");
            grid = Assert.IsType<Grid>(leaving.Parent);
            neighbor = grid.Children.Single(c => Grid.GetRow(c) == 1 && Grid.GetColumn(c) == 0);
            old = neighbor.TranslatePoint(default, grid)!.Value;
            Publish(model, "github");
        });
        try
        {
            runtime.AdvanceMilliseconds(900);
            runtime.OnMain(() =>
            {
                Layout(page);
                Assert.Contains(Nodes<MCPConnectedRow>(page), r => Texts(r).Contains("GitHub"));
                Assert.Contains(leaving, grid.Children); Assert.Equal(1, leaving.Opacity);
                Assert.Equal(old.X, neighbor.TranslatePoint(default, grid)!.Value.X, 7);
                Assert.Equal(old.Y, neighbor.TranslatePoint(default, grid)!.Value.Y, 7);
            });
            runtime.AdvanceMilliseconds(100); runtime.OnMain(() => frames.Advance(TimeSpan.FromMilliseconds(100)));
            runtime.OnMain(() =>
            {
                var omega = 2 * Math.PI / 0.35;
                var progress = 1 - Math.Exp(-omega * 0.1) * (1 + omega * 0.1); // ED-011 critical spring
                Assert.Equal(1 - progress, leaving.Opacity, 7);
                var left = leaving.TranslatePoint(default, grid)!.Value;
                var right = leaving.TranslatePoint(new Point(leaving.Bounds.Width, 0), grid)!.Value;
                Assert.Equal(1 - 0.1 * progress, (right.X - left.X) / leaving.Bounds.Width, 7);
                Assert.Equal(old.X + (neighbor.Bounds.X - old.X) * progress, neighbor.TranslatePoint(default, grid)!.Value.X, 7);
                Assert.Equal(old.Y + (neighbor.Bounds.Y - old.Y) * progress, neighbor.TranslatePoint(default, grid)!.Value.Y, 7);
            });
            runtime.AdvanceMilliseconds(400); runtime.OnMain(() => frames.Advance(TimeSpan.FromMilliseconds(400)));
            runtime.OnMain(() => { Layout(page); Assert.DoesNotContain(leaving, grid.Children); Assert.Equal(neighbor.Bounds.Position, neighbor.TranslatePoint(default, grid)!.Value); });
        }
        finally { runtime.OnMain(window.Dispose); }
    });

    [Fact]
    [Trait("Decision", "ED-218")]
    [Trait("Req", "UI-MCP-018")] // App/Views/Settings/MCPSettingsPage.swift:49-57
    [Trait("Req", "PR-MCP-203")] // App/Views/Settings/MCPSettingsPage.swift:64-74
    public void The_last_catalog_card_keeps_its_category_attached_until_its_removal_transition_finishes() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var harness = new AppModelHarness(withRuntime: false);
        var frames = new ManualFrameSource(); using var source = FrameClock.Source.Override(frames);
        using var sound = SoundOutput.Current.Override(new MCPSettingsSilentSound());
        AppModel model = null!; MCPSettingsPage page = null!; IDisposable window = null!;
        runtime.OnMain(() => { model = new AppModel(); page = new MCPSettingsPage("github"); window = Attach(page, model); Publish(model, "github"); });
        try
        {
            runtime.AdvanceMilliseconds(900);
            runtime.OnMain(() => { Layout(page); Assert.Single(Nodes<MCPServerCard>(page)); Assert.Contains(Texts(page), t => t == "Developer"); });
            runtime.AdvanceMilliseconds(500); runtime.OnMain(() => frames.Advance(TimeSpan.FromMilliseconds(500)));
            runtime.OnMain(() => { Layout(page); Assert.Empty(Nodes<MCPServerCard>(page)); Assert.DoesNotContain("Developer", Texts(page)); });
        }
        finally { runtime.OnMain(window.Dispose); }
    });
}
