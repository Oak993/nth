using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;
using G = Marrow.App.Tests.Window.Native.WindowManagerNativePlacementGeometry;
using T = Marrow.App.Tests.Window.Native.WindowManagerNativeMonitorTopology;
using HostWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Window.Native;

// Called inside the existing authenticated native child Fact.
// Uses the production manager and factory HWNDs; moves are native setup operations, not claims
// of mouse dragging between displays. No Screens/scale override, private centering call or fake monitor.
internal static class WindowManagerNativePlacementExercise
{
    internal static void Run(WindowManagerNativeSubject subject)
    {
        var session = subject.Session;
        var run = Environment.GetEnvironmentVariable(WindowManagerNativeChildProtocol.DirectoryVariable)
            ?? throw new InvalidOperationException("Native placement must run inside the existing child protocol.");
        var topology = session.OnUi(T.Read);
        WindowManagerNativeChildProtocol.Write(run, "placement-topology-start.json", topology);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Native topology: desktop monitors={topology.Monitors.Length}, physical connector targets={topology.PhysicalTargetCount}, independent physical desktop regions={topology.PhysicalDesktopCount}, unclassified targets={topology.UnclassifiedTargetCount}."));
        T.AssertUsable(topology);
        var physical = topology.Monitors.Where(item => item.PhysicalTargetCount > 0).ToArray();
        var primary = Assert.Single(topology.Monitors, item => item.Primary);
        var completedMonitors = new List<string>();
        var visitedDpi = new HashSet<uint>();
        var results = new List<NativePlacementCase>();
        var finished = false;
        Exception? failure = null;
        try
        {
            session.OnUi(() =>
            {
                subject.Main.WindowState = WindowState.Normal;
                subject.Settings.WindowState = WindowState.Normal;
                subject.Main.Topmost = false;
                subject.Settings.Topmost = false;
                // Fixture size only: divisible by four at the usual 100/125/150/175/200% DPIs.
                // Settings keeps the source's 780x580 without any fixture rewrite of its dimensions.
                subject.Main.Width = 880;
                subject.Main.Height = 576;
                subject.Main.Show();
                subject.Main.UpdateLayout();
                subject.Settings.UpdateLayout();
            });
            foreach (var monitor in physical)
            {
                AssertTopologyStillMatches(session, topology, run, results.Count);
                // Seed the main HWND onto this actual monitor. A DPI-change message can change its
                // physical size, so all later setup points come from a fresh settled native frame.
                MoveAndSettle(subject, subject.Main, subject.MainProbe,
                    new(monitor.Work.Left + 32, monitor.Work.Top + 32));
                var main = Settled(subject, subject.Main, subject.MainProbe);
                Assert.Equal(monitor.Handle, main.Monitor);
                visitedDpi.Add(main.Dpi);
                if (main.Frame.Width + 64 >= monitor.Work.Width || main.Frame.Height + 64 >= monitor.Work.Height)
                    throw new InvalidOperationException("NATIVE_PRECONDITION: this real monitor lacks working area for the fixture's unclamped and edge cases: " + monitor.Device);
                var interior = new NativePlacementPoint(
                    monitor.Work.Left + (monitor.Work.Width - main.Frame.Width) / 3,
                    monitor.Work.Top + (monitor.Work.Height - main.Frame.Height) / 3);
                MoveAndSettle(subject, subject.Main, subject.MainProbe, interior);

                // First request on each monitor leaves Settings on whichever native monitor/DPI it
                // previously occupied. No preliminary ShowSettings or manual Settings relocation
                // is allowed to hide a first-request cross-monitor/DPI failure.
                RecordCase("arrival-hidden", hidden: true, moveSettings: false);

                var centers = new[]
                {
                    ("interior-visible", interior, false),
                    ("one-pixel-shift-hidden", new NativePlacementPoint(interior.X + 1, interior.Y + 1), true),
                    ("left-workarea-visible", new NativePlacementPoint(monitor.Work.Left - main.Frame.Width / 2 + 32, interior.Y), false),
                    ("right-workarea-hidden", new NativePlacementPoint(monitor.Work.Right - main.Frame.Width / 2 - 32, interior.Y), true),
                    ("top-workarea-visible", new NativePlacementPoint(interior.X, monitor.Work.Top - main.Frame.Height / 2 + 32), false),
                    ("bottom-workarea-hidden", new NativePlacementPoint(interior.X, monitor.Work.Bottom - main.Frame.Height / 2 - 32), true),
                };
                foreach (var (name, position, hidden) in centers)
                {
                    MoveAndSettle(subject, subject.Main, subject.MainProbe, position);
                    RecordCase(name, hidden, moveSettings: true);
                }
                // A native one-pixel frame-size change exposes half-point centers at 100% and
                // additional rounding residues at other DPIs. Observe what the current physical
                // scale actually exercised; no emulated scale is credited as hardware evidence.
                MoveAndSettle(subject, subject.Main, subject.MainProbe, interior);
                session.OnUi(() => G.ResizeMain(subject.MainProbe.Handle, main.Frame.Width + 1, main.Frame.Height + 1));
                _ = Settled(subject, subject.Main, subject.MainProbe);
                RecordCase("odd-physical-frame-visible", hidden: false, moveSettings: true);
                MoveAndSettle(subject, subject.Main, subject.MainProbe, new(interior.X + 1, interior.Y + 1));
                RecordCase("odd-physical-frame-shift-hidden", hidden: true, moveSettings: true);
                session.OnUi(() => G.ResizeMain(subject.MainProbe.Handle, main.Frame.Width, main.Frame.Height));
                _ = Settled(subject, subject.Main, subject.MainProbe);
                var monitorCases = results.Where(item => string.Equals(item.MonitorDevice, monitor.Device, StringComparison.Ordinal)).ToArray();
                Assert.Contains(monitorCases, item => item.Expected.XClampExercised);
                Assert.Contains(monitorCases, item => item.Expected.YClampExercised);
                if (monitor.Work != monitor.Bounds)
                    Assert.Contains(monitorCases, item => item.DistinguishesBounds);
                completedMonitors.Add(monitor.Device);

                void RecordCase(string name, bool hidden, bool moveSettings)
                {
                    // These two paths prove both re-presenting a hidden window and repositioning
                    // an already visible one. The only ShowSettings invocation is the measured act.
                    if (moveSettings)
                    {
                        session.OnUi(() => subject.Settings.Show());
                        MoveAndSettle(subject, subject.Settings, subject.SettingsProbe,
                            new(monitor.Work.Left + 16, monitor.Work.Top + 16));
                    }
                    if (hidden) session.OnUi(subject.Settings.Hide);
                    var beforeMain = Settled(subject, subject.Main, subject.MainProbe);
                    var beforeSettings = Settled(subject, subject.Settings, subject.SettingsProbe);
                    Assert.Equal(monitor.Handle, beforeMain.Monitor);
                    Assert.True(beforeMain.Visible);
                    Assert.False(beforeMain.Minimized);
                    Assert.Equal(!hidden, beforeSettings.Visible);
                    Assert.False(beforeSettings.Minimized);
                    AssertTopologyStillMatches(session, topology, run, results.Count);
                    var expected = G.Expected(beforeMain, beforeSettings, primary.Bounds, monitor.Work);
                    var usingBounds = G.Expected(beforeMain, beforeSettings, primary.Bounds, monitor.Bounds);
                    var usingEven = G.Expected(beforeMain, beforeSettings, primary.Bounds, monitor.Work, MidpointRounding.ToEven);
                    var beforeManaged = session.OnUi(() => DescribeManaged(subject.Main, subject.Settings));
                    session.OnUi(() => subject.Manager.ShowSettings());
                    var after = Settled(subject, subject.Settings, subject.SettingsProbe);
                    var afterMain = Settled(subject, subject.Main, subject.MainProbe);
                    var afterManaged = session.OnUi(() => DescribeManaged(subject.Main, subject.Settings));
                    var dwm = session.OnUi(() => G.ReadVisibleBounds(subject.SettingsProbe.Handle));
                    var reading = new NativePlacementCase(monitor.Device, name, hidden,
                        beforeMain, beforeSettings, expected, after, afterMain,
                        expected.Origin != usingBounds.Origin, expected.Origin != usingEven.Origin,
                        N.GetForegroundWindow().ToInt64(), dwm, beforeManaged, afterManaged);
                    results.Add(reading);
                    WindowManagerNativeChildProtocol.Write(run,
                        string.Create(CultureInfo.InvariantCulture, $"placement-case-{results.Count:D3}.json"), reading);
                    // Record evidence before any assertion. Never retry the action until an expected
                    // position appears, and never accept an arbitrary pixel tolerance to mask a miss.
                    Assert.Equal(beforeSettings.Handle, after.Handle);
                    Assert.Equal(beforeMain, afterMain);
                    Assert.True(after.Visible);
                    Assert.False(after.Minimized);
                    Assert.Equal(monitor.Handle, after.Monitor);
                    Assert.Equal(beforeMain.Dpi, after.Dpi);
                    Assert.Equal(expected.Origin, after.Origin);
                    Assert.Equal(780, session.OnUi(() => subject.Settings.Width));
                    Assert.Equal(580, session.OnUi(() => subject.Settings.Height));
                    // Once both HWNDs share a DPI, the unchanged Settings frame must fit rcWork.
                    // Cross-DPI arrival uses pre-call dimensions, so that transition is logged
                    // separately and is not silently reinterpreted using its post-call frame.
                    if (beforeSettings.Dpi == after.Dpi && monitor.Work.Width >= after.Frame.Width && monitor.Work.Height >= after.Frame.Height)
                        Assert.True(monitor.Work.Contains(after.Frame), "Settings frame overlaps the native reserved working area.");
                }
            }
            AssertTopologyStillMatches(session, topology, run, results.Count);
            Assert.Equal(physical.Select(item => item.Device).ToArray(), completedMonitors.ToArray());
            Assert.Equal(physical.Length * 9, results.Count);
            Assert.All(results, item => Assert.Equal(item.Expected.Origin, item.AfterSettings.Origin));
            Assert.Contains(results, item => item.Expected.XClampExercised);
            Assert.Contains(results, item => item.Expected.YClampExercised);
            // A taskbar may be auto-hidden, or all four rcWork edges may equal rcMonitor. Such a
            // run still checks the true topology; it cannot claim an observed workarea discriminator.
            finished = true;
        }
        catch (Exception error) { failure = error; throw; }
        finally
        {
            WindowManagerNativeChildProtocol.Write(run, "placement-coverage.json", new
            {
                ActualDesktopMonitorCount = topology.Monitors.Length,
                PhysicalConnectorTargetCount = topology.PhysicalTargetCount,
                IndependentPhysicalDesktopCount = physical.Length,
                UnclassifiedTargets = topology.UnclassifiedTargetCount,
                CompletedPhysicalMonitors = completedMonitors.ToArray(),
                ObservedMainDpi = visitedDpi.Order().ToArray(),
                RecordedCaseCount = results.Count,
                AvailableMonitorPlacementPassed = finished,
                // STOP TWO-PHYSICAL-MONITORS: the saved machine has one physical region.
                // A passing available-monitor run must not be promoted to WIN-011's two-monitor clause.
                TwoMonitorPlacementClauseExercised = finished && completedMonitors.Count >= 2,
                MixedDpiTransitionExercised = results.Any(item => item.BeforeMain.Dpi != item.BeforeSettings.Dpi),
                WorkareaVsBoundsDiscriminatorCases = results.Count(item => item.DistinguishesBounds),
                AwayFromZeroVsEvenDiscriminatorCases = results.Count(item => item.DistinguishesEvenRounding),
                ActivationAndFirstPaintProven = false,
                ActualSettingsViewProven = false,
                Error = failure?.ToString(),
                Limitation = physical.Length < 2
                    ? "Only one independently movable physical desktop region is available. PR-WIN-011's two-monitor clause remains UNVERIFIED."
                    : "This Fact covers the reported physical topology only; review the case evidence and all other requirement clauses separately.",
            });
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"Native placement: recorded cases={results.Count}; completed physical desktop regions={completedMonitors.Count}/{physical.Length}; two-monitor clause exercised={finished && completedMonitors.Count >= 2}; artifacts={run}"));
        }
    }

    private static void MoveAndSettle(WindowManagerNativeSubject subject, HostWindow window,
        WindowManagerNativeProbe probe, NativePlacementPoint origin)
    {
        subject.Session.OnUi(() => G.Move(probe.Handle, origin));
        _ = Settled(subject, window, probe);
    }

    private static NativePlacementSnapshot Settled(WindowManagerNativeSubject subject, HostWindow window, WindowManagerNativeProbe probe)
    {
        // Wait for unchanged observations across native queue fences, never for oracle agreement.
        // DPI/size/window messages remain on the real STA. Only setup liveness uses a wall clock.
        var clock = Stopwatch.StartNew();
        NativePlacementSnapshot? previous = null;
        var equal = 0;
        do
        {
            probe.Fence();
            var reading = subject.Session.OnUi(() => { window.UpdateLayout(); return G.Read(probe.Handle); });
            equal = reading == previous ? equal + 1 : 0;
            if (equal >= 2) return reading;
            previous = reading;
        }
        while (clock.Elapsed < TimeSpan.FromSeconds(4));
        throw new InvalidOperationException("NATIVE_PRECONDITION: owned frame/DPI did not settle across native fences.");
    }

    private static void AssertTopologyStillMatches(WindowManagerNativeSession session,
        NativePlacementTopology original, string run, int completedCases)
    {
        var current = session.OnUi(T.Read);
        try { T.AssertUnchanged(original, current); }
        catch (Exception error)
        {
            WindowManagerNativeChildProtocol.Write(run,
                string.Create(CultureInfo.InvariantCulture, $"placement-topology-change-{completedCases:D3}.json"), current);
            throw new InvalidOperationException("NATIVE_PRECONDITION: actual monitor topology/workarea changed during the placement proof.", error);
        }
    }

    // Supplemental diagnostics only. Neither these values nor WindowManager's private frame or
    // centering helpers are inputs to G.Expected, which uses native snapshots and rcWork exclusively.
    private static string DescribeManaged(HostWindow main, HostWindow settings) => string.Create(CultureInfo.InvariantCulture,
        $"Main: position={main.Position}; requested={main.Width}x{main.Height}; client={main.ClientSize}; frame={main.FrameSize}; desktopScale={main.DesktopScaling}. Settings: position={settings.Position}; requested={settings.Width}x{settings.Height}; client={settings.ClientSize}; frame={settings.FrameSize}; desktopScale={settings.DesktopScaling}.");
}

internal sealed record NativePlacementCase(string MonitorDevice, string Name, bool HiddenBefore,
    NativePlacementSnapshot BeforeMain, NativePlacementSnapshot BeforeSettings, NativePlacementExpectation Expected,
    NativePlacementSnapshot AfterSettings, NativePlacementSnapshot AfterMain, bool DistinguishesBounds,
    bool DistinguishesEvenRounding, long ForegroundAfter, NativePlacementDwmReading Dwm,
    string ManagedBefore, string ManagedAfter);
