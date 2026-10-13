using Marrow.Setup.Flow;
using Marrow.Setup.Platform;
using Marrow.Setup.Screens;
using Marrow.Setup.Tests.Fixtures;
using Marrow.Setup.Text;
using Marrow.Setup.Ui;

namespace Marrow.Setup.Tests.Ui;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3b, "Reserved lines", unit U2 (ED-NEW-inst-u2-1). A line whose words
// change keeps the height of its tallest wording at every text size (100, 150 and 225 %), so a change never moves what
// is under it: Welcome's desktop line (adds both / adds Start only), Options' install path (the default under a long,
// non-ASCII profile, a OneDrive folder, a short drive path), Installing's subtitle (usual / slower than usual), and the
// byte count beside the copying step (its width that of the longest count).
[Collection("SetupHeadless")]
public sealed class ReservedLinesTests(SetupHeadlessSession headless)
{
    private const string OneDrive = @"C:\Users\Ana-María Østergård-Fernández\OneDrive - Contoso Ltd\Programs\nth";

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.25)]
    [Trait("Decision", "OD-0072")]
    public void Welcome_keeps_the_desktop_lines_height_whichever_way_it_reads(double textScale)
    {
        headless.OnUiThread(() =>
        {
            var (rig, session) = Shown(SetupScreen.Welcome, textScale);
            var welcome = Assert.IsType<WelcomeScreen>(session.Current);
            var height = welcome.DeskLineBox.Bounds.Height;
            var install = welcome.Install.Bounds;
            Assert.True(height > 0);

            session.SetDesktopShortcut(false);
            rig.Run(.5);
            Assert.Equal(SetupStrings.WelcomeAddsStart, welcome.DeskLine.Sentence);
            Assert.Equal(height, welcome.DeskLineBox.Bounds.Height, 3);
            Assert.Equal(install, welcome.Install.Bounds);
            session.SetDesktopShortcut(true);
            rig.Run(.5);
            Assert.Equal(height, welcome.DeskLineBox.Bounds.Height, 3);
            Assert.Equal(install, welcome.Install.Bounds);
            rig.Window.CloseNow();
        });
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.25)]
    [Trait("Decision", "OD-0072")]
    public void Options_keeps_the_paths_height_for_the_long_default_and_a_short_path_and_fits_a_OneDrive_folder(double textScale)
    {
        headless.OnUiThread(() =>
        {
            var (rig, session) = Shown(SetupScreen.Options, textScale);
            var options = Assert.IsType<OptionsScreen>(session.Current);
            var height = options.PathBox.Bounds.Height;
            var done = options.Done.Bounds;
            Assert.Equal(WindowRig.Root, options.PathBlock.Text);

            session.SetInstallRoot(@"D:\nth");
            rig.Run(.8);
            Assert.Equal(height, options.PathBox.Bounds.Height, 3);
            Assert.Equal(done, options.Done.Bounds);
            Assert.Equal(@"D:\nth", options.PathBlock.Text);

            // a path taller than the default's: the box fits it (the rows under it glide), and Done stays put
            session.SetInstallRoot(OneDrive);
            rig.Run(.8);
            Assert.True(options.PathBox.Bounds.Height >= height);
            Assert.Equal(done, options.Done.Bounds);
            session.SetInstallRoot(WindowRig.Root);
            rig.Run(.8);
            Assert.Equal(height, options.PathBox.Bounds.Height, 3);
            Assert.Equal(done, options.Done.Bounds);
            rig.Window.CloseNow();
        });
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.25)]
    [Trait("Decision", "OD-0072")]
    public void Installing_keeps_the_subtitles_height_when_it_says_it_is_slower_than_usual(double textScale)
    {
        headless.OnUiThread(() =>
        {
            var (rig, session) = Shown(SetupScreen.Installing, textScale);
            var installing = Assert.IsType<InstallingScreen>(session.Current);
            var box = (Avalonia.Controls.Control)installing.Subtitle.Parent!;
            var height = box.Bounds.Height;
            var steps = installing.Steps.Bounds;

            installing.ShowSlow(true);
            rig.Run(.8);
            Assert.Equal(SetupStrings.InstallingSlow, installing.Subtitle.Text);
            Assert.Equal(height, box.Bounds.Height, 3);
            Assert.Equal(steps, installing.Steps.Bounds);
            installing.ShowSlow(false);
            rig.Run(.8);
            Assert.Equal(height, box.Bounds.Height, 3);
            Assert.Equal(steps, installing.Steps.Bounds);
            rig.Window.CloseNow();
        });
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.25)]
    [Trait("Decision", "OD-0072")]
    public void The_byte_count_keeps_the_width_of_the_longest_count(double textScale)
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig(SystemLook.Default with { TextScale = textScale });
            var session = rig.Session(SetupScreen.Installing);
            session.SizeMegabytes = 420;
            session.Start();
            rig.Window.Show();
            rig.Settle();
            var installing = Assert.IsType<InstallingScreen>(session.Current);
            installing.Steps.Advance(1);
            var copying = installing.Steps.Rows[1];
            copying.SetDetail(SetupStrings.ProgressBytes(1, 420));
            rig.Run(.3);
            var label = copying.LabelBlock.Bounds;
            var row = copying.Bounds;
            foreach (var done in new long[] { 9, 87, 120, 419, 420 })
            {
                copying.SetDetail(SetupStrings.ProgressBytes(done, 420));
                rig.Run(1.0 / 60);
                Assert.Equal(label, copying.LabelBlock.Bounds);
                Assert.Equal(row, copying.Bounds);
            }
            Assert.Equal("420 of 420 MB", copying.Detail);
            rig.Window.CloseNow();
        });
    }

    private static (WindowRig Rig, SetupSession Session) Shown(SetupScreen screen, double textScale)
    {
        var rig = new WindowRig(SystemLook.Default with { TextScale = textScale });
        var session = rig.Session(screen);
        session.Start();
        rig.Window.Show();
        rig.Settle();
        rig.Run(1.5);
        return (rig, session);
    }
}
