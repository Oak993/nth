using Marrow.Setup.Flow;
using Marrow.Setup.Platform;
using Marrow.Setup.Tests.Fixtures;

namespace Marrow.Setup.Tests.Screens;

[Collection("SetupHeadless")]
public sealed class C4cSignatureRouteTests(SetupHeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public void Welcome_redirects_when_signature_is_unavailable() => Check(SetupScreen.Welcome, true);
    [Fact]
    [Trait("Decision", "ED-247")]
    public void Update_redirects_when_signature_is_unavailable() => Check(SetupScreen.Update, true);
    [Fact]
    [Trait("Decision", "ED-247")]
    public void Options_redirects_when_signature_is_unavailable() => Check(SetupScreen.Options, true);
    [Fact]
    [Trait("Decision", "ED-247")]
    public void Uninstall_keeps_its_route_when_signature_is_unavailable() => Check(SetupScreen.Uninstall, false);
    [Fact]
    [Trait("Decision", "ED-247")]
    public void UpToDate_keeps_its_route_when_signature_is_unavailable() => Check(SetupScreen.UpToDate, false);
    [Fact]
    [Trait("Decision", "ED-247")]
    public void NewerInstalled_keeps_its_route_when_signature_is_unavailable() => Check(SetupScreen.NewerInstalled, false);

    private void Check(SetupScreen screen, bool redirects)
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var session = rig.Session(screen);
            session.Start();
            var exit = session.ExitCode;
            session.SignatureCheckUnavailable();
            Assert.Equal(redirects ? SetupScreen.Problem : screen, session.Screen);
            Assert.Equal(redirects ? 1 : exit, session.ExitCode);
            Assert.False(session.CanChooseInstall);
            Assert.Equal(SignatureState.Unknown, session.Signature.State);
            session.SignatureChecked(new(SignatureState.Signed, "Publisher", 0));
            Assert.False(session.CanChooseInstall);
            rig.Window.CloseNow();
        });
    }
}
