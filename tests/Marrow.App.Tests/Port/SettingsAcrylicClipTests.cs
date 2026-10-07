using System.Numerics;
using Avalonia;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using HostWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Port;

[Collection("Headless")]
public sealed class SettingsAcrylicClipTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-249")]
    public void A_failed_initial_clip_update_releases_its_geometry() => headless.OnUiThread(() =>
    {
        var geometry = new SettingsClipGeometry { RejectOffset = true };
        var window = new HostWindow();
        Assert.Throws<InvalidOperationException>(() => new SettingsAcrylicClip(window, geometry, new object(), 180, new Size(780, 580)));
        Assert.Equal(1, geometry.Disposals);
        window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-249")]
    public void The_persistent_inner_clip_updates_physical_geometry_and_releases_once() => headless.OnUiThread(() =>
    {
        var geometry = new SettingsClipGeometry();
        var window = new HostWindow();
        var clip = new SettingsAcrylicClip(window, geometry, new object(), 180, new Size(780, 580));
        Assert.Equal(new Vector2(180), geometry.Offset);
        Assert.Equal(new Vector2(780, 580), geometry.Size);
        Assert.Equal(new Vector2(8), geometry.Radius);
        clip.Update(1.5);
        Assert.Equal(new Vector2(270), geometry.Offset);
        Assert.Equal(new Vector2(1170, 870), geometry.Size);
        Assert.Equal(new Vector2(12), geometry.Radius);
        Assert.Equal(0, geometry.Disposals);
        clip.Dispose(); clip.Dispose(); clip.Update(2);
        Assert.Equal(1, geometry.Disposals);
        Assert.Equal(new Vector2(1170, 870), geometry.Size);
        window.Close();
    });
}
