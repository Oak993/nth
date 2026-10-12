using System.Security.Cryptography;
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

// ED-NEW-lean-9 (lean pass L9, the 60 Hz pacing): the composer's working dots on a 240 Hz display, captured at every refresh for 24
// refreshes after 10 warm ones (1/240 s apart, well inside the pacer's 0.25 s reset, so a dropped or doubled 60 Hz slot shows), one
// forced compositor tick a refresh, as a display gives. Unpaced holds the SHA-256 (first 16 hex digits) of each refresh's pixels
// recorded on the unpaced field (claude/lean-r1 d723dd5f8, before the pacing): the field drawn at every refresh, at the time the
// clock shows. The paced field must show, at every refresh, exactly the picture the unpaced field showed at the refresh of the last
// 60 Hz slot: one held picture until the first slot, then a new one every 4th refresh (240 Hz is 4 refreshes a slot). The slot
// phase is the shared pacer's (other sources of the same compositor may have started its count), so exactly one of the four phases
// must match. So every drawn picture is the unpaced field's picture at the same instant, and the field draws on one refresh of each
// slot.
[Collection("Headless")]
public sealed class ComposerWorkingDotsPacedPixelGuardTests(HeadlessSession headless)
{
    private const double Rate = 240;
    private const int Refreshes = 24;
    private const int Warm = 10;

    private static string Capture(Avalonia.Controls.Window window)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        // the frame that one tick rendered (CaptureRenderedFrame forces more ticks at the same time, which a pacer counts as refreshes)
        using var frame = window.GetLastRenderedFrame();
        Assert.NotNull(frame);
        using var stream = new MemoryStream();
        frame!.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        using var pixels = SKBitmap.Decode(stream.ToArray());
        return Convert.ToHexString(SHA256.HashData(pixels.Bytes))[..16];
    }

    private static List<string> Pictures()
    {
        var period = RenderFrameSource.PeriodSource;
        RenderFrameSource.PeriodSource = () => TimeSpan.FromSeconds(1 / Rate);
        using var runtime = new TestRuntime();
        using var executor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var window = new Avalonia.Controls.Window { Width = 240, Height = 40, Background = Brushes.Black };
        window.Show();
        try
        {
            // 10 s in: every dot of the loop is in its wave, so consecutive refreshes differ
            runtime.Advance(TimeSpan.FromSeconds(10));
            window.Content = new ComposerWorkingDots { Tint = new SwiftColor(0.2, 0.6, 1), Height = 24, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom };
            // the mount and 10 refreshes first: the first tick after a mount is not always the same picture headless, and the pacer is
            // in its run by then
            Capture(window);
            for (var i = 0; i < Warm; i++)
            {
                runtime.Advance(TimeSpan.FromSeconds(1 / Rate));
                Capture(window);
            }
            var hashes = new List<string>();
            for (var i = 0; i < Refreshes; i++)
            {
                runtime.Advance(TimeSpan.FromSeconds(1 / Rate));
                hashes.Add(Capture(window));
            }
            Assert.Equal(0, frames.Pending);
            return hashes;
        }
        finally
        {
            window.Content = null; window.Close(); Dispatcher.UIThread.RunJobs();
            RenderFrameSource.PeriodSource = period;
        }
    }

    // Whether the paced pictures fit the slots k = phase (mod 4): from the first slot on, each refresh shows the unpaced picture of the
    // last slot; before it (a slot before the 24 refreshes), the field holds one picture.
    private static bool Fits(List<string> paced, int phase)
    {
        var first = Enumerable.Range(0, 4).First(k => k % 4 == phase);
        for (var k = 0; k < paced.Count; k++)
        {
            if (k < first) { if (paced[k] != paced[0]) return false; continue; }
            if (paced[k] != Unpaced[k - (k - phase) % 4]) return false;
        }
        return true;
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-9")]
    public void The_paced_dots_show_the_unpaced_picture_of_the_last_60_Hz_slot_at_every_240_Hz_refresh() => headless.OnUiThread(() =>
    {
        Assert.Equal(Refreshes, Unpaced.Length);
        Assert.True(Unpaced.Distinct().Count() >= Refreshes - 2, "the unpaced pictures barely change: the guard could not tell a dropped slot");
        var paced = Pictures();
        var phases = Enumerable.Range(0, 4).Where(phase => Fits(paced, phase)).ToArray();
        Assert.True(phases.Length == 1, $"the paced pictures match {phases.Length} slot phases, not one (now \"{string.Join("\", \"", paced)}\")");
        Assert.True(paced.Distinct().Count() >= Refreshes / 4, "the paced field skipped a 60 Hz slot");
    });

    private static readonly string[] Unpaced =
    [
        "6B9BA3026C303A94", "6D603F6034F24F01", "F9F669CC3E8B99A3", "CA8961ED187B18EF",
        "75B381D3161ECC83", "1BDECBA79957C19E", "07C0D2A39EC48461", "7E7BE751D21C7554",
        "4DF08445113BEE4B", "D06DE92B5AEC33F7", "10103D6600ED556E", "D49CF3552FE8188E",
        "FAC7B0DDD0BB1403", "1C1404C53D0D9C01", "BD68F28F9B2084B5", "DD4FB5BD628D9D75",
        "BD3BFA0FA3CAA4F2", "EF61B35AFF94BA03", "2B8CD73126E273C1", "8143A8949DA045AA",
        "659C627EB8508307", "8A0C365B850C6090", "B351CB5D9D6AAD58", "ACA446A1A399C369",
    ];
}
