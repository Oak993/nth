using Marrow.Core.Support;
using Marrow.Setup.Flow;
using Marrow.Setup.Screens;
using Marrow.Setup.Tests.Fixtures;
using Marrow.Setup.Text;
using Marrow.Setup.Ui;

namespace Marrow.Setup.Tests.Screens;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 8a, "Ready", unit U2 (ED-NEW-inst-u2-1). Ready shows what the hook
// reported in install-result.json, never the choice: each outcome's sentence and second line, the desktop moment only
// for created or kept with the icons shown, "nth is updated" with no moment, the moment one thing at a time (the desktop
// at 1.35 s, the icon at 1.75 s, the drift at 3.1 s from mid-path), Calm without waits, and Open nth closing 1.4 s later.
[Collection("SetupHeadless")]
public sealed class ReadyScreenTests(SetupHeadlessSession headless)
{
    public static TheoryData<string, string, string?, bool> Outcomes => new()
    {
        { "created", "Find it in Start and on your desktop.", null, true },
        { "kept", "Find it in Start and on your desktop.", null, true },
        { "off", "Find it in Start.", null, false },
        { "name-taken", "Find it in Start.", "Your desktop already has a different “nth” shortcut, so none was added.", false },
        { "failed", "Find it in Start.", "Windows didn’t allow a desktop shortcut. You can add one later in nth’s Settings.", false },
        { "hidden", "Find it in Start and on your desktop.", "Your desktop icons are hidden right now, so you won’t see it there until you show them again.", false },
        { "missing", "Find it in Start.", "Windows didn’t allow a desktop shortcut. You can add one later in nth’s Settings.", false },
    };

    [Theory]
    [MemberData(nameof(Outcomes))]
    [Trait("Decision", "OD-0072")]
    public void Each_outcome_has_its_sentence_its_second_line_and_the_moment_only_when_the_icon_is_there(string outcome, string sentence, string? note, bool moment)
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var session = rig.Session(SetupScreen.Ready);
            if (Result(outcome) is { } result) session.SetResult(result, updated: false);
            session.Start();
            rig.Window.Show();
            rig.Settle();
            var ready = Assert.IsType<ReadyScreen>(session.Current);

            Assert.Equal("nth is ready", ready.TitleBlock.Text);
            Assert.Equal(sentence, ready.Sentence.Text);
            Assert.Equal(note is not null, ready.Note.IsVisible);
            if (note is not null) Assert.Equal(note, ready.Note.Text);
            Assert.Equal(moment, ready.HasMoment);
            Assert.Equal(moment, ready.DeskSlot.IsVisible);
            rig.Run(3.4);
            Assert.Equal(moment ? DeskState.Land : DeskState.Off, ready.Desk.State);
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void After_an_update_it_says_nth_is_updated_with_no_moment()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var session = rig.Session(SetupScreen.Ready);
            session.SetResult(new InstallResult(DesktopShortcutOutcome.Created), updated: true);
            session.Start();
            var ready = Assert.IsType<ReadyScreen>(session.Current);

            Assert.Equal("nth is updated", ready.TitleBlock.Text);
            Assert.Equal("You’re on 1.1.0. Your chats and settings are as you left them.", ready.Sentence.Text);
            Assert.False(ready.HasMoment);
            Assert.False(ready.DeskSlot.IsVisible);
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void The_moment_comes_one_thing_at_a_time_and_the_drift_starts_mid_path()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var session = rig.Session(SetupScreen.Ready);
            session.SetResult(new InstallResult(DesktopShortcutOutcome.Created), updated: false);
            session.Start();
            rig.Window.Show();
            rig.Settle();
            var ready = Assert.IsType<ReadyScreen>(session.Current);

            rig.Run(1.30);
            Assert.Equal(0, ready.Desk.Appearance.Opacity);
            Assert.False(ready.MarkView.IsDrifting);
            rig.Run(0.10);
            Assert.InRange(ready.Desk.Appearance.Opacity, 0.001, 0.5);
            Assert.Equal(DeskState.Off, ready.Desk.State);
            rig.Run(0.40);
            Assert.Equal(DeskState.Land, ready.Desk.State);
            Assert.True(ready.Desk.IsGlowing);
            rig.Run(1.25);
            Assert.False(ready.MarkView.IsDrifting);
            rig.Run(0.10);
            Assert.True(ready.MarkView.IsDrifting);
            // mid-path: the silver is where it rested, so it never jumps
            Assert.InRange(ready.MarkView.Pose.DriftX, -0.004, 0.004);
            Assert.InRange(ready.MarkView.Pose.DriftY, -0.004, 0.004);
            Assert.Equal((0.0, 1.0, 1.0, 1.0), (Math.Round(ready.Desk.IconPose.Y, 3), Math.Round(ready.Desk.IconPose.Scale, 3), Math.Round(ready.Desk.IconPose.Opacity, 3), Math.Round(ready.Desk.IconPose.Label, 3)));
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Calm_shows_the_desktop_at_once_lands_at_80_ms_and_never_drifts()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig(WindowRig.Calm);
            var session = rig.Session(SetupScreen.Ready);
            session.SetResult(new InstallResult(DesktopShortcutOutcome.Created), updated: false);
            session.Start();
            rig.Window.Show();
            rig.Settle();
            var ready = Assert.IsType<ReadyScreen>(session.Current);

