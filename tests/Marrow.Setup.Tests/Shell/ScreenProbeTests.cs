using Avalonia.Automation;
using Marrow.Setup.Flow;
using Marrow.Setup.Platform;
using Marrow.Setup.Screens;
using Marrow.Setup.Tests.Fixtures;

namespace Marrow.Setup.Tests.Shell;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3b ("the window never jumps"); VELOPACK_CHECK.md findings 11 and 12
// (ED-NEW-inst-fix-7, ED-NEW-inst-fix-8). The window grew in one frame when a taller screen arrived after the first (the body only
// grows), and a longer path in Options pushed the desktop row, the note and Done down in one frame. The body now measures the screens
// the window can reach with the first, unseen, and Options' path change goes through the screen's reveal.
[Collection("SetupHeadless")]
public sealed class ScreenProbeTests(SetupHeadlessSession headless)
{
    [Theory]
    [Trait("Decision", "OD-0072")]
    [InlineData(SetupScreen.Welcome, new[] { SetupScreen.Options, SetupScreen.Installing, SetupScreen.Ready, SetupScreen.Problem })]
    [InlineData(SetupScreen.Update, new[] { SetupScreen.Ready, SetupScreen.Problem })]
    [InlineData(SetupScreen.Uninstall, new[] { SetupScreen.Problem })]
    [InlineData(SetupScreen.UpToDate, new SetupScreen[0])]
    [InlineData(SetupScreen.NewerInstalled, new SetupScreen[0])]
    [InlineData(SetupScreen.Problem, new SetupScreen[0])]
    public void The_screens_a_window_can_reach_follow_the_flows(SetupScreen first, SetupScreen[] reachable) =>
        Assert.Equal(reachable, SetupSession.Reachable(first));

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void The_reachable_screens_are_measured_unseen_and_taken_away_after_one_layout()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var session = rig.Session(SetupScreen.Welcome);
            session.Start();

            var probes = rig.Window.Body.Probes.ToArray();
            Assert.Equal(SetupSession.Reachable(SetupScreen.Welcome).Select(screen => session.Get(screen).View), probes);
            foreach (var probe in probes)
            {
                Assert.Equal(0, probe.Opacity);
                Assert.False(probe.IsHitTestVisible);
                Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(probe));
                Assert.Contains(probe, rig.Window.Body.Children);
            }

            rig.Window.Show();
            rig.Settle();

            Assert.Empty(rig.Window.Body.Probes);
            Assert.Equal([session.Current!.View], rig.Window.Body.Children);
            foreach (var probe in probes)
            {
                Assert.Null(probe.Parent);
                Assert.Equal(AccessibilityView.Default, AutomationProperties.GetAccessibilityView(probe));
            }
            rig.Window.CloseNow();
        });
    }

    [Theory]
    [Trait("Decision", "OD-0072")]
    [InlineData(1.0)]
    [InlineData(2.25)]
    public void The_window_opens_at_the_tallest_screen_it_can_reach_so_none_grows_it_later(double textScale)
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig(SystemLook.Default with { TextScale = textScale });
            var session = rig.Session(SetupScreen.Welcome);
            session.Start();
            rig.Window.Show();
            rig.Settle();
            var height = rig.Window.Bounds.Height;
            var tallest = 0.0;

            foreach (var screen in new[] { SetupScreen.Options, SetupScreen.Welcome, SetupScreen.Installing, SetupScreen.Ready })
            {
                session.Go(screen);
                rig.Run(1);
                tallest = Math.Max(tallest, session.Current!.View.DesiredSize.Height);
                Assert.Equal(height, rig.Window.Bounds.Height);
            }
            Assert.True(rig.Window.Body.Bounds.Height >= tallest - .5, $"the body ({rig.Window.Body.Bounds.Height}) holds the tallest screen ({tallest})");
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void A_folder_whose_path_takes_two_lines_opens_the_window_at_Options_height_instead_of_growing_it_when_Options_opens()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig(); // the rig's long name: Options' path takes two lines, so Options is the tallest screen
            var session = rig.Session(SetupScreen.Welcome);
            session.Start();
            rig.Window.Show();
            rig.Settle();
            var opened = rig.Window.ClientSize.Height;

            session.Go(SetupScreen.Options);
            rig.Run(1);
            var options = Assert.IsType<OptionsScreen>(session.Current);

            Assert.True(opened > 468, $"Options needs more than the canvas's 468 here ({opened})");
            Assert.True(options.PathBox.Bounds.Height > 30, "the path takes two lines");
            Assert.Equal(opened, rig.Window.ClientSize.Height);
            Assert.Equal(rig.Window.Body.Bounds.Height, options.View.Bounds.Height, 3);
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void A_screen_shown_while_it_is_still_a_probe_becomes_the_screen_once_and_is_seen()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var session = rig.Session(SetupScreen.Welcome);
            session.Start();
            var options = session.Get(SetupScreen.Options).View;
            Assert.Contains(options, rig.Window.Body.Probes);

            session.Go(SetupScreen.Options);
            rig.Window.Show();
            rig.Settle();
            rig.Run(1);

            Assert.DoesNotContain(options, rig.Window.Body.Probes);
            Assert.Single(rig.Window.Body.Children, child => ReferenceEquals(child, options));
            Assert.True(options.IsHitTestVisible);
            Assert.Equal(AccessibilityView.Default, AutomationProperties.GetAccessibilityView(options));
            Assert.Equal(1, rig.Window.Body.Pose(options).Opacity, 3);
            Assert.Equal(1, options.Opacity, 3);
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void A_text_size_change_measures_the_reachable_screens_again()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var session = rig.Session(SetupScreen.Update, installed: "1.0.0");
            session.Start();
            rig.Window.Show();
            rig.Settle();
            var height = rig.Window.Bounds.Height;

            rig.Window.Body.ResetHeight();

            Assert.Equal([session.Get(SetupScreen.Ready).View, session.Get(SetupScreen.Problem).View], rig.Window.Body.Probes);
            rig.Settle();
            Assert.Empty(rig.Window.Body.Probes);
            Assert.Equal(height, rig.Window.Bounds.Height);
            rig.Window.CloseNow();
        });
    }

    [Theory]
    [Trait("Decision", "OD-0072")]
    [InlineData(1.0)]
    [InlineData(2.25)]
    public void A_path_that_needs_another_line_glides_the_rows_below_it_and_Done_never_jumps(double textScale)
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig(SystemLook.Default with { TextScale = textScale });
            var session = rig.Session(SetupScreen.Options);
            session.Start();
            rig.Window.Show();
            rig.Settle();
            rig.Run(1);
            var options = Assert.IsType<OptionsScreen>(session.Current);
            var probe = new FrameProbe(rig, options.View, [options.Switch, options.Preview, options.Done], [options.Done]) { ButtonsFixed = false };
            var doneBefore = probe.Of(options.Done).Box;
            var pathBefore = options.PathBox.Bounds.Height;

            session.SetInstallRoot(@"D:\Apps and tools for the whole family\Shared between several accounts\Development\Editors and agents\nth");
            probe.Run(1.5);

            Assert.True(options.PathBox.Bounds.Height > pathBefore + 1, "the new path takes another line");
            Assert.True(probe.Of(options.Done).Box.Top > doneBefore.Top + 1, "Done moved down to make room");
            Assert.Equal(session.InstallRoot, options.PathBlock.Text);
            rig.Window.CloseNow();
        });
    }
}
