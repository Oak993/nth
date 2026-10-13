using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port.Settings;

[Collection("Headless")]
public sealed class CzStyleGlyphWakeTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(60.1, false)]
    [InlineData(144, false)]
    [InlineData(240, false)]
    [InlineData(60.1, true)]
    [InlineData(144, true)]
    [InlineData(240, true)]
    [Trait("Decision", "ED-NEW-lean-8")]
    public void The_glyph_updates_sixty_poses_per_second_including_near_sixty_and_jitter(double rate, bool jitter)
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var frames = new ManualFrameSource { WakePeriod = TimeSpan.FromSeconds(1 / rate) };
            using var source = FrameClock.Source.Override(frames);
            WindowPresence.TestFactory = w => new WindowPresence(w, true);
            var glyph = new CzStyleGlyph("beam") { Width = 180 };
            var window = new Window { Width = 200, Height = 120, Content = glyph };
            var pose = typeof(CzStyleGlyph).GetField("angle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            try
            {
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var updates = 0; var previous = (double)pose.GetValue(glyph)!;
                long elapsed = 0;
                for (var k = 1; k <= (int)Math.Round(rate * 10); k++)
                {
                    var latency = jitter ? .0006 * (1 + Math.Sin(k * 7.31)) : 0;
                    var at = (long)Math.Round((k / rate + latency) * TimeSpan.TicksPerSecond);
                    var step = TimeSpan.FromTicks(at - elapsed); elapsed = at;
                    runtime.Advance(step); frames.Advance(step);
                    var angle = (double)pose.GetValue(glyph)!;
                    if (angle != previous) updates++;
                    previous = angle;
                }
                Assert.InRange(updates, 595, 605);
                Assert.True(frames.Delivered <= Math.Min(rate * 10, 1400) + 2,
                    $"{rate} Hz, jitter={jitter}: {frames.Delivered} callbacks for {updates} poses.");
            }
            finally { window.Close(); Dispatcher.UIThread.RunJobs(); WindowPresence.TestFactory = null; }
            Assert.Equal(0, frames.Pending);
        });
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-7")]
    public void A_style_glyph_keeps_its_clock_but_stops_requests_while_its_own_window_is_unseen()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var period = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 240);
            var frames = new ManualFrameSource { WakePeriod = period };
            using var source = FrameClock.Source.Override(frames);
            WindowPresence.TestFactory = w => new WindowPresence(w, true);
            var glyph = new CzStyleGlyph("beam") { Width = 180 };
            var window = new Window { Width = 200, Height = 120, Content = glyph };
            void Run(int count)
            {
                for (var i = 0; i < count; i++) { runtime.Advance(period); frames.Advance(period); }
            }
            try
            {
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                Run(240);
                var before = frames.Delivered;
                Run(2400);
                Assert.True(frames.Delivered - before <= 1400,
                    $"The glyph delivered {(frames.Delivered - before) / 10.0} callbacks/s (budget 140).");
                var presence = WindowPresence.For(glyph)!;
                presence.SetSeen(false); before = frames.Delivered;
                Run(2400);
                Assert.Equal(before, frames.Delivered);
                presence.SetSeen(true); Run(8);
                Assert.True(frames.Delivered > before);
                var angle = (double)typeof(CzStyleGlyph).GetField("angle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(glyph)!;
                var began = (ContinuousInstant)typeof(CzStyleGlyph).GetField("began", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(glyph)!;
                var expected = ((SwiftClock.Now - began).TotalSeconds / glyph.Period * 360) % 360;
                // A frame can be held until its 60 Hz slot; the pose still reads elapsed time, never an accumulated angle.
                Assert.InRange(Math.Abs(angle - expected), 0, 360 / glyph.Period / 60 + .001);
            }
            finally { window.Close(); Dispatcher.UIThread.RunJobs(); WindowPresence.TestFactory = null; }
            Assert.Equal(0, frames.Pending);
        });
    }
}
