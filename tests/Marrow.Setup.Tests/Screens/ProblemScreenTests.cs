using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Marrow.Setup.Flow;
using Marrow.Setup.Platform;
using Marrow.Setup.Screens;
using Marrow.Setup.Tests.Fixtures;
using Marrow.Setup.Text;
using Marrow.Setup.Ui;

namespace Marrow.Setup.Tests.Screens;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 7 and 8a, "Problem", unit U2 (ED-NEW-inst-u2-1). Problem says what went
// wrong in one sentence, then one of its two endings (nothing was changed, or files may be left and Retry repairs them);
// Retry is the default and is not offered for a changed copy of the setup; Copy details copies the header line (setup
// version, Windows, build, architecture) and the details, and cross-fades to "Copied" for 1.6 s without changing width;
// Show details fades the 104 DIP log in and out without moving the buttons.
[Collection("SetupHeadless")]
public sealed class ProblemScreenTests(SetupHeadlessSession headless)
{
    public static TheoryData<string, bool> Reasons => new()
    {
        { SetupStrings.ProblemSpace("C:", 420, 112), true },
        { SetupStrings.ProblemInUse, true },
        { SetupStrings.ProblemBlocked, true },
        { SetupStrings.ProblemOther, true },
        { SetupStrings.ProblemChanged, false },
    };

