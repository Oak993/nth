using System.Diagnostics;
using Avalonia.Headless;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Terminal;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Chat;

// Written for the lead only: machine WakeTimer cadence, including jitter/late frames, through the actual headless drawer scene.
// This is timing/geometry and a rendered-frame availability check; it does not certify physical desktop screen pixels.
[Collection("Headless")]
public sealed class DrawerMotionLandingNativeTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(60, false)]
    [InlineData(144, false)]
    [InlineData(60, true)]
    [InlineData(144, true)]
    [Trait("Decision", "ED-NEW-motion-drawer-3")]
    public void Native_retargeted_drawer_and_partners_stay_together_and_land_on_the_machines_frames(int hertz, bool closing)
        => ChatHeadlessFixtures.Run(headless, () =>
        {
            if (!OperatingSystem.IsWindows()) return;
            using var rig = new DrawerMotionRig(PanelDockCorner.BottomLeading);
            rig.Toggle(true); rig.Frame(1);
            rig.Toggle(false); rig.Frame(.05);
            rig.Toggle(true); rig.Frame(.05);
            if (closing) rig.Toggle(false);
            var target = closing ? -rig.Terminal.Bounds.Height : 0;
            var values = new List<double> { rig.Margin };
            var panels = new List<double> { rig.RawPanelBottom };
            var times = new List<double> { 0 };
            using var signal = new SemaphoreSlim(0);
            var timer = WakeTimer.Create(() => signal.Release(), job => job(), precise: true);
            Assert.NotNull(timer);
            var clock = Stopwatch.StartNew();
            try
            {
                var frame = 1d / hertz;
                for (var next = frame; next < 2; next += frame)
                {
                    timer.Arm(TimeSpan.FromSeconds(Math.Max(.0005, next - clock.Elapsed.TotalSeconds)));
                    Assert.True(signal.Wait(TimeSpan.FromSeconds(2)), "the native frame wake timer fired");
                    var now = clock.Elapsed.TotalSeconds;
                    rig.Frame(now - times[^1]);
                    times.Add(now);
                    var gone = !LogicalQuery.SelfAndDescendants<TerminalPanel>(rig.View).Any();
                    values.Add(gone ? target : rig.Margin);
                    panels.Add(rig.RawPanelBottom);
                    if (!gone) Assert.Equal(rig.ComposerBottom, rig.TerminalTop, tolerance: 1);
                    Assert.Equal(rig.Gap, rig.ComposerBottom - rig.PanelBottom, tolerance: 1);
                    if (gone || rig.Margin == target) break;
                    if (now > next + frame) next = Math.Floor(now / frame) * frame;
                }
                Assert.Equal(target, values[^1]);
                Assert.True(values.Count >= 3);
                AssertLanding(times, values);
                AssertLanding(times, panels);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                rig.Drain();
                using var drawn = rig.Window.CaptureRenderedFrame();
                Assert.NotNull(drawn);
                Assert.True(drawn.PixelSize.Width > 0 && drawn.PixelSize.Height > 0);
            }
            finally { timer.Dispose(); }
        });

    private static void AssertLanding(IReadOnlyList<double> times, IReadOnlyList<double> values)
    {
        var n = values.Count - 1;
        var last = Math.Abs(values[n] - values[n - 1]);
        var previous = Math.Abs(values[n - 1] - values[n - 2]);
        Assert.True(last / (times[n] - times[n - 1]) <= previous / (times[n - 1] - times[n - 2]) || last < 1e-6,
            $"native landing accelerated: {previous:E3} px then {last:E3} px");
    }
}
