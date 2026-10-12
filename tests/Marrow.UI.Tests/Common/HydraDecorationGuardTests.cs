using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Common;

// ED-NEW-lean-8 (review): the breathing ring's guards for what the Reduce-Motion-off guard never reaches: Reduce Motion on
// and off, and theme changes while the ring rests (it then asks for no frames). One SHA-256 over the compositor's frames,
// recorded on the unchanged base, and a resting ring after a theme change checked against a ring made in that theme on the
// same machine (no pinned output).
[Collection("Headless")]
public sealed class HydraDecorationGuardTests(HeadlessSession headless)
{
    // Recorded on the unchanged base (claude/lean-l8 at 6c67eba4a, the base plus the test hooks).
    private const string RestingFrames = "F5FE61FD03D20CEE4F5D7900A883796D45C134E3D5F67C832FE2C0E19B7FEA3A";

    // a colour of each appearance: the label colour, as the ring of a themed glyph would use
    private static SwiftColor Stroke => Marrow.UI.Chrome.Chrome.PrimaryText.Opacity(0.55);

    [Fact]
    [Trait("Decision", "ED-NEW-lean-8")]
    public void The_ring_under_Reduce_Motion_and_theme_changes_draws_the_base_pixels() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        var (window, host) = Show(ThemeVariant.Light, reduceMotion: true);
        try
        {
            using var all = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var seen = new HashSet<string>();
            Tick(time, frames, 1.0);
            for (var k = 0; k < 20; k++)
            {
                switch (k)
                {
                    case 4: window.RequestedThemeVariant = ThemeVariant.Dark; break;
                    case 7: window.RequestedThemeVariant = ThemeVariant.Light; break;
                    case 9: EnvironmentValues.SetAccessibilityReduceMotion(host, false); break;
                    case 12: window.RequestedThemeVariant = ThemeVariant.Dark; break;
                    case 15: EnvironmentValues.SetAccessibilityReduceMotion(host, true); break;
                    case 17: window.RequestedThemeVariant = ThemeVariant.Light; break;
                }
                Dispatcher.UIThread.RunJobs();
                if (k > 0) Tick(time, frames, 0.173);
                var bytes = Capture(window);
                all.AppendData(bytes);
                seen.Add(Convert.ToHexString(SHA256.HashData(bytes)));
            }
            var hash = Convert.ToHexString(all.GetHashAndReset());
            Assert.True(seen.Count >= 6, $"the ring changes between the timestamps: {seen.Count} distinct frames");
            Assert.True(hash == RestingFrames, $"resting frames: {hash}");
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lean-8")]
    public void Under_Reduce_Motion_the_resting_ring_takes_a_theme_change_at_once() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        var (live, _) = Show(ThemeVariant.Light, reduceMotion: true);
        try
        {
            Tick(time, frames, 1.0);
            var before = Capture(live);
            live.RequestedThemeVariant = ThemeVariant.Dark;
            Dispatcher.UIThread.RunJobs();
            var after = Capture(live);
            var (made, _) = Show(ThemeVariant.Dark, reduceMotion: true);
            try
            {
                Tick(time, frames, 1.0);
                Assert.False(before.SequenceEqual(after), "the theme change shows on the ring");
                Assert.True(Capture(made).SequenceEqual(Capture(live)), "a resting ring after a theme change is the ring made in that theme");
            }
            finally { made.Close(); Dispatcher.UIThread.RunJobs(); }
        }
        finally { live.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    private static (Window Window, Border Host) Show(ThemeVariant theme, bool reduceMotion)
    {
        var ring = new HydraBreathRingView { Width = 20, Height = 20, StrokeColor = Stroke, StrokeWidth = 1.5 };
        var host = new Border { Child = ring };
        EnvironmentValues.SetAccessibilityReduceMotion(host, reduceMotion);
        var window = new Window { Width = 48, Height = 48, Content = host, Background = Brushes.Gray, RequestedThemeVariant = theme };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, host);
    }

    private static void Tick(ManualTimeProvider time, ManualFrameSource frames, double seconds)
    {
        time.Advance(TimeSpan.FromSeconds(seconds));
        frames.Advance(TimeSpan.FromSeconds(seconds));
        Dispatcher.UIThread.RunJobs();
    }

    private static byte[] Capture(Window window)
    {
        using var frame = window.CaptureRenderedFrame() ?? throw new Xunit.Sdk.XunitException("no rendered frame");
        using var buffer = frame.Lock();
        var bytes = new byte[buffer.RowBytes * buffer.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
        return bytes;
    }
}
