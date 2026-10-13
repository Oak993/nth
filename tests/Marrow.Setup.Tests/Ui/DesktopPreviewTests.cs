using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Marrow.Setup.Flow;
using Marrow.Setup.Motion;
using Marrow.Setup.Screens;
using Marrow.Setup.Support;
using Marrow.Setup.Tests.Fakes;
using Marrow.Setup.Tests.Fixtures;
using Marrow.Setup.Ui;

namespace Marrow.Setup.Tests.Ui;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 8a, "The desktop component", unit U2 (ED-NEW-inst-u2-1). Frame capture
// at 60 fps of the icon's interruptible springs: ten toggles 60 ms apart, and a landing turned off at 150 ms and landed
// again at 300 ms, never move the icon more than 3 DIP or change its scale by more than 0.04 in a frame while it can be
// seen (4 % opacity or more), never fade it by more than 35 % in a frame, and end at rest; Calm only fades; the glow plays
// only on the large size and never in a contrast theme; the medium one lifts on hover, presses and reports a tap; a
// contrast theme outlines the icons; light wallpaper gives ink labels.
[Collection("SetupHeadless")]
public sealed class DesktopPreviewTests(SetupHeadlessSession headless)
{
    private const double MaxStep = 3, MaxScale = .04, MaxFade = .35, Seen = .04;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Decision", "OD-0072")]
    public void Ten_toggles_60_ms_apart_never_jump_and_end_at_rest(bool large)
    {
        headless.OnUiThread(() =>
        {
            var (frames, desk) = Desk(large, calm: false);
            desk.SetState(DeskState.Off, instant: true);
            var poses = new List<(double Y, double Scale, double Opacity, double Label)> { desk.IconPose };
            var toggles = 0;
            var clock = 0.0;
            for (var f = 0; f < 160; f++)
            {
                while (toggles < 10 && toggles * .06 <= clock + 1e-9)
                {
                    desk.SetState(toggles % 2 == 0 ? DeskState.Land : DeskState.Off);
                    poses.Add(desk.IconPose);
                    toggles++;
                }
                poses.Add(Frame(frames, desk));
                clock += 1.0 / 60;
            }
            Check(poses, desk.Em);
            Assert.Equal(DeskState.Off, desk.State);
            Assert.Equal((0.0, DesktopPreview.OffScale, 0.0, 0.0), Rounded(desk.IconPose));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Decision", "OD-0072")]
    public void A_landing_turned_off_at_150_ms_and_landed_again_at_300_ms_keeps_its_speed_and_never_jumps(bool large)
    {
        headless.OnUiThread(() =>
        {
            var (frames, desk) = Desk(large, calm: false);
            desk.SetState(DeskState.Off, instant: true);
            var poses = new List<(double Y, double Scale, double Opacity, double Label)> { desk.IconPose };
            desk.SetState(DeskState.Land);
            for (var f = 0; f < 9; f++) poses.Add(Frame(frames, desk));
            var interrupted = desk.IconPose;
            desk.SetState(DeskState.Off);
            // the springs keep their value: nothing snaps when the target changes
            Assert.Equal(interrupted, desk.IconPose);
            for (var f = 0; f < 9; f++) poses.Add(Frame(frames, desk));
            var before = desk.IconPose;
            desk.SetState(DeskState.Land);
            poses.Add(desk.IconPose);
            // while it can be seen it lands from where it is; only an icon under 4 % may be placed at its start pose
            if (before.Opacity >= Seen) Assert.Equal(before, desk.IconPose);
            for (var f = 0; f < 120; f++) poses.Add(Frame(frames, desk));
            Check(poses, desk.Em);
            Assert.Equal((0.0, 1.0, 1.0, 1.0), Rounded(desk.IconPose));
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void The_drop_starts_0_9_em_higher_at_74_percent_and_the_label_follows_0_2_s_later()
    {
        headless.OnUiThread(() =>
        {
            var (frames, desk) = Desk(false, calm: false);
            desk.SetState(DeskState.Off, instant: true);
            desk.SetState(DeskState.Land);
            Assert.Equal(DesktopPreview.DropFrom, desk.IconPose.Y, 6);
            Assert.Equal(DesktopPreview.DropScale, desk.IconPose.Scale, 6);
            frames.Run(.15);
            Assert.Equal(0, desk.IconPose.Label);
            frames.Run(.2);
            Assert.True(desk.IconPose.Label > 0);
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Calm_only_fades()
    {
        headless.OnUiThread(() =>
        {
            var (frames, desk) = Desk(true, calm: true);
            desk.SetState(DeskState.Off, instant: true);
            desk.SetState(DeskState.Land);
            for (var f = 0; f < 30; f++)
            {
                Frame(frames, desk);
                Assert.Equal(0, desk.IconPose.Y);
                Assert.Equal(1, desk.IconPose.Scale);
            }
            Assert.Equal(1, desk.IconPose.Opacity, 2);
            Assert.False(desk.IsGlowing);
            desk.SetState(DeskState.Off);
            for (var f = 0; f < 30; f++)
            {
                Frame(frames, desk);
                Assert.Equal(1, desk.IconPose.Scale);
            }
            Assert.Equal(0, desk.IconPose.Opacity, 2);
            desk.Appear(false);
            desk.Appear(true);
            Assert.Equal(0, desk.Appearance.Rise);
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void The_glow_plays_once_on_the_large_size_only()
    {
        headless.OnUiThread(() =>
        {
            var (frames, large) = Desk(true, calm: false);
            large.SetState(DeskState.Off, instant: true);
            large.SetState(DeskState.Land);
            Assert.True(large.IsGlowing);
            frames.Run(DesktopPreview.GlowDelay + DesktopPreview.GlowLength + .1);
            Assert.False(large.IsGlowing);

            var (_, medium) = Desk(false, calm: false);
            medium.SetState(DeskState.Off, instant: true);
            medium.SetState(DeskState.Land);
            Assert.False(medium.IsGlowing);

            Assert.Equal((0.0, .5), DesktopPreview.GlowAt(DesktopPreview.GlowDelay));
            Assert.Equal(1, DesktopPreview.GlowAt(DesktopPreview.GlowDelay + DesktopPreview.GlowLength * .28).Opacity, 6);
            var end = DesktopPreview.GlowAt(DesktopPreview.GlowDelay + DesktopPreview.GlowLength);
            Assert.Equal(0, end.Opacity, 6);
            Assert.Equal(1.35, end.Scale, 6);
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Ready_fades_the_large_one_in_rising_6_DIP()
    {
        headless.OnUiThread(() =>
        {
            var (frames, desk) = Desk(true, calm: false);
            desk.Appear(false);
            desk.Appear(true);
            Assert.Equal((0.0, DesktopPreview.AppearRise), desk.Appearance);
            var last = desk.Appearance;
            for (var f = 0; f < 40; f++)
            {
                frames.Frame();
                var now = desk.Appearance;
                Assert.True(Math.Abs(now.Rise - last.Rise) <= MaxStep);
                Assert.True(Math.Abs(now.Opacity - last.Opacity) <= MaxFade);
                last = now;
            }
            Assert.Equal((1.0, 0.0), (Math.Round(desk.Appearance.Opacity, 3), Math.Round(desk.Appearance.Rise, 3)));
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void The_medium_one_lifts_on_hover_presses_and_a_tap_turns_the_shortcut_over()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var session = rig.Session(SetupScreen.Options);
            session.Start();
            rig.Window.Show();
            rig.Settle();
            rig.Run(1);
            var options = Assert.IsType<OptionsScreen>(session.Current);
            var preview = options.Preview;
            var centre = preview.TranslatePoint(new Point(preview.Bounds.Width / 2, preview.Bounds.Height / 2), rig.Window)!.Value;
            var shortcut = session.DesktopShortcut;

            rig.Window.MouseMove(centre);
            rig.Run(.4);
            Assert.Equal(1, preview.Lift.Hover, 3);
            rig.Window.MouseDown(centre, MouseButton.Left);
            rig.Run(.2);
            Assert.Equal(.985, preview.Lift.Press, 3);
            rig.Window.MouseUp(centre, MouseButton.Left);
            rig.Run(.4);
            Assert.Equal(1, preview.Lift.Press, 3);
            Assert.NotEqual(shortcut, session.DesktopShortcut);
            Assert.Equal(session.DesktopShortcut, options.Switch.IsOn);
            rig.Window.MouseMove(new Point(2, 2));
            rig.Run(.4);
            Assert.Equal(0, preview.Lift.Hover, 3);
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Calm_and_contrast_never_lift_and_contrast_outlines_with_no_glow()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig(WindowRig.Contrast);
            var session = rig.Session(SetupScreen.Options);
            session.Start();
            rig.Window.Show();
            rig.Settle();
            rig.Run(1);
            var preview = Assert.IsType<OptionsScreen>(session.Current).Preview;
            var centre = preview.TranslatePoint(new Point(preview.Bounds.Width / 2, preview.Bounds.Height / 2), rig.Window)!.Value;
            rig.Window.MouseMove(centre);
            rig.Run(.4);
            Assert.Equal(0, preview.Lift.Hover);
            Assert.True(preview.Tokens.Desk.Outlined);
            Assert.False(preview.InkLabels);
            rig.Window.CloseNow();

            var large = new DesktopPreview(large: true, () => false);
            var frames = new ManualFrameSource();
            large.Attach(new Animator(frames));
            var window = new WindowRig(WindowRig.Contrast);
            window.Window.Body.Show(large, immediately: true);
            window.Window.Show();
            window.Settle();
            large.SetState(DeskState.Off, instant: true);
            large.SetState(DeskState.Land);
            Assert.False(large.IsGlowing);
            window.Window.CloseNow();
        });
    }

    [Theory]
    [InlineData(250, true)]
    [InlineData(20, false)]
    [Trait("Decision", "OD-0072")]
    public void A_light_wallpaper_gives_ink_labels_and_a_dark_one_light_labels(byte grey, bool ink)
    {
        headless.OnUiThread(() =>
        {
            var (frames, desk) = Desk(false, calm: false);
            var scene = new WallpaperScene(WallpaperOrigin.Wallpaper, FakeWallpaperSource.Solid(320, 200, grey, grey, grey), WallpaperPosition.Fill, default, new WallpaperRect(0, 0, 2560, 1600), new WallpaperRect(0, 0, 2560, 1600));
            desk.SetWallpaper(scene);
            Assert.Equal(ink, desk.InkLabels);
            Assert.Equal(288, desk.Picture!.Width);
            Assert.Equal(0, desk.WallOpacity);
            frames.Run(.35);
            Assert.Equal(1, desk.WallOpacity, 3);
            desk.SetWallpaper(null);
            Assert.Null(desk.Picture);
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void The_preview_is_decorative_and_never_a_tab_stop()
    {
        headless.OnUiThread(() =>
        {
            var desk = new DesktopPreview(large: false, () => false);
            Assert.False(desk.Focusable);
            Assert.False(KeyboardNavigation.GetIsTabStop(desk));
            Assert.Equal(Avalonia.Automation.AccessibilityView.Raw, Avalonia.Automation.AutomationProperties.GetAccessibilityView(desk));
            Assert.Equal((DesktopPreview.MediumWidth, 90.0), (desk.Width, desk.Height));
            var large = new DesktopPreview(large: true, () => false);
            Assert.Equal((DesktopPreview.LargeWidth, 110.0), (large.Width, large.Height));
        });
    }

    private static (ManualFrameSource Frames, DesktopPreview Desk) Desk(bool large, bool calm)
    {
        var frames = new ManualFrameSource();
        var desk = new DesktopPreview(large, () => calm);
        desk.Attach(new Animator(frames));
        return (frames, desk);
    }

    private static (double Y, double Scale, double Opacity, double Label) Frame(ManualFrameSource frames, DesktopPreview desk)
    {
        frames.Frame();
        return desk.IconPose;
    }

    private static void Check(List<(double Y, double Scale, double Opacity, double Label)> poses, double em)
    {
        for (var i = 1; i < poses.Count; i++)
        {
            var a = poses[i - 1];
            var b = poses[i];
            Assert.True(Math.Abs(b.Opacity - a.Opacity) <= MaxFade, $"the icon faded by {Math.Abs(b.Opacity - a.Opacity):F3} in frame {i}");
            Assert.True(Math.Abs(b.Label - a.Label) <= MaxFade, $"the label faded by {Math.Abs(b.Label - a.Label):F3} in frame {i}");
            if (a.Opacity < Seen || b.Opacity < Seen) continue;
            Assert.True(Math.Abs(b.Y - a.Y) * em <= MaxStep, $"the icon moved {Math.Abs(b.Y - a.Y) * em:F2} DIP in frame {i}");
            Assert.True(Math.Abs(b.Scale - a.Scale) <= MaxScale, $"the icon's scale changed by {Math.Abs(b.Scale - a.Scale):F3} in frame {i}");
        }
    }

    private static (double, double, double, double) Rounded((double Y, double Scale, double Opacity, double Label) p) =>
        (Math.Round(p.Y, 3), Math.Round(p.Scale, 3), Math.Round(p.Opacity, 3), Math.Round(p.Label, 3));
}
