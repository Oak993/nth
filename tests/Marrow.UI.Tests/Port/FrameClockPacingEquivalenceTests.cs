using System.Security.Cryptography;
using System.Text;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// ED-NEW-lean-7 equivalence: the instants a slow display link or timeline draws at, for 30, 24 and 20 Hz schedules on
// 240, 144 and 60 Hz displays, with and without frame-time jitter, and for timelines paused, resumed and re-timed on the
// way. The expected digests were recorded on the unchanged base (1fdf47b43), where every schedule asks for every frame.
public sealed class FrameClockPacingEquivalenceTests
{
    private const int Seconds = 4;

    // Recorded on 1fdf47b43: "<scenario> <draws> <first 16 hex digits of SHA-256 over the draw instants in ticks>".
    private const string Recorded =
        "link-30hz-on-240 120 B0E0C35180BC545F\n" +
        "link-30hz-on-240-jitter 120 790F2A84F7DCFFE3\n" +
        "link-24hz-on-240 96 58AE5AA3FBC3190E\n" +
        "link-24hz-on-240-jitter 96 61BC66566A1A51B9\n" +
        "link-20hz-on-240 80 364639FAD299E1E9\n" +
        "link-20hz-on-240-jitter 80 EB846B151D7CAC02\n" +
        "link-30hz-on-144 116 D740FDF266D025B8\n" +
        "link-30hz-on-144-jitter 116 68312E0BAF04C238\n" +
        "link-24hz-on-144 96 3628B669B9B9ADE2\n" +
        "link-24hz-on-144-jitter 96 0622DF187928B8C9\n" +
        "link-20hz-on-144 72 0162FA79F87E6088\n" +
        "link-20hz-on-144-jitter 73 668A6C6476FACC1A\n" +
        "link-30hz-on-60 120 2420D5D36ADEF421\n" +
        "link-30hz-on-60-jitter 120 D3A54B8347E91270\n" +
        "link-24hz-on-60 80 C620DA9AB3D7FE2D\n" +
        "link-24hz-on-60-jitter 80 1F9766488BB52CA4\n" +
        "link-20hz-on-60 80 C620DA9AB3D7FE2D\n" +
        "link-20hz-on-60-jitter 80 1F9766488BB52CA4\n" +
        "timeline-30hz-on-240 121 E4FF30AE458EA40E\n" +
        "timeline-30hz-on-240-jitter 121 BC844B4FA350DD66\n" +
        "timeline-24hz-on-240 97 3CA1E1C63C8AA60D\n" +
        "timeline-24hz-on-240-jitter 97 40F78A1662022DC6\n" +
        "timeline-20hz-on-240 81 0DE0B190BEA743A7\n" +
        "timeline-20hz-on-240-jitter 81 DBC36D7F3D036C6F\n" +
        "timeline-30hz-on-144 121 9779C1003377356F\n" +
        "timeline-30hz-on-144-jitter 121 B306C11D8914DFA4\n" +
        "timeline-24hz-on-144 97 3CA1E1C63C8AA60D\n" +
        "timeline-24hz-on-144-jitter 97 90A64EA08B2186CE\n" +
        "timeline-20hz-on-144 81 68FF53A1F0458829\n" +
        "timeline-20hz-on-144-jitter 81 830498F288926A89\n" +
        "timeline-30hz-on-60 121 E4FF30AE458EA40E\n" +
        "timeline-30hz-on-60-jitter 121 223D9193810B4FF0\n" +
        "timeline-24hz-on-60 97 DC2DA4D8ED9F780F\n" +
        "timeline-24hz-on-60-jitter 97 A04CC537FDC99C7D\n" +
        "timeline-20hz-on-60 81 0DE0B190BEA743A7\n" +
        "timeline-20hz-on-60-jitter 81 75F2587B526C9709\n" +
        "changes-30hz-on-240 104 B6FBCF8B3EAE1E60\n" +
        "changes-30hz-on-240-jitter 104 6DAD21DF0EA73DCA\n" +
        "changes-24hz-on-240 85 296E83095C9863F0\n" +
        "changes-24hz-on-240-jitter 85 F4D3C6A53AF8DF17\n" +
        "changes-20hz-on-240 90 10506FC0F0073224\n" +
        "changes-20hz-on-240-jitter 90 9F69B6BE9CC8B9AA\n" +
        "changes-30hz-on-144 104 A05B0826624061CA\n" +
        "changes-30hz-on-144-jitter 104 3544D948410AC4FF\n" +
        "changes-24hz-on-144 84 4A0DA343F8082D5B\n" +
        "changes-24hz-on-144-jitter 84 C2E86D561F1BBEFA\n" +
        "changes-20hz-on-144 90 8D850B68B24242FD\n" +
        "changes-20hz-on-144-jitter 90 7EC1DBA26CAC455D\n" +
        "changes-30hz-on-60 104 562FE1366FAC8AF0\n" +
        "changes-30hz-on-60-jitter 104 C6B02D9FC46BC860\n" +
        "changes-24hz-on-60 84 0F4CF7E706E1EB16\n" +
        "changes-24hz-on-60-jitter 84 7FB8695C7D2D1F55\n" +
        "changes-20hz-on-60 90 E896117212DC5E96\n" +
        "changes-20hz-on-60-jitter 90 64F72806C6FF8A40";

