// PORT-ONLY: Tactus, the native frame capture (P30; ED-NEW-pcctl-30; DESIGN.md 10.11 "Native", 10.13 NP-16). NATIVE and
// owner-scheduled: run it alone, in a quiet session, on a DEBUG build (every regular run excludes FullyQualifiedName~Native,
// which the class name carries). It starts MARROW_TACTUS_EXE (a debug Marrow.exe) with the overlay spike's np16 probe and
// --tactus-capture-test, captures the spike's top band through Windows.Graphics.Capture while its pill travels, finds the
// pill's left edge in every captured frame and holds each travel against its closed form (from rest, within 1 DIP by
// 400 ms, never past its target, no frame off its curve by more than the capture's own time spread); then it reads the
// probe's RESULT line: the p99 frame interval at most 1.5 refresh intervals. It sends no input and drives no other window.
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Marrow.App.Port.PcControl;
using Marrow.App.Port.PcControl.Overlay;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.PcControl;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.Services.Port.PcControl;

namespace Marrow.App.Tests.Port.PcControl.Native;

[Trait("Decision", "ED-NEW-pcctl-30")]
public sealed class PillCaptureNativeTests
{
    private const double CaptureSeconds = 14;

    // the overlay spike's command and its np16 pill (OverlaySpike, a debug-only class: its values, kept here)
    private const string SpikeCommand = "--tactus-overlay-spike";
    private const double SpikeRunSeconds = 90;
    private const double PillTop = 16;
    private const double PillHeight = 48;

    [Fact]
    public async Task The_spike_s_pill_travels_on_its_curve_in_every_captured_frame_and_paces_its_frames()
    {
        var exe = Environment.GetEnvironmentVariable("MARROW_TACTUS_EXE");
        Assert.False(string.IsNullOrEmpty(exe), "set MARROW_TACTUS_EXE to a DEBUG Marrow.exe");
        Assert.True(WgcWindowCapture.IsSupported);
        var log = Path.Combine(Path.GetTempPath(), "tactus-spike-np16.log");
        if (File.Exists(log)) File.Delete(log);
        using var spike = Process.Start(new ProcessStartInfo(exe!, $"{SpikeCommand} np16 {TactusCaptureTest.Flag}") { UseShellExecute = false, CreateNoWindow = true })!;
        try
        {
            var band = await BandOf(spike.Id);
            Assert.NotEqual((nint)0, band);
            var scale = PcTopWindows.Dpi(band) / 96.0;
            var rect = PcTopWindows.Rect(band);
            Assert.NotNull(rect);
            var frame = new PcRect(rect.Value.X, rect.Value.Y, rect.Value.Width, rect.Value.Height);
            using var capture = new WgcWindowCapture();
            var edges = new List<(double Before, double After, double Left)>();
            var end = Clock() + CaptureSeconds;
            while (Clock() < end)
            {
                var before = Clock();
                var shot = await capture.Capture(band, frame, CancellationToken.None);
                var after = Clock();
                if (shot is not null && LeftEdge(shot, (int)Math.Round((PillTop + (PillHeight / 2)) * scale)) is { } left)
                    edges.Add((before, after, left / scale));
            }
            Assert.True(edges.Count > 200, $"only {edges.Count} frames captured");

            // the rests are the quarter points; each run of frames between them is one travel from rest
            var rests = edges.Select(e => Math.Round(e.Left)).GroupBy(x => x).OrderByDescending(g => g.Count()).Take(2).Select(g => g.Key).Order().ToArray();
            Assert.Equal(2, rests.Length);
            var travels = 0;
            for (var i = 1; i < edges.Count; i++)
            {
                if (Math.Abs(edges[i].Left - edges[i - 1].Left) < 0.5) continue;
                var from = Math.Abs(edges[i - 1].Left - rests[0]) < 1 ? rests[0] : rests[1];
                var to = from == rests[0] ? rests[1] : rests[0];
                var last = i;
                while (last + 1 < edges.Count && Math.Abs(edges[last + 1].Left - to) > 0.5) last++;
                Check(edges.Skip(i - 1).Take(last - i + 3).ToList(), from, to);
                travels++;
                i = last + 1;
            }
            Assert.True(travels >= 2, $"only {travels} travels captured");
        }
        finally
        {
            if (!spike.WaitForExit(TimeSpan.FromSeconds(SpikeRunSeconds + 15))) spike.Kill();
        }

        // the probe's own frame pacing (its RESULT line, written as it exits)
        var text = File.ReadAllText(log);
        var refresh = double.Parse(Regex.Match(text, @"refresh=(\d+(\.\d+)?) Hz").Groups[1].Value, CultureInfo.InvariantCulture);
        var result = Regex.Match(text, @"RESULT np16 [^\n]*p99 (\d+(\.\d+)?) ms");
        Assert.True(result.Success, "no np16 RESULT line in " + log);
        var p99 = double.Parse(result.Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.True(p99 <= 1.5 * 1000 / refresh, $"p99 frame interval {p99} ms at {refresh} Hz");
    }

    // One travel: the start time that fits the frames best, then every frame on the curve within what the capture's own
    // time spread allows (its speed over the grab, plus 1.5 DIP for the edge's pixel), never past its target, landed by 400 ms.
    private static void Check(List<(double Before, double After, double Left)> frames, double from, double to)
    {
        var first = frames.First(f => Math.Abs(f.Left - from) >= 0.5);
        var best = double.NaN;
        var bestError = double.PositiveInfinity;
        for (var start = first.After - 0.1; start <= first.After; start += 0.0005)
        {
            var spring = PillSpring.Travel(start, new PillVector(from, 0), new PillVector(to, 0), PillVector.Zero);
            var error = frames.Max(f => Math.Abs(spring.Value((f.Before + f.After) / 2).X - f.Left));
            if (error < bestError) (best, bestError) = (start, error);
        }
        var curve = PillSpring.Travel(best, new PillVector(from, 0), new PillVector(to, 0), PillVector.Zero);
        foreach (var f in frames)
        {
            var mid = (f.Before + f.After) / 2;
            var allowed = (Math.Abs(curve.Velocity(mid).X) * (f.After - f.Before) / 2) + 1.5;
            Assert.True(Math.Abs(curve.Value(mid).X - f.Left) <= allowed, $"a frame at {f.Left} DIP is {Math.Abs(curve.Value(mid).X - f.Left)} DIP off its curve");
            Assert.True((f.Left - to) * Math.Sign(to - from) <= 1, $"a frame at {f.Left} DIP passed its target {to}");
            if (f.Before - best >= PillSpring.LandingSeconds) Assert.True(Math.Abs(f.Left - to) <= PillSpring.LandingDistance + 1, $"{f.Left} DIP at 400 ms");
        }
    }

    // The pill's left edge on one row: the first pixel drawn over the transparent band.
    private static int? LeftEdge(PcFrame frame, int row)
    {
        if (row < 0 || row >= frame.Height) return null;
        var line = row * frame.Stride;
        for (var x = 0; x < frame.Width; x++)
            if (frame.Bgra[line + (x * 4) + 3] > 128) return x;
        return null;
    }

    // The spike's first band: its process's visible top-level window that is wider than tall.
    private static async Task<nint> BandOf(int process)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            foreach (var window in PcTopWindows.Of(process))
                if (PcTopWindows.Rect(window) is { } r && r.Width > 3 * r.Height) return window;
            await Task.Delay(200);
        }
        return 0;
    }

    private static double Clock() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
}
