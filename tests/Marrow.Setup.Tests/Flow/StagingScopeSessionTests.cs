using Avalonia.Controls;
using Marrow.Core.Support;
using Marrow.Setup.Flow;
using Marrow.Setup.Screens;
using Marrow.Setup.Tests.Fixtures;

namespace Marrow.Setup.Tests.Flow;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3 (step 8: the staging folder is deleted, then Ready shows) and 5 ("Leftovers": the
// Problem screen is an exit path), unit U9 (ED-NEW-inst-u9-1). A session tells when a screen has been put in the window; the
// staging scope releases its folder when that screen is Ready or Problem and on no other screen. Headless window, temporary folders.
[Collection("SetupHeadless")]
public sealed class StagingScopeSessionTests(SetupHeadlessSession headless) : IDisposable
{
    private readonly SweepArea area = new();

    public void Dispose() => area.Dispose();

    private StagingScope Staged(out string folder)
    {
        var scope = StagingScope.Create(area.LocalAppData, area.Temp, "1.1.0", lastChance: false);
        folder = scope.Ensure();
        area.Put(Path.Combine(folder, InstallOptions.OptionsFileName), "{}");
        return scope;
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_session_says_which_screen_was_shown_the_first_and_every_one_after()
    {
        headless.OnUiThread(() =>
        {
            var session = new WindowRig().Session(SetupScreen.Update, installed: "1.0.0");
            var shown = new List<SetupScreen>();
            session.ScreenShown += shown.Add;

            session.Start();
            session.Go(SetupScreen.Installing);
            session.Go(SetupScreen.Ready);

            Assert.Equal([SetupScreen.Update, SetupScreen.Installing, SetupScreen.Ready], shown);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_screen_is_built_and_in_the_window_when_it_is_announced()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var session = rig.Session(SetupScreen.Welcome);
            session.Start();
            Control? inWindow = null;
            session.ScreenShown += _ => inWindow = rig.Window.Body.Current;

            session.Go(SetupScreen.Options);

            Assert.Same(session.Current!.View, inWindow);
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Ready_releases_the_staging_folder_after_its_result_could_be_read_and_Problem_does_too()
    {
        headless.OnUiThread(() =>
        {
            foreach (var final in new[] { SetupScreen.Ready, SetupScreen.Problem })
            {
                using var scope = Staged(out var folder);
                var session = new WindowRig().Session(SetupScreen.Welcome);
                scope.ReleaseWhenShown(session);
                session.Start();
                session.Go(SetupScreen.Installing);
                Assert.True(Directory.Exists(folder), "the folder stays while the install runs");

                session.Go(final);

                Assert.False(Directory.Exists(folder), $"{final} shows with the folder gone");
            }
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void No_other_screen_releases_the_folder()
    {
        headless.OnUiThread(() =>
        {
            using var scope = Staged(out var folder);
            var session = new WindowRig().Session(SetupScreen.Welcome);
            scope.ReleaseWhenShown(session);
            session.Start();

            foreach (var screen in new[] { SetupScreen.Options, SetupScreen.Installing, SetupScreen.Update, SetupScreen.UpToDate, SetupScreen.NewerInstalled, SetupScreen.Uninstall, SetupScreen.Welcome })
            {
                session.Go(screen);
                Assert.True(File.Exists(Path.Combine(folder, InstallOptions.OptionsFileName)), $"{screen} must not release the folder");
            }
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_run_that_starts_on_Problem_has_nothing_to_release_and_says_so_quietly()
    {
        headless.OnUiThread(() =>
        {
            using var scope = StagingScope.Create(area.LocalAppData, area.Temp, "1.1.0", lastChance: false);
            var session = new WindowRig().Session(SetupScreen.Problem);
            scope.ReleaseWhenShown(session);

            session.Start();

            Assert.False(scope.IsHeld);
            Assert.False(Directory.Exists(area.SetupRoot));
        });
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_session_is_required()
    {
        using var scope = StagingScope.Create(area.LocalAppData, area.Temp, "1.1.0", lastChance: false);

        Assert.Throws<ArgumentNullException>(() => scope.ReleaseWhenShown(null!));
    }
}
