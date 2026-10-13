using Avalonia.Input;
using Avalonia.LogicalTree;
using Marrow.Setup.Flow;
using Marrow.Setup.Platform;
using Marrow.Setup.Screens;
using Marrow.Setup.Tests.Fixtures;
using Marrow.Setup.Text;
using Marrow.Setup.Ui;

namespace Marrow.Setup.Tests.Screens;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3 ("Keyboard": Tab goes through the links and buttons in reading order) and
// LEAN-TESTS item 10; VELOPACK_CHECK.md finding 16 (ED-NEW-inst-fix-9). Only Options had its Tab order tested
// (CanvasScreensTests); these pin every other screen a person can tab through: what takes a Tab stop, in which order, which
// button Enter presses, and that nothing else (a preview, a mark, a hidden button) is a stop. Problem has no Close button.
[Collection("SetupHeadless")]
public sealed class TabOrderTests(SetupHeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Welcome_tabs_from_Install_to_the_Options_link_then_the_desktop_and_licence_links()
    {
        headless.OnUiThread(() =>
        {
            var (rig, session) = Open(SetupScreen.Welcome);
            var welcome = Assert.IsType<WelcomeScreen>(session.Current);

            Assert.Equal(new object[] { welcome.Install, welcome.OptionsLink, welcome.DeskLink, welcome.LicenseLink }, Stops(welcome));
            Assert.Same(welcome.Install, welcome.DefaultButton);
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Problem_tabs_through_Copy_details_Retry_and_Show_details_and_has_no_Close_button()
    {
        headless.OnUiThread(() =>
        {
            var (rig, session) = Open(SetupScreen.Problem, fail: SetupStrings.ProblemOther);
            var problem = Assert.IsType<ProblemScreen>(session.Current);

            var stops = Stops(problem);
            Assert.Equal(new object[] { problem.Copy, problem.Retry, problem.DetailsLink }, stops);
            Assert.DoesNotContain(stops.OfType<SetupButton>(), button => button.Label == SetupStrings.ReadyClose);
            Assert.Same(problem.Retry, problem.DefaultButton);
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Problem_for_a_changed_copy_drops_Retry_from_the_Tab_order()
    {
        headless.OnUiThread(() =>
        {
            var (rig, session) = Open(SetupScreen.Problem, fail: SetupStrings.ProblemChanged);
            var problem = Assert.IsType<ProblemScreen>(session.Current);

            Assert.Equal(new object[] { problem.Copy, problem.DetailsLink }, Stops(problem));
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Update_tabs_from_Not_now_to_Update_and_Enter_updates()
    {
        headless.OnUiThread(() =>
        {
            var (rig, session) = Open(SetupScreen.Update, installed: "1.0.0");
            var update = Assert.IsType<UpdateScreen>(session.Current);

            Assert.Equal(new object[] { update.Later, update.Go }, Stops(update));
            Assert.Same(update.Go, update.DefaultButton);
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Uninstall_tabs_from_the_switch_to_Cancel_then_Remove_and_Cancel_holds_the_focus()
    {
        headless.OnUiThread(() =>
        {
            var (rig, session) = Open(SetupScreen.Uninstall, installed: "1.1.0");
            var uninstall = Assert.IsType<UninstallScreen>(session.Current);

            Assert.Equal(new object[] { uninstall.Switch, uninstall.Cancel, uninstall.Remove }, Stops(uninstall));
            Assert.True(uninstall.Cancel.IsFocused);
            Assert.Same(uninstall.Cancel, uninstall.DefaultButton);
            rig.Window.CloseNow();
        });
    }

    [Theory]
    [Trait("Decision", "OD-0072")]
    [InlineData(SetupScreen.UpToDate, "1.1.0")]
    [InlineData(SetupScreen.NewerInstalled, "9.0.0")]
    public void Up_to_date_and_newer_installed_tab_from_Close_to_Open_nth(SetupScreen screen, string installed)
    {
        headless.OnUiThread(() =>
        {
            var (rig, session) = Open(screen, installed: installed);
            var current = Assert.IsType<CurrentScreen>(session.Current);

            Assert.Equal(new object[] { current.Close, current.Open }, Stops(current));
            Assert.Same(current.Open, current.DefaultButton);
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Ready_tabs_from_Close_to_Open_nth_and_the_desktop_moment_takes_no_stop()
    {
        headless.OnUiThread(() =>
        {
            var (rig, session) = Open(SetupScreen.Welcome);
            session.Go(SetupScreen.Ready);
            rig.Run(1);
            var ready = Assert.IsType<ReadyScreen>(session.Current);

            Assert.Equal(new object[] { ready.Close, ready.Open }, Stops(ready));
            Assert.Same(ready.Open, ready.DefaultButton);
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Installing_has_no_Tab_stop_at_all()
    {
        headless.OnUiThread(() =>
        {
            var (rig, session) = Open(SetupScreen.Welcome);
            session.Go(SetupScreen.Installing);
            rig.Run(1);
            var installing = Assert.IsType<InstallingScreen>(session.Current);

            Assert.Empty(Stops(installing));
            rig.Window.CloseNow();
        });
    }

    private static (WindowRig Rig, SetupSession Session) Open(SetupScreen screen, string? installed = null, string? fail = null)
    {
        var rig = new WindowRig();
        var session = rig.Session(screen, installed);
        if (fail is not null) session.Fail(fail);
        session.Start();
        session.SignatureChecked(new SignatureResult(SignatureState.Unsigned, null, 0)); // the window's check has answered: Install and Update are live
        rig.Window.Show();
        rig.Settle();
        return (rig, session);
    }

    // What Tab visits, in order: the screen's focusable tab stops that can be reached now (shown and enabled).
    private static object[] Stops(ISetupScreen screen) =>
        [.. screen.View.GetLogicalDescendants().OfType<InputElement>()
            .Where(element => element.Focusable && KeyboardNavigation.GetIsTabStop(element) && element.IsEffectivelyVisible && element.IsEffectivelyEnabled)];
}
