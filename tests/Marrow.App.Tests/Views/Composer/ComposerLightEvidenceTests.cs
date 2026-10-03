using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models.ComposerLights;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port.ComposerLights;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Composer;

// DEVIATION OD-0089 (owner-ruled addition; ED-251): the light on the app's real composer (ComposerArea: the glass pill, its
// text and controls), drawn by the compositor in a headless window over the playground's window colours (gen.py WIN_D,
// WIN_L), for the evidence folder (MARROW_COMPOSER_LIGHT_EVIDENCE): Border beam, the Uiverse light and Off, dark and
// light, at rest and typing, and frames through a change of style. The assertions check what the frames must show.
[Collection("Headless")]
public sealed class ComposerLightEvidenceTests(HeadlessSession headless)
{
    private static ThreadRuntimePart12Harness Harness()
    {
        System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
        var h = new ThreadRuntimePart12Harness();
        h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        return h;
    }

    private static IBrush Wall(bool dark) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0.41, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0.59, RelativeUnit.Relative),
        GradientStops = dark
            ? [new GradientStop(Color.Parse("#50463F"), 0), new GradientStop(Color.Parse("#523F36"), 0.52), new GradientStop(Color.Parse("#41362F"), 1)]
            : [new GradientStop(Color.Parse("#DFD4CC"), 0), new GradientStop(Color.Parse("#E1CEC4"), 0.52), new GradientStop(Color.Parse("#D0C4BC"), 1)],
    };

    private static void Frame(ThreadRuntimePart12Harness h, AvaloniaWindow window)
    {
        for (var round = 0; round < 3; round++)
            h.Main.OnMain(() =>
            {
                window.UpdateLayout();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            });
    }

    private static (byte[] Pixels, int Width) Capture(ThreadRuntimePart12Harness h, AvaloniaWindow window, string? name)
    {
        return h.Main.OnMain(() =>
        {
            using var bitmap = window.CaptureRenderedFrame()!;
            var folder = Environment.GetEnvironmentVariable("MARROW_COMPOSER_LIGHT_EVIDENCE");
            if (name is not null && !string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
                bitmap.Save(Path.Combine(folder, name + ".png"), new PngBitmapEncoderOptions());
            }
            var size = bitmap.PixelSize;
            var bytes = new byte[size.Width * size.Height * 4];
            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try { bitmap.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), bytes.Length, size.Width * 4); }
            finally { handle.Free(); }
            return (bytes, size.Width);
        });
    }

    private static int Changed((byte[] Pixels, int Width) a, (byte[] Pixels, int Width) b, Rect region)
    {
        var count = 0;
        for (var y = (int)region.Top; y < (int)region.Bottom; y++)
            for (var x = (int)region.Left; x < (int)region.Right; x++)
            {
                var o = (y * a.Width + x) * 4;
                if (Math.Abs(a.Pixels[o] - b.Pixels[o]) + Math.Abs(a.Pixels[o + 1] - b.Pixels[o + 1]) + Math.Abs(a.Pixels[o + 2] - b.Pixels[o + 2]) > 6) count++;
            }
        return count;
    }

    [Fact]
    [Trait("Decision", "ED-251")] // OD-0089: the light around the real composer, Border beam by default; the Uiverse light outside; Off nothing
    public void The_light_on_the_real_composer()
    {
        headless.OnUiThread(() =>
        {
            using var motion = new ComposerLightMotion();
            using var h = Harness();
            var clock = h.Main.Time;   // the harness's clock is SwiftClock's on its main loop
            var runtime = h.Main.OnMain(() => new ThreadRuntime(Guid.NewGuid(), h.App));
            h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
            foreach (var dark in new[] { true, false })
            {
                var area = h.Main.OnMain(() => new ComposerArea(runtime, workingDirectory: null));
                var window = h.Main.OnMain(() =>
                {
                    var host = new Border { Child = area, VerticalAlignment = VerticalAlignment.Bottom };
                    AppEnvironment.SetAppModel(host, h.App);
                    var w = new AvaloniaWindow { Content = host, Width = 900, Height = 260, Background = Wall(dark), RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
                    w.Show();
                    return w;
                });
                try
                {
                    var theme = dark ? "dark" : "light";
                    // a change reaches the light on the next main turn; time then moves on from there
                    void Use(ComposerLightSettings s) { h.Main.OnMain(() => h.App.Settings.ComposerLight = s); Frame(h, window); }
                    void Wait(double seconds) { clock.Advance(TimeSpan.FromSeconds(seconds)); Frame(h, window); }
                    Rect pill = default;
                    Use(ComposerLightSettings.Default with { Style = ComposerLightStyle.Off });
                    Wait(1);
                    h.Main.OnMain(() =>
                    {
                        var view = area.GetLogicalDescendants().OfType<ComposerView>().Single();
                        pill = new Rect(view.TranslatePoint(default, window)!.Value, view.Bounds.Size);
                    });
                    var off = Capture(h, window, $"app_{theme}_off");
                    Use(ComposerLightSettings.Default);
                    for (var i = 0; i < 4; i++) Wait(0.3);
                    var beam = Capture(h, window, $"app_{theme}_beam_rest");
                    Assert.True(Changed(off, beam, pill) > 150, "Border beam lights the composer");
                    Assert.Equal(0, Changed(off, beam, new Rect(0, 0, 900, pill.Top - 2)));      // and nothing above it
                    h.Main.OnMain(() => runtime.Draft = runtime.Draft with { Text = "Add a keyboard shortcut for the theme switch" });
                    Frame(h, window);
                    for (var i = 0; i < 3; i++) Wait(0.4);
                    Capture(h, window, $"app_{theme}_beam_typing");
                    h.Main.OnMain(() => runtime.Draft = runtime.Draft with { Text = "" });
                    // a change of style, frame by frame (OD-0076: no cut)
                    Use(ComposerLightSettings.Default with { Style = ComposerLightStyle.Aura });
                    for (var i = 1; i <= 5; i++) { Wait(0.12); Capture(h, window, $"app_{theme}_fade_{i}"); }
                    for (var i = 0; i < 3; i++) Wait(0.5);
                    var aura = Capture(h, window, $"app_{theme}_aura_rest");
                    Assert.True(Changed(off, aura, new Rect(pill.Left, pill.Top - 30, pill.Width, 25)) > 200, "the Uiverse halo shows above the composer");
                    Use(ComposerLightSettings.Default with { Style = ComposerLightStyle.Off });
                    for (var i = 0; i < 3; i++) Wait(0.5);
                    var offAgain = Capture(h, window, null);
                    Assert.Equal(0, Changed(off, offAgain, new Rect(0, 0, 900, 260)));
                }
                finally { h.Main.OnMain(() => window.Close()); }
            }
        });
    }
}
