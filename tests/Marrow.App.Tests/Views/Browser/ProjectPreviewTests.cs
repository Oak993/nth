using Avalonia.Controls;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Browser;
using Marrow.Core.Web;

namespace Marrow.App.Tests.Views.Browser;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w9-3")]
public sealed class ProjectPreviewTests(HeadlessSession headless)
{
    [Fact]
    public void Preview_tabs_have_the_bar_drawer_and_shared_splitters_browse_tabs_do_not() => headless.OnUiThread(() =>
    {
        using var rig = new VisusRig();
        var tab = rig.Page("http://localhost:5173/", VisusTabKind.Preview);
        var body = new VisusPaneBody(rig.Session);
        Assert.NotNull(body.Preview);
        Assert.Equal(38, body.Preview!.Bar.Height);
        Assert.Equal(3, body.Preview.DeviceSplitters.Count);
        Assert.Equal("390 × 844", body.Preview.Bar.SizeText);
        body.Preview.Drawer.State.Begin(); body.Preview.Drawer.State.DragTo(33, 800); body.Preview.Drawer.State.End();
        body.Preview.Drawer.Refresh();
        Assert.True(body.Preview.Drawer.IsStrip); Assert.Equal(24, body.Preview.Drawer.ShownHeight);
        rig.Session.NewTab(VisusTabKind.Browse, "https://example.test/");
        // Sync on an unattached body is explicit; no real/native window is used.
        body.RefreshPreview(); Assert.Null(body.Preview);
        rig.Session.Select(tab); body.RefreshPreview(); Assert.NotNull(body.Preview);
    });
}
