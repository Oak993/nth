using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models.ComposerLights;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Composer;

// ED-NEW-perf-motion-6: with a wallpaper showing, what lies under the composer is measured again (a GPU readback, OD-0150 ED-NEW-lightv2-6)
// when the composer's or the window's size changes; a size that changes every frame (a zoom glide, a sidebar slide, a live resize) is
// measured once, after it has held still for ComposerLightSlot.StillSize, not on each frame.
[Collection("Headless")]
public sealed class ComposerLightRemeasureTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-NEW-perfmotion-6")]
    public void A_composer_width_that_changes_every_frame_is_measured_once_after_it_holds_still()
    {
        headless.OnUiThread(() =>
        {
            System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
            using var h = new ThreadRuntimePart12Harness();
            h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
            var runtime = h.Main.OnMain(() => new ThreadRuntime(Guid.NewGuid(), h.App));
            h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
            var area = h.Main.OnMain(() => new ComposerArea(runtime, workingDirectory: null));
            var host = new StackPanel { Width = 1000, VerticalAlignment = VerticalAlignment.Bottom };
            var window = h.Main.OnMain(() =>
            {
                host.Children.Add(area);
                AppEnvironment.SetAppModel(host, h.App);
                var shown = new AvaloniaWindow { Content = host, Width = 1100, Height = 700 };
                shown.Show();
                return shown;
            });
            Pump(h, window);
            var light = h.Main.OnMain(() => area.GetLogicalDescendants().OfType<ComposerLightSlot>().Single().Light);
            h.Main.OnMain(() => light.Wallpaper = new WallpaperState(Showing: true));
            Pump(h, window);
            var settled = h.Main.OnMain(() => light.Wallpaper.Revision);

            var seen = new List<int>();
            var before = h.Main.OnMain(() => window.Bounds.Width);
            for (var frame = 0; frame < 8; frame++) // a live resize: the window a little narrower every frame
            {
                h.Main.OnMain(() => window.Width -= 9);
                Pump(h, window);
                h.Main.Advance(TimeSpan.FromMilliseconds(16));
                seen.Add(h.Main.OnMain(() => light.Wallpaper.Revision));
            }
            Assert.Equal(before - 72, h.Main.OnMain(() => window.Bounds.Width), 6);
            Assert.All(seen, revision => Assert.Equal(settled, revision));

            h.Main.Advance(ComposerLightSlot.StillSize + TimeSpan.FromMilliseconds(20));
            Pump(h, window);
            Assert.Equal(settled + 1, h.Main.OnMain(() => light.Wallpaper.Revision));
            h.Main.Advance(TimeSpan.FromSeconds(1));
            Pump(h, window);
            Assert.Equal(settled + 1, h.Main.OnMain(() => light.Wallpaper.Revision));
            h.Main.OnMain(() => window.Close());
        });
    }

    private static void Pump(ThreadRuntimePart12Harness h, AvaloniaWindow window)
    {
        for (var round = 0; round < 4; round++)
            h.Main.OnMain(() =>
            {
                for (var pass = 0; pass < 5; pass++)
                {
                    window.UpdateLayout();
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                }
            });
    }
}