    private static readonly int[] Displays = [240, 144, 60];
    private static readonly int[] Rates = [30, 24, 20];

    private static string Digest(IReadOnlyList<long> draws)
    {
        var text = string.Join(",", draws);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
    }

    // The frame times of a display: frame k at k/display s in whole ticks, moved by up to 0.3 ms either way when jittered.
    private static long[] FrameTimes(int display, bool jitter)
    {
        var random = new Random(7 * display + (jitter ? 1 : 0));
        var times = new long[display * Seconds + 1];
        for (var k = 1; k < times.Length; k++)
            times[k] = k * TimeSpan.TicksPerSecond / display + (jitter ? random.Next(-3000, 3001) : 0);
        return times;
    }

    internal static List<long> Run(Func<ManualFrameSource> make, string kind, int rate, int display, bool jitter)
    {
        using var runtime = new TestRuntime();
        var frames = make();
        using var source = FrameClock.Source.Override(frames);
        var start = SwiftDate.Now;
        var draws = new List<long>();
        var paused = new CellSample<bool>(false);
        var retimed = new CellSample<bool>(false);
        var next = rate == 30 ? 24 : rate == 24 ? 20 : 30;
        runtime.OnMain(() =>
        {
            if (kind == "link")
            {
                var link = FrameClock.DisplayLink(() => draws.Add((SwiftDate.Now - start).Ticks));
                link.PreferredFrameRateRange = new FrameRateRange(24, 60, rate);
                link.Add();
            }
            else
            {
                var timeline = new AnimationTimeline(
                    () => new AnimationTimelineSchedule(MinimumInterval: 1.0 / (retimed.Value ? next : rate), Paused: paused.Value),
                    date => draws.Add((date - start).Ticks));
                timeline.Appear();
            }
        });
        var times = FrameTimes(display, jitter);
        for (var k = 1; k < times.Length; k++)
        {
            var gap = TimeSpan.FromTicks(times[k] - times[k - 1]);
            runtime.Advance(gap);
            runtime.OnMain(() => frames.Advance(gap));
            if (kind != "changes") continue;
            var at = times[k] / (double)TimeSpan.TicksPerSecond;
            var after = times[k - 1] / (double)TimeSpan.TicksPerSecond;
            if (after < 1.5 && at >= 1.5) runtime.OnMain(() => paused.Value = true);
            if (after < 1.8 && at >= 1.8) runtime.OnMain(() => paused.Value = false);
            if (after < 2.6 && at >= 2.6) runtime.OnMain(() => retimed.Value = true);
        }
        return draws;
    }

    internal static string Table(Func<ManualFrameSource> make)
    {
        var lines = new List<string>();
        foreach (var kind in new[] { "link", "timeline", "changes" })
            foreach (var display in Displays)
                foreach (var rate in Rates)
                    foreach (var jitter in new[] { false, true })
                    {
                        var draws = Run(make, kind, rate, display, jitter);
                        lines.Add($"{kind}-{rate}hz-on-{display}{(jitter ? "-jitter" : "")} {draws.Count} {Digest(draws)}");
                    }
        return string.Join("\n", lines);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-7")]
    public void Slow_schedules_draw_at_the_instants_recorded_on_the_unchanged_base() =>
        Assert.Equal(Recorded, Table(() => new ManualFrameSource()));
}
