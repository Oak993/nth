using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Composer;

// ED-NEW-lean-9 (lean pass L9), the pixel guard: the composer's working dots, a finishing sweep and an accent ease, rendered headlessly at
// fixed clock times, must give the same pixels after the lean pass as before it. The hashes below were recorded on the unchanged field
// (claude/release-integration 1fdf47b43, the run before the L9 fix) and are the SHA-256 (first 16 hex digits) of each captured frame's
// pixels. Each picture is drawn at its exact time: the composer field's times are 0.37 s apart (more than a 60 Hz pacer's 0.25 s reset,
// so a later paced field also draws the first refresh after the gap), and the sweep and the ease are freshly mounted or retinted, so the
// frame the field sends is drawn on the next tick at the time the clock shows.
[Collection("Headless")]
public sealed class ComposerWorkingDotsPixelGuardTests(HeadlessSession headless)
{
    private static string Capture(Avalonia.Controls.Window window)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        using var stream = new MemoryStream();
        frame!.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        using var pixels = SKBitmap.Decode(stream.ToArray());
        return Convert.ToHexString(SHA256.HashData(pixels.Bytes))[..16];
    }

    private static void Match(string[] expected, List<string> actual, string what)
    {
        Assert.NotEmpty(expected);
        Assert.Equal(expected.Length, actual.Count);
        var differ = Enumerable.Range(0, actual.Count).Where(i => actual[i] != expected[i]).ToArray();
        Assert.True(differ.Length == 0, $"{what}: the pictures at {string.Join(", ", differ)} differ from the field before the lean pass (now \"{string.Join("\", \"", actual)}\")");
    }

    private static void Run(Action<TestRuntime, Avalonia.Controls.Window> body)
    {
        using var runtime = new TestRuntime();
        using var executor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var window = new Avalonia.Controls.Window { Width = 240, Height = 40, Background = Brushes.Black };
        window.Show();
        try { body(runtime, window); }
        finally { window.Content = null; window.Close(); Dispatcher.UIThread.RunJobs(); }
        Assert.Equal(0, frames.Pending);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-9")]
    public void The_composer_dots_are_pixel_equal_at_20_fixed_times() => headless.OnUiThread(() => Run((runtime, window) =>
    {
        var dots = new ComposerWorkingDots { Tint = new SwiftColor(0.2, 0.6, 1), Height = 24, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom };
        window.Content = dots;
        Capture(window);
        var hashes = new List<string>();
        for (var i = 0; i < 20; i++)
        {
            runtime.Advance(TimeSpan.FromSeconds(0.37));
            hashes.Add(Capture(window));
        }
        Assert.True(hashes.Distinct().Count() >= 15, "the pictures barely change: the guard could not tell");
        Match(Composer, hashes, "composer dots");
    }));

    [Fact]
    [Trait("Decision", "ED-NEW-lean-9")]
    public void A_finishing_sweep_is_pixel_equal_at_20_fixed_times() => headless.OnUiThread(() => Run((runtime, window) =>
    {
        var hashes = new List<string>();
        for (var i = 0; i < 20; i++)
        {
            window.Content = null;
            Capture(window);
            window.Content = new DotFieldSweep { Tint = new SwiftColor(1, 0.9, 0.4) };
            Dispatcher.UIThread.RunJobs();
            runtime.Advance(TimeSpan.FromSeconds(0.03 + 0.08 * i));
            hashes.Add(Capture(window));
        }
        Assert.True(hashes.Distinct().Count() >= 15, "the pictures barely change: the guard could not tell");
        Match(Sweep, hashes, "sweep");
    }));

    [Fact]
    [Trait("Decision", "ED-NEW-lean-9")]
    public void An_accent_ease_of_the_composer_dots_is_pixel_equal_at_10_fixed_times() => headless.OnUiThread(() => Run((runtime, window) =>
    {
        var hashes = new List<string>();
        for (var i = 0; i < 10; i++)
        {
            var dots = new ComposerWorkingDots { Tint = new SwiftColor(1, 0, 0), Height = 24, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom };
            window.Content = dots;
            runtime.Advance(TimeSpan.FromSeconds(0.5));
            Capture(window);
            dots.Tint = new SwiftColor(0, 0.4, 1);
            runtime.Advance(TimeSpan.FromSeconds(0.04 * i));
            hashes.Add(Capture(window));
        }
        Assert.True(hashes.Distinct().Count() >= 8, "the pictures barely change: the guard could not tell");
        Match(Ease, hashes, "accent ease");
    }));

    private static readonly string[] Composer =
    [
        "AF5861D674BF2EFE", "AF5861D674BF2EFE", "6CACEB105220BCC2", "4D4A3F039C6C11E5", "5591ED5680FE94D3", "49E70BD66D7F8CC3", "CA3996C5242E4894", "56F9A4028FBFBB1A", "5A95035891099DBC", "A3195943BA30ADF7", "D8A6DAC57AD13D4E", "C94659C46D445A61", "BF8233E2EEDC03DF", "B6CB210E039E0654", "7681533F219C89E5", "F0A3DBCBE216C3D6", "68E7905C5D425790", "2821BC59DB913563", "131C99BAD38F8608", "71BB15115531B75B",
    ];
    private static readonly string[] Sweep =
    [
        "28E2C4F713B68025", "51ECC0F012B9ECCA", "0345BDF7F690043E", "86CE016D296AB0AC", "D5480C9561138E92", "1ED313FF9011FA14", "6DE8913081C2EB3D", "211B63605B964732", "CFBD1F3E03582000", "9D78EFF239C42D82", "F8FA9CCC5317FBF2", "4AA0ADDC8A417AF1", "317505C4EE3E5D21", "AF2985080D28C87A", "A8153639D7D5DA1F", "30B9A27BB2D16D5C", "9EFF65AD488EAA34", "75E34CEEC41BE7AF", "DB5DFE7C569FA8BC", "DB5DFE7C569FA8BC",
    ];
    private static readonly string[] Ease =
    [
        "C34E14D7A5D9E1CB", "392C62995F0AEBEF", "43793D6C87C7E166", "FE8723CE574ACF04", "F921A1EFF74279EC", "F062F19DA406D70D", "D21C903919014D89", "1F390A15A02DA126", "310DC5A6C5D02AE7", "7F96C98FF2B87A80",
    ];
}