            rig.Run(0.05);
            Assert.True(ready.Desk.Appearance.Opacity > 0);
            Assert.Equal(DeskState.Off, ready.Desk.State);
            rig.Run(0.05);
            Assert.Equal(DeskState.Land, ready.Desk.State);
            var y = new List<double>();
            for (var i = 0; i < 40; i++)
            {
                rig.Run(1.0 / 60);
                y.Add(ready.Desk.IconPose.Y);
            }
            Assert.All(y, v => Assert.Equal(0, v));
            Assert.False(ready.Desk.IsGlowing);
            rig.Run(4);
            Assert.False(ready.MarkView.IsDrifting);
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void A_result_arriving_while_shown_fades_the_desktop_in_with_its_place_never_an_empty_gap()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var session = rig.Session(SetupScreen.Ready);
            session.SetResult(new InstallResult(DesktopShortcutOutcome.Off), updated: false);
            session.Start();
            rig.Window.Show();
            rig.Settle();
            rig.Run(1);
            var ready = Assert.IsType<ReadyScreen>(session.Current);

            session.SetResult(new InstallResult(DesktopShortcutOutcome.Created), updated: false);
            for (var i = 0; i < 90; i++)
            {
                rig.Run(1.0 / 60);
                var slot = ready.Reveal.Pose(ready.DeskSlot).Opacity;
                if (ready.DeskSlot.IsVisible && slot > 0.02) Assert.Equal(1, ready.Desk.Appearance.Opacity);
            }
            Assert.Equal("Find it in Start and on your desktop.", ready.Sentence.Text);
            Assert.True(ready.HasMoment);
            rig.Run(1);
            Assert.Equal(DeskState.Land, ready.Desk.State);
            rig.Window.CloseNow();
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Open_nth_cross_fades_to_Opening_and_closes_the_window_1_4_s_later_with_exit_code_0()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            var session = rig.Session(SetupScreen.Ready);
            session.SetResult(new InstallResult(DesktopShortcutOutcome.Created), updated: false);
            session.Start();
            rig.Window.Show();
            rig.Settle();
            var ready = Assert.IsType<ReadyScreen>(session.Current);
            var width = ready.Open.Bounds.Width;

            ready.Open.Invoke();
            Assert.Equal([WindowRig.Root], rig.Actions.Opened);
            var last = ready.Open.LabelOpacity;
            for (var i = 0; i < 60; i++)
            {
                rig.Run(1.0 / 60);
                Assert.InRange(Math.Abs(ready.Open.LabelOpacity - last), 0, 0.35);
                last = ready.Open.LabelOpacity;
                Assert.Equal(width, ready.Open.Bounds.Width, 3);
            }
            Assert.Equal(SetupStrings.ReadyOpening, ready.Open.Label);
            Assert.True(rig.Window.IsVisible);
            rig.Run(0.5);
            Assert.False(rig.Window.IsVisible);
            Assert.Equal(0, session.ExitCode);
        });
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void Open_nth_that_cannot_start_keeps_the_window_and_its_words()
    {
        headless.OnUiThread(() =>
        {
            var rig = new WindowRig();
            rig.Actions.OpenSucceeds = false;
            var session = rig.Session(SetupScreen.NewerInstalled, installed: "1.2.0");
            session.Start();
            rig.Window.Show();
            rig.Settle();
            var screen = Assert.IsType<CurrentScreen>(session.Current);

            screen.Open.Invoke();
            rig.Run(2);
            Assert.True(rig.Window.IsVisible);
            Assert.Equal(SetupStrings.ReadyOpen, screen.Open.Label);
            rig.Window.CloseNow();
            Assert.Equal(1, session.ExitCode);
        });
    }

    private static InstallResult? Result(string outcome) => outcome switch
    {
        "created" => new InstallResult(DesktopShortcutOutcome.Created),
        "kept" => new InstallResult(DesktopShortcutOutcome.Kept),
        "off" => new InstallResult(DesktopShortcutOutcome.Off),
        "name-taken" => new InstallResult(DesktopShortcutOutcome.NameTaken),
        "failed" => InstallResult.FailedWith(unchecked((int)0x80070005)),
        "hidden" => new InstallResult(DesktopShortcutOutcome.Created, IconsHidden: true),
        _ => null,
    };
}
