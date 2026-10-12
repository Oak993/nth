using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.UI.Port;
using Xunit;
using Xunit.Abstractions;
using AvaloniaWindow = Avalonia.Controls.Window;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;

namespace Marrow.App.Tests.Window.Native;

// ED-NEW-lean-7, review 2026-10-11: native evidence owed before L7 merges (not run when written: the owner's PC was in use).
// (1) A slow draw loop held back by the production AvaloniaFrameSource wakes 1.5 refreshes before its draw into a render loop
// that has gone idle. ED-237 found an idle loop answering a request 16-31 ms late; the headless FrameClockIdleWakeTests show
// that wait is MediaContext's animations timer, which a request with a commit (every AvaloniaFrameSource request) never takes.
// Here the whole path runs on a real Win32 window and the display's own refreshes: a 30 Hz display link draws as often, and
// as soon after each due instant, as the same link asking at every frame (the path before ED-NEW-lean-7), with far fewer
// frame callbacks on a fast display. (2) The production host's answers on real windows: a never-shown window is a Win32
// window (Program.cs builds the process's source on one), a minimised window is hidden, a window the app cloaks itself is not,
// and Windows' cloak events reach the cloak watch. Shell cloaking (another virtual desktop) is not produced here: it would
// change the owner's desktops.
[Collection("WindowManagerNative")]
public sealed class FrameClockWakeNativeTests(WindowManagerNativeSession native, ITestOutputHelper output)
{
    private const int DwmwaCloak = 13, DwmwaCloaked = 14;

    private AvaloniaWindow Shown()
    {
        var window = new AvaloniaWindow { Width = 320, Height = 120, ShowInTaskbar = false, Content = new Border() };
        window.Template = new FuncControlTemplate<AvaloniaWindow>((owner, _) => new ContentPresenter { Content = owner.Content });
        native.Register(window);
        window.Show();
        return window;
    }

    private static nint Hwnd(AvaloniaWindow window) =>
        window.TryGetPlatformHandle()?.Handle ?? throw new InvalidOperationException("NATIVE_PRECONDITION: the test window has no HWND.");

    // A 30 Hz display link on the given source for `seconds` of wall time after a second's warm-up; each draw's lateness is the
    // time from the instant its interval was complete (ED-117 (2): 1/30 s since the last draw, 1 ms early) to the draw.
    private WakeLinkRun Link(Func<AvaloniaWindow, IFrameSource> source, double seconds)
    {
        var draws = new List<ContinuousInstant>();
        var (window, link, counter) = native.OnUi(() =>
        {
            var window = Shown();
            var frames = source(window);
            FrameClock.Source.SetProcessDefault(frames);
            var link = FrameClock.DisplayLink(() => draws.Add(SwiftClock.Now));
            link.PreferredFrameRateRange = new FrameRateRange(30, 30, 30);
            link.Add();
            return (window, link, frames);
        });
        Thread.Sleep(1000); // the first frames, JIT, the window's own appearance
        var (startDraw, startAnswered) = native.OnUi(() => (draws.Count, Answered(counter)));
        Thread.Sleep(TimeSpan.FromSeconds(seconds));
        var run = native.OnUi(() =>
        {
            link.Invalidate();
            var span = (draws[^1] - draws[startDraw]).TotalSeconds;
            var interval = TimeSpan.FromSeconds(1.0 / 30) - TimeSpan.FromMilliseconds(1);
            var late = new List<double>();
            for (var i = startDraw + 1; i < draws.Count; i++) late.Add((draws[i] - (draws[i - 1] + interval)).TotalMilliseconds);
            var result = new WakeLinkRun(draws.Count - startDraw - 1, span, Answered(counter) - startAnswered, [.. late.Order()]);
            window.Close();
            return result;
        });
        return run;
    }

    private static long Answered(IFrameSource source) => source switch
    {
        CountingEveryFrameSource every => every.Answered,
        CountingHeldFrameSource held => held.Answered,
        _ => 0,
    };

    private static double At(double[] sorted, double q) => sorted[(int)Math.Min(sorted.Length - 1, Math.Floor(q * sorted.Length))];

