using Marrow.Setup.Flow;
using Marrow.Setup.Platform;
using Marrow.Setup.Tests.Fixtures;
using Marrow.Setup.Screens;

namespace Marrow.Setup.Tests.Screens;

[Collection("SetupHeadless")]
public sealed class SignatureGateTests(SetupHeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public void Welcome_command_is_unavailable_until_the_signature_answers() => CheckCommand(SetupScreen.Welcome);

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Update_command_is_unavailable_until_the_signature_answers() => CheckCommand(SetupScreen.Update);

    private void CheckCommand(SetupScreen screen)
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var session = rig.Session(screen, screen == SetupScreen.Update ? "1.0.0" : null);
            session.Start();
            rig.Window.Show();
            try
            {
                var button = session.Current is WelcomeScreen welcome ? welcome.Install : Assert.IsType<UpdateScreen>(session.Current).Go;
                Assert.False(button.IsEnabled);
                button.Invoke();
                Assert.False(session.InstallChosen);
                session.SignatureChecked(new SignatureResult(SignatureState.Unsigned, null, 0));
                Assert.True(button.IsEnabled);
                button.Invoke();
                Assert.True(session.InstallChosen);
            }
            finally { rig.Window.CloseNow(); }
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Welcome_install_waits_for_the_signature_answer() => Check(SetupScreen.Welcome);

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Update_install_waits_for_the_signature_answer() => Check(SetupScreen.Update);

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_timed_out_check_shows_the_plain_failure_and_cannot_be_reopened_by_a_late_answer()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var session = rig.Session(SetupScreen.Welcome);
            session.Start();
            session.SignatureCheckUnavailable();
            Assert.Equal(SetupScreen.Problem, session.Screen);
            Assert.Equal(Marrow.Setup.Text.SetupStrings.ProblemSignature, session.ProblemReason);
            Assert.Equal(SignatureState.Unknown, session.Signature.State);
            session.SignatureChecked(new SignatureResult(SignatureState.Signed, "Publisher", 0));
            session.Go(SetupScreen.Welcome);
            session.ChooseInstall();
            Assert.False(session.CanChooseInstall);
            Assert.False(session.InstallChosen);
            rig.Window.CloseNow();
        });
    }

    private void Check(SetupScreen screen)
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var session = rig.Session(screen, screen == SetupScreen.Update ? "1.0.0" : null);
            session.Start();
            rig.Window.Show();
            try
            {
                Assert.Equal(SignatureState.Unknown, session.Signature.State);
                session.ChooseInstall();
                Assert.False(session.InstallChosen);
                Assert.True(rig.Window.IsVisible);
            }
            finally { rig.Window.CloseNow(); }
        });
    }
}
