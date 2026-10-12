using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Marrow.Core.Support;
using Marrow.Setup.Flow;
using Marrow.Setup.Platform;
using Marrow.Setup.Screens;
using Marrow.Setup.Tests.Fixtures;
using Marrow.Setup.Text;
using Marrow.Setup.Ui;

namespace Marrow.Setup.Tests.Ui;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3b, "Test", unit U2 (ED-NEW-inst-u2-1). Headless frame capture on a
// fixed 60 fps clock: ten fast toggles of the Uninstall switch (60 ms apart, 120 ms apart and at irregular times, at 100 %
// and 225 % text size, in the smallest window) never move anything more than 3 DIP in a 16.7 ms frame, never move Cancel
// or Uninstall, keep the warning between the row and the buttons while it shows, and end at rest; every reveal of the
// plan's table and its reverse mid-flight passes the same checks with no element appearing or leaving by more than 35 %
// opacity in a frame; Calm moves nothing; the live region speaks once per appearance; the switch's state changes in the
// tap's frame.
[Collection("SetupHeadless")]
public sealed class RevealFrameTests(SetupHeadlessSession headless)
{
    private static readonly int[] Irregular = [0, 90, 150, 330, 370, 520, 600, 610, 800, 1000, 1015];

    [Theory]
    [InlineData(60, 1.0)]
    [InlineData(120, 1.0)]
    [InlineData(0, 1.0)]
    [InlineData(60, 2.25)]
    [InlineData(120, 2.25)]
    [InlineData(0, 2.25)]
    [Trait("Decision", "OD-0072")]
    public void Ten_fast_toggles_never_jump_never_move_the_buttons_and_end_at_rest(int gapMs, double textScale)
    {
        headless.OnUiThread(() =>
        {
            var (rig, screen) = Uninstall(SystemLook.Default with { TextScale = textScale });
            var times = gapMs > 0 ? Enumerable.Range(0, 10).Select(i => i * gapMs).ToArray() : Irregular;
            var probe = Probe(rig, screen);
            var toggled = 0;
            var clock = 0.0;
            var row = screen.Group;
            while (toggled < times.Length || clock < times[^1] / 1000.0 + 2)
            {
                while (toggled < times.Length && times[toggled] / 1000.0 <= clock + 1e-9)
                {
                    var before = screen.Switch.IsOn;
                    screen.Switch.Toggle();
                    Assert.NotEqual(before, screen.Switch.IsOn);
                    Assert.Equal(screen.Switch.IsOn ? ToggleState.On : ToggleState.Off, Toggle(screen.Switch).ToggleState);
                    toggled++;
                }
                probe.Step();
                clock += FrameProbe.Frame;
                var warning = probe.Of(screen.Warning);
                if (warning.Opacity > .01)
                {
                    Assert.True(warning.Box.Top >= probe.Of(row).Box.Bottom - .5, "the warning rose above the row");
                    Assert.True(warning.Box.Bottom <= probe.Of(screen.Cancel).Box.Top + .5, "the warning reached the buttons");
                }
            }
            Assert.False(screen.Reveal.IsBusy);
            Assert.Equal(screen.Switch.IsOn, screen.Warning.IsVisible);
            Assert.Equal(screen.Switch.IsOn ? 1 : 0, probe.Of(screen.Warning).Opacity, 3);
            Assert.Equal(times.Length % 2 == 1, screen.Switch.IsOn);
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Calm_moves_nothing_and_the_warning_comes_and_goes_within_0_2_s()
    {
        headless.OnUiThread(() =>
        {
            var (rig, screen) = Uninstall(WindowRig.Calm);
            var probe = Probe(rig, screen);
            var start = Snapshot(probe, screen);

            // the canvas's Calm fade (a 0.15 s spring) is full to the eye at 0.2 s (at least 99 %) and exactly full by 0.3 s,
            // and it never turns back on the way
            screen.Switch.Toggle();
            var last = probe.Of(screen.Warning).Opacity;
            probe.Run(0.2, _ =>
            {
                Assert.True(probe.Of(screen.Warning).Opacity >= last - 1e-9, "the warning dimmed while arriving");
                last = probe.Of(screen.Warning).Opacity;
            });
            Assert.True(probe.Of(screen.Warning).Opacity >= .99, $"the warning is at {probe.Of(screen.Warning).Opacity:F3} after 0.2 s");
            probe.Run(0.1);
            Assert.Equal(1, probe.Of(screen.Warning).Opacity);
            Assert.Equal(start, Snapshot(probe, screen));
            screen.Switch.Toggle();
            last = probe.Of(screen.Warning).Opacity;
            probe.Run(0.2, _ =>
            {
                Assert.True(probe.Of(screen.Warning).Opacity <= last + 1e-9, "the warning brightened while leaving");
                last = probe.Of(screen.Warning).Opacity;
            });
            Assert.True(probe.Of(screen.Warning).Opacity <= .01, $"the warning is at {probe.Of(screen.Warning).Opacity:F3} after 0.2 s");
            probe.Run(0.1);
            Assert.Equal(0, probe.Of(screen.Warning).Opacity);
            Assert.Equal(start, Snapshot(probe, screen));
            for (var i = 0; i < 10; i++)
            {
                screen.Switch.Toggle();
                probe.Run(0.05);
                Assert.Equal(start, Snapshot(probe, screen));
            }
            Assert.Equal(0, probe.LargestStep);
            rig.Window.CloseNow();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Decision", "OD-0072")]
    public void The_warning_live_region_speaks_once_per_appearance(bool calm)
    {
        headless.OnUiThread(() =>
        {
            var (rig, screen) = Uninstall(calm ? WindowRig.Calm : SystemLook.Default);
            var spoken = new List<Control>();
            screen.LiveRegionChanged += spoken.Add;

            screen.Switch.Toggle();
            rig.Run(0.6);
            Assert.Single(spoken);
            Assert.Same(screen.WarningRegion, spoken[0]);
            screen.Switch.Toggle();
            rig.Run(0.6);
            Assert.Single(spoken);
            screen.Switch.Toggle();
            rig.Run(0.6);
            Assert.Equal(2, spoken.Count);
            Assert.Equal(Avalonia.Automation.AutomationLiveSetting.Polite, Avalonia.Automation.AutomationProperties.GetLiveSetting(screen.WarningRegion));
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Removing_and_Removed_reveal_one_commit_at_a_time_and_Close_arrives_as_the_primary()
    {
        headless.OnUiThread(() =>
        {
            var (rig, screen) = Uninstall(SystemLook.Default);
            // Remove leaves and Close comes back centred, so only the drawn steps and fades are checked here
            var probe = new FrameProbe(rig, screen.View, [screen.MarkWrap, screen.TitleBlock, screen.SubtitleBlock, screen.Group, screen.Warning, screen.Bar, screen.Hold!.Child!], [screen.Cancel, screen.Remove]) { ButtonsFixed = false };

            screen.ShowRemoving();
            probe.Run(0.8);
            Assert.False(screen.Group.IsVisible);
            Assert.True(screen.Bar.IsVisible);
            Assert.False(screen.Cancel.IsVisible);
            Assert.True(rig.Window.Guard.IsPaused);
            Assert.True(screen.Hold!.IsVisible);
            Assert.Equal(1, screen.Hold.LineOpacity, 3);
            screen.ShowRemoved();
            probe.Run(1.2);
            Assert.False(screen.Bar.IsVisible);
            Assert.Equal("Close", screen.Cancel.Label);
            Assert.Equal(ButtonKind.Primary, screen.Cancel.Kind);
            Assert.Equal("nth was removed", screen.TitleBlock.Text);
            Assert.Equal(UninstallScreen.RemovedDim, screen.MarkPose.Opacity, 3);
            Assert.Equal(UninstallScreen.RemovedScale, screen.MarkPose.Scale, 3);
            Assert.False(rig.Window.Guard.IsPaused);
            Assert.False(screen.Hold.IsVisible); // Close works again, so the paused line has left
            rig.Window.CloseNow();
        });
    }

    public static TheoryData<string> Reveals => new() { "welcome-line", "options-path", "installing-slow", "ready-note", "update-busy", "update-wait", "current-case", "problem-log", "problem-reason" };

    [Theory]
    [MemberData(nameof(Reveals))]
    [Trait("Decision", "OD-0072")]
    public void Every_reveal_and_its_reverse_mid_flight_never_jumps_or_pops(string reveal)
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var (screen, change, back) = Arrange(rig, reveal);
            var probe = new FrameProbe(rig, screen.View, Elements(screen), Buttons(screen)) { ButtonsFixed = !reveal.StartsWith("update", StringComparison.Ordinal) };
            foreach (var mark in screen.Column.Children.OfType<SetupMark>()) probe.Unchecked.Add(mark);

            change();
            probe.Run(0.15);
            back();
            probe.Run(1.2);
            change();
            probe.Run(1.4);
            Assert.False(screen.Reveal.IsBusy);
            rig.Window.CloseNow();
        });
    }

    private static (CanvasScreen Screen, Action Change, Action Back) Arrange(WindowRig rig, string reveal)
    {
        SetupSession session;
        switch (reveal)
        {
            case "welcome-line":
            {
                session = Shown(rig, SetupScreen.Welcome);
                var welcome = (WelcomeScreen)session.Current!;
                return (welcome, () => session.SetDesktopShortcut(false), () => session.SetDesktopShortcut(true));
            }
            case "options-path":
            {
                session = Shown(rig, SetupScreen.Options);
                return ((CanvasScreen)session.Current!, () => session.SetInstallRoot(@"D:\Apps\nth"), () => session.SetInstallRoot(WindowRig.Root));
            }
            case "installing-slow":
            {
                session = Shown(rig, SetupScreen.Installing);
                var installing = (InstallingScreen)session.Current!;
                return (installing, () => installing.ShowSlow(true), () => installing.ShowSlow(false));
            }
            case "ready-note":
            {
                session = rig.Session(SetupScreen.Ready);
                session.SetResult(new InstallResult(DesktopShortcutOutcome.Off), false);
                Show(rig, session);
                return ((CanvasScreen)session.Current!, () => session.SetResult(new InstallResult(DesktopShortcutOutcome.NameTaken), false), () => session.SetResult(new InstallResult(DesktopShortcutOutcome.Created), false));
            }
            case "update-busy":
            {
                session = rig.Session(SetupScreen.Update, installed: "1.0.0");
                session.ReleaseNotes = ["Threads open faster on large projects", "Voice input understands more accents"];
                Show(rig, session);
                var update = (UpdateScreen)session.Current!;
                return (update, () => update.ShowBusy([("Refactor the sign-in module", 4), ("Write the release notes", 1)]), update.ShowAsk);
            }
            case "update-wait":
            {
                session = Shown(rig, SetupScreen.Update, "1.0.0");
                var update = (UpdateScreen)session.Current!;
                return (update, () => update.ShowWaiting(2), update.ShowAsk);
            }
            case "current-case":
            {
                session = Shown(rig, SetupScreen.UpToDate, "1.1.0");
                var current = (CurrentScreen)session.Current!;
                return (current, () => current.SetCase(SetupScreen.NewerInstalled), () => current.SetCase(SetupScreen.UpToDate));
            }
            case "problem-log":
            {
                session = rig.Session(SetupScreen.Problem);
                session.Fail(SetupStrings.ProblemInUse, safe: true, "step: copying · Setup.exe exit 1\n[12:04:31] Velopack Setup 1.2.161");
                Show(rig, session);
                var problem = (ProblemScreen)session.Current!;
                return (problem, problem.DetailsLink.Invoke, problem.DetailsLink.Invoke);
            }
            default:
            {
                session = rig.Session(SetupScreen.Problem);
                session.Fail(SetupStrings.ProblemOther);
                Show(rig, session);
                return ((CanvasScreen)session.Current!, () => session.Fail(SetupStrings.ProblemSpace("C:", 420, 112), safe: false), () => session.Fail(SetupStrings.ProblemOther, safe: true));
            }
        }
    }

    private static SetupSession Shown(WindowRig rig, SetupScreen screen, string? installed = null)
    {
        var session = rig.Session(screen, installed);
        Show(rig, session);
        return session;
    }

    private static void Show(WindowRig rig, SetupSession session)
    {
        session.Start();
        rig.Window.Show();
        rig.Settle();
        rig.Run(2);
    }

    private static (WindowRig Rig, UninstallScreen Screen) Uninstall(SystemLook look)
    {
        var rig = new WindowRig(look);
        var session = rig.Session(SetupScreen.Uninstall, installed: "1.1.0");
        Show(rig, session);
        return (rig, (UninstallScreen)session.Current!);
    }

    private static FrameProbe Probe(WindowRig rig, UninstallScreen screen) =>
        new(rig, screen.View, [screen.MarkWrap, screen.TitleBlock, screen.SubtitleBlock, screen.Group, screen.Warning, screen.Bar], [screen.Cancel, screen.Remove]);

    private static List<Control> Elements(CanvasScreen screen)
    {
        var list = new List<Control>();
        foreach (var child in screen.Column.Children)
        {
            if (child is Grow || ScreenColumn.GetIsGhost(child) || child is ButtonRow) continue;
            list.Add(child);
        }
        return list;
    }

    private static List<Control> Buttons(CanvasScreen screen) =>
        [.. screen.Column.Children.OfType<ButtonRow>().SelectMany(r => r.Children).OfType<Control>()];

    private static List<(double, double, double)> Snapshot(FrameProbe probe, UninstallScreen screen) =>
        [.. new Control[] { screen.MarkWrap, screen.TitleBlock, screen.SubtitleBlock, screen.Group, screen.Cancel, screen.Remove }.Select(c => (probe.Of(c).Box.X, probe.Of(c).Box.Y, probe.Of(c).Box.Height))];

    private static IToggleProvider Toggle(Control control) =>
        ControlAutomationPeer.CreatePeerForElement(control).GetProvider<IToggleProvider>() ?? throw new InvalidOperationException("no toggle pattern");
}
