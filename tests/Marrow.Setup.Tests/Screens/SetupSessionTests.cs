using Marrow.Setup.Flow;
using Marrow.Setup.Platform;
using Marrow.Setup.Screens;
using Marrow.Setup.Tests.Fixtures;
using Marrow.Setup.Text;

namespace Marrow.Setup.Tests.Screens;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3 (steps 3 to 5, "Close") and 10, "Gate routing"; unit U1
// (ED-NEW-inst-u1-1). One run of the window: it starts on the route's screen with the window (no motion), says "Uninstall
// nth" in uninstall mode, keeps each screen it built, shows the signature on Welcome when the check answers, goes to
// Problem and installs nothing when the signature does not hold, pauses Close on the screen that carries the line, and
// exits with the screen's code (Newer installed and Problem: 1). A smoke run can never install.
[Collection("SetupHeadless")]
public sealed class SetupSessionTests(SetupHeadlessSession headless)
{
    private static readonly SignatureResult Unsigned = new(SignatureState.Unsigned, null, unchecked((int)0x800B0100));
    private static readonly SignatureResult Changed = new(SignatureState.Invalid, null, unchecked((int)0x80096010));

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void It_starts_on_the_routes_screen_with_the_window()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var session = rig.Session(SetupScreen.Update, installed: "1.0.0");

            session.Start();

