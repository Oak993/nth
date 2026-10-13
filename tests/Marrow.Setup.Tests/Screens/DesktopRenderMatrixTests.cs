using Marrow.Core.Support;
using Marrow.Setup.Flow;
using Marrow.Setup.Platform;
using Marrow.Setup.Screens;
using Marrow.Setup.Tests.Fixtures;
using Marrow.Setup.Ui;

namespace Marrow.Setup.Tests.Screens;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 8a, "Tests" (headless renders), unit U10 (ED-NEW-inst-u10-1). The desktop
// component drawn in the window, in the dark, light, high contrast and Calm looks: the Ready moment at 0, 1.35, 1.75 and
// 3.2 s, and the Options preview on, off and on again. Every frame draws (not blank), fits the window without clipping or
// overlap (the same check as ScreenRenderTests, which only looks at a screen once it has settled), and sits in the pose the
// timeline gives it. With NTH_SETUP_RENDERS set to a folder the renders are saved there as PNG files for the owner's review.
[Collection("SetupHeadless")]
public sealed class DesktopRenderMatrixTests(SetupHeadlessSession headless)
{
    public static TheoryData<string> Looks => new() { "dark", "light", "contrast", "calm" };

    [Theory]
    [MemberData(nameof(Looks))]
    [Trait("Decision", "OD-0072")]
    public void The_Ready_moment_draws_at_0_1_35_1_75_and_3_2_s_in_every_look(string look)
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig(Look(look));
            var session = rig.Session(SetupScreen.Ready);
            session.SetResult(new InstallResult(DesktopShortcutOutcome.Created), updated: false);
            session.Start();
            rig.Window.Show();
            rig.Settle();
            var ready = Assert.IsType<ReadyScreen>(session.Current);
            var calm = look == "calm";
            var problems = new List<string>();

            var at = 0.0;
            foreach (var second in new[] { 0.0, 1.35, 1.75, 3.2 })
            {
                rig.Run(second - at);
                at = second;
                var name = FormattableString.Invariant($"u10-ready-{look}-t{second * 1000:0}");
                problems.AddRange(Draw(rig, ready, name).Select(problem => $"{name}: {problem}"));

                var desk = ready.Desk.Appearance.Opacity;
                if (second == 0) Assert.True(desk <= 0.01, $"{name}: the desktop shows at once ({desk:F3})");
                if (second == 1.35 && !calm) Assert.InRange(desk, 0, 0.5); // the desktop's 0.4 s fade begins at 1.35 s (canvas timeline): not yet there
                if (second == 1.35 && calm) Assert.Equal(DeskState.Land, ready.Desk.State); // Calm has no wait: shown at once and landed at 80 ms
                if (second == 1.75 && !calm) Assert.InRange(desk, 0.9, 1); // the desktop is in; the icon drops from here (lands within 0.05 s of this frame)
                if (second == 3.2)
                {
                    Assert.Equal(DeskState.Land, ready.Desk.State);
                    Assert.Equal(1, desk, 3);
                    Assert.Equal((0.0, 1.0, 1.0, 1.0), (Math.Round(ready.Desk.IconPose.Y, 3), Math.Round(ready.Desk.IconPose.Scale, 3), Math.Round(ready.Desk.IconPose.Opacity, 3), Math.Round(ready.Desk.IconPose.Label, 3)));
                }
            }

            Assert.True(problems.Count == 0, string.Join("\n", problems));
            rig.Window.CloseNow();
        });
    }

    [Theory]
    [MemberData(nameof(Looks))]
    [Trait("Decision", "OD-0072")]
    public void The_Options_preview_draws_on_off_and_on_again_in_every_look(string look)
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig(Look(look));
            var session = rig.Session(SetupScreen.Options);
            session.Start();
            rig.Window.Show();
            rig.Settle();
            rig.Run(2.5);
            var options = Assert.IsType<OptionsScreen>(session.Current);
            var problems = new List<string>();

            Assert.True(options.Switch.IsOn);
            Assert.Equal(DeskState.On, options.Preview.State); // opened still, with no animation (PLAN.md 8a)
            Assert.Equal((0.0, 1.0, 1.0, 1.0), (Math.Round(options.Preview.IconPose.Y, 3), Math.Round(options.Preview.IconPose.Scale, 3), Math.Round(options.Preview.IconPose.Opacity, 3), Math.Round(options.Preview.IconPose.Label, 3)));
            problems.AddRange(Draw(rig, options, $"u10-options-{look}-on").Select(problem => $"on: {problem}"));

            options.Switch.Toggle();
            rig.Run(1.5);
            Assert.False(session.DesktopShortcut);
            Assert.Equal(DeskState.Off, options.Preview.State);
            Assert.Equal(0, options.Preview.IconPose.Opacity, 3);
            problems.AddRange(Draw(rig, options, $"u10-options-{look}-off").Select(problem => $"off: {problem}"));

            options.Switch.Toggle();
            rig.Run(2);
            Assert.True(session.DesktopShortcut);
            Assert.Equal(DeskState.Land, options.Preview.State);
            Assert.Equal((0.0, 1.0, 1.0, 1.0), (Math.Round(options.Preview.IconPose.Y, 3), Math.Round(options.Preview.IconPose.Scale, 3), Math.Round(options.Preview.IconPose.Opacity, 3), Math.Round(options.Preview.IconPose.Label, 3)));
            problems.AddRange(Draw(rig, options, $"u10-options-{look}-on-again").Select(problem => $"on again: {problem}"));

            Assert.True(problems.Count == 0, string.Join("\n", problems));
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void A_session_starts_with_the_desktop_shortcut_on_and_Options_shows_it_on()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var session = rig.Session(SetupScreen.Welcome);
            Assert.True(session.DesktopShortcut);

            session.Start();
            rig.Window.Show();
            rig.Settle();
            var welcome = Assert.IsType<WelcomeScreen>(session.Current);
            Assert.Equal(("Adds nth to Start and your desktop", "Don’t add to desktop"), welcome.DeskLine);
            welcome.OptionsLink.Invoke();
            rig.Run(0.8);

            var options = Assert.IsType<OptionsScreen>(session.Current);
            Assert.True(options.Switch.IsOn);
            Assert.Equal(DeskState.On, options.Preview.State);
            rig.Window.CloseNow();
        });
    }

    private static SystemLook Look(string look) => look switch
    {
        "light" => WindowRig.Light,
        "contrast" => WindowRig.Contrast,
        "calm" => WindowRig.Calm,
        _ => SystemLook.Default,
    };

    /// <summary>Renders the window as it is now (saved when NTH_SETUP_RENDERS names a folder) and returns what is wrong with it.</summary>
    private static List<string> Draw(WindowRig rig, CanvasScreen screen, string name)
    {
        var problems = ScreenRenderTests.Check(rig, screen).ToList();
        using var bitmap = ScreenRenderTests.Render(rig.Window, 1);
        if (!ScreenRenderTests.Draws(bitmap)) problems.Add("the render is blank");
        if (Environment.GetEnvironmentVariable("NTH_SETUP_RENDERS") is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
            bitmap.Save(Path.Combine(folder, name + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
        return problems;
    }
}
