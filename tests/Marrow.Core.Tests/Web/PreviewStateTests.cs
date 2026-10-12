using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Web;

namespace Marrow.Core.Tests.Web;

[Trait("Decision", "ED-NEW-web-w9-1")]
public sealed class PreviewStateTests
{
    [Fact]
    public void PreviewUrl_round_trips_and_old_scripts_keep_no_url()
    {
        var old = JsonSerializer.Deserialize<ProjectScript>("{\"name\":\"dev\",\"command\":\"npm run dev\"}")!;
        Assert.Null(old.PreviewUrl);
        var next = old with { PreviewUrl = "https://localhost:5173/" };
        Assert.Equal(next, JsonSerializer.Deserialize<ProjectScript>(JsonSerializer.Serialize(next)));
        Assert.DoesNotContain("previewUrl", JsonSerializer.Serialize(old));
    }

    [Theory]
    [InlineData(DevicePreset.Phone, 390, 844, 3, true)]
    [InlineData(DevicePreset.Tablet, 820, 1180, 2, true)]
    [InlineData(DevicePreset.Laptop, 1280, 800, 1, false)]
    public void Presets_keep_css_pixels_scale_touch_and_rotation(DevicePreset preset, int w, int h, double dpr, bool touch)
    {
        var state = new PreviewState { Preset = preset };
        var m = state.Metrics(500, 600);
        Assert.Equal(w, m.Width); Assert.Equal(h, m.Height); Assert.Equal(dpr, m.DeviceScaleFactor); Assert.Equal(touch, m.Touch);
        Assert.True(m.Scale <= 1);
        Assert.Equal(h, (state with { Rotated = true }).Metrics(500, 600).Width);
    }

    [Fact]
    public void Collapse_below_34_restores_exact_height_and_closed_is_separate()
    {
        var drawer = new PreviewDrawerState();
        drawer.Begin(); drawer.DragTo(260, 800); drawer.End();
        drawer.Begin(); drawer.DragTo(33, 800); drawer.End();
        Assert.True(drawer.Collapsed); Assert.Equal(24, drawer.ShownHeight);
        drawer.Restore(); Assert.Equal(260, drawer.Height);
        drawer.Open = false; Assert.Equal(0, drawer.ShownHeight);
    }

    [Fact]
    public void Preview_settings_persist_with_the_tab_and_corrupt_sizes_are_clamped()
    {
        var state = new PreviewState { Preset = DevicePreset.Responsive, Width = 10_000, Height = 1, ReloadOnSave = false, ConsoleHeight = 250 };
        var tab = new VisusTabState("p", VisusTabKind.Preview, "http://localhost:5173/", "Preview") { Preview = state };
        var saved = new VisusTabsState { Tabs = new([tab]) };
        var restored = VisusTabsState.FromJson(saved.ToJson()).Tabs.Single().Preview!;
        Assert.Equal(4000, restored.Width); Assert.Equal(240, restored.Height);
        Assert.False(restored.ReloadOnSave); Assert.Equal(250, restored.ConsoleHeight);
    }
}
