using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Rendering.Composition;
using Marrow.App.Tests.Fixtures;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;
using Marrow.UI.Theme.Wallpapers;
using Xunit.Abstractions;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Window.Native;

// CU-SMOOTH2: real Win32 frame delivery and minimise/restore, for the lead only. Never part of a headless run.
// This checks delivery and wake behaviour; GPU presentation and perceived smoothness still require a desktop trace.
[Collection("WindowManagerNative")]
public sealed class Smooth2DecorationsNativeTests(WindowManagerNativeSession native, ITestOutputHelper output)
{
    [Fact]
    [Trait("Decision", "OD-0150")]
    public void The_retained_wallpaper_veil_keeps_gpu_pixels_through_resize_and_zoom() => native.Run(subject =>
    {
        var (window, probe) = native.OnUi(() =>
        {
            subject.Main.Hide(); subject.Settings.Hide();
            var host = new Border();
            var window = new AvaloniaWindow { Width = 160, Height = 100, Content = host, ShowInTaskbar = false };
            window.Template = new FuncControlTemplate<AvaloniaWindow>((owner, _) => new ContentPresenter { Content = owner.Content });
            native.Register(window); window.Show(); window.UpdateLayout();
            var probe = new Smooth2VeilGpuProbe();
            var visual = ElementComposition.GetElementVisual(host) ?? throw new InvalidOperationException("no compositor");
            var custom = visual.Compositor.CreateCustomVisual(probe);
            custom.Size = new Avalonia.Vector(1, 1);
            ElementComposition.SetElementChildVisual(host, custom); custom.SendHandlerMessage(new object());
            return (window, probe);
        });
        try
        {
            // ED-NEW-smooth2-6: same bounded, off-UI native probe wait as VoiceGlowNativeTests.
            var result = probe.Completion.Task.WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
            Assert.Equal(36, result.Comparisons);
            Assert.InRange(result.Milliseconds, 0, 1000.0 / 60);
            output.WriteLine($"{result.Backend}: {result.Comparisons} exact GPU comparisons, {result.Milliseconds:F3} ms per warm veil including GPU finish.");
        }
        finally { native.OnUi(() => window.Close()); }
    }, factType: typeof(Smooth2DecorationsNativeTests));

    [Fact]
    [Trait("Decision", "ED-NEW-lean-7")]
    public void The_style_glyph_is_paced_on_real_frames_and_parks_when_minimised() =>
        Check(() => new CzStyleGlyph("beam") { Width = 180 }, nameof(The_style_glyph_is_paced_on_real_frames_and_parks_when_minimised));

    [Fact]
    [Trait("Decision", "ED-NEW-wltwo-1")]
    public void A_live_wallpaper_thumbnail_is_paced_on_real_frames_and_parks_when_minimised() =>
        Check(() => new WallpaperThumbnail(WallpaperCatalogue.Get(WallpaperKind.Animated, "borealis"),
            new(WallpaperKind.Animated, "borealis"), new WallpaperThumbnailTicker()) { Width = 180, Height = 100 },
            nameof(A_live_wallpaper_thumbnail_is_paced_on_real_frames_and_parks_when_minimised));

    private void Check(Func<Control> content, string member)
    {
        native.Run(subject =>
        {
            var (window, frames, period) = native.OnUi(() =>
            {
                subject.Main.Hide(); subject.Settings.Hide();
                var window = new AvaloniaWindow { Width = 240, Height = 160, ShowInTaskbar = false };
                window.Template = new FuncControlTemplate<AvaloniaWindow>((owner, _) => new ContentPresenter { Content = owner.Content });
                native.Register(window);
                var source = new AvaloniaFrameSource(window);
                var period = source.WakePeriod ?? throw new InvalidOperationException("NATIVE_PRECONDITION: no precise display period.");
                var control = content();
                var angle = typeof(CzStyleGlyph).GetField("angle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                var frames = new Smooth2FrameSource(source, () => control is CzStyleGlyph glyph
                    ? (double)angle.GetValue(glyph)! : ((WallpaperThumbnail)control).Ticked);
                FrameClock.Source.SetProcessDefault(frames);
                window.Content = control; window.Show(); window.Activate();
                return (window, frames, period);
            });
            try
            {
                Thread.Sleep(1000); // warm real-window appearance, admission, shaders and the one-time fade
                var before = native.OnUi(() => frames.Answered);
                var posesBefore = native.OnUi(() => frames.Poses);
                Thread.Sleep(4000);
                var delivered = native.OnUi(() => frames.Answered) - before;
                var rate = delivered / 4.0;
                var poseRate = (native.OnUi(() => frames.Poses) - posesBefore) / 4.0;
                output.WriteLine($"{member}: {rate:F2} callbacks/s; {poseRate:F2} changed poses/s; display {1 / period.TotalSeconds:F2} Hz.");
                Assert.InRange(rate, 40, Math.Min(1 / period.TotalSeconds, 140) + 2);
                Assert.InRange(poseRate, member.StartsWith("The_style", StringComparison.Ordinal) ? Math.Min(1 / period.TotalSeconds, 60) - 3 : 40,
                    Math.Min(1 / period.TotalSeconds, 60) + 3);
                native.OnUi(() => window.WindowState = WindowState.Minimized);
                Thread.Sleep(1000); // let an already requested native frame finish
                before = native.OnUi(() => frames.Answered);
                Thread.Sleep(2000);
                Assert.Equal(before, native.OnUi(() => frames.Answered));
                native.OnUi(() => { window.WindowState = WindowState.Normal; window.Activate(); });
                Thread.Sleep(500);
                Assert.True(native.OnUi(() => frames.Answered) > before, "the decoration resumes on restore");
            }
            finally { native.OnUi(() => window.Close()); }
        }, member: member, factType: typeof(Smooth2DecorationsNativeTests));
    }
}
