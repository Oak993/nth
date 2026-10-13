using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.App.Port.MultiAccount.Capsule;
using Marrow.App.Port.MultiAccount.Link;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.MultiAccount.Capsule;
using Marrow.Testing.MultiAccountSettings;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer.MultiAccount;

// ED-NEW-perf-idle-3: a capsule style that moves (Orbit, Liquid fill, Aurora rim, a standby mark, a handshake), the linked-account bridge and
// the plain outline of a linked account each asked the frame clock for a frame at every display refresh, for as long as they were
// on screen, minimised or idle. The capsule is a draw loop at 60 a second now (held back to the next slot, parked while no window
// of the app can be seen), the bridge pulses on the compositor with no frame, and the outline repaints with the capsule it reads.
[Collection("Headless")]
[Trait("Rule", "R-TEST-4")]
public sealed class AccountCapsuleLoopTests(HeadlessSession headless)
{
    private const int Refresh = 240;

    private static CapsuleContent Pill()
    {
        var steps = Enumerable.Range(0, 3).Select(i => new CapsuleStep("S" + (i + 1), 0x808080u + (uint)i, false)).ToArray();
        var view = new CapsuleView(steps, 0, null, null, false, .3, 0.1, "Deep work");
        return new CapsuleContent("S1", "Account", .3, false, "Pill Deep work, step 1 of 3: Account. Open accounts and pills", view);
    }

    private static CapsuleContent Plain(double use) =>
        new("M2", "Claude Max 2", use, CapsuleLayout.IsHot(use), "Account: Claude Max 2, " + (int)Math.Round(use * 100) + "% of its 5-hour limit used. Change account", null);

    private void Run(Func<Control> make, Action<MultiAccountHarness> check, bool reduceMotion = false) =>
        MultiAccountHarness.Run(headless, new AccountsRig(), make, check, motion: true, reduceMotion: reduceMotion);

    [Fact]
    public void A_moving_capsule_draws_sixty_a_second_asks_for_fewer_frames_than_the_display_makes_and_parks_while_unseen()
    {
        AccountCapsule? capsule = null;
        Run(() => capsule = new AccountCapsule("5") { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Margin = new Thickness(20) }, h =>
        {
            h.Frames.WakePeriod = TimeSpan.FromSeconds(1.0 / Refresh);
            capsule!.Apply(Pill(), animate: false);
            h.Settle();
            Assert.True(capsule.IsLooping);
            var repaints = 0;
            capsule.Repainted += () => repaints++;
            for (var i = 0; i < 60; i++) h.Step(1000.0 / Refresh);
            var delivered = h.Frames.Delivered;
            repaints = 0;
            var phase = capsule.Phase;
            for (var i = 0; i < Refresh; i++) h.Step(1000.0 / Refresh);
            Assert.InRange(repaints, 59, 66); // the 60 Hz cap, unchanged
            Assert.True(h.Frames.Delivered - delivered <= 140, $"frames delivered to the capsule in a simulated second: {h.Frames.Delivered - delivered} (240 before)");
            Assert.InRange(capsule.Phase - phase, .95, 1.05); // and the orbit has moved on by the second

            // no window of the app can be seen: nothing is asked for, and nothing is drawn, for as long as that lasts
            h.Frames.Unseen = true;
            h.Step(1000.0 / Refresh);
            delivered = h.Frames.Delivered;
            repaints = 0;
            for (var i = 0; i < Refresh * 10; i++) h.Step(1000.0 / Refresh);
            Assert.Equal(delivered, h.Frames.Delivered);
            Assert.Equal(0, repaints);

            // seen again: the first frame draws where the clock has got to (the orbit is a function of the clock)
            h.Frames.Unseen = false;
            for (var i = 0; i < 8; i++) h.Step(1000.0 / Refresh);
            Assert.True(repaints > 0);
            Assert.InRange(capsule.Phase - phase, 10.9, 11.2);
        });
    }

    [Fact]
    public void A_capsule_that_stops_moving_leaves_no_frame_request_and_one_that_never_moves_runs_no_loop()
    {
        AccountCapsule? capsule = null;
        Run(() => capsule = new AccountCapsule("1") { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Margin = new Thickness(20) }, h =>
        {
            capsule!.Apply(Pill(), animate: false);
            h.Settle();
            Assert.False(capsule.IsLooping);
            for (var i = 0; i < 30; i++) h.Step(50);
            Assert.Equal(0, h.Frames.Pending);
        });
    }

    [Fact]
    public void The_plain_outline_of_a_linked_account_repaints_with_the_capsule_it_reads_and_asks_for_no_frame()
    {
        AccountCapsule? capsule = null;
        LinkedCapsulePaint? outline = null;
        Run(() =>
        {
            capsule = new AccountCapsule("plain") { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Margin = new Thickness(20) };
            outline = new LinkedCapsulePaint(capsule) { Width = 60, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom, Margin = new Thickness(20) };
            return new Grid { Children = { capsule, outline } };
        }, h =>
        {
            capsule!.Apply(Plain(.3), animate: false);
            h.Settle();
            for (var i = 0; i < 4; i++) { AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
            var renders = outline!.Renders;
            Assert.True(renders > 0);
            // two simulated seconds with nothing changing: no frame is asked for and the outline is not drawn again
            for (var i = 0; i < 120; i++)
            {
                h.Step(1000.0 / 60);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
            }
            Assert.True(h.Frames.Pending == 0, "frames pending: " + h.Frames.Pending);
            Assert.True(renders == outline.Renders, "renders went from " + renders + " to " + outline.Renders);
            // the capsule it reads changes, so it is drawn again with the new usage
            capsule.Apply(Plain(.8), animate: false);
            for (var i = 0; i < 4; i++) { AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
            Assert.True(outline.Renders > renders);
        });
    }

    [Fact]
    public void The_bridge_pulses_on_the_compositor_with_no_frame_and_stands_still_under_Reduce_Motion()
    {
        var loop = new RecordingOpacityLoop();
        LinkBridge? bridge = null;
        IDisposable? driver = null;
        Run(() =>
        {
            driver = OpacityLoops.Driver.Override(loop); // before the bridge joins the window
            return bridge = new LinkBridge(Avalonia.Media.Colors.Orange, Avalonia.Media.Colors.Teal);
        }, h =>
        {
            var start = Assert.Single(loop.Starts);
            Assert.Equal(TimeSpan.FromSeconds(2.4), start.Duration);
            Assert.NotNull(start.Ease);
            Assert.Equal([(0f, .25f), (.5f, .9f), (1f, .25f)], start.Keys.Select(key => (key.At, key.Value)));
            Assert.True(bridge!.IsPulsing);
            for (var i = 0; i < 100; i++) h.Step(50);
            Assert.Equal(0, h.Frames.Pending);
            Assert.Equal(0, h.Frames.Delivered);
        });
        driver?.Dispose();
    }

    [Fact]
    public void The_bridge_does_not_pulse_under_Reduce_Motion()
    {
        var loop = new RecordingOpacityLoop();
        LinkBridge? bridge = null;
        IDisposable? driver = null;
        Run(() =>
        {
            driver = OpacityLoops.Driver.Override(loop);
            return bridge = new LinkBridge(Avalonia.Media.Colors.Orange, Avalonia.Media.Colors.Teal);
        }, h =>
        {
            Assert.Empty(loop.Starts);
            Assert.False(bridge!.IsPulsing);
        }, reduceMotion: true);
        driver?.Dispose();
    }
}