    [Theory]
    [MemberData(nameof(Reasons))]
    [Trait("Decision", "OD-0072")]
    public void Each_reason_is_said_once_with_Retry_except_for_a_changed_copy(string reason, bool retry)
    {
        headless.OnUiThread(() =>
        {
            var (rig, session, problem) = Problem(reason, safe: true);

            Assert.Equal("nth couldn’t be installed", Title(problem).Text);
            Assert.Equal(reason, problem.Reason.Text);
            Assert.Equal("Nothing was changed on your PC.", problem.Ending.Text);
            Assert.Equal(retry, problem.Retry.IsVisible);
            Assert.True(problem.Copy.IsVisible);
            Assert.Equal("Show details", problem.DetailsLink.Words);
            Assert.False(problem.Log.IsVisible);
            Assert.Equal(1, session.ExitCode);
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void The_space_reason_reads_word_for_word()
    {
        Assert.Equal("There isn’t enough free space on drive C:. nth needs about 420 MB, and 112 MB is free.", SetupStrings.ProblemSpace("C:", 420, 112));
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void A_failure_that_left_files_says_Retry_repairs_them()
    {
        headless.OnUiThread(() =>
        {
            var (rig, _, problem) = Problem(SetupStrings.ProblemOther, safe: false);

            Assert.Equal("Some of nth’s files may be left. Retry repairs them.", problem.Ending.Text);
            Assert.True(problem.Retry.IsVisible);
            Assert.Equal(ButtonKind.Primary, problem.Retry.Kind);
            Assert.Equal(ButtonKind.Secondary, problem.Copy.Kind);
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Copy_details_copies_the_header_and_the_details_and_says_Copied_for_1_6_s()
    {
        headless.OnUiThread(() =>
        {
            var (rig, session, problem) = Problem(SetupStrings.ProblemInUse, safe: true, "step: copying · Setup.exe exit 1");
            var look = rig.Window.Look;
            var header = SetupStrings.ProblemHeader("1.1.0", look.WindowsBuild >= 22000 ? "11" : "10", look.WindowsBuild, RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant());
            Assert.StartsWith("nth Setup 1.1.0 · Windows ", header, StringComparison.Ordinal);
            var width = problem.Copy.Bounds.Width;

            problem.Copy.Invoke();
            rig.Settle();
            Assert.Equal(header + "\nstep: copying · Setup.exe exit 1", Assert.Single(rig.Actions.Copied));
            Assert.Equal(SetupStrings.ProblemCopied, problem.Copy.Label);
            var last = problem.Copy.LabelOpacity;
            for (var i = 0; i < 60; i++)
            {
                rig.Run(1.0 / 60);
                Assert.InRange(Math.Abs(problem.Copy.LabelOpacity - last), 0, .35);
                last = problem.Copy.LabelOpacity;
                Assert.Equal(width, problem.Copy.Bounds.Width, 3);
            }
            Assert.Equal(SetupStrings.ProblemCopied, problem.Copy.Label);
            rig.Run(.7);
            Assert.Equal(SetupStrings.ProblemCopy, problem.Copy.Label);
            Assert.Equal(width, problem.Copy.Bounds.Width, 3);
            Assert.Equal(SetupScreen.Problem, session.Screen);
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void A_copy_that_fails_keeps_its_words()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            rig.Actions.CopySucceeds = false;
            var session = rig.Session(SetupScreen.Problem);
            session.Fail(SetupStrings.ProblemOther);
            session.Start();
            rig.Window.Show();
            rig.Settle();
            var problem = Assert.IsType<ProblemScreen>(session.Current);

            problem.Copy.Invoke();
            rig.Run(.5);
            Assert.Equal(SetupStrings.ProblemCopy, problem.Copy.Label);
            rig.Window.CloseNow();
        });
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(2.25)]
    [Trait("Decision", "OD-0072")]
    public void Show_details_fades_the_log_in_and_out_and_the_buttons_never_move(double textScale)
    {
        headless.OnUiThread(() =>
        {
            var (rig, _, problem) = Problem(SetupStrings.ProblemInUse, safe: true, "[12:04:31] Velopack Setup 1.2.161\n[12:04:33] copying files\n[12:04:34] error: access denied", SystemLook.Default with { TextScale = textScale });
            var probe = new FrameProbe(rig, problem.View, [problem.Reason, problem.Ending, problem.Log, problem.DetailsLink], [problem.Copy, problem.Retry]);
            var height = rig.Window.Bounds.Height;

            problem.DetailsLink.Invoke();
            probe.Run(.8);
            Assert.True(problem.Log.IsVisible);
            Assert.Equal(1, probe.Of(problem.Log).Opacity, 3);
            Assert.Equal(LogBox.FullHeight, problem.Log.Bounds.Height, 3);
            Assert.Equal("Hide details", problem.DetailsLink.Words);
            Assert.Contains("error: access denied", problem.Log.Text.Text, StringComparison.Ordinal);
            Assert.True(probe.Of(problem.Log).Box.Bottom <= probe.Of(problem.Copy).Box.Top - CanvasScreen.ButtonGap + .5, "the log reaches into the gap above the buttons");

            problem.DetailsLink.Invoke();
            probe.Run(.1);
            problem.DetailsLink.Invoke();
            probe.Run(.1);
            problem.DetailsLink.Invoke();
            probe.Run(.8);
            Assert.False(problem.Log.IsVisible);
            Assert.Equal("Show details", problem.DetailsLink.Words);
            Assert.Equal(height, rig.Window.Bounds.Height);
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Enter_retries_Esc_closes_and_the_mark_is_dimmed_with_its_badge()
    {
        headless.OnUiThread(() =>
        {
            var (rig, session, problem) = Problem(SetupStrings.ProblemOther, safe: true);
            var mark = problem.Column.Children.OfType<Panel>().SelectMany(p => p.Children).OfType<SetupMark>().Single();
            Assert.True(mark.Dim);
            Assert.Single(problem.Column.Children.OfType<Panel>().SelectMany(p => p.Children).OfType<MarkBadge>());
            Assert.Same(problem.Retry, problem.DefaultButton);

            Assert.True(problem.OnKey(Key.Escape));
            rig.Settle();
            Assert.False(rig.Window.IsVisible);
            Assert.False(session.InstallChosen);
        });
    }

    private static (WindowRig Rig, SetupSession Session, ProblemScreen Problem) Problem(string reason, bool safe, string? details = null, SystemLook? look = null)
    {
        var rig = new WindowRig(look);
        var session = rig.Session(SetupScreen.Problem);
        session.Fail(reason, safe, details);
        session.Start();
        rig.Window.Show();
        rig.Settle();
        rig.Run(1);
        return (rig, session, Assert.IsType<ProblemScreen>(session.Current));
    }

    private static TextBlock Title(ProblemScreen problem) => problem.Column.Children.OfType<TextBlock>().First();
}