            Assert.Equal(SetupScreen.Update, session.Screen);
            Assert.Same(session.Current!.View, rig.Window.Body.Current);
            Assert.Equal((1, 0), rig.Window.Body.Pose(session.Current.View));
            Assert.Equal("nth Setup", rig.Window.CaptionTitle);
            Assert.Equal(0, session.ExitCode);
        });
    }

    [Fact]
    [Trait("Decision", "OD-0135")]
    public void Uninstall_mode_says_Uninstall_nth_in_the_caption_and_the_taskbar()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var session = rig.Session(SetupScreen.Uninstall, installed: "1.1.0");

            session.Start();

            Assert.Equal("Uninstall nth", rig.Window.CaptionTitle);
            Assert.Equal("Uninstall nth", rig.Window.Title);
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Each_screen_is_built_once_and_found_as_it_was()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var session = rig.Session(SetupScreen.Welcome);
            session.Start();
            rig.Window.Show();
            rig.Settle(); // the first layout measures the screens the window can reach and takes them away (ED-NEW-inst-fix-7)
            var welcome = session.Current!;

            session.Go(SetupScreen.Options);
            rig.Frames.Run(1);
            session.Go(SetupScreen.Welcome);
            rig.Frames.Run(1);

            Assert.Same(welcome, session.Current);
            Assert.Same(welcome.View, rig.Window.Body.Current);
            Assert.Single(rig.Window.Body.Children);
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void The_signature_check_fills_Welcomes_line_even_when_Welcome_is_built_later()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var session = rig.Session(SetupScreen.Welcome);
            session.Start();

            session.SignatureChecked(Unsigned);
            rig.Frames.Run(1);
            Assert.Equal(SetupStrings.WelcomeUnsigned, session.Current!.Signature!.Words);
            Assert.Equal(1, session.Current.Signature.LineOpacity);

            var later = new WindowRig().Session(SetupScreen.Update, installed: "1.0.0");
            later.Start();
            later.SignatureChecked(Unsigned);
            Assert.Equal(SetupStrings.WelcomeUnsigned, later.Get(SetupScreen.Welcome).Signature!.Words);
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void A_changed_setup_goes_to_Problem_exits_1_and_cannot_install()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var session = rig.Session(SetupScreen.Welcome);
            session.Start();
            rig.Window.Show();

            session.SignatureChecked(Changed);
            session.ChooseInstall();

            Assert.Equal(SetupScreen.Problem, session.Screen);
            Assert.Equal(SetupStrings.ProblemChanged, session.ProblemReason);
            Assert.Equal(1, session.ExitCode);
            Assert.False(session.InstallChosen);
            Assert.True(rig.Window.IsVisible);
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void A_changed_setup_leaves_a_screen_that_installs_nothing_where_it_is()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var session = rig.Session(SetupScreen.NewerInstalled, installed: "1.2.0");
            session.Start();

            session.SignatureChecked(Changed);

            Assert.Equal(SetupScreen.NewerInstalled, session.Screen);
            Assert.Equal(1, session.ExitCode);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Install_requires_a_completed_check_on_Welcome_or_Update_and_never_runs_in_smoke()
    {
        headless.OnUiThread(() =>
        {
            var smoke = new WindowRig().Session(SetupScreen.Welcome, canInstall: false);
            smoke.Start();
            smoke.SignatureChecked(new SignatureResult(SignatureState.Unsigned, null, 0));
            smoke.ChooseInstall();
            Assert.False(smoke.InstallChosen);

            var current = new WindowRig().Session(SetupScreen.UpToDate, installed: "1.1.0");
            current.Start();
            current.SignatureChecked(new SignatureResult(SignatureState.Signed, "Publisher", 0));
            current.ChooseInstall();
            Assert.False(current.InstallChosen);

            var rig = new WindowRig();
            var welcome = rig.Session(SetupScreen.Welcome);
            welcome.Start();
            rig.Window.Show();
            var closed = false;
            rig.Window.Closed += (_, _) => closed = true;
            welcome.ChooseInstall();
            Assert.False(welcome.InstallChosen);
            Assert.False(closed);
            welcome.SignatureChecked(new SignatureResult(SignatureState.Unsigned, null, 0));
            welcome.ChooseInstall();
            Assert.True(welcome.InstallChosen);
            Assert.True(closed);
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Close_pauses_on_the_screen_with_the_line_and_its_words()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var session = rig.Session(SetupScreen.Welcome);
            session.Start();
            Assert.Throws<InvalidOperationException>(session.PauseClose);

            session.Go(SetupScreen.Installing);
            session.PauseClose();
            Assert.Equal(SetupStrings.InstallingHold, session.Current!.Hold!.Words);
            Assert.Equal(SetupStrings.ShutdownInstalling, rig.Blocker.Reason);
            session.ChooseInstall();
            Assert.False(session.InstallChosen);

            session.Go(SetupScreen.Update);
            session.PauseClose();
            Assert.Equal(SetupStrings.UpdateHold, session.Current!.Hold!.Words);
            Assert.False(session.Get(SetupScreen.Installing).Hold!.IsWanted);

            session.ResumeClose();
            Assert.Null(rig.Blocker.Reason);
            Assert.Equal(SetupStrings.UninstallRemoving, SetupSession.ShutdownReason(SetupScreen.Uninstall));
            Assert.Equal(SetupStrings.UninstallHold, SetupSession.HoldWords(SetupScreen.Uninstall));
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Each_screen_closes_with_its_exit_code_unless_the_flow_set_one()
    {
        headless.OnUiThread(() =>
        {
            Assert.Equal(1, Started(SetupScreen.NewerInstalled, "1.2.0").ExitCode);
            Assert.Equal(0, Started(SetupScreen.UpToDate, "1.1.0").ExitCode);
            Assert.Equal(0, Started(SetupScreen.Welcome, null).ExitCode);

            var session = Started(SetupScreen.Welcome, null);
            session.Fail(SetupStrings.ProblemOther);
            Assert.Equal(SetupScreen.Problem, session.Screen);
            Assert.Equal(1, session.ExitCode);
            session.Window.Show();
            session.Finish(7);
            Assert.Equal(7, session.ExitCode);
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void A_problem_found_before_the_window_opens_is_its_first_screen()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var session = rig.Session(SetupScreen.Problem);

            session.Fail(SetupStrings.ProblemOther);
            session.Start();

            Assert.Equal(SetupScreen.Problem, session.Screen);
            Assert.Equal(SetupStrings.ProblemOther, session.ProblemReason);
            Assert.Equal(1, session.ExitCode);
        });
    }

    private static SetupSession Started(SetupScreen screen, string? installed)
    {
        var session = new WindowRig().Session(screen, installed);
        session.Start();
        return session;
    }
}
