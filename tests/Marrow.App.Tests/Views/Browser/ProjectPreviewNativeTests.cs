using Marrow.App.Tests.Window.Native;
using Marrow.App.Views.Browser;
using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.App.Tests.Views.Browser;

// Owner-scheduled only: the real Avalonia WebView2 HWND, isolated native fixture/data. Never run by W9.
[Collection("WindowManagerNative")]
[Trait("Decision", "ED-NEW-web-w9-3")]
public sealed class ProjectPreviewNativeTests(WindowManagerNativeSession native)
{
    [Theory]
    [InlineData("VITE")]
    [InlineData("NEXT")]
    public async Task Real_dev_server_listener_is_found_only_in_the_fixture_process_tree(string framework)
    {
        // The lead starts disposable fixture projects before the scheduled run, never the owner's projects.
        var text = Environment.GetEnvironmentVariable("W9_" + framework + "_FIXTURE_PID");
        Assert.True(int.TryParse(text, out var pid) && pid > 0, "NATIVE_PRECONDITION: set W9_" + framework + "_FIXTURE_PID to the disposable dev server's script-shell PID");
        var found = await new PreviewDiscovery().FindAsync(pid);
        Assert.NotEmpty(found);
        Assert.All(found, url => Assert.Equal("localhost", new Uri(url).Host));
    }

    [Fact]
    public void Real_device_emulation_tracks_css_pixels_and_responsive_drag_uses_a_rectangular_region() => native.Run(subject =>
    {
        var root = Path.Combine(Path.GetTempPath(), "w9-native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "index.html"), "<meta name='viewport' content='width=device-width,initial-scale=1'><body style='margin:0;background:#ff00ff'>fixture<script>console.error('fixture error')</script>");
        VisusSession? session = null;
        try
        {
            var (tab, body) = native.OnUi(() =>
            {
                VisusHost.ResetForTests();
                VisusHost.SurfaceFactory = () => new WebSurface(new WebView2Engine(), new WebProfiles(Path.Combine(root, "WebView2"), "en-US"));
                VisusHost.DataFolder = null;
                session = new VisusSession();
                var site = new StaticPreviewSite("nativefixture", root);
                var tab = session.NewTab(VisusTabKind.Preview, site.Origin + "/");
                tab.View!.Engine!.SetResourceHandler(site);
                var body = new VisusPaneBody(session);
                subject.MainContent.Children.Add(body);
                return (tab, body);
            });
            Assert.True(SpinWait.SpinUntil(() => native.OnUi(() => tab.View!.Engine!.IsAttached && !tab.IsLoading), TimeSpan.FromSeconds(15)), "real WebView2 did not attach/navigate");
            var controller = native.OnUi(() => body.Preview!);
            native.OnUi(() => controller.SetDevice(DevicePreset.Phone));
            native.OnUi(() => controller.WhenIdle).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            var result = native.OnUi(() => tab.View!.Cdp!.CallAsync("Runtime.evaluate", new() { ["expression"] = "JSON.stringify([innerWidth,innerHeight,devicePixelRatio])", ["returnByValue"] = true })).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            Assert.Equal("[390,844,3]", result.GetProperty("result").GetProperty("value").GetString());
            native.OnUi(() => { controller.SetDevice(DevicePreset.Responsive); controller.BeginResponsive(); controller.ResizeResponsive(412, 860); });
            native.OnUi(() => controller.WhenIdle).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            var region = native.OnUi(() => ((WebView2View)tab.View!.Engine!).PreviewRegion);
            Assert.NotNull(region);
            Assert.Equal(412, controller.Bar.Metrics.Width); Assert.Equal(860, controller.Bar.Metrics.Height);
            Assert.True(region!.IsRectangular, "HWND clipping must be CreateRectRgn, not a rounded/aliased clip");
            Assert.Equal(region.InitialWidth, region.CurrentWidth); Assert.Equal(region.InitialHeight, region.CurrentHeight);
            native.OnUi(controller.EndResponsive);
        }
        finally
        {
            native.OnUi(() => { session?.Dispose(); VisusHost.ResetForTests(); });
            Directory.Delete(root, recursive: true);
        }
    });
}