    [Fact]
    [Trait("Decision", "ED-NEW-lean-7")]
    public void A_held_thirty_hertz_link_draws_as_often_and_as_soon_after_each_due_instant_as_one_asking_at_every_frame()
    {
        native.Run(subject =>
        {
            native.OnUi(() =>
            {
                subject.Main.Hide();
                subject.Settings.Hide();
            });
            var period = native.OnUi(() => Win32FrameHost.Shared.RefreshPeriod())
                ?? throw new InvalidOperationException("NATIVE_PRECONDITION: DWM gave no refresh period.");
            var refreshMs = period.TotalMilliseconds;
            var before = Link(window => new CountingEveryFrameSource(new AvaloniaFrameSource(window)), 6);
            var after = Link(window =>
            {
                var production = new AvaloniaFrameSource(window);
                if (production.WakePeriod is null) throw new InvalidOperationException("NATIVE_PRECONDITION: the production source holds nothing back on this window.");
                return new CountingHeldFrameSource(production);
            }, 6);
            string Describe(string name, WakeLinkRun run) =>
                $"{name}: {run.Draws / run.Seconds:F2} draws/s, {run.Answered / run.Seconds:F1} frame callbacks/s, lateness ms p50 {At(run.LatenessMs, .5):F2} " +
                $"p90 {At(run.LatenessMs, .9):F2} p99 {At(run.LatenessMs, .99):F2} max {run.LatenessMs[^1]:F2}, over a refresh +2 ms: " +
                $"{run.LatenessMs.Count(ms => ms > refreshMs + 2)}";
            var diagnosis = $"refresh {refreshMs:F3} ms; {Describe("every frame", before)}; {Describe("held", after)}";
            output.WriteLine(diagnosis);

            Assert.True(before.Draws / before.Seconds >= 28.5, diagnosis); // the reference path itself is healthy
            // The same draws: as many a second ...
            Assert.True(after.Draws / after.Seconds >= before.Draws / before.Seconds - 0.5, diagnosis);
            Assert.True(after.Draws / after.Seconds <= 30.5, diagnosis);
            // ... each on the refresh the every-frame path draws on: no later after its due instant (a quarter refresh of noise).
            Assert.True(At(after.LatenessMs, .5) <= At(before.LatenessMs, .5) + refreshMs / 4 + 0.5, diagnosis);
            Assert.True(At(after.LatenessMs, .9) <= At(before.LatenessMs, .9) + refreshMs / 4 + 0.5, diagnosis);
            var lateBefore = before.LatenessMs.Count(ms => ms > refreshMs + 2);
            var lateAfter = after.LatenessMs.Count(ms => ms > refreshMs + 2);
            Assert.True(lateAfter <= lateBefore + Math.Max(2, after.Draws / 50), diagnosis); // not ED-237's 16-31 ms idle wake-up
            // ... with far fewer frame callbacks where the display is faster than about two draws' worth.
            if (refreshMs < 1000.0 / 100)
                Assert.True(after.Answered / after.Seconds <= 2.7 * 30, diagnosis);
        }, factType: typeof(FrameClockWakeNativeTests));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-7")]
    public void The_production_host_answers_for_real_windows_never_shown_minimised_and_cloaked_by_the_app()
    {
        native.Run(_ =>
        {
            // Program.cs builds the process's source on a window that is never shown.
            var (unshownIsWin32, period) = native.OnUi(() =>
            {
                var unshown = new AvaloniaWindow();
                var source = new AvaloniaFrameSource(unshown);
                return (Win32FrameHost.Shared.IsWin32(unshown), source.WakePeriod);
            });
            Assert.True(unshownIsWin32);
            Assert.NotNull(period); // DWM's period with the high-resolution timer: held back from the first request

            var (window, hwnd) = native.OnUi(() =>
            {
                var shown = Shown();
                return (shown, Hwnd(shown));
            });
            N.Until(() => N.IsWindowVisible(hwnd), "the test window never showed");
            Assert.False(native.OnUi(() => Win32FrameHost.Hidden(hwnd)));

            native.OnUi(() => window.WindowState = WindowState.Minimized);
            N.Until(() => N.IsIconic(hwnd), "the test window never minimised");
            Assert.True(native.OnUi(() => Win32FrameHost.Hidden(hwnd)));
            native.OnUi(() => window.WindowState = WindowState.Normal);
            N.Until(() => !N.IsIconic(hwnd), "the test window never came back");
            Assert.False(native.OnUi(() => Win32FrameHost.Hidden(hwnd)));

            // The app's own cloak (as WindowManager.SettingsCloak): DWM_CLOAKED_APP, counted as seen; its events reach the watch.
            var calls = 0;
            var host = new Win32FrameHost();
            native.OnUi(() => host.WatchCloaking(() => Interlocked.Increment(ref calls)));
            Assert.True(native.OnUi(() => host.WatchingCloaks));
            native.OnUi(() => Cloak(hwnd, true));
            try
            {
                N.Until(() => N.DwmGetWindowAttribute(hwnd, DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked == 0x1, "the window never reported the app's cloak");
                Assert.False(native.OnUi(() => Win32FrameHost.Hidden(hwnd)));
                N.Until(() => Volatile.Read(ref calls) >= 1, "EVENT_OBJECT_CLOAKED never reached the watch");
            }
            finally
            {
                native.OnUi(() => Cloak(hwnd, false));
            }
            N.Until(() => Volatile.Read(ref calls) >= 2, "EVENT_OBJECT_UNCLOAKED never reached the watch");
            native.OnUi(() => window.Close());
        }, factType: typeof(FrameClockWakeNativeTests));
    }

    private static void Cloak(nint window, bool on)
    {
        var value = on ? 1 : 0;
        N.Check(DwmSetWindowAttribute(window, DwmwaCloak, ref value, sizeof(int)) == 0, "DwmSetWindowAttribute(DWMWA_CLOAK)");
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
